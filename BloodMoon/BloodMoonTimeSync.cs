using HarmonyLib;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonController), "AdvanceToFrozenMorning")]
    internal static class BloodMoonFrozenMorningNetworkPatch
    {
        private static void Postfix()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            ZNet.instance.SendNetTime();
            if (EnvMan.instance != null)
            {
                EnvMan.instance.m_skipTime = false;
                EnvMan.instance.m_totalSeconds = ZNet.instance.GetTimeSeconds();
            }
        }
    }
}
