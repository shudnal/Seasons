using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonResolutionFreezeGuards
    {
        private static bool IsFrozenForNewCombatWork()
        {
            return BloodMoonNetwork.ClientGlobal.Phase == BloodMoonEventPhase.Resolving;
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

        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static class CharacterDamagePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Character __instance)
            {
                if (!IsFrozenForNewCombatWork() || __instance == null)
                    return true;

                // Resolution freezes Blood Moon combat results while the fade/cleanup transaction
                // completes, but it must not suppress unrelated vanilla skill progression. Skill
                // accounting already stops naturally because the event is no longer in Active or
                // AutoCompleting on the authoritative server.
                if (__instance is Player player && BloodMoonInteractionRules.IsActiveParticipant(player))
                    return false;
                if (BloodMoonInteractionRules.IsBloodEnemy(__instance))
                    return false;
                return true;
            }
        }
    }
}