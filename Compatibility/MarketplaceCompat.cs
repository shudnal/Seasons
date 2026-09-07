using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.Compatibility
{
    public static class MarketplaceCompat
    {
        public const string GUID = "MarketplaceAndServerNPCs";
        private const string TerritoryClientType = "Marketplace.Modules.TerritorySystem.TerritorySystem_Main_Client";

        public static PluginInfo plugin;
        public static Assembly assembly;
        public static MethodBase methodDoMapMagic;
        public static FieldInfo fieldOriginalMapColors;
        public static bool isEnabled;

        private static FieldInfo fieldOriginalHeightColors;
        private static bool reportedUpdateFailure;

        public static void CheckForCompatibility()
        {
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin) && plugin.Instance != null;
            assembly = isEnabled ? plugin.Instance.GetType().Assembly : null;
            methodDoMapMagic = null;
            fieldOriginalMapColors = null;
            fieldOriginalHeightColors = null;
            reportedUpdateFailure = false;
            if (assembly == null)
                return;

            Type clientType = assembly.GetType(TerritoryClientType);
            if (clientType != null)
            {
                MethodInfo method = AccessTools.Method(clientType, "DoMapMagic", Type.EmptyTypes);
                FieldInfo colors = AccessTools.Field(clientType, "originalMapColors");
                if (method != null && method.IsStatic && colors != null && colors.IsStatic && !colors.IsInitOnly &&
                    (colors.FieldType == typeof(Color[]) || colors.FieldType == typeof(Color32[])))
                {
                    methodDoMapMagic = method;
                    fieldOriginalMapColors = colors;
                    FieldInfo heightColors = AccessTools.Field(clientType, "originalHeightColors");
                    if (heightColors != null && heightColors.IsStatic)
                        fieldOriginalHeightColors = heightColors;
                }
            }

            if (methodDoMapMagic == null)
                LogWarning($"[Marketplace] Unsupported territory map API in {plugin.Metadata.Version}; seasonal terrain colors remain enabled without territory redraw.");
        }

        public static void UpdateMap()
        {
            UpdateMap(Minimap.instance);
        }

        public static void UpdateMap(Minimap minimap)
        {
            if (!isEnabled || methodDoMapMagic == null || fieldOriginalMapColors == null ||
                minimap == null || minimap != Minimap.instance || !minimap.m_hasGenerated ||
                minimap.m_mapTexture == null || !minimap.m_mapTexture.isReadable ||
                minimap.m_heightTexture == null || minimap.m_fogTexture == null || minimap.m_explored == null)
                return;

            try
            {
                // Recent Marketplace versions also use their original height-map snapshot.
                // Do not invoke their asynchronous redraw before their own map initialization,
                // and do not overwrite that baseline with an already modified height texture.
                if (fieldOriginalHeightColors != null && fieldOriginalHeightColors.GetValue(null) == null)
                    return;

                object colors = fieldOriginalMapColors.FieldType == typeof(Color32[])
                    ? (object)minimap.m_mapTexture.GetPixels32()
                    : minimap.m_mapTexture.GetPixels();
                fieldOriginalMapColors.SetValue(null, colors);
                methodDoMapMagic.Invoke(null, null);
                reportedUpdateFailure = false;
            }
            catch (Exception exception)
            {
                // A transient world/UI lifecycle failure must not disable compatibility for
                // the remainder of the process. The next seasonal redraw may safely retry.
                if (reportedUpdateFailure)
                    return;
                reportedUpdateFailure = true;
                Exception cause = exception is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;
                LogWarning($"[Marketplace] Could not refresh territory map colors; the next map update will retry.\n{cause}");
            }
        }
    }
}
