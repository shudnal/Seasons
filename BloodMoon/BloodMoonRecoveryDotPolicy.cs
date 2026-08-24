using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonRecovery), "RemoveDamagingDots")]
    internal static class BloodMoonRecoveryDotPolicyPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player player)
        {
            SEMan seman = player?.GetSEMan();
            if (seman == null)
                return false;

            List<StatusEffect> remove = seman.GetStatusEffects()
                .Where(IsKnownVanillaDamagingDot)
                .ToList();
            foreach (StatusEffect effect in remove)
                seman.RemoveStatusEffect(effect.m_nameHash);
            return false;
        }

        private static bool IsKnownVanillaDamagingDot(StatusEffect effect)
        {
            if (effect == null)
                return false;

            System.Type type = effect.GetType();
            if (type == typeof(SE_Burning) || type == typeof(SE_Poison) || type == typeof(SE_Smoke))
                return true;

            return type == typeof(SE_Stats) && effect is SE_Stats stats && stats.m_tickInterval > 0f && stats.m_healthPerTick < 0f;
        }
    }
}
