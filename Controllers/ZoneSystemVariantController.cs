using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heightmap;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;

namespace Seasons
{
    public class ZoneSystemVariantController : MonoBehaviour
    {
        public class WaterState 
        {
            public GameObject m_iceSurface;
            private sealed class PropertyState
            {
                public bool IsColor;
                public bool HadOverride;
                public bool Active;
                public float OriginalFloat;
                public float AppliedFloat;
                public Color OriginalColor;
                public Color AppliedColor;
            }

            private readonly MaterialPropertyBlock m_properties = new MaterialPropertyBlock();
            private readonly Dictionary<int, PropertyState> m_ownedProperties = new Dictionary<int, PropertyState>();
            private readonly List<int> m_releasedProperties = new List<int>();
            public bool m_useGlobalWind;
            public bool m_appliedGlobalWind;
            public float m_appliedSurfaceOffset;
            public bool m_waterControlled;

            public float m_surfaceOffset;
            public float m_foamDepth;

            public Color m_colorTop;
            public Color m_colorBottom;
            public Color m_colorBottomShallow;

            public Color m_colorTopFrozen;
            public Color m_colorBottomFrozen;
            public Color m_colorBottomShallowFrozen;

            public WaterState(WaterVolume waterVolume)
            {
                waterVolume.m_waterSurface.GetPropertyBlock(m_properties);
                m_surfaceOffset = waterVolume.m_surfaceOffset;
                m_useGlobalWind = waterVolume.m_useGlobalWind;
                m_foamDepth = waterVolume.m_waterSurface.sharedMaterial.GetFloat("_FoamDepth");

                m_colorTop = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorTop");
                m_colorBottom = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorBottom");
                m_colorBottomShallow = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorBottomShallow");
                
                InitFrozenColors();
            }

            public WaterState(MeshRenderer waterSurface)
            {
                waterSurface.GetPropertyBlock(m_properties);
                m_foamDepth = waterSurface.sharedMaterial.GetFloat("_FoamDepth");

                m_colorTop = waterSurface.sharedMaterial.GetColor("_ColorTop");
                m_colorBottom = waterSurface.sharedMaterial.GetColor("_ColorBottom");
                m_colorBottomShallow = waterSurface.sharedMaterial.GetColor("_ColorBottomShallow");

                InitFrozenColors();
            }

            private void InitFrozenColors()
            {
                m_colorTopFrozen = new Color(0.98f, 0.98f, 1f);
                m_colorBottomFrozen = Color.Lerp(m_colorBottom, Color.white, 0.5f);
                m_colorBottomShallowFrozen = Color.Lerp(m_colorBottomShallow, Color.white, 0.5f);
            }

            public void RestoreProperties(MeshRenderer renderer, bool inactiveOnly = false)
            {
                if (renderer == null || renderer.sharedMaterial == null)
                    return;
                renderer.GetPropertyBlock(m_properties);
                m_releasedProperties.Clear();
                bool changed = false;
                foreach (KeyValuePair<int, PropertyState> entry in m_ownedProperties)
                {
                    PropertyState property = entry.Value;
                    if (inactiveOnly && property.Active)
                        continue;
                    bool stillOwned = m_properties.HasProperty(entry.Key) && (property.IsColor
                        ? m_properties.GetColor(entry.Key) == property.AppliedColor
                        : m_properties.GetFloat(entry.Key).Equals(property.AppliedFloat));
                    if (!stillOwned)
                    {
                        m_releasedProperties.Add(entry.Key);
                        continue;
                    }

                    if (property.IsColor)
                    {
                        property.AppliedColor = property.HadOverride ? property.OriginalColor : renderer.sharedMaterial.GetColor(entry.Key);
                        m_properties.SetColor(entry.Key, property.AppliedColor);
                    }
                    else
                    {
                        property.AppliedFloat = property.HadOverride ? property.OriginalFloat : renderer.sharedMaterial.GetFloat(entry.Key);
                        m_properties.SetFloat(entry.Key, property.AppliedFloat);
                    }
                    changed = true;
                    property.Active = false;
                    if (property.HadOverride)
                        m_releasedProperties.Add(entry.Key);
                }
                foreach (int property in m_releasedProperties)
                    m_ownedProperties.Remove(property);
                if (changed)
                    renderer.SetPropertyBlock(m_properties);
            }

            public void SetFloat(int property, float value)
            {
                if (!m_ownedProperties.TryGetValue(property, out PropertyState state))
                {
                    state = new PropertyState { HadOverride = m_properties.HasProperty(property), OriginalFloat = m_properties.GetFloat(property) };
                    m_ownedProperties.Add(property, state);
                }
                state.Active = true;
                state.AppliedFloat = value;
                m_properties.SetFloat(property, value);
            }

            public void SetFloat(string property, float value) => SetFloat(Shader.PropertyToID(property), value);

            public void SetColor(string propertyName, Color value)
            {
                int property = Shader.PropertyToID(propertyName);
                if (!m_ownedProperties.TryGetValue(property, out PropertyState state))
                {
                    state = new PropertyState { IsColor = true, HadOverride = m_properties.HasProperty(property), OriginalColor = m_properties.GetColor(property) };
                    m_ownedProperties.Add(property, state);
                }
                state.Active = true;
                state.AppliedColor = value;
                m_properties.SetColor(property, value);
            }

            public void ApplyProperties(MeshRenderer renderer) => renderer.SetPropertyBlock(m_properties);
        }

        private class FrozenOceanFishPositionGuard : MonoBehaviour
        {
            public float m_nextCheckTime;
        }

        private class FrozenShipState : MonoBehaviour
        {
            public Rigidbody Body;
            public ZSyncTransform SyncTransform;
            public bool IsKinematic;
            public bool SyncIsKinematic;
            public bool AppliedIsKinematic;
            public bool WasOwner;
            public readonly Dictionary<GameObject, bool> WaterMasks = new Dictionary<GameObject, bool>();

            public void Restore()
            {
                if (Body != null && Body.isKinematic == AppliedIsKinematic)
                    Body.isKinematic = IsKinematic;
                if (SyncTransform != null && SyncTransform.m_isKinematicBody == AppliedIsKinematic)
                    SyncTransform.m_isKinematicBody = SyncIsKinematic;
                foreach (KeyValuePair<GameObject, bool> mask in WaterMasks)
                    if (mask.Key != null && !mask.Key.activeSelf)
                        mask.Key.SetActive(mask.Value);
                WaterMasks.Clear();
                Body = null;
                SyncTransform = null;
            }
        }

        private static MeshRenderer s_waterPlane;
        private static WaterState s_waterPlaneState;
        public static float s_waterEdge;
        public static bool s_waterEdgeLocalPlayerState;
        public static float s_waterDistance;

        public static readonly Dictionary<WaterVolume, WaterState> waterStates = new Dictionary<WaterVolume, WaterState>();
        
        private static readonly List<ZDO> m_tempZDOList = new List<ZDO>();
        private static readonly HashSet<ZoneSystem.SectorIndex> s_visitedSectorIndices = new HashSet<ZoneSystem.SectorIndex>();
        private static readonly List<Vector3> m_tempHits = new List<Vector3>();

        private static float s_freezeStatus = 0f;

        public static float s_colliderHeight = 0f;

        public const float _winterWaterSurfaceOffset = 2f;
        public const float _colliderOffset = 0.01f;
        public const string _iceSurfaceName = "IceSurface";
        public static readonly int s_playerDroppedFish = "Seasons_PlayerDroppedFish".GetStableHashCode();

        private const float FishIceCheckInterval = 5f;
        private const float FishIceRayStartAboveWater = 0.5f;
        private const float FishIceRayEndBelowTarget = 0.25f;
        private const float FishIceCheckRandomJitter = 1f;
        private static readonly RaycastHit[] s_fishIceHits = new RaycastHit[8];

        public const string _iceFloeName = "ice1";
        public static int s_iceFloePrefab = _iceFloeName.GetStableHashCode();
        public static Vector2 s_floeSize = new Vector2(8.36f, 8.0f) / 2;

        public static GameObject s_iceSurface;
        public static ZoneSystem.ZoneVegetation s_iceFloe;
        public static int s_zoneCtrlPrefab;
        
        public static int s_terrainCompilerPrefab;
        public const int s_terrainCompVersion = 1;

        private const float _FoamDepthFrozen = 10f;
        private const float _WaveVel = 0f;
        private const float _WaveFoam = 0f;
        private const float _Glossiness = 0.95f;
        private const float _Metallic = 0.1f;
        private const float _DepthFade = 20f;
        private const float _ShoreFade = 0f;

        public float m_createDestroyTimer;
        public RaycastHit[] rayHits = new RaycastHit[200];
        
        private ParticleSystem m_snowStorm;
        private Biome m_currentBiome;
        private int m_snowStormMaxParticles;
        private float m_snowStormEmissionRate;

        private void RestoreSnowStorm()
        {
            if (m_snowStorm == null)
                return;
            ParticleSystem.MainModule main = m_snowStorm.main;
            ParticleSystem.EmissionModule emission = m_snowStorm.emission;
            emission.rateOverTimeMultiplier = m_snowStormEmissionRate;
            main.maxParticles = m_snowStormMaxParticles;
        }

        internal static bool waterStateInitialized = false;
        private static ZoneSystemVariantController m_instance;

        public static ZoneSystemVariantController Instance => m_instance;

        public readonly List<WaterVolume> waterVolumesCheckFloes = new List<WaterVolume>();
        
        private static readonly List<WaterVolume> tempWaterVolumesList = new List<WaterVolume>();
        private static readonly List<ZoneSystem.ClearArea> m_tempClearAreas = new List<ZoneSystem.ClearArea>();
        private static readonly List<GameObject> m_tempSpawnedObjects = new List<GameObject>();
        private static readonly List<Color32> s_tempColors = new List<Color32>();
        private static readonly List<Color32> s_smoothColors = new List<Color32>();
        private static readonly List<Heightmap> s_protectedHeightmaps = new List<Heightmap>();
        private static readonly List<Heightmap> s_tempHeightmaps = new List<Heightmap>();

        public static bool IsWaterSurfaceFrozen() => s_freezeStatus == 1f;
        
        public static bool IsTimeForIceFloes() => enableIceFloes.Value && !IsWaterSurfaceFrozen() && seasonState.GetCurrentSeason() == Season.Winter && (int)iceFloesInWinterDays.Value.x <= seasonState.GetCurrentDay() && seasonState.GetCurrentDay() <= (int)iceFloesInWinterDays.Value.y;

        public static bool IsTimeToDecultivateGround() => cultivatedGroundTurnsIntoDirtInWinter.Value && seasonState.GetCurrentSeason() == Season.Winter;

        public static float WaterLevel => s_colliderHeight == 0f || !IsWaterSurfaceFrozen() ? ZoneSystem.instance.m_waterLevel : s_colliderHeight;

        public static bool IsBeyondWorldEdge(Vector3 position, float offset = 0f) => Utils.DistanceXZ(Vector3.zero, position) > s_waterEdge - offset;

        private void Awake()
        {
            m_instance = this;
        }

        public void Update()
        {
            // MPB has no per-property removal; released overrides follow the current material values.
            s_waterPlaneState?.RestoreProperties(s_waterPlane, inactiveOnly: true);
            float deltaTime = Time.deltaTime;
            m_createDestroyTimer += deltaTime;
            if (m_createDestroyTimer >= (1f / 15f) && waterVolumesCheckFloes.Count > 0)
            {
                m_createDestroyTimer = 0f;
                CreateDestroyFloes();
            }
        }

        private void CreateDestroyFloes()
        {
            m_tempClearAreas.Clear();

            if (!waterStateInitialized)
                return;

            tempWaterVolumesList.Clear();
            foreach (WaterVolume waterVolume in waterVolumesCheckFloes)
            {
                if (!CheckWaterVolumeForIceFloes(waterVolume))
                    tempWaterVolumesList.Add(waterVolume);
            }
            waterVolumesCheckFloes.Clear();
            waterVolumesCheckFloes.AddRange(tempWaterVolumesList);
            
            tempWaterVolumesList.Clear();
        }

        private void OnDestroy()
        {
            RestoreSnowStorm();
            foreach (Ship ship in Ship.Instances.ToArray().Cast<Ship>())
                if (ship != null)
                    ship.GetComponent<FrozenShipState>()?.Restore();
            foreach (KeyValuePair<WaterVolume, WaterState> waterState in waterStates)
                if (waterState.Key != null)
                    UpdateWater(waterState.Key, waterState.Value, revertState: true);
            s_waterPlaneState?.RestoreProperties(s_waterPlane);
            s_waterPlane = null;
            s_waterPlaneState = null;
            s_iceSurface = null;
            s_iceFloe = null;
            s_freezeStatus = 0f;
            s_colliderHeight = 0f;
            waterStates.Clear();
            m_tempZDOList.Clear();
            s_visitedSectorIndices.Clear();
            s_protectedHeightmaps.Clear();
            s_tempHeightmaps.Clear();
            ZoneSystem_GetGroundHeight_CheckForIceSurface.checkForIceSurface = false;
            m_instance = null;
            waterStateInitialized = false;
        }

        public void Initialize(ZoneSystem instance)
        {
            Transform waterPlane = EnvMan.instance.transform.Find("WaterPlane");
            if (waterPlane != null)
                s_waterPlane = waterPlane.GetComponentInChildren<MeshRenderer>();

            if (s_waterPlane != null)
                s_waterPlaneState = new WaterState(s_waterPlane);

            Transform water = instance.m_zonePrefab.transform.Find("Water");
            if (water != null)
                AddIceCollider(water);

            s_iceFloe = instance.m_vegetation.Find(veg => veg.m_prefab?.name == _iceFloeName)?.Clone();
            if (s_iceFloe?.m_prefab == null)
            {
                LogWarning("Unable to initialize seasonal ice floes: the ice1 vegetation prefab was not found.");
                return;
            }

            s_iceFloe.m_biome = Biome.Ocean;
            ZNetView floeView = s_iceFloe.m_prefab.GetComponent<ZNetView>();
            if (floeView == null || s_iceFloe.m_prefab.GetComponent<Rigidbody>() == null)
            {
                LogWarning("Unable to initialize seasonal ice floes: the ice1 prefab has no network view or rigidbody.");
                s_iceFloe = null;
                return;
            }
            if (!s_iceFloe.m_prefab.TryGetComponent<IceFloeClimb>(out _))
                s_iceFloe.m_prefab.AddComponent<IceFloeClimb>();

            floeView.m_syncInitialScale = true;
        }

        public bool BiomeChanged(Biome biome)
        {
            if (m_currentBiome == biome)
                return false;

            m_currentBiome = biome;
            return true;
        }

        public void CheckBiomeChanged(Biome biome)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (reduceSnowStormInWinter.Value == Vector2.zero || Player.m_localPlayer == null)
            {
                RestoreSnowStorm();
                return;
            }

            if (m_snowStorm == null)
            {
                Transform snowStormTransform = EnvMan.instance.transform.Find("FollowPlayer/SnowStorm") ?? Utils.FindChild(EnvMan.instance.transform, "SnowStorm");
                if (snowStormTransform == null)
                    return;

                Transform snowParticles = snowStormTransform.Find("snow (1)");
                if (snowParticles == null)
                    return;

                m_snowStorm = snowParticles.GetComponent<ParticleSystem>();
                if (m_snowStorm == null)
                    return;
                m_snowStormMaxParticles = m_snowStorm.main.maxParticles;
                m_snowStormEmissionRate = m_snowStorm.emission.rateOverTimeMultiplier;
            }

            ParticleSystem.MainModule snowStormMain = m_snowStorm.main;
            ParticleSystem.EmissionModule snowStormEmission = m_snowStorm.emission;

            bool reduceParticles = seasonState.GetCurrentSeason() == Season.Winter && biome != Biome.Mountain && biome != Biome.AshLands && biome != Biome.DeepNorth;

            snowStormEmission.rateOverTimeMultiplier = reduceParticles ? reduceSnowStormInWinter.Value.x : m_snowStormEmissionRate;
            snowStormMain.maxParticles = reduceParticles ? (int)reduceSnowStormInWinter.Value.y : m_snowStormMaxParticles;
        }
        
        public static void SnowStormReduceParticlesChanged()
        {
            if (!Instance)
                return;

            Instance.m_currentBiome = Biome.None;
        }

        public static void UpdateTerrainColor(Heightmap heightmap)
        {
            if (heightmap?.m_renderMesh == null)
                return;

            bool previousOverride = Heightmap_GetBiomeColor_TerrainColor.overrideColor;
            Heightmap_GetBiomeColor_TerrainColor.overrideColor = true;
            s_tempColors.Clear();

            try
            {
                int num = heightmap.m_width + 1;
                Vector3 vector = heightmap.transform.position + new Vector3((float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5), 0f, (float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5));

                bool hasShieldedPosition = false;
                for (int i = 0; i < num; i++)
                    for (int j = 0; j < num; j++)
                        if (heightmap.m_isDistantLod)
                        {
                            float wx = vector.x + j * heightmap.m_scale;
                            float wy = vector.z + i * heightmap.m_scale;
                            BiomeSector biome = WorldGenerator.instance.GetBiomeSector(wx, wy);
                            s_tempColors.Add(GetBiomeColor(biome));
                        }
                        else
                        {
                            float ix = DUtils.SmoothStep(0f, 1f, (float)j / heightmap.m_width);
                            float iy = DUtils.SmoothStep(0f, 1f, (float)i / heightmap.m_width);
                            Vector3 position = heightmap.transform.position + heightmap.CalcVertex(j, i);
                            if (IsProtectedHeightmap(heightmap) && IsShieldedPosition(position))
                            {
                                hasShieldedPosition = true;
                                s_tempColors.Add(Heightmap_GetBiomeColor_TerrainColor.GetOriginalColor(heightmap, ix, iy));
                            }
                            else
                            {
                                s_tempColors.Add(heightmap.GetBiomeColor(ix, iy));
                            }
                        }

                if (hasShieldedPosition)
                    SmoothenProtectedBorders(s_tempColors, heightmap.m_width + 1);

                heightmap.m_renderMesh.SetColors(s_tempColors);
            }
            finally
            {
                Heightmap_GetBiomeColor_TerrainColor.overrideColor = previousOverride;
                s_tempColors.Clear();
            }
        }

        public static void SmoothenProtectedBorders(List<Color32> colors, int size)
        {
            s_smoothColors.Clear();
            s_smoothColors.AddRange(colors);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    int count = 0;

                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                            {
                                Color32 neighbor = colors[ny * size + nx];

                                r += neighbor.r;
                                g += neighbor.g;
                                b += neighbor.b;
                                a += neighbor.a;
                                count++;
                            }
                        }

                    s_smoothColors[y * size + x] = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
                }
            }

            colors.Clear();
            colors.AddRange(s_smoothColors);
            s_smoothColors.Clear();
        }

        public static void UpdateTerrainColors()
        {
            UpdateTerrainColorsFromList(Instances.Cast<Heightmap>());
        }

        public static void UpdateTerrainColorsFromList(IEnumerable<Heightmap> list)
        {
            UpdateProtectedHeightmaps();
            foreach (Heightmap instance in list)
                UpdateTerrainColor(instance);
        }

        private static void UpdateProtectedHeightmaps()
        {
            s_protectedHeightmaps.Clear();
            if (IsShieldProtectionActive())
                foreach (ShieldGenerator instance in ShieldGenerator.m_instances)
                    FindHeightmap(instance.m_shieldDome?.transform.position ?? instance.transform.position, instance.m_maxShieldRadius + 1, s_protectedHeightmaps);
        }

        public static bool IsProtectedHeightmap(Heightmap heightmap)
        {
            return heightmap != null && s_protectedHeightmaps.Contains(heightmap);
        }

        public static void UpdateTerrainColorsAroundPosition(Vector3 position, float radius, float delay = 0f)
        {
            if (Instance == null)
                return;

            if (delay == 0f)
                UpdateTerrainAroundPosition(position, radius);
            else
                Instance.StartCoroutine(UpdateTerrainColorsAroundPositionDelayed(position, radius, delay));
        }

        public static IEnumerator UpdateTerrainColorsAroundPositionDelayed(Vector3 position, float radius, float delay = 0f)
        {
            ZoneSystemVariantController controller = Instance;
            yield return new WaitForSeconds(delay);

            if (controller == null || Instance != controller || ZoneSystem.instance == null)
                yield break;

            UpdateTerrainAroundPosition(position, radius);
        }

        private static void UpdateTerrainAroundPosition(Vector3 position, float radius)
        {
            ClutterVariantController.UpdateShieldActiveState();

            s_tempHeightmaps.Clear();
            FindHeightmap(position, radius, s_tempHeightmaps);

            UpdateTerrainColorsFromList(s_tempHeightmaps);

            ClutterSystem.instance?.ResetGrass(position, radius + 1);
        }

        public static void AddIceCollider(Transform water)
        {
            if (s_iceSurface != null)
                return;

            Transform waterSurface = water.Find("WaterSurface");
            if (waterSurface == null || !waterSurface.TryGetComponent(out MeshFilter waterMesh) || waterMesh.sharedMesh == null)
            {
                LogWarning("Unable to initialize the seasonal ice collider: the zone water surface mesh was not found.");
                return;
            }

            // The prefab can survive a world change, but the world-specific cached height cannot.
            s_colliderHeight = waterSurface.position.y + _colliderOffset;

            Transform iceSurfaceTransform = water.Find(_iceSurfaceName);
            if (iceSurfaceTransform != null)
            {
                s_iceSurface = iceSurfaceTransform.gameObject;
                return;
            }

            s_iceSurface = new GameObject(_iceSurfaceName);
            s_iceSurface.transform.SetParent(water);
            s_iceSurface.layer = 0;
            s_iceSurface.transform.localScale = new Vector3(waterSurface.transform.localScale.x, Math.Abs(_colliderOffset), waterSurface.transform.localScale.z);
            s_iceSurface.transform.localPosition = new Vector3(0, _colliderOffset, 0);
            s_iceSurface.SetActive(false);

            MeshCollider iceCollider = s_iceSurface.gameObject.AddComponent<MeshCollider>();
            iceCollider.sharedMesh = waterMesh.sharedMesh;
            iceCollider.material.staticFriction = 0.1f;
            iceCollider.material.dynamicFriction = 0.1f;
            iceCollider.material.frictionCombine = PhysicsMaterialCombine.Minimum;
            iceCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
        }

        public static void UpdateWaterState()
        {
            if (!SeasonState.IsActive)
                return;

            s_freezeStatus = seasonState.GetWaterSurfaceFreezeStatus();

            waterStateInitialized = true;

            CheckZDODatabase();

            foreach (KeyValuePair<WaterVolume, WaterState> waterState in waterStates)
                UpdateWater(waterState.Key, waterState.Value);

            UpdateWaterSurface(s_waterPlane, s_waterPlaneState);

            Instance?.StartCoroutine(UpdateWaterObjects());
        }

        public static void UpdateWater(WaterVolume waterVolume, WaterState waterState, bool revertState = false)
        {
            SetupIceCollider(waterVolume, waterState, revertState);

            if (!waterState.m_waterControlled || waterVolume.m_useGlobalWind != waterState.m_appliedGlobalWind)
                waterState.m_useGlobalWind = waterVolume.m_useGlobalWind;
            if (!waterState.m_waterControlled || !waterVolume.m_surfaceOffset.Equals(waterState.m_appliedSurfaceOffset))
                waterState.m_surfaceOffset = waterVolume.m_surfaceOffset;

            if (s_freezeStatus == 0f || revertState)
            {
                waterState.RestoreProperties(waterVolume.m_waterSurface);

                waterVolume.m_surfaceOffset = waterState.m_surfaceOffset;
                waterVolume.m_useGlobalWind = waterState.m_useGlobalWind;
                waterState.m_waterControlled = false;
                if (waterVolume.m_waterSurface != null)
                {
                    waterVolume.SetupMaterial();
                    waterState.RestoreProperties(waterVolume.m_waterSurface, inactiveOnly: true);
                }

                return;
            }

            UpdateWaterSurface(waterVolume.m_waterSurface, waterState);

            waterVolume.m_surfaceOffset = waterState.m_appliedSurfaceOffset = waterState.m_surfaceOffset - (IsWaterSurfaceFrozen() ? _winterWaterSurfaceOffset : 0);
            waterVolume.m_useGlobalWind = waterState.m_appliedGlobalWind = waterState.m_useGlobalWind && !IsWaterSurfaceFrozen();
            waterState.m_waterControlled = true;
            if (waterVolume.m_waterSurface != null)
            {
                waterVolume.SetupMaterial();
                waterState.RestoreProperties(waterVolume.m_waterSurface, inactiveOnly: true);
            }
        }

        private static void UpdateWaterSurface(MeshRenderer waterSurface, WaterState waterState)
        {
            if (waterSurface == null || waterState == null)
                return;

            waterState.RestoreProperties(waterSurface);
            if (s_freezeStatus == 0f)
                return;

            waterState.SetColor("_FoamColor", new Color(0.95f, 0.96f, 0.98f));
            waterState.SetFloat("_FoamDepth", Mathf.Lerp(waterState.m_foamDepth, _FoamDepthFrozen, s_freezeStatus));
            waterState.SetColor("_ColorTop", Color.Lerp(waterState.m_colorTop, waterState.m_colorTopFrozen, s_freezeStatus));
            waterState.SetColor("_ColorBottom", Color.Lerp(waterState.m_colorBottom, waterState.m_colorBottomFrozen, s_freezeStatus));
            waterState.SetColor("_ColorBottomShallow", Color.Lerp(waterState.m_colorBottomShallow, waterState.m_colorBottomShallowFrozen, s_freezeStatus));

            if (IsWaterSurfaceFrozen())
            {
                waterState.SetFloat(WaterVolume.s_shaderWaterTime, 0f);
                waterState.SetFloat(WaterVolume.s_shaderUseGlobalWind, 0f);

                waterState.SetFloat("_DepthFade", _DepthFade);
                waterState.SetFloat("_Glossiness", _Glossiness);
                waterState.SetFloat("_Metallic", _Metallic);
                waterState.SetFloat("_ShoreFade", _ShoreFade);
                waterState.SetFloat("_WaveVel", _WaveVel);
                waterState.SetFloat("_WaveFoam", _WaveFoam);
            }

            waterState.ApplyProperties(waterSurface);
        }

        private static void SetupIceCollider(WaterVolume waterVolume, WaterState waterState, bool revertState)
        {
            if (waterState.m_iceSurface == null)
                waterState.m_iceSurface = waterVolume.transform.parent?.Find(_iceSurfaceName)?.gameObject;
            
            if (revertState)
                waterState.m_iceSurface?.SetActive(false);
            else
                waterState.m_iceSurface?.SetActive(IsWaterSurfaceFrozen());
        }

        public static bool LocalPlayerIsOnFrozenOcean() => IsWaterSurfaceFrozen()
                                        && Player.m_localPlayer != null
                                        && Player.m_localPlayer.GetCurrentBiome() == Biome.Ocean;

        public static IEnumerator UpdateWaterObjects()
        {
            ZoneSystemVariantController controller = Instance;
            yield return waitForFixedUpdate;

            if (controller == null || Instance != controller || ZoneSystem.instance == null)
                yield break;

            foreach (WaterVolume waterVolume in waterStates.Keys.ToArray())
            {
                if (waterVolume == null)
                    continue;

                if (IsWaterSurfaceFrozen())
                {
                    foreach (IWaterInteractable waterInteractable in waterVolume.m_inWater.ToArray())
                        if (waterInteractable is Fish fish)
                            CheckIfFishAboveSurface(fish);
                        else if (waterInteractable is Character character)
                            CheckIfCharacterBelowSurface(character);
                        else if (waterInteractable is Floating floating)
                            CheckIfFloatingContainerBelowSurface(floating);
                }

                if (!controller.waterVolumesCheckFloes.Contains(waterVolume))
                    controller.waterVolumesCheckFloes.Add(waterVolume);
            }

            yield return waitForFixedUpdate;

            if (controller == null || Instance != controller || ZoneSystem.instance == null)
                yield break;

            foreach (Ship ship in Ship.Instances.ToArray().Cast<Ship>())
            {
                if (controller == null || Instance != controller || ZoneSystem.instance == null)
                    yield break;
                yield return CheckIfShipBelowSurface(ship);
            }
        }

        public static IEnumerator CheckSingleFishPosition(Fish fish)
        {
            ZoneSystemVariantController controller = Instance;
            yield return waitForFixedUpdate;
            if (controller == null || Instance != controller || !IsWaterSurfaceFrozen())
                yield break;
            CheckIfFishAboveSurface(fish);
        }

        public static bool IsUnderwaterAI(Character character, out BaseAI ai)
        {
            return character.TryGetComponent(out ai) && (ai.m_pathAgentType == Pathfinding.AgentType.Fish || ai.m_pathAgentType == Pathfinding.AgentType.BigFish);
        }

        public static void UpdateShipsPositions()
        {
            foreach (Ship ship in Ship.Instances.ToArray().Cast<Ship>())
                PlaceShip(ship);
        }

        public static void UpdateFloatingPositions()
        {
            foreach (Floating floating in Floating.Instances.ToArray().Cast<Floating>())
                CheckIfFloatingContainerBelowSurface(floating);
        }

        public static IEnumerator CheckIfShipBelowSurface(Ship ship)
        {
            ZNetScene scene = ZNetScene.instance;
            while (ship != null && ship.m_nview != null && ship.m_nview.IsValid() && !ship.m_nview.HasOwner()
                && scene != null && ZNetScene.instance == scene && ZoneSystem.instance != null)
                yield return waitForFixedUpdate;

            if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid() || scene == null || ZNetScene.instance != scene || ZoneSystem.instance == null)
                yield break;

            PlaceShip(ship);
        }

        public static void PlaceShip(Ship ship)
        {
            if (ship == null)
                return;

            FrozenShipState state = ship.GetComponent<FrozenShipState>();
            state?.Restore();
            if (ZoneSystem.instance == null || ship.m_nview == null || !ship.m_nview.IsValid() || !ship.m_nview.IsOwner() || ship.m_body == null || !IsWaterSurfaceFrozen())
                return;

            List<MeshRenderer> watermask = ship.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                .Where(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name == "Custom/WaterMask").ToList();

            float positionDelta = ship.m_body.position.y - (WaterLevel + ship.m_waterLevelOffset);
            if (positionDelta > 0)
                return;

            state ??= ship.gameObject.AddComponent<FrozenShipState>();
            state.Body = ship.m_body;
            state.IsKinematic = ship.m_body.isKinematic;
            state.SyncTransform = ship.GetComponent<ZSyncTransform>();
            state.SyncIsKinematic = state.SyncTransform != null && state.SyncTransform.m_isKinematicBody;
            state.AppliedIsKinematic = !placeShipAboveFrozenOcean.Value;
            ship.m_body.WakeUp();
            ship.m_body.isKinematic = !placeShipAboveFrozenOcean.Value;

            if (state.SyncTransform != null)
                state.SyncTransform.m_isKinematicBody = ship.m_body.isKinematic;

            if (placeShipAboveFrozenOcean.Value)
            {
                ship.m_body.rotation = Quaternion.identity;
                ship.m_body.position = new Vector3(ship.m_body.position.x, WaterLevel + ship.m_waterLevelOffset + 0.1f, ship.m_body.position.z);
                ship.m_body.linearVelocity = Vector3.zero;
            }
            else if (frozenKarvePositionFix.Value && Utils.GetPrefabName(ship.name) == "Karve" && positionDelta <= -1.43f)
            {
                ship.m_body.rotation = Quaternion.identity;
                ship.m_body.position = new Vector3(ship.m_body.position.x, WaterLevel + ship.m_waterLevelOffset - 1.42f, ship.m_body.position.z);
                ship.m_body.linearVelocity = Vector3.zero;
            }
            else if (positionDelta < -ship.m_waterLevelOffset * 1.5f && ship.m_body.isKinematic)
            {
                foreach (MeshRenderer renderer in watermask)
                {
                    state.WaterMasks[renderer.gameObject] = renderer.gameObject.activeSelf;
                    renderer.gameObject.SetActive(false);
                }
            }
        }

        public static void CheckShipOwnership(Ship ship)
        {
            if (ship == null || ship.m_nview == null || !ship.m_nview.IsValid())
                return;
            FrozenShipState state = ship.GetComponent<FrozenShipState>() ?? ship.gameObject.AddComponent<FrozenShipState>();
            bool isOwner = ship.m_nview.IsOwner();
            if (state.WasOwner == isOwner)
                return;
            state.WasOwner = isOwner;
            PlaceShip(ship);
        }

        public static void CheckIfFishAboveSurface(Fish fish)
        {
            if (!IsWaterSurfaceFrozen() || ZoneSystem.instance == null || fish == null || fish.m_nview == null || !fish.m_nview.IsValid())
                return;

            if (IsPlayerDroppedFish(fish))
                return;

            if (!fish.m_nview.IsOwner())
                return;

            float maximumLevel = WaterLevel - _winterWaterSurfaceOffset - fish.m_height - 1.5f;
            if (fish.transform.position.y <= maximumLevel)
                return;

            if (!IsFishAboveFrozenSurface(fish, maximumLevel))
                return;

            fish.transform.position = new Vector3(fish.transform.position.x, maximumLevel, fish.transform.position.z);
            fish.m_nview.GetZDO().SetPosition(fish.transform.position);

            if (fish.m_body != null)
                fish.m_body.linearVelocity = Vector3.zero;

            fish.m_haveWaypoint = false;
            fish.m_isJumping = false;
        }

        private static bool IsFishAboveFrozenSurface(Fish fish, float maximumLevel)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null)
                return false;

            Vector3 origin = fish.transform.position;
            origin.y = Mathf.Max(fish.transform.position.y + 0.25f, WaterLevel + FishIceRayStartAboveWater);

            float distance = origin.y - (maximumLevel - FishIceRayEndBelowTarget);
            if (distance <= 0f)
                return false;

            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, s_fishIceHits, distance, zoneSystem.m_solidRayMask, QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
                return false;

            RaycastHit nearestHit = default;
            float nearestDistance = float.MaxValue;
            bool hasNearest = false;

            for (int i = 0; i < hitCount; ++i)
            {
                RaycastHit hit = s_fishIceHits[i];
                if (hit.collider == null)
                    continue;

                if (hit.collider.transform.IsChildOf(fish.transform))
                    continue;

                if (hit.distance >= nearestDistance)
                    continue;

                nearestHit = hit;
                nearestDistance = hit.distance;
                hasNearest = true;
            }

            return hasNearest && IsIceSurfaceCollider(nearestHit.collider);
        }

        private static bool IsIceSurfaceCollider(Collider collider)
        {
            if (collider == null)
                return false;

            for (Transform current = collider.transform; current != null; current = current.parent)
                if (Utils.GetPrefabName(current.name) == _iceSurfaceName)
                    return true;

            return false;
        }

        public static bool IsPlayerDroppedFish(Fish fish)
        {
            return fish != null && fish.m_nview != null && fish.m_nview.IsValid() && fish.m_nview.GetZDO().GetBool(s_playerDroppedFish);
        }

        public static void MarkPlayerDroppedFish(ItemDrop itemDrop)
        {
            if (itemDrop == null || !itemDrop.TryGetComponent(out Fish fish) || fish.m_nview == null || !fish.m_nview.IsValid())
                return;

            fish.m_nview.GetZDO().Set(s_playerDroppedFish, true);
        }

        internal static bool ShouldThrottleFishIceCheck(Fish fish)
        {
            if (fish == null)
                return true;

            FrozenOceanFishPositionGuard guard = fish.GetComponent<FrozenOceanFishPositionGuard>() ?? fish.gameObject.AddComponent<FrozenOceanFishPositionGuard>();
            if (Time.time < guard.m_nextCheckTime)
                return true;

            guard.m_nextCheckTime = Time.time + FishIceCheckInterval + UnityEngine.Random.Range(0f, FishIceCheckRandomJitter);
            return false;
        }

        public static void CheckIfCharacterBelowSurface(Character character)
        {
            if (!IsWaterSurfaceFrozen() || ZoneSystem.instance == null || character == null || character.m_nview == null || !character.m_nview.IsValid())
                return;

            if (!character.m_nview.IsOwner())
                return;

            if (IsUnderwaterAI(character, out BaseAI ai))
            {
                if (character.transform.position.y >= WaterLevel)
                {
                    m_tempHits.Clear();
                    Pathfinding.instance.FindGround(character.transform.position, testWater: true, m_tempHits, Pathfinding.instance.GetSettings(ai.m_pathAgentType));

                    Vector3 hit = m_tempHits.Find(h => h.y < WaterLevel);
                    if (hit.y != 0)
                    {
                        character.m_body.linearVelocity = Vector3.zero;
                        character.transform.position = new Vector3(character.transform.position.x, Mathf.Max(WaterLevel - _winterWaterSurfaceOffset, hit.y + 0.1f), character.transform.position.z);
                    }
                }
            }
            else if (character.transform.position.y <= WaterLevel && !character.IsAttachedToShip())
            {
                character.m_body.linearVelocity = Vector3.zero;
                character.transform.position = new Vector3(character.transform.position.x, WaterLevel + 0.5f, character.transform.position.z);
                character.InvalidateCachedLiquidDepth();
                character.m_maxAirAltitude = character.transform.position.y;
                character.m_swimTimer = 0.6f;
            }
        }

        public static void CheckIfFloatingContainerBelowSurface(Floating floating)
        {
            if (!placeFloatingContainersAboveFrozenOcean.Value)
                return;

            if (floating == null || floating.m_nview == null || !floating.m_nview.IsValid())
                return;

            if (!floating.m_nview.IsOwner())
                return;

            if (floating.GetComponent<Container>() == null)
                return;

            floating.m_body.WakeUp();

            float positionDelta = floating.m_body.position.y - (WaterLevel + floating.m_waterLevelOffset);
            if (positionDelta > 0 || !IsWaterSurfaceFrozen())
                return;

            floating.m_body.rotation = Quaternion.identity;
            floating.m_body.position = new Vector3(floating.m_body.position.x, WaterLevel + floating.m_waterLevelOffset + 0.1f, floating.m_body.position.z);
            floating.m_body.linearVelocity = Vector3.zero;
        }

        public static void CheckZDODatabase()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
                return;

            if (s_zoneCtrlPrefab == 0)
                s_zoneCtrlPrefab = (ZoneSystem.instance == null ? "_ZoneCtrl" : Utils.GetPrefabName(ZoneSystem.instance.m_zoneCtrlPrefab)).GetStableHashCode();

            if (s_terrainCompilerPrefab == 0)
                s_terrainCompilerPrefab = "_TerrainCompiler".GetStableHashCode();

            bool removeIceFloes = !IsTimeForIceFloes();
            bool removeCultivatedGround = IsTimeToDecultivateGround();

            if (!removeIceFloes && !removeCultivatedGround)
                return;

            int yearLength = seasonState.GetYearLengthInDays();
            int worldDay = seasonState.GetCurrentWorldDay();

            int floes = 0; int zones = 0; int terrains = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.ToArray())
            {
                if (removeIceFloes)
                {
                    if (zdo.GetPrefab() == s_iceFloePrefab && zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                    {
                        RemoveObject(zdo, true);
                        floes++;
                    }

                    if (zdo.GetPrefab() == s_zoneCtrlPrefab && zdo.GetBool(SeasonsVars.s_iceFloesSpawned) && (!zdo.HasOwner() || zdo.IsOwner()))
                    {
                        zdo.Set(SeasonsVars.s_iceFloesSpawned, false);
                        zones++;
                    }
                }

                if (removeCultivatedGround)
                {
                    if (zdo.GetPrefab() == s_terrainCompilerPrefab && TerrainDecultivation.IsDecultivationDue(zdo, worldDay, yearLength))
                    {
                        if (TerrainDecultivation.TryDecultivateGround(zdo, out bool changed))
                        {
                            zdo.Set(SeasonsVars.s_terrainDecultivated, worldDay);
                            if (changed)
                                terrains++;
                        }
                    }
                }
            }

            LogFloeState($"Removed overworld floes:{floes}, Zones refreshed:{zones}");
            LogInfo($"Terrains decultivated:{terrains}");
        }

        public bool CheckWaterVolumeForIceFloes(WaterVolume waterVolume)
        {
            if (waterVolume == null || waterVolume.m_heightmap == null || s_iceFloe?.m_prefab == null)
                return true;

            Vector3 position = waterVolume.transform.position;
            if (WorldGenerator.instance.GetBiome(position) != Biome.Ocean)
                return true;

            Vector2s zoneID = ZoneSystem.GetZone(position);

            if (!ZoneSystem.instance.IsZoneLoaded(zoneID))
                return false;

            Vector3 zonePos = ZoneSystem.GetZonePos(zoneID);
            SpawnSystem spawnSystem = SpawnSystem.m_instances.FirstOrDefault(ss => ss != null && ss.m_heightmap == waterVolume.m_heightmap);
            if (spawnSystem == null || spawnSystem.m_nview == null || !spawnSystem.m_nview.IsValid() || !spawnSystem.m_nview.IsOwner())
                return false;

            ZDO zoneZDO = spawnSystem.m_nview.GetZDO();
            if (!IsTimeForIceFloes())
                zoneZDO.Set(SeasonsVars.s_iceFloesSpawned, false);

            m_tempZDOList.Clear();
            s_visitedSectorIndices.Clear();
            ZDOMan.instance.FindObjects(zoneID, m_tempZDOList, s_visitedSectorIndices);
            m_tempZDOList.RemoveAll(zdo => zdo.GetPrefab() != s_iceFloePrefab || !zdo.GetBool(SeasonsVars.s_iceFloeWatermark) || ZoneSystem.GetZone(zdo.GetPosition()) != zoneID);
            
            if (IsTimeForIceFloes() && m_tempZDOList.Count > 0)
                return true;
            else if (!IsTimeForIceFloes() && m_tempZDOList.Count == 0)
                return true;
            else if (!IsTimeForIceFloes() && m_tempZDOList.Count > 0)
            {
                LogFloeState($"Checking occasional floes: {m_tempZDOList.Count}");
                foreach (ZDO zdo in m_tempZDOList)
                    if (zdo.GetBool(SeasonsVars.s_iceFloeWatermark))
                        RemoveObject(zdo);
            }
            else if (IsTimeForIceFloes() && m_tempZDOList.Count == 0)
            {
                if (zoneZDO.GetBool(SeasonsVars.s_iceFloesSpawned))
                    return true;

                ZoneSystem.SpawnMode mode = ZNetScene.instance.IsAreaReady(position) ? ZoneSystem.SpawnMode.Full : ZoneSystem.SpawnMode.Ghost;

                m_tempSpawnedObjects.Clear();

                PlaceIceFloes(zoneID, zonePos, m_tempClearAreas, mode, m_tempSpawnedObjects);
                zoneZDO.Set(SeasonsVars.s_iceFloesSpawned, true);
                LogFloeState($"{zoneID} {zonePos} Spawned {mode} floes:{m_tempSpawnedObjects.Count}");
                
                if (mode == ZoneSystem.SpawnMode.Ghost)
                    foreach (GameObject tempSpawnedObject in m_tempSpawnedObjects)
                        Destroy(tempSpawnedObject);

                m_tempSpawnedObjects.Clear();
            }

            return true;
        }

        public static void PlaceIceFloes(Vector2s zoneID, Vector3 zoneCenterPos, List<ZoneSystem.ClearArea> clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            int seed = WorldGenerator.instance.GetSeed();
            float num = ZoneSystem.instance.m_zoneSize / 2f;

            UnityEngine.Random.InitState(seed + zoneID.x * 4271 + zoneID.y * 9187 + s_iceFloePrefab + (SeasonState.IsActive ? seasonState.GetCurrentWorldDay() : 0));
            try
            {
                int spawnCount = UnityEngine.Random.Range((int)amountOfIceFloesInWinterDays.Value.x, (int)amountOfIceFloesInWinterDays.Value.y + 1);
                for (int i = 0; i < spawnCount; i++)
                {
                    Vector3 p = new Vector3(UnityEngine.Random.Range(zoneCenterPos.x - num, zoneCenterPos.x + num), 0f, UnityEngine.Random.Range(zoneCenterPos.z - num, zoneCenterPos.z + num));
                    if (IsBeyondWorldEdge(p, 100f))
                        continue;

                    if (ZoneSystem.instance.InsideClearArea(clearAreas, p))
                        continue;

                    if (s_iceFloe.m_blockCheck && ZoneSystem.instance.IsBlocked(p))
                        continue;

                    ZoneSystem.instance.GetGroundData(ref p, out _, out var biome, out var biomeArea, out var hmap2);
                    float num11 = p.y - ZoneSystem.instance.m_waterLevel;
                    if (num11 < s_iceFloe.m_minAltitude || num11 > s_iceFloe.m_maxAltitude)
                        continue;

                    if ((s_iceFloe.m_biome & biome) == 0 || (s_iceFloe.m_biomeArea & biomeArea) == 0)
                        continue;

                    float oceanDepth = hmap2.GetOceanDepth(p);
                    if (s_iceFloe.m_minOceanDepth != s_iceFloe.m_maxOceanDepth)
                    {
                        if (oceanDepth < s_iceFloe.m_minOceanDepth || oceanDepth > s_iceFloe.m_maxOceanDepth)
                            continue;
                    }

                    float oceanDepthFactor = GetOceanDepthFactor(oceanDepth);

                    float scaleX = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y) * oceanDepthFactor;
                    float scaleY = PowSquash(UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y), 0.6f); // Squash a bit to prevent extra thick or thin
                    float scaleZ = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y) * oceanDepthFactor;

                    float halfX = s_floeSize.x * scaleX / 2;
                    float halfZ = s_floeSize.y * scaleZ / 2;
                    float radius = Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + 0.2f;

                    if (clearAreas.Any(area => IsInside(area, p, radius)))
                        continue;

                    if (s_iceFloe.m_snapToWater)
                        p.y = ZoneSystem.instance.m_waterLevel - _winterWaterSurfaceOffset;

                    if (mode == ZoneSystem.SpawnMode.Ghost)
                        ZNetView.StartGhostInit();

                    GameObject gameObject;
                    try
                    {
                        gameObject = Instantiate(s_iceFloe.m_prefab, p, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));
                    }
                    finally
                    {
                        if (mode == ZoneSystem.SpawnMode.Ghost)
                            ZNetView.FinishGhostInit();
                    }

                    ZNetView netView = gameObject.GetComponent<ZNetView>();

                    netView.SetLocalScale(new Vector3(scaleX, scaleY, scaleZ));

                    float health = iceFloesHealth.Value * scaleX * scaleY * scaleZ;

                    ZDO zdo = netView.GetZDO();
                    zdo.Set(SeasonsVars.s_iceFloeWatermark, true);
                    zdo.Set(SeasonsVars.s_iceFloeMass, netView.m_body.mass * PowSquash(Mathf.Sqrt(Mathf.Abs(scaleX * scaleY * scaleZ)), 0.6f));
                    zdo.Set(ZDOVars.s_health, health + Game.m_worldLevel * health * Game.instance.m_worldLevelMineHPMultiplier);

                    if (mode == ZoneSystem.SpawnMode.Ghost)
                        spawnedObjects.Add(gameObject);

                    clearAreas.Add(new ZoneSystem.ClearArea(p, GetFloeSize(gameObject) + 0.5f));
                }
            }
            finally
            {
                UnityEngine.Random.state = state;
            }
        }

        public static float PowSquash(float x, float gamma = 0.5f) => Mathf.Pow(Mathf.Max(0f, x), gamma);

        public static float GetOceanDepthFactor(float value)
        {
            const float minDepth = 22f;
            const float maxDepth = 30f;
            const float minFactor = 0.8f;
            const float maxFactor = 1.3f;

            if (value <= minDepth)
                return minFactor;

            if (value >= maxDepth)
                return maxFactor;

            float t = (value - minDepth) / (maxDepth - minDepth);
            return minFactor + t * (maxFactor - minFactor);
        }

        public static bool IsInside(ZoneSystem.ClearArea area, Vector3 point, float radius) => Utils.DistanceXZ(area.m_center, point) < area.m_radius + radius;

        public static float GetFloeSize(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponentInChildren<Collider>();
            if (collider)
            {
                bool enabled = collider.enabled;
                try
                {
                    collider.enabled = false;
                    collider.enabled = true;
                    return Mathf.Sqrt(collider.bounds.size.x * collider.bounds.size.x / 4 + collider.bounds.size.z * collider.bounds.size.z / 4);
                }
                finally
                {
                    collider.enabled = enabled;
                }
            }

            Renderer renderer = gameObject.GetComponentInChildren<Renderer>();
            if (renderer)
            {
                bool enabled = renderer.enabled;
                try
                {
                    renderer.enabled = false;
                    renderer.enabled = true;
                    return Mathf.Sqrt(renderer.bounds.size.x * renderer.bounds.size.x / 4 + renderer.bounds.size.z * renderer.bounds.size.z / 4);
                }
                finally
                {
                    renderer.enabled = enabled;
                }
            }

            return 5.8f;
        }

        private static void RemoveObject(ZDO zdo, bool force = false)
        {
            if (zdo == null || !zdo.IsValid())
                return;

            if (!zdo.IsOwner())
            {
                if (!force && !ZNet.instance.IsServer())
                    return;

                zdo.SetOwner(ZDOMan.GetSessionID());
            }

            if (ZNetScene.instance.m_instances.TryGetValue(zdo, out ZNetView netView))
                ZNetScene.instance.Destroy(netView.gameObject);
            else
                ZDOMan.instance.DestroyZDO(zdo);
        }

        private static void LogFloeState(object log)
        {
            if (!logFloes.Value)
                return;

            LogInfo(log);
        }
    }

    public static class CharacterExtentions_FrozenOceanSliding
    {
        private struct SlideStatus
        {
            public Vector3 m_iceSlipVelocity;
            public float m_slip;
        }

        private static readonly Dictionary<Character, SlideStatus> charactersSlides = new Dictionary<Character, SlideStatus>();

        public static bool IsOnIce(this Character character)
        {
            if (!IsWaterSurfaceFrozen())
                return false;

            if (!character.IsOnGround())
                return false;

            Collider lastGroundCollider = character.GetLastGroundCollider();
            if (lastGroundCollider == null)
                return false;

            return lastGroundCollider.name == _iceSurfaceName;
        }

        public static void StartIceSliding(this Character character, Vector3 currentVel, bool checkMagnitude = false, bool checkRunning = true)
        {
            if (frozenOceanSlipperiness.Value <= 0)
                return;

            if (checkRunning && character.IsRunning())
                return;

            SlideStatus slideStatus = charactersSlides.GetValueSafe(character);
            slideStatus.m_slip = 1f;

            if (checkMagnitude && slideStatus.m_iceSlipVelocity.magnitude > currentVel.magnitude)
                return;

            slideStatus.m_iceSlipVelocity = Vector3.ClampMagnitude(currentVel, 10f);

            charactersSlides[character] = slideStatus;
        }

        public static void UpdateIceSliding(this Character character, ref Vector3 currentVel)
        {
            SlideStatus slideStatus = charactersSlides[character];
            if (slideStatus.m_slip > 0f && (character.IsOnIce() || !character.IsOnGround()))
            {
                currentVel = Vector3.Lerp(currentVel, slideStatus.m_iceSlipVelocity, slideStatus.m_slip);
                float delta = character.IsOnGround() ? Time.fixedDeltaTime / 2 / Mathf.Abs(frozenOceanSlipperiness.Value) : Time.fixedDeltaTime;
                slideStatus.m_slip = Mathf.MoveTowards(slideStatus.m_slip, 0f, delta);
                charactersSlides[character] = slideStatus;
            }
            else
            {
                character.StopIceSliding();
            }
        }

        public static void StopIceSliding(this Character character)
        {
            charactersSlides.Remove(character);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.OnDestroy))]
        public static class Character_OnDestroy_WaterVariantControllerInit
        {
            private static void Prefix(Character __instance)
            {
                __instance.StopIceSliding();
                Character_UpdateGroundContact_FrozenOceanSlippery.RemoveCharacter(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        public static class Player_SetControls_FrozenOceanSlippery
        {
            private static void Prefix(Player __instance, bool run)
            {
                if (frozenOceanSlipperiness.Value == 0 || __instance != Player.m_localPlayer || !__instance.IsOnIce())
                    return;

                if (!run && __instance.m_run)
                    __instance.StartIceSliding(__instance.m_currentVel, checkRunning: false);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.SetRun))]
        public static class Character_SetRun_FrozenOceanSlippery
        {
            private static void Prefix(Character __instance, bool run, ZNetView ___m_nview)
            {
                if (frozenOceanSlipperiness.Value == 0 || !__instance.IsOnIce() || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                if (!run && __instance.m_run)
                    __instance.StartIceSliding(__instance.m_currentVel, checkRunning: false);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundContact))]
        public static class Character_UpdateGroundContact_FrozenOceanSlippery
        {
            private struct SlideState
            {
                public bool HasValue;
                public float AirAltitude;
                public Vector3 BodyVelocity;
            }

            private static readonly Dictionary<Character, Vector3> m_characterSlideVelocity = new Dictionary<Character, Vector3>(64);

            public static void RemoveCharacter(Character character) => m_characterSlideVelocity.Remove(character);

            public static void CheckForSlide(Character characterSyncVelocity)
            {
                if (characterSyncVelocity == null || m_characterSlideVelocity.Count == 0)
                    return;

                if (m_characterSlideVelocity.TryGetValue(characterSyncVelocity, out Vector3 bodyVelocity))
                {
                    m_characterSlideVelocity.Remove(characterSyncVelocity);
                    characterSyncVelocity.StartIceSliding(bodyVelocity, checkMagnitude: true);
                }
            }

            private static void Prefix(
                Character __instance,
                ZNetView ___m_nview,
                float ___m_maxAirAltitude,
                ref SlideState __state)
            {
                __state = default;

                if (frozenOceanSlipperiness.Value == 0f || ___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                Vector3 position = __instance.transform.position;

                __state.HasValue = true;
                __state.AirAltitude = Mathf.Max(0f, ___m_maxAirAltitude - position.y);
                __state.BodyVelocity = __instance.m_body.linearVelocity;
            }

            private static void Postfix(
                Character __instance,
                float ___m_maxAirAltitude,
                SlideState __state)
            {
                if (!__state.HasValue)
                    return;

                if (__state.AirAltitude <= 1f || frozenOceanSlipperiness.Value <= 0f)
                    return;

                float positionY = __instance.transform.position.y;

                if (___m_maxAirAltitude == positionY && __instance.IsOnIce())
                    m_characterSlideVelocity[__instance] = __state.BodyVelocity;
            }

            [HarmonyPatch(typeof(Character), nameof(Character.SyncVelocity))]
            public static class Character_SyncVelocity_FrozenOceanSlippery
            {
                private static void Prefix(Character __instance)
                {
                    CheckForSlide(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateDodge))]
        public static class Player_UpdateDodge_FrozenOceanSlippery
        {
            private static bool m_initiateSlide;
            private static Vector3 m_bodyVelocity = Vector3.zero;
            private static Player m_slidePlayer;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, bool ___m_inDodge, ref bool __state)
            {
                if (m_slidePlayer != __instance)
                {
                    m_slidePlayer = __instance;
                    m_initiateSlide = false;
                    m_bodyVelocity = Vector3.zero;
                }
                if (m_initiateSlide && !___m_inDodge && __instance.IsOnIce())
                    __instance.StartIceSliding(m_bodyVelocity);

                __state = frozenOceanSlipperiness.Value > 0 && ___m_inDodge && __instance == Player.m_localPlayer;
            }

            private static void Postfix(Player __instance, bool ___m_inDodge, ref bool __state)
            {
                m_initiateSlide = frozenOceanSlipperiness.Value > 0 && ___m_inDodge && __instance == Player.m_localPlayer;
                m_bodyVelocity = __instance.m_queuedDodgeDir * __instance.m_body.linearVelocity.magnitude;

                if (__state && !___m_inDodge && __instance.IsOnIce())
                    __instance.StartIceSliding(m_bodyVelocity);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyGroundForce))]
        public static class Character_ApplyGroundForce_FrozenOceanSlippery
        {
            private static void Postfix(Character __instance, ZNetView ___m_nview, ref Vector3 vel)
            {
                if (!charactersSlides.ContainsKey(__instance))
                    return;

                if (frozenOceanSlipperiness.Value == 0 || !___m_nview.IsValid() || !___m_nview.IsOwner())
                {
                    __instance.StopIceSliding();
                    return;
                }

                __instance.UpdateIceSliding(ref vel);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.UpdateBodyFriction))]
        public static class Character_UpdateBodyFriction_FrozenOceanSurface
        {
            private static void Postfix(Character __instance, CapsuleCollider ___m_collider)
            {
                if (!__instance.IsOnIce())
                    return;

                PhysicsMaterial material = ___m_collider.material;
                if (material.staticFriction != 0.1f)
                    material.staticFriction = 0.1f;
                if (material.dynamicFriction != 0.1f)
                    material.dynamicFriction = 0.1f;
                if (material.frictionCombine != PhysicsMaterialCombine.Minimum)
                    material.frictionCombine = PhysicsMaterialCombine.Minimum;
            }
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor), new[] { typeof(Biome) })]
    public static class Heightmap_GetBiomeColor_TerrainColor
    {
        public static bool overrideColor = false;

        public static bool overrideSeason = false;
        public static Season seasonOverride;

        private static Color GetColorWithoutOverride(Heightmap heightmap, float ix, float iy)
        {
            bool wasOverridden = overrideColor;
            overrideColor = false;
            try
            {
                return heightmap.GetBiomeColor(ix, iy);
            }
            finally
            {
                overrideColor = wasOverridden;
            }
        }

        private static Color GetColorWithoutOverride(Biome biome)
        {
            bool wasOverridden = overrideColor;
            overrideColor = false;
            try
            {
                return Heightmap.GetBiomeColor(biome);
            }
            finally
            {
                overrideColor = wasOverridden;
            }
        }

        private static Color GetColorWithSeasonOverride(Season season, Heightmap heightmap, float ix, float iy)
        {
            bool wasOverriddenColor = overrideColor;
            bool wasOverriddenSeason = overrideSeason;
            Season previousSeason = seasonOverride;

            overrideColor = true;
            overrideSeason = true;
            seasonOverride = season;
            try
            {
                return heightmap.GetBiomeColor(ix, iy);
            }
            finally
            {
                overrideSeason = wasOverriddenSeason;
                seasonOverride = previousSeason;
                overrideColor = wasOverriddenColor;
            }
        }

        private static Color GetColorWithSeasonOverride(Season season, Biome biome)
        {
            bool wasOverriddenColor = overrideColor;
            bool wasOverriddenSeason = overrideSeason;
            Season previousSeason = seasonOverride;

            overrideColor = true;
            overrideSeason = true;
            seasonOverride = season;
            try
            {
                return Heightmap.GetBiomeColor(biome);
            }
            finally
            {
                overrideSeason = wasOverriddenSeason;
                seasonOverride = previousSeason;
                overrideColor = wasOverriddenColor;
            }
        }

        public static Color GetOriginalColor(Heightmap heightmap, float ix, float iy) => GetColorWithoutOverride(heightmap, ix, iy);

        public static Color GetOriginalColor(Biome biome) => GetColorWithoutOverride(biome);

        public static Color GetSeasonalColor(Season season, Biome biome) => GetColorWithSeasonOverride(season, biome);

        public static Color GetSeasonalColor(Season season, Heightmap heightmap, float ix, float iy) => GetColorWithSeasonOverride(season, heightmap, ix, iy);

        public static bool HasBiomeOverride(Biome biome, Season season, out Biome overridedBiome)
        {
            overridedBiome = Biome.None;
            return SeasonState.seasonBiomeSettings.SeasonalBiomeColorOverride.TryGetValue(biome, out Dictionary<Season, Biome> overrideBiome)
                    && overrideBiome.TryGetValue(season, out overridedBiome);
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref Biome biome, ref Biome __state)
        {
            __state = Biome.None;

            if (!overrideColor || !SeasonState.IsActive || !UseTextureControllers())
                return;

            if (HasBiomeOverride(biome, overrideSeason ? seasonOverride : seasonState.GetCurrentSeason(), out Biome overridedBiome))
            {
                __state = biome;
                biome = overridedBiome;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref Biome biome, Biome __state)
        {
            if (__state == Biome.None || !overrideColor || !SeasonState.IsActive || !UseTextureControllers())
                return;

            biome = __state;
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor), new[] { typeof(float), typeof(float) })]
    public static class Heightmap_GetBiomeColor_BiomesEdgeFix
    {
        [HarmonyPriority(Priority.First)]
        private static void Postfix(Heightmap __instance, float ix, float iy, ref Color __result)
        {
            if (!Heightmap_GetBiomeColor_TerrainColor.overrideColor || !SeasonState.IsActive || !UseTextureControllers())
                return;

            // Swamp-Plains and Swamp-Mistlands borders -> Blackforest
            if (plainsSwampBorderFix.Value && __instance.IsBiomeEdge() && 0f < __result.r && __result.r < 1f && 0f < __result.a && __result.a < 1f)
                __result = new Color(0, __result.g, __result.r, __result.a);

            // Fixed-season samples must not recursively apply the transition blend.
            if (Heightmap_GetBiomeColor_TerrainColor.overrideSeason)
                return;

            if (seasonState.GetCurrentDay() == seasonState.GetDaysInSeason() && lastDayTerrainFactor.Value != 0f)
                __result = Color.Lerp(__result, Heightmap_GetBiomeColor_TerrainColor.GetSeasonalColor(seasonState.GetNextSeason(), __instance, ix, iy), lastDayTerrainFactor.Value);
            else if (seasonState.GetCurrentDay() == 1 && firstDayTerrainFactor.Value != 0f)
                __result = Color.Lerp(__result, Heightmap_GetBiomeColor_TerrainColor.GetSeasonalColor(seasonState.GetPreviousSeason(), __instance, ix, iy), firstDayTerrainFactor.Value);
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.RebuildRenderMesh))]
    public static class Heightmap_RebuildRenderMesh_TerrainColor
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref bool __state)
        {
            __state = Heightmap_GetBiomeColor_TerrainColor.overrideColor;
            Heightmap_GetBiomeColor_TerrainColor.overrideColor = SeasonState.IsActive && UseTextureControllers();
        }

        private static void Finalizer(bool __state) => Heightmap_GetBiomeColor_TerrainColor.overrideColor = __state;
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.Awake))]
    public static class WaterVolume_Awake_WaterState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            if (!UseTextureControllers() || !SeasonState.IsActive || !__instance.m_useGlobalWind || Instance == null)
                return;

            if (waterStates.ContainsKey(__instance))
                return;
            
            waterStates.Add(__instance, new WaterState(__instance));
            Instance.waterVolumesCheckFloes.Add(__instance);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnEnable))]
    public static class WaterVolume_OnEnable_WaterState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!waterStates.ContainsKey(__instance))
                return;

            UpdateWater(__instance, waterStates[__instance]);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnDisable))]
    public static class WaterVolume_OnDisable_WaterState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!waterStates.ContainsKey(__instance))
                return;

            UpdateWater(__instance, waterStates[__instance], revertState: true);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.OnDestroy))]
    public static class WaterVolume_OnDestroy_WaterState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            waterStates.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateWaterTime))]
    public static class WaterVolume_UpdateWaterTime_WaterVariantControllerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            WaterVolume.s_waterTime = 0f;
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateMaterials))]
    public static class WaterVolume_UpdateMaterials_RestoreReleasedProperties
    {
        private static void Postfix(WaterVolume __instance)
        {
            if (waterStates.TryGetValue(__instance, out WaterState state))
                state.RestoreProperties(__instance.m_waterSurface, inactiveOnly: true);
        }
    }

    [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.FindAverageOceanPoint))]
    public static class AudioMan_FindAverageOceanPoint_DisableOceanSounds
    {
        private static bool Prefix()
        {
            return !IsWaterSurfaceFrozen();
        }
    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.GetGroundMaterial))]
    public static class FootStep_GetGroundMaterial_FrozenOceanFootstep
    {
        private static bool Prefix(Character character, ref FootStep.GroundMaterial __result)
        {
            if (character == null || character != Player.m_localPlayer)
                return true;

            if (!character.IsOnIce())
                return true;

            __result = Player.m_localPlayer.GetCurrentBiome() == Biome.Ocean ? FootStep.GroundMaterial.Default : FootStep.GroundMaterial.Snow;
            return false;
        }
    }

    [HarmonyPatch(typeof(MusicMan), nameof(MusicMan.GetEnvironmentMusic))]
    public static class MusicMan_GetEnvironmentMusic_FrozenOceanNightMusic
    {
        const string frozenOceanMusic = "frozen ocean";
        private static MusicMan musicOwner;
        private static MusicMan.NamedMusic registeredMusic;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MusicMan.NamedMusic, object> releasedMusic = new System.Runtime.CompilerServices.ConditionalWeakTable<MusicMan.NamedMusic, object>();

        internal static bool WasReleased(MusicMan.NamedMusic music) => music != null && releasedMusic.TryGetValue(music, out _);

        private static void Postfix(MusicMan __instance, ref MusicMan.NamedMusic __result)
        {
            if (musicOwner != __instance)
            {
                musicOwner = __instance;
                registeredMusic = null;
            }

            int musicHash = frozenOceanMusic.GetStableHashCode();
            if (!enableNightMusicOnFrozenOcean.Value || !LocalPlayerIsOnFrozenOcean() || !EnvMan.IsNight())
            {
                if (registeredMusic != null)
                {
                    releasedMusic.GetValue(registeredMusic, _ => new object());
                    __instance.m_music.Remove(registeredMusic);
                    if (__instance.m_musicHashes.TryGetValue(musicHash, out MusicMan.NamedMusic indexedMusic) && ReferenceEquals(indexedMusic, registeredMusic))
                        __instance.m_musicHashes.Remove(musicHash);
                    registeredMusic = null;
                }
                return;
            }

            if (__result != null && __result.m_name == "home")
                return;

            MusicMan.NamedMusic frozenOcean = __instance.FindMusic(frozenOceanMusic);
            if (frozenOcean == null)
            {
                frozenOcean = __instance.m_music.Find(music => music.m_name == frozenOceanMusic && music.m_enabled && music.m_clips != null && music.m_clips.Length > 0 && music.m_clips[0] != null);
                if (frozenOcean != null)
                    __instance.m_musicHashes[musicHash] = frozenOcean;
            }
            if (frozenOcean == null)
            {
                MusicMan.NamedMusic intro = __instance.FindMusic("intro");
                if (intro != null)
                {
                    frozenOcean = JsonUtility.FromJson<MusicMan.NamedMusic>(JsonUtility.ToJson(intro));
                    frozenOcean.m_name = frozenOceanMusic;
                    frozenOcean.m_ambientMusic = true;
                    frozenOcean.m_loop = Settings.ContinousMusic;
                    frozenOcean.m_volume = 0.2f;
                    frozenOcean.m_fadeInTime = 10f;

                    __instance.m_music.Add(frozenOcean);
                    __instance.m_musicHashes[musicHash] = frozenOcean;
                    registeredMusic = frozenOcean;
                }
            }

            if (frozenOcean != null)
                __result = frozenOcean;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    public static class EnvMan_SetEnv_FrozenOceanWindLoop
    {
        private struct AmbientLoopState
        {
            public bool Changed;
            public AudioClip Clip;
        }

        public static Dictionary<string, AudioClip> UsedAudioClips
        {
            get
            {
                _usedAudioClips.Clear();
                if (EnvMan.instance == null)
                    return _usedAudioClips;

                foreach (EnvSetup env in EnvMan.instance.m_environments)
                    if (env.m_ambientLoop != null && !_usedAudioClips.ContainsKey(env.m_ambientLoop.name))
                        _usedAudioClips.Add(env.m_ambientLoop.name, env.m_ambientLoop);

                return _usedAudioClips;
            }
        }

        private static readonly Dictionary<string, AudioClip> _usedAudioClips = new Dictionary<string, AudioClip>();

        private static void Prefix(EnvSetup env, ref AmbientLoopState __state)
        {
            __state = default;
            if (!LocalPlayerIsOnFrozenOcean() || env == null)
                return;

            Dictionary<string, AudioClip> audioClips = UsedAudioClips;
            if (!audioClips.TryGetValue("Wind_ColdLoop3", out AudioClip coldLoop))
                return;
            if (audioClips.TryGetValue("Wind_BlowingLoop3", out AudioClip blowingLoop) && env.m_ambientLoop == blowingLoop)
                return;

            __state.Changed = true;
            __state.Clip = env.m_ambientLoop;
            env.m_ambientLoop = coldLoop;
        }

        private static void Finalizer(EnvSetup env, AmbientLoopState __state)
        {
            if (__state.Changed)
                env.m_ambientLoop = __state.Clip;
        }
    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.FixedUpdate))]
    public static class Leviathan_FixedUpdate_FrozenOceanLeviathan
    {
        private static bool Prefix(Leviathan __instance, Rigidbody ___m_body, ZNetView ___m_nview)
        {
            if (IsIgnoredPosition(__instance.transform.position) || !IsWaterSurfaceFrozen())
                return true;

            if (___m_nview.IsValid() && ___m_nview.IsOwner())
            {
                Vector3 position2 = ___m_body.position;
                position2.y = Floating.GetLiquidLevel(___m_body.position, 0) - 5f;
                ___m_body.position = position2;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.OnHit))]
    public static class Leviathan_OnHit_FrozenOceanLeviathan
    {
        private static bool Prefix(Leviathan __instance)
        {
            return IsIgnoredPosition(__instance.transform.position) || !IsWaterSurfaceFrozen();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    public static class Player_TeleportTo_FrozenOceanMinimapTeleportation
    {
        private static void Postfix(bool __result, ref Vector3 ___m_teleportTargetPos)
        {
            if (!__result)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            if (___m_teleportTargetPos.y == 0)
                ___m_teleportTargetPos = new Vector3(___m_teleportTargetPos.x, ___m_teleportTargetPos.y + WaterLevel, ___m_teleportTargetPos.z);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), new Type[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float) })]
    public static class WaterVolume_CalcWave_FrozenOceanNoWaves
    {
        private static bool Prefix(ref float __result)
        {
            if (!IsWaterSurfaceFrozen())
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.Start))]
    public static class Ship_Start_FrozenOceanShip
    {
        private static void Postfix(Ship __instance)
        {
            __instance.StartCoroutine(CheckIfShipBelowSurface(__instance));
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    public static class Character_Awake_FrozenOceanCharacter
    {
        private static void Postfix(Character __instance)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            CheckIfCharacterBelowSurface(__instance);
        }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnPlayerDrop))]
    public static class ItemDrop_OnPlayerDrop_FrozenOceanFish
    {
        private static void Postfix(ItemDrop __instance)
        {
            MarkPlayerDroppedFish(__instance);
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.Start))]
    public static class Fish_Start_FrozenOcean
    {
        private static void Postfix(Fish __instance)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            Instance?.StartCoroutine(CheckSingleFishPosition(__instance));
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.SetVisible))]
    public static class Fish_SetVisible_CheckPositionIfBecameVisible
    {
        private static void Prefix(Fish __instance, ref bool __state) => __state = __instance.m_lodVisible;

        private static void Postfix(Fish __instance, bool __state)
        {
            if (!IsWaterSurfaceFrozen() || __instance.m_lodGroup == null || __state == __instance.m_lodVisible || !__instance.m_lodVisible)
                return;

            Instance?.StartCoroutine(CheckSingleFishPosition(__instance));
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.ConsiderJump))]
    public static class Fish_ConsiderJump_FrozenOceanFishNoJumps
    {
        private static void Prefix(ref float ___m_JumpHeightStrength, ref float? __state)
        {
            __state = null;
            if (!IsWaterSurfaceFrozen())
                return;

            __state = ___m_JumpHeightStrength;
            ___m_JumpHeightStrength = 0f;
        }

        private static void Finalizer(ref float ___m_JumpHeightStrength, float? __state)
        {
            if (__state.HasValue)
                ___m_JumpHeightStrength = __state.Value;
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.CustomFixedUpdate))]
    public static class Fish_CustomFixedUpdate_CheckPosition
    {
        private static void Postfix(Fish __instance)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            if (__instance.m_lodVisible)
                return;

            if (ShouldThrottleFishIceCheck(__instance))
                return;

            CheckIfFishAboveSurface(__instance);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), new Type[] { typeof(Vector3), typeof(float), typeof(Vector4), typeof(float), typeof(float), typeof(float) })]
    public static class WaterVolume_CalcWave_FrozenOceanPreventWaves
    {
        private static bool Prefix(ref float __result)
        {
            if (!IsWaterSurfaceFrozen())
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.IsBlocked))]
    public static class ZoneSystem_IsBlocked_VegetationPlacing
    {
        private static bool Prefix(Vector3 p, int ___m_blockRayMask, ref bool __result)
        {
            if (!UseTextureControllers())
                return true;

            if (!SeasonState.IsActive)
                return true;

            if (!IsWaterSurfaceFrozen())
                return true;

            Vector3 origin = p;
            origin.y += 2000f;
            int num = Physics.RaycastNonAlloc(origin, Vector3.down, Instance.rayHits, 10000f, ___m_blockRayMask);
            __result = false;
            for (int i = 0; i < num; i++)
            {
                if (Instance.rayHits[i].collider != null && Instance.rayHits[i].collider.name == _iceSurfaceName)
                    continue;
                
                __result = true;
                break;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Floating), nameof(Floating.CustomFixedUpdate))]
    public static class Floating_CustomFixedUpdate_IceFloeRotation
    {
        private class FloeSyncState : MonoBehaviour
        {
            public ZSyncTransform Sync;
            public bool SyncPosition;
            public bool SyncVelocity;
            public bool AppliedPosition;
            public bool AppliedVelocity;
            public bool Applied;

            public void Restore()
            {
                if (!Applied || Sync == null)
                    return;
                if (Sync.m_syncPosition == AppliedPosition)
                    Sync.m_syncPosition = SyncPosition;
                if (Sync.m_syncBodyVelocity == AppliedVelocity)
                    Sync.m_syncBodyVelocity = SyncVelocity;
                Applied = false;
            }
        }

        private static readonly Vector3[] positions = new Vector3[4];
        private static void AddWaveForce(Floating floating, float fixedDeltaTime)
        {
            floating.m_body.WakeUp();

            foreach (Vector3 position in positions)
            {
                float depthDelta = position.y - Floating.GetLiquidLevel(position);
                float forceAmount = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4f)) * (fixedDeltaTime * 50f) * Mathf.Abs(depthDelta);
                Vector3 force = depthDelta < 0f ? Vector3.up * (forceAmount * 0.6f) : Vector3.down * forceAmount;
                floating.m_body.AddForceAtPosition(force * 0.02f * floating.m_body.mass * 0.25f, position, ForceMode.Impulse);
            }
        }

        private static float Dampen(float value) => value / (1f + Mathf.Abs(value));

        private static bool Prefix(Floating __instance, float fixedDeltaTime)
        {
            FloeSyncState state = __instance.GetComponent<FloeSyncState>();
            bool wasSyncPosition = state == null || state.Sync == null || state.Sync.m_syncPosition;
            state?.Restore();
            if (!GameCamera.instance || __instance.m_nview is not ZNetView nview || !nview.IsValid() || nview.GetZDO()?.GetPrefab() != s_iceFloePrefab || !__instance.m_body)
                return true;

            if (!nview.GetZDO().GetBool(SeasonsVars.s_iceFloeWatermark) || !nview.IsOwner())
                return true;

            ZSyncTransform syncTransform = __instance.GetComponent<ZSyncTransform>();
            if (syncTransform == null || __instance.m_collider == null)
                return true;
            state ??= __instance.gameObject.AddComponent<FloeSyncState>();
            state.Sync = syncTransform;
            state.SyncPosition = syncTransform.m_syncPosition;
            state.SyncVelocity = syncTransform.m_syncBodyVelocity;
            state.Applied = true;
            bool inActiveWaterDistance = Utils.DistanceXZ(GameCamera.instance.transform.position, __instance.transform.position) < s_waterDistance;
            syncTransform.m_syncBodyVelocity = inActiveWaterDistance || !__instance.HaveLiquidLevel();

            syncTransform.m_syncPosition = syncTransform.m_syncBodyVelocity;
            state.AppliedPosition = syncTransform.m_syncPosition;
            state.AppliedVelocity = syncTransform.m_syncBodyVelocity;
            if (!wasSyncPosition && syncTransform.m_syncPosition)
                syncTransform.SyncNow();

            if (!syncTransform.m_syncPosition && __instance.HaveLiquidLevel() && !inActiveWaterDistance)
            {
                __instance.m_body.Sleep();
                __instance.transform.position = new Vector3(__instance.transform.position.x, WaterLevel + __instance.m_waterLevelOffset + Dampen(__instance.m_waterLevel - WaterLevel), __instance.transform.position.z);
                return false;
            }

            if (!nview.IsOwner() || !__instance.HaveLiquidLevel())
                return true;

            Vector3 wind = WaterVolume.s_globalWindAlpha == 0f
                ? WaterVolume.s_globalWind1
                : Vector4.Lerp(WaterVolume.s_globalWind1, WaterVolume.s_globalWind2, WaterVolume.s_globalWindAlpha);

            Vector3 windSide = Vector3.Cross(wind, __instance.transform.up);
            Vector3 center = __instance.m_body.worldCenterOfMass;

            positions[0] = __instance.m_collider.ClosestPoint(center + wind * 100f);
            positions[1] = __instance.m_collider.ClosestPoint(center - wind * 100f);
            positions[2] = __instance.m_collider.ClosestPoint(center + windSide * 100f);
            positions[3] = __instance.m_collider.ClosestPoint(center - windSide * 100f);

            AddWaveForce(__instance, fixedDeltaTime);

            return true;
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    public static class Ship_CustomFixedUpdate_FrozenShip
    {
        private static void Prefix(Ship __instance, ref float ___m_disableLevel, ref float? __state)
        {
            __state = null;
            CheckShipOwnership(__instance);
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            __state = ___m_disableLevel;

            ___m_disableLevel -= _winterWaterSurfaceOffset;
        }

        private static void Finalizer(ref float ___m_disableLevel, float? __state)
        {
            if (__state.HasValue)
                ___m_disableLevel = __state.Value;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.GetGroundHeight), new Type[] { typeof(Vector3) })]
    public static class ZoneSystem_GetGroundHeight_CheckForIceSurface
    {
        public static bool checkForIceSurface = false;
        public static int s_terrainRayMask = 0;

        private static bool Prefix(Vector3 p, ref float __result)
        {
            if (!checkForIceSurface)
                return true;

            checkForIceSurface = false;

            if (s_terrainRayMask == 0)
                s_terrainRayMask = LayerMask.GetMask("terrain", "Default");

            __result = p.y;

            Vector3 origin = p;
            origin.y = 6000f;
            int num = Physics.RaycastNonAlloc(origin, Vector3.down, Instance.rayHits, 10000f, s_terrainRayMask);

            float height = 0;
            for (int i = 0; i < num; i++)
            {
                RaycastHit raycastHit = Instance.rayHits[i];
                if (raycastHit.collider.gameObject.layer == 0 && raycastHit.collider.name == _iceSurfaceName)
                    height = Mathf.Max(raycastHit.point.y, height);
                else if (raycastHit.collider.gameObject.layer == 11)
                    height = Mathf.Max(raycastHit.point.y, height);
            }

            if (height > 0)
                __result = height;

            return false;
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.PositionCheck))]
    public static class TombStone_PositionCheck_FrozenSurfaceCheck
    {
        private static void Prefix(ref bool __state)
        {
            __state = ZoneSystem_GetGroundHeight_CheckForIceSurface.checkForIceSurface;
            if (UseTextureControllers() && SeasonState.IsActive && IsWaterSurfaceFrozen())
                ZoneSystem_GetGroundHeight_CheckForIceSurface.checkForIceSurface = true;
        }

        private static void Finalizer(bool __state) => ZoneSystem_GetGroundHeight_CheckForIceSurface.checkForIceSurface = __state;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateBiome))]
    public static class Player_UpdateBiome_OnBiomeChange
    {
        private static void Prefix(Player __instance, ref Biome __state)
        {
            __state = Biome.None;

            if (!UseTextureControllers())
                return;

            if (__instance != Player.m_localPlayer)
                return;

            if (!SeasonState.IsActive)
                return;

            __state = __instance.GetCurrentBiome();
        }

        private static void Postfix(Player __instance, Biome __state)
        {
            if (__instance == Player.m_localPlayer && SeasonState.IsActive && UseTextureControllers())
                seasonState.OnBiomeChange(__state, __instance.GetCurrentBiome());
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.RebuildRenderMesh))]
    public static class Heightmap_RebuildRenderMesh_UpdateProtectedHmap
    {
        private static void Postfix(Heightmap __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (IsProtectedHeightmap(__instance))
                UpdateTerrainColor(__instance);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateEnvironment))]
    public static class EnvMan_UpdateEnvironment_CheckSnowStormOnBiomeChange
    {
        private static void Postfix(EnvMan __instance)
        {
            if (__instance.m_currentBiome != null)
                Instance?.CheckBiomeChanged(__instance.m_currentBiome.Biome);
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.Awake))]
    public static class EnvMan_Awake_GetWorldEdge
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("expand_world_size")]
        private static void Postfix(EnvMan __instance)
        {
            var water = __instance.transform.Find("WaterPlane").Find("watersurface");
            Material waterMaterial = water.GetComponent<MeshRenderer>().sharedMaterial;
            s_waterDistance = waterMaterial.GetFloat("_VisibleMaxDistance");
            s_waterEdge = waterMaterial.GetFloat("_WaterEdge");
            s_waterEdgeLocalPlayerState = false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class Player_OnSpawned_CheckForEdgePosition
    {
        private static void Postfix(Player __instance)
        {
            if (Player.m_localPlayer != __instance)
                return;

            s_waterEdgeLocalPlayerState = IsBeyondWorldEdge(__instance.transform.position);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.EdgeOfWorldKill))]
    public static class Player_EdgeOfWorldKill_CheckForEdgePosition
    {
        private static void Postfix(Player __instance)
        {
            if (Player.m_localPlayer != __instance)
                return;

            if (__instance.IsDead())
                return;

            if (s_waterEdgeLocalPlayerState != (s_waterEdgeLocalPlayerState = IsBeyondWorldEdge(__instance.transform.position)))
                UpdateWaterState();
        }
    }
}
