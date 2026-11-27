using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    /// <summary>
    /// Optional main-thread vegetation integration for distant rendering and grass builders.
    /// Returned masks are owned by Seasons; consumers must not destroy or modify them.
    /// </summary>
    public static class VegetationApi
    {
        private static int s_appearanceRevision;
        private static Season? s_appearanceSeason;

        public static int ApiVersion() => 1;

        public static bool IsShieldedClutter(ClutterSystem.Clutter clutter) =>
            ClutterVariantController.IsShieldedClutter(clutter);

        /// <summary>
        /// Fills the caller's lists with the current intersecting outer and inner
        /// shield spheres (XYZ center, W squared radius). Returns the native
        /// placement gate. False means inactive, not an API failure.
        /// </summary>
        public static bool GetGrassProtection(Vector3 patchCenter, float halfSize, List<Vector4> outer, List<Vector4> inner)
        {
            outer.Clear();
            inner.Clear();

            if (!ClutterVariantController.GrassProtectionActive)
                return false;

            // Preserve the native prefix's active-but-no-position-filter state:
            // ordinary clutter passes, protected copies are rejected.
            if (!SeasonState.IsActive || !IsShieldProtectionActive())
                return true;

            foreach (ShieldGenerator shield in ShieldGenerator.m_instances)
                if (shield && shield.m_shieldDome)
                    AddIntersectingSphere(patchCenter, halfSize, shield.m_shieldDome.transform.position, shield.m_radius, outer);

            foreach (var shield in ShieldDomeImageEffect_SetShieldData_ProtectedStateChange.shieldRadius)
                if (shield.Key)
                    AddIntersectingSphere(patchCenter, halfSize, shield.Key.GetShieldPosition(), shield.Value - 2f, inner);

            return true;
        }

        private static void AddIntersectingSphere(Vector3 patchCenter, float halfSize, Vector3 center, float radius, List<Vector4> spheres)
        {
            if (!(radius > 0f))
                return;

            float dx = Mathf.Max(0f, Mathf.Abs(center.x - patchCenter.x) - halfSize);
            float dz = Mathf.Max(0f, Mathf.Abs(center.z - patchCenter.z) - halfSize);
            float radiusSquared = radius * radius;
            if (dx * dx + dz * dz < radiusSquared)
                spheres.Add(new Vector4(center.x, center.y, center.z, radiusSquared));
        }

        public static bool ReplacesPlacementPatch(MethodInfo patchMethod) =>
            ClutterVariantController.InstanceRenderer_AddInstance_PreventInProtectedArea.ReplacesPlacementPatch(patchMethod);

        /// <summary>
        /// Follows a controlled copy using the specified autumn variant (0-3).
        /// -1 preserves the ordinary position-based selection in all seasons.
        /// </summary>
        public static bool FollowWithVariant(GameObject controlledCopy, string prefabName, int variant)
        {
            PrefabVariantController controller = PrefabVariantController.instance;
            return controller && variant >= -1 && variant < seasonColorVariants &&
                controller.FollowWithVariant(controlledCopy, prefabName, variant);
        }

        /// <summary>
        /// Returns the source albedo recolor mask and writes the configured
        /// autumn palette (four target/strength vectors and four flag vectors).
        /// The caller supplies an eight-element array. False means no correction.
        /// </summary>
        public static bool TryGetRecolor(MeshRenderer controlledRenderer, int materialIndex, out Texture mask, Vector4[] palette)
        {
            mask = null;
            PrefabVariantController controller = PrefabVariantController.instance;
            return controller && controlledRenderer && materialIndex >= 0 && palette?.Length == 8 &&
                SeasonState.IsActive && seasonState.GetCurrentSeason() == Season.Fall &&
                UseTextureControllers() && !IsAppearanceUpdating() &&
                controller.TryGetRecolor(controlledRenderer, materialIndex, out mask, palette);
        }

        public static int GetAutumnVariant(Vector3 worldPosition)
        {
            if (!PrefabVariantController.instance)
                return 3;

            PrefabVariantController.WorldToMapPoint(worldPosition, out float mx, out float my);
            return PrefabVariantController.GetVariant(PrefabVariantController.GetNoise(mx, 1f - my));
        }

        public static int AppearanceRevision() => s_appearanceRevision;

        public static bool IsAppearanceUpdating() => texturesVariants.IsUpdating;

        internal static void InvalidateAppearance() => s_appearanceRevision = unchecked(s_appearanceRevision + 1);

        internal static void NotifySeasonAppearance()
        {
            if (!SeasonState.IsActive)
                return;

            Season current = seasonState.GetCurrentSeason();
            if (s_appearanceSeason != current)
            {
                s_appearanceSeason = current;
                InvalidateAppearance();
            }
        }
    }
}
