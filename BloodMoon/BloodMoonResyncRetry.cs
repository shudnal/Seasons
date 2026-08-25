using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonResyncRetry
    {
        private const float RetryIntervalSeconds = 2f;
        private static long localPlayerId;
        private static long lastObservedEventId = long.MinValue;
        private static float retryTimer;

        internal static void OnLocalPlayerReady(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
                return;

            localPlayerId = player.GetPlayerID();
            lastObservedEventId = long.MinValue;
            retryTimer = 0f;
            BloodMoonNetwork.RequestResync();
        }

        internal static void Tick(Player player, float dt)
        {
            if (player == null || player != Player.m_localPlayer || ZRoutedRpc.instance == null)
                return;

            long playerId = player.GetPlayerID();
            if (playerId == 0L)
                return;
            if (localPlayerId != playerId)
            {
                localPlayerId = playerId;
                lastObservedEventId = long.MinValue;
                retryTimer = 0f;
            }

            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (lastObservedEventId != eventId)
            {
                lastObservedEventId = eventId;
                retryTimer = 0f;
            }

            if (!NeedsResync(eventId, playerId))
                return;

            retryTimer -= Mathf.Max(0f, dt);
            if (retryTimer > 0f)
                return;

            retryTimer = RetryIntervalSeconds;
            BloodMoonNetwork.RequestResync();
        }

        internal static void Reset()
        {
            localPlayerId = 0L;
            lastObservedEventId = long.MinValue;
            retryTimer = 0f;
        }

        private static bool NeedsResync(long eventId, long playerId)
        {
            if (eventId < 0L)
                return false;

            if (BloodMoonNetwork.ClientParticipants.EventId != eventId || BloodMoonNetwork.ClientParticipants.Participants == null)
                return true;

            bool enrolled = BloodMoonNetwork.ClientParticipants.Participants.Any(participant => participant.PlayerId == playerId);
            if (!enrolled)
                return false;

            BloodMoonParticipantDetailSnapshot detail = BloodMoonParticipantDetails.ClientOwn;
            return detail == null || detail.EventId != eventId || detail.PlayerId != playerId;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
    internal static class BloodMoonLocalPlayerResyncPatch
    {
        private static void Postfix(Player __instance)
        {
            BloodMoonResyncRetry.OnLocalPlayerReady(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.CustomFixedUpdate))]
    internal static class BloodMoonLocalHumanoidResyncRetryPatch
    {
        private static void Postfix(Humanoid __instance, float fixedDeltaTime)
        {
            if (__instance != Player.m_localPlayer)
                return;

            BloodMoonResyncRetry.Tick((Player)__instance, fixedDeltaTime);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonResyncRetryWorldCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonResyncRetry.Reset();
        }
    }
}
