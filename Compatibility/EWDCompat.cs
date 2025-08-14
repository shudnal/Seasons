using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System.Reflection;

namespace Seasons.Compatibility
{
    public static class EWDCompat
    {
        public const string GUID = "expand_world_data";
        public static PluginInfo plugin;
        public static Assembly assembly;

        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
        }

        [HarmonyPatch]
        public static class EWD_EnvironmentManager_Set_RefreshBiomeSettings
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                    return false;

                assembly ??= Assembly.GetAssembly(ewd.Instance.GetType());

                target ??= AccessTools.Method(assembly.GetType("ExpandWorldData.EnvironmentManager"), "Set");
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.EnvironmentManager:Set method is patched to add postfix to refresh biome settings on change");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Finalizer() => SeasonState.RefreshBiomesDefault(forceUpdate: true);
        }
    }
}

