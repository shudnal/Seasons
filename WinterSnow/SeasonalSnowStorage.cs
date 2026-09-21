using System;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Separates seasonal save data from native Deep North snow.</summary>
    internal static class SeasonalSnowStorage
    {
        internal const long MissingEpoch = long.MinValue;

        internal readonly struct Snapshot
        {
            internal readonly bool Present;
            internal readonly bool CurrentWinter;
            internal readonly float Value;
            internal readonly float Baseline;
            internal readonly long From;
            internal readonly long Epoch;

            internal Snapshot(ZDO zdo)
            {
                Present = TryRead(zdo, out float value);
                Value = value;
                Epoch = zdo != null ? zdo.GetLong(SeasonsVars.s_seasonalSnowEpoch, MissingEpoch) : MissingEpoch;
                From = zdo != null ? zdo.GetLong(SeasonsVars.s_seasonalSnowFrom, 0L) : 0L;
                Baseline = zdo != null ? Sanitize(zdo.GetFloat(SeasonsVars.s_seasonalSnowBaseline, value)) : 0f;
                CurrentWinter = zdo != null && zdo.GetBool(SeasonsVars.s_seasonalSnowWinter);
            }

            internal bool AppliesTo(long epoch) => Present && (Epoch == MissingEpoch || Epoch == epoch);
        }

        // Read the calendar clock once: realtime seasons must not get a different
        // millisecond epoch from two DateTime reads while rounding a day boundary.
        internal static long CurrentWinterEpoch
        {
            get
            {
                double dayLength = seasonState.GetDayLengthInSeconds();
                double startOfDay = Math.Floor(seasonState.GetTotalSeconds() / dayLength) * dayLength;
                double start = startOfDay - (seasonState.GetCurrentDay() -
                    (seasonState.IsPendingSeasonChange() ? 0 : 1)) * dayLength;
                return (long)Math.Round(Math.Max(0d, start) * 1000d);
            }
        }

        internal static long ToTimestamp(double seconds) => (long)Math.Round(Math.Max(0d, seconds) * 1000d);
        internal static double FromTimestamp(long timestamp) => Math.Max(0L, timestamp) / 1000d;

        internal static bool IsPreviousWinter(ZDO zdo)
        {
            long epoch = zdo.GetLong(SeasonsVars.s_seasonalSnowEpoch, MissingEpoch);
            return epoch != MissingEpoch && epoch != CurrentWinterEpoch;
        }

        internal static bool HasEpoch(ZDO zdo) =>
            zdo != null && zdo.GetLong(SeasonsVars.s_seasonalSnowEpoch, MissingEpoch) != MissingEpoch;

        internal static bool HasCurrentWinterState(ZDO zdo) =>
            zdo != null && zdo.GetBool(SeasonsVars.s_seasonalSnowWinter) && !IsPreviousWinter(zdo);

        internal static bool HasLegacyState(ZDO zdo) =>
            zdo != null && (zdo.GetBool(SeasonsVars.s_seasonalSnowWatermark) ||
                zdo.GetBool(SeasonsVars.s_seasonalSnowWinter) ||
                zdo.GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) != 0L);

        internal static bool HasSavedValue(ZDO zdo) =>
            zdo != null && zdo.GetFloat(SeasonsVars.s_seasonalSnowValue, out _);

        internal static bool TryRead(ZDO zdo, out float value)
        {
            value = 0f;
            if (zdo == null)
                return false;
            // An explicitly stored zero is a snapshot, not a request for prediction.
            if (!zdo.GetFloat(SeasonsVars.s_seasonalSnowValue, out value) &&
                !(HasLegacyState(zdo) && zdo.GetFloat(ZDOVars.s_snow, out value)))
                return false;
            value = Sanitize(value);
            return true;
        }

        internal static float Sanitize(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

        internal static bool CanWrite(ZNetView view, ZDO zdo) =>
            view && view.IsValid() && zdo != null &&
                (zdo.GetOwner() == 0L || view.IsOwner());

        // Callers decide authority and cadence. Store the exact level and the weather
        // interval already consumed by that level in the same main-thread operation.
        internal static void Write(ZDO zdo, float value, long epoch, double consumedUntil)
        {
            if (zdo == null)
                return;
            value = Sanitize(value);
            zdo.Set(SeasonsVars.s_seasonalSnowValue, value);
            zdo.Set(SeasonsVars.s_seasonalSnowEpoch, epoch);
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, ToTimestamp(consumedUntil));
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, value);
            zdo.Set(SeasonsVars.s_seasonalSnowWinter, 1, okForNotOwner: true);
            zdo.Set(SeasonsVars.s_seasonalSnowWatermark, value > 0f ? 1 : 0, okForNotOwner: true);
            bool removed = zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter);
            removed |= zdo.RemoveInt(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            if (removed)
                zdo.IncreaseDataRevision();
            ClearNative(zdo);
        }

        internal static void Clear(ZDO zdo, bool clearNative)
        {
            if (zdo == null)
                return;
            bool removed = RemoveSavedValue(zdo);
            removed |= zdo.RemoveInt(SeasonsVars.s_seasonalSnowWinter);
            removed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter);
            removed |= zdo.RemoveInt(SeasonsVars.s_seasonalSnowWatermark);
            removed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowFrom);
            removed |= zdo.RemoveFloat(SeasonsVars.s_seasonalSnowBaseline);
            removed |= zdo.RemoveInt(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            if (removed)
                zdo.IncreaseDataRevision();
            if (clearNative)
                ClearNative(zdo);
        }

        internal static void MigrateLoaded(WearNTear piece)
        {
            // The caller has already classified this as a managed seasonal surface.
            piece.m_addPreSnow = false;
            ZNetView view = piece.m_nview;
            ZDO zdo = view ? view.GetZDO() : null;
            if (!CanWrite(view, zdo))
                return;
            bool hasValue = zdo.GetFloat(SeasonsVars.s_seasonalSnowValue, out _);
            bool legacy = HasLegacyState(zdo);
            if (!hasValue && legacy && zdo.GetFloat(ZDOVars.s_snow, out float oldValue))
            {
                zdo.Set(SeasonsVars.s_seasonalSnowValue, Sanitize(oldValue));
                hasValue = true;
            }
            // Also finish an interrupted migration whose custom value was already written.
            if (hasValue || legacy)
                ClearNative(zdo);
        }

        internal static void ClearNative(ZDO zdo)
        {
            if (zdo.GetFloat(ZDOVars.s_snow, 0f) != 0f)
                zdo.Set(ZDOVars.s_snow, 0f);
            if (zdo.GetBool(ZDOVars.s_preSnow))
                zdo.Set(ZDOVars.s_preSnow, false);
        }

        // Placement survives unload and snow cleanup, but only applies to its winter.
        internal static void RecordPlacement(ZDO zdo)
        {
            if (zdo == null || !SeasonState.IsActive || !ZNet.instance ||
                seasonState.GetCurrentDay() <= 0 || seasonState.GetCurrentSeason() != Season.Winter)
                return;
            zdo.Set(SeasonsVars.s_seasonalSnowPlacedEpoch, CurrentWinterEpoch);
            zdo.Set(SeasonsVars.s_seasonalSnowPlacedAt, ToTimestamp(ZNet.instance.GetTimeSeconds()));
        }

        internal static bool IsCurrentWinterPlacement(ZDO zdo) => zdo != null &&
            zdo.GetLong(SeasonsVars.s_seasonalSnowPlacedEpoch, MissingEpoch) == CurrentWinterEpoch;

        internal static double PlacementTime(ZDO zdo) =>
            FromTimestamp(zdo.GetLong(SeasonsVars.s_seasonalSnowPlacedAt, 0L));
        internal static bool RemoveSavedValue(ZDO zdo)
        {
            bool changed = zdo.RemoveFloat(SeasonsVars.s_seasonalSnowValue);
            changed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowEpoch);
            return changed;
        }
    }

}
