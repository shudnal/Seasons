using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonEnvironment
    {
        internal const string EnvironmentName = "Seasons_BloodMoon";

        private static EnvMan registeredEnvMan;
        private static EnvSetup bloodEnvironment;
        private static string previousForceEnvironment;
        private static bool ownsForceEnvironment;
        private static GameObject clonedFaderFx;
        private static readonly List<ParticleState> particleStates = new List<ParticleState>();

        internal static void EnsureRegistered()
        {
            EnvMan envMan = EnvMan.instance;
            if (envMan == null)
                return;
            if (ReferenceEquals(registeredEnvMan, envMan) && envMan.GetEnv(EnvironmentName) != null)
                return;

            CleanupRegistration();
            registeredEnvMan = envMan;
            EnvSetup source = envMan.GetEnv("Fader") ?? envMan.GetDefaultEnv();
            if (source == null)
                return;

            bloodEnvironment = source.Clone();
            bloodEnvironment.m_name = EnvironmentName;
            bloodEnvironment.m_default = false;
            SetRedChannel(bloodEnvironment);
            bloodEnvironment.m_windMin = 1f;
            bloodEnvironment.m_windMax = 2f;
            bloodEnvironment.m_sunAngle = 70f;
            bloodEnvironment.m_musicMorning = string.Empty;
            bloodEnvironment.m_musicEvening = string.Empty;
            bloodEnvironment.m_musicDay = string.Empty;
            bloodEnvironment.m_musicNight = string.Empty;

            GameObject sourceFx = envMan.m_environments.Select(env => env.m_envObject).FirstOrDefault(obj => obj != null && obj.name == "Ashlands_FaderFX")
                ?? source.m_envObject;
            if (sourceFx != null)
            {
                clonedFaderFx = UnityEngine.Object.Instantiate(sourceFx, sourceFx.transform.parent);
                clonedFaderFx.name = "Seasons_BloodMoon_FaderFX";
                clonedFaderFx.SetActive(false);
                PruneAndCacheCloudParticles(clonedFaderFx);
                bloodEnvironment.m_envObject = clonedFaderFx;
            }

            envMan.AppendEnvironment(bloodEnvironment);
            LogInfo("[BloodMoon.Presentation] Registered Seasons_BloodMoon environment.");
        }

        internal static void AcquireForcedEnvironment()
        {
            EnsureRegistered();
            EnvMan envMan = EnvMan.instance;
            if (envMan == null || envMan.GetEnv(EnvironmentName) == null)
                return;

            if (!ownsForceEnvironment)
                previousForceEnvironment = envMan.m_forceEnv ?? string.Empty;
            envMan.SetForceEnvironment(EnvironmentName);
            ownsForceEnvironment = envMan.m_forceEnv == EnvironmentName;
            SetParticleFactor(1f);
        }

        internal static void ReleaseForcedEnvironment()
        {
            EnvMan envMan = EnvMan.instance;
            if (envMan != null && ownsForceEnvironment && envMan.m_forceEnv == EnvironmentName)
                envMan.SetForceEnvironment(previousForceEnvironment ?? string.Empty);
            ownsForceEnvironment = false;
            previousForceEnvironment = string.Empty;
            SetParticleFactor(0f);
        }

        internal static void CleanupRegistration()
        {
            ReleaseForcedEnvironment();
            if (registeredEnvMan != null && bloodEnvironment != null && registeredEnvMan.m_environments.Contains(bloodEnvironment))
                registeredEnvMan.m_environments.Remove(bloodEnvironment);
            if (clonedFaderFx != null)
                UnityEngine.Object.Destroy(clonedFaderFx);
            clonedFaderFx = null;
            bloodEnvironment = null;
            registeredEnvMan = null;
            particleStates.Clear();
        }

        internal static float GetVisualFactor()
        {
            BloodMoonGlobalSnapshot snapshot = BloodMoonNetwork.ClientGlobal;
            BloodMoonScheduleSnapshot schedule = snapshot.Schedule;
            if (schedule == null || !schedule.IsValid || !SeasonState.IsActive)
                return 0f;

            double now = seasonState.GetTotalSeconds();
            if (snapshot.Phase == BloodMoonEventPhase.Forewarning)
            {
                if (EnvMan.instance == null || !EnvMan.IsNight())
                    return 0f;
                float advance = Mathf.Clamp01((float)((now - schedule.ForewarningAt) / Math.Max(1d, schedule.MarkedAt - schedule.ForewarningAt)));
                return Mathf.Lerp(0.08f, 0.28f, advance);
            }
            if (snapshot.Phase == BloodMoonEventPhase.Marked)
                return Mathf.Clamp01((float)((now - schedule.MarkedAt) / Math.Max(1d, schedule.ActiveAt - schedule.MarkedAt)));
            if (snapshot.Phase == BloodMoonEventPhase.Active || snapshot.Phase == BloodMoonEventPhase.AutoCompleting || snapshot.Phase == BloodMoonEventPhase.Resolving && snapshot.ResolutionStep < BloodMoonResolutionStep.RestoringWorldSystems)
                return 1f;
            return 0f;
        }

        internal static EnvOverlayState ApplyOverlay(EnvSetup env)
        {
            float factor = GetVisualFactor();
            if (env == null || factor <= 0f)
            {
                SetParticleFactor(factor);
                return null;
            }

            EnsureRegistered();
            if (bloodEnvironment == null || env.m_name == EnvironmentName)
            {
                SetParticleFactor(factor);
                return null;
            }

            EnvOverlayState state = new EnvOverlayState(env);
            LerpColors(env, bloodEnvironment, factor);
            env.m_windMin = Mathf.Lerp(state.WindMin, bloodEnvironment.m_windMin, factor);
            env.m_windMax = Mathf.Lerp(state.WindMax, bloodEnvironment.m_windMax, factor);
            env.m_sunAngle = Mathf.Lerp(state.SunAngle, bloodEnvironment.m_sunAngle, factor);
            SetParticleFactor(factor);
            return state;
        }

        internal static void RestoreOverlay(EnvSetup env, EnvOverlayState state)
        {
            state?.Restore(env);
        }

        private static void SetRedChannel(EnvSetup env)
        {
            env.m_ambColorNight.r = 1f;
            env.m_ambColorDay.r = 1f;
            env.m_fogColorNight.r = 1f;
            env.m_fogColorMorning.r = 1f;
            env.m_fogColorDay.r = 1f;
            env.m_fogColorEvening.r = 1f;
            env.m_fogColorSunNight.r = 1f;
            env.m_fogColorSunMorning.r = 1f;
            env.m_fogColorSunDay.r = 1f;
            env.m_fogColorSunEvening.r = 1f;
            env.m_sunColorNight.r = 1f;
            env.m_sunColorMorning.r = 1f;
            env.m_sunColorDay.r = 1f;
            env.m_sunColorEvening.r = 1f;
        }

        private static void LerpColors(EnvSetup env, EnvSetup target, float factor)
        {
            env.m_ambColorNight = Color.Lerp(env.m_ambColorNight, target.m_ambColorNight, factor);
            env.m_ambColorDay = Color.Lerp(env.m_ambColorDay, target.m_ambColorDay, factor);
            env.m_fogColorNight = Color.Lerp(env.m_fogColorNight, target.m_fogColorNight, factor);
            env.m_fogColorMorning = Color.Lerp(env.m_fogColorMorning, target.m_fogColorMorning, factor);
            env.m_fogColorDay = Color.Lerp(env.m_fogColorDay, target.m_fogColorDay, factor);
            env.m_fogColorEvening = Color.Lerp(env.m_fogColorEvening, target.m_fogColorEvening, factor);
            env.m_fogColorSunNight = Color.Lerp(env.m_fogColorSunNight, target.m_fogColorSunNight, factor);
            env.m_fogColorSunMorning = Color.Lerp(env.m_fogColorSunMorning, target.m_fogColorSunMorning, factor);
            env.m_fogColorSunDay = Color.Lerp(env.m_fogColorSunDay, target.m_fogColorSunDay, factor);
            env.m_fogColorSunEvening = Color.Lerp(env.m_fogColorSunEvening, target.m_fogColorSunEvening, factor);
            env.m_sunColorNight = Color.Lerp(env.m_sunColorNight, target.m_sunColorNight, factor);
            env.m_sunColorMorning = Color.Lerp(env.m_sunColorMorning, target.m_sunColorMorning, factor);
            env.m_sunColorDay = Color.Lerp(env.m_sunColorDay, target.m_sunColorDay, factor);
            env.m_sunColorEvening = Color.Lerp(env.m_sunColorEvening, target.m_sunColorEvening, factor);
        }

        private static void PruneAndCacheCloudParticles(GameObject root)
        {
            particleStates.Clear();
            for (int i = root.transform.childCount - 1; i >= 0; --i)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name != "cloud" && child.name != "cloud (1)")
                    UnityEngine.Object.Destroy(child.gameObject);
            }

            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                ParticleSystem.MinMaxGradient color = main.startColor;
                Color start = color.color;
                start.r = 1f;
                main.startColor = start;
                ParticleSystem.EmissionModule emission = particle.emission;
                particleStates.Add(new ParticleState(particle, emission.rateOverTimeMultiplier));
            }
        }

        private static void SetParticleFactor(float factor)
        {
            foreach (ParticleState state in particleStates)
            {
                if (state.System == null)
                    continue;
                ParticleSystem.EmissionModule emission = state.System.emission;
                emission.rateOverTimeMultiplier = state.OriginalRateOverTime * Mathf.Clamp01(factor);
                emission.enabled = factor > 0f;
                if (factor <= 0f)
                    state.System.Clear();
            }
        }

        private readonly struct ParticleState
        {
            internal readonly ParticleSystem System;
            internal readonly float OriginalRateOverTime;

            internal ParticleState(ParticleSystem system, float originalRateOverTime)
            {
                System = system;
                OriginalRateOverTime = originalRateOverTime;
            }
        }

        internal sealed class EnvOverlayState
        {
            private readonly Color ambNight;
            private readonly Color ambDay;
            private readonly Color fogNight;
            private readonly Color fogMorning;
            private readonly Color fogDay;
            private readonly Color fogEvening;
            private readonly Color fogSunNight;
            private readonly Color fogSunMorning;
            private readonly Color fogSunDay;
            private readonly Color fogSunEvening;
            private readonly Color sunNight;
            private readonly Color sunMorning;
            private readonly Color sunDay;
            private readonly Color sunEvening;
            internal readonly float WindMin;
            internal readonly float WindMax;
            internal readonly float SunAngle;

            internal EnvOverlayState(EnvSetup env)
            {
                ambNight = env.m_ambColorNight; ambDay = env.m_ambColorDay;
                fogNight = env.m_fogColorNight; fogMorning = env.m_fogColorMorning; fogDay = env.m_fogColorDay; fogEvening = env.m_fogColorEvening;
                fogSunNight = env.m_fogColorSunNight; fogSunMorning = env.m_fogColorSunMorning; fogSunDay = env.m_fogColorSunDay; fogSunEvening = env.m_fogColorSunEvening;
                sunNight = env.m_sunColorNight; sunMorning = env.m_sunColorMorning; sunDay = env.m_sunColorDay; sunEvening = env.m_sunColorEvening;
                WindMin = env.m_windMin; WindMax = env.m_windMax; SunAngle = env.m_sunAngle;
            }

            internal void Restore(EnvSetup env)
            {
                if (env == null)
                    return;
                env.m_ambColorNight = ambNight; env.m_ambColorDay = ambDay;
                env.m_fogColorNight = fogNight; env.m_fogColorMorning = fogMorning; env.m_fogColorDay = fogDay; env.m_fogColorEvening = fogEvening;
                env.m_fogColorSunNight = fogSunNight; env.m_fogColorSunMorning = fogSunMorning; env.m_fogColorSunDay = fogSunDay; env.m_fogColorSunEvening = fogSunEvening;
                env.m_sunColorNight = sunNight; env.m_sunColorMorning = sunMorning; env.m_sunColorDay = sunDay; env.m_sunColorEvening = sunEvening;
                env.m_windMin = WindMin; env.m_windMax = WindMax; env.m_sunAngle = SunAngle;
            }
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.Awake))]
    internal static class BloodMoonEnvManAwakePatch
    {
        private static void Postfix() => BloodMoonEnvironment.EnsureRegistered();
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.OnDestroy))]
    internal static class BloodMoonEnvManDestroyPatch
    {
        private static void Prefix() => BloodMoonEnvironment.CleanupRegistration();
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    internal static class BloodMoonEnvManSetEnvPatch
    {
        [HarmonyPriority(Priority.Last - 10)]
        private static void Prefix(EnvSetup env, ref BloodMoonEnvironment.EnvOverlayState __state)
        {
            __state = BloodMoonEnvironment.ApplyOverlay(env);
        }

        [HarmonyPriority(Priority.First + 10)]
        private static void Postfix(EnvSetup env, BloodMoonEnvironment.EnvOverlayState __state)
        {
            BloodMoonEnvironment.RestoreOverlay(env, __state);
        }

        private static Exception Finalizer(Exception __exception, EnvSetup env, BloodMoonEnvironment.EnvOverlayState __state)
        {
            BloodMoonEnvironment.RestoreOverlay(env, __state);
            return __exception;
        }
    }
}
