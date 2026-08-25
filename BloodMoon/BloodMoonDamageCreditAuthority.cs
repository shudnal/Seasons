using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDamageCreditAuthority
    {
        private const string RpcAuthorize = "Seasons.BloodMoon.DamageCreditAuthorize";
        private const string RpcConfirm = "Seasons.BloodMoon.DamageCreditConfirm";
        private const float AuthorizationLifetimeSeconds = 5f;

        private readonly struct AuthorizationKey
        {
            internal readonly long EventId;
            internal readonly ZDOID TargetId;
            internal readonly ZDOID SourceId;
            internal readonly BloodMoonCombatSourceType SourceType;
            internal readonly long PlayerId;

            internal AuthorizationKey(long eventId, ZDOID targetId, ZDOID sourceId, BloodMoonCombatSourceType sourceType, long playerId)
            {
                EventId = eventId;
                TargetId = targetId;
                SourceId = sourceId;
                SourceType = sourceType;
                PlayerId = playerId;
            }

            public override bool Equals(object obj)
            {
                return obj is AuthorizationKey other && EventId == other.EventId && TargetId == other.TargetId &&
                    SourceId == other.SourceId && SourceType == other.SourceType && PlayerId == other.PlayerId;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = EventId.GetHashCode();
                    hash = hash * 397 ^ TargetId.GetHashCode();
                    hash = hash * 397 ^ SourceId.GetHashCode();
                    hash = hash * 397 ^ (int)SourceType;
                    hash = hash * 397 ^ PlayerId.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class ConfirmedCredit
        {
            internal long EventId;
            internal long PlayerId;
        }

        private static readonly Dictionary<AuthorizationKey, Queue<float>> authorizations = new Dictionary<AuthorizationKey, Queue<float>>();
        private static readonly Dictionary<ZDOID, ConfirmedCredit> confirmedCredits = new Dictionary<ZDOID, ConfirmedCredit>();
        private static ZRoutedRpc registeredRpc;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            authorizations.Clear();
            confirmedCredits.Clear();
            rpc.Register<ZPackage>(RpcAuthorize, OnAuthorizeRpc);
            rpc.Register<ZPackage>(RpcConfirm, OnConfirmRpc);
        }

        internal static bool TryResolveCreditableSource(Character attacker, BloodMoonHitAttributionData attribution,
            out BloodMoonCombatSourceType sourceType, out ZDOID sourceId, out long playerId)
        {
            sourceType = BloodMoonCombatSourceType.None;
            sourceId = ZDOID.None;
            playerId = 0L;

            if (attribution != null)
            {
                if ((attribution.SourceType != BloodMoonCombatSourceType.Participant && attribution.SourceType != BloodMoonCombatSourceType.ParticipantSummon) ||
                    !BloodMoonHitAttribution.CanCredit(attribution) || attribution.SourceCharacterId.IsNone() || attribution.SourcePlayerId == 0L)
                    return false;

                sourceType = attribution.SourceType;
                sourceId = attribution.SourceCharacterId;
                playerId = attribution.SourcePlayerId;
                return true;
            }

            if (attacker == null || attacker.m_nview == null || !attacker.m_nview.IsValid() ||
                !BloodMoonInteractionRules.TryGetParticipantSourcePlayerId(attacker, out playerId) ||
                !BloodMoonInteractionRules.CanCreditProgress(playerId))
                return false;

            sourceId = attacker.GetZDOID();
            if (sourceId.IsNone())
                return false;
            sourceType = attacker is Player ? BloodMoonCombatSourceType.Participant : BloodMoonCombatSourceType.ParticipantSummon;
            return true;
        }

        internal static void AuthorizeHit(Character target, Character attacker, BloodMoonHitAttributionData attribution)
        {
            if (target == null || target.m_nview == null || !target.m_nview.IsValid() || !BloodMoonInteractionRules.IsBloodEnemy(target) ||
                !TryResolveCreditableSource(attacker, attribution, out BloodMoonCombatSourceType sourceType, out ZDOID sourceId, out long playerId))
                return;

            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            ZDOID targetId = target.GetZDOID();
            if (eventId < 0L || targetId.IsNone())
                return;

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                long localOwner = ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
                AcceptAuthorization(localOwner, eventId, targetId, sourceId, sourceType, playerId, trustedLocalSource: true);
                return;
            }

            if (ZRoutedRpc.instance == null)
                return;
            ZPackage package = CreatePackage(eventId, targetId, sourceId, sourceType, playerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAuthorize, package);
        }

        internal static void ConfirmActualDamage(Character target, long eventId, ZDOID sourceId, BloodMoonCombatSourceType sourceType, long playerId)
        {
            if (target == null || target.m_nview == null || !target.m_nview.IsValid() || !target.m_nview.IsOwner() ||
                eventId < 0L || sourceId.IsNone() || playerId == 0L)
                return;

            ZDOID targetId = target.GetZDOID();
            if (targetId.IsNone())
                return;

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                long localOwner = ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
                AcceptConfirmation(localOwner, eventId, targetId, sourceId, sourceType, playerId, trustedLocalTarget: true);
                return;
            }

            if (ZRoutedRpc.instance == null)
                return;
            ZPackage package = CreatePackage(eventId, targetId, sourceId, sourceType, playerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcConfirm, package);
        }

        internal static bool TryGetConfirmedCredit(long eventId, ZDOID targetId, out long playerId)
        {
            playerId = 0L;
            if (!confirmedCredits.TryGetValue(targetId, out ConfirmedCredit credit) || credit.EventId != eventId || credit.PlayerId == 0L)
                return false;
            playerId = credit.PlayerId;
            return true;
        }

        internal static void ConsumeConfirmedCredit(long eventId, ZDOID targetId)
        {
            if (confirmedCredits.TryGetValue(targetId, out ConfirmedCredit credit) && credit.EventId == eventId)
                confirmedCredits.Remove(targetId);
        }

        internal static void ResetRuntime()
        {
            authorizations.Clear();
            confirmedCredits.Clear();
            registeredRpc = null;
        }

        private static ZPackage CreatePackage(long eventId, ZDOID targetId, ZDOID sourceId, BloodMoonCombatSourceType sourceType, long playerId)
        {
            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(targetId);
            package.Write(sourceId);
            package.Write((int)sourceType);
            package.Write(playerId);
            return package;
        }

        private static bool TryReadPackage(ZPackage package, out long eventId, out ZDOID targetId, out ZDOID sourceId,
            out BloodMoonCombatSourceType sourceType, out long playerId)
        {
            eventId = -1L;
            targetId = ZDOID.None;
            sourceId = ZDOID.None;
            sourceType = BloodMoonCombatSourceType.None;
            playerId = 0L;
            if (package == null || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return false;
            eventId = package.ReadLong();
            targetId = package.ReadZDOID();
            sourceId = package.ReadZDOID();
            sourceType = (BloodMoonCombatSourceType)package.ReadInt();
            playerId = package.ReadLong();
            return true;
        }

        private static void OnAuthorizeRpc(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                !TryReadPackage(package, out long eventId, out ZDOID targetId, out ZDOID sourceId, out BloodMoonCombatSourceType sourceType, out long playerId))
                return;
            AcceptAuthorization(sender, eventId, targetId, sourceId, sourceType, playerId, trustedLocalSource: false);
        }

        private static void OnConfirmRpc(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                !TryReadPackage(package, out long eventId, out ZDOID targetId, out ZDOID sourceId, out BloodMoonCombatSourceType sourceType, out long playerId))
                return;
            AcceptConfirmation(sender, eventId, targetId, sourceId, sourceType, playerId, trustedLocalTarget: false);
        }

        private static void AcceptAuthorization(long sender, long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId, bool trustedLocalSource)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || playerId == 0L || targetId.IsNone() || sourceId.IsNone() ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive || ZDOMan.instance == null)
                return;

            ZDO targetZdo = ZDOMan.instance.GetZDO(targetId);
            ZDO sourceZdo = ZDOMan.instance.GetZDO(sourceId);
            if (targetZdo == null || sourceZdo == null || !BloodMoonEnemyDeathReports.IsEligibleBloodEnemyZdo(eventId, targetZdo))
                return;

            if (!ValidateSource(controller, sender, eventId, sourceZdo, sourceType, playerId, trustedLocalSource))
                return;

            PurgeExpiredAuthorizations();
            AuthorizationKey key = new AuthorizationKey(eventId, targetId, sourceId, sourceType, playerId);
            if (!authorizations.TryGetValue(key, out Queue<float> expirations))
            {
                expirations = new Queue<float>();
                authorizations[key] = expirations;
            }
            expirations.Enqueue(Time.realtimeSinceStartup + AuthorizationLifetimeSeconds);
        }

        private static void AcceptConfirmation(long sender, long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId, bool trustedLocalTarget)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || playerId == 0L || targetId.IsNone() || sourceId.IsNone() ||
                !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive || ZDOMan.instance == null)
                return;

            ZDO targetZdo = ZDOMan.instance.GetZDO(targetId);
            if (targetZdo == null || !BloodMoonEnemyDeathReports.IsEligibleBloodEnemyZdo(eventId, targetZdo) ||
                !trustedLocalTarget && targetZdo.GetOwner() != sender)
                return;

            PurgeExpiredAuthorizations();
            AuthorizationKey key = new AuthorizationKey(eventId, targetId, sourceId, sourceType, playerId);
            if (!authorizations.TryGetValue(key, out Queue<float> expirations) || expirations.Count == 0)
                return;

            expirations.Dequeue();
            if (expirations.Count == 0)
                authorizations.Remove(key);
            confirmedCredits[targetId] = new ConfirmedCredit { EventId = eventId, PlayerId = playerId };
        }

        private static bool ValidateSource(BloodMoonController controller, long sender, long eventId, ZDO sourceZdo,
            BloodMoonCombatSourceType sourceType, long playerId, bool trustedLocalSource)
        {
            if (sourceType != BloodMoonCombatSourceType.Participant && sourceType != BloodMoonCombatSourceType.ParticipantSummon)
                return false;
            if (!trustedLocalSource && sourceZdo.GetOwner() != sender)
                return false;

            if (sourceType == BloodMoonCombatSourceType.Participant)
            {
                if (sourceZdo.GetLong(ZDOVars.s_playerID, 0L) != playerId)
                    return false;
                if (!trustedLocalSource && controller.GetPeerForPlayer(playerId) != sender)
                    return false;
                return true;
            }

            if (!BloodMoonSummons.ValidateMarkedSummonZdo(sourceZdo, eventId, playerId))
                return false;
            long ownerPeer = controller.GetPeerForPlayer(playerId);
            return trustedLocalSource || ownerPeer == 0L || ownerPeer == sender;
        }

        private static void PurgeExpiredAuthorizations()
        {
            float now = Time.realtimeSinceStartup;
            foreach (AuthorizationKey key in authorizations.Keys.ToArray())
            {
                Queue<float> expirations = authorizations[key];
                while (expirations.Count > 0 && expirations.Peek() < now)
                    expirations.Dequeue();
                if (expirations.Count == 0)
                    authorizations.Remove(key);
            }
        }
    }
}
