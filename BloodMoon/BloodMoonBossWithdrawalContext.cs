using HarmonyLib;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonBosses), "WithdrawAffectedPlayers")]
    internal static class BloodMoonBossWithdrawalContextPatch
    {
        private const float EncounterWithdrawDistance = 120f;

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BloodMoonEventState state, Vector3 bossPosition, string reason)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            if (state == null || controller == null)
                return false;

            bool bossInterior = Character.InInterior(bossPosition);
            Location bossLocation = bossInterior ? Location.GetLocation(bossPosition) : null;
            Vector2i bossZone = ZoneSystem.GetZone(bossPosition);

            foreach (BloodMoonParticipantState participant in state.Participants.Values.Where(item => item.IsCombatActive).ToArray())
            {
                if (!controller.TryGetConnectedPosition(participant.PlayerId, out Vector3 position))
                    continue;

                bool participantInterior = Character.InInterior(position);
                if (participantInterior != bossInterior)
                    continue;

                if (bossInterior)
                {
                    Location participantLocation = Location.GetLocation(position);
                    if (bossLocation != null || participantLocation != null)
                    {
                        if (participantLocation != bossLocation)
                            continue;
                    }
                    else if (ZoneSystem.GetZone(position) != bossZone)
                    {
                        // Some modded interiors have no Location component. In that case the same
                        // interior/surface flag is not enough: Valheim keeps unrelated interiors in
                        // different map zones, so require the same zone as the navigation-context fallback.
                        continue;
                    }
                }

                if (Utils.DistanceXZ(position, bossPosition) > EncounterWithdrawDistance)
                    continue;

                controller.WithdrawLocalOrRequested(participant.PlayerId);
                LogWarning($"[BloodMoon][event:{state.EventId}][boss] withdrew player {participant.PlayerId}: {reason}; interior={bossInterior}.");
            }

            return false;
        }
    }
}
