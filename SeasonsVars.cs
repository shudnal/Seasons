using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public static class SeasonsVars
    {
        public const string s_cropSurvivedWinterDayName = "Seasons_Survived_Winter_Day";
        public static int s_cropSurvivedWinterDayHash = s_cropSurvivedWinterDayName.GetStableHashCode();

        public const string s_cropStartedFreezingName = "Seasons_Started_Freezing";
        public static int s_cropStartedFreezingHash = s_cropStartedFreezingName.GetStableHashCode();

        public static int s_treeRegrowthHaveGrowSpace = "Seasons_HaveGrowSpace".GetStableHashCode();

        public const string s_statusEffectSeasonName = "Season";
        public static int s_statusEffectSeasonHash = s_statusEffectSeasonName.GetStableHashCode();

        public const string s_statusEffectOverheatName = "Overheat";
        public static int s_statusEffectOverheatHash = s_statusEffectOverheatName.GetStableHashCode();

        public const string s_statusEffectSummerHeatName = "SummerHeat";
        public static int s_statusEffectSummerHeatHash = s_statusEffectSummerHeatName.GetStableHashCode();

        public static int s_iceFloeWatermark = "Seasons_IceFloe".GetStableHashCode();
        public static int s_iceFloeMass = "Seasons_IceFloeMass".GetStableHashCode();
        public static int s_iceFloesSpawned = "Seasons_IceFloesSpawned".GetStableHashCode();

        public static int s_seasonalSnowWatermark = "Seasons_SeasonalSnow".GetStableHashCode();
        public static int s_seasonalSnowWinter = "Seasons_SeasonalSnowWinter".GetStableHashCode();
        public static int s_seasonalSnowFrom = "Seasons_SeasonalSnowFrom".GetStableHashCode();
        public static int s_seasonalSnowBaseline = "Seasons_SeasonalSnowBaseline".GetStableHashCode();
        public static int s_seasonalSnowMeltedBelowMinimum = "Seasons_SeasonalSnowMeltedBelowMinimum".GetStableHashCode();

        public static int s_terrainDecultivated = "Seasons_Terrain_Decultivated".GetStableHashCode();
    }

    internal static class SeasonalSnowRuntimeState
    {
        private const float SnowChangeEpsilon = 0.0001f;
        private const float NearbyHeatDistance = 4f;
        private const float DefaultCraftingStationMeltMultiplier = 5f;
        private const double TimeStampScale = 1000d;

        private static readonly int CraftingAnimationHash = ZSyncAnimation.GetHash("crafting");
        private static readonly MethodInfo FindBiomeMethod =
            AccessTools.Method(typeof(Heightmap), nameof(Heightmap.FindBiome), new[] { typeof(Vector3) });
        private static readonly MethodInfo FireplaceSnowMeltScheduleMethod =
            AccessTools.Method(typeof(SeasonalSnowRuntimeState), nameof(IsFireplaceSnowMeltSupportedPosition));

        private static ConfigEntry<float> craftingStationMeltMultiplier;

        private sealed class PieceMeltSourceState
        {
            public readonly EffectArea[] heatAreas;
            public readonly CraftingStation craftingStation;
            public float nextSnowCoverCheckTime;
            public bool haveSnowRoof;

            public PieceMeltSourceState(WearNTear instance)
            {
                heatAreas = instance ? instance.GetComponentsInChildren<EffectArea>(true) : Array.Empty<EffectArea>();
                if (instance)
                {
                    craftingStation = instance.GetComponent<CraftingStation>();
                    if (!craftingStation)
                        craftingStation = instance.GetComponentInChildren<CraftingStation>(true);
                }
            }
        }

        private static readonly ConditionalWeakTable<WearNTear, PieceMeltSourceState> PieceMeltSources =
            new ConditionalWeakTable<WearNTear, PieceMeltSourceState>();

        private static float CraftingStationMeltMultiplier =>
            Mathf.Max(0f, craftingStationMeltMultiplier?.Value ?? DefaultCraftingStationMeltMultiplier);

        private static void EnsureConfig()
        {
            if (craftingStationMeltMultiplier != null || Seasons.instance == null)
                return;

            craftingStationMeltMultiplier = Seasons.configSync.AddConfigEntry(
                Seasons.instance.Config,
                "Season - Winter snow",
                "Crafting station snow melt multiplier",
                DefaultCraftingStationMeltMultiplier,
                new ConfigDescription("Multiplier for real-time seasonal snow melting on a crafting station while any player is using it."),
                syncMode: ConditionalConfigSync.ConfigSyncMode.AlwaysServerControlled,
                serverControlledByDefault: true).SourceConfig;
        }

        private static bool CanOwnSnowState(WearNTear instance)
        {
            return instance
                && instance.m_nview != null
                && instance.m_nview.IsValid()
                && instance.m_nview.IsOwner()
                && instance.m_nview.GetZDO() != null;
        }

        private static Heightmap.Biome GetBiome(WearNTear instance)
        {
            if (!instance)
                return Heightmap.Biome.None;

            if (instance.m_biome == Heightmap.Biome.None)
                instance.UpdateBiome();

            if (instance.m_biome != Heightmap.Biome.None)
                return instance.m_biome;

            return WorldGenerator.instance == null
                ? Heightmap.Biome.None
                : WorldGenerator.instance.GetBiome(instance.transform.position);
        }

        private static bool IsDeepNorth(WearNTear instance) => GetBiome(instance) == Heightmap.Biome.DeepNorth;

        private static long ToTimeStamp(double seconds)
        {
            return (long)Math.Floor(Math.Max(0d, seconds) * TimeStampScale);
        }

        private static double FromTimeStamp(long value)
        {
            return Math.Max(0d, value) / TimeStampScale;
        }

        private static long GetCurrentWinterId()
        {
            if (!SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter)
                return 0L;

            return ToTimeStamp(seasonState.GetStartOfCurrentSeason()) + 1L;
        }

        private static long GetCurrentTimeStamp()
        {
            if (ZNet.instance == null)
                return 0L;

            return ToTimeStamp(ZNet.instance.GetTimeSeconds());
        }

        private static bool HasCurrentWinterState(WearNTear instance)
        {
            if (!CanOwnSnowState(instance))
                return false;

            long winterId = GetCurrentWinterId();
            return winterId != 0L
                && instance.m_nview.GetZDO().GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) == winterId;
        }

        private static long EnsureCurrentWinterState(WearNTear instance)
        {
            if (!CanOwnSnowState(instance))
                return 0L;

            long winterId = GetCurrentWinterId();
            if (winterId == 0L)
                return 0L;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo.GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) != winterId)
            {
                zdo.Set(SeasonsVars.s_seasonalSnowWinter, winterId);
                zdo.Set(SeasonsVars.s_seasonalSnowFrom, ToTimeStamp(SeasonalSnow.TimelineStartSeconds));
                zdo.Set(SeasonsVars.s_seasonalSnowBaseline, 0f);
                zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            }

            return winterId;
        }

        private static void SetAccumulationBaselineNow(WearNTear instance, bool meltedBelowMinimum)
        {
            if (EnsureCurrentWinterState(instance) == 0L)
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, GetCurrentTimeStamp());
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, Mathf.Max(0f, instance.m_snowBuildup));

            if (meltedBelowMinimum)
                zdo.Set(SeasonsVars.s_seasonalSnowMeltedBelowMinimum, true);
            else
                zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
        }

        private static void SetSnowValue(WearNTear instance, float value, bool markSeasonalSnow, bool meltedBelowMinimum)
        {
            if (!instance)
                return;

            Vector2 range = SeasonalSnow.GetSnowBuildupRange(instance);
            value = Mathf.Clamp(value, 0f, range.y);
            instance.m_snowBuildup = value;

            if (CanOwnSnowState(instance))
            {
                ZDO zdo = instance.m_nview.GetZDO();
                zdo.Set(ZDOVars.s_snow, value);

                if (markSeasonalSnow && value > SnowChangeEpsilon)
                    zdo.Set(SeasonsVars.s_seasonalSnowWatermark, true);
                else
                    zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);

                if (meltedBelowMinimum)
                    zdo.Set(SeasonsVars.s_seasonalSnowMeltedBelowMinimum, true);
                else
                    zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            }

            instance.UpdateSnowVisual();
        }

        private static bool IsActiveHeatArea(EffectArea area)
        {
            return area
                && area.enabled
                && area.gameObject.activeInHierarchy
                && area.m_isHeatType
                && area.m_collider != null
                && area.m_collider.enabled
                && area.m_collider.gameObject.activeInHierarchy;
        }

        private static bool IsInsideHeatArea(Vector3 position)
        {
            EffectArea area = EffectArea.IsPointInsideArea(position, EffectArea.Type.Heat);
            return IsActiveHeatArea(area);
        }

        private static PieceMeltSourceState GetMeltSourceState(WearNTear instance)
        {
            return instance ? PieceMeltSources.GetValue(instance, key => new PieceMeltSourceState(key)) : null;
        }

        private static bool HasActiveInternalHeatArea(WearNTear instance)
        {
            PieceMeltSourceState state = GetMeltSourceState(instance);
            if (state == null)
                return false;

            foreach (EffectArea area in state.heatAreas)
                if (IsActiveHeatArea(area))
                    return true;

            return false;
        }

        private static bool IsNearHeatArea(Vector3 position)
        {
            foreach (EffectArea area in EffectArea.GetAllAreas())
            {
                if (!IsActiveHeatArea(area))
                    continue;

                if ((position - area.transform.position).sqrMagnitude < NearbyHeatDistance * NearbyHeatDistance)
                    return true;
            }

            return false;
        }

        private static CraftingStation GetCraftingStation(WearNTear instance)
        {
            return GetMeltSourceState(instance)?.craftingStation;
        }

        private static bool IsCraftingStationInUse(CraftingStation station)
        {
            if (!station)
                return false;

            if (station.m_useTimer < 1f)
                return true;

            foreach (Player player in Player.s_players)
            {
                if (!player)
                    continue;

                if (player.GetCurrentCraftingStation() == station)
                    return true;

                if (!station.InUseDistance(player))
                    continue;

                if (player.m_inCraftingStation)
                    return true;

                Animator animator = player.m_zanim?.m_animator;
                if (animator != null && station.m_useAnimation != 0
                    && animator.GetInteger(CraftingAnimationHash) == station.m_useAnimation)
                    return true;
            }

            return false;
        }

        private static bool IsBlockedFromSnow(WearNTear instance)
        {
            if (!instance)
                return true;

            PieceMeltSourceState state = GetMeltSourceState(instance);
            if (state == null)
                return true;

            if (ShieldGenerator.IsInsideShieldCached(instance.transform.position, ref instance.m_shieldChangeID))
                return true;

            if (Time.time < state.nextSnowCoverCheckTime)
                return state.haveSnowRoof;

            state.nextSnowCoverCheckTime = Time.time + 5f;
            state.haveSnowRoof = instance.HaveRoof();
            instance.m_haveRoof = state.haveSnowRoof;
            return state.haveSnowRoof;
        }

        private static float GetMeltMultiplier(WearNTear instance)
        {
            if (!instance || !SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter)
                return 0f;

            EnsureConfig();

            Vector3 position = instance.transform.position;
            float multiplier = 0f;

            if (IsInsideHeatArea(position) && IsBlockedFromSnow(instance))
                multiplier = 1f;

            if (HasActiveInternalHeatArea(instance))
                multiplier = Mathf.Max(multiplier, 1f);

            if (IsNearHeatArea(position))
                multiplier = Mathf.Max(multiplier, 1f);

            CraftingStation station = GetCraftingStation(instance);
            if (station && IsCraftingStationInUse(station))
                multiplier = Mathf.Max(multiplier, CraftingStationMeltMultiplier);

            return multiplier;
        }

        private static float GetPredictedSnowGain(float snowBuildup, double seconds)
        {
            if (snowBuildup <= 0f || seconds <= 0d || Game.instance == null)
                return 0f;

            float wearUpdates = (float)(seconds / WearNTearUpdater.c_WearNTearTime);
            return snowBuildup
                * wearUpdates
                * SeasonalSnow.PredictedWearUpdateDelta
                * Game.instance.m_snowBuildupSpeed
                * SeasonalSnow.SnowAccumulationSpeed;
        }

        private static float GetCumulativeGainAt(Heightmap.Biome biome, double seconds)
        {
            if (!SeasonalSnow.SeasonalSnowTimelines.TryGetValue(biome, out SeasonalSnow.BiomeSnowTimeline timeline)
                || timeline.cumulativeSnowGain.Length == 0)
                return 0f;

            if (seconds <= SeasonalSnow.TimelineStartSeconds)
                return 0f;

            if (seconds >= SeasonalSnow.TimelineEndSeconds)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            long duration = Math.Max(1L, SeasonalSnow.EnvironmentDuration);
            long environmentPeriod = (long)Math.Floor(seconds / duration);
            long indexLong = environmentPeriod - SeasonalSnow.FirstEnvironmentPeriod;
            if (indexLong < 0L)
                return 0f;

            if (indexLong >= timeline.cumulativeSnowGain.Length)
                return timeline.cumulativeSnowGain[timeline.cumulativeSnowGain.Length - 1];

            int index = (int)indexLong;
            float previousGain = index > 0 ? timeline.cumulativeSnowGain[index - 1] : 0f;
            double periodStart = environmentPeriod * (double)duration;
            double overlapStart = Math.Max(periodStart, SeasonalSnow.TimelineStartSeconds);
            double overlapEnd = Math.Min(seconds, SeasonalSnow.TimelineEndSeconds);
            float partial = GetPredictedSnowGain(
                timeline.snowBuildup[index],
                Math.Max(0d, overlapEnd - overlapStart));

            return Mathf.Max(0f, previousGain + partial);
        }

        private static float GetPersonalSnowGain(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || ZNet.instance == null)
                return 0f;

            ZDO zdo = instance.m_nview.GetZDO();
            long fromStamp = zdo.GetLong(
                SeasonsVars.s_seasonalSnowFrom,
                ToTimeStamp(SeasonalSnow.TimelineStartSeconds));

            double from = FromTimeStamp(fromStamp);
            double now = ZNet.instance.GetTimeSeconds();
            Heightmap.Biome biome = GetBiome(instance);

            return Mathf.Max(0f, GetCumulativeGainAt(biome, now) - GetCumulativeGainAt(biome, from));
        }

        private static bool IsPreWinterPiece(WearNTear instance)
        {
            if (!CanOwnSnowState(instance))
                return false;

            long from = instance.m_nview.GetZDO().GetLong(SeasonsVars.s_seasonalSnowFrom, long.MaxValue);
            long winterStart = ToTimeStamp(SeasonalSnow.TimelineStartSeconds);
            return from <= winterStart;
        }

        private static float GetPersonalSnowTarget(WearNTear instance, bool requireGain)
        {
            Vector2 range = SeasonalSnow.GetSnowBuildupRange(instance);
            float gain = GetPersonalSnowGain(instance);
            bool preWinterPiece = IsPreWinterPiece(instance);
            float baseline = CanOwnSnowState(instance)
                ? Mathf.Max(0f, instance.m_nview.GetZDO().GetFloat(SeasonsVars.s_seasonalSnowBaseline, 0f))
                : 0f;

            if (requireGain && gain <= SnowChangeEpsilon && !preWinterPiece)
                return 0f;

            if (preWinterPiece)
                baseline = Mathf.Max(baseline, range.x);
            else if (baseline + SnowChangeEpsilon < range.x)
                baseline = gain > SnowChangeEpsilon ? range.x : 0f;

            if (baseline <= SnowChangeEpsilon)
                return 0f;

            return Mathf.Clamp(baseline + gain, 0f, range.y);
        }

        private static void ApplyPassiveSeasonalSnow(WearNTear instance, bool forceTarget)
        {
            _ = forceTarget;

            if (!SeasonState.IsActive || !CanOwnSnowState(instance) || !SeasonalSnow.SupportsSeasonalSnow(instance))
                return;

            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(instance.transform.position))
                return;

            if (IsDeepNorth(instance) || !SeasonalSnow.IsSeasonalSnowPosition(instance))
                return;

            bool hadCurrentWinterState = HasCurrentWinterState(instance);
            if (EnsureCurrentWinterState(instance) == 0L)
                return;

            float meltMultiplier = GetMeltMultiplier(instance);
            if (meltMultiplier > 0f)
            {
                SetAccumulationBaselineNow(
                    instance,
                    instance.m_nview.GetZDO().GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum));
                return;
            }

            if (IsBlockedFromSnow(instance))
            {
                if (instance.m_snowBuildup > SnowChangeEpsilon)
                    SetSnowValue(instance, 0f, markSeasonalSnow: false, meltedBelowMinimum: false);

                SetAccumulationBaselineNow(instance, meltedBelowMinimum: false);
                return;
            }

            float gain = GetPersonalSnowGain(instance);
            if (!hadCurrentWinterState && IsPreWinterPiece(instance))
            {
                // Existing pieces start winter with the configured visual minimum even if
                // no snowfall has occurred yet.
            }
            else if (gain <= SnowChangeEpsilon)
            {
                return;
            }

            float target = GetPersonalSnowTarget(instance, requireGain: hadCurrentWinterState);
            if (target <= SnowChangeEpsilon)
                return;

            if (instance.m_snowBuildup + SnowChangeEpsilon < target)
                SetSnowValue(instance, target, markSeasonalSnow: true, meltedBelowMinimum: false);
        }

        private static void MarkPlacedDuringWinter(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || !SeasonalSnow.SupportsSeasonalSnow(instance) || IsDeepNorth(instance)
                || !SeasonalSnow.IsSeasonalSnowPosition(instance))
                return;

            EnsureCurrentWinterState(instance);

            ZDO zdo = instance.m_nview.GetZDO();
            zdo.Set(SeasonsVars.s_seasonalSnowFrom, GetCurrentTimeStamp());
            zdo.Set(SeasonsVars.s_seasonalSnowBaseline, 0f);
            zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            SetSnowValue(instance, 0f, markSeasonalSnow: false, meltedBelowMinimum: false);
        }

        private static void ClearTrackedInstance(WearNTear instance)
        {
            if (!CanOwnSnowState(instance) || IsDeepNorth(instance))
                return;

            ZDO zdo = instance.m_nview.GetZDO();
            if (zdo.GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) == 0L)
                return;

            instance.m_snowBuildup = 0f;
            zdo.Set(ZDOVars.s_snow, 0f);
            zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);
            zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
            zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter);
            zdo.RemoveLong(SeasonsVars.s_seasonalSnowFrom);
            zdo.RemoveFloat(SeasonsVars.s_seasonalSnowBaseline);
            instance.UpdateSnowVisual();
        }

        private static void ClearTrackedSeasonalSnow()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && ZDOMan.instance != null)
            {
                foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
                {
                    if (zdo == null || !zdo.IsValid()
                        || zdo.GetLong(SeasonsVars.s_seasonalSnowWinter, 0L) == 0L)
                        continue;

                    if (WorldGenerator.instance != null
                        && WorldGenerator.instance.GetBiome(zdo.GetPosition()) == Heightmap.Biome.DeepNorth)
                        continue;

                    zdo.Set(ZDOVars.s_snow, 0f);
                    zdo.RemoveBool(SeasonsVars.s_seasonalSnowWatermark);
                    zdo.RemoveBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum);
                    zdo.RemoveLong(SeasonsVars.s_seasonalSnowWinter);
                    zdo.RemoveLong(SeasonsVars.s_seasonalSnowFrom);
                    zdo.RemoveFloat(SeasonsVars.s_seasonalSnowBaseline);
                }
            }

            foreach (WearNTear instance in WearNTear.GetAllInstances().ToArray())
                ClearTrackedInstance(instance);
        }

        private static bool IsFireplaceSnowMeltSupportedPosition(Vector3 position)
        {
            Heightmap.Biome biome = Heightmap.FindBiome(position);
            return biome == Heightmap.Biome.DeepNorth
                || (biome != Heightmap.Biome.None && biome != Heightmap.Biome.AshLands);
        }

        private static bool ShouldRunFireplaceSnowMelt(Fireplace fireplace)
        {
            if (!fireplace)
                return false;

            Vector3 position = fireplace.transform.position;
            Heightmap.Biome biome = Heightmap.FindBiome(position);
            if (biome == Heightmap.Biome.DeepNorth)
                return true;

            return SeasonState.IsActive
                && SeasonalSnow.Enabled
                && controlEnvironments.Value
                && seasonState.GetCurrentSeason() == Season.Winter
                && biome != Heightmap.Biome.None
                && biome != Heightmap.Biome.AshLands
                && !IsIgnoredPosition(position);
        }

        private static bool TryPatchFireplaceSnowMeltCondition(List<CodeInstruction> codes)
        {
            if (FindBiomeMethod == null || FireplaceSnowMeltScheduleMethod == null)
                return false;

            for (int i = 0; i < codes.Count - 2; ++i)
            {
                CodeInstruction findBiome = codes[i];
                if ((findBiome.opcode != OpCodes.Call && findBiome.opcode != OpCodes.Callvirt)
                    || !Equals(findBiome.operand, FindBiomeMethod))
                    continue;

                if (!codes[i + 1].LoadsConstant((long)(int)Heightmap.Biome.DeepNorth))
                    continue;

                CodeInstruction branch = codes[i + 2];
                findBiome.opcode = OpCodes.Call;
                findBiome.operand = FireplaceSnowMeltScheduleMethod;
                codes[i + 1].opcode = OpCodes.Nop;
                codes[i + 1].operand = null;

                if (branch.opcode == OpCodes.Bne_Un)
                    branch.opcode = OpCodes.Brfalse;
                else if (branch.opcode == OpCodes.Bne_Un_S)
                    branch.opcode = OpCodes.Brfalse_S;
                else if (branch.opcode == OpCodes.Beq)
                    branch.opcode = OpCodes.Brtrue;
                else if (branch.opcode == OpCodes.Beq_S)
                    branch.opcode = OpCodes.Brtrue_S;
                else
                    continue;

                return true;
            }

            return false;
        }

        [HarmonyPatch(typeof(SeasonalSnow), "TryApplyPassiveSeasonalSnow")]
        private static class SeasonalSnow_TryApplyPassiveSeasonalSnow_RuntimeState
        {
            [HarmonyPrepare]
            private static bool Prepare()
            {
                EnsureConfig();
                return true;
            }

            [HarmonyPrefix]
            private static bool Prefix(WearNTear instance, bool forceTarget)
            {
                ApplyPassiveSeasonalSnow(instance, forceTarget);
                return false;
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), "TryInitializeSeasonalSnowOnStart")]
        private static class SeasonalSnow_TryInitializeSeasonalSnowOnStart_RuntimeState
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear instance)
            {
                ApplyPassiveSeasonalSnow(instance, forceTarget: false);
                return false;
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), "IsSnowBlocked")]
        private static class SeasonalSnow_IsSnowBlocked_RuntimeState
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear instance, ref bool __result)
            {
                __result = IsBlockedFromSnow(instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), "TryClearCoveredSeasonalSnow")]
        private static class SeasonalSnow_TryClearCoveredSeasonalSnow_GradualMelt
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear instance, ref bool __result)
            {
                if (!CanOwnSnowState(instance)
                    || !SeasonState.IsActive
                    || seasonState.GetCurrentSeason() != Season.Winter
                    || IsDeepNorth(instance)
                    || !SeasonalSnow.IsSeasonalSnowPosition(instance))
                    return true;

                if (GetMeltMultiplier(instance) <= 0f)
                    return true;

                __result = false;
                return false;
            }

            [HarmonyPostfix]
            private static void Postfix(WearNTear instance, bool __result)
            {
                if (__result
                    && SeasonState.IsActive
                    && seasonState.GetCurrentSeason() == Season.Winter
                    && CanOwnSnowState(instance)
                    && !IsDeepNorth(instance))
                {
                    SetAccumulationBaselineNow(instance, meltedBelowMinimum: false);
                }
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), nameof(SeasonalSnow.ClampSnowBuildup), new Type[] { typeof(WearNTear), typeof(float) })]
        private static class SeasonalSnow_ClampSnowBuildup_AllowMeltingBelowMinimum
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear instance, float value, ref float __result)
            {
                if (!CanOwnSnowState(instance) || !HasCurrentWinterState(instance))
                    return true;

                ZDO zdo = instance.m_nview.GetZDO();
                if (!zdo.GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum))
                    return true;

                Vector2 range = SeasonalSnow.GetSnowBuildupRange(instance);
                if (value >= range.x)
                    return true;

                __result = Mathf.Clamp(value, 0f, range.y);
                return false;
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.OnPlaced))]
        private static class WearNTear_OnPlaced_SeasonalSnowRuntimeState
        {
            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance)
            {
                MarkPlacedDuringWinter(__instance);
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
        private static class WearNTear_UpdateWear_SeasonalSnowMelting
        {
            [HarmonyPrefix]
            private static void Prefix(WearNTear __instance, ref float __state)
            {
                __state = __instance ? __instance.m_snowBuildup : 0f;
            }

            [HarmonyPostfix]
            [HarmonyPriority(HarmonyLib.Priority.Last)]
            private static void Postfix(WearNTear __instance, float __state)
            {
                if (!CanOwnSnowState(__instance) || !SeasonalSnow.SupportsSeasonalSnow(__instance) || IsDeepNorth(__instance))
                    return;

                if (!SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter
                    || !SeasonalSnow.IsSeasonalSnowPosition(__instance))
                {
                    ClearTrackedInstance(__instance);
                    return;
                }

                if (EnsureCurrentWinterState(__instance) == 0L)
                    return;

                float meltMultiplier = GetMeltMultiplier(__instance);
                if (meltMultiplier > 0f && Game.instance != null)
                {
                    Vector2 range = SeasonalSnow.GetSnowBuildupRange(__instance);
                    float melt = Time.deltaTime
                        * Game.instance.m_snowBuildupSpeed
                        * SeasonalSnow.SnowAccumulationSpeed
                        * meltMultiplier;

                    float value = Mathf.Max(0f, __instance.m_snowBuildup - melt);
                    bool belowMinimum = value + SnowChangeEpsilon < range.x;

                    SetSnowValue(
                        __instance,
                        value,
                        markSeasonalSnow: value > SnowChangeEpsilon,
                        meltedBelowMinimum: belowMinimum);
                    SetAccumulationBaselineNow(__instance, belowMinimum);
                    return;
                }

                ZDO zdo = __instance.m_nview.GetZDO();
                if (!zdo.GetBool(SeasonsVars.s_seasonalSnowMeltedBelowMinimum))
                    return;

                if (__instance.m_snowBuildup <= __state + SnowChangeEpsilon)
                    return;

                Vector2 snowRange = SeasonalSnow.GetSnowBuildupRange(__instance);
                float target = Mathf.Max(snowRange.x, GetPersonalSnowTarget(__instance, requireGain: false));
                SetSnowValue(
                    __instance,
                    Mathf.Max(__instance.m_snowBuildup, target),
                    markSeasonalSnow: true,
                    meltedBelowMinimum: false);
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), nameof(SeasonalSnow.UpdateSeasonState))]
        private static class SeasonalSnow_UpdateSeasonState_RuntimeCleanup
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!SeasonState.IsActive || seasonState.GetCurrentSeason() != Season.Winter)
                    ClearTrackedSeasonalSnow();
            }
        }

        [HarmonyPatch(typeof(SeasonalSnow), nameof(SeasonalSnow.OnEnabledConfigChanged))]
        private static class SeasonalSnow_OnEnabledConfigChanged_RuntimeCleanup
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!SeasonalSnow.Enabled)
                    ClearTrackedSeasonalSnow();
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
        private static class Fireplace_Awake_SeasonalSnowMelt
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = instructions.ToList();
                if (!TryPatchFireplaceSnowMeltCondition(codes))
                    LogWarning("Failed to patch Fireplace.Awake seasonal snow melter biome condition.");

                return codes;
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.UpdateSnowMelt))]
        private static class Fireplace_UpdateSnowMelt_SeasonalSnow
        {
            [HarmonyPrefix]
            private static bool Prefix(Fireplace __instance)
            {
                return ShouldRunFireplaceSnowMelt(__instance);
            }
        }
    }
}
