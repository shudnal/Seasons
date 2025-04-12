using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class EnvManPatches
    {
        private static int totalSecondsCached;
        internal static bool skiptimeUsed;

        [HarmonyPatch]
        public static class EnvMan_DayLength
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.Awake));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.FixedUpdate));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetCurrentDay));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetDay));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.SkipToMorning));
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(ref long ___m_dayLengthSec)
            {
                if (dayLengthSec.Value != 0L && ___m_dayLengthSec != dayLengthSec.Value)
                    ___m_dayLengthSec = dayLengthSec.Value;
            }
        }

        [HarmonyPatch]
        public static class EnvMan_TotalSecondsUpdate
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.Awake));
                yield return AccessTools.Method(typeof(EnvMan), nameof(EnvMan.OnDestroy));
            }

            private static void Postfix() => totalSecondsCached = 0;
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTriggers))]
        public static class EnvMan_UpdateTriggers_SeasonStateUpdate
        {
            private static int secondUpdated;

            private static void Postfix(float oldDayFraction, float newDayFraction)
            {
                float fraction = SeasonState.GetDayFractionForSeasonChange();

                bool timeForSeasonToChange = oldDayFraction > 0.16f && oldDayFraction <= fraction && newDayFraction >= fraction && newDayFraction < 0.3f;
                if (logTime.Value && timeForSeasonToChange)
                    LogInfo($"It's time to check for seasons change {oldDayFraction} -> {newDayFraction}");

                bool forceSeasonChange = totalSecondsCached != 0 && Math.Abs(totalSecondsCached - (int)seasonState.GetTotalSeconds()) > 10;
                if (logTime.Value && forceSeasonChange)
                    LogInfo($"Total seconds was changed significantly, force update season");

                if (!forceSeasonChange && skiptimeUsed)
                {
                    forceSeasonChange = true;
                    LogInfo("Force update season state after skiptime command");
                }

                skiptimeUsed = false;

                if (secondUpdated != (secondUpdated = DateTime.Now.Second) || timeForSeasonToChange || forceSeasonChange)
                {
                    totalSecondsCached = (int)seasonState.GetTotalSeconds();
                    seasonState.UpdateState(timeForSeasonToChange, forceSeasonChange);
                }
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.RescaleDayFraction))]
        public static class EnvMan_RescaleDayFraction_DayNightLength
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(float fraction, ref float __result)
            {
                float dayStart = seasonState.DayStartFraction();
                if (dayStart == EnvMan.c_MorningL)
                    return true;

                float nightStart = 1.0f - dayStart;

                if (dayStart <= fraction && fraction <= nightStart)
                {
                    float num = (fraction - dayStart) / (nightStart - dayStart);
                    fraction = 0.25f + num * 0.5f;
                }
                else if (fraction < 0.5f)
                {
                    fraction = fraction / dayStart * 0.25f;
                }
                else
                {
                    float num2 = (fraction - nightStart) / dayStart;
                    fraction = 0.75f + num2 * 0.25f;
                }

                __result = fraction;
                return false;
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec))]
        public static class EnvMan_GetMorningStartSec_DayNightLength
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(EnvMan __instance, int day, ref double __result)
            {
                __result = (day * __instance.m_dayLengthSec) + (double)(__instance.m_dayLengthSec * seasonState.DayStartFraction(seasonState.GetSeason(day)));
                return false;
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
        public static class EnvMan_SkipToMorning_DayNightLength
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(EnvMan __instance, ref bool ___m_skipTime, ref double ___m_skipToTime, ref double ___m_timeSkipSpeed)
            {
                float dayStart = seasonState.DayStartFraction();
                if (dayStart == EnvMan.c_MorningL)
                    return true;

                double timeSeconds = ZNet.instance.GetTimeSeconds();
                double startOfMorning = timeSeconds - timeSeconds % __instance.m_dayLengthSec + __instance.m_dayLengthSec * dayStart;

                int day = __instance.GetDay(startOfMorning);
                double morningStartSec = __instance.GetMorningStartSec(day + 1);

                ___m_skipTime = true;
                ___m_skipToTime = morningStartSec;

                ___m_timeSkipSpeed = (morningStartSec - timeSeconds) / 12.0;

                LogInfo($"Time: {timeSeconds,-10:F2} Day: {day} Next morning: {morningStartSec,-10:F2} Skipspeed: {___m_timeSkipSpeed,-5:F2}");

                return false;
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.FixedUpdate))]
        public static class EnvMan_FixedUpdate_UpdateWarmStatus
        {
            private static bool IsCold() => EnvMan.IsFreezing() || EnvMan.IsCold();

            private static void Prefix(ref bool __state)
            {
                __state = IsCold();
            }
            private static void Postfix(bool __state)
            {
                if (__state != IsCold())
                    seasonState.CheckOverheatStatus(Player.m_localPlayer);
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
        public static class EnvMan_SetEnv_LuminancePatch
        {
            private class LightState
            {
                public Color m_ambColorNight;
                public Color m_fogColorNight;
                public Color m_fogColorSunNight;
                public Color m_sunColorNight;

                public Color m_ambColorDay;
                public Color m_fogColorMorning;
                public Color m_fogColorDay;
                public Color m_fogColorEvening;
                public Color m_fogColorSunMorning;
                public Color m_fogColorSunDay;
                public Color m_fogColorSunEvening;
                public Color m_sunColorMorning;
                public Color m_sunColorDay;
                public Color m_sunColorEvening;

                public float m_lightIntensityDay;
                public float m_lightIntensityNight;

                public float m_fogDensityNight;
                public float m_fogDensityMorning;
                public float m_fogDensityDay;
                public float m_fogDensityEvening;
            }

            private static readonly LightState _lightState = new LightState();

            private static Color ChangeColorLuminance(Color color, float luminanceMultiplier)
            {
                HSLColor newColor = new HSLColor(color);
                newColor.l *= luminanceMultiplier;
                return newColor.ToRGBA();
            }

            private static void SaveLightState(EnvSetup env)
            {
                _lightState.m_ambColorNight = env.m_ambColorNight;
                _lightState.m_sunColorNight = env.m_sunColorNight;
                _lightState.m_fogColorNight = env.m_fogColorNight;
                _lightState.m_fogColorSunNight = env.m_fogColorSunNight;

                _lightState.m_ambColorDay = env.m_ambColorDay;
                _lightState.m_sunColorDay = env.m_sunColorDay;
                _lightState.m_fogColorDay = env.m_fogColorDay;
                _lightState.m_fogColorSunDay = env.m_fogColorSunDay;

                _lightState.m_sunColorMorning = env.m_sunColorMorning;
                _lightState.m_fogColorMorning = env.m_fogColorMorning;
                _lightState.m_fogColorSunMorning = env.m_fogColorSunMorning;

                _lightState.m_sunColorEvening = env.m_sunColorEvening;
                _lightState.m_fogColorEvening = env.m_fogColorEvening;
                _lightState.m_fogColorSunEvening = env.m_fogColorSunEvening;

                _lightState.m_lightIntensityDay = env.m_lightIntensityDay;
                _lightState.m_lightIntensityNight = env.m_lightIntensityNight;

                _lightState.m_fogDensityNight = env.m_fogDensityNight;
                _lightState.m_fogDensityMorning = env.m_fogDensityMorning;
                _lightState.m_fogDensityDay = env.m_fogDensityDay;
                _lightState.m_fogDensityEvening = env.m_fogDensityEvening;
            }

            private static void ChangeEnvColor(EnvSetup env, SeasonLightings.SeasonLightingSettings lightingSettings, bool indoors = false)
            {
                env.m_ambColorNight = ChangeColorLuminance(env.m_ambColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
                env.m_fogColorNight = ChangeColorLuminance(env.m_fogColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
                env.m_fogColorSunNight = ChangeColorLuminance(env.m_fogColorSunNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);
                env.m_sunColorNight = ChangeColorLuminance(env.m_sunColorNight, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.night.luminanceMultiplier);

                env.m_fogColorMorning = ChangeColorLuminance(env.m_fogColorMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);
                env.m_fogColorSunMorning = ChangeColorLuminance(env.m_fogColorSunMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);
                env.m_sunColorMorning = ChangeColorLuminance(env.m_sunColorMorning, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.morning.luminanceMultiplier);

                env.m_ambColorDay = ChangeColorLuminance(env.m_ambColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
                env.m_fogColorDay = ChangeColorLuminance(env.m_fogColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
                env.m_fogColorSunDay = ChangeColorLuminance(env.m_fogColorSunDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);
                env.m_sunColorDay = ChangeColorLuminance(env.m_sunColorDay, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.day.luminanceMultiplier);

                env.m_fogColorEvening = ChangeColorLuminance(env.m_fogColorEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);
                env.m_fogColorSunEvening = ChangeColorLuminance(env.m_fogColorSunEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);
                env.m_sunColorEvening = ChangeColorLuminance(env.m_sunColorEvening, indoors ? lightingSettings.indoors.luminanceMultiplier : lightingSettings.evening.luminanceMultiplier);

                env.m_fogDensityNight *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.night.fogDensityMultiplier;
                env.m_fogDensityMorning *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.morning.fogDensityMultiplier;
                env.m_fogDensityDay *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.day.fogDensityMultiplier;
                env.m_fogDensityEvening *= indoors ? lightingSettings.indoors.fogDensityMultiplier : lightingSettings.evening.fogDensityMultiplier;

                env.m_lightIntensityDay *= lightingSettings.lightIntensityDayMultiplier;
                env.m_lightIntensityNight *= lightingSettings.lightIntensityNightMultiplier;
            }

            public static void ChangeLightState(EnvSetup env)
            {
                SaveLightState(env);

                SeasonLightings.SeasonLightingSettings lightingSettings = SeasonState.seasonLightings.GetSeasonLighting(seasonState.GetCurrentSeason());

                ChangeEnvColor(env, lightingSettings, indoors: Player.m_localPlayer != null && Player.m_localPlayer.InInterior());
            }

            public static void ResetLightState(EnvSetup env)
            {
                env.m_ambColorNight = _lightState.m_ambColorNight;
                env.m_sunColorNight = _lightState.m_sunColorNight;
                env.m_fogColorNight = _lightState.m_fogColorNight;
                env.m_fogColorSunNight = _lightState.m_fogColorSunNight;

                env.m_ambColorDay = _lightState.m_ambColorDay;
                env.m_sunColorDay = _lightState.m_sunColorDay;
                env.m_fogColorDay = _lightState.m_fogColorDay;
                env.m_fogColorSunDay = _lightState.m_fogColorSunDay;

                env.m_sunColorMorning = _lightState.m_sunColorMorning;
                env.m_fogColorMorning = _lightState.m_fogColorMorning;
                env.m_fogColorSunMorning = _lightState.m_fogColorSunMorning;

                env.m_sunColorEvening = _lightState.m_sunColorEvening;
                env.m_fogColorEvening = _lightState.m_fogColorEvening;
                env.m_fogColorSunEvening = _lightState.m_fogColorSunEvening;

                env.m_fogDensityNight = _lightState.m_fogDensityNight;
                env.m_fogDensityMorning = _lightState.m_fogDensityMorning;
                env.m_fogDensityDay = _lightState.m_fogDensityDay;
                env.m_fogDensityEvening = _lightState.m_fogDensityEvening;

                env.m_lightIntensityDay = _lightState.m_lightIntensityDay;
                env.m_lightIntensityNight = _lightState.m_lightIntensityNight;
            }

            [HarmonyPriority(Priority.Last)]
            [HarmonyBefore(new string[1] { "shudnal.GammaOfNightLights" })]
            public static void Prefix(EnvSetup env)
            {
                if (!controlLightings.Value || !UseTextureControllers())
                    return;

                ChangeLightState(env);
            }

            [HarmonyPriority(Priority.First)]
            [HarmonyAfter(new string[1] { "shudnal.GammaOfNightLights" })]
            public static void Postfix(EnvSetup env)
            {
                if (!controlLightings.Value || !UseTextureControllers())
                    return;

                ResetLightState(env);
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.CalculateFreezing))]
        public static class EnvMan_CalculateFreezing_SwimmingInWinterIsFreezing
        {
            private static void Postfix(ref bool __result)
            {
                if (!freezingSwimmingInWinter.Value)
                    return;

                Player player = Player.m_localPlayer;
                if (player == null)
                    return;

                __result = __result || player.IsSwimming() && seasonState.GetCurrentSeason() == Season.Winter && EnvMan.IsCold();
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.OnMorning))]
        public static class EnvMan_OnMorning_SeasonChangeAnnouncement
        {
            private static void ShowMessage(string message)
            {
                MessageHud.instance.m_msgQeue.Clear();
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
            }

            private static void Postfix()
            {
                if (!overrideNewDayMessagesOnSeasonStartEnd.Value)
                    return;

                Player player = Player.m_localPlayer;
                if (player == null)
                    return;

                if (seasonState.GetCurrentDay() == 1)
                    ShowMessage(GetSeasonTooltip(seasonState.GetCurrentSeason()));
                else if (seasonState.GetCurrentDay() == seasonState.GetDaysInSeason())
                    ShowMessage(GetSeasonIsComing(seasonState.GetNextSeason()));
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindForce))]
        public static class EnvMan_GetWindForce_WindIntensityMultiplier
        {
            private static void Prefix(ref Vector4 ___m_wind, ref float __state)
            {
                if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                    return;

                __state = ___m_wind.w;
                ___m_wind.w *= seasonState.GetWindIntensityMultiplier();
            }

            private static void Postfix(ref Vector4 ___m_wind, float __state)
            {
                if (seasonState.GetWindIntensityMultiplier() == 1.0f)
                    return;

                ___m_wind.w = __state;
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetWindIntensity))]
        public static class EnvMan_GetWindIntensity_WindIntensityMultiplier
        {
            private static void Postfix(ref float __result)
            {
                __result *= seasonState.GetWindIntensityMultiplier();
            }
        }
    }
}
