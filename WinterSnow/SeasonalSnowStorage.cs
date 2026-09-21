using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>Separates seasonal save data from native Deep North snow.</summary>
    internal static class SeasonalSnowStorage
    {
        private const long MissingEpoch = long.MinValue;

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

        // Transitional routing for the existing native producer. Its simulation will be
        // replaced separately; no global ZDOVars constant or ZDO method is patched.
        internal static int GetKey(WearNTear piece)
        {
            if (!SeasonalSnow.IsSeasonalSnowPosition(piece))
                return ZDOVars.s_snow;

            MigrateLoaded(piece);
            ZDO zdo = piece.m_nview.GetZDO();
            // A remote owner must perform migration. Until its snapshot arrives, preserve
            // the legacy read without claiming ownership or publishing on its behalf.
            if (!HasSavedValue(zdo) && HasLegacyState(zdo) && !CanWrite(piece.m_nview, zdo))
                return ZDOVars.s_snow;
            return SeasonsVars.s_seasonalSnowValue;
        }

        internal static bool RemoveSavedValue(ZDO zdo)
        {
            bool changed = zdo.RemoveFloat(SeasonsVars.s_seasonalSnowValue);
            changed |= zdo.RemoveLong(SeasonsVars.s_seasonalSnowEpoch);
            return changed;
        }
    }

    [HarmonyPatch]
    internal static class WearNTear_SeasonalSnowStorage
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.Awake));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.UpdateWear));
            yield return AccessTools.Method(typeof(WearNTear), nameof(WearNTear.RPC_SetSnow));
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            FieldInfo snowKey = AccessTools.Field(typeof(ZDOVars), nameof(ZDOVars.s_snow));
            MethodInfo getKey = AccessTools.Method(typeof(SeasonalSnowStorage), nameof(SeasonalSnowStorage.GetKey));
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(snowKey))
                {
                    // Replace the hash load with a per-piece choice; preserve entry labels.
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, getKey);
                    found = true;
                }
                else
                    yield return instruction;
            }
            if (!found)
                LogWarning($"Failed to route seasonal snow storage in WearNTear.{original.Name}.");
        }
    }

}
