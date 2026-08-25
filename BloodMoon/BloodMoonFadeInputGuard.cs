using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonFadeInputGuard
    {
        private static bool resolutionBlocking;
        private static int dreamBlocks;

        internal static bool Blocking => resolutionBlocking || dreamBlocks > 0;

        internal static void SetResolution(bool blocking)
        {
            resolutionBlocking = blocking;
        }

        internal static void AcquireDream()
        {
            dreamBlocks++;
        }

        internal static void ReleaseDream()
        {
            if (dreamBlocks > 0)
                dreamBlocks--;
        }

        internal static void Reset()
        {
            resolutionBlocking = false;
            dreamBlocks = 0;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class BloodMoonPlayerInputFadePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (BloodMoonFadeInputGuard.Blocking)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonFadeInputWorldCleanupPatch
    {
        private static void Prefix()
        {
            BloodMoonFadeInputGuard.Reset();
        }
    }
}
