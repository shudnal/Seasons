using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonWorldLifecycle
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodCraftWorldRecoveryPreservation.BeginManual();
            try
            {
                BloodCraft.CleanupLocal(Player.m_localPlayer);
            }
            finally
            {
                BloodCraftWorldRecoveryPreservation.EndManual();
            }

            BloodMoonRecovery.ResetRuntime();
            BloodMoonSummons.ResetRuntimeState();
            BloodMoonHitAttribution.Reset();
            BloodMoonSpawner.ResetClientState();
            BloodMoonBosses.ResetRuntimeState();
            BloodMoonParticipantDetails.Reset();
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            BloodMoonPresentation.CleanupTransientState();
            BloodMoonSkills.ResetLocal(-1L);
        }
    }
}
