using HarmonyLib;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRound5LeaseFixes
    {
        private static readonly FieldInfo PendingReportsField = AccessTools.Field(typeof(BloodMoonSpawner), "pendingSpawnReports");
        private static readonly MethodInfo AddReservationTokenMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "AddReservationToken");
        private static readonly MethodInfo TryConsumeRetiredMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "TryConsumeRetired");
        private static readonly MethodInfo TryConsumeCurrentMethod = AccessTools.Method(typeof(BloodMoonRetiredLeaseReservations), "TryConsumeCurrent");
        private static readonly MethodInfo ValidateSpawnSystemOwnershipMethod = AccessTools.Method(typeof(BloodMoonZoneOwnership), "ValidateSpawnSystemOwnership");

        internal static void RetireOwnershipMigrations(BloodMoonEventState state)
        {
            if (state?.SpawnLeases == null || state.SpawnLeases.Count == 0 || AddReservationTokenMethod == null ||
                ValidateSpawnSystemOwnershipMethod == null)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;

            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values.ToArray())
            {
                if (lease == null || lease.Allowance <= 0 || lease.EventId != state.EventId ||
                    !state.Groups.TryGetValue(lease.GroupId, out BloodMoonGroupState group) || group.Revision != lease.GroupRevision)
                    continue;

                bool stillOwnsZone;
                try
                {
                    stillOwnsZone = (bool)ValidateSpawnSystemOwnershipMethod.Invoke(null,
                        new object[] { lease.OwnerPeerId, new Vector2i(lease.ZoneX, lease.ZoneY) });
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Spawn] Could not verify lease owner for zone {lease.ZoneX},{lease.ZoneY}: {ex.Message}");
                    continue;
                }

                if (stillOwnsZone)
                    continue;

                int deliveredAllowance = Math.Max(0, lease.Allowance);
                for (int i = 0; i < deliveredAllowance; ++i)
                    AddReservationTokenMethod.Invoke(null, new object[] { pending, state, lease });

                // The group/revision is still current, but the zone owner is not. Retain the already-delivered
                // currency as reservation tokens, then revoke this exact lease revision before the ordinary
                // lease pass can reclaim its capacity for the replacement owner.
                lease.Allowance = 0;
                BloodMoonNetwork.SendSpawnLease(lease.OwnerPeerId, lease);
                LogInfo($"[BloodMoon][event:{state.EventId}][zone:{lease.ZoneX},{lease.ZoneY}][lease:{lease.LeaseRevision}] " +
                    $"Retired {deliveredAllowance} delivered token(s) after zone ownership migrated from peer {lease.OwnerPeerId}.");
            }
        }

        internal static void DiscoverReplicatedExtras(BloodMoonEventState state)
        {
            if (state == null || state.EventId < 0L || state.ExtraEnemyZdos == null || ZDOMan.instance == null ||
                TryConsumeRetiredMethod == null || TryConsumeCurrentMethod == null)
                return;

            IDictionary pending = PendingReportsField?.GetValue(null) as IDictionary;
            if (pending == null)
                return;

            string expectedPrefabName = BloodMoonSpawner.GetFrozenSpawnPrefabName(state);
            int poolIdentity = BloodMoonSpawner.GetSpawnPoolIdentity(state);
            if (string.IsNullOrEmpty(expectedPrefabName) || poolIdentity == 0)
                return;

            bool changed = false;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (zdo == null || zdo.GetLong(BloodMoonSpawner.EventMarker, -1L) != state.EventId ||
                    !BloodMoonSpawnReportValidation.IsAllowedExtraEnemyZdo(zdo, state.EventId, expectedPrefabName))
                    continue;

                string idValue = zdo.m_uid.ToString();
                if (state.ExtraEnemyZdos.Contains(idValue))
                    continue;

                // A normal report has already spent one lease token before entering pendingSpawnReports.
                // Discovery must wait for that report's metadata-validation path instead of spending a
                // second current/retired token for the same ZDO.
                if (pending.Contains(zdo.m_uid))
                    continue;

                long groupId = zdo.GetLong(BloodMoonSpawner.GroupMarker, -1L);
                int zoneX = zdo.GetInt(BloodMoonSpawner.SpawnZoneXMarker, int.MinValue);
                int zoneY = zdo.GetInt(BloodMoonSpawner.SpawnZoneYMarker, int.MinValue);
                if (groupId < 0L || zoneX == int.MinValue || zoneY == int.MinValue)
                    continue;

                long creator = zdo.m_uid.UserID;
                bool consumed;
                try
                {
                    consumed = (bool)TryConsumeRetiredMethod.Invoke(null,
                        new object[] { pending, state.EventId, groupId, zoneX, zoneY, creator, poolIdentity });
                    if (!consumed)
                    {
                        consumed = (bool)TryConsumeCurrentMethod.Invoke(null,
                            new object[] { state, groupId, zoneX, zoneY, creator, poolIdentity });
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"[BloodMoon.Spawn] Could not validate replicated extra {zdo.m_uid} against lease provenance: {ex.Message}");
                    continue;
                }

                if (!consumed)
                    continue;

                state.ExtraEnemyZdos.Add(idValue);
                changed = true;
                LogInfo($"[BloodMoon][event:{state.EventId}][spawn] Recovered replicated marked extra {zdo.m_uid} by consuming one matching lease token.");
            }

            if (changed)
                BloodMoonPersistence.Save(state);
        }
    }

    internal static class BloodMoonRound5DrainTimeout
    {
        private const float MaximumHoldSeconds = 35f;
        private static BloodMoonEventState heldState;
        private static long worldUid;
        private static long eventId = -1L;
        private static float startedAt;
        private static bool warned;

        internal static void Reset()
        {
            heldState = null;
            worldUid = 0L;
            eventId = -1L;
            startedAt = 0f;
            warned = false;
        }

        internal static void Bound(BloodMoonController controller, ref bool hold)
        {
            if (!hold)
            {
                Reset();
                return;
            }

            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId < 0L)
                return;
            if (!ReferenceEquals(heldState, state) || worldUid != state.WorldUid || eventId != state.EventId)
            {
                heldState = state;
                worldUid = state.WorldUid;
                eventId = state.EventId;
                startedAt = Time.realtimeSinceStartup;
                warned = false;
                return;
            }

            if (Time.realtimeSinceStartup - startedAt < MaximumHoldSeconds)
                return;
            hold = false;
            if (!warned)
            {
                warned = true;
                LogWarning($"[BloodMoon][event:{eventId}][resolution] Durable report drain reached its {MaximumHoldSeconds:0}s bound; continuing resolution.");
            }
        }
    }

    internal static class BloodMoonRound7Conformance
    {
        private const string OfferingRequestMarker = "Seasons.BloodMoon.OfferingRequestId";
        private const string OfferingAuthorityMarker = "Seasons.BloodMoon.OfferingAuthorityId";
        private const string OfferingAttemptRequestMarker = "Seasons.BloodMoon.OfferingExecutedRequestId";
        private const string OfferingAttemptAuthorityMarker = "Seasons.BloodMoon.OfferingExecutedAuthorityId";
        private const string OfferingCompletedRequestMarker = "Seasons.BloodMoon.OfferingCompletedRequestId";
        private const string OfferingCompletedAuthorityMarker = "Seasons.BloodMoon.OfferingCompletedAuthorityId";

        internal static bool TryAcceptTerminalSkillReport(long sender, long eventId, long playerId, long sequence, Skills.SkillType skill,
            float baseEquivalent, float liveBonusEquivalent)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || sequence <= 0L || float.IsNaN(baseEquivalent) || float.IsInfinity(baseEquivalent) ||
                float.IsNaN(liveBonusEquivalent) || float.IsInfinity(liveBonusEquivalent) ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsTerminal)
                return false;

            bool drainOpen = state.IsCombatLive || state.Phase == BloodMoonEventPhase.Resolving &&
                (int)state.ResolutionStep < (int)BloodMoonResolutionStep.PublishingOutcomes;
            if (!drainOpen || !BloodMoonRound5Runtime.ValidateSenderPlayer(controller, sender, playerId))
                return true;

            long previousSequence = participant.LastSkillReportSequence;
            BloodMoonSkills.AcceptServerReport(participant, sequence, skill, baseEquivalent, liveBonusEquivalent);
            bool changed = participant.LastSkillReportSequence != previousSequence;
            if (changed)
            {
                state.UpdatedAt = SeasonState.IsActive ? seasonState.GetTotalSeconds() : state.UpdatedAt;
                state.Revision++;
            }

            if (!BloodMoonPersistence.Save(state))
                return true;
            if (changed)
                BloodMoonNetwork.Publish(state, state.UpdatedAt);
            BloodMoonSkillReportReliability.SendAck(sender, eventId, playerId, participant.LastSkillReportSequence);
            return true;
        }

        internal static bool TryAcceptTerminalDeathReplay(long sender, long eventId, long playerId, ZDOID enemyId,
            ref float serverPoints, ref bool result)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || state.EventId != eventId || !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) ||
                !participant.IsTerminal || !BloodMoonRound5Runtime.IsPreOutcomeDrainOpen(state) || !IsReadySender(sender) ||
                !BloodMoonEnemyDeathDurability.TryGetCurrentProfileReplay(eventId, playerId, enemyId, out float replayPoints))
                return false;

            serverPoints = replayPoints;
            result = replayPoints > 0f && !float.IsNaN(replayPoints) && !float.IsInfinity(replayPoints);
            return true;
        }

        internal static void MarkOfferingCompleted(OfferingBowl bowl)
        {
            if (!BloodMoonOfferingAuthority.IsAuthorizedCompletion || bowl?.m_nview == null || !bowl.m_nview.IsValid() || ZDOMan.instance == null)
                return;

            ZDO zdo = bowl.m_nview.GetZDO();
            if (zdo == null)
                return;
            long requestId = zdo.GetLong(OfferingRequestMarker, 0L);
            long authorityId = zdo.GetLong(OfferingAuthorityMarker, 0L);
            if (requestId <= 0L || authorityId == 0L)
                return;

            zdo.Set(OfferingCompletedRequestMarker, requestId);
            zdo.Set(OfferingCompletedAuthorityMarker, authorityId);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }

        internal static void VerifyOfferingCompletion(long requestId, ZDOID bowlId, ref bool completed)
        {
            if (!completed || requestId <= 0L || bowlId.IsNone() || ZDOMan.instance == null)
                return;

            ZDO zdo = ZDOMan.instance.GetZDO(bowlId);
            if (zdo == null)
            {
                completed = false;
                return;
            }

            long authorityId = zdo.GetLong(OfferingAuthorityMarker, 0L);
            bool completionRecorded = authorityId != 0L && zdo.GetLong(OfferingCompletedRequestMarker, 0L) == requestId &&
                zdo.GetLong(OfferingCompletedAuthorityMarker, 0L) == authorityId;
            if (completionRecorded)
                return;

            // The old markers mean only that execution was attempted. If the owner disappeared before
            // vanilla SpawnBoss actually scheduled DelayedSpawnBoss, the replacement owner must be able
            // to retry instead of acknowledging a phantom completion.
            completed = false;
            if (zdo.GetLong(OfferingAttemptRequestMarker, 0L) == requestId &&
                zdo.GetLong(OfferingAttemptAuthorityMarker, 0L) == authorityId)
            {
                zdo.Set(OfferingAttemptRequestMarker, 0L);
                zdo.Set(OfferingAttemptAuthorityMarker, 0L);
                ZDOMan.instance.ForceSendZDO(bowlId);
            }
        }

        internal static BloodMoonScheduleSnapshot FindMostRecentCrossedSchedule(double now)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.FirstEnabledAt <= 0d ||
                state.Phase != BloodMoonEventPhase.Dormant && state.Phase != BloodMoonEventPhase.Resolved && state.Phase != BloodMoonEventPhase.Skipped)
                return null;

            long highWater = Math.Max(state.LastCreatedEventId, state.LastResolvedEventId);
            int currentWorldDay = seasonState.GetWorldDay(now);
            int annualDays = 0;
            foreach (Season season in Enum.GetValues(typeof(Season)))
                annualDays += seasonState.GetDaysInSeason(season);
            int searchRadius = Math.Max(16, annualDays + 4);
            BloodMoonScheduleSnapshot best = null;

            for (int day = Math.Max(0, currentWorldDay - searchRadius); day <= currentWorldDay; ++day)
            {
                BloodMoonScheduleSnapshot candidate = BloodMoonSchedule.TryCreateForWorldDay(day);
                if (candidate == null || candidate.EventWorldDay <= highWater || candidate.MorningAt > now ||
                    state.FirstEnabledAt >= candidate.ForewarningAt)
                    continue;
                if (best == null || candidate.MorningAt > best.MorningAt)
                    best = candidate;
            }
            return best;
        }

        internal static bool ShouldCatchUpCrossedSchedule(BloodMoonScheduleSnapshot schedule, double now)
        {
            BloodMoonScheduleSnapshot crossed = FindMostRecentCrossedSchedule(now);
            return schedule != null && crossed != null && schedule.EventWorldDay == crossed.EventWorldDay && schedule.MorningAt <= now;
        }

        private static bool IsReadySender(long sender)
        {
            if (sender == 0L || ZRoutedRpc.instance == null || ZNet.instance == null)
                return false;
            if (sender == ZRoutedRpc.instance.GetServerPeerID())
                return true;
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            return peer != null && peer.IsReady();
        }
    }

    [HarmonyPatch(typeof(BloodMoonRetiredLeaseReservations), nameof(BloodMoonRetiredLeaseReservations.RetireObsolete))]
    internal static class BloodMoonZoneOwnerLeaseRetirementPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static void Prefix(BloodMoonEventState state)
        {
            BloodMoonRound5LeaseFixes.RetireOwnershipMigrations(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonRetiredLeaseReservations), nameof(BloodMoonRetiredLeaseReservations.DiscoverReplicatedExtras))]
    internal static class BloodMoonPendingSpawnDiscoveryTokenPatch
    {
        [HarmonyPriority(Priority.First + 300)]
        private static bool Prefix(BloodMoonEventState state)
        {
            BloodMoonRound5LeaseFixes.DiscoverReplicatedExtras(state);
            return false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonOutcomeDrainGate), nameof(BloodMoonOutcomeDrainGate.ShouldHold))]
    internal static class BloodMoonOutcomeDrainTimeoutPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonController controller, ref bool __result)
        {
            BloodMoonRound5DrainTimeout.Bound(controller, ref __result);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPresentation), nameof(BloodMoonPresentation.OnResolutionComplete))]
    internal static class BloodMoonCompletedTerminalMarkerCleanupPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            BloodMoonTerminalReliability.OnResolutionComplete();
        }
    }

    [HarmonyPatch(typeof(BloodMoonSkillReports), nameof(BloodMoonSkillReports.Accept))]
    internal static class BloodMoonTerminalSkillDrainPatch
    {
        [HarmonyPriority(Priority.First + 400)]
        private static bool Prefix(long sender, long eventId, long playerId, long sequence, Skills.SkillType skill,
            float baseEquivalent, float liveBonusEquivalent)
        {
            return !BloodMoonRound7Conformance.TryAcceptTerminalSkillReport(sender, eventId, playerId, sequence, skill,
                baseEquivalent, liveBonusEquivalent);
        }
    }

    [HarmonyPatch(typeof(BloodMoonEnemyDeathReports), nameof(BloodMoonEnemyDeathReports.TryValidate))]
    internal static class BloodMoonTerminalDeathReplayPatch
    {
        [HarmonyPriority(Priority.First + 500)]
        private static bool Prefix(long sender, long eventId, long creditedPlayerId, ZDOID enemyId, ref float serverPoints, ref bool __result)
        {
            return !BloodMoonRound7Conformance.TryAcceptTerminalDeathReplay(sender, eventId, creditedPlayerId, enemyId,
                ref serverPoints, ref __result);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.SpawnBoss))]
    internal static class BloodMoonOfferingCompletionFactPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(OfferingBowl __instance)
        {
            BloodMoonRound7Conformance.MarkOfferingCompleted(__instance);
        }
    }

    [HarmonyPatch(typeof(BloodMoonOfferingAuthority), "SendRelayResult")]
    internal static class BloodMoonOfferingResultVerificationPatch
    {
        [HarmonyPriority(Priority.First + 400)]
        private static void Prefix(long requestId, ZDOID bowlId, ref bool completed)
        {
            BloodMoonRound7Conformance.VerifyOfferingCompletion(requestId, bowlId, ref completed);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSchedule), nameof(BloodMoonSchedule.FindCurrentOrNext))]
    internal static class BloodMoonCrossedScheduleSelectionPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(double now, ref BloodMoonScheduleSnapshot __result)
        {
            if (__result != null && now >= __result.ForewarningAt && now <= __result.MorningAt)
                return;
            BloodMoonScheduleSnapshot crossed = BloodMoonRound7Conformance.FindMostRecentCrossedSchedule(now);
            if (crossed != null)
                __result = crossed;
        }
    }

    [HarmonyPatch(typeof(BloodMoonSchedule), nameof(BloodMoonSchedule.GetExpectedPhase))]
    internal static class BloodMoonCrossedSchedulePhasePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(BloodMoonScheduleSnapshot schedule, double now, ref BloodMoonEventPhase __result)
        {
            if (__result == BloodMoonEventPhase.Resolved && BloodMoonRound7Conformance.ShouldCatchUpCrossedSchedule(schedule, now))
                __result = BloodMoonEventPhase.AutoCompleting;
        }
    }
}
