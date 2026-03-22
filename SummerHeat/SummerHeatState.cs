using UnityEngine;

namespace Seasons
{
    internal enum HeatZone
    {
        Green,
        Neutral,
        Red,
        Max
    }

    internal enum SummerHeatMode
    {
        Stable,
        Heating,
        CoolingFast,
        CoolingNormal,
        CoolingSlow
    }

    internal struct SummerHeatState
    {
        public bool MechanicActive;
        public bool SeasonHeatWindowActive;
        public bool IsSunny;
        public bool IsInSun;
        public bool IsInShade;
        public bool IsCooling;
        public bool BiomeSupported;
        public float HeatPercent;
        public float OverflowHeatPercent;
        public float TotalHeatPercent;
        public float HeatFactor;
        public float GreenFactor;
        public float RedFactor;
        public float MaxFactor;
        public HeatZone Zone;
        public int Direction;

        public void SetHeat(float heatPercent, float overflowHeatPercent, float totalHeatPercent, float displayCap, HeatZone zone, float greenFactor, float redFactor, float maxFactor)
        {
            HeatPercent = Mathf.Clamp(heatPercent, 0f, 100f);
            OverflowHeatPercent = Mathf.Max(0f, overflowHeatPercent);
            TotalHeatPercent = Mathf.Max(0f, totalHeatPercent);
            HeatFactor = Mathf.Clamp01(displayCap <= 0f ? 0f : HeatPercent / displayCap);
            GreenFactor = Mathf.Clamp01(greenFactor);
            RedFactor = Mathf.Clamp01(redFactor);
            MaxFactor = Mathf.Clamp01(maxFactor);
            Zone = zone;
        }
    }

    internal static class SummerHeat
    {
        public static SummerHeatComponent Instance => SummerHeatComponent.Instance;
        public static bool IsReady => Instance != null && Instance.Player != null;
        public static bool IsMechanicActive => Instance != null && Instance.State.MechanicActive;
        public static bool IsSeasonHeatWindowActive => Instance != null && Instance.State.SeasonHeatWindowActive;
        public static bool IsSunny => Instance != null && Instance.State.IsSunny;
        public static bool IsInSun => Instance != null && Instance.State.IsInSun;
        public static bool IsInShade => Instance != null && Instance.State.IsInShade;
        public static bool IsCooling => Instance != null && Instance.State.IsCooling;
        public static bool IsBiomeSupported => Instance != null && Instance.State.BiomeSupported;
        public static float HeatPercent => Instance != null ? Instance.State.HeatPercent : 0f;
        public static float TotalHeatPercent => Instance != null ? Instance.State.TotalHeatPercent : 0f;
        public static float OverflowHeatPercent => Instance != null ? Instance.State.OverflowHeatPercent : 0f;
        public static float HeatFactor => Instance != null ? Instance.State.HeatFactor : 0f;
        public static float GreenFactor => Instance != null ? Instance.State.GreenFactor : 0f;
        public static float RedFactor => Instance != null ? Instance.State.RedFactor : 0f;
        public static float MaxEffectFactor => Instance != null ? Instance.State.MaxFactor : 0f;
        public static int Direction => Instance != null ? Instance.State.Direction : 0;
        public static HeatZone CurrentZone => Instance != null ? Instance.State.Zone : HeatZone.Neutral;
    }
}
