using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Seasons
{
    internal static partial class SeasonalIceFloeWaves
    {
        private static FloeWaveMath.Spectrum backgroundSpectrum;
        private static FloeWaveMath.Snapshot backgroundWind;
        private static int backgroundFrame = -1;
        private static long backgroundRevision;
        private static double backgroundNow, windChangedAt, previousBackgroundTime = -1;
        private static float previousBackgroundPhase;
        private static bool backgroundReady;
        internal static long BackgroundWindRevision => backgroundWind?.Revision ?? 0;

        internal static bool TryBackgroundSnapshot(out FloeWaveMath.Snapshot wind, out double now, out bool stable)
        {
            wind = null;
            now = 0;
            stable = false;
            if (!Snapshot()) return false;
            if (backgroundFrame != Time.frameCount)
            {
                backgroundFrame = Time.frameCount;
                backgroundReady = false;
                backgroundNow = ZNet.instance.GetTimeSeconds();
                if (double.IsNaN(backgroundNow) || double.IsInfinity(backgroundNow) || backgroundNow < 0)
                    return false;
                double advance = backgroundNow - previousBackgroundTime;
                float expected = (previousBackgroundPhase + (float)advance) % 86400f;
                if (previousBackgroundTime >= 0 && (advance < -0.01 || advance > 2.0 ||
                    Mathf.Abs(WaveTime - expected) > 0.1f || WaveTime < previousBackgroundPhase - 0.1f))
                    FloeForecastWorker.ResetWorld(); // Includes the native phase discontinuity; no interpolated seam.
                previousBackgroundTime = backgroundNow;
                previousBackgroundPhase = WaveTime;
                if (backgroundSpectrum == null)
                    backgroundSpectrum = new FloeWaveMath.Spectrum(oceanPrefab.CreateWave, waves, WaterVolume.s_createWaveDirections);
                if (backgroundWind == null || !backgroundWind.Matches(effectiveWind, WaterWind1, WaterWind2, WaterWindBlend))
                {
                    backgroundWind = new FloeWaveMath.Snapshot(++backgroundRevision, backgroundSpectrum,
                        effectiveWind, WaterWind1, WaterWind2, WaterWindBlend, backgroundWind);
                    windChangedAt = backgroundNow;
                }
                backgroundReady = true;
            }
            if (!backgroundReady) return false;
            wind = backgroundWind;
            now = backgroundNow;
            stable = now - windChangedAt >= 2.0;
            return wind != null;
        }

        internal static void ResetBackgroundWorld()
        {
            FloeForecastWorker.ResetWorld();
            backgroundWind = null;
            backgroundSpectrum = null;
            backgroundFrame = -1;
            previousBackgroundTime = -1;
            backgroundReady = false;
        }

        internal static FloeWaveMath.Input CaptureBackgroundInput(FloeWaveMath.Snapshot wind, Vector3 origin,
            Vector2 radii, double time, float spacing, float weight, bool fullHeight, SurfaceContext context)
        {
            Vector3[] points = new Vector3[5];
            float[] big = new float[5], baseline = new float[5];
            // Kinematic hull center X/Z is fixed. World/biome inputs are sampled once for
            // this spatial/wind revision, NEVER by the worker or once per future knot.
            origin.y = 0f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 4 ? Vector3.zero : i < 2 ? wind.Along * (i == 0 ? radii.x : -radii.x) :
                    wind.Across * (i == 2 ? radii.y : -radii.y);
                points[i] = origin + offset;
                big[i] = 1f - (float)WorldGenerator.DeepNorthWaveFade(points[i].x, points[i].z);
                baseline[i] = context.WaterLevel;
                if (context.HasWorldEdge && Utils.LengthXZ(points[i]) > 10500f)
                    baseline[i] -= 100f;
            }
            return new FloeWaveMath.Input(wind, origin, radii, time, previousBackgroundPhase, spacing, weight,
                fullHeight, context.UseWaves, points, big, baseline);
        }
    }

    public partial class IceFloeClimb
    {
        [Header("Background kinematic forecasts (shared peer-local settings)")]
        public static bool EnableBackgroundWaveForecast = true;
        public static float BackgroundForecastSeconds = 10f;

        public float BackgroundReserveSeconds, BackgroundQueueMilliseconds, BackgroundWorkMilliseconds;
        public long BackgroundAcceptedBlocks, BackgroundDiscardedBlocks, BackgroundWaitFrames;
        public long BackgroundWindAdoptions, BackgroundResets;
        public string BackgroundStatus = "Not active";
        private bool backgroundPending;
        private OceanCurve oceanCurve, spareOceanCurve, ticketCurve;
        private FloeForecastWorker.Ticket oceanTicket;
        private uint oceanGeometry;
        private long oceanLease;
        private ushort oceanNativeRevision;
        private ZDOID oceanId;
        private bool oceanInputsValid, oceanFull, oceanScaleProbes, oceanWaves;
        private float oceanProbe, oceanWeight, oceanSpacing, oceanLevel;
        private double nextWindRefresh, retryBackgroundAt;
        private static double nextBackgroundErrorLog;

        // Only cumulative worker counters are reset; curves, jobs and motion are untouched.
        public static void ResetBackgroundForecastCounters() => FloeForecastWorker.ResetCounters();

        private bool UsingBackgroundForecast => EnableBackgroundWaveForecast && EnableWavePrediction &&
            OwnerlessKinematic && FallbackSimulator && !Distant;

        // The main thread alone owns these rings. Two rings are reused on wind replacement;
        // queued/running work holds immutable Input and separate output arrays, not this ring.
        private sealed class OceanCurve
        {
            private struct Node
            {
                internal double Time;
                internal FloeWaveMath.Sample Value;
            }
            private readonly Node[] nodes = new Node[128];
            private int head;
            internal int Count { get; private set; }
            internal FloeWaveMath.Input Input;
            internal int Epoch;
            private FloeWaveMath.Sample correction;
            private double correctionStart;
            private float correctionDuration;
            internal double End => Count == 0 ? (Input?.TimeOrigin ?? 0.0) : At(Count - 1).Time;
            private Node At(int i) => nodes[(head + i) % nodes.Length];
            internal FloeWaveMath.Sample Last => At(Count - 1).Value;

            internal void Reset(FloeWaveMath.Input input, int epoch)
            {
                Input = input;
                Epoch = epoch;
                Count = head = 0;
                correctionDuration = 0;
            }

            internal void Clear()
            {
                Input = null;
                Count = head = 0;
                correctionDuration = 0;
            }

            internal void Trim(double now)
            {
                while (Count > 2 && At(1).Time <= now)
                {
                    head = (head + 1) % nodes.Length;
                    Count--;
                }
            }

            internal bool Append(FloeForecastWorker.Ticket ticket, FloeForecastWorker.Result result)
            {
                if (!ReferenceEquals(ticket.Input, Input) || result.Samples == null || result.Samples.Length != ticket.Count)
                    return false;
                int first = Count == 0 ? 0 : 1;
                if ((Count != 0 && Math.Abs(End - ticket.Start) > 0.00001) || Count + ticket.Count - first > nodes.Length)
                    return false;
                for (int i = first; i < ticket.Count; i++)
                {
                    nodes[(head + Count) % nodes.Length] = new Node
                    { Time = ticket.Start + ticket.Step * i, Value = result.Samples[i] };
                    Count++;
                }
                return true;
            }

            internal bool Evaluate(double time, out FloeWaveMath.Sample value)
            {
                value = default;
                if (Count < 2 || time < At(0).Time - 0.00001 || time > End + 0.00001)
                    return false;
                // Usually the first segment; at most a few extra nodes for the derivative.
                int i = 0;
                while (i < Count - 2 && At(i + 1).Time < time) i++;
                Node a = At(i), b = At(i + 1);
                float duration = (float)(b.Time - a.Time);
                float t = Mathf.Clamp01((float)((time - a.Time) / (b.Time - a.Time)));
                value = FloeWaveMath.Interpolate(a.Value, b.Value, t, duration);
                if (correctionDuration > 0f && time < correctionStart + correctionDuration)
                {
                    float elapsed = (float)(time - correctionStart);
                    FloeWaveMath.Sample tail = FloeWaveMath.Interpolate(correction, default,
                        Mathf.Clamp01(elapsed / correctionDuration), correctionDuration);
                    value.Heights += tail.Heights;
                    value.Rates += tail.Rates;
                    value.Height += tail.Height;
                    value.Rate += tail.Rate;
                }
                return value.IsFinite;
            }

            internal void ContinueFrom(OceanCurve previous, double now, float duration)
            {
                if (!previous.Evaluate(now, out FloeWaveMath.Sample old) || !Evaluate(now, out FloeWaveMath.Sample next))
                    return;
                old = FloeWaveMath.Reframe(old, previous.Input, Input);
                correction = new FloeWaveMath.Sample
                {
                    Heights = old.Heights - next.Heights, Rates = old.Rates - next.Rates,
                    Height = old.Height - next.Height, Rate = old.Rate - next.Rate
                };
                correctionStart = now;
                correctionDuration = duration;
            }
        }

        private void ReleaseBackgroundForecast()
        {
            FloeForecastWorker.Cancel(oceanTicket);
            oceanTicket = null;
            ticketCurve = null;
            oceanInputsValid = false;
            backgroundPending = false;
            BackgroundReserveSeconds = 0;
            BackgroundStatus = "Not active";
            oceanCurve?.Clear();
            spareOceanCurve?.Clear();
            // Retain only numeric ring storage; release the captured delegate/world inputs.
            // No running task can mutate these rings or apply a pose to this instance.
        }

        private void ResetOceanInputs(FloeWaveMath.Snapshot wind, double now, SurfaceSettings settings,
            HullGeometry hull, SeasonalIceFloeWaves.SurfaceContext context, float spacing)
        {
            ReleaseBackgroundForecast();
            InvalidatePrediction("Background forecast inputs changed");
            oceanCurve ??= new OceanCurve();
            spareOceanCurve ??= new OceanCurve();
            Vector2 radii = ProbeRadii(settings, wind.Along, wind.Across);
            FloeWaveMath.Input input = SeasonalIceFloeWaves.CaptureBackgroundInput(wind, hull.Center, radii, now,
                spacing, settings.SecondarySwellWeight, UseFullWaterHeight, context);
            oceanCurve.Reset(input, FloeForecastWorker.Epoch);
            oceanInputsValid = true;
            oceanGeometry = hullGeometryRevision;
            ZDO zdo = m_view.GetZDO();
            oceanId = zdo.m_uid;
            oceanNativeRevision = zdo.OwnerRevision;
            oceanLease = PhysicsAuthorityToken;
            oceanFull = UseFullWaterHeight;
            oceanScaleProbes = settings.ScaleProbes;
            oceanProbe = settings.ProbeDistance;
            oceanWeight = settings.SecondarySwellWeight;
            oceanSpacing = spacing;
            oceanLevel = context.WaterLevel;
            oceanWaves = context.UseWaves;
            nextWindRefresh = now + WindRefreshDelay;
            retryBackgroundAt = now;
            BackgroundResets++;
        }

        private float WindRefreshDelay => Mathf.Max(0.5f, PredictionSeconds) * (1f + (float)electionPhase * 0.1f);

        private bool OceanInputsChanged(SurfaceSettings settings, HullGeometry hull,
            SeasonalIceFloeWaves.SurfaceContext context, float spacing)
        {
            if (!oceanInputsValid || oceanCurve.Epoch != FloeForecastWorker.Epoch ||
                oceanGeometry != hullGeometryRevision || oceanLease != PhysicsAuthorityToken ||
                oceanId != m_view.GetZDO().m_uid || oceanNativeRevision != m_view.GetZDO().OwnerRevision ||
                oceanFull != UseFullWaterHeight || oceanScaleProbes != settings.ScaleProbes ||
                oceanProbe != settings.ProbeDistance || oceanWeight != settings.SecondarySwellWeight ||
                oceanSpacing != spacing || oceanLevel != context.WaterLevel || oceanWaves != context.UseWaves)
                return true;
            Vector3 delta = hull.Center - oceanCurve.Input.Origin;
            delta.y = 0;
            float tolerance = Setting(PredictionPositionTolerance, 0.5f, 0.1f, 2f);
            if (delta.sqrMagnitude > tolerance * tolerance) return true;
            return settings.ScaleProbes && (ProbeRadii(settings, oceanCurve.Input.Wind.Along,
                oceanCurve.Input.Wind.Across) - oceanCurve.Input.Radii).sqrMagnitude >= 0.0001f;
        }

        private void ReceiveOceanBlock(double now)
        {
            if (oceanTicket == null) return;
            FloeForecastWorker.Result result = oceanTicket.TakeResult();
            if (result == null) return;
            FloeForecastWorker.Ticket ticket = oceanTicket;
            OceanCurve target = ticketCurve;
            oceanTicket = null;
            ticketCurve = null;
            BackgroundQueueMilliseconds = (float)result.QueueMilliseconds;
            BackgroundWorkMilliseconds = (float)result.WorkMilliseconds;
            if (result.Error != null)
            {
                BackgroundDiscardedBlocks++;
                BackgroundStatus = "Worker error: " + result.Error;
                retryBackgroundAt = now + 1.0;
                if (now >= nextBackgroundErrorLog)
                {
                    nextBackgroundErrorLog = now + 5;
                    Seasons.LogWarning("Floe background forecast failed: " + result.Error);
                }
                return;
            }
            target.Trim(now);
            if (ticket.Epoch != FloeForecastWorker.Epoch || ticket.End < now + SurfaceDerivativeStep ||
                !target.Append(ticket, result))
            {
                BackgroundDiscardedBlocks++;
                BackgroundStatus = "Expired or superseded block discarded";
                return;
            }
            BackgroundAcceptedBlocks++;
            ForecastKnotsSampled += ticket.Count - (ticket.HasFirst ? 1 : 0);
            SurfaceHeightQueries += (ticket.Count - (ticket.HasFirst ? 1 : 0)) * (UseFullWaterHeight ? 10 : 8);
            PredictionPerformance.SampledKnots += ticket.Count - (ticket.HasFirst ? 1 : 0);
            if (!ReferenceEquals(target, oceanCurve))
            {
                target.ContinueFrom(oceanCurve, now, WindRefreshDelay);
                spareOceanCurve = oceanCurve;
                oceanCurve = target;
                BackgroundWindAdoptions++;
                WindRefreshes++;
                PredictionPerformance.WindRefreshes++;
                nextWindRefresh = now + WindRefreshDelay;
            }
        }

        private void QueueOceanBlock(OceanCurve curve, double now, bool replace)
        {
            bool append = !replace && curve.Count > 0 && curve.End > now + SurfaceDerivativeStep;
            double start = append ? curve.End : now;
            if (!append) curve.Reset(curve.Input, curve.Epoch);
            double end = Math.Min(start + 1.0, curve.Input.PhaseEnd);
            if (end - start < SurfaceDerivativeStep + 0.01) return;
            // Initial coverage beats all speculative tails; existing tails are ordered by
            // their expiration. Only one small block may be in flight for this floe.
            oceanTicket = FloeForecastWorker.Submit(curve.Input, start, end, append ? curve.End : now,
                append, append ? curve.Last : default);
            if (oceanTicket != null)
            {
                ticketCurve = curve;
                PredictionBuilds++;
                CountForecastBuild(append ? ForecastRebuildCause.Completed : ForecastRebuildCause.Empty);
            }
            else
            {
                retryBackgroundAt = now + 0.1 + electionPhase * 0.1;
                BackgroundStatus = FloeForecastWorker.StartupError == null ? "Queue full; retry scheduled" :
                    "Worker unavailable: " + FloeForecastWorker.StartupError;
            }
        }

        private bool TryBackgroundSurfaceFrame(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            HullGeometry hull, out SurfaceFrame frame)
        {
            frame = default;
            backgroundPending = false;
            if (!SeasonalIceFloeWaves.TryBackgroundSnapshot(out FloeWaveMath.Snapshot wind, out double now, out bool stable))
                return false;
            float spacing = Setting(PredictionKnotSeconds, 0.25f, 0.1f, 0.25f);
            if (OceanInputsChanged(settings, hull, context, spacing))
                ResetOceanInputs(wind, now, settings, hull, context, spacing);
            oceanCurve.Trim(now);
            ReceiveOceanBlock(now);
            bool changed = oceanCurve.Input.Wind.Revision != wind.Revision;
            if (changed && !WindRefreshPending)
            {
                WindChangesDeferred++;
                PredictionPerformance.WindChangesDeferred++;
            }
            WindRefreshPending = changed;
            if (changed && oceanTicket != null && ReferenceEquals(ticketCurve, oceanCurve) &&
                oceanTicket.Start > now + 2.0)
            {
                // A speculative old-wind tail must not delay a useful short replacement.
                // Do not cancel a replacement or urgent coverage on each wind-transition tick.
                FloeForecastWorker.Cancel(oceanTicket);
                oceanTicket = null;
                ticketCurve = null;
            }
            double reserve = oceanCurve.End - now;
            if (oceanTicket == null && now >= retryBackgroundAt)
            {
                if (changed && now >= nextWindRefresh)
                {
                    // Start a short replacement, not a recomputation of all ten seconds.
                    Vector2 radii = ProbeRadii(settings, wind.Along, wind.Across);
                    FloeWaveMath.Input input = SeasonalIceFloeWaves.CaptureBackgroundInput(wind, hull.Center, radii,
                        now, spacing, settings.SecondarySwellWeight, UseFullWaterHeight, context);
                    spareOceanCurve.Reset(input, FloeForecastWorker.Epoch);
                    QueueOceanBlock(spareOceanCurve, now, true);
                    nextWindRefresh = now + WindRefreshDelay;
                }
                else
                {
                    float desired = stable && !changed ? Setting(BackgroundForecastSeconds, 10f, 2f, 10f) : 2f;
                    // Refill a whole second at a time, not a tiny new segment every frame.
                    // This keeps the displayed usable reserve at or below the configured target.
                    if (reserve <= desired - 1.0 + SurfaceDerivativeStep)
                        QueueOceanBlock(oceanCurve, now, false);
                }
            }
            BackgroundReserveSeconds = (float)Math.Max(0, oceanCurve.End - now - SurfaceDerivativeStep);
            PredictionAge = (float)(now - oceanCurve.Input.TimeOrigin);
            if (!oceanCurve.Evaluate(now, out FloeWaveMath.Sample current) ||
                !oceanCurve.Evaluate(now + SurfaceDerivativeStep, out FloeWaveMath.Sample next))
            {
                // Never build a multi-knot forecast on the main thread to catch up. Keep
                // the accepted body pose, publish zero velocities, and retry in the worker.
                backgroundPending = true;
                BackgroundWaitFrames++;
                BackgroundStatus = oceanTicket != null ? "Waiting for first/current coverage" : BackgroundStatus;
                return false;
            }
            FloeWaveMath.Input accepted = oceanCurve.Input;
            frame.Center = Body.worldCenterOfMass;
            frame.Hull = hull;
            frame.SampleVelocity = KinematicVelocity + Vector3.Cross(KinematicAngularVelocity, hull.Center - frame.Center);
            frame.Wind = accepted.Wind.Along;
            frame.Side = accepted.Wind.Across;
            frame.AlongRadius = accepted.Radii.x;
            frame.AcrossRadius = accepted.Radii.y;
            frame.Heights = current.Heights;
            frame.NextHeights = next.Heights;
            frame.Height = current.Height;
            frame.VerticalVelocity = current.Rate;
            frame.Normal = PlaneNormal(current.Heights, frame.Wind, frame.Side, accepted.Radii, settings.MaxSurfaceTilt);
            frame.NextNormal = PlaneNormal(next.Heights, frame.Wind, frame.Side, accepted.Radii, settings.MaxSurfaceTilt);
            PredictionHits++;
            PredictionPerformance.ForecastHits++;
            BackgroundStatus = changed ? "Using accepted wind; replacement scheduled" : "Using rolling background curve";
            PredictionInvalidation = BackgroundStatus;
            return WaterValid(frame.Height) && Finite(frame.VerticalVelocity) && Finite(frame.Normal) && Finite(frame.NextNormal);
        }

        private bool TryKinematicSurfaceFrame(SeasonalIceFloeWaves.SurfaceContext context, SurfaceSettings settings,
            HullGeometry hull, out SurfaceFrame frame)
        {
            if (UsingBackgroundForecast)
                return TryBackgroundSurfaceFrame(context, settings, hull, out frame);
            if (oceanInputsValid)
                InvalidatePrediction("Returned to synchronous surface forecasting");
            ReleaseBackgroundForecast();
            return TryScheduledSurfaceFrame(context, settings, hull, out frame);
        }

        private void AppendBackgroundDiagnostics(StringBuilder b)
        {
            b.Append("\nSurface mode=Background rolling activeArea=").Append(InActivePhysicsArea);
            b.Append(" waveRadius=").Append(Number(SeasonalIceFloeWaves.WaterDistance));
            b.Append("\nDistance XZ camera/reference/player=").Append(Number(DistanceToCamera)).Append('/');
            b.Append(Number(DistanceToReference)).Append('/').Append(Number(DistanceToPlayer));
            b.Append("\nBackground reserve/target=").Append(Number(BackgroundReserveSeconds)).Append('/');
            b.Append(Number(Setting(BackgroundForecastSeconds, 10f, 2f, 10f))).Append("s knots=").Append(oceanCurve?.Count ?? 0);
            b.Append(" pending=").Append(oceanTicket != null).Append(" status=").Append(BackgroundStatus);
            b.Append("\nBackground blocks/discards/waitFrames=").Append(BackgroundAcceptedBlocks).Append('/');
            b.Append(BackgroundDiscardedBlocks).Append('/').Append(BackgroundWaitFrames);
            b.Append(" queue/work ms=").Append(Number(BackgroundQueueMilliseconds)).Append('/').Append(Number(BackgroundWorkMilliseconds));
            b.Append("\nBackground wind used/latest=").Append(oceanCurve?.Input?.Wind.Revision ?? 0).Append('/');
            b.Append(SeasonalIceFloeWaves.BackgroundWindRevision).Append(" adoptions=").Append(BackgroundWindAdoptions);
            b.Append(" resets=").Append(BackgroundResets).Append(" sampleHits=").Append(PredictionHits);
            b.Append("\nWorker queued/active/highWater=").Append(FloeForecastWorker.Queued).Append('/');
            b.Append(Volatile.Read(ref FloeForecastWorker.Active)).Append('/').Append(FloeForecastWorker.HighWater);
            b.Append(" submitted/finished/cancelled/rejected/errors=").Append(Interlocked.Read(ref FloeForecastWorker.Submitted)).Append('/');
            b.Append(Interlocked.Read(ref FloeForecastWorker.Finished)).Append('/').Append(Interlocked.Read(ref FloeForecastWorker.Cancelled)).Append('/');
            b.Append(Interlocked.Read(ref FloeForecastWorker.Rejected)).Append('/').Append(Interlocked.Read(ref FloeForecastWorker.Errors));
            b.Append("\nWorker sampledKnots=").Append(Interlocked.Read(ref FloeForecastWorker.Knots));
            b.Append(" total work/queue ms=").Append(Number((float)(Interlocked.Read(ref FloeForecastWorker.TotalWorkTicks) * 1000.0 / Stopwatch.Frequency)));
            b.Append('/').Append(Number((float)(Interlocked.Read(ref FloeForecastWorker.TotalQueueTicks) * 1000.0 / Stopwatch.Frequency)));
            b.Append("\nOcean depth/offset=1/0 nativeWaterThrottling=").Append(SeasonalIceFloeWaves.NativeWaterThrottlingActive);
        }
    }
}
