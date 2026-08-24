using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRecovery
    {
        private sealed class RecoveryState
        {
            internal long EventId;
            internal bool StageOne = true;
            internal float Remaining = 15f;
        }

        private static readonly Dictionary<long, RecoveryState> recovery = new Dictionary<long, RecoveryState>();
        private static readonly HashSet<(long EventId, long PlayerId)> locallyExited = new HashSet<(long, long)>();

        internal static bool TryInterceptDefeat(Player player)
        {
            if (player == null)
                return false;
            long playerId = player.GetPlayerID();
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (eventId < 0 || locallyExited.Contains((eventId, playerId)))
                return false;

            locallyExited.Add((eventId, playerId));
            RestoreCombatResources(player);
            RemoveDamagingDots(player);
            recovery[playerId] = new RecoveryState { EventId = eventId, StageOne = true, Remaining = 15f };
            BloodCraft.CleanupLocal(player);
            BloodMoonStatus.RemoveLocal();
            LogInfo($"[BloodMoon.Recovery] Intercepted defeat for {player.GetPlayerName()} ({playerId}).");
            return true;
        }

        internal static void ApplyDefeat(Player player)
        {
            if (player == null)
                return;
            long playerId = player.GetPlayerID();
            locallyExited.Add((BloodMoonNetwork.ClientGlobal.EventId, playerId));
            RestoreCombatResources(player);
            RemoveDamagingDots(player);
            if (!recovery.ContainsKey(playerId))
                recovery[playerId] = new RecoveryState { EventId = BloodMoonNetwork.ClientGlobal.EventId, StageOne = true, Remaining = 15f };
            BloodMoonStatus.RemoveLocal();
        }

        internal static void ApplyWithdrawal(Player player)
        {
            if (player == null)
                return;
            locallyExited.Add((BloodMoonNetwork.ClientGlobal.EventId, player.GetPlayerID()));
            BloodMoonStatus.RemoveLocal();
        }

        internal static bool IsLocallyExited(long playerId)
        {
            return locallyExited.Contains((BloodMoonNetwork.ClientGlobal.EventId, playerId));
        }

        internal static float GetIncomingDamageMultiplier(Player player)
        {
            if (player == null || !recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state))
                return 1f;
            return state.StageOne ? 0f : 0.25f;
        }

        internal static void TickClientProtection(float dt)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state))
                return;

            state.Remaining -= dt;
            if (state.StageOne)
            {
                bool stabilized = player.IsOnGround() || player.IsSwimming() || player.IsAttached();
                if (stabilized || state.Remaining <= 0f)
                {
                    state.StageOne = false;
                    state.Remaining = 10f;
                    LogInfo($"[BloodMoon.Recovery] Stage 2 started for {player.GetPlayerName()}.");
                }
            }
            else if (state.Remaining <= 0f)
            {
                recovery.Remove(player.GetPlayerID());
                LogInfo($"[BloodMoon.Recovery] Protection ended for {player.GetPlayerName()}.");
            }
        }

        internal static void RemoveRested(Player player)
        {
            player?.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectRested);
        }

        internal static void ResetEvent(long eventId)
        {
            locallyExited.RemoveWhere(item => item.EventId == eventId);
        }

        private static void RestoreCombatResources(Player player)
        {
            player.SetHealth(player.GetMaxHealth());
            player.m_stamina = player.GetMaxStamina();
            player.m_eitr = player.GetMaxEitr();
        }

        private static void RemoveDamagingDots(Player player)
        {
            SEMan seman = player.GetSEMan();
            if (seman == null)
                return;
            List<StatusEffect> remove = seman.GetStatusEffects()
                .Where(effect => effect is SE_Burning || effect is SE_Poison || effect is SE_Smoke || effect is SE_Stats stats && stats.m_tickInterval > 0f && stats.m_healthPerTick < 0f)
                .ToList();
            foreach (StatusEffect effect in remove)
                seman.RemoveStatusEffect(effect.m_nameHash);
        }
    }
}
