using HarmonyLib;

namespace Seasons.BloodMoon
{
    internal static class BloodCraftWorldRecoveryPreservation
    {
        private sealed class Scope
        {
            internal bool Active;
        }

        private static int depth;

        internal static void BeginManual()
        {
            depth++;
        }

        internal static void EndManual()
        {
            depth = System.Math.Max(0, depth - 1);
        }

        internal static bool ShouldPreserve(Player player)
        {
            return player != null && player == Player.m_localPlayer && (depth > 0 || ZNet.instance == null);
        }

        [HarmonyPatch(typeof(BloodMoonController), "EnsureWorldLoaded")]
        private static class EnsureWorldLoadedPatch
        {
            private static void Prefix(out Scope __state)
            {
                __state = new Scope { Active = true };
                depth++;
            }

            private static void Postfix(Scope __state)
            {
                End(__state);
            }

            private static System.Exception Finalizer(System.Exception __exception, Scope __state)
            {
                End(__state);
                return __exception;
            }

            private static void End(Scope state)
            {
                if (state == null || !state.Active)
                    return;
                state.Active = false;
                depth = System.Math.Max(0, depth - 1);
            }
        }

        [HarmonyPatch(typeof(BloodCraft), nameof(BloodCraft.CleanupLocal))]
        private static class CleanupLocalPatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Player player)
            {
                return !ShouldPreserve(player);
            }
        }
    }
}
