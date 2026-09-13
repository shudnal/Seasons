using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class ControlledComponentsExtentions
    {
        public static string Localize(this string text) => Localization.instance.Localize(text);

        public static bool ShouldBePickedInWinter(this Pickable pickable)
        {
            return pickable.CanBePicked()
                && !pickable.GetPicked()
                && pickable.IsVulnerableToWinter()
                && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value
                && !pickable.IsProtectedPosition()
                && !pickable.ProtectedWithHeat();
        }

        public static bool IsVulnerableToWinter(this Pickable pickable)
        {
            return seasonState.GetPlantsGrowthMultiplier() == 0f &&
                    seasonState.GetCurrentSeason() == Season.Winter
                    && !pickable.ShouldSurviveWinter()
                    && !pickable.SurvivedCurrentWinter();
        }

        public static bool SurvivedCurrentWinter(this Pickable pickable)
        {
            return pickable.m_nview 
                && pickable.m_nview.IsValid() 
                && seasonState.GetCurrentSeason() == Season.Winter
                && Mathf.Abs(pickable.m_nview.GetZDO().GetInt(SeasonsVars.s_cropSurvivedWinterDayHash, 0) - seasonState.GetCurrentWorldDay()) <= seasonState.GetDaysInSeason();
        }

        public static bool IsFreezingToDeath(this Pickable pickable)
        {
            return pickable.m_nview
                && pickable.m_nview.IsValid()
                && seasonState.GetCurrentSeason() == Season.Winter
                && pickable.GetSecondsToFreeze() > 0;
        }

        public static double GetSecondsToFreeze(this Pickable pickable)
        {
            if (pickable.m_nview && pickable.m_nview.IsValid() && ZNet.instance)
            {
                long freezingTime = pickable.m_nview.GetZDO().GetLong(SeasonsVars.s_cropStartedFreezingHash, 0L);
                if (freezingTime <= 0)
                    return 0d;

                float secondsToFreeze = secondsToFreezeForCropInWinter.Value;
                if (secondsToFreeze % 60f == 0)
                    secondsToFreeze -= 2f;

                TimeSpan timeSpan = new DateTime(freezingTime).AddSeconds(secondsToFreeze) - ZNet.instance.GetTime();
                return timeSpan.TotalSeconds;
            }

            return 0d;
        }

        public static bool CheckForPerishInWinter(this Pickable pickable)
        {
            if (!pickable.ShouldBePickedInWinter())
            {
                pickable.SetFreezing(false);
                return false;
            }

            if (secondsToFreezeForCropInWinter.Value > 0)
                pickable.SetFreezing(true);

            if (pickable.IsFreezingToDeath())
                return false;

            pickable.StartCoroutine(PickableSetPickedInWinter(pickable));
            return true;
        }

        public static void SetFreezing(this Pickable pickable, bool freezing)
        {
            if (pickable.m_nview && pickable.m_nview.IsValid() && ZNet.instance && pickable.m_nview.GetZDO() is ZDO zdo)
            {
                if (freezing && zdo.GetLong(SeasonsVars.s_cropStartedFreezingHash, 0L) == 0L && seasonState.GetCurrentSeason() == Season.Winter && seasonState.GetCurrentDay() >= cropsDiesAfterSetDayInWinter.Value)
                    zdo.Set(SeasonsVars.s_cropStartedFreezingHash, ZNet.instance.GetTime().Ticks);
                else if (!freezing)
                    zdo.Set(SeasonsVars.s_cropStartedFreezingHash, 0L);
            }
        }

        public static bool IsIgnored(this Pickable pickable)
        {
            return pickable.m_nview == null ||
                  !pickable.m_nview.IsValid() ||
                  pickable.m_nview.HasOwner() && !pickable.m_nview.IsOwner() ||
                  !pickable.ControlPlantGrowth() ||
                  pickable.IsIgnoredPosition();
        }

        public static string GetColdStatus(this Pickable pickable)
        {
            if (pickable.ShouldSurviveWinter())
                return "$seasons_plant_frost_resistant";
            else if (pickable.ProtectedWithHeat())
                return "$seasons_plant_heat_protected";
            else if (pickable.SurvivedCurrentWinter())
                return "$seasons_plant_survived_winter";
            else if (pickable.GetSecondsToFreeze() is double seconds && seconds != 0d && secondsToFreezeForCropInWinter.Value > 0)
            {
                if (seconds > 0)
                    return $"$seasons_plant_is_freezing\n{FromPercent(seconds / secondsToFreezeForCropInWinter.Value)}";
                else
                    return "$seasons_plant_is_frozen";
            }
            else if (seasonState.GetCurrentDay() > cropsDiesAfterSetDayInWinter.Value)
                return "$seasons_plant_will_perish";
            else
                return "$seasons_plant_is_exposed";
        }

        public static bool ControlPlantGrowth(this MonoBehaviour behaviour) => Seasons.ControlPlantGrowth(behaviour.gameObject);

        public static bool ShouldSurviveWinter(this MonoBehaviour behaviour) => Seasons.PlantWillSurviveWinter(behaviour.gameObject);

        public static bool IsIgnoredPosition(this MonoBehaviour behaviour) => Seasons.IsIgnoredPosition(behaviour.transform.position);

        public static bool IsProtectedPosition(this MonoBehaviour behaviour) => Seasons.IsProtectedPosition(behaviour.transform.position);

        public static bool ProtectedWithHeat(this MonoBehaviour behaviour) => Seasons.ProtectedWithHeat(behaviour.transform.position);

    }

    internal static class SeasonalSnowCraftingStationMelt
    {
        private const float SnowChangeEpsilon = 0.0001f;
        private const float SnowRpcStep = 0.01f;
        private const float DefaultCraftingStationMeltMultiplier = 5f;
        private const double TimeStampScale = 1000d;

        private static readonly FieldInfo CraftingStationMeltMultiplierField =
            AccessTools.Field(typeof(SeasonalSnowRuntimeState), "craftingStationMeltMultiplier");

        private sealed class StationMeltState
        {
            public float pendingMelt;
            public float lastPokeTime = -1f;
        }

        private static readonly ConditionalWeakTable<CraftingStation, StationMeltState> StationMeltStates =
            new ConditionalWeakTable<CraftingStation, StationMeltState>();

        private static float CraftingStationMeltMultiplier
        {
            get
            {
                ConfigEntry<float> entry = CraftingStationMeltMultiplierField?.GetValue(null) as ConfigEntry<float>;
                return Mathf.Max(0f, entry?.Value ?? DefaultCraftingStationMeltMultiplier);
            }
        }

        private static WearNTear GetWearNTear(CraftingStation station)
        {
            if (!station)
                return null;

            WearNTear wearNTear = station.GetComponent<WearNTear>();
            if (!wearNTear)
                wearNTear = station.GetComponentInParent<WearNTear>();
            if (!wearNTear)
                wearNTear = station.GetComponentInChildren<WearNTear>(true);

            return wearNTear;
        }

        private static void ResetStationMeltState(CraftingStation station)
        {
            if (!station || !StationMeltStates.TryGetValue(station, out StationMeltState state))
                return;

            state.pendingMelt = 0f;
            state.lastPokeTime = -1f;
        }

        private static long ToTimeStamp(double seconds)
        {
            return (long)Math.Floor(Math.Max(0d, seconds) * TimeStampScale);
        }

        private static bool CanTrackSeasonalSnow(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.GetZDO() != null
                && SeasonState.IsActive
                && seasonState.GetCurrentSeason() == Season.Winter
                && SeasonalSnow.IsSeasonalSnowPosition(instance);
        }

        private static void RecordSnowDecrease(WearNTear instance, float snowBefore)
        {
            if (!CanTrackSeasonalSnow(instance) || !instance.m_nview.IsOwner() || ZNet.instance == null)
                return;

            float current = instance.m_snowBuildup;
            if (current >= snowBefore - SnowChangeEpsilon)
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            long winterId = ToTimeStamp(seasonState.GetStartOfCurrentSeason()) + 1L;
            long now = ToTimeStamp(ZNet.instance.GetTimeSeconds());

            zdo.Set(SeasonsVars.s_seasonalSnowWinter, winterId);
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, now);
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, Mathf.Max(0f, current));

            if (current > SnowChangeEpsilon)
                zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
            else
                zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);

            Vector2 range = SeasonalSnow.GetSnowBuildupRange(instance);
            if (current + SnowChangeEpsilon < range.x)
                zdo.Set(SeasonsVars.s_seasonalSnowMeltedBelowMinimum, true);
            else
                zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);

            instance.UpdateSnowVisual();
        }

        [HarmonyPatch(typeof(SeasonalSnowRuntimeState), "IsCraftingStationInUse")]
        private static class SeasonalSnowRuntimeState_IsCraftingStationInUse_DisableOwnerTracking
        {
            [HarmonyPrefix]
            private static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.PokeInUse))]
        private static class CraftingStation_PokeInUse_SeasonalSnowMelt
        {
            [HarmonyPostfix]
            private static void Postfix(CraftingStation __instance)
            {
                Player player = Player.m_localPlayer;
                if (!player || player.GetCurrentCraftingStation() != __instance
                    || !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter
                    || Game.instance == null)
                {
                    ResetStationMeltState(__instance);
                    return;
                }

                WearNTear wearNTear = GetWearNTear(__instance);
                if (!CanTrackSeasonalSnow(wearNTear) || wearNTear.m_snowBuildup <= SnowChangeEpsilon)
                {
                    ResetStationMeltState(__instance);
                    return;
                }

                float multiplier = CraftingStationMeltMultiplier;
                if (multiplier <= 0f)
                {
                    ResetStationMeltState(__instance);
                    return;
                }

                StationMeltState state = StationMeltStates.GetValue(__instance, _ => new StationMeltState());
                float now = Time.time;
                if (state.lastPokeTime < 0f || now <= state.lastPokeTime)
                {
                    state.lastPokeTime = now;
                    return;
                }

                float deltaTime = now - state.lastPokeTime;
                state.lastPokeTime = now;

                // PokeInUse normally runs every frame while the station GUI is open. Do not
                // count an inactive gap when the player returns to the station later.
                if (deltaTime > 0.25f)
                    deltaTime = Time.deltaTime;

                state.pendingMelt += deltaTime
                    * Game.instance.m_snowBuildupSpeed
                    * SeasonalSnow.SnowAccumulationSpeed
                    * multiplier;

                float availableSnow = Mathf.Max(0f, wearNTear.m_snowBuildup);
                bool meltToZero = state.pendingMelt + SnowChangeEpsilon >= availableSnow;
                int steps = Mathf.FloorToInt((state.pendingMelt + SnowChangeEpsilon) / SnowRpcStep);

                if (!meltToZero && steps <= 0)
                    return;

                float change = meltToZero
                    ? availableSnow
                    : Mathf.Min(availableSnow, steps * SnowRpcStep);

                if (change <= SnowChangeEpsilon)
                    return;

                state.pendingMelt = meltToZero ? 0f : Mathf.Max(0f, state.pendingMelt - change);

                // ChangeSnow updates the local visual immediately and routes RPC_SetSnow to
                // the current ZDO owner, which persists the value and replicates it normally.
                wearNTear.ChangeSnow(-change);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_SetSnow))]
        private static class WearNTear_RPC_SetSnow_SeasonalSnowBaseline
        {
            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance, ref float __state)
            {
                __state = __instance ? __instance.m_snowBuildup : 0f;
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance, float __state)
            {
                RecordSnowDecrease(__instance, __state);
            }
        }
    }
}
