using HarmonyLib;
using System.Reflection;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonNetworkSessionLifecycle
    {
        private static readonly FieldInfo ClientGlobalField = AccessTools.Field(typeof(BloodMoonNetwork), "<ClientGlobal>k__BackingField");
        private static readonly FieldInfo ClientParticipantsField = AccessTools.Field(typeof(BloodMoonNetwork), "<ClientParticipants>k__BackingField");

        internal static void ResetClientSnapshots()
        {
            ClientGlobalField?.SetValue(null, new BloodMoonGlobalSnapshot());
            ClientParticipantsField?.SetValue(null, new BloodMoonParticipantSnapshot());
            BloodMoonParticipantDetails.Reset();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
    internal static class BloodMoonNetworkSessionResetPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            BloodMoonNetworkSessionLifecycle.ResetClientSnapshots();
        }
    }
}
