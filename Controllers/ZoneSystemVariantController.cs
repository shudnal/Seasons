﻿using HarmonyLib;
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
                m_surfaceOffset = waterVolume.m_surfaceOffset;
                m_foamDepth = waterVolume.m_waterSurface.sharedMaterial.GetFloat("_FoamDepth");

                m_colorTop = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorTop");
                m_colorBottom = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorBottom");
                m_colorBottomShallow = waterVolume.m_waterSurface.sharedMaterial.GetColor("_ColorBottomShallow");
                
                InitFrozenColors();
            }

            public WaterState(MeshRenderer waterSurface)
            {
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
        }

        private static MeshRenderer s_waterPlane;
        private static WaterState s_waterPlaneState;
        public static float s_waterEdge;
        public static bool s_waterEdgeLocalPlayerState;
        public static float s_waterDistance;

        public static readonly Dictionary<WaterVolume, WaterState> waterStates = new Dictionary<WaterVolume, WaterState>();
        
        private static readonly MaterialPropertyBlock s_matBlock = new MaterialPropertyBlock();
        private static readonly List<ZDO> m_tempZDOList = new List<ZDO>();
        private static readonly List<Vector3> m_tempHits = new List<Vector3>();

        private static float s_freezeStatus = 0f;

        public static float s_colliderHeight = 0f;

        public const float _winterWaterSurfaceOffset = 2f;
        public const float _colliderOffset = 0.01f;
        public const string _iceSurfaceName = "IceSurface";

        public const string _iceFloeName = "ice1";
        public static int _iceFloePrefab = _iceFloeName.GetStableHashCode();
        public static Vector2 s_floeSize = new Vector2(8.36f, 8.0f) / 2;

        public static GameObject s_iceSurface;
        public static ZoneSystem.ZoneVegetation s_iceFloe;
        public static int s_iceFloeWatermark = "Seasons_IceFloe".GetStableHashCode();
        public static int s_iceFloeMass = "Seasons_IceFloeMass".GetStableHashCode();
        public static int s_iceFloesSpawned = "Seasons_IceFloesSpawned".GetStableHashCode();
        public static int s_zoneCtrlPrefab;

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

        public static float WaterLevel => s_colliderHeight == 0f || !IsWaterSurfaceFrozen() ? ZoneSystem.instance.m_waterLevel : s_colliderHeight;

        public static bool IsBeyondWorldEdge(Vector3 position, float offset = 0f) => Utils.DistanceXZ(Vector3.zero, position) > s_waterEdge - offset;

        private void Awake()
        {
            m_instance = this;
        }

        public void Update()
        {
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
            s_waterPlane = null;
            s_waterPlaneState = null;
            s_iceSurface = null;
            waterStates.Clear();
            m_instance = null;
            waterStateInitialized = false;
        }

        public void Initialize(ZoneSystem instance)
        {
            Transform waterPlane = EnvMan.instance.transform.Find("WaterPlane");
            if (waterPlane != null)
                s_waterPlane = waterPlane.GetComponentInChildren<MeshRenderer>();

            s_waterPlaneState = new WaterState(s_waterPlane);

            Transform water = instance.m_zonePrefab.transform.Find("Water");
            if (water != null)
                AddIceCollider(water);

            s_iceFloe ??= ZoneSystem.instance.m_vegetation.Find(veg => veg.m_prefab?.name == _iceFloeName).Clone();
            s_iceFloe.m_biome = Biome.Ocean;
            if (!s_iceFloe.m_prefab.TryGetComponent<IceFloeClimb>(out _))
                s_iceFloe.m_prefab.AddComponent<IceFloeClimb>();

            s_iceFloe.m_prefab.GetComponent<ZNetView>().m_syncInitialScale = true;
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
                if (m_snowStorm != null && m_snowStormMaxParticles != 0 && m_snowStormEmissionRate != 0f)
                {
                    ParticleSystem.MainModule main = m_snowStorm.main;
                    ParticleSystem.EmissionModule emission = m_snowStorm.emission;
                    emission.rateOverTimeMultiplier = m_snowStormEmissionRate;
                    main.maxParticles = m_snowStormMaxParticles;
                }
                return;
            }

            if (!BiomeChanged(biome))
                return;

            if (m_snowStorm == null)
            {
                Transform snowStormTransform = EnvMan.instance.transform.Find("FollowPlayer/SnowStorm") ?? Utils.FindChild(EnvMan.instance.transform, "SnowStorm");
                if (snowStormTransform == null)
                    return;

                Transform snowParticles = snowStormTransform.Find("snow (1)");
                if (snowParticles == null)
                    return;

                m_snowStorm = snowParticles.GetComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule snowStormMain = m_snowStorm.main;
            ParticleSystem.EmissionModule snowStormEmission = m_snowStorm.emission;

            if (m_snowStormMaxParticles == 0)
                m_snowStormMaxParticles = snowStormMain.maxParticles;

            if (m_snowStormEmissionRate == 0f)
                m_snowStormEmissionRate = snowStormEmission.rateOverTimeMultiplier;

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

            Heightmap_GetBiomeColor_TerrainColor.overrideColor = true;

            int num = heightmap.m_width + 1;
            Vector3 vector = heightmap.transform.position + new Vector3((float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5), 0f, (float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5));
            s_tempColors.Clear();

            bool hasShieldedPosition = false;
            for (int i = 0; i < num; i++)
                for (int j = 0; j < num; j++)
                    if (heightmap.m_isDistantLod)
                    {
                        float wx = vector.x + j * heightmap.m_scale;
                        float wy = vector.z + i * heightmap.m_scale;
                        Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
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


            Heightmap_GetBiomeColor_TerrainColor.overrideColor = false;

            if (hasShieldedPosition)
                SmoothenProtectedBorders(s_tempColors, heightmap.m_width + 1);

            heightmap.m_renderMesh.SetColors(s_tempColors);
            s_tempColors.Clear();
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
            yield return new WaitForSeconds(delay);

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

            Transform iceSurfaceTransform = water.Find(_iceSurfaceName);
            if (iceSurfaceTransform != null)
            {
                s_iceSurface = iceSurfaceTransform.gameObject;
                return;
            }

            Transform waterSurface = water.Find("WaterSurface");

            if (s_colliderHeight == 0f)
                s_colliderHeight = waterSurface.transform.position.y + _colliderOffset;

            s_iceSurface = new GameObject(_iceSurfaceName);
            s_iceSurface.transform.SetParent(water);
            s_iceSurface.layer = 0;
            s_iceSurface.transform.localScale = new Vector3(waterSurface.transform.localScale.x, Math.Abs(_colliderOffset), waterSurface.transform.localScale.z);
            s_iceSurface.transform.localPosition = new Vector3(0, _colliderOffset, 0);
            s_iceSurface.SetActive(false);

            MeshCollider iceCollider = s_iceSurface.gameObject.AddComponent<MeshCollider>();
            iceCollider.sharedMesh = waterSurface.GetComponent<MeshFilter>().sharedMesh;
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

            CheckToRemoveIceFloes();

            foreach (KeyValuePair<WaterVolume, WaterState> waterState in waterStates)
                UpdateWater(waterState.Key, waterState.Value);

            UpdateWaterSurface(s_waterPlane, s_waterPlaneState);

            Instance?.StartCoroutine(UpdateWaterObjects());
        }

        public static void UpdateWater(WaterVolume waterVolume, WaterState waterState, bool revertState = false)
        {
            SetupIceCollider(waterVolume, waterState, revertState);

            if (s_freezeStatus == 0f || revertState)
            {
                if (waterVolume.m_waterSurface != null && waterVolume.m_waterSurface.HasPropertyBlock())
                    waterVolume.m_waterSurface.SetPropertyBlock(null);

                waterVolume.m_surfaceOffset = waterState.m_surfaceOffset;
                waterVolume.m_useGlobalWind = true;
                waterVolume.SetupMaterial();

                return;
            }

            UpdateWaterSurface(waterVolume.m_waterSurface, waterState);

            waterVolume.m_surfaceOffset = waterState.m_surfaceOffset - (IsWaterSurfaceFrozen() ? _winterWaterSurfaceOffset : 0);
            waterVolume.m_useGlobalWind = !IsWaterSurfaceFrozen();
            waterVolume.SetupMaterial();
        }

        private static void UpdateWaterSurface(MeshRenderer waterSurface, WaterState waterState)
        {
            if (waterSurface == null || waterState == null)
                return;

            s_matBlock.Clear();
            s_matBlock.SetColor("_FoamColor", new Color(0.95f, 0.96f, 0.98f));
            s_matBlock.SetFloat("_FoamDepth", Mathf.Lerp(waterState.m_foamDepth, _FoamDepthFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorTop", Color.Lerp(waterState.m_colorTop, waterState.m_colorTopFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottom", Color.Lerp(waterState.m_colorBottom, waterState.m_colorBottomFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottomShallow", Color.Lerp(waterState.m_colorBottomShallow, waterState.m_colorBottomShallowFrozen, s_freezeStatus));

            if (IsWaterSurfaceFrozen())
            {
                s_matBlock.SetFloat(WaterVolume.s_shaderWaterTime, 0f);
                s_matBlock.SetFloat(WaterVolume.s_shaderUseGlobalWind, 0f);

                s_matBlock.SetFloat("_DepthFade", _DepthFade);
                s_matBlock.SetFloat("_Glossiness", _Glossiness);
                s_matBlock.SetFloat("_Metallic", _Metallic);
                s_matBlock.SetFloat("_ShoreFade", _ShoreFade);
                s_matBlock.SetFloat("_WaveVel", _WaveVel);
                s_matBlock.SetFloat("_WaveFoam", _WaveFoam);
            }

            waterSurface.SetPropertyBlock(s_matBlock);
        }

        private static void SetupIceCollider(WaterVolume waterVolume, WaterState waterState, bool revertState)
        {
            if (waterState.m_iceSurface == null)
                waterState.m_iceSurface = waterVolume.transform.parent.Find(_iceSurfaceName)?.gameObject;
            
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
            yield return waitForFixedUpdate;

            foreach (WaterVolume waterVolume in waterStates.Keys)
            {
                foreach (IWaterInteractable waterInteractable in waterVolume.m_inWater)
                    if (waterInteractable is Fish fish)
                        CheckIfFishAboveSurface(fish);
                    else if (waterInteractable is Character character)
                        CheckIfCharacterBelowSurface(character);
                    else if (waterInteractable is Floating floating)
                        CheckIfFloatingContainerBelowSurface(floating);


                Instance.waterVolumesCheckFloes.Add(waterVolume);
            }

            yield return waitForFixedUpdate;

            foreach (Ship ship in Ship.Instances.ToArray().Cast<Ship>())
                yield return CheckIfShipBelowSurface(ship);
        }

        public static bool IsUnderwaterAI(Character character, out BaseAI ai)
        {
            return character.TryGetComponent(out ai) && (ai.m_pathAgentType == Pathfinding.AgentType.Fish || ai.m_pathAgentType == Pathfinding.AgentType.BigFish);
        }

        public static void UpdateShipsPositions()
        {
            foreach (Ship ship in Ship.Instances.ToArray().Cast<Ship>())
                if (ship.m_nview.IsOwner())
                    PlaceShip(ship);
        }

        public static void UpdateFloatingPositions()
        {
            foreach (Floating floating in Floating.Instances.ToArray().Cast<Floating>())
                CheckIfFloatingContainerBelowSurface(floating);
        }

        public static IEnumerator CheckIfShipBelowSurface(Ship ship)
        {
            if (ship == null || ship.gameObject == null)
                yield break;

            while (ship.m_nview.IsValid() && !ship.m_nview.HasOwner())
                yield return waitForFixedUpdate;

            if (!ship.m_nview.IsValid() || !ship.m_nview.IsOwner())
                yield break;

            PlaceShip(ship);
        }

        public static void PlaceShip(Ship ship)
        {
            ship.m_body.WakeUp();
            ship.m_body.isKinematic = false;

            List<MeshRenderer> watermask = ship.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                .Where(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name == "Custom/WaterMask").ToList();

            watermask.Do(renderer => renderer.gameObject.SetActive(true));

            float positionDelta = ship.m_body.position.y - (WaterLevel + ship.m_waterLevelOffset);
            if (positionDelta > 0 || !IsWaterSurfaceFrozen())
                return;

            ship.m_body.isKinematic = !placeShipAboveFrozenOcean.Value;

            if (ship.TryGetComponent(out ZSyncTransform zSyncTransform))
                zSyncTransform.m_isKinematicBody = ship.m_body.isKinematic;

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
                watermask.Do(renderer => renderer.gameObject.SetActive(false));
            }
        }

        public static void CheckIfFishAboveSurface(Fish fish)
        {
            if (fish == null || fish.m_nview == null || !fish.m_nview.IsValid())
                return;

            if (fish.m_nview.HasOwner() && !fish.m_nview.IsOwner())
                return;

            float maximumLevel = WaterLevel - _winterWaterSurfaceOffset - fish.m_height - 0.5f;
            if (fish.transform.position.y > maximumLevel)
            {
                fish.transform.position = new Vector3(fish.transform.position.x, maximumLevel, fish.transform.position.z);
                fish.m_nview.GetZDO().SetPosition(fish.transform.position);
            }

            fish.m_body.linearVelocity = Vector3.zero;
            fish.m_haveWaypoint = false;
            fish.m_isJumping = false;
        }

        public static void CheckIfCharacterBelowSurface(Character character)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
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

        public static void CheckToRemoveIceFloes()
        {
            if (IsTimeForIceFloes() || !ZNet.instance.IsServer())
                return;

            if (s_zoneCtrlPrefab == 0)
                s_zoneCtrlPrefab = (ZoneSystem.instance == null ? "_ZoneCtrl" : Utils.GetPrefabName(ZoneSystem.instance.m_zoneCtrlPrefab)).GetStableHashCode();

            int floes = 0; int zones = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (IsValidIceFloe(zdo))
                {
                    RemoveObject(zdo, true);
                    floes++;
                }

                if (zdo.GetPrefab() == s_zoneCtrlPrefab && zdo.GetBool(s_iceFloesSpawned))
                {
                    zdo.Set(s_iceFloesSpawned, false);
                    zones++;
                }
            }

            LogFloeState($"Removed overworld floes:{floes}, Zones refreshed:{zones}");

            static bool IsValidIceFloe(ZDO zdo) => zdo.GetPrefab() == _iceFloePrefab && (!WorldGenerator.IsDeepnorth(zdo.GetPosition().x, zdo.GetPosition().z) || zdo.GetBool(s_iceFloeWatermark)); // TODO: Remove IsDeepnorth check to prevent other floes from removing
        }

        public bool CheckWaterVolumeForIceFloes(WaterVolume waterVolume)
        {
            if (waterVolume == null || waterVolume.m_heightmap == null)
                return true;

            Vector3 position = waterVolume.transform.position;
            if (WorldGenerator.instance.GetBiome(position) != Biome.Ocean)
                return true;

            Vector2i zoneID = ZoneSystem.GetZone(position);

            if (!ZoneSystem.instance.IsZoneLoaded(zoneID))
                return false;

            m_tempZDOList.Clear();
            ZDOMan.instance.FindObjects(zoneID, m_tempZDOList);
            m_tempZDOList.RemoveAll(zdo => zdo.GetPrefab() != _iceFloePrefab);
            
            if (IsTimeForIceFloes() && m_tempZDOList.Count > 0)
                return true;
            else if (!IsTimeForIceFloes() && m_tempZDOList.Count == 0)
                return true;
            else if (!IsTimeForIceFloes() && m_tempZDOList.Count > 0)
            {
                LogFloeState($"Removing occasional floes: {m_tempZDOList.Count}");
                foreach (ZDO zdo in m_tempZDOList)
                    RemoveObject(zdo, force: true);
            }
            else if (IsTimeForIceFloes() && m_tempZDOList.Count == 0)
            {
                Vector3 zonePos = ZoneSystem.GetZonePos(zoneID);

                SpawnSystem spawnSystem = SpawnSystem.m_instances.FirstOrDefault(ss => ss.m_heightmap == FindHeightmap(zonePos));
                if (spawnSystem == null)
                    return false;

                ZDO zoneZDO = spawnSystem.m_nview?.GetZDO();
                if (zoneZDO != null)
                {
                    if (zoneZDO.GetBool(s_iceFloesSpawned) == true)
                        return true;

                    zoneZDO.Set(s_iceFloesSpawned, true);
                }

                ZoneSystem.SpawnMode mode = ZNetScene.instance.IsAreaReady(position) ? ZoneSystem.SpawnMode.Full : ZoneSystem.SpawnMode.Ghost;

                m_tempSpawnedObjects.Clear();

                PlaceIceFloes(zoneID, zonePos, m_tempClearAreas, mode, m_tempSpawnedObjects);
                LogFloeState($"{zoneID} {zonePos} Spawned {mode} floes:{m_tempSpawnedObjects.Count}");
                
                if (mode == ZoneSystem.SpawnMode.Ghost)
                    foreach (GameObject tempSpawnedObject in m_tempSpawnedObjects)
                        Destroy(tempSpawnedObject);

                m_tempSpawnedObjects.Clear();
            }

            return true;
        }

        public static void PlaceIceFloes(Vector2i zoneID, Vector3 zoneCenterPos, List<ZoneSystem.ClearArea> clearAreas, ZoneSystem.SpawnMode mode, List<GameObject> spawnedObjects)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            int seed = WorldGenerator.instance.GetSeed();
            float num = ZoneSystem.instance.m_zoneSize / 2f;

            UnityEngine.Random.InitState(seed + zoneID.x * 4271 + zoneID.y * 9187 + _iceFloePrefab + (SeasonState.IsActive ? seasonState.GetCurrentWorldDay() : 0));
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

                float num11 = p.y - ZoneSystem.instance.m_waterLevel;
                if (num11 < s_iceFloe.m_minAltitude || num11 > s_iceFloe.m_maxAltitude)
                    continue;

                ZoneSystem.instance.GetGroundData(ref p, out _, out var biome, out var biomeArea, out var hmap2);
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

                GameObject gameObject = Instantiate(s_iceFloe.m_prefab, p, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));

                if (mode == ZoneSystem.SpawnMode.Ghost)
                    ZNetView.FinishGhostInit();

                ZNetView netView = gameObject.GetComponent<ZNetView>();

                netView.SetLocalScale(new Vector3(scaleX, scaleY, scaleZ));

                ZDO zdo = netView.GetZDO();
                zdo.Set(s_iceFloeWatermark, true);
                zdo.Set(s_iceFloeMass, netView.m_body.mass * PowSquash(Mathf.Sqrt(Mathf.Abs(scaleX * scaleY * scaleZ)), 0.6f));

                if (mode == ZoneSystem.SpawnMode.Ghost)
                    spawnedObjects.Add(gameObject);

                clearAreas.Add(new ZoneSystem.ClearArea(p, GetFloeSize(gameObject) + 0.5f));
            }

            UnityEngine.Random.state = state;
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
                collider.enabled = false;
                collider.enabled = true;
                return Mathf.Sqrt(collider.bounds.size.x * collider.bounds.size.x / 4 + collider.bounds.size.z * collider.bounds.size.z / 4);
            }

            Renderer renderer = s_iceFloe.m_prefab.GetComponentInChildren<Renderer>();
            if (renderer)
            {
                renderer.enabled = false;
                renderer.enabled = true;
                return Mathf.Sqrt(renderer.bounds.size.x * renderer.bounds.size.x / 4 + renderer.bounds.size.z * renderer.bounds.size.z / 4);
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
            private static readonly Dictionary<Character, Vector3> m_characterSlideVelocity = new Dictionary<Character, Vector3>();

            public static void CheckForSlide(Character characterSyncVelocity)
            {
                if (m_characterSlideVelocity.TryGetValue(characterSyncVelocity, out Vector3 bodyVelocity))
                {
                    characterSyncVelocity.StartIceSliding(bodyVelocity, checkMagnitude: true);
                    m_characterSlideVelocity.Remove(characterSyncVelocity);
                }
            }

            private static void Prefix(Character __instance, ZNetView ___m_nview, float ___m_maxAirAltitude, ref Tuple<float, Vector3> __state)
            {
                if (frozenOceanSlipperiness.Value == 0 || !___m_nview.IsValid() || !___m_nview.IsOwner())
                    return;

                __state = new Tuple<float, Vector3>(Mathf.Max(0f, ___m_maxAirAltitude - __instance.transform.position.y), __instance.m_body.linearVelocity);
            }

            private static void Postfix(Character __instance, float ___m_maxAirAltitude, Tuple<float, Vector3> __state)
            {
                if (__state == null)
                    return;

                if (__state.Item1 > 1f && frozenOceanSlipperiness.Value > 0 && (___m_maxAirAltitude == __instance.transform.position.y) && __instance.IsOnIce())
                    m_characterSlideVelocity[__instance] = __state.Item2;
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

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, bool ___m_inDodge, ref bool __state)
            {
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
            private static bool Prefix(Character __instance, CapsuleCollider ___m_collider)
            {
                if (!__instance.IsOnIce())
                    return true;

                ___m_collider.material.staticFriction = 0.1f;
                ___m_collider.material.dynamicFriction = 0.1f;
                ___m_collider.material.frictionCombine = PhysicsMaterialCombine.Minimum;

                return false;
            }
        }

    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor), new[] { typeof(Biome) })]
    public static class Heightmap_GetBiomeColor_TerrainColor
    {
        public static bool overrideColor = false;

        public static bool overrideSeason = false;
        public static Season seasonOverride;

        private static Color GetColorWithoutOverride(Func<Color> getColorFunction)
        {
            bool wasOverridden = overrideColor != (overrideColor = false);

            Color result = getColorFunction();

            if (wasOverridden)
                overrideColor = true;

            return result;
        }

        private static Color GetColorWithSeasonOverride(Season season, Func<Color> getColorFunction)
        {
            bool wasDefaultColor = overrideColor != (overrideColor = true);
            
            bool wasDefaultSeason = overrideSeason != (overrideSeason = true);

            seasonOverride = season;

            Color result = getColorFunction();

            if (wasDefaultSeason)
                overrideSeason = false;

            if (wasDefaultColor)
                overrideColor = false;

            return result;
        }

        public static Color GetOriginalColor(Heightmap heightmap, float ix, float iy)
        {
            return GetColorWithoutOverride(() => heightmap.GetBiomeColor(ix, iy));
        }

        public static Color GetOriginalColor(Biome biome)
        {
            return GetColorWithoutOverride(() => Heightmap.GetBiomeColor(biome));
        }

        public static Color GetSeasonalColor(Season season, Biome biome)
        {
            return GetColorWithSeasonOverride(season, () => Heightmap.GetBiomeColor(biome));
        }

        public static Color GetSeasonalColor(Season season, Heightmap heightmap, float ix, float iy)
        {
            return GetColorWithSeasonOverride(season, () => heightmap.GetBiomeColor(ix, iy));
        }

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

            if (!overrideColor|| !SeasonState.IsActive || !UseTextureControllers())
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
            if (!overrideColor || !SeasonState.IsActive || !UseTextureControllers())
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
            if (!plainsSwampBorderFix.Value)
                return;

            if (!Heightmap_GetBiomeColor_TerrainColor.overrideColor || !SeasonState.IsActive || !UseTextureControllers())
                return;

            // Swamp-Plains and Swamp-Mistlands borders -> Blackforest
            if (__instance.IsBiomeEdge() && 0f < __result.r && __result.r < 1f && 0f < __result.a && __result.a < 1f)
                __result = new Color(0, __result.g, __result.r, __result.a);

            // Terrain season transition recoloring PoC and tests
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
        private static void Prefix() => Heightmap_GetBiomeColor_TerrainColor.overrideColor = SeasonState.IsActive && UseTextureControllers();

        [HarmonyPriority(Priority.First)]
        private static void Postfix() => Heightmap_GetBiomeColor_TerrainColor.overrideColor = false;
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
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!waterStates.ContainsKey(__instance))
                return;

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

        private static void Postfix(MusicMan __instance, ref MusicMan.NamedMusic __result)
        {
            if (!enableNightMusicOnFrozenOcean.Value)
                return;

            if (__result != null && __result.m_name == "home")
                return;

            if (!LocalPlayerIsOnFrozenOcean() || !EnvMan.IsNight())
                return;

            MusicMan.NamedMusic frozenOcean = __instance.FindMusic(frozenOceanMusic);
            if (frozenOcean == null)
            {
                MusicMan.NamedMusic intro = __instance.FindMusic("intro");
                if (intro != null)
                {
                    frozenOcean = JsonUtility.FromJson<MusicMan.NamedMusic>(JsonUtility.ToJson(intro));
                    frozenOcean.m_name = frozenOceanMusic;
                    frozenOcean.m_ambientMusic = true;
                    frozenOcean.m_loop = Settings.ContinousMusic;
                    frozenOcean.m_volume = 0.1f;
                    frozenOcean.m_fadeInTime = 10f;
                    __instance.m_music.Add(frozenOcean);
                }
            }

            if (frozenOcean != null)
                __result = frozenOcean;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    public static class EnvMan_SetEnv_FrozenOceanWindLoop
    {
        public static Dictionary<string, AudioClip> UsedAudioClips
        {
            get
            {
                if (_usedAudioClips.Count > 0 || EnvMan.instance == null)
                    return _usedAudioClips;

                foreach (EnvSetup env in EnvMan.instance.m_environments)
                    if (env.m_ambientLoop != null && !_usedAudioClips.ContainsKey(env.m_ambientLoop.name))
                        _usedAudioClips.Add(env.m_ambientLoop.name, env.m_ambientLoop);

                return _usedAudioClips;
            }
        }

        private static readonly Dictionary<string, AudioClip> _usedAudioClips = new Dictionary<string, AudioClip>();

        public static void Prefix(EnvSetup env, ref AudioClip __state)
        {
            if (!LocalPlayerIsOnFrozenOcean())
                return;

            __state = env.m_ambientLoop;
            if (env.m_ambientLoop != UsedAudioClips["Wind_BlowingLoop3"])
                env.m_ambientLoop = UsedAudioClips["Wind_ColdLoop3"];
        }

        public static void Postfix(EnvSetup env, AudioClip __state)
        {
            if (!LocalPlayerIsOnFrozenOcean())
                return;

            env.m_ambientLoop = __state;
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

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), new Type[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) })]
    public static class WaterVolume_CalcWave_FrozenOceanNoWaves
    {
        private static void Prefix(ref float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            __state = WaterVolume.s_globalWindAlpha;
            WaterVolume.s_globalWindAlpha = 0f;
        }

        private static void Postfix(float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            WaterVolume.s_globalWindAlpha = __state;
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.ConsiderJump))]
    public static class Fish_ConsiderJump_FrozenOceanFishNoJumps
    {
        private static void Prefix(ref float ___m_jumpChance, ref float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            __state = ___m_jumpChance;
            ___m_jumpChance = 0f;
        }

        private static void Postfix(ref float ___m_jumpChance, float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            ___m_jumpChance = __state;
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

    [HarmonyPatch(typeof(Fish), nameof(Fish.Start))]
    public static class Fish_Start_FrozenOcean
    {
        private static void Postfix(Fish __instance)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            CheckIfFishAboveSurface(__instance);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), new Type[] { typeof(Vector3), typeof(float), typeof(Vector4), typeof(float), typeof(float) })]
    public static class WaterVolume_CalcWave_FrozenOceanPreventWaves
    {
        private static void Prefix(ref float waterTime, ref float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            __state = waterTime;
            waterTime = 0f;
        }

        private static void Postfix(ref float waterTime, float __state)
        {
            if (!IsWaterSurfaceFrozen())
                return;

            waterTime = __state;
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
        private static readonly Vector3[] positions = new Vector3[4];
        private static void AddWaveForce(Floating floating, float fixedDeltaTime)
        {
            floating.m_body.WakeUp();
            
            foreach (Vector3 position in positions)
            {
                float depthDelta = position.y - Floating.GetLiquidLevel(position);
                Vector3 force = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4)) * (fixedDeltaTime * 50f) * depthDelta < 0 ? Vector3.up * 0.6f : Vector3.down;
                floating.m_body.AddForceAtPosition(force * 0.02f * floating.m_body.mass * 0.25f, position, ForceMode.Impulse);
            }
        }

        private static float Dampen(float value) => value / (1f + Mathf.Abs(value));

        private static bool Prefix(Floating __instance, float fixedDeltaTime)
        {
            if (!GameCamera.instance || __instance.m_nview is not ZNetView nview || !nview.IsValid() || nview.GetZDO()?.GetPrefab() != _iceFloePrefab || !__instance.m_body)
                return true;

            ZSyncTransform syncTransform = __instance.GetComponent<ZSyncTransform>();
            bool inActiveWaterDistance = Utils.DistanceXZ(GameCamera.instance.transform.position, __instance.transform.position) < s_waterDistance;
            syncTransform.m_syncBodyVelocity = inActiveWaterDistance || !__instance.HaveLiquidLevel();

            if ((syncTransform.m_syncPosition != (syncTransform.m_syncPosition = syncTransform.m_syncBodyVelocity)) && syncTransform.m_syncPosition)
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
        private static void Prefix(ref float ___m_disableLevel, ref float __state)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            __state = ___m_disableLevel;

            ___m_disableLevel -= _winterWaterSurfaceOffset;
        }

        private static void Postfix(ref float ___m_disableLevel, float __state)
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            ___m_disableLevel = __state;
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
        private static void Prefix()
        {
            if (!UseTextureControllers())
                return;

            if (!SeasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            ZoneSystem_GetGroundHeight_CheckForIceSurface.checkForIceSurface = true;
        }
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
            Instance?.CheckBiomeChanged(__instance.m_currentBiome);
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