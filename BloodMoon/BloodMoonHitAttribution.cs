using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal enum BloodMoonCombatSourceType
    {
        None,
        Participant,
        ParticipantSummon,
        BloodEnemy
    }

    internal sealed class BloodMoonHitAttributionData
    {
        internal long EventId;
        internal BloodMoonCombatSourceType SourceType;
        internal ZDOID SourceCharacterId;
        internal long SourcePlayerId;
    }

    internal static class BloodMoonHitAttribution
    {
        private const string RpcName = "Seasons.BloodMoon.HitAttribution";
        private const string EventMarker = "Seasons.BloodMoon.ProjectileEventId";
        private const string SourceTypeMarker = "Seasons.BloodMoon.ProjectileSourceType";
        private const string SourceCharacterMarker = "Seasons.BloodMoon.ProjectileSourceCharacter";
        private const string SourcePlayerMarker = "Seasons.BloodMoon.ProjectileSourcePlayer";
        private const float PendingLifetime = 3f;

        private sealed class PendingHit
        {
            internal BloodMoonHitAttributionData Attribution;
            internal float ExpiresAt;
        }

        private readonly struct PendingKey : IEquatable<PendingKey>
        {
            internal readonly ZDOID Target;
            internal readonly ZDOID Attacker;

            internal PendingKey(ZDOID target, ZDOID attacker)
            {
                Target = target;
                Attacker = attacker;
            }

            public bool Equals(PendingKey other) => Target == other.Target && Attacker == other.Attacker;
            public override bool Equals(object obj) => obj is PendingKey other && Equals(other);
            public override int GetHashCode() => (Target.GetHashCode() * 397) ^ Attacker.GetHashCode();
        }

        private static readonly Dictionary<int, BloodMoonHitAttributionData> runtimeSources = new Dictionary<int, BloodMoonHitAttributionData>();
        private static readonly Dictionary<PendingKey, Queue<PendingHit>> pendingHits = new Dictionary<PendingKey, Queue<PendingHit>>();
        private static ZRoutedRpc registeredRpc;

        internal static void RegisterRpc(ZRoutedRpc rpc)
        {
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;
            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcName, OnAttributionRpc);
            pendingHits.Clear();
        }

        internal static void Capture(UnityEngine.Object sourceObject, Character owner, ZNetView nview)
        {
            RegisterRpc(ZRoutedRpc.instance);
            if (sourceObject == null || owner == null || !BloodMoonInteractionRules.IsEventCombatLive)
                return;

            BloodMoonCombatSourceType sourceType = BloodMoonCombatSourceType.None;
            long playerId = 0L;
            if (owner is Player player && BloodMoonInteractionRules.IsActiveParticipant(player))
            {
                sourceType = BloodMoonCombatSourceType.Participant;
                playerId = player.GetPlayerID();
            }
            else if (BloodMoonSummons.IsBloodSummon(owner) && BloodMoonSummons.TryGetOwnerPlayerId(owner, out playerId))
            {
                sourceType = BloodMoonCombatSourceType.ParticipantSummon;
            }
            else if (BloodMoonInteractionRules.IsBloodEnemy(owner))
            {
                sourceType = BloodMoonCombatSourceType.BloodEnemy;
            }

            if (sourceType == BloodMoonCombatSourceType.None)
                return;

            BloodMoonHitAttributionData attribution = new BloodMoonHitAttributionData
            {
                EventId = BloodMoonNetwork.ClientGlobal.EventId,
                SourceType = sourceType,
                SourceCharacterId = owner.GetZDOID(),
                SourcePlayerId = playerId
            };
            runtimeSources[sourceObject.GetInstanceID()] = attribution;
            BloodMoonAttributionLifetime.Ensure(sourceObject);

            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null && nview.IsOwner())
            {
                zdo.Set(EventMarker, attribution.EventId);
                zdo.Set(SourceTypeMarker, (int)attribution.SourceType);
                zdo.Set(SourceCharacterMarker, attribution.SourceCharacterId);
                zdo.Set(SourcePlayerMarker, attribution.SourcePlayerId);
            }
        }

        internal static bool TryGet(UnityEngine.Object sourceObject, ZNetView nview, out BloodMoonHitAttributionData attribution)
        {
            attribution = null;
            if (sourceObject == null)
                return false;
            if (runtimeSources.TryGetValue(sourceObject.GetInstanceID(), out attribution))
                return attribution.EventId == BloodMoonNetwork.ClientGlobal.EventId;

            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null)
                return false;

            long eventId = zdo.GetLong(EventMarker, -1L);
            BloodMoonCombatSourceType sourceType = (BloodMoonCombatSourceType)zdo.GetInt(SourceTypeMarker, 0);
            if (eventId != BloodMoonNetwork.ClientGlobal.EventId || !IsKnownSourceType(sourceType))
                return false;

            attribution = new BloodMoonHitAttributionData
            {
                EventId = eventId,
                SourceType = sourceType,
                SourceCharacterId = zdo.GetZDOID(SourceCharacterMarker),
                SourcePlayerId = zdo.GetLong(SourcePlayerMarker, 0L)
            };
            runtimeSources[sourceObject.GetInstanceID()] = attribution;
            BloodMoonAttributionLifetime.Ensure(sourceObject);
            return true;
        }

        internal static bool CanDamage(BloodMoonHitAttributionData attribution, Character target)
        {
            if (attribution == null || target == null || attribution.EventId != BloodMoonNetwork.ClientGlobal.EventId || !BloodMoonInteractionRules.IsEventCombatLive)
                return false;
            return attribution.SourceType switch
            {
                BloodMoonCombatSourceType.Participant => BloodMoonInteractionRules.IsBloodEnemy(target),
                BloodMoonCombatSourceType.ParticipantSummon => BloodMoonInteractionRules.IsBloodEnemy(target),
                BloodMoonCombatSourceType.BloodEnemy => target is Player player && BloodMoonInteractionRules.IsActiveParticipant(player),
                _ => false
            };
        }

        internal static void QueueForTarget(BloodMoonHitAttributionData attribution, Character target)
        {
            RegisterRpc(ZRoutedRpc.instance);
            if (attribution == null || target == null || target.m_nview == null || !target.m_nview.IsValid())
                return;

            ZDO targetZdo = target.m_nview.GetZDO();
            if (targetZdo == null)
                return;

            long targetPeer = targetZdo.GetOwner();
            if (targetPeer == ZDOMan.GetSessionID())
            {
                AddPending(attribution, target.GetZDOID());
                return;
            }
            if (targetPeer == 0L || ZRoutedRpc.instance == null)
                return;

            ZPackage pkg = new ZPackage();
            pkg.Write(BloodMoonNetwork.ProtocolVersion);
            pkg.Write(attribution.EventId);
            pkg.Write(target.GetZDOID());
            pkg.Write(attribution.SourceCharacterId);
            pkg.Write((int)attribution.SourceType);
            pkg.Write(attribution.SourcePlayerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer, RpcName, pkg);
        }

        internal static bool TryConsume(Character target, HitData hit, out BloodMoonHitAttributionData attribution)
        {
            attribution = null;
            if (target == null || hit == null)
                return false;

            PrunePending();
            PendingKey key = new PendingKey(target.GetZDOID(), hit.m_attacker);
            if (!pendingHits.TryGetValue(key, out Queue<PendingHit> queue) || queue.Count == 0)
                return false;

            PendingHit pending = queue.Dequeue();
            if (queue.Count == 0)
                pendingHits.Remove(key);
            attribution = pending.Attribution;
            return attribution != null && attribution.EventId == BloodMoonNetwork.ClientGlobal.EventId;
        }

        internal static bool CanCredit(BloodMoonHitAttributionData attribution)
        {
            return attribution != null &&
                (attribution.SourceType == BloodMoonCombatSourceType.Participant || attribution.SourceType == BloodMoonCombatSourceType.ParticipantSummon) &&
                BloodMoonInteractionRules.CanCreditProgress(attribution.SourcePlayerId);
        }

        internal static void Cleanup(UnityEngine.Object sourceObject)
        {
            if (sourceObject != null)
                runtimeSources.Remove(sourceObject.GetInstanceID());
        }

        internal static void Reset()
        {
            runtimeSources.Clear();
            pendingHits.Clear();
        }

        private static void OnAttributionRpc(long sender, ZPackage pkg)
        {
            if (pkg == null || pkg.ReadInt() != BloodMoonNetwork.ProtocolVersion || ZDOMan.instance == null)
                return;

            BloodMoonHitAttributionData attribution = new BloodMoonHitAttributionData { EventId = pkg.ReadLong() };
            ZDOID target = pkg.ReadZDOID();
            attribution.SourceCharacterId = pkg.ReadZDOID();
            attribution.SourceType = (BloodMoonCombatSourceType)pkg.ReadInt();
            attribution.SourcePlayerId = pkg.ReadLong();

            if (attribution.EventId != BloodMoonNetwork.ClientGlobal.EventId || !IsKnownSourceType(attribution.SourceType) || target.IsNone() || attribution.SourceCharacterId.IsNone())
                return;

            ZDO targetZdo = ZDOMan.instance.GetZDO(target);
            if (targetZdo == null || targetZdo.GetOwner() != ZDOMan.GetSessionID())
                return;

            ZDO sourceZdo = ZDOMan.instance.GetZDO(attribution.SourceCharacterId);
            if (sourceZdo == null)
                return;

            if (attribution.SourceType == BloodMoonCombatSourceType.Participant)
            {
                if (sourceZdo.GetOwner() != sender || attribution.SourcePlayerId == 0L || sourceZdo.GetLong(ZDOVars.s_playerID, 0L) != attribution.SourcePlayerId)
                    return;
            }
            else if (attribution.SourceType == BloodMoonCombatSourceType.ParticipantSummon)
            {
                if (!BloodMoonSummons.ValidateMarkedSummonZdo(sourceZdo, attribution.EventId, attribution.SourcePlayerId))
                    return;
            }
            else
            {
                if (sourceZdo.GetOwner() != sender || attribution.SourcePlayerId != 0L)
                    return;
            }

            AddPending(attribution, target);
        }

        private static bool IsKnownSourceType(BloodMoonCombatSourceType sourceType)
        {
            return sourceType == BloodMoonCombatSourceType.Participant || sourceType == BloodMoonCombatSourceType.ParticipantSummon ||
                sourceType == BloodMoonCombatSourceType.BloodEnemy;
        }

        private static void AddPending(BloodMoonHitAttributionData attribution, ZDOID target)
        {
            PrunePending();
            PendingKey key = new PendingKey(target, attribution.SourceCharacterId);
            if (!pendingHits.TryGetValue(key, out Queue<PendingHit> queue))
            {
                queue = new Queue<PendingHit>();
                pendingHits.Add(key, queue);
            }
            queue.Enqueue(new PendingHit { Attribution = attribution, ExpiresAt = Time.realtimeSinceStartup + PendingLifetime });
        }

        private static void PrunePending()
        {
            float now = Time.realtimeSinceStartup;
            foreach (PendingKey key in new List<PendingKey>(pendingHits.Keys))
            {
                Queue<PendingHit> queue = pendingHits[key];
                while (queue.Count > 0 && queue.Peek().ExpiresAt < now)
                    queue.Dequeue();
                if (queue.Count == 0)
                    pendingHits.Remove(key);
            }
        }
    }

    internal sealed class BloodMoonAttributionLifetime : MonoBehaviour
    {
        private UnityEngine.Object source;

        internal static void Ensure(UnityEngine.Object sourceObject)
        {
            if (sourceObject is not Component component)
                return;
            BloodMoonAttributionLifetime lifetime = component.gameObject.GetComponent<BloodMoonAttributionLifetime>() ?? component.gameObject.AddComponent<BloodMoonAttributionLifetime>();
            lifetime.source = sourceObject;
        }

        private void OnDestroy()
        {
            BloodMoonHitAttribution.Cleanup(source);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class BloodMoonProjectileSetupAttributionPatch
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            BloodMoonHitAttribution.Capture(__instance, owner, __instance.m_nview);
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class BloodMoonAoeSetupAttributionPatch
    {
        private static void Postfix(Aoe __instance, Character owner)
        {
            BloodMoonHitAttribution.Capture(__instance, owner, __instance.m_nview);
        }
    }
}
