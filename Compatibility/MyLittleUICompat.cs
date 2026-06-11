using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using System;

namespace Seasons.Compatibility
{
    public static class MyLittleUICompat
    {
        public const string GUID = "shudnal.MyLittleUI";

        private const string MapStatusEffectElementGroup = "Status effects - Map - List element";
        private const string CustomElementEnabledName = "Custom element enabled";

        public static PluginInfo plugin;
        public static bool isEnabled;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin);
        }

        public static bool IsMapStatusEffectListElementEnabled()
        {
            if (!isEnabled)
                CheckForCompatibility();

            if (!isEnabled || plugin?.Instance?.Config == null)
                return false;

            try
            {
                return plugin.Instance.Config.TryGetEntry(MapStatusEffectElementGroup, CustomElementEnabledName, out ConfigEntry<bool> customElementEnabled) && customElementEnabled.Value;
            }
            catch (Exception e)
            {
                Seasons.LogWarning($"Failed to read My Little UI config '{MapStatusEffectElementGroup}/{CustomElementEnabledName}'\n{e}");
                return false;
            }
        }
    }
}
