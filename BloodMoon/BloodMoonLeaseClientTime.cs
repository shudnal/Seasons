using HarmonyLib;
using System;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    // Spawn leases are authored in the server SeasonState time domain. Rebase the remaining
    // lifetime when the lease arrives so the existing client expiry check never compares
    // independent real-time-calendar wall clocks.
    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.ReceiveLease))]
    internal static class BloodMoonLeaseClientTimePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonSpawnLeaseState lease)
        {
            if (lease == null || ZNet.instance == null || ZNet.instance.IsServer() || !SeasonState.IsActive)
                return;

            double localNow = seasonState.GetTotalSeconds();
            double advertisedLifetime = Math.Max(2d, BloodMoonConfig.SpawnLeaseSeconds.Value);
            double serverNow = BloodMoonNetwork.ClientGlobal.ServerTime;
            double remaining = serverNow > 0d ? lease.ExpiresAt - serverNow : advertisedLifetime;

            if (double.IsNaN(remaining) || double.IsInfinity(remaining) || remaining <= 0d)
                remaining = advertisedLifetime;
            remaining = Math.Min(advertisedLifetime, Math.Max(0.1d, remaining));
            lease.ExpiresAt = localNow + remaining;
        }
    }
}
