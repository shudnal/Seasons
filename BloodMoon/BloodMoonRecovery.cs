using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonRecovery
    {
        private const string RecoveryDataKey = "Seasons.BloodMoon.Recovery";
        private const string DefeatedDataKey = "Seasons.BloodMoon.Defeated";
        private const string RestedRemovalPrefix = "Seasons.BloodMoon.RestedRemoved.";
        private const float StageOneDuration = 15f;
        private const float StageTwoDuration = 10f;
        private const float DefeatReportRetrySeconds = 2f;

        [Serializable]
        private sealed class PersistedRecovery
        {
            public long WorldUid;
            public long EventId;
            public bool StageOne;
            public float Remaining;
            public long SavedUtcTicks;
        }

        [Serializable]
        private sealed class PersistedDefeat
        {
            public long WorldUid;
            public long EventId;
        }

        private sealed class RecoveryState
        {
            internal long WorldUid;
            internal long EventId;
            internal bool StageOne;
            internal float Remaining;
            internal float SaveTimer;
        }

        private static readonly Dictionary<long, RecoveryState> recovery = new Dictionary<long, RecoveryState>();
        private static readonly HashSet<(long EventId, long PlayerId)> locallyExited = new HashSet<(long, long)>();
        private static long defeatReportEventId = -1L;
        private static float defeatReportTimer;

        internal static bool TryInterceptDefeat(Player player)
        {
            if (player == null)
                return false;
            long playerId = player.GetPlayerID();
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (eventId < 0L || IsLocallyExited(playerId))
                return false;

            MarkDefeated(player, eventId, immediateReportAlreadyScheduled: true);
            RestoreCombatResources(player);
            RemoveDamagingDots(player);
            BeginRecovery(player, eventId, resetExisting: true);
            BloodCraft.CleanupLocal(player);
            BloodMoonStatus.RemoveLocal();
            LogInfo($"[BloodMoon][event:{eventId}][player:{playerId}] Intercepted Defeated and started recovery.");
            return true;
        }

        internal static void ApplyDefeat(Player player)
        {
            if (player == null)
                return;
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            MarkDefeated(player, eventId, immediateReportAlreadyScheduled: false);
            RestoreCombatResources(player);
            RemoveDamagingDots(player);
            BeginRecovery(player, eventId, resetExisting: false);
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
            Player player = Player.m_localPlayer;
            if (player != null && player.GetPlayerID() == playerId)
                EnsureDefeatLoaded(player);
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            return eventId >= 0L && locallyExited.Contains((eventId, playerId));
        }

        internal static bool HasProtection(Player player)
        {
            EnsureLoaded(player);
            if (player == null || !recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state))
                return false;
            long worldUid = GetCurrentWorldUid();
            return worldUid != 0L && state.WorldUid == worldUid;
        }

        internal static float GetIncomingDamageMultiplier(Player player)
        {
            EnsureLoaded(player);
            if (player == null || !recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state) || state.WorldUid != GetCurrentWorldUid())
                return 1f;
            return state.StageOne ? 0f : 0.25f;
        }

        internal static bool ApplyIncomingDamageProtection(Player player, HitData hit)
        {
            if (player == null || hit == null || !HasProtection(player))
                return true;

            float multiplier = GetIncomingDamageMultiplier(player);
            if (multiplier <= 0f)
                return false;
            if (!Mathf.Approximately(multiplier, 1f))
                hit.ApplyModifier(multiplier);
            return true;
        }

        internal static bool TryGetStatus(Player player, out bool stageOne, out float remaining, out float multiplier)
        {
            EnsureLoaded(player);
            stageOne = false;
            remaining = 0f;
            multiplier = 1f;
            if (player == null || !recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state) || state.WorldUid != GetCurrentWorldUid())
                return false;
            stageOne = state.StageOne;
            remaining = Mathf.Max(0f, state.Remaining);
            multiplier = state.StageOne ? 0f : 0.25f;
            return true;
        }

        internal static void TickClientProtection(float dt)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            EnsureDefeatLoaded(player);
            TickDefeatReport(player, dt);
            EnsureLoaded(player);
            if (!recovery.TryGetValue(player.GetPlayerID(), out RecoveryState state))
                return;

            long worldUid = GetCurrentWorldUid();
            if (worldUid == 0L || state.WorldUid != worldUid)
                return;

            state.Remaining -= Mathf.Max(0f, dt);
            state.SaveTimer -= Mathf.Max(0f, dt);

            if (state.StageOne)
            {
                bool stabilized = player.IsOnGround() || player.IsSwimming() || player.IsAttached();
                if (stabilized || state.Remaining <= 0f)
                {
                    float overdue = Mathf.Min(0f, state.Remaining);
                    state.StageOne = false;
                    state.Remaining = Mathf.Max(0f, StageTwoDuration + overdue);
                    state.SaveTimer = 0f;
                    LogInfo($"[BloodMoon][event:{state.EventId}][player:{player.GetPlayerID()}] Recovery stage 2 started.");
                }
            }

            if (!state.StageOne && state.Remaining <= 0f)
            {
                EndRecovery(player);
                return;
            }

            if (state.SaveTimer <= 0f)
            {
                state.SaveTimer = 1f;
                Persist(player, state);
            }
        }

        internal static void RemoveRested(Player player)
        {
            if (player == null)
                return;

            long worldUid = GetCurrentWorldUid();
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (worldUid == 0L || eventId < 0L)
            {
                player.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectRested);
                return;
            }

            string key = RestedRemovalPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
            if (player.m_customData.ContainsKey(key))
                return;

            player.GetSEMan()?.RemoveStatusEffect(SEMan.s_statusEffectRested);
            player.m_customData[key] = "1";
        }

        internal static void ResetEvent(long eventId)
        {
            locallyExited.RemoveWhere(item => item.EventId == eventId);
            Player player = Player.m_localPlayer;
            if (player != null)
            {
                locallyExited.RemoveWhere(item => item.PlayerId == player.GetPlayerID());
                player.m_customData.Remove(DefeatedDataKey);
            }
            defeatReportEventId = -1L;
            defeatReportTimer = 0f;
        }

        internal static void ResetRuntime()
        {
            recovery.Clear();
            locallyExited.Clear();
            defeatReportEventId = -1L;
            defeatReportTimer = 0f;
            BloodMoonStatus.RemoveRecoveryLocal();
        }

        private static void BeginRecovery(Player player, long eventId, bool resetExisting)
        {
            if (player == null || eventId < 0L)
                return;
            long worldUid = GetCurrentWorldUid();
            if (worldUid == 0L)
                return;

            long playerId = player.GetPlayerID();
            EnsureLoaded(player);
            if (!resetExisting && recovery.TryGetValue(playerId, out RecoveryState existing) && existing.WorldUid == worldUid && existing.EventId == eventId)
                return;

            RecoveryState state = new RecoveryState
            {
                WorldUid = worldUid,
                EventId = eventId,
                StageOne = true,
                Remaining = StageOneDuration,
                SaveTimer = 0f
            };
            recovery[playerId] = state;
            Persist(player, state);
        }

        private static void EnsureLoaded(Player player)
        {
            if (player == null)
                return;
            EnsureDefeatLoaded(player);
            if (recovery.ContainsKey(player.GetPlayerID()))
                return;
            if (!player.m_customData.TryGetValue(RecoveryDataKey, out string json) || string.IsNullOrWhiteSpace(json))
                return;

            PersistedRecovery persisted;
            try
            {
                persisted = JsonConvert.DeserializeObject<PersistedRecovery>(json);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Recovery] Invalid persisted recovery record removed: {ex.Message}");
                player.m_customData.Remove(RecoveryDataKey);
                return;
            }

            long worldUid = GetCurrentWorldUid();
            if (worldUid == 0L)
                return;
            if (persisted == null || persisted.WorldUid != worldUid || persisted.EventId < 0L || persisted.Remaining <= 0f || persisted.SavedUtcTicks <= 0L)
            {
                player.m_customData.Remove(RecoveryDataKey);
                return;
            }

            double elapsed = Math.Max(0d, TimeSpan.FromTicks(Math.Max(0L, DateTime.UtcNow.Ticks - persisted.SavedUtcTicks)).TotalSeconds);
            bool stageOne = persisted.StageOne;
            float remaining = persisted.Remaining - (float)elapsed;
            if (stageOne && remaining <= 0f)
            {
                stageOne = false;
                remaining = StageTwoDuration + remaining;
            }
            if (remaining <= 0f)
            {
                player.m_customData.Remove(RecoveryDataKey);
                return;
            }

            recovery[player.GetPlayerID()] = new RecoveryState
            {
                WorldUid = persisted.WorldUid,
                EventId = persisted.EventId,
                StageOne = stageOne,
                Remaining = remaining,
                SaveTimer = 0f
            };
        }

        private static void EnsureDefeatLoaded(Player player)
        {
            if (player == null || !player.m_customData.TryGetValue(DefeatedDataKey, out string json) || string.IsNullOrWhiteSpace(json))
                return;

            PersistedDefeat persisted;
            try
            {
                persisted = JsonConvert.DeserializeObject<PersistedDefeat>(json);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Recovery] Invalid persisted Defeated marker removed: {ex.Message}");
                player.m_customData.Remove(DefeatedDataKey);
                return;
            }

            long worldUid = GetCurrentWorldUid();
            if (persisted == null || persisted.WorldUid == 0L || persisted.EventId < 0L || worldUid == 0L || persisted.WorldUid != worldUid)
                return;

            long playerId = player.GetPlayerID();
            if (locallyExited.Add((persisted.EventId, playerId)))
            {
                defeatReportEventId = persisted.EventId;
                defeatReportTimer = 0f;
                LogInfo($"[BloodMoon][event:{persisted.EventId}][player:{playerId}] Restored durable local Defeated marker.");
            }
        }

        private static void MarkDefeated(Player player, long eventId, bool immediateReportAlreadyScheduled)
        {
            long worldUid = GetCurrentWorldUid();
            long playerId = player.GetPlayerID();
            locallyExited.Add((eventId, playerId));
            if (worldUid != 0L && eventId >= 0L)
            {
                player.m_customData[DefeatedDataKey] = JsonConvert.SerializeObject(new PersistedDefeat
                {
                    WorldUid = worldUid,
                    EventId = eventId
                });
            }
            defeatReportEventId = eventId;
            defeatReportTimer = immediateReportAlreadyScheduled ? DefeatReportRetrySeconds : 0f;
        }

        private static void TickDefeatReport(Player player, float dt)
        {
            if (player == null || ZRoutedRpc.instance == null)
                return;

            long currentEventId = BloodMoonNetwork.ClientGlobal.EventId;
            long playerId = player.GetPlayerID();
            if (currentEventId < 0L || currentEventId != defeatReportEventId || !locallyExited.Contains((currentEventId, playerId)))
                return;

            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            if (phase != BloodMoonEventPhase.Active && phase != BloodMoonEventPhase.AutoCompleting ||
                BloodMoonNetwork.ClientParticipants.EventId != currentEventId || BloodMoonNetwork.ClientParticipants.Participants == null)
                return;

            BloodMoonParticipantState routing = BloodMoonNetwork.ClientParticipants.Participants.FirstOrDefault(item => item.PlayerId == playerId);
            if (routing == null || !routing.IsCombatActive)
                return;

            defeatReportTimer -= Mathf.Max(0f, dt);
            if (defeatReportTimer > 0f)
                return;

            defeatReportTimer = DefeatReportRetrySeconds;
            BloodMoonNetwork.SendDefeated(currentEventId, playerId);
            LogInfo($"[BloodMoon][event:{currentEventId}][player:{playerId}] Re-sent Defeated notification while server routing still reports active participation.");
        }

        private static void Persist(Player player, RecoveryState state)
        {
            if (player == null || state == null)
                return;
            player.m_customData[RecoveryDataKey] = JsonConvert.SerializeObject(new PersistedRecovery
            {
                WorldUid = state.WorldUid,
                EventId = state.EventId,
                StageOne = state.StageOne,
                Remaining = Mathf.Max(0f, state.Remaining),
                SavedUtcTicks = DateTime.UtcNow.Ticks
            });
        }

        private static void EndRecovery(Player player)
        {
            if (player == null)
                return;
            recovery.Remove(player.GetPlayerID());
            player.m_customData.Remove(RecoveryDataKey);
            BloodMoonStatus.RemoveRecoveryLocal();
            LogInfo($"[BloodMoon.Recovery] Protection ended for {player.GetPlayerName()}; terminal Defeated state remains until the event ends.");
        }

        private static long GetCurrentWorldUid()
        {
            return ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
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
                .Where(IsKnownVanillaDamagingDot)
                .ToList();
            foreach (StatusEffect effect in remove)
                seman.RemoveStatusEffect(effect.m_nameHash);
        }

        private static bool IsKnownVanillaDamagingDot(StatusEffect effect)
        {
            if (effect == null)
                return false;

            Type type = effect.GetType();
            if (type == typeof(SE_Burning) || type == typeof(SE_Poison) || type == typeof(SE_Smoke))
                return true;
            return type == typeof(SE_Stats) && effect is SE_Stats stats && stats.m_tickInterval > 0f && stats.m_healthPerTick < 0f;
        }
    }
}
