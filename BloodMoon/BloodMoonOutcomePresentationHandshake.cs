namespace Seasons.BloodMoon
{
    // DreamText is intentionally deferred to the next ordinary sleep. Resolution must not wait for
    // presentation start/completion and no presentation-specific RPC is required.
    internal static class BloodMoonOutcomePresentationHandshake
    {
        internal static void RegisterRpc()
        {
        }

        internal static void Begin(long currentEventId)
        {
        }

        internal static void NotifyStarted(long worldUid, long currentEventId, long playerId)
        {
        }

        internal static void NotifyCompleted(long worldUid, long currentEventId, long playerId)
        {
        }

        internal static bool CanRelease(BloodMoonEventState state, out string timeoutDetail)
        {
            timeoutDetail = string.Empty;
            return true;
        }

        internal static void ResetRuntime()
        {
        }
    }
}
