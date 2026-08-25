using HarmonyLib;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal sealed class BloodMoonBossRecoveryTicker : MonoBehaviour
    {
        private float nextAttempt;

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextAttempt)
                return;
            nextAttempt = Time.realtimeSinceStartup + 0.5f;

            if (ZNet.instance == null)
                return;
            if (!ZNet.instance.IsServer())
            {
                Destroy(this);
                return;
            }

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || ZDOMan.instance == null || ZNetScene.instance == null)
                return;

            BloodMoonBosses.Recover(state);
            Destroy(this);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class BloodMoonBossRecoveryBootstrapPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (__instance != null && __instance.GetComponent<BloodMoonBossRecoveryTicker>() == null)
                __instance.gameObject.AddComponent<BloodMoonBossRecoveryTicker>();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class BloodMoonBossDiscoveryPatch
    {
        private static void Postfix(Character __instance)
        {
            BloodMoonBosses.ReportLoadedBoss(__instance);
        }
    }
}
