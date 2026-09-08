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

        private sealed class PendingConfirmation
        {
            internal float ExpiresAt;
            internal long Order;
            internal float ActualDamage;
            internal bool Lethal;
        }

        private sealed class ConfirmedCredit
        {
            internal long EventId;
            internal long PlayerId;
            internal long ConfirmationOrder;
        }

        private static readonly Dictionary<AuthorizationKey, Queue<float>> authorizations = new Dictionary<AuthorizationKey, Queue<float>>();
        private static readonly Dictionary<AuthorizationKey, Queue<PendingConfirmation>> pendingConfirmations = new Dictionary<AuthorizationKey, Queue<PendingConfirmation>>();
        private static readonly Dictionary<ZDOID, ConfirmedCredit> confirmedCredits = new Dictionary<ZDOID, ConfirmedCredit>();
        private static ZRoutedRpc registeredRpc;
        private static long nextConfirmationOrder;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            authorizations.Clear();
            pendingConfirmations.Clear();
            confirmedCredits.Clear();
            nextConfirmationOrder = 0L;
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
                // The target owner only preserves immutable source identity here. Participant activity is
                // authoritative on the server; a delayed routing snapshot must not suppress a valid
                // confirmation before the server can make that decision.
                if ((attribution.SourceType != BloodMoonCombatSourceType.Participant && attribution.SourceType != BloodMoonCombatSourceType.ParticipantSummon) ||
                    attribution.EventId != BloodMoonNetwork.ClientGlobal.EventId || attribution.SourceCharacterId.IsNone() || attribution.SourcePlayerId == 0L)
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
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAuthorize,
                CreateAuthorizationPackage(eventId, targetId, sourceId, sourceType, playerId));
        }

        internal static void ConfirmActualDamage(Character target, long eventId, ZDOID sourceId, BloodMoonCombatSourceType sourceType,
            long playerId, float actualDamage)
        {
            if (target == null || target.m_nview == null || !target.m_nview.IsValid() || !target.m_nview.IsOwner() ||
                eventId < 0L || sourceId.IsNone() || playerId == 0L || !IsFinitePositive(actualDamage))
                return;

            ZDOID targetId = target.GetZDOID();
            if (targetId.IsNone())
                return;

            // Called from Character.SetHealth postfix, before the enclosing RPC_Damage reaches
            // CheckDeath. This is the stable point at which the target owner knows whether this exact
            // positive HP loss is the lethal loss rather than merely the most recent credited hit.
            bool lethal = target.GetHealth() <= 0f;

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                long localOwner = ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
                AcceptConfirmation(localOwner, eventId, targetId, sourceId, sourceType, playerId, actualDamage, lethal);
                return;
            }

            if (ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcConfirm,
                CreateConfirmationPackage(eventId, targetId, sourceId, sourceType, playerId, actualDamage, lethal));
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
            pendingConfirmations.Clear();
            confirmedCredits.Clear();
            nextConfirmationOrder = 0L;
        }

        private static ZPackage CreateAuthorizationPackage(long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId)
        {
            ZPackage package = new ZPackage();
            WriteCommon(package, eventId, targetId, sourceId, sourceType, playerId);
            return package;
        }

        private static ZPackage CreateConfirmationPackage(long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId, float actualDamage, bool lethal)
        {
            ZPackage package = new ZPackage();
            WriteCommon(package, eventId, targetId, sourceId, sourceType, playerId);
            package.Write(actualDamage);
            package.Write(lethal);
            return package;
        }

        private static void WriteCommon(ZPackage package, long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId)
        {
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(targetId);
            package.Write(sourceId);
            package.Write((int)sourceType);
            package.Write(playerId);
        }

        private static bool TryReadCommon(ZPackage package, out long eventId, out ZDOID targetId, out ZDOID sourceId,
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
                !TryReadCommon(package, out long eventId, out ZDOID targetId, out ZDOID sourceId, out BloodMoonCombatSourceType sourceType, out long playerId))
                return;
            AcceptAuthorization(sender, eventId, targetId, sourceId, sourceType, playerId, trustedLocalSource: false);
        }

        private static void OnConfirmRpc(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                !TryReadCommon(package, out long eventId, out ZDOID targetId, out ZDOID sourceId, out BloodMoonCombatSourceType sourceType, out long playerId))
                return;
            float actualDamage = package.ReadSingle();
            bool lethal = package.ReadBool();
            AcceptConfirmation(sender, eventId, targetId, sourceId, sourceType, playerId, actualDamage, lethal);
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
            bool targetIsKnownBloodEnemy = targetZdo != null
                ? BloodMoonEnemyDeathReports.IsEligibleBloodEnemyZdo(eventId, targetZdo)
                : BloodMoonEnemyDeathPending.TryGetRetainedDeathEvidence(eventId, targetId, out _);
            if (!targetIsKnownBloodEnemy || !ValidateSource(controller, sender, eventId, sourceZdo, sourceType, playerId, trustedLocalSource))
                return;

            PurgeExpired();
            AuthorizationKey key = new AuthorizationKey(eventId, targetId, sourceId, sourceType, playerId);
            if (TryConsumePendingConfirmation(key, out PendingConfirmation confirmation))
            {
                ApplyMatchedDamage(key, confirmation.Order, confirmation.ActualDamage, confirmation.Lethal);
                return;
            }

            if (!authorizations.TryGetValue(key, out Queue<float> expirations))
            {
                expirations = new Queue<float>();
                authorizations[key] = expirations;
            }
            expirations.Enqueue(Time.realtimeSinceStartup + AuthorizationLifetimeSeconds);
        }

        private static void AcceptConfirmation(long sender, long eventId, ZDOID targetId, ZDOID sourceId,
            BloodMoonCombatSourceType sourceType, long playerId, float actualDamage, bool lethal)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || !state.IsCombatLive || state.EventId != eventId || playerId == 0L || targetId.IsNone() || sourceId.IsNone() ||
                !IsFinitePositive(actualDamage) || !state.Participants.TryGetValue(playerId, out BloodMoonParticipantState participant) ||
                !participant.IsCombatActive || ZDOMan.instance == null)
                return;

            ZDO targetZdo = ZDOMan.instance.GetZDO(targetId);
            // The sender was the target owner when it observed the positive HP loss. Normal ZDO
            // ownership or the ordinary dead-ZDO cleanup may complete before this routed RPC reaches
            // the server. A bounded retained death record is therefore an equivalent target-evidence
            // source for the already observed lethal hit.
            bool targetIsKnownBloodEnemy = targetZdo != null
                ? BloodMoonEnemyDeathReports.IsEligibleBloodEnemyZdo(eventId, targetZdo)
                : BloodMoonEnemyDeathPending.TryGetRetainedDeathEvidence(eventId, targetId, out _);
            if (!targetIsKnownBloodEnemy)
                return;

            PurgeExpired();
            AuthorizationKey key = new AuthorizationKey(eventId, targetId, sourceId, sourceType, playerId);
            PendingConfirmation confirmation = new PendingConfirmation
            {
                ExpiresAt = Time.realtimeSinceStartup + AuthorizationLifetimeSeconds,
                Order = ++nextConfirmationOrder,
                ActualDamage = actualDamage,
                Lethal = lethal
            };
            if (TryConsumeAuthorization(key))
            {
                ApplyMatchedDamage(key, confirmation.Order, confirmation.ActualDamage, confirmation.Lethal);
                return;
            }

            if (!pendingConfirmations.TryGetValue(key, out Queue<PendingConfirmation> confirmations))
            {
                confirmations = new Queue<PendingConfirmation>();
                pendingConfirmations[key] = confirmations;
            }
            confirmations.Enqueue(confirmation);
        }

        private static bool ValidateSource(BloodMoonController controller, long sender, long eventId, ZDO sourceZdo,
            BloodMoonCombatSourceType sourceType, long playerId, bool trustedLocalSource)
        {
            if (sourceType != BloodMoonCombatSourceType.Participant && sourceType != BloodMoonCombatSourceType.ParticipantSummon)
                return false;

            if (sourceType == BloodMoonCombatSourceType.Participant)
            {
                // Player ownership can migrate before the routed authorization reaches the server. The
                // stable binding is the routed peer <-> participant identity; when the source ZDO still
                // exists, also require its durable player id to match.
                if (sourceZdo != null && sourceZdo.GetLong(ZDOVars.s_playerID, 0L) != playerId)
                    return false;
                return trustedLocalSource || controller.GetPeerForPlayer(playerId) == sender;
            }

            // Summon ownership may migrate for the same reason. Its durable event/owner marker, rather
            // than receive-time ZDO ownership, identifies the supported source. Unlike a Player source,
            // an absent summon ZDO has no independent server-side identity proof.
            return sourceZdo != null && BloodMoonSummons.ValidateMarkedSummonZdo(sourceZdo, eventId, playerId);
        }

        private static bool TryConsumeAuthorization(AuthorizationKey key)
        {
            if (!authorizations.TryGetValue(key, out Queue<float> expirations) || expirations.Count == 0)
                return false;
            expirations.Dequeue();
            if (expirations.Count == 0)
                authorizations.Remove(key);
            return true;
        }

        private static bool TryConsumePendingConfirmation(AuthorizationKey key, out PendingConfirmation confirmation)
        {
            confirmation = null;
            if (!pendingConfirmations.TryGetValue(key, out Queue<PendingConfirmation> confirmations) || confirmations.Count == 0)
                return false;
            confirmation = confirmations.Dequeue();
            if (confirmations.Count == 0)
                pendingConfirmations.Remove(key);
            return true;
        }

        private static void ApplyMatchedDamage(AuthorizationKey key, long confirmationOrder, float actualDamage, bool lethal)
        {
            if (key.SourceType == BloodMoonCombatSourceType.Participant)
                BloodMoonBloodlust.AcceptAuthorizedDamage(key.EventId, key.PlayerId, actualDamage);

            // Death progress may only be signed by the matched HP loss which actually crossed health to
            // zero. Older non-lethal hits must never remain eligible to credit a later post-exit death.
            if (!lethal)
                return;

            if (confirmedCredits.TryGetValue(key.TargetId, out ConfirmedCredit existing) &&
                existing.EventId == key.EventId && existing.ConfirmationOrder >= confirmationOrder)
                return;
            confirmedCredits[key.TargetId] = new ConfirmedCredit
            {
                EventId = key.EventId,
                PlayerId = key.PlayerId,
                ConfirmationOrder = confirmationOrder
            };
        }

        private static void PurgeExpired()
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

            foreach (AuthorizationKey key in pendingConfirmations.Keys.ToArray())
            {
                Queue<PendingConfirmation> confirmations = pendingConfirmations[key];
                while (confirmations.Count > 0 && confirmations.Peek().ExpiresAt < now)
                    confirmations.Dequeue();
                if (confirmations.Count == 0)
                    pendingConfirmations.Remove(key);
            }
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }
}
