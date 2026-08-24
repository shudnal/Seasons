using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonController), "AdvanceToFrozenMorning")]
    internal static class BloodMoonFrozenMorningNetworkPatch
    {
        private static void Postfix()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
                ZNet.instance.SendNetTime();
        }
    }
}
