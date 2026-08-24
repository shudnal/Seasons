using HarmonyLib;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(Game), nameof(Game.Awake))]
    internal static class BloodMoonBootstrap
    {
        private static bool initialized;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            Initialize();
            return true;
        }

        private static void Postfix()
        {
            Initialize();
            BloodMoonNetwork.RequestResync();
        }

        internal static void Initialize()
        {
            if (initialized || Seasons.instance == null)
                return;

            BloodMoonConfig.Initialize();
            BloodMoonNetwork.InitializeValues();
            BloodMoonController.Attach(Seasons.instance.gameObject);
            BloodMoonDiagnostics.Initialize();
            initialized = true;
            LogInfo("[BloodMoon.Bootstrap] Blood Moon subsystem initialized.");
        }
    }
}
