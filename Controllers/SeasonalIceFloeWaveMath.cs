using System;
using UnityEngine;

namespace Seasons
{
    // Immutable inputs and arithmetic only. Construct snapshots on the main thread.
    // CreateWave/TrochSin are trusted unpatched managed math by the maintainer's contract.
    internal static class FloeWaveMath
    {
        internal const float DerivativeStep = 0.05f;
        internal delegate float WaveFunction(Vector3 point, float time, float speed, float length,
            float height, Vector2 direction, Vector2 tangent, float sharpness);

        internal readonly struct Wind
        {
            internal readonly Vector2 Direction, Tangent;
            internal readonly float Intensity;
            internal Wind(Vector4 value)
            {
                Direction = new Vector2(value.x, value.z).normalized;
                Tangent = new Vector2(-Direction.y, Direction.x);
                Intensity = value.w;
            }
            private Wind(Wind direction, float intensity)
            { Direction = direction.Direction; Tangent = direction.Tangent; Intensity = intensity; }

            internal static Wind Prepare(Vector4 value, Vector4 previous, Wind prepared) =>
                value.x == previous.x && value.z == previous.z
                    ? new Wind(prepared, value.w) : new Wind(value);
        }

        internal sealed class Spectrum
        {
            private readonly Vector4[] parameters;
            private readonly Vector2[] directions, tangents;
            private readonly WaveFunction create;

            // Clone once per world, not per floe, job, or sample. Element zero is NEVER
            // read from the game arrays: native CalcWave writes it on the main thread.
            internal Spectrum(WaveFunction create, Vector4[] parameters, Vector2[] directions)
            {
                this.create = create;
                this.parameters = (Vector4[])parameters.Clone();
                this.directions = (Vector2[])directions.Clone();
                tangents = new Vector2[directions.Length];
                for (int i = 1; i < directions.Length; i++)
                    tangents[i] = new Vector2(-directions[i].y, directions[i].x);
            }

            internal float Sum(Vector3 point, float phase, Wind wind, float big, float secondaryWeight, bool full)
            {
                float result = 0f;
                int count = full ? 10 : secondaryWeight > 0f ? 5 : 1;
                for (int i = 0; i < count; i++)
                {
                    Vector4 p = parameters[i];
                    float height = p.z * (i < 6 ? big : 1f);
                    if (!full && i > 0)
                        height *= secondaryWeight;
                    result += create(point, phase / 20f, p.x, p.y, height,
                        i == 0 ? wind.Direction : directions[i],
                        i == 0 ? wind.Tangent : tangents[i], p.w);
                }
                // Normalized Ocean depth is always 1. No scene objects or Depth calls.
                return result * wind.Intensity;
            }
        }

        internal sealed class Snapshot
        {
            internal readonly long Revision;
            internal readonly Spectrum Spectrum;
            internal readonly Wind Tilt, First, Second;
            internal readonly Vector4 RawTilt, RawFirst, RawSecond;
            internal readonly Vector3 Along, Across;
            internal readonly float Blend;

            internal Snapshot(long revision, Spectrum spectrum, Vector4 tilt, Vector4 first,
                Vector4 second, float blend, Snapshot previous)
            {
                Revision = revision;
                Spectrum = spectrum;
                RawTilt = tilt;
                RawFirst = first;
                RawSecond = second;
                // Do not normalize unchanged states merely because alpha changed.
                Tilt = previous == null ? new Wind(tilt) : Wind.Prepare(tilt, previous.RawTilt, previous.Tilt);
                First = previous == null ? new Wind(first) : Wind.Prepare(first, previous.RawFirst, previous.First);
                Second = previous == null ? new Wind(second) : Wind.Prepare(second, previous.RawSecond, previous.Second);
                Blend = blend;
                Along = new Vector3(Tilt.Direction.x, 0f, Tilt.Direction.y);
                if (Along.sqrMagnitude < 0.000001f)
                    Along = Vector3.forward;
                Across = Vector3.Cross(Along, Vector3.up);
            }

            internal bool Matches(Vector4 tilt, Vector4 first, Vector4 second, float blend) =>
                RawTilt.Equals(tilt) && RawFirst.Equals(first) && RawSecond.Equals(second) && Blend == blend;
        }

        internal sealed class Input
        {
            internal readonly Snapshot Wind;
            internal readonly Vector3 Origin;
            internal readonly Vector2 Radii;
            internal readonly double TimeOrigin;
            internal readonly float PhaseOrigin, Spacing, SecondaryWeight;
            internal readonly bool FullHeight, Waves;
            private readonly Vector3[] points;
            private readonly float[] largeWaveFactors, baseHeights;

            internal Input(Snapshot wind, Vector3 origin, Vector2 radii, double time, float phase,
                float spacing, float weight, bool fullHeight, bool waves, Vector3[] points,
                float[] largeWaveFactors, float[] baseHeights)
            {
                Wind = wind;
                Origin = origin;
                Radii = radii;
                TimeOrigin = time;
                PhaseOrigin = phase;
                Spacing = spacing;
                SecondaryWeight = weight;
                FullHeight = fullHeight;
                Waves = waves;
                // These private arrays are freshly prepared for this input and never mutated.
                this.points = points;
                this.largeWaveFactors = largeWaveFactors;
                this.baseHeights = baseHeights;
            }

            internal double PhaseEnd => TimeOrigin + (86400.0 - PhaseOrigin) - DerivativeStep - 0.002;

            private float Height(int point, float phase, bool full)
            {
                if (!Waves)
                    return baseHeights[point];
                Snapshot s = Wind;
                float wave;
                if (!full)
                    wave = s.Spectrum.Sum(points[point], phase, s.Tilt, largeWaveFactors[point], SecondaryWeight, false);
                else
                {
                    float a = s.Spectrum.Sum(points[point], phase, s.First, largeWaveFactors[point], 1f, true);
                    wave = s.Blend == 0f ? a : a + (s.Spectrum.Sum(points[point], phase, s.Second,
                        largeWaveFactors[point], 1f, true) - a) * s.Blend;
                }
                return baseHeights[point] + wave;
            }

            internal Sample Evaluate(double time)
            {
                float phase = (PhaseOrigin + (float)(time - TimeOrigin)) % 86400f;
                float next = (phase + DerivativeStep) % 86400f;
                Sample sample = default;
                for (int i = 0; i < 4; i++)
                {
                    float h = Height(i, phase, false);
                    sample.Heights[i] = h;
                    sample.Rates[i] = (Height(i, next, false) - h) / DerivativeStep;
                }
                sample.Height = FullHeight ? Height(4, phase, true) : Mean(sample.Heights);
                sample.Rate = FullHeight ? (Height(4, next, true) - sample.Height) / DerivativeStep : Mean(sample.Rates);
                if (!sample.IsFinite)
                    throw new InvalidOperationException("Non-finite floe wave sample.");
                return sample;
            }
        }

        internal struct Sample
        {
            internal Vector4 Heights, Rates;
            internal float Height, Rate;
            internal bool IsFinite => Finite(Height) && Finite(Rate) && Finite(Heights) && Finite(Rates);
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector4 v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w);
        internal static float Mean(Vector4 v) => (v.x + v.y + v.z + v.w) * 0.25f;

        internal static Sample Interpolate(Sample a, Sample b, float t, float duration)
        {
            float t2 = t * t, t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f, h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2, h11 = t3 - t2;
            float d00 = (6f * t2 - 6f * t) / duration, d10 = 3f * t2 - 4f * t + 1f;
            float d01 = -d00, d11 = 3f * t2 - 2f * t;
            return new Sample
            {
                Heights = a.Heights * h00 + a.Rates * (h10 * duration) + b.Heights * h01 + b.Rates * (h11 * duration),
                Rates = a.Heights * d00 + a.Rates * d10 + b.Heights * d01 + b.Rates * d11,
                Height = a.Height * h00 + a.Rate * (h10 * duration) + b.Height * h01 + b.Rate * (h11 * duration),
                Rate = a.Height * d00 + a.Rate * d10 + b.Height * d01 + b.Rate * d11
            };
        }

        internal static Sample Reframe(Sample source, Input oldInput, Input newInput)
        {
            Vector3 gradient = Gradient(source.Heights, oldInput);
            Vector3 rateGradient = Gradient(source.Rates, oldInput);
            Vector4 heights = default, rates = default;
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = i < 2 ? newInput.Wind.Along * (i == 0 ? newInput.Radii.x : -newInput.Radii.x) :
                    newInput.Wind.Across * (i == 2 ? newInput.Radii.y : -newInput.Radii.y);
                heights[i] = Mean(source.Heights) + Vector3.Dot(gradient, offset);
                rates[i] = Mean(source.Rates) + Vector3.Dot(rateGradient, offset);
            }
            source.Heights = heights;
            source.Rates = rates;
            return source;
        }

        private static Vector3 Gradient(Vector4 v, Input input) =>
            input.Wind.Along * ((v.x - v.y) / (2f * input.Radii.x)) +
            input.Wind.Across * ((v.z - v.w) / (2f * input.Radii.y));
    }
}
