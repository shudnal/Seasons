using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

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
        private static FieldInfo environmentManagerInitializedField;
        private static FieldInfo biomeToDisplayNameField;
        private static bool seasonsWorldInitialized;
        private static bool ewdBiomeSetupAppliedLast;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
        }

        public static void ResetWorldState()
        {
            seasonsWorldInitialized = false;
            ewdBiomeSetupAppliedLast = false;
        }

        public static void MarkWorldInitialized()
        {
            seasonsWorldInitialized = true;
        }

        public static void OnSeasonsBiomeSetupApplied()
        {
            if (!isEnabled)
                return;

            ewdBiomeSetupAppliedLast = false;
        }

        public static bool ShouldApplySeasonalRulesToAvailableEnvironments()
        {
            return isEnabled && seasonsWorldInitialized && ewdBiomeSetupAppliedLast;
        }

        public static bool TryGetBiomeDisplayName(Heightmap.Biome biome, out string displayName)
        {
            displayName = null;

            if (!TryGetBiomeManagerType())
                return false;

            biomeToDisplayNameField ??= AccessTools.Field(biomeManagerType, "BiomeToDisplayName");
            Dictionary<Heightmap.Biome, string> displayNames = biomeToDisplayNameField?.GetValue(null) as Dictionary<Heightmap.Biome, string>;

            return displayNames != null && displayNames.TryGetValue(biome, out displayName) && !String.IsNullOrWhiteSpace(displayName);
        }

        private static bool TryGetEnvironmentManagerType()
        {
            if (environmentManagerType != null)
                return true;

            if (assembly == null && Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                assembly = Assembly.GetAssembly(ewd.Instance.GetType());

            environmentManagerType = assembly?.GetType("ExpandWorldData.EnvironmentManager");
            return environmentManagerType != null;
        }

        private static bool TryGetBiomeManagerType()
        {
            if (biomeManagerType != null)
                return true;

            if (assembly == null && Chainloader.PluginInfos.TryGetValue(GUID, out PluginInfo ewd))
                assembly = Assembly.GetAssembly(ewd.Instance.GetType());

            biomeManagerType = assembly?.GetType("ExpandWorldData.BiomeManager");
            return biomeManagerType != null;
        }

        private static bool IsEnvironmentManagerInitialized()
        {
            if (!TryGetEnvironmentManagerType())
                return false;

            environmentManagerInitializedField ??= AccessTools.Field(environmentManagerType, "Initialized");
            return environmentManagerInitializedField?.GetValue(null) is bool initialized && initialized;
        }

        private static void ReapplySeasonEnvironmentsAfterEwdUpdate()
        {
            if (!seasonsWorldInitialized || !IsEnvironmentManagerInitialized() || !SeasonState.IsActive)
                return;

            SeasonState.PrepareForExternalEnvironmentUpdate();
            SeasonState.UpdateSeasonEnvironments();
            EnvManPatches.settingsUpdated = true;

            Seasons.LogInfo("Reapplied Seasons environments after Expand World Data environment update.");
        }

        private static void CaptureEwdBiomeSetup()
        {
            ewdBiomeSetupAppliedLast = true;

            if (!seasonsWorldInitialized || EnvMan.instance == null)
                return;

            SeasonState.RefreshBiomesDefault(forceUpdate: true);
            EnvMan.instance.m_environmentPeriod = -1L;

            if (SeasonState.IsActive)
            {
                EnvManPatches.settingsUpdated = true;
                Seasons.LogInfo("Expand World Data biome environment setup registered as authoritative for seasonal weather rules.");
            }
        }

        [HarmonyPatch]
        public static class EWD_EnvironmentManager_Initialize_ResetWorldState
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!TryGetEnvironmentManagerType())
                    return false;

                target ??= AccessTools.Method(environmentManagerType, "Initialize", Type.EmptyTypes);
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.EnvironmentManager:Initialize method is patched to reset Seasons EWD world state");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Prefix()
            {
                ResetWorldState();
            }
        }

        [HarmonyPatch]
        public static class EWD_EnvironmentManager_Set_ReapplySeasonEnvironments
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!TryGetEnvironmentManagerType())
                    return false;

                target ??= AccessTools.Method(environmentManagerType, "Set", new Type[] { typeof(string) });
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.EnvironmentManager:Set method is patched to reapply Seasons environments after EWD environment changes");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix()
            {
                ReapplySeasonEnvironmentsAfterEwdUpdate();
            }
        }

        [HarmonyPatch]
        public static class EWD_BiomeManager_SetupBiomeEnvs_RegisterAuthoritativeSetup
        {
            public static MethodBase target;

            public static bool Prepare(MethodBase original)
            {
                if (!TryGetBiomeManagerType())
                    return false;

                target ??= AccessTools.Method(biomeManagerType, "SetupBiomeEnvs", new Type[] { typeof(List<BiomeEnvSetup>) });
                if (target == null)
                    return false;

                if (original == null)
                    Seasons.LogInfo("ExpandWorldData.BiomeManager:SetupBiomeEnvs method is patched to preserve EWD biome entries while applying seasonal rules");

                return true;
            }

            public static MethodBase TargetMethod() => target;

            public static void Postfix()
            {
                CaptureEwdBiomeSetup();
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetAvailableEnvironments), new Type[] { typeof(BiomeSector) })]
        public static class EnvMan_GetAvailableEnvironments_ApplySeasonalRulesAfterEWD
        {
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter(new string[1] { GUID })]
            public static void Postfix(BiomeSector biome, ref List<EnvEntry> __result)
            {
                if (biome == null || __result == null || !ShouldApplySeasonalRulesToAvailableEnvironments())
                    return;

                __result = SeasonState.ApplySeasonBiomeEnvironmentRules(biome.Biome, __result);
            }
        }
    }
}
