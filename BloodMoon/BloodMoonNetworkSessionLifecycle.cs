using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonNetworkSessionLifecycle
    {
        internal static void ResetClientSnapshots()
        {
            BloodMoonNetwork.ResetSession();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonNetworkSessionResetPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonNetworkSessionLifecycle.ResetClientSnapshots();
        }
    }
}
