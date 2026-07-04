using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Seasons.Compatibility
{
    public static class EWDCompat
    {
        public const string GUID = "expand_world_data";
        public static PluginInfo plugin;
        public static Assembly assembly;

        public static bool isEnabled;

        private static Type environmentManagerType;
        private static Type biomeManagerType;
        private static FieldInfo environmentOriginalsField;
        private static bool delayedRefreshPending;
        private static float lastDelayedRefreshRequestTime;
        private static bool ewdBiomeSetupAppliedLast;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
        }

        public static void OnSeasonsBiomeSetupApplied()
        {
            if (!isEnabled)
                return;

            ewdBiomeSetupAppliedLast = false;
        }

        public static bool ShouldApplySeasonalRulesToAvailableEnvironments()
        {
            return isEnabled && ewdBiomeSetupAppliedLast;
        }

        public static void RegisterSeasonEnvironmentsInEwdOriginals()
        {
            if (!isEnabled || EnvMan.instance == null)
                return;

            Dictionary<string, EnvSetup> originals = GetEnvironmentOriginals();
            if (originals == null)
                return;

            int added = 0;

            foreach (SeasonEnvironment seasonEnvironment in SeasonState.seasonEnvironments)
            {
                if (seasonEnvironment == null || string.IsNullOrWhiteSpace(seasonEnvironment.m_name))
                    continue;

                EnvSetup env = EnvMan.instance.GetEnv(seasonEnvironment.m_name);
                if (env == null || string.IsNullOrWhiteSpace(env.m_name) || originals.ContainsKey(env.m_name))
                    continue;

                originals.Add(env.m_name, env);
                added++;
            }

            if (added > 0)
                Seasons.LogInfo($"Added {added} Seasons custom environments to Expand World Data originals.");
        }

        private static Dictionary<string, EnvSetup> GetEnvironmentOriginals()
        {
            environmentManagerType ??= assembly?.GetType("ExpandWorldData.EnvironmentManager");
            if (environmentManagerType == null)
                return null;

            environmentOriginalsField ??= AccessTools.Field(environmentManagerType, "Originals");
            return environmentOriginalsField?.GetValue(null) as Dictionary<string, EnvSetup>;
        }

        private static void CaptureEwdBiomeDefaults()
        {
            if (!SeasonState.IsActive)
                return;

            SeasonState.RefreshBiomesDefault(forceUpdate: true);
            ewdBiomeSetupAppliedLast = true;
        }

        private static void RequestDelayedRefresh(string reason)
        {
            if (!isEnabled || Seasons.instance == null || !SeasonState.IsActive)
                return;

            lastDelayedRefreshRequestTime = Time.realtimeSinceStartup;

            if (!delayedRefreshPending)
            {
                delayedRefreshPending = true;
                Seasons.instance.StartCoroutine(DelayedRefresh());
            }

            Seasons.LogInfo($"Expand World Data compatibility refresh requested: {reason}");
        }

        private static IEnumerator DelayedRefresh()
        {
            while (Time.realtimeSinceStartup - lastDelayedRefreshRequestTime < 1f)
                yield return null;

            delayedRefreshPending = false;

            ApplyDelayedRefresh();
        }

        private static void ApplyDelayedRefresh()
        {
            if (!SeasonState.IsActive)
                return;

            Seasons.LogInfo($"Applying Expand World Data environment compatibility refresh.");

            SeasonState.UpdateSeasonEnvironments();
            SeasonState.UpdateBiomeEnvironments();
            RegisterSeasonEnvironmentsInEwdOriginals();
            EnvManPatches.settingsUpdated = true;
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
                environmentManagerType ??= assembly.GetType("ExpandWorldData.EnvironmentManager");
                if (environmentManagerType == null)
                    return false;

                target ??= AccessTools.Method(environmentManagerType, "Set");
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.EnvironmentManager:Set method is patched to refresh Seasons environments after EWD environment changes");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Finalizer()
            {
                RequestDelayedRefresh("EWD environment data changed");
            }
        }

        [HarmonyPatch]
        public static class EWD_EnvironmentManager_SetOriginals_RegisterSeasonEnvironments
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                    return false;

                assembly ??= Assembly.GetAssembly(ewd.Instance.GetType());
                environmentManagerType ??= assembly.GetType("ExpandWorldData.EnvironmentManager");
                if (environmentManagerType == null)
                    return false;

                target ??= AccessTools.Method(environmentManagerType, "SetOriginals");
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.EnvironmentManager:SetOriginals method is patched to include Seasons custom environments");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Finalizer()
            {
                RegisterSeasonEnvironmentsInEwdOriginals();
            }
        }

        [HarmonyPatch]
        public static class EWD_BiomeManager_LoadEnvironments_RefreshBiomeSettings
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                    return false;

                assembly ??= Assembly.GetAssembly(ewd.Instance.GetType());
                biomeManagerType ??= assembly.GetType("ExpandWorldData.BiomeManager");
                if (biomeManagerType == null)
                    return false;

                target ??= AccessTools.Method(biomeManagerType, "LoadEnvironments");
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.BiomeManager:LoadEnvironments method is patched to refresh Seasons biome settings after EWD biome changes");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Finalizer()
            {
                CaptureEwdBiomeDefaults();
                RequestDelayedRefresh("EWD biome environments loaded");
            }
        }

        [HarmonyPatch]
        public static class EWD_BiomeManager_SetupBiomeEnvs_RefreshBiomeSettings
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                    return false;

                assembly ??= Assembly.GetAssembly(ewd.Instance.GetType());
                biomeManagerType ??= assembly.GetType("ExpandWorldData.BiomeManager");
                if (biomeManagerType == null)
                    return false;

                target ??= AccessTools.Method(biomeManagerType, "SetupBiomeEnvs");
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.BiomeManager:SetupBiomeEnvs method is patched to refresh Seasons biome settings after EWD biome changes");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Finalizer()
            {
                CaptureEwdBiomeDefaults();
                RequestDelayedRefresh("EWD biome setup changed");
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetAvailableEnvironments))]
        public static class EnvMan_GetAvailableEnvironments_ApplySeasonalRulesAfterEWD
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(ref List<EnvEntry> __result, object[] __args)
            {
                if (__result == null || !ShouldApplySeasonalRulesToAvailableEnvironments())
                    return;

                Heightmap.Biome biome = Heightmap.Biome.None;
                if (__args != null && __args.Length > 0 && __args[0] is Heightmap.Biome argBiome)
                    biome = argBiome;

                __result = SeasonState.ApplySeasonBiomeEnvironmentRules(biome, __result);
            }
        }
    }
}
