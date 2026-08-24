using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonFadeInputGuard
    {
        internal static bool Blocking { get; private set; }

        internal static void Set(bool blocking)
        {
            Blocking = blocking;
        }

        internal static void Reset()
        {
            Blocking = false;
        }
    }

    [HarmonyPatch(typeof(BloodMoonPresentation), nameof(BloodMoonPresentation.SetResolutionFade))]
    internal static class BloodMoonResolutionFadeStatePatch
    {
        private static void Prefix(bool begin)
        {
            BloodMoonFadeInputGuard.Set(begin);
        }
    }

    [HarmonyPatch(typeof(BloodMoonPresentation), nameof(BloodMoonPresentation.OnResolutionComplete))]
    internal static class BloodMoonResolutionInputReleasePatch
    {
        private static void Postfix()
        {
            BloodMoonFadeInputGuard.Reset();
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
