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
                FieldInfo mapColors = AccessTools.Field(clientType, "originalMapColors");
                FieldInfo heightColors = AccessTools.Field(clientType, "originalHeightColors");

                bool mapApiSupported = method != null && method.IsStatic && IsWritableColorArrayField(mapColors);
                bool heightApiSupported = heightColors == null || IsWritableColorArrayField(heightColors);
                if (mapApiSupported && heightApiSupported)
                {
                    methodDoMapMagic = method;
                    fieldOriginalMapColors = mapColors;
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
                minimap.m_heightTexture == null || !minimap.m_heightTexture.isReadable ||
                minimap.m_fogTexture == null || minimap.m_explored == null)
                return;

            try
            {
                if (!TryReadPixels(minimap.m_mapTexture, fieldOriginalMapColors.FieldType, out object mapColors))
                    return;

                object heightColors = null;
                if (fieldOriginalHeightColors != null &&
                    !TryReadPixels(minimap.m_heightTexture, fieldOriginalHeightColors.FieldType, out heightColors))
                    return;

                // Marketplace 9.9.4 snapshots both arrays in its Minimap.LoadMapData and
                // Minimap.GenerateWorldMap postfixes before calling DoMapMagic(). Do the same
                // here. Setting only originalMapColors is unsafe because 9.9.4 DoMapMagic()
                // checks that field for null but then accesses originalHeightColors.Length
                // unconditionally.
                fieldOriginalMapColors.SetValue(null, mapColors);
                if (fieldOriginalHeightColors != null)
                    fieldOriginalHeightColors.SetValue(null, heightColors);

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

        private static bool IsWritableColorArrayField(FieldInfo field)
        {
            return field != null && field.IsStatic && !field.IsInitOnly &&
                (field.FieldType == typeof(Color[]) || field.FieldType == typeof(Color32[]));
        }

        private static bool TryReadPixels(Texture2D texture, Type arrayType, out object pixels)
        {
            pixels = null;
            if (texture == null || !texture.isReadable)
                return false;

            if (arrayType == typeof(Color[]))
            {
                pixels = texture.GetPixels();
                return true;
            }
            if (arrayType == typeof(Color32[]))
            {
                pixels = texture.GetPixels32();
                return true;
            }
            return false;
        }
    }
}
