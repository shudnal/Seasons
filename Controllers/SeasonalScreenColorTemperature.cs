using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonalScreenColorTemperature
    {
        private static ColorGradingModel controlledModel;
        private static float baseTemperature;
        private static bool hasBaseTemperature;

        internal static void Apply()
        {
            if (!UseTextureControllers())
            {
                Restore();
                return;
            }

            ColorGradingModel model = GetCurrentModel();
            if (model == null)
            {
                Restore();
                return;
            }

            if (!ReferenceEquals(controlledModel, model))
            {
                Restore();
                controlledModel = model;
                baseTemperature = model.settings.basic.temperature;
                hasBaseTemperature = true;
            }

            float seasonalOffset = 0f;
            if (controlLightings?.Value == true && SeasonState.IsActive)
                seasonalOffset = SeasonState.seasonLightings.GetSeasonLighting(seasonState.GetCurrentSeason()).screenColorTemperature;

            SetTemperature(Mathf.Clamp(baseTemperature + seasonalOffset, -100f, 100f));
        }

        internal static void Reset()
        {
            Restore();
        }

        private static ColorGradingModel GetCurrentModel()
        {
            CameraEffects cameraEffects = CameraEffects.instance;
            if (cameraEffects == null || cameraEffects.m_postProcessing == null || cameraEffects.m_postProcessing.profile == null)
                return null;

            return cameraEffects.m_postProcessing.profile.colorGrading;
        }

        private static void Restore()
        {
            if (controlledModel != null && hasBaseTemperature)
                SetTemperature(baseTemperature);

            controlledModel = null;
            baseTemperature = 0f;
            hasBaseTemperature = false;
        }

        private static void SetTemperature(float temperature)
        {
            if (controlledModel == null)
                return;

            ColorGradingModel.Settings settings = controlledModel.settings;
            ColorGradingModel.BasicSettings basic = settings.basic;

            if (Mathf.Approximately(basic.temperature, temperature))
                return;

            basic.temperature = temperature;
            settings.basic = basic;
            controlledModel.settings = settings;
        }

        [HarmonyPatch(typeof(CameraEffects), nameof(CameraEffects.Awake))]
        private static class CameraEffects_Awake_ApplySeasonalScreenColorTemperature
        {
            private static void Postfix()
            {
                Apply();
            }
        }

        [HarmonyPatch(typeof(CameraEffects), nameof(CameraEffects.OnDestroy))]
        private static class CameraEffects_OnDestroy_RestoreScreenColorTemperature
        {
            private static void Prefix(CameraEffects __instance)
            {
                if (__instance?.m_postProcessing?.profile?.colorGrading != null &&
                    ReferenceEquals(__instance.m_postProcessing.profile.colorGrading, controlledModel))
                    Restore();
            }
        }
    }
}
