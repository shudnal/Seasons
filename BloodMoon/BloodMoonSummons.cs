using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSummons
    {
        internal static readonly int EventMarker = "Seasons.BloodMoon.SummonEventId".GetStableHashCode();
        internal static readonly int OwnerPlayerMarker = "Seasons.BloodMoon.SummonOwnerPlayerId".GetStableHashCode();
        internal static readonly int TemporaryMarker = "Seasons.BloodMoon.SummonTemporary".GetStableHashCode();

        private sealed class SpawnContext
        {
            internal long EventId;
            internal long PlayerId;
            internal bool Temporary;
        }

        private static readonly Dictionary<int, SpawnContext> spawnContexts = new Dictionary<int, SpawnContext>();
        private static readonly Dictionary<(long EventId, long PlayerId), double> playerCleanupWatches = new Dictionary<(long, long), double>();
        private static readonly Dictionary<long, double> eventCleanupWatches = new Dictionary<long, double>();
        private static double nextCleanupScan;

        internal static void CaptureAbility(SpawnAbility ability, Character owner, ItemDrop.ItemData item)
        {
            if (ability == null || owner is not Player player || !BloodMoonInteractionRules.IsEventCombatLive || !BloodMoonInteractionRules.IsActiveParticipant(player))
                return;

            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            long playerId = player.GetPlayerID();
            bool temporary = item != null && BloodCraft.TryReadMarker(item, out long itemEventId, out long itemOwnerId) &&
                itemEventId == eventId && itemOwnerId == playerId;

            spawnContexts[ability.GetInstanceID()] = new SpawnContext
            {
                EventId = eventId,
                PlayerId = playerId,
                Temporary = temporary
            };
            BloodMoonSummonAbilityLifetime.Ensure(ability);
        }

        internal static void MarkSpawned(SpawnAbility ability, Character summon)
        {
            if (ability == null || summon == null || summon.m_nview == null || !summon.m_nview.IsValid() || !summon.m_nview.IsOwner() ||
                !spawnContexts.TryGetValue(ability.GetInstanceID(), out SpawnContext context))
                return;

            ZDO zdo = summon.m_nview.GetZDO();
            if (zdo == null)
                return;

            zdo.Set(EventMarker, context.EventId);
            zdo.Set(OwnerPlayerMarker, context.PlayerId);
            zdo.Set(TemporaryMarker, context.Temporary);

            if (context.Temporary && ShouldRemoveTemporary(context.EventId, context.PlayerId))
                DestroyLocalOwnedSummon(summon);
        }

        internal static bool IsBloodSummon(Character character)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid() || !BloodMoonInteractionRules.IsEventCombatLive)
                return false;
            ZDO zdo = character.m_nview.GetZDO();
            return zdo != null && ValidateMarkedSummonZdo(zdo, BloodMoonNetwork.ClientGlobal.EventId, zdo.GetLong(OwnerPlayerMarker, 0L));
        }

        internal static bool TryGetOwnerPlayerId(Character character, out long playerId)
        {
            playerId = 0L;
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
                return false;
            ZDO zdo = character.m_nview.GetZDO();
            if (zdo == null)
                return false;
            long eventId = zdo.GetLong(EventMarker, -1L);
            playerId = zdo.GetLong(OwnerPlayerMarker, 0L);
            return eventId == BloodMoonNetwork.ClientGlobal.EventId && playerId != 0L;
        }

        internal static bool ValidateMarkedSummonZdo(ZDO zdo, long eventId, long playerId)
        {
            return zdo != null && eventId >= 0L && playerId != 0L &&
                zdo.GetLong(EventMarker, -1L) == eventId && zdo.GetLong(OwnerPlayerMarker, 0L) == playerId;
        }

        internal static void CleanupLocalTemporary(long eventId, long playerId)
        {
            if (eventId < 0L || playerId == 0L || ZNetScene.instance == null)
                return;

            foreach (Character character in Character.GetAllCharacters().ToArray())
            {
                if (!IsTemporaryFor(character, eventId, playerId) || character.m_nview == null || !character.m_nview.IsValid() || !character.m_nview.IsOwner())
                    continue;
                DestroyLocalOwnedSummon(character);
            }
        }

        internal static void CleanupForPlayer(long eventId, long playerId, double now)
        {
            _ = now;
            if (eventId < 0L || playerId == 0L)
                return;
            DestroyServerTemporarySummons(eventId, playerId);
            playerCleanupWatches[(eventId, playerId)] = Time.realtimeSinceStartup + 5d;
        }

        internal static void CleanupEvent(long eventId, double now)
        {
            _ = now;
            if (eventId < 0L)
                return;
            DestroyServerTemporarySummons(eventId, 0L);
            eventCleanupWatches[eventId] = Time.realtimeSinceStartup + 5d;
        }

        internal static void TickServer(BloodMoonEventState state, double now)
        {
            _ = state;
            _ = now;
            double realtime = Time.realtimeSinceStartup;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || realtime < nextCleanupScan ||
                playerCleanupWatches.Count == 0 && eventCleanupWatches.Count == 0)
                return;

            nextCleanupScan = realtime + 1d;
            foreach (KeyValuePair<(long EventId, long PlayerId), double> watch in playerCleanupWatches.ToArray())
            {
                DestroyServerTemporarySummons(watch.Key.EventId, watch.Key.PlayerId);
                if (realtime >= watch.Value)
                    playerCleanupWatches.Remove(watch.Key);
            }
            foreach (KeyValuePair<long, double> watch in eventCleanupWatches.ToArray())
            {
                DestroyServerTemporarySummons(watch.Key, 0L);
                if (realtime >= watch.Value)
                    eventCleanupWatches.Remove(watch.Key);
            }
        }

        internal static void Recover(BloodMoonEventState state)
        {
            if (ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetBool(TemporaryMarker, false) && zdo.GetLong(EventMarker, -1L) >= 0L)
                .ToArray())
            {
                long eventId = zdo.GetLong(EventMarker, -1L);
                long playerId = zdo.GetLong(OwnerPlayerMarker, 0L);
                bool matchingActiveOwner = state != null && state.IsCombatLive && eventId == state.EventId && playerId != 0L &&
                    state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) && participant.IsCombatActive;
                if (!matchingActiveOwner)
                    DestroyZdo(zdo);
            }
        }

        internal static void ResetRuntimeState()
        {
            spawnContexts.Clear();
            playerCleanupWatches.Clear();
            eventCleanupWatches.Clear();
            nextCleanupScan = 0d;
        }

        internal static void CleanupAbility(SpawnAbility ability)
        {
            if (ability != null)
                spawnContexts.Remove(ability.GetInstanceID());
        }

        private static bool IsTemporaryFor(Character character, long eventId, long playerId)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
                return false;
            ZDO zdo = character.m_nview.GetZDO();
            return zdo != null && zdo.GetBool(TemporaryMarker, false) && ValidateMarkedSummonZdo(zdo, eventId, playerId);
        }

        private static bool ShouldRemoveTemporary(long eventId, long playerId)
        {
            return eventId != BloodMoonNetwork.ClientGlobal.EventId || !BloodMoonInteractionRules.IsEventCombatLive ||
                BloodMoonRecovery.IsLocallyExited(playerId) || !BloodMoonInteractionRules.CanCreditProgress(playerId);
        }

        private static void DestroyLocalOwnedSummon(Character summon)
        {
            if (summon?.m_nview == null || !summon.m_nview.IsValid() || !summon.m_nview.IsOwner())
                return;
            LogInfo($"[BloodMoon.Craft] Removing temporary summon {summon.GetZDOID()}.");
            ZNetScene.instance?.Destroy(summon.gameObject);
        }

        private static void DestroyServerTemporarySummons(long eventId, long playerId)
        {
            if (ZDOMan.instance == null)
                return;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetBool(TemporaryMarker, false) && zdo.GetLong(EventMarker, -1L) == eventId &&
                    (playerId == 0L || zdo.GetLong(OwnerPlayerMarker, 0L) == playerId))
                .ToArray())
            {
                DestroyZdo(zdo);
            }
        }

        private static void DestroyZdo(ZDO zdo)
        {
            if (zdo == null || ZDOMan.instance == null)
                return;
            LogInfo($"[BloodMoon.Craft] Removing temporary summon ZDO {zdo.m_uid}.");
            ZDOMan.instance.DestroyZDO(zdo);
        }
    }

    internal sealed class BloodMoonSummonAbilityLifetime : MonoBehaviour
    {
        private SpawnAbility ability;

        internal static void Ensure(SpawnAbility source)
        {
            if (source == null)
                return;
            BloodMoonSummonAbilityLifetime lifetime = source.gameObject.GetComponent<BloodMoonSummonAbilityLifetime>() ?? source.gameObject.AddComponent<BloodMoonSummonAbilityLifetime>();
            lifetime.ability = source;
        }

        private void OnDestroy()
        {
            BloodMoonSummons.CleanupAbility(ability);
        }
    }

    [HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.Setup))]
    internal static class BloodMoonSpawnAbilitySetupPatch
    {
        private static void Postfix(SpawnAbility __instance, Character owner, ItemDrop.ItemData item)
        {
            BloodMoonSummons.CaptureAbility(__instance, owner, item);
        }
    }

    [HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.SetupAoe))]
    internal static class BloodMoonSpawnAbilityTransferPatch
    {
        private static void Prefix(SpawnAbility __instance, Character owner)
        {
            BloodMoonSummons.MarkSpawned(__instance, owner);
        }
    }
}
