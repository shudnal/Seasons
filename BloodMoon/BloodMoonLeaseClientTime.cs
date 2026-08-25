using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonLeaseClientTime
    {
        private static readonly FieldInfo ClientLeasesField = AccessTools.Field(typeof(BloodMoonSpawner), "clientLeases");
        private static readonly Dictionary<string, float> deadlines = new Dictionary<string, float>();

        internal static void Rebase(BloodMoonSpawnLeaseState lease)
        {
            if (lease == null || ZNet.instance == null || ZNet.instance.IsServer())
                return;

            double advertisedLifetime = Math.Max(2d, BloodMoonConfig.SpawnLeaseSeconds != null ? BloodMoonConfig.SpawnLeaseSeconds.Value : 8f);
            double serverNow = BloodMoonNetwork.ClientGlobal.ServerTime;
            double remaining = advertisedLifetime;

            if (serverNow > 0d)
            {
                remaining = lease.ExpiresAt - serverNow;
                if (double.IsNaN(remaining) || double.IsInfinity(remaining))
                    remaining = advertisedLifetime;
                else
                    remaining = Math.Min(advertisedLifetime, Math.Max(0d, remaining));
            }

            deadlines[MakeDeadlineKey(lease)] = Time.realtimeSinceStartup + (float)remaining;

            // Client expiry is maintained in the monotonic realtimeSinceStartup domain below.
            // Keep the serialized SeasonState-domain value from participating in TickClient's legacy check.
            lease.ExpiresAt = double.MaxValue;
        }

        internal static void PrepareTick()
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
                return;

            IDictionary leases = ClientLeasesField?.GetValue(null) as IDictionary;
            if (leases == null || leases.Count == 0)
            {
                deadlines.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            float fallbackLifetime = Mathf.Max(2f, BloodMoonConfig.SpawnLeaseSeconds != null ? BloodMoonConfig.SpawnLeaseSeconds.Value : 8f);
            List<object> expiredLeaseKeys = new List<object>();
            HashSet<string> activeDeadlineKeys = new HashSet<string>();

            foreach (DictionaryEntry entry in leases)
            {
                if (entry.Value is not BloodMoonSpawnLeaseState lease)
                    continue;

                string deadlineKey = MakeDeadlineKey(lease);
                activeDeadlineKeys.Add(deadlineKey);

                if (!deadlines.TryGetValue(deadlineKey, out float deadline))
                {
                    // Defensive compatibility path for a lease that entered the dictionary before this
                    // runtime state was initialized. It still receives a bounded monotonic lifetime.
                    deadline = now + fallbackLifetime;
                    deadlines[deadlineKey] = deadline;
                }

                if (now >= deadline)
                {
                    expiredLeaseKeys.Add(entry.Key);
                    deadlines.Remove(deadlineKey);
                    continue;
                }

                lease.ExpiresAt = double.MaxValue;
            }

            foreach (object key in expiredLeaseKeys)
                leases.Remove(key);

            foreach (string deadlineKey in new List<string>(deadlines.Keys))
            {
                if (!activeDeadlineKeys.Contains(deadlineKey))
                    deadlines.Remove(deadlineKey);
            }
        }

        internal static void Reset()
        {
            deadlines.Clear();
        }

        private static string MakeDeadlineKey(BloodMoonSpawnLeaseState lease)
        {
            return lease == null
                ? string.Empty
                : $"{lease.EventId}:{lease.GroupId}:{lease.ZoneX}:{lease.ZoneY}:{lease.OwnerSessionId}:{lease.LeaseRevision}";
        }
    }

    // Spawn leases are authored in the server SeasonState time domain. On receipt, preserve only
    // the bounded remaining lifetime and move client expiry into Unity's monotonic realtime clock.
    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.ReceiveLease))]
    internal static class BloodMoonLeaseClientTimePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(BloodMoonSpawnLeaseState lease)
        {
            BloodMoonLeaseClientTime.Rebase(lease);
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.TickClient))]
    internal static class BloodMoonLeaseClientExpiryPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonLeaseClientTime.PrepareTick();
        }
    }

    [HarmonyPatch(typeof(BloodMoonSpawner), nameof(BloodMoonSpawner.ResetClientState))]
    internal static class BloodMoonLeaseClientResetPatch
    {
        private static void Postfix()
        {
            BloodMoonLeaseClientTime.Reset();
        }
    }
}
