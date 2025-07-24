using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;
using static Seasons.Seasons;

namespace Seasons.Compatibility
{
    public static class MarketplaceCompat
    {
        public const string GUID = "MarketplaceAndServerNPCs";
        public static PluginInfo plugin;
        public static Assembly assembly;

        public static MethodBase methodDoMapMagic;
        public static FieldInfo fieldOriginalMapColors;

        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);

            if (isEnabled)
                assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());

            if (assembly != null)
            {
                methodDoMapMagic ??= AccessTools.Method(assembly.GetType("Marketplace.Modules.TerritorySystem.TerritorySystem_Main_Client"), "DoMapMagic");
                if (methodDoMapMagic == null)
                    LogInfo("Marketplace.Modules.TerritorySystem.TerritorySystem_Main_Client:DoMapMagic method does not found, there could be incompatibilities in map colors management");
                else
                    fieldOriginalMapColors ??= AccessTools.Field(assembly.GetType("Marketplace.Modules.TerritorySystem.TerritorySystem_Main_Client"), "originalMapColors");
            }
        }

        public static void UpdateMap()
        {
            if (!isEnabled)
                return;

            if (methodDoMapMagic == null || fieldOriginalMapColors == null)
                return;

            try
            {
                fieldOriginalMapColors.SetValue(fieldOriginalMapColors, Minimap.instance.m_mapTexture.GetPixels());
            }
            catch (Exception e)
            {
                LogInfo("Error while setting TerritorySystem_Main_Client.originalMapColors\n" + e.ToString());
                fieldOriginalMapColors = null;
                return;
            }

            try
            {
                methodDoMapMagic.Invoke(methodDoMapMagic, null);
            }
            catch (Exception e)
            {
                LogInfo("Error while invoking TerritorySystem_Main_Client.DoMapMagic\n" + e.ToString());
                methodDoMapMagic = null;
                return;
            }
        }
    }
}

