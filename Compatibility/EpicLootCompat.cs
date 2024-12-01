using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Collections;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.Compatibility
{
    public static class EpicLootCompat
    {
        public const string GUID = "randyknapp.mods.epicloot";
        public static PluginInfo epicLootPlugin;
        public static Assembly assembly;

        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out epicLootPlugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(epicLootPlugin.Instance.GetType());
        }

        [HarmonyPatch]
        public static class EpicLoot_Adventure_GetAvailableBounties_PreventSerpentsBountyInWinter
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!isEnabled)
                    return false;

                target ??= AccessTools.Method(assembly.GetType("EpicLoot.Adventure.Feature.BountiesAdventureFeature"), "AcceptBounty");
                if (target == null)
                    return false;

                if (original == null)
                    LogInfo("EpicLoot.Adventure.Feature.BountiesAdventureFeature:AcceptBounty method is patched to prevent accepting serpents bounty in Winter");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static bool Prefix(object __1, ref IEnumerator __result)
            {
                bool prevent = IsSerpent(__1) && ZoneSystemVariantController.IsWaterSurfaceFrozen();
                if (prevent)
                    __result = Empty();
                return !prevent;
            }

            private static IEnumerator Empty()
            {
                yield return new WaitForEndOfFrame();
            }

            public static bool IsSerpent(object bountyInfo)
            {
                FieldInfo target = AccessTools.Field(bountyInfo.GetType(), "Target");
                if (target == null)
                    return false;

                FieldInfo monsterID = AccessTools.Field(target.GetValue(bountyInfo).GetType(), "MonsterID");
                if (monsterID == null)
                    return false;

                return monsterID.GetValue(target.GetValue(bountyInfo)).Equals("Serpent");
            }
        }
    }
}

