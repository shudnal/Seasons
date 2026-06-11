using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons
{
    public static class SummerHeatVisuals
    {
        public const string SummerHeatHazeObjectName = "SummerHeatHaze";

        private static GameObject _hazeObject;
        private static bool _summerHeatColorApplied;
        private static bool _hasDefaultHeatDistortionColor;
        private static Color _defaultHeatDistortionColor;

        internal static void Initialize()
        {
            DestroyHazeObject();

            if (!ZoneSystem.instance)
                return;

            GameObject hazeObject = FindAshlandsHaze();
            Transform parent = Game.instance?.gameObject?.transform?.Find("_Environment/FollowPlayer");

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
                            Object.Destroy(child.gameObject);
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
            if (ps == null)
                return;

            ParticleSystem.MainModule main = ps.main;
            main.simulationSpeed = 1f;
            main.maxParticles = 200;

            ParticleSystem.MinMaxGradient startColor = main.startColor;
            startColor.colorMax = new Color(1f, 1f, 1f, 0.75f);
            main.startColor = startColor;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 100f;

            if (ps.transform.parent != null)
                ps.transform.parent.localScale = new Vector3(3f, 1.5f, 3f);

            ps.transform.localPosition = new Vector3(0f, -5f, 0f);
        }

        internal static void Reset()
        {
            DestroyHazeObject();
            _summerHeatColorApplied = false;
            _hasDefaultHeatDistortionColor = false;
        }

        internal static void UpdateHazeState()
        {
            if (_hazeObject == null)
                return;

            bool active = IsWorldHazeActive();
            if (_hazeObject.activeSelf != active)
                _hazeObject.SetActive(active);
        }

        internal static bool IsWorldHazeActive()
        {
            if (!Seasons.summerHeatEnabled.Value || !SummerHeat.IsReady || !SummerHeat.IsSeasonHeatWindowActive || !SummerHeat.IsSunny || !SummerHeat.IsBiomeSupported)
                return false;

            if (SummerHeat.Instance == null || !SummerHeat.Instance.IsDaytime())
                return false;

            Player player = Player.m_localPlayer;
            if (player != null && player.GetCurrentBiome() == Heightmap.Biome.Mistlands)
                return false;

            return true;
        }

        internal static float GetVisualIntensity()
        {
            if (!IsPersonalVisualStateActive())
                return 0f;

            bool isDaytime = SummerHeat.Instance == null || SummerHeat.Instance.IsDaytime();
            float nightFactor = Mathf.Clamp(Seasons.summerHeatNightFactor.Value, 0.1f, 1f);
            float timeScale = isDaytime ? 1f : nightFactor;
            float heatCap = SummerHeatController.DaytimeHeatCap * timeScale;
            float greenThreshold = Mathf.Clamp(Seasons.summerHeatGreenThreshold.Value, 0f, 100f) * timeScale;
            float greenFadeWidth = Mathf.Max(0.1f, Mathf.Clamp(Seasons.summerHeatGreenFadeWidth.Value, 0f, 100f) * timeScale);
            float visualStart = Mathf.Min(heatCap, greenThreshold + greenFadeWidth);

            if (SummerHeat.HeatPercent <= visualStart || heatCap <= visualStart)
                return 0f;

            return Mathf.InverseLerp(visualStart, heatCap, SummerHeat.HeatPercent);
        }

        internal static void ApplyCameraDistortion(HeatDistortImageEffect heatDistortImageEffect)
        {
            if (heatDistortImageEffect == null)
                return;

            if (!_hasDefaultHeatDistortionColor)
            {
                _defaultHeatDistortionColor = heatDistortImageEffect.m_color;
                _hasDefaultHeatDistortionColor = true;
            }

            float summerHeatIntensity = GetVisualIntensity();
            if (summerHeatIntensity <= 0f)
            {
                if (_summerHeatColorApplied)
                {
                    heatDistortImageEffect.m_color = _defaultHeatDistortionColor;
                    _summerHeatColorApplied = false;
                }
                return;
            }

            heatDistortImageEffect.enabled = true;
            heatDistortImageEffect.m_intensity = Mathf.Max(heatDistortImageEffect.m_intensity, summerHeatIntensity);

            float maxOverflow = Mathf.Max(1f, SummerHeatController.ClampPercent(Seasons.summerHeatMaxOverflow.Value));
            float overflowFactor = Mathf.Clamp01(SummerHeat.OverflowHeatPercent / maxOverflow);
            Color color = _defaultHeatDistortionColor;
            color.a = Mathf.Lerp(_defaultHeatDistortionColor.a, 0.85f, overflowFactor);
            heatDistortImageEffect.m_color = color;
            _summerHeatColorApplied = true;
        }

        private static bool IsPersonalVisualStateActive() => Seasons.summerHeatEnabled.Value && SummerHeat.IsReady && SummerHeat.IsMechanicActive && SummerHeat.HeatFactor > 0f;

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
        private static void Postfix(Character __instance)
        {
            if (__instance != Player.m_localPlayer)
                return;

            SummerHeatVisuals.UpdateHazeState();
            SummerHeatVisuals.ApplyCameraDistortion(GameCamera.instance?.m_heatDistortImageEffect);
        }
    }
}
