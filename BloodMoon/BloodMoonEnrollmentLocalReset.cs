using HarmonyLib;

namespace Seasons.BloodMoon
{
    // A production server sends "enroll" only when creating the participant record, so clearing
    // the local terminal marker here cannot re-enter a terminal participant. It also makes repeated
    // diagnostic cleanup/start cycles on the same world day deterministic when they reuse eventId.
    [HarmonyPatch(typeof(BloodMoonController), nameof(BloodMoonController.HandleClientAction))]
    internal static class BloodMoonEnrollmentLocalResetPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(long eventId, string action)
        {
            if (action == "enroll" && eventId >= 0L)
                BloodMoonRecovery.ResetEvent(eventId);
        }
    }
}
