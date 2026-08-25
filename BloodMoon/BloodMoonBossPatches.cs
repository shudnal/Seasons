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

    internal sealed class BloodMoonBossParkingGuard : MonoBehaviour
    {
        private float nextTick;

        private void Update()
        {
            if (Time.realtimeSinceStartup < nextTick)
                return;
            nextTick = Time.realtimeSinceStartup + 0.25f;

            if (ZNet.instance == null)
                return;
            if (!ZNet.instance.IsServer())
            {
                Destroy(this);
                return;
            }

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null || ZDOMan.instance == null)
                return;

            BloodMoonBosses.TickPending(state);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class BloodMoonBossRecoveryBootstrapPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (__instance == null)
                return;
            if (__instance.GetComponent<BloodMoonBossRecoveryTicker>() == null)
                __instance.gameObject.AddComponent<BloodMoonBossRecoveryTicker>();
            if (__instance.GetComponent<BloodMoonBossParkingGuard>() == null)
                __instance.gameObject.AddComponent<BloodMoonBossParkingGuard>();
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
