using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Reflection;
using static Seasons.Seasons;

namespace Seasons.Compatibility
{
    public static class HoneyPlusCompat
    {
        public const string GUID = "OhhLoz-HoneyPlus";
        public static PluginInfo plugin;
        public static Assembly assembly;

        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
        public static class CraftingStation_CheckUsable_PreventPickingHoneyFromApiaryInWinter
        {
            private static void Postfix(CraftingStation __instance, Player player, bool showMessage, ref bool __result)
            {
                if (!isEnabled || !__result || IsProtectedPosition(__instance.transform.position))
                    return;

                if (__instance.m_name == "$custom_piece_apiary" && seasonState.GetBeehiveProductionMultiplier() == 0f && !player.NoCostCheat())
                {
                    if (showMessage)
                        player.Message(MessageHud.MessageType.Center, "$piece_beehive_sleep");

                    __result = false;
                }
            }
        }
    }
}

