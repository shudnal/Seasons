namespace Seasons.BloodMoon
{
    // Blood Moon does not own or suppress gameplay input. Resolution fade and sleep DreamText
    // presentation are visual-only and must not interfere with vanilla or modded ZInput hotkeys.
    internal static class BloodMoonFadeInputGuard
    {
        internal static bool Blocking => false;

        internal static void SetResolution(bool blocking)
        {
        }

        internal static void AcquireDream()
        {
        }

        internal static void ReleaseDream()
        {
        }

        internal static void Reset()
        {
        }
    }
}
