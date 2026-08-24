using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonResolutionFreezeGuards
    {
        private static bool IsFrozenForNewCombatWork()
        {
            return BloodMoonNetwork.ClientGlobal.Phase == BloodMoonEventPhase.Resolving;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
        private static class PlayerRaiseSkillPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player __instance)
            {
                if (!IsFrozenForNewCombatWork() || __instance == null || __instance != Player.m_localPlayer)
                    return true;
                BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
                return participant == null || !participant.IsCombatActive;
            }
        }

        [HarmonyPatch(typeof(BloodMoonSpawner), "TrySpawnFromLease")]
        private static class SpawnLeasePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !IsFrozenForNewCombatWork();
            }
        }
    }
}
