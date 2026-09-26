using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    // WaterVolume's list is independent of Floating.Instances. Trigger callbacks are
    // sparse boundaries; ordinary water interactables never enter a Seasons frame loop.
    internal static class SeasonalIceFloeWater
    {
        private static readonly HashSet<IceFloe> managed = new HashSet<IceFloe>();
        private static readonly Dictionary<WaterVolume, HashSet<Floating>> pendingRestore =
            new Dictionary<WaterVolume, HashSet<Floating>>();
        private static readonly List<WaterVolume> emptyPending = new List<WaterVolume>();
        private static bool bulkAdmission;
        internal static int ManagedCount => managed.Count;
        internal static int PendingCount
        {
            get
            {
                int count = 0;
                foreach (HashSet<Floating> floaters in pendingRestore.Values)
                    count += floaters.Count;
                return count;
            }
        }
        internal static int VolumeCount
        {
            get
            {
                int count = 0;
                foreach (IceFloe floe in managed)
                    count += floe.ManagedWaterVolumes.Count;
                return count;
            }
        }

        internal static void BeginBulkAdmission() => bulkAdmission = true;

        internal static void EndBulkAdmission()
        {
            bulkAdmission = false;
            // One volume/list pass for a simultaneous winter activation, not one
            // full world pass for every loaded floe.
            foreach (WaterVolume volume in WaterVolume.Instances)
                RetireMembers(volume);
        }

        internal static void Admit(IceFloe floe)
        {
            managed.Add(floe);
            // A prior release may have deferred native restoration until a
            // disabled volume wakes. Reclaim that candidate for this winter.
            Floating floating = floe.m_floating;
            emptyPending.Clear();
            foreach (KeyValuePair<WaterVolume, HashSet<Floating>> entry in pendingRestore)
            {
                if (entry.Value.Remove(floating))
                    floe.ManagedWaterVolumes.Add(entry.Key);
                if (entry.Value.Count == 0)
                    emptyPending.Add(entry.Key);
            }
            foreach (WaterVolume volume in emptyPending)
                pendingRestore.Remove(volume);
            emptyPending.Clear();
            if (bulkAdmission)
                return;
            foreach (WaterVolume volume in WaterVolume.Instances)
                RetireMember(volume, floe);
        }

        private static void RetireMembers(WaterVolume volume)
        {
            if (!volume)
                return;
            for (int i = volume.m_inWater.Count - 1; i >= 0; --i)
            {
                Floating floating = volume.m_inWater[i] as Floating;
                IceFloe floe = floating ? floating.GetComponent<IceFloe>() : null;
                if (floe && managed.Contains(floe))
                    RetireMember(volume, floe);
            }
        }

        private static void RetireMember(WaterVolume volume, IceFloe floe)
        {
            if (!volume || !floe || !floe.m_floating)
                return;
            Floating floating = floe.m_floating;
            if (volume.m_inWater.Remove(floating))
            {
                if (floating.m_liquids[(int)LiquidType.Water] > 0 && floating.Decrement(LiquidType.Water) == 0)
                    floating.SetLiquidLevel(-10000f, LiquidType.Water, volume);
                floe.ManagedWaterVolumes.Add(volume);
            }
        }

        internal static void Release(IceFloe floe)
        {
            if (!managed.Remove(floe))
                return;
            Floating floating = floe.m_floating;
            Collider shape = floating ? floating.m_collider : null;
            foreach (WaterVolume volume in floe.ManagedWaterVolumes)
            {
                if (!volume || !volume.m_collider || !shape || !shape.enabled ||
                    !shape.gameObject.activeInHierarchy || volume.m_inWater.Contains(floating))
                    continue;
                if (!volume.isActiveAndEnabled || !volume.m_collider.enabled ||
                    !volume.m_collider.gameObject.activeInHierarchy)
                {
                    if (!pendingRestore.TryGetValue(volume, out HashSet<Floating> pending))
                        pendingRestore.Add(volume, pending = new HashSet<Floating>());
                    pending.Add(floating);
                    continue;
                }
                RestoreNativeIfOverlapping(volume, floating);
            }
            floe.ManagedWaterVolumes.Clear();
            if (floating && floating.m_liquids[(int)LiquidType.Water] == 0)
                floating.SetLiquidLevel(-10000f, LiquidType.Water, null);
        }

        private static void RestoreNativeIfOverlapping(WaterVolume volume, Floating floating)
        {
            Collider shape = floating ? floating.m_collider : null;
            if (!volume || !volume.m_collider || !volume.m_collider.enabled || !shape ||
                !shape.enabled || !shape.gameObject.activeInHierarchy ||
                volume.m_inWater.Contains(floating) ||
                !volume.m_collider.bounds.Intersects(shape.bounds) ||
                !Physics.ComputePenetration(volume.m_collider, volume.transform.position, volume.transform.rotation,
                    shape, shape.transform.position, shape.transform.rotation, out _, out _))
                return;
            volume.m_inWater.Add(floating);
            floating.Increment(LiquidType.Water);
            floating.SetLiquidLevel(volume.GetWaterSurface(floating.transform.position), LiquidType.Water, volume);
        }

        internal static void ForgetPending(Floating floating)
        {
            emptyPending.Clear();
            foreach (KeyValuePair<WaterVolume, HashSet<Floating>> entry in pendingRestore)
            {
                entry.Value.Remove(floating);
                if (entry.Value.Count == 0)
                    emptyPending.Add(entry.Key);
            }
            foreach (WaterVolume volume in emptyPending)
                pendingRestore.Remove(volume);
            emptyPending.Clear();
        }

        internal static void Reset()
        {
            bulkAdmission = false;
            managed.Clear();
        }

        internal static void ResetWorld()
        {
            Reset();
            pendingRestore.Clear();
            emptyPending.Clear();
        }

        private static IceFloe FloeFor(Collider trigger)
        {
            Rigidbody body = trigger ? trigger.attachedRigidbody : null;
            return body ? body.GetComponent<IceFloe>() : trigger ? trigger.GetComponentInParent<IceFloe>() : null;
        }

        private static Floating NativeFloating(IceFloe floe) =>
            floe.m_floating ? floe.m_floating : floe.GetComponent<Floating>();

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnTriggerEnter))]
        private static class WaterVolume_Enter_SeasonalFloe
        {
            private static bool Prefix(WaterVolume __instance, Collider triggerCollider)
            {
                IceFloe floe = FloeFor(triggerCollider);
                if (!floe)
                    return true;
                if (managed.Contains(floe))
                {
                    RetireMember(__instance, floe);
                    floe.ManagedWaterVolumes.Add(__instance);
                    return false;
                }
                // A late enter after explicit restoration must not increment twice.
                return triggerCollider.attachedRigidbody != null &&
                    !__instance.m_inWater.Contains(NativeFloating(floe));
            }
        }

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnTriggerExit))]
        private static class WaterVolume_Exit_SeasonalFloe
        {
            private static bool Prefix(WaterVolume __instance, Collider triggerCollider)
            {
                IceFloe floe = FloeFor(triggerCollider);
                if (!floe)
                    return true;
                if (managed.Contains(floe))
                {
                    RetireMember(__instance, floe);
                    // Keep the candidate volume until release: hiding the child can
                    // produce an exit even though its transform is still submerged.
                    return false;
                }
                // The managed entry was already decremented at admission.
                Floating floating = NativeFloating(floe);
                if (!__instance.m_inWater.Contains(floating))
                    return false;
                if (triggerCollider.attachedRigidbody)
                    return true;
                // A disabled child can report a late exit without an attached body.
                __instance.m_inWater.Remove(floating);
                if (floating.m_liquids[(int)LiquidType.Water] > 0 && floating.Decrement(LiquidType.Water) == 0)
                    floating.SetLiquidLevel(-10000f, LiquidType.Water, __instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnDestroy))]
        private static class WaterVolume_Destroy_SeasonalFloe
        {
            private static void Prefix(WaterVolume __instance)
            {
                pendingRestore.Remove(__instance);
                foreach (IceFloe floe in managed)
                    floe.ManagedWaterVolumes.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnEnable))]
        private static class WaterVolume_Enable_SeasonalFloe
        {
            private static void Postfix(WaterVolume __instance)
            {
                if (!pendingRestore.TryGetValue(__instance, out HashSet<Floating> pending))
                    return;
                pendingRestore.Remove(__instance);
                foreach (Floating floating in pending)
                {
                    IceFloe floe = floating ? floating.GetComponent<IceFloe>() : null;
                    if (floe && managed.Contains(floe))
                        floe.ManagedWaterVolumes.Add(__instance);
                    else
                        RestoreNativeIfOverlapping(__instance, floating);
                }
            }
        }
    }
}
