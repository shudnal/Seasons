using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.Compatibility
{
    public static class MarketplaceCompat
    {
        public const string GUID = "MarketplaceAndServerNPCs";
        private const string TerritoryNamespace = "Marketplace.Modules.TerritorySystem.";
        private const float RetryInterval = 0.5f;

        public static PluginInfo plugin;
        public static Assembly assembly;
        public static MethodBase methodDoMapMagic;
        public static FieldInfo fieldOriginalMapColors;
        public static bool isEnabled;

        private static FieldInfo fieldOriginalHeightColors;
        private static FieldInfo fieldUseMapDraw;
        private static FieldInfo fieldTerritories;
        private static PropertyInfo propertyTerritoryValue;
        private static TerritoryApi territoryApi;
        private static ConfigEntry<bool> useMapDraw;
        private static MapContext context;
        private static bool reportedFailure;
        private static ZNet stoppedNetwork;
        private static bool stopping;

        private sealed class MapContext
        {
            internal readonly Minimap Map;
            internal readonly ZNet Network;
            internal readonly World World;
            internal readonly Texture2D MapTexture;
            internal readonly Texture2D HeightTexture;
            internal readonly Texture2D FogTexture;
            internal readonly int Size;
            internal readonly float PixelSize;
            internal Color32[] Colors;
            internal Color[] Heights;
            internal object PublishedColors;
            internal Color32[] PublishedSource;
            internal bool Pending = true;
            internal bool? DrawingEnabled;
            internal float RetryAt;
            internal BitArray Revealed;
            internal MarketplaceMapOverlay Drawing;

            internal MapContext(Minimap minimap, Color32[] colors)
            {
                Map = minimap;
                Network = ZNet.instance;
                World = ZNet.m_world;
                MapTexture = minimap.m_mapTexture;
                HeightTexture = minimap.m_heightTexture;
                FogTexture = minimap.m_fogTexture;
                Size = minimap.m_textureSize;
                PixelSize = minimap.m_pixelSize;
                Colors = colors;
            }

            internal bool IsCurrent()
            {
                return !stopping && Map && Map == Minimap.instance && MapTexture && HeightTexture && FogTexture
                    && MapTexture == Map.m_mapTexture && HeightTexture == Map.m_heightTexture
                    && FogTexture == Map.m_fogTexture && Size == Map.m_textureSize && PixelSize == Map.m_pixelSize
                    && ReferenceEquals(Network, ZNet.instance) && ReferenceEquals(World, ZNet.m_world);
            }

            internal void RequestRedraw()
            {
                Drawing?.Dispose();
                Drawing = null;
                Pending = true;
            }
        }

        public static void CheckForCompatibility()
        {
            ReleaseMap();
            stopping = false;
            stoppedNetwork = null;
            reportedFailure = false;
            isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin) && plugin.Instance != null;
            assembly = isEnabled ? plugin.Instance.GetType().Assembly : null;
            methodDoMapMagic = null;
            fieldOriginalMapColors = null;
            fieldOriginalHeightColors = null;
            fieldUseMapDraw = null;
            fieldTerritories = null;
            propertyTerritoryValue = null;
            territoryApi = null;
            useMapDraw = null;
            if (assembly == null || !UseTextureControllers())
                return;

            try
            {
                Type client = assembly.GetType(TerritoryNamespace + "TerritorySystem_Main_Client", throwOnError: true);
                Type data = assembly.GetType(TerritoryNamespace + "TerritorySystem_DataTypes", throwOnError: true);
                MethodInfo method = AccessTools.DeclaredMethod(client, "DoMapMagic", Type.EmptyTypes);
                FieldInfo colors = RequiredField(client, "originalMapColors", null, isStatic: true);
                if (method == null || !method.IsStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters
                    || (colors.FieldType != typeof(Color[]) && colors.FieldType != typeof(Color32[])) || colors.IsInitOnly)
                    throw new NotSupportedException("Unsupported territory map entry point or color array.");

                FieldInfo heights = AccessTools.DeclaredField(client, "originalHeightColors");
                if (heights != null && (!heights.IsStatic || heights.FieldType != typeof(Color[]) || heights.IsInitOnly))
                    throw new NotSupportedException("Unsupported floating-point territory height baseline.");
                fieldUseMapDraw = RequiredField(client, "UseMapDraw", typeof(ConfigEntry<bool>), isStatic: true);
                fieldTerritories = RequiredField(data, "SyncedTerritoriesData", null, isStatic: true);
                propertyTerritoryValue = AccessTools.Property(fieldTerritories.FieldType, "Value");
                if (propertyTerritoryValue == null || !propertyTerritoryValue.CanRead
                    || !typeof(IEnumerable).IsAssignableFrom(propertyTerritoryValue.PropertyType))
                    throw new NotSupportedException("Unsupported synchronized territory collection.");
                territoryApi = new TerritoryApi(data.GetNestedType("Territory", BindingFlags.Public | BindingFlags.NonPublic));
                fieldOriginalMapColors = colors;
                fieldOriginalHeightColors = heights;
                // Publish the patch target last. An unsupported optional mod must not
                // leave a half-initialized patch or prevent Seasons from loading.
                methodDoMapMagic = method;
            }
            catch (Exception error)
            {
                LogWarning($"[Marketplace] Unsupported territory map API in {plugin.Metadata.Version}; seasonal colors remain available without territory redraw.\n{Unwrap(error)}");
            }
        }

        // Called only after native generation or a successful native texture-cache
        // load, before Marketplace's postfixes can take or modify their snapshots.
        internal static void CaptureNativeMap(Minimap minimap, Color32[] pixels)
        {
            if (methodDoMapMagic == null || !minimap || pixels == null)
                return;
            if (stopping && ReferenceEquals(stoppedNetwork, ZNet.instance))
                return;
            stopping = false;
            stoppedNetwork = null;
            // Regeneration replaces terrain pixels but may leave the same fog texture.
            // Retain only our reveal mask for that exact map so old overlays can be removed.
            MapContext previous = context;
            BitArray revealed = previous != null && previous.Map == minimap && previous.IsCurrent()
                && previous.Colors.Length == pixels.Length ? previous.Revealed : null;
            ReleaseMap();
            context = new MapContext(minimap, pixels) { Revealed = revealed };
            reportedFailure = false;
            try
            {
                TryCaptureHeights(context);
            }
            catch (Exception error)
            {
                ReportFailure(error);
            }
        }

        public static void UpdateMap() => RequestRedraw(Minimap.instance);

        public static void UpdateMap(Minimap minimap) => RequestRedraw(minimap);

        internal static void UpdateMap(Minimap minimap, Color32[] pixels)
        {
            MapContext current = context;
            if (current == null || current.Map != minimap || !current.IsCurrent() || pixels == null
                || pixels.Length != current.Colors.Length)
                return;
            current.Colors = pixels;
            current.RequestRedraw();
        }

        private static void RequestRedraw(Minimap minimap)
        {
            if (context != null && context.Map == minimap && context.IsCurrent())
                context.RequestRedraw();
        }

        internal static void UpdatePendingMap(Minimap minimap)
        {
            MapContext current = context;
            if (current == null || current.Map != minimap)
                return;
            if (!current.IsCurrent())
            {
                ReleaseMap(minimap);
                return;
            }
            if (Time.realtimeSinceStartup < current.RetryAt)
                return;

            try
            {
                useMapDraw ??= fieldUseMapDraw.GetValue(null) as ConfigEntry<bool>;
                if (useMapDraw == null || !IsMapReady(current) || !TryCaptureHeights(current))
                {
                    current.RetryAt = Time.realtimeSinceStartup + RetryInterval;
                    return;
                }
                bool draw = useMapDraw.Value;
                if (current.DrawingEnabled != draw)
                {
                    current.DrawingEnabled = draw;
                    current.RequestRedraw();
                }
                if (!current.Pending)
                    return;

                if (current.Drawing == null)
                {
                    object synced = fieldTerritories.GetValue(null);
                    if (synced == null || !(propertyTerritoryValue.GetValue(synced) is IEnumerable territories))
                    {
                        current.RetryAt = Time.realtimeSinceStartup + RetryInterval;
                        return;
                    }
                    PublishBaselines(current);
                    var snapshots = new List<MarketplaceMapOverlay.Territory>();
                    if (draw)
                        foreach (object territory in territories)
                        {
                            MarketplaceMapOverlay.Territory snapshot = territoryApi.Capture(territory);
                            if (snapshot != null)
                                snapshots.Add(snapshot);
                        }
                    // OrderBy preserves collection order for equal priorities, just
                    // like Marketplace. Disabling drawing/removing all territories
                    // intentionally redraws the clean baseline instead of doing nothing.
                    current.Drawing = new MarketplaceMapOverlay(current.Colors, current.Heights,
                        current.Size, current.PixelSize, snapshots.OrderBy(territory => territory.Priority));
                }

                MarketplaceMapOverlay drawing = current.Drawing;
                if (!drawing.Advance())
                    return;
                if (context != current || current.Drawing != drawing || !current.IsCurrent() || !IsMapReady(current))
                    return;
                BitArray previouslyRevealed = current.Revealed;
                // A failed Unity texture upload may have changed part of the fog.
                // Retain the union until success so a retry can remove either overlay.
                if (drawing.Revealed != null)
                    current.Revealed = previouslyRevealed == null ? drawing.Revealed
                        : new BitArray(previouslyRevealed).Or(drawing.Revealed);
                drawing.Apply(minimap, previouslyRevealed);
                current.Revealed = drawing.Revealed;
                drawing.Dispose();
                current.Drawing = null;
                current.Pending = false;
                reportedFailure = false;
            }
            catch (Exception error)
            {
                current.RequestRedraw();
                current.RetryAt = Time.realtimeSinceStartup + RetryInterval;
                ReportFailure(error);
            }
        }

        private static bool IsMapReady(MapContext current)
        {
            Minimap minimap = current.Map;
            int size = current.Size;
            long count = (long)size * size;
            return minimap.m_hasGenerated && size > 0 && count <= int.MaxValue
                && current.PixelSize > 0f && !float.IsInfinity(current.PixelSize) && !float.IsNaN(current.PixelSize)
                && current.Colors.Length == count
                && MatchesTexture(current.MapTexture, size) && MatchesTexture(current.HeightTexture, size)
                && MatchesTexture(current.FogTexture, size)
                && minimap.m_explored != null && minimap.m_explored.Length == count
                && minimap.m_exploredOthers != null && minimap.m_exploredOthers.Length == count;
        }

        private static bool MatchesTexture(Texture2D texture, int size)
        {
            return texture && texture.isReadable && texture.width == size && texture.height == size;
        }

        private static bool TryCaptureHeights(MapContext current)
        {
            if (current.Heights != null)
                return true;
            if (!current.IsCurrent() || !MatchesTexture(current.HeightTexture, current.Size))
                return false;
            // Keep floating-point heights. Color32 would clamp RHalf height values.
            // Never refresh this baseline from a texture carrying ShowExternalWater.
            Color[] heights = current.HeightTexture.GetPixels();
            if (heights.Length != current.Colors.Length)
                return false;
            current.Heights = heights;
            return true;
        }

        private static void PublishBaselines(MapContext current)
        {
            if (!ReferenceEquals(current.PublishedSource, current.Colors))
            {
                if (fieldOriginalMapColors.FieldType == typeof(Color32[]))
                    current.PublishedColors = current.Colors;
                else
                {
                    Color[] colors = new Color[current.Colors.Length];
                    for (int i = 0; i < colors.Length; ++i)
                        colors[i] = current.Colors[i];
                    current.PublishedColors = colors;
                }
                current.PublishedSource = current.Colors;
            }
            fieldOriginalMapColors.SetValue(null, current.PublishedColors);
            fieldOriginalHeightColors?.SetValue(null, current.Heights);
        }

        internal static void ReleaseMap(Minimap minimap = null)
        {
            if (context == null || (!ReferenceEquals(minimap, null) && context.Map != minimap))
                return;
            MapContext previous = context;
            context = null;
            previous.Drawing?.Dispose();
            try
            {
                // Release only snapshots we published; do not clear a newer snapshot
                // installed by Marketplace or another mod during map initialization.
                if (fieldOriginalMapColors != null && previous.PublishedColors != null
                    && ReferenceEquals(fieldOriginalMapColors.GetValue(null), previous.PublishedColors))
                    fieldOriginalMapColors.SetValue(null, null);
                if (fieldOriginalHeightColors != null && previous.Heights != null
                    && ReferenceEquals(fieldOriginalHeightColors.GetValue(null), previous.Heights))
                    fieldOriginalHeightColors.SetValue(null, null);
            }
            catch (Exception error)
            {
                LogWarning($"[Marketplace] Could not release territory map snapshots.\n{Unwrap(error)}");
            }
        }

        private static void StopWorld()
        {
            stopping = true;
            stoppedNetwork = ZNet.instance;
            ReleaseMap();
        }

        private static Exception Unwrap(Exception error) =>
            error is TargetInvocationException invocation && invocation.InnerException != null ? invocation.InnerException : error;

        private static void ReportFailure(Exception error)
        {
            if (reportedFailure)
                return;
            reportedFailure = true;
            LogWarning($"[Marketplace] Territory map refresh failed; the current map will retry.\n{Unwrap(error)}");
        }

        private static FieldInfo RequiredField(Type type, string name, Type fieldType, bool isStatic = false)
        {
            FieldInfo field = type == null ? null : AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic != isStatic || (fieldType != null && field.FieldType != fieldType))
                throw new NotSupportedException($"Unsupported territory field: {type?.FullName}.{name}.");
            return field;
        }

        // Reuse Marketplace's visibility/color/gradient rules on independent snapshots;
        // do not duplicate its gradient math or reference its assembly at compile time.
        private sealed class TerritoryApi
        {
            private static readonly MethodInfo Clone = AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone");
            private readonly Type type;
            private readonly FieldInfo x, y, radius, priority, shape, colors, externalWater, flags, gradient;
            private readonly long revealFlag;
            private readonly MethodInfo draw, usingGradient, getColor, gradientX, gradientY, gradientXY, gradientXY2, gradientCenter;

            internal TerritoryApi(Type type)
            {
                this.type = type ?? throw new NotSupportedException("Missing territory type.");
                x = RequiredField(type, "X", typeof(int[]));
                y = RequiredField(type, "Y", typeof(int[]));
                radius = RequiredField(type, "Radius", typeof(int));
                priority = RequiredField(type, "Priority", typeof(int));
                shape = RequiredField(type, "Shape", null);
                colors = RequiredField(type, "Colors", typeof(List<Color32>));
                externalWater = RequiredField(type, "ShowExternalWater", typeof(bool));
                flags = RequiredField(type, "AdditionalFlags", null);
                gradient = RequiredField(type, "GradientType", null);
                if (!shape.FieldType.IsEnum || !flags.FieldType.IsEnum || !gradient.FieldType.IsEnum)
                    throw new NotSupportedException("Unsupported territory enum fields.");
                revealFlag = Convert.ToInt64(Enum.Parse(flags.FieldType, "RevealOnMap"));
                draw = Method("DrawOnMap", typeof(bool));
                usingGradient = Method("UsingGradient", typeof(bool));
                getColor = Method("GetColor", typeof(Color32));
                gradientX = Method("GetGradientX", typeof(Color32), typeof(float), typeof(bool));
                gradientY = Method("GetGradientY", typeof(Color32), typeof(float), typeof(bool));
                gradientXY = Method("GetGradientXY", typeof(Color32), typeof(Vector2), typeof(bool));
                gradientXY2 = Method("GetGradientXY_2", typeof(Color32), typeof(Vector2), typeof(bool));
                gradientCenter = Method("GetGradientFromCenter", typeof(Color32), typeof(Vector2), typeof(bool));
            }

            private MethodInfo Method(string name, Type result, params Type[] arguments)
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, name, arguments);
                if (method == null || method.IsStatic || method.ReturnType != result || method.ContainsGenericParameters)
                    throw new NotSupportedException($"Unsupported territory method: {type.FullName}.{name}.");
                return method;
            }

            internal MarketplaceMapOverlay.Territory Capture(object territory)
            {
                if (territory == null || !type.IsInstanceOfType(territory))
                    throw new NotSupportedException("Unexpected territory collection entry.");
                int[] sourceX = x.GetValue(territory) as int[];
                int[] sourceY = y.GetValue(territory) as int[];
                List<Color32> sourceColors = colors.GetValue(territory) as List<Color32>;
                if (sourceX == null || sourceX.Length == 0 || sourceY == null || sourceY.Length == 0 || sourceColors == null)
                    throw new InvalidOperationException("Territory map coordinates or colors are missing.");
                object snapshot = Clone.Invoke(territory, null);
                x.SetValue(snapshot, sourceX.Clone());
                y.SetValue(snapshot, sourceY.Clone());
                colors.SetValue(snapshot, new List<Color32>(sourceColors));
                if (!(bool)draw.Invoke(snapshot, null))
                    return null;
                if (!Enum.TryParse(shape.GetValue(snapshot).ToString(), out MarketplaceMapOverlay.Shape areaShape)
                    || !Enum.IsDefined(typeof(MarketplaceMapOverlay.Shape), areaShape))
                    throw new NotSupportedException("Unsupported territory map shape.");
                int areaRadius = (int)radius.GetValue(snapshot);
                bool rectangle = areaShape == MarketplaceMapOverlay.Shape.Rectangle;
                if (rectangle ? sourceX.Length < 2 || sourceY.Length < 2 || sourceX[1] <= 0 || sourceY[1] <= 0 : areaRadius <= 0)
                    throw new InvalidOperationException("Territory map dimensions must be positive.");

                return new MarketplaceMapOverlay.Territory
                {
                    AreaShape = areaShape,
                    X = sourceX[0],
                    Y = sourceY[0],
                    Width = rectangle ? sourceX[1] : 0,
                    Height = rectangle ? sourceY[1] : 0,
                    Radius = areaRadius,
                    Priority = (int)priority.GetValue(snapshot),
                    ShowExternalWater = (bool)externalWater.GetValue(snapshot),
                    RevealOnMap = (Convert.ToInt64(flags.GetValue(snapshot)) & revealFlag) != 0,
                    Color = (Color32)getColor.Invoke(snapshot, null),
                    Gradient = (bool)usingGradient.Invoke(snapshot, null) ? GetGradient(snapshot) : null
                };
            }

            private Func<Vector2, Color32> GetGradient(object territory)
            {
                string kind = gradient.GetValue(territory).ToString();
                bool reverse;
                MethodInfo method;
                switch (kind)
                {
                    case "LeftRight": case "RightLeft": case "BottomTop": case "TopBottom":
                        bool horizontal = kind == "LeftRight" || kind == "RightLeft";
                        reverse = kind == "RightLeft" || kind == "TopBottom";
                        var axis = (Func<float, bool, Color32>)(horizontal ? gradientX : gradientY)
                            .CreateDelegate(typeof(Func<float, bool, Color32>), territory);
                        return position => axis(horizontal ? position.x : position.y, reverse);
                    case "FromCenter": case "ToCenter":
                        method = gradientCenter;
                        reverse = kind == "ToCenter";
                        break;
                    case "BottomLeftTopRight": case "TopRightBottomLeft":
                        method = gradientXY;
                        reverse = kind == "TopRightBottomLeft";
                        break;
                    case "BottomRightTopLeft": case "TopLeftBottomRight":
                        method = gradientXY2;
                        reverse = kind == "TopLeftBottomRight";
                        break;
                    default:
                        return null;
                }
                var planar = (Func<Vector2, bool, Color32>)method.CreateDelegate(typeof(Func<Vector2, bool, Color32>), territory);
                return position => planar(position, reverse);
            }
        }

        [HarmonyPatch]
        private static class TerritoryMapRedraw
        {
            private static bool Prepare() => methodDoMapMagic != null;
            private static MethodBase TargetMethod() => methodDoMapMagic;

            private static bool Prefix()
            {
                // Marketplace 9.9.4's async-void renderer accesses live Unity objects
                // on a worker thread and assumes bool[] exploration fields. Redirect
                // only this renderer, including Marketplace-originated redraws.
                RequestRedraw(Minimap.instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnDestroy))]
        private static class MinimapDestroyed
        {
            private static void Prefix(Minimap __instance) => ReleaseMap(__instance);
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        private static class WorldShutdown
        {
            private static void Prefix() => StopWorld();
        }
    }
}
