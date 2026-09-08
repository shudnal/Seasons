using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        [System.ThreadStatic]
        private static bool replaying;

        internal static bool ShouldDefer(long sender, long eventId, long playerId, ZDOID enemyId)
        {
            if (replaying || BloodMoonEnemyDeathReports.TryValidate(sender, eventId, playerId, enemyId, out _))
                return false;

            if (pending.TryGetValue(enemyId, out PendingReport existing) && existing.EventId == eventId && existing.PlayerId == playerId &&
                Time.realtimeSinceStartup < existing.ExpiresAt)
                return true;

            // Capture the authoritative facts that can disappear with the dead ZDO. The report is still
            // not creditable until the independently matched damage authorization/confirmation arrives.
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
                    pending.Remove(report.EnemyId);
                    replaying = true;
                    try
                    {
                        BloodMoonController.Instance?.OnEnemyDeathReport(report.Sender, report.EventId, report.PlayerId, report.EnemyId, 0f);
                    }
                    finally
                    {
                        replaying = false;
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

    [HarmonyPatch(typeof(BloodMoonController), "FixedUpdate")]
    internal static class BloodMoonEnemyDeathPendingTickPatch
    {
        private static void Postfix()
        {
            BloodMoonEnemyDeathPending.Process();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonEnemyDeathPendingWorldCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonEnemyDeathPending.Reset();
        }
    }
}
