using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonEnemyDeathPending
    {
        private const float PendingLifetimeSeconds = 5f;

        private sealed class PendingReport
        {
            internal long Sender;
            internal long EventId;
            internal long PlayerId;
            internal ZDOID EnemyId;
            internal float ServerPoints;
            internal float ExpiresAt;
        }

        private static readonly Dictionary<ZDOID, PendingReport> pending = new Dictionary<ZDOID, PendingReport>();
        [ThreadStatic]
        private static bool replaying;

        internal static bool ShouldDefer(long sender, long eventId, long playerId, ZDOID enemyId)
        {
            if (replaying || BloodMoonEnemyDeathReports.TryValidate(sender, eventId, playerId, enemyId, out _))
                return false;

            if (pending.TryGetValue(enemyId, out PendingReport existing) && existing.EventId == eventId && existing.PlayerId == playerId &&
                Time.realtimeSinceStartup < existing.ExpiresAt)
                return true;

            if (!BloodMoonEnemyDeathReports.TryCapturePendingDeathEvidence(sender, eventId, playerId, enemyId, out float serverPoints))
                return false;

            pending[enemyId] = new PendingReport
            {
                Sender = sender,
                EventId = eventId,
                PlayerId = playerId,
                EnemyId = enemyId,
                ServerPoints = serverPoints,
                ExpiresAt = Time.realtimeSinceStartup + PendingLifetimeSeconds
            };
            return true;
        }

        internal static bool TryGetRetainedDeathEvidence(long eventId, ZDOID enemyId, out float serverPoints)
        {
            serverPoints = 0f;
            if (!pending.TryGetValue(enemyId, out PendingReport report) || report.EventId != eventId ||
                Time.realtimeSinceStartup >= report.ExpiresAt)
                return false;
            serverPoints = report.ServerPoints;
            return serverPoints > 0f && !float.IsNaN(serverPoints) && !float.IsInfinity(serverPoints);
        }

        internal static void Process()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pending.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            foreach (PendingReport report in pending.Values.ToArray())
            {
                BloodMoonEventState state = BloodMoonController.Instance?.State;
                if (state == null || state.EventId != report.EventId || !state.IsCombatLive || state.ReportedEnemyDeaths.Contains(report.EnemyId.ToString()) ||
                    !BloodMoonEnemyDeathReports.CanUseRetainedDeathEvidence(report.Sender, report.EventId, report.PlayerId, report.EnemyId))
                {
                    pending.Remove(report.EnemyId);
                    continue;
                }

                if (BloodMoonEnemyDeathReports.TryValidate(report.Sender, report.EventId, report.PlayerId, report.EnemyId, out _))
                {
                    // Validation inside the controller still needs this retained evidence if the actual
                    // dead ZDO has already disappeared. Consume it only after the transaction accepts it.
                    replaying = true;
                    try
                    {
                        BloodMoonController.Instance?.OnEnemyDeathReport(report.Sender, report.EventId, report.PlayerId, report.EnemyId, 0f);
                    }
                    finally
                    {
                        replaying = false;
                        if (state.ReportedEnemyDeaths.Contains(report.EnemyId.ToString()))
                            pending.Remove(report.EnemyId);
                    }
                    continue;
                }

                if (now >= report.ExpiresAt)
                    pending.Remove(report.EnemyId);
            }
        }

        internal static void Reset()
        {
            pending.Clear();
            replaying = false;
        }

        internal static bool IsReplaying => replaying;
    }

    /// <summary>
    /// Exactly-once death progress is not considered acknowledged until the ReportedEnemyDeaths marker
    /// and awarded progress are recoverable from BloodMoonPersistence. The enemy owner keeps a profile-backed
    /// replay record until ACK, so a server restart after a failed state write still has a normal-client replay source.
    /// </summary>
    internal static class BloodMoonEnemyDeathDurability
    {
        private const string RpcAck = "Seasons.BloodMoon.EnemyDeathAck";
        private const string ProfilePrefix = "Seasons.BloodMoon.PendingEnemyDeath.";
        private const float RetrySeconds = 2f;

        private sealed class ServerPending
        {
            internal long Sender;
            internal long EventId;
            internal long PlayerId;
            internal ZDOID EnemyId;
        }

        private static readonly Dictionary<ZDOID, ServerPending> serverPending = new Dictionary<ZDOID, ServerPending>();
        private static readonly HashSet<string> durableDeaths = new HashSet<string>(StringComparer.Ordinal);
        private static ZRoutedRpc registeredRpc;
        private static float retryTimer;

        [ThreadStatic] private static bool deathScope;
        [ThreadStatic] private static long scopeEventId;
        [ThreadStatic] private static long scopePlayerId;
        [ThreadStatic] private static long scopeSender;
        [ThreadStatic] private static ZDOID scopeEnemyId;
        [ThreadStatic] private static float scopeReplayPoints;
        [ThreadStatic] private static bool scopeSaveAttempted;
        [ThreadStatic] private static bool scopeSaveSucceeded;
        [ThreadStatic] private static bool scopeConsumeRequested;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            retryTimer = 0f;
            rpc.Register<ZPackage>(RpcAck, OnAckRpc);
        }

        internal static void TrackLocal(long eventId, ZDOID enemyId, long playerId)
        {
            Player local = Player.m_localPlayer;
            long worldUid = ZNet.m_world?.m_uid ?? 0L;
            if (local == null || worldUid == 0L || eventId < 0L || enemyId.IsNone() || playerId == 0L)
                return;

            string key = MakeProfileKey(worldUid, eventId, enemyId);
            if (local.m_customData.ContainsKey(key))
                return;

            // GetPointsForEnemy is the same server-side formula (10 * replicated level) used by normal
            // validation. This value is only sent on retries; the first report still carries zero and must
            // pass the ordinary dead-ZDO + matched lethal-credit path.
            float replayPoints = BloodMoonCombat.GetPointsForEnemy(enemyId, 0f);
            local.m_customData[key] = playerId.ToString(CultureInfo.InvariantCulture) + "|" +
                Mathf.Max(0f, replayPoints).ToString("R", CultureInfo.InvariantCulture);
            Game.instance?.SavePlayerProfile(false);
        }

        internal static bool BeginServerReport(long sender, long eventId, long playerId, ZDOID enemyId, float clientPoints)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || state.EventId != eventId)
                return true;

            string deathKey = MakeDurableKey(eventId, enemyId);
            if (state.ReportedEnemyDeaths.Contains(enemyId.ToString()))
            {
                if (durableDeaths.Contains(deathKey))
                    SendAck(sender, eventId, enemyId);
                else
                    serverPending[enemyId] = new ServerPending { Sender = sender, EventId = eventId, PlayerId = playerId, EnemyId = enemyId };
                return false;
            }

            deathScope = true;
            scopeEventId = eventId;
            scopePlayerId = playerId;
            scopeSender = sender;
            scopeEnemyId = enemyId;
            scopeReplayPoints = IsValidReplayPoints(clientPoints) ? clientPoints : 0f;
            scopeSaveAttempted = false;
            scopeSaveSucceeded = false;
            scopeConsumeRequested = false;

            if (BloodMoonEnemyDeathReports.TryValidate(sender, eventId, playerId, enemyId, out _))
                return true;

            // Leave the original method to perform its normal no-op validation result, but do not leak a
            // replay hint into unrelated validation calls.
            ClearScope();
            return true;
        }

        internal static bool TryGetCurrentProfileReplay(long eventId, long playerId, ZDOID enemyId, out float serverPoints)
        {
            serverPoints = 0f;
            if (!deathScope || scopeEventId != eventId || scopePlayerId != playerId || scopeEnemyId != enemyId ||
                !IsValidReplayPoints(scopeReplayPoints))
                return false;
            serverPoints = scopeReplayPoints;
            return true;
        }

        internal static void EndServerReport()
        {
            if (!deathScope)
                return;

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            bool accepted = state != null && state.EventId == scopeEventId && state.ReportedEnemyDeaths.Contains(scopeEnemyId.ToString());
            long sender = scopeSender;
            long eventId = scopeEventId;
            long playerId = scopePlayerId;
            ZDOID enemyId = scopeEnemyId;
            bool durable = accepted && scopeSaveAttempted && scopeSaveSucceeded;
            bool consume = scopeConsumeRequested;
            ClearScope();

            if (!accepted)
                return;

            if (durable)
            {
                durableDeaths.Add(MakeDurableKey(eventId, enemyId));
                if (consume)
                    BloodMoonDamageCreditAuthority.ConsumeConfirmedCredit(eventId, enemyId);
                SendAck(sender, eventId, enemyId);
                return;
            }

            serverPending[enemyId] = new ServerPending
            {
                Sender = sender,
                EventId = eventId,
                PlayerId = playerId,
                EnemyId = enemyId
            };
        }

        internal static bool InterceptConsume(long eventId, ZDOID targetId)
        {
            if (!deathScope || eventId != scopeEventId || targetId != scopeEnemyId)
                return false;
            scopeConsumeRequested = true;
            return true;
        }

        internal static void ObserveSave(BloodMoonEventState state, bool succeeded)
        {
            if (succeeded)
                CaptureDurable(state);
            if (!deathScope || state == null || state.EventId != scopeEventId)
                return;
            scopeSaveAttempted = true;
            scopeSaveSucceeded |= succeeded;
        }

        internal static bool ShouldBlockPublish(BloodMoonEventState state)
        {
            if (state == null)
                return false;
            if (deathScope && state.EventId == scopeEventId && scopeSaveAttempted && !scopeSaveSucceeded)
                return true;
            return serverPending.Values.Any(item => item.EventId == state.EventId && !durableDeaths.Contains(MakeDurableKey(item.EventId, item.EnemyId)));
        }

        internal static void Tick(float dt)
        {
            RegisterRpc();
            RetryLocal(dt);

            if (ZNet.instance == null || !ZNet.instance.IsServer() || serverPending.Count == 0)
                return;

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            foreach (ServerPending pending in serverPending.Values.ToArray())
            {
                if (state == null || state.EventId != pending.EventId || !state.ReportedEnemyDeaths.Contains(pending.EnemyId.ToString()))
                {
                    serverPending.Remove(pending.EnemyId);
                    continue;
                }

                string key = MakeDurableKey(pending.EventId, pending.EnemyId);
                if (!durableDeaths.Contains(key) && !BloodMoonPersistence.Save(state))
                    continue;

                durableDeaths.Add(key);
                BloodMoonDamageCreditAuthority.ConsumeConfirmedCredit(pending.EventId, pending.EnemyId);
                SendAck(pending.Sender, pending.EventId, pending.EnemyId);
                serverPending.Remove(pending.EnemyId);
                BloodMoonNetwork.Publish(state, SeasonState.IsActive ? seasonState.GetTotalSeconds() : state.UpdatedAt);
            }
        }

        internal static bool HasUndurableDeath(long eventId)
        {
            return serverPending.Values.Any(item => item.EventId == eventId && !durableDeaths.Contains(MakeDurableKey(item.EventId, item.EnemyId)));
        }

        internal static void Reset()
        {
            serverPending.Clear();
            durableDeaths.Clear();
            retryTimer = 0f;
            ClearScope();
        }

        private static void RetryLocal(float dt)
        {
            Player local = Player.m_localPlayer;
            long worldUid = ZNet.m_world?.m_uid ?? 0L;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (local == null || worldUid == 0L || eventId < 0L)
                return;

            retryTimer -= Mathf.Max(0f, dt);
            if (retryTimer > 0f)
                return;
            retryTimer = RetrySeconds;

            string prefix = ProfilePrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture) + ".";
            KeyValuePair<string, string>[] reports = local.m_customData.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            // Retention handlers may have left an unconfirmed record in memory after an I/O failure.
            // The periodic replay route must obey the same durability gate as the retention ACK route.
            // Verify once for the complete batch rather than saving the profile separately for each kill.
            if (reports.Length == 0 || !BloodMoonRound5Runtime.SaveProfile(local, "pending enemy death replay batch"))
                return;

            foreach (KeyValuePair<string, string> entry in reports)
            {
                string enemyText = entry.Key.Substring(prefix.Length);
                if (!BloodMoonSpawner.TryParseZdoId(enemyText, out ZDOID enemyId) ||
                    !TryParseProfileValue(entry.Value, out long playerId, out float replayPoints))
                    continue;
                BloodMoonNetwork.SendEnemyDeath(eventId, enemyId, playerId, replayPoints);
            }
        }

        private static bool TryParseProfileValue(string value, out long playerId, out float replayPoints)
        {
            playerId = 0L;
            replayPoints = 0f;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split('|');
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerId) || playerId == 0L)
                return false;
            if (parts.Length > 1)
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out replayPoints);
            replayPoints = IsValidReplayPoints(replayPoints) ? replayPoints : 0f;
            return true;
        }

        private static bool IsValidReplayPoints(float points)
        {
            if (points <= 0f || float.IsNaN(points) || float.IsInfinity(points))
                return false;
            float level = points / 10f;
            return Mathf.Abs(level - Mathf.Round(level)) <= 0.0001f;
        }

        private static void OnAckRpc(long sender, ZPackage package)
        {
            if (package == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;
            long eventId = package.ReadLong();
            ZDOID enemyId = package.ReadZDOID();
            Player local = Player.m_localPlayer;
            long worldUid = ZNet.m_world?.m_uid ?? 0L;
            if (local == null || worldUid == 0L || eventId < 0L || enemyId.IsNone())
                return;
            if (local.m_customData.Remove(MakeProfileKey(worldUid, eventId, enemyId)))
                Game.instance?.SavePlayerProfile(false);
        }

        private static void SendAck(long peerId, long eventId, ZDOID enemyId)
        {
            if (peerId == 0L || ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
                return;
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(enemyId);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcAck, package);
        }

        private static void CaptureDurable(BloodMoonEventState state)
        {
            if (state == null || state.EventId < 0L || state.ReportedEnemyDeaths == null)
                return;
            foreach (string enemy in state.ReportedEnemyDeaths)
                durableDeaths.Add(state.EventId.ToString(CultureInfo.InvariantCulture) + ":" + enemy);
        }

        private static string MakeDurableKey(long eventId, ZDOID enemyId) => eventId.ToString(CultureInfo.InvariantCulture) + ":" + enemyId;

        private static string MakeProfileKey(long worldUid, long eventId, ZDOID enemyId)
        {
            return ProfilePrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." +
                eventId.ToString(CultureInfo.InvariantCulture) + "." + enemyId;
        }

        private static void ClearScope()
        {
            deathScope = false;
            scopeEventId = -1L;
            scopePlayerId = 0L;
            scopeSender = 0L;
            scopeEnemyId = ZDOID.None;
            scopeReplayPoints = 0f;
            scopeSaveAttempted = false;
            scopeSaveSucceeded = false;
            scopeConsumeRequested = false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.OnEnemyDeathReport))]
    internal static class BloodMoonEnemyDeathPendingReportPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(long sender, long eventId, long playerId, ZDOID enemyId)
        {
            if (BloodMoonEnemyDeathPending.IsReplaying)
                return true;
            return !BloodMoonEnemyDeathPending.ShouldDefer(sender, eventId, playerId, enemyId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.OnEnemyDeathReport))]
    internal static class BloodMoonEnemyDeathDurabilityScopePatch
    {
        // Establish replay evidence before the pending-evidence guard makes its validation decision.
        [HarmonyPriority(Priority.First + 50)]
        private static bool Prefix(long sender, long eventId, long playerId, ZDOID enemyId, float clientPoints)
        {
            return BloodMoonEnemyDeathDurability.BeginServerReport(sender, eventId, playerId, enemyId, clientPoints);
        }

        private static void Postfix()
        {
            BloodMoonEnemyDeathDurability.EndServerReport();
        }

        private static Exception Finalizer(Exception __exception)
        {
            BloodMoonEnemyDeathDurability.EndServerReport();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(BloodMoonDamageCreditAuthority), nameof(BloodMoonDamageCreditAuthority.ConsumeConfirmedCredit))]
    internal static class BloodMoonEnemyDeathDeferredCreditConsumePatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(long eventId, ZDOID targetId)
        {
            return !BloodMoonEnemyDeathDurability.InterceptConsume(eventId, targetId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPersistence), nameof(BloodMoonPersistence.Save))]
    internal static class BloodMoonEnemyDeathDurableSavePatch
    {
        private static void Postfix(BloodMoonEventState state, bool __result)
        {
            BloodMoonEnemyDeathDurability.ObserveSave(state, __result);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPersistence), nameof(BloodMoonPersistence.Load))]
    internal static class BloodMoonEnemyDeathDurableLoadPatch
    {
        private static void Postfix(BloodMoonEventState __result)
        {
            if (__result != null)
                BloodMoonEnemyDeathDurability.ObserveSave(__result, true);
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.Publish))]
    internal static class BloodMoonEnemyDeathPublishDurabilityPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(BloodMoonEventState state)
        {
            return !BloodMoonEnemyDeathDurability.ShouldBlockPublish(state);
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.SendEnemyDeath))]
    internal static class BloodMoonEnemyDeathClientPersistencePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(long eventId, ZDOID enemyId, long creditedPlayerId)
        {
            BloodMoonEnemyDeathDurability.TrackLocal(eventId, enemyId, creditedPlayerId);
        }
    }

    [HarmonyPatch(typeof(BloodMoonNetwork), nameof(BloodMoonNetwork.RegisterRpcs))]
    internal static class BloodMoonEnemyDeathAckRegistrationPatch
    {
        private static void Postfix() => BloodMoonEnemyDeathDurability.RegisterRpc();
    }

    [HarmonyPatch(typeof(BloodMoonController), "FixedUpdate")]
    internal static class BloodMoonEnemyDeathPendingTickPatch
    {
        private static void Postfix()
        {
            BloodMoonEnemyDeathPending.Process();
            BloodMoonEnemyDeathDurability.Tick(Time.fixedDeltaTime);
        }
    }

    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.BeginResolution))]
    internal static class BloodMoonEnemyDeathResolutionDurabilityPatch
    {
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(BloodMoonController __instance)
        {
            long eventId = __instance?.State?.EventId ?? -1L;
            BloodMoonEnemyDeathDurability.Tick(0f);
            return eventId < 0L || !BloodMoonEnemyDeathDurability.HasUndurableDeath(eventId);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonEnemyDeathPendingWorldCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonEnemyDeathPending.Reset();
            BloodMoonEnemyDeathDurability.Reset();
        }
    }
}