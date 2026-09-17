using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonWorldEdge
    {
        private static bool unavailableLogged;

        internal static bool TryIsBeyondWorldEdge(Vector3 position, out bool beyond)
        {
            beyond = false;
            float edge = ZoneSystemVariantController.s_waterEdge;
            float offset = Mathf.Max(0f, BloodMoonConfig.WorldEdgeWithdrawalSafetyOffset.Value);
            if (!IsFinitePositive(edge) || !IsFinite(offset) || edge <= offset)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    LogInfo($"[BloodMoon.WorldEdge] Dynamic world boundary is not initialized yet (edge={edge:0.##}, offset={offset:0.##}); withdrawal checks are deferred.");
                }
                return false;
            }

            unavailableLogged = false;
            beyond = ZoneSystemVariantController.IsBeyondWorldEdge(position, offset);
            return true;
        }

        internal static void ResetRuntime()
        {
            unavailableLogged = false;
        }

        private static bool IsFinitePositive(float value) => IsFinite(value) && value > 0f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
