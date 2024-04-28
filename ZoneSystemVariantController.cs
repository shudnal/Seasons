using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Heightmap;
using static Seasons.Seasons;
using static Seasons.ZoneSystemVariantController;
using static ZoneSystem;

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

        public static readonly Dictionary<Biome, Dictionary<Season, Biome>> seasonalBiomeColorOverride = new Dictionary<Biome, Dictionary<Season, Biome>>
                {
                    { Biome.BlackForest, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Swamp }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Meadows, new Dictionary<Season, Biome>() { { Season.Fall, Biome.Plains }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Plains, new Dictionary<Season, Biome>() { { Season.Spring, Biome.Meadows }, { Season.Winter, Biome.Mountain } } },
                    { Biome.Mistlands, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                    { Biome.Swamp, new Dictionary<Season, Biome>() { { Season.Winter, Biome.Mountain } } },
                };
        
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

        public static GameObject s_iceSurface;
        public static ZoneVegetation s_iceFloe;

        private const float _FoamDepthFrozen = 10f;
        private const float _WaveVel = 0f;
        private const float _WaveFoam = 0f;
        private const float _Glossiness = 0.95f;
        private const float _Metallic = 0.1f;
        private const float _DepthFade = 20f;
        private const float _ShoreFade = 0f;

        public float m_createDestroyTimer;
        public RaycastHit[] rayHits = new RaycastHit[200];

        private static ZoneSystemVariantController m_instance;

        public static ZoneSystemVariantController instance => m_instance;

        public readonly List<WaterVolume> waterVolumesCheckFloes = new List<WaterVolume>();
        private readonly List<WaterVolume> tempWaterVolumesList = new List<WaterVolume>();
        private readonly List<ClearArea> m_tempClearAreas = new List<ClearArea>();
        private readonly List<GameObject> m_tempSpawnedObjects = new List<GameObject>();

        public static bool IsWaterSurfaceFrozen() => s_freezeStatus == 1f;
        
        public static bool IsTimeForIceFloes() => enableIceFloes.Value && !IsWaterSurfaceFrozen() && seasonState.GetCurrentSeason() == Season.Winter && (int)iceFloesInWinterDays.Value.x <= seasonState.GetCurrentDay() && seasonState.GetCurrentDay() <= (int)iceFloesInWinterDays.Value.y;

        public static float s_waterLevel => s_colliderHeight == 0f || !IsWaterSurfaceFrozen() ? ZoneSystem.instance.m_waterLevel : s_colliderHeight;

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
            tempWaterVolumesList.Clear();
            foreach (WaterVolume waterVolume in waterVolumesCheckFloes)
            {
                if (!CheckWaterVolumeForIceFloes(waterVolume))
                    tempWaterVolumesList.Add(waterVolume);
            }
            waterVolumesCheckFloes.Clear();
            waterVolumesCheckFloes.AddRange(tempWaterVolumesList);
        }

        private void OnDestroy()
        {
            s_waterPlane = null;
            s_waterPlaneState = null;
            s_iceSurface = null;
            waterStates.Clear();
            m_instance = null;
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

        private static void UpdateTerrainColor(Heightmap heightmap)
        {
            if (heightmap.m_renderMesh == null)
                return;

            Heightmap_GetBiomeColor_TerrainColor.overrideColor = true;

            int num = heightmap.m_width + 1;
            Vector3 vector = heightmap.transform.position + new Vector3((float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5), 0f, (float)((double)heightmap.m_width * (double)heightmap.m_scale * -0.5));
            s_tempColors.Clear();
            for (int i = 0; i < num; i++)
            {
                float iy = DUtils.SmoothStep(0f, 1f, (float)((double)i / (double)heightmap.m_width));
                for (int j = 0; j < num; j++)
                {
                    float ix = DUtils.SmoothStep(0f, 1f, (float)((double)j / (double)heightmap.m_width));
                    if (heightmap.m_isDistantLod)
                    {
                        float wx = vector.x + j * heightmap.m_scale;
                        float wy = vector.z + i * heightmap.m_scale;
                        Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                        s_tempColors.Add(GetBiomeColor(biome));
                    }
                    else
                    {
                        s_tempColors.Add(heightmap.GetBiomeColor(ix, iy));
                    }
                }
            }

            Heightmap_GetBiomeColor_TerrainColor.overrideColor = false;

            heightmap.m_renderMesh.SetColors(s_tempColors);
        }

        public static void UpdateTerrainColors()
        {
            foreach (Heightmap instance in Instances.Cast<Heightmap>())
                UpdateTerrainColor(instance);
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
            iceCollider.material.frictionCombine = PhysicMaterialCombine.Minimum;
            iceCollider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase;
        }

        public static void UpdateWaterState()
        {
            if (!seasonState.IsActive)
                return;

            s_freezeStatus = seasonState.GetWaterSurfaceFreezeStatus();

            CheckToRemoveIceFloes();

            foreach (KeyValuePair<WaterVolume, WaterState> waterState in waterStates)
                UpdateWater(waterState.Key, waterState.Value);

            UpdateWaterSurface(s_waterPlane, s_waterPlaneState);

            Seasons.instance.StartCoroutine(UpdateWaterObjects());
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
                    if (waterInteractable is Fish)
                        CheckIfFishAboveSurface(waterInteractable as Fish);
                    else if (waterInteractable is Character)
                        CheckIfCharacterAboveSurface(waterInteractable as Character);

                instance.waterVolumesCheckFloes.Add(waterVolume);
            }

            yield return waitForFixedUpdate;

            foreach (Ship ship in Ship.Instances.Cast<Ship>())
                yield return CheckIfShipBelowSurface(ship);
        }

        public static bool IsUnderwaterAI(Character character, out BaseAI ai)
        {
            return character.TryGetComponent(out ai) && (ai.m_pathAgentType == Pathfinding.AgentType.Fish || ai.m_pathAgentType == Pathfinding.AgentType.BigFish);
        }

        public static void UpdateShipsPositions()
        {
            foreach (Ship ship in Ship.Instances.Cast<Ship>())
                if (ship.m_nview.IsOwner())
                    PlaceShip(ship);
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

            if (ship.m_body.position.y > s_waterLevel + ship.m_waterLevelOffset || !IsWaterSurfaceFrozen())
                return;

            ship.m_body.isKinematic = !placeShipAboveFrozenOcean.Value;

            if (placeShipAboveFrozenOcean.Value)
            {
                ship.m_body.rotation = Quaternion.identity;
                ship.m_body.position = new Vector3(ship.m_body.position.x, s_waterLevel + ship.m_waterLevelOffset + 0.1f, ship.m_body.position.z);
                ship.m_body.velocity = Vector3.zero;
            }
        }

        public static void CheckIfFishAboveSurface(Fish fish)
        {
            if (fish == null || fish.m_nview == null || !fish.m_nview.IsValid())
                return;

            if (fish.m_nview.HasOwner() && !fish.m_nview.IsOwner())
                return;

            if (fish.transform.position.y > s_waterLevel - _winterWaterSurfaceOffset)
                fish.transform.position = new Vector3(fish.transform.position.x, s_waterLevel - _winterWaterSurfaceOffset, fish.transform.position.z);

            fish.m_body.velocity = Vector3.zero;
            fish.m_haveWaypoint = false;
            fish.m_isJumping = false;
        }

        public static void CheckIfCharacterAboveSurface(Character character)
        {
            if (character == null || character.m_nview == null || !character.m_nview.IsValid())
                return;

            if (!character.m_nview.IsOwner())
                return;

            if (IsUnderwaterAI(character, out BaseAI ai))
            {
                if (character.transform.position.y >= s_waterLevel)
                {
                    m_tempHits.Clear();
                    Pathfinding.instance.FindGround(character.transform.position, testWater: true, m_tempHits, Pathfinding.instance.GetSettings(ai.m_pathAgentType));

                    Vector3 hit = m_tempHits.Find(h => h.y < s_waterLevel);
                    if (hit.y != 0)
                    {
                        character.m_body.velocity = Vector3.zero;
                        character.transform.position = new Vector3(character.transform.position.x, Mathf.Max(s_waterLevel - _winterWaterSurfaceOffset, hit.y + 0.1f), character.transform.position.z);
                    }
                }
            }
            else if (character.transform.position.y <= s_waterLevel && !character.IsAttachedToShip())
            {
                character.m_body.velocity = Vector3.zero;
                character.transform.position = new Vector3(character.transform.position.x, s_waterLevel + 0.5f, character.transform.position.z);
                character.InvalidateCachedLiquidDepth();
                character.m_maxAirAltitude = character.transform.position.y;
                character.m_swimTimer = 0.6f;
            }
        }

        public static void CheckToRemoveIceFloes()
        {
            if (IsTimeForIceFloes() || !ZNet.instance.IsServer())
                return;

            int floes = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetPrefab() != _iceFloePrefab)
                    continue;

                Vector3 position = zdo.GetPosition();

                float num = WorldGenerator.WorldAngle(position.x, position.z) * 100.0f;

                if (new Vector2(position.x, position.z + 4000.0f).magnitude > 12000.0f + num)
                    continue;

                RemoveObject(zdo, true);
                floes++;
            }
            
            LogFloeState($"Removed overworld floes:{floes}");
        }

        public static bool CheckWaterVolumeForIceFloes(WaterVolume waterVolume)
        {
            if (waterVolume == null || waterVolume.m_heightmap == null)
                return true;

            Vector3 position = waterVolume.transform.position;
            if (WorldGenerator.instance.GetBiome(position) != Biome.Ocean)
                return true;

            Vector2i zoneID = ZoneSystem.instance.GetZone(position);

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
                instance.m_tempClearAreas.Clear();
                instance.m_tempSpawnedObjects.Clear();

                Vector3 zonePos = ZoneSystem.instance.GetZonePos(zoneID);

                SpawnMode mode = ZNetScene.instance.IsAreaReady(position) ? SpawnMode.Full : SpawnMode.Ghost;

                PlaceIceFloes(zoneID, zonePos, instance.m_tempClearAreas, mode, instance.m_tempSpawnedObjects);
                LogFloeState($"{zoneID} {zonePos} Spawned {mode} floes:{instance.m_tempSpawnedObjects.Count}");

                if (mode == SpawnMode.Ghost)
                {
                    foreach (GameObject tempSpawnedObject in instance.m_tempSpawnedObjects)
                        Destroy(tempSpawnedObject);
                    
                    instance.m_tempSpawnedObjects.Clear();
                }
            }

            return true;
        }

        public static void PlaceIceFloes(Vector2i zoneID, Vector3 zoneCenterPos, List<ClearArea> clearAreas, SpawnMode mode, List<GameObject> spawnedObjects)
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            int seed = WorldGenerator.instance.GetSeed();
            float num = ZoneSystem.instance.m_zoneSize / 2f;
            ZoneVegetation item = s_iceFloe;

            UnityEngine.Random.InitState(seed + zoneID.x * 4271 + zoneID.y * 9187 + _iceFloePrefab);
            int spawnCount = UnityEngine.Random.Range((int)amountOfIceFloesInWinterDays.Value.x, (int)amountOfIceFloesInWinterDays.Value.y + 1);
            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 p = new Vector3(UnityEngine.Random.Range(zoneCenterPos.x - num, zoneCenterPos.x + num), 0f, UnityEngine.Random.Range(zoneCenterPos.z - num, zoneCenterPos.z + num));
                if (ZoneSystem.instance.InsideClearArea(clearAreas, p))
                    continue;

                if (item.m_blockCheck && ZoneSystem.instance.IsBlocked(p))
                    continue;

                float num11 = p.y - ZoneSystem.instance.m_waterLevel;
                if (num11 < item.m_minAltitude || num11 > item.m_maxAltitude)
                    continue;

                ZoneSystem.instance.GetGroundData(ref p, out _, out var biome, out var biomeArea, out var hmap2);
                if ((item.m_biome & biome) == 0 || (item.m_biomeArea & biomeArea) == 0)
                    continue;

                if (item.m_minOceanDepth != item.m_maxOceanDepth)
                {
                    float oceanDepth = hmap2.GetOceanDepth(p);
                    if (oceanDepth < item.m_minOceanDepth || oceanDepth > item.m_maxOceanDepth)
                        continue;
                }

                if (item.m_snapToWater)
                    p.y = ZoneSystem.instance.m_waterLevel - _winterWaterSurfaceOffset;

                if (mode == SpawnMode.Ghost)
                    ZNetView.StartGhostInit();

                GameObject gameObject = Instantiate(item.m_prefab, p, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0));

                if (mode == SpawnMode.Ghost)
                    ZNetView.FinishGhostInit();

                ZNetView component = gameObject.GetComponent<ZNetView>();

                float scaleX = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y);
                float scaleY = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y);
                float scaleZ = UnityEngine.Random.Range(iceFloesScale.Value.x, iceFloesScale.Value.y);

                float radius = 5f;

                component.SetLocalScale(new Vector3(scaleX, scaleY, scaleZ));
                Collider[] componentsInChildren = gameObject.GetComponentsInChildren<Collider>();
                foreach (Collider obj in componentsInChildren)
                {
                    obj.enabled = false;
                    obj.enabled = true;
                    radius = Math.Max(radius, obj.bounds.size.magnitude / 2);
                }

                if (mode == SpawnMode.Ghost)
                    spawnedObjects.Add(gameObject);

                clearAreas.Add(new ClearArea(p, radius));
            }

            UnityEngine.Random.state = state;
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

        private static void LogFloeState(string log)
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

                __state = new Tuple<float, Vector3>(Mathf.Max(0f, ___m_maxAirAltitude - __instance.transform.position.y), __instance.m_body.velocity);
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
                m_bodyVelocity = __instance.m_queuedDodgeDir * __instance.m_body.velocity.magnitude;

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
                ___m_collider.material.frictionCombine = PhysicMaterialCombine.Minimum;

                return false;
            }
        }

    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiomeColor), new[] { typeof(Biome) })]
    public static class Heightmap_GetBiomeColor_TerrainColor
    {
        public static bool overrideColor = false;

        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref Biome biome, ref Biome __state)
        {
            __state = Biome.None;

            if (!overrideColor || !seasonState.IsActive || !UseTextureControllers())
                return;

            if (seasonalBiomeColorOverride.TryGetValue(biome, out Dictionary<Season, Biome> overrideBiome) && overrideBiome.TryGetValue(seasonState.GetCurrentSeason(), out Biome overridedBiome))
            {
                __state = biome;
                biome = overridedBiome;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref Biome biome, Biome __state)
        {
            if (__state == Biome.None)
                return;

            biome = __state;
        }
    }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.RebuildRenderMesh))]
    public static class Heightmap_RebuildRenderMesh_TerrainColor
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            Heightmap_GetBiomeColor_TerrainColor.overrideColor = seasonState.IsActive && UseTextureControllers();
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix()
        {
            Heightmap_GetBiomeColor_TerrainColor.overrideColor = false;
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.Awake))]
    public static class WaterVolume_Awake_WaterState
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!seasonState.IsActive)
                return;

            if (!__instance.m_useGlobalWind)
                return;

            if (waterStates.ContainsKey(__instance))
                return;
            
            waterStates.Add(__instance, new WaterState(__instance));
            ZoneSystemVariantController.instance.waterVolumesCheckFloes.Add(__instance);
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

            if (!seasonState.IsActive)
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

            if (!seasonState.IsActive)
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

            if (!seasonState.IsActive)
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

            if (!seasonState.IsActive)
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
        public static Dictionary<string, AudioClip> usedAudioClips
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
            if (env.m_ambientLoop != usedAudioClips["Wind_BlowingLoop3"])
                env.m_ambientLoop = usedAudioClips["Wind_ColdLoop3"];
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
        private static bool Prefix(Rigidbody ___m_body, ZNetView ___m_nview)
        {
            if (!IsWaterSurfaceFrozen())
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
        private static bool Prefix()
        {
            return !IsWaterSurfaceFrozen();
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
                ___m_teleportTargetPos = new Vector3(___m_teleportTargetPos.x, ___m_teleportTargetPos.y + s_waterLevel, ___m_teleportTargetPos.z);
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

            CheckIfCharacterAboveSurface(__instance);
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.Awake))]
    public static class Fish_Awake_FrozenOceanCharacter
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

            if (!seasonState.IsActive)
                return true;

            if (!IsWaterSurfaceFrozen())
                return true;

            Vector3 origin = p;
            origin.y += 2000f;
            int num = Physics.RaycastNonAlloc(origin, Vector3.down, ZoneSystemVariantController.instance.rayHits, 10000f, ___m_blockRayMask);
            __result = false;
            for (int i = 0; i < num; i++)
            {
                if (ZoneSystemVariantController.instance.rayHits[i].collider.name == _iceSurfaceName)
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
        private static void AddWaveForce(Vector3[] positions, Rigidbody rbody, float fixedDeltaTime)
        {
            foreach (Vector3 position in positions)
            {
                float depthDelta = position.y - Floating.GetLiquidLevel(position);
                if (Mathf.Abs(depthDelta) < 0.2f)
                    continue;

                Vector3 force = 0.5f * Mathf.Clamp01(Mathf.Abs(depthDelta / 4)) * (fixedDeltaTime * 50f) * depthDelta < 0 ? Vector3.up / 2 : Vector3.down;
                rbody.AddForceAtPosition(force * 0.02f, position, ForceMode.VelocityChange);
            }
        }

        private static void Postfix(Floating __instance, float fixedDeltaTime, ZNetView ___m_nview, Rigidbody ___m_body, Collider ___m_collider)
        {
            if (!___m_nview.IsValid() || !___m_nview.IsOwner())
                return;

            if (___m_nview.GetZDO().GetPrefab() != _iceFloePrefab)
                return;

            if (__instance.HaveLiquidLevel() && __instance.GetFloatDepth() > 0f && ___m_body.IsSleeping())
                ___m_body.WakeUp();

            if (!__instance.HaveLiquidLevel() || __instance.GetFloatDepth() > 0f)
                return;

            Vector3 wind = WaterVolume.s_globalWindAlpha == 0f ? WaterVolume.s_globalWind1 : Vector4.Lerp(WaterVolume.s_globalWind1, WaterVolume.s_globalWind2, WaterVolume.s_globalWindAlpha);
            Vector3 windSide = Vector3.Cross(wind, __instance.transform.up);

            Vector3[] forcePositions = new Vector3[4]
            {
                ___m_collider.ClosestPoint(___m_body.worldCenterOfMass + wind * 100f),
                ___m_collider.ClosestPoint(___m_body.worldCenterOfMass - wind * 100f),
                ___m_collider.ClosestPoint(___m_body.worldCenterOfMass + windSide * 100f),
                ___m_collider.ClosestPoint(___m_body.worldCenterOfMass - windSide * 100f)
            };

            AddWaveForce(forcePositions, ___m_body, fixedDeltaTime);
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    public static class Ship_CustomFixedUpdate_FrozenShip
    {
        private static void Prefix(ref float ___m_disableLevel, ref float __state)
        {
            if (!UseTextureControllers())
                return;

            if (!seasonState.IsActive)
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

            if (!seasonState.IsActive)
                return;

            if (!IsWaterSurfaceFrozen())
                return;

            ___m_disableLevel = __state;
        }
    }

}
