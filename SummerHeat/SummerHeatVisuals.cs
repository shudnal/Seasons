using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons
{
    public static class SummerHeatVisuals
    {
        public const string SummerHeatHazeObjectName = "SummerHeatHaze";

        private static GameObject _hazeObject;

        internal static void Initialize()
        {
            DestroyHazeObject();

            if (!ZoneSystem.instance)
                return;

            GameObject hazeObject = FindAshlandsHaze();

            Transform parent = Game.instance?.transform?.Find("_Environment/FollowPlayer");

            if (hazeObject && parent)
            {
                _hazeObject = Object.Instantiate(hazeObject, parent);
                _hazeObject.name = SummerHeatHazeObjectName;
                _hazeObject.SetActive(false);

                for (int i = _hazeObject.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = _hazeObject.transform.GetChild(i);
                    switch (child.name)
                    {
                        case "ash":
                        case "zinder":
                        case "fx_ember_rain":
                            child.parent = null;
                            UnityEngine.Object.Destroy(child.gameObject);
                            continue;
                        case "mist":
                            child.gameObject.SetActive(false);
                            continue;
                        case "vfx_Ashlands_HeatDistortion":
                            AdaptAshlandsHeatDistortion(child.GetComponent<ParticleSystem>(), child.GetComponent<ParticleSystemRenderer>());
                            continue;
                    }
                }
            }
            else
                Seasons.LogWarning($"Error when initializing summer heat: Haze object {hazeObject != null} FollowPlayer {parent != null}");
        }

        private static void AdaptAshlandsHeatDistortion(ParticleSystem ps, ParticleSystemRenderer psRenderer)
        {
            var main = ps.main;
            main.simulationSpeed = 1;
            main.maxParticles = 200;
            
            var startColor = main.startColor;
            startColor.colorMax = new Color(1f, 1f, 1f, 0.75f);
            main.startColor = startColor;

            var emission = ps.emission;
            emission.rateOverTime = 100;

            ps.transform.parent.localScale = new Vector3(3f, 1.5f, 3f);
            ps.transform.localPosition = new Vector3(0f, -5f, 0f);
        }

        internal static void Reset()
        {
            DestroyHazeObject();
        }

        internal static void UpdateHazeState()
        {
            if (_hazeObject == null)
                return;

            if (_hazeObject.activeSelf != IsMechanicActive())
                _hazeObject.SetActive(!_hazeObject.activeSelf);
        }

        internal static float GetVisualIntensity()
        {
            if (!IsVisualStateActive())
                return 0f;

            return Mathf.Clamp01(SummerHeat.HeatFactor);
        }

        private static bool IsMechanicActive() => SummerHeat.IsReady && SummerHeat.IsMechanicActive;

        private static bool IsVisualStateActive() => IsMechanicActive() && SummerHeat.HeatFactor > 0f;

        private static GameObject FindAshlandsHaze()
        {
            GameObject ashlandsLocList = ZoneSystem.instance.m_locationLists.FirstOrDefault(locList => locList.name == "_LocationList_Ashlands");
            if (ashlandsLocList == null)
                return null;

            return ashlandsLocList.transform.Find("environment_effects/FollowPlayer/Ashlands_AshRain")?.gameObject;
        }

        private static void DestroyHazeObject()
        {
            if (_hazeObject == null)
                return;

            Object.Destroy(_hazeObject);
            _hazeObject = null;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    internal static class ZoneSystem_Start_SummerHeatVisuals
    {
        private static void Postfix(ZoneSystem __instance)
        {
            SummerHeatVisuals.Initialize();
            SummerHeatVisuals.UpdateHazeState();
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.OnDestroy))]
    internal static class ZoneSystem_OnDestroy_SummerHeatVisuals
    {
        private static void Prefix() => SummerHeatVisuals.Reset();
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateHeatEffects))]
    internal static class Character_UpdateHeatEffects_SummerHeatVisuals
    {
        private static float _defaultHeatDistortionAlpha = 0;

        private static void Postfix(Character __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;

            SummerHeatVisuals.UpdateHazeState();

            float summerHeatIntensity = SummerHeatVisuals.GetVisualIntensity();
            if (summerHeatIntensity <= 0f)
                return;

            HeatDistortImageEffect heatDistortImageEffect = GameCamera.instance?.m_heatDistortImageEffect;
            if (heatDistortImageEffect == null)
                return;

            heatDistortImageEffect.enabled = true;
            heatDistortImageEffect.m_intensity = Mathf.Max(heatDistortImageEffect.m_intensity, summerHeatIntensity * 1.5f);

            if (_defaultHeatDistortionAlpha == 0f)
                _defaultHeatDistortionAlpha = heatDistortImageEffect.m_color.a;

            heatDistortImageEffect.m_color.a = Mathf.Lerp(_defaultHeatDistortionAlpha, 0.85f, SummerHeat.OverflowHeatPercent / Seasons.summerHeatMaxOverflow.Value);
        }
    }
}
