using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    internal static class BloodMoonBedInteractPatch
    {
        private static bool Prefix(Humanoid human, ref bool __result)
        {
            if (human is not Player player || BloodMoonInteractionRules.CanSleep(player))
                return true;
            human.Message(MessageHud.MessageType.Center, "You cannot sleep while marked by the Blood Moon.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
    internal static class BloodMoonOfferingInteractPatch
    {
        private static bool Prefix(OfferingBowl __instance, Humanoid user, ref bool __result)
        {
            if (BloodMoonInteractionRules.CanUseBossOffering(__instance))
                return true;
            user?.Message(MessageHud.MessageType.Center, "Boss offerings are sealed during the Blood Moon.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
    internal static class BloodMoonOfferingUseItemPatch
    {
        private static bool Prefix(OfferingBowl __instance, Humanoid user, ref bool __result)
        {
            if (BloodMoonInteractionRules.CanUseBossOffering(__instance))
                return true;
            user?.Message(MessageHud.MessageType.Center, "Boss offerings are sealed during the Blood Moon.");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.RPC_SpawnBoss))]
    internal static class BloodMoonOfferingSpawnBossPatch
    {
        private static bool Prefix(OfferingBowl __instance)
        {
            return BloodMoonInteractionRules.CanUseBossOffering(__instance);
        }
    }
}
