using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonBloodlust
    {
        private const string RpcHealingGrant = "Seasons.BloodMoon.LifestealGrant";
        private const float HealingWindowSeconds = 1f;

        private readonly struct HealingWindowEntry
        {
            internal readonly float At;
            internal readonly float Amount;

            internal HealingWindowEntry(float at, float amount)
            {
                At = at;
                Amount = amount;
            }
        }

        internal struct MovementState
        {
            internal bool Applied;
            internal float Speed;
            internal float WalkSpeed;
            internal float RunSpeed;
            internal float CrouchSpeed;
            internal float SwimSpeed;
        }

        private static readonly Dictionary<long, long> nextHealingGrantSequenceByPlayer = new Dictionary<long, long>();
        private static readonly Dictionary<long, long> lastHealingGrantSequenceByEvent = new Dictionary<long, long>();
        private static readonly Dictionary<long, Queue<HealingWindowEntry>> serverHealingWindows = new Dictionary<long, Queue<HealingWindowEntry>>();
        private static ZRoutedRpc registeredRpc;

        internal static void RegisterRpc()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(registeredRpc, rpc))
                return;

            registeredRpc = rpc;
            rpc.Register<ZPackage>(RpcHealingGrant, OnHealingGrant);
        }

        internal static float GetEarnedFactor(BloodMoonParticipantState participant)
        {
            if (participant == null)
                return 0f;
            if (participant.GoalReached)
                return 1f;
            return Mathf.Clamp01(participant.CombatPoints / Mathf.Max(1f, BloodMoonConfig.GoalPoints.Value));
        }

        internal static float GetFactor(BloodMoonParticipantState participant)
        {
            return participant != null && participant.IsCombatActive ? GetEarnedFactor(participant) : 0f;
        }

        internal static float GetCombatProgressPercent(BloodMoonParticipantState participant)
        {
            return GetEarnedFactor(participant) * 100f;
        }

        internal static float GetLocalFactor(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !BloodMoonInteractionRules.IsEventCombatLive || BloodMoonNetwork.ClientGlobal.EventId < 0L)
                return 0f;

            long playerId = player.GetPlayerID();
            if (playerId == 0L || BloodMoonRecovery.IsLocallyExited(playerId))
                return 0f;

            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            if (participant == null || participant.PlayerId != playerId || !participant.IsCombatActive ||
                BloodMoonParticipantDetails.ClientOwn.EventId != BloodMoonNetwork.ClientGlobal.EventId)
                return 0f;

            return GetFactor(participant);
        }

        internal static float GetOutgoingMultiplier(Player player)
        {
            return Interpolate(1f, BloodMoonConfig.BloodlustFullOutgoingDamageMultiplier.Value, GetLocalFactor(player));
        }

        internal static float GetIncomingMultiplier(Player player)
        {
            return Interpolate(1f, BloodMoonConfig.BloodlustFullIncomingDamageMultiplier.Value, GetLocalFactor(player));
        }

        internal static float GetMovementMultiplier(Player player)
        {
            return Interpolate(1f, BloodMoonConfig.BloodlustFullMovementSpeedMultiplier.Value, GetLocalFactor(player));
        }

        internal static float GetLifestealFraction(BloodMoonParticipantState participant)
        {
            return Interpolate(0f, BloodMoonConfig.BloodlustFullLifestealFraction.Value, GetFactor(participant));
        }

        internal static void AcceptAuthorizedDamage(long eventId, long sourcePlayerId, float actualDamage)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !IsFinitePositive(actualDamage))
                return;

            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null || eventId != state.EventId || !state.BloodBehaviorEnabled || !state.IsCombatLive || sourcePlayerId == 0L ||
                !state.Participants.TryGetValue(sourcePlayerId, out BloodMoonParticipantState participant) || !participant.IsCombatActive)
                return;

            float fraction = GetLifestealFraction(participant);
            if (fraction <= 0f)
                return;

            float requestedHealing = actualDamage * fraction;
            if (!IsFinitePositive(requestedHealing) || !TryApplyServerHealingCap(sourcePlayerId, requestedHealing, out float grantedHealing))
                return;

            SendHealingGrant(controller, eventId, sourcePlayerId, grantedHealing);
        }

        internal static void ResetRuntime()
        {
            nextHealingGrantSequenceByPlayer.Clear();
            lastHealingGrantSequenceByEvent.Clear();
            serverHealingWindows.Clear();
        }

        internal static MovementState ApplyWalkingMovement(Player player)
        {
            MovementState state = default;
            float multiplier = GetMovementMultiplier(player);
            if (player == null || Mathf.Approximately(multiplier, 1f))
                return state;

            state.Applied = true;
            state.Speed = player.m_speed;
            state.WalkSpeed = player.m_walkSpeed;
            state.RunSpeed = player.m_runSpeed;
            state.CrouchSpeed = player.m_crouchSpeed;
            player.m_speed *= multiplier;
            player.m_walkSpeed *= multiplier;
            player.m_runSpeed *= multiplier;
            player.m_crouchSpeed *= multiplier;
            return state;
        }

        internal static MovementState ApplySwimmingMovement(Player player)
        {
            MovementState state = default;
            float multiplier = GetMovementMultiplier(player);
            if (player == null || Mathf.Approximately(multiplier, 1f))
                return state;

            state.Applied = true;
            state.SwimSpeed = player.m_swimSpeed;
            player.m_swimSpeed *= multiplier;
            return state;
        }

        internal static void RestoreWalkingMovement(Player player, MovementState state)
        {
            if (player == null || !state.Applied)
                return;
            player.m_speed = state.Speed;
            player.m_walkSpeed = state.WalkSpeed;
            player.m_runSpeed = state.RunSpeed;
            player.m_crouchSpeed = state.CrouchSpeed;
        }

        internal static void RestoreSwimmingMovement(Player player, MovementState state)
        {
            if (player == null || !state.Applied)
                return;
            player.m_swimSpeed = state.SwimSpeed;
        }

        private static float Interpolate(float neutral, float full, float factor)
        {
            if (!IsFinite(full))
                full = neutral;
            return Mathf.Lerp(neutral, Mathf.Max(0f, full), Mathf.Clamp01(factor));
        }

        private static bool TryApplyServerHealingCap(long playerId, float requested, out float granted)
        {
            granted = 0f;
            if (!TryGetPlayerMaxHealth(playerId, out float maxHealth) || maxHealth <= 0f)
                return false;

            float fractionPerSecond = Mathf.Max(0f, BloodMoonConfig.BloodlustLifestealMaximumHealthPerSecond.Value);
            float maximum = maxHealth * fractionPerSecond;
            if (!IsFinitePositive(maximum))
                return false;

            if (!serverHealingWindows.TryGetValue(playerId, out Queue<HealingWindowEntry> window))
            {
                window = new Queue<HealingWindowEntry>();
                serverHealingWindows[playerId] = window;
            }

            float now = Time.realtimeSinceStartup;
            float used = 0f;
            while (window.Count > 0 && now - window.Peek().At >= HealingWindowSeconds)
                window.Dequeue();
            foreach (HealingWindowEntry entry in window)
                used += entry.Amount;

            granted = Mathf.Min(requested, Mathf.Max(0f, maximum - used));
            if (!IsFinitePositive(granted))
                return false;

            window.Enqueue(new HealingWindowEntry(now, granted));
            return true;
        }

        private static bool TryGetPlayerMaxHealth(long playerId, out float maxHealth)
        {
            maxHealth = 0f;
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
            {
                maxHealth = localPlayer.GetMaxHealth();
                return IsFinitePositive(maxHealth);
            }

            if (ZNet.instance == null || ZDOMan.instance == null)
                return false;
            foreach (ZNetPeer peer in ZNet.instance.m_peers)
            {
                if (peer == null || peer.m_characterID.IsNone())
                    continue;
                ZDO playerZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (playerZdo == null || playerZdo.GetLong(ZDOVars.s_playerID, 0L) != playerId)
                    continue;
                maxHealth = playerZdo.GetFloat(ZDOVars.s_maxHealth, 0f);
                return IsFinitePositive(maxHealth);
            }
            return false;
        }

        private static void SendHealingGrant(BloodMoonController controller, long eventId, long playerId, float amount)
        {
            if (controller == null || !IsFinitePositive(amount))
                return;

            long sequence = nextHealingGrantSequenceByPlayer.TryGetValue(playerId, out long current) ? current + 1L : 1L;
            nextHealingGrantSequenceByPlayer[playerId] = sequence;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
            {
                ApplyHealingGrant(eventId, playerId, sequence, amount);
                return;
            }

            long peerId = controller.GetPeerForPlayer(playerId);
            if (peerId == 0L || ZRoutedRpc.instance == null)
                return;

            ZPackage package = new ZPackage();
            package.Write(BloodMoonNetwork.ProtocolVersion);
            package.Write(eventId);
            package.Write(playerId);
            package.Write(sequence);
            package.Write(amount);
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcHealingGrant, package);
        }

        private static void OnHealingGrant(long sender, ZPackage package)
        {
            if (package == null || ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null ||
                sender != ZRoutedRpc.instance.GetServerPeerID() || package.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                return;

            long eventId = package.ReadLong();
            long playerId = package.ReadLong();
            long sequence = package.ReadLong();
            float amount = package.ReadSingle();
            ApplyHealingGrant(eventId, playerId, sequence, amount);
        }

        private static void ApplyHealingGrant(long eventId, long playerId, long sequence, float amount)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || eventId != BloodMoonNetwork.ClientGlobal.EventId ||
                !BloodMoonInteractionRules.IsEventCombatLive || GetLocalFactor(player) <= 0f || sequence <= 0L || !IsFinitePositive(amount))
                return;

            if (lastHealingGrantSequenceByEvent.TryGetValue(eventId, out long previousSequence) && sequence <= previousSequence)
                return;
            lastHealingGrantSequenceByEvent[eventId] = sequence;
            player.Heal(amount);
        }

        private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateWalking))]
    internal static class BloodMoonPlayerWalkingSpeedPatch
    {
        private static void Prefix(Character __instance, out BloodMoonBloodlust.MovementState __state)
        {
            __state = __instance is Player player ? BloodMoonBloodlust.ApplyWalkingMovement(player) : default;
        }

        private static void Postfix(Character __instance, BloodMoonBloodlust.MovementState __state)
        {
            if (__instance is Player player)
                BloodMoonBloodlust.RestoreWalkingMovement(player, __state);
        }

        private static Exception Finalizer(Exception __exception, Character __instance, BloodMoonBloodlust.MovementState __state)
        {
            if (__instance is Player player)
                BloodMoonBloodlust.RestoreWalkingMovement(player, __state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateSwimming))]
    internal static class BloodMoonPlayerSwimmingSpeedPatch
    {
        private static void Prefix(Character __instance, out BloodMoonBloodlust.MovementState __state)
        {
            __state = __instance is Player player ? BloodMoonBloodlust.ApplySwimmingMovement(player) : default;
        }

        private static void Postfix(Character __instance, BloodMoonBloodlust.MovementState __state)
        {
            if (__instance is Player player)
                BloodMoonBloodlust.RestoreSwimmingMovement(player, __state);
        }

        private static Exception Finalizer(Exception __exception, Character __instance, BloodMoonBloodlust.MovementState __state)
        {
            if (__instance is Player player)
                BloodMoonBloodlust.RestoreSwimmingMovement(player, __state);
            return __exception;
        }
    }
}
