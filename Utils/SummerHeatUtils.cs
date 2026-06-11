using UnityEngine;

namespace Seasons
{
    internal static class SummerHeatUtils
    {
        internal const float NightFactorMin = 0.1f;

        internal static float ClampPercent(float value) => Mathf.Clamp(value, 0f, 100f);

        internal static float ClampEffect(float value) => Mathf.Clamp01(value);

        internal static float GetNightFactor() => Mathf.Clamp(Seasons.summerHeatNightFactor.Value, NightFactorMin, 1f);

        internal static float ScaleHeatPercentForTime(float value, bool isDaytime, float nightFactor) => isDaytime ? value : value * nightFactor;

        internal static float ScaleHeatPercentForTime(float value, bool isDaytime) => ScaleHeatPercentForTime(value, isDaytime, GetNightFactor());
    }
}
