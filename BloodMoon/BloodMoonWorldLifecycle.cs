using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonWorldLifecycle
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodCraft.CleanupLocal(Player.m_localPlayer);
            BloodMoonRecovery.ResetRuntime();
            BloodMoonHitAttribution.Reset();
            BloodMoonSpawner.ResetClientState();
            BloodMoonBosses.ResetClientState();
            BloodMoonParticipantDetails.Reset();
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            BloodMoonPresentation.CleanupTransientState();
            BloodMoonSkills.ResetLocal(-1L);
        }
    }
}
