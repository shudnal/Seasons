using System;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;
using System.Collections;

namespace Seasons
{
    internal class WaterVariantController : MonoBehaviour
    {
        private WaterVolume m_waterVolume;
        private MeshRenderer m_waterSurface;
        private GameObject m_iceSurface;
        private float m_surfaceOffset;

        private float m_foamDepth;

        private Color m_colorTop;
        private Color m_colorBottom;
        private Color m_colorBottomShallow;

        private Color m_colorTopFrozen;
        private Color m_colorBottomFrozen;
        private Color m_colorBottomShallowFrozen;

        private int m_myListIndex = -1;
        private static readonly List<WaterVariantController> s_allControllers = new List<WaterVariantController>();

        private static readonly MaterialPropertyBlock s_matBlock = new MaterialPropertyBlock();

        private static float s_freezeStatus = 0f;

        public const float _winterWaterSurfaceOffset = 2f;
        public const float _colliderOffset = -0.01f;
        public const string _iceSurfaceName = "IceSurface";

        public static float s_colliderHeight = 0f;

        public static GameObject s_iceSurface;

        const float _FoamDepthFrozen = 10f;
        const float _WaveVel = 0f;
        const float _WaveFoam = 0f;
        const float _Glossiness = 0.95f;
        const float _Metallic = 0.1f;
        const float _DepthFade = 20f;
        const float _ShoreFade = 0f;
        
        public static bool IsWaterSurfaceFrozen() => s_freezeStatus == 1f;

        public static float s_waterLevel => s_colliderHeight == 0f || !IsWaterSurfaceFrozen() ? ZoneSystem.instance.m_waterLevel : s_colliderHeight;

        private void Awake()
        {
            s_allControllers.Add(this);
            m_myListIndex = s_allControllers.Count - 1;
        }

        private void OnDestroy()
        {
            if (m_myListIndex >= 0)
            {
                s_allControllers[m_myListIndex] = s_allControllers[s_allControllers.Count - 1];
                s_allControllers[m_myListIndex].m_myListIndex = m_myListIndex;
                s_allControllers.RemoveAt(s_allControllers.Count - 1);
                m_myListIndex = -1;
            }
        }

        private void RevertChanges()
        {
            if (m_waterSurface != null)
            {
                m_waterSurface.SetPropertyBlock(null);
            };

            if (m_waterVolume != null)
            {
                m_waterVolume.m_surfaceOffset = m_surfaceOffset;
                m_waterVolume.m_useGlobalWind = true;
                m_waterVolume.SetupMaterial();
            }

            SetupIceCollider();
        }

        public void Init(WaterVolume waterVolume)
        {
            m_waterVolume = waterVolume;
            m_surfaceOffset = waterVolume.m_surfaceOffset;
            Init(waterVolume.m_waterSurface);
        }

        public void Init(MeshRenderer waterSurface)
        {
            m_waterSurface = waterSurface;

            m_colorTop = m_waterSurface.sharedMaterial.GetColor("_ColorTop");
            m_colorBottom = m_waterSurface.sharedMaterial.GetColor("_ColorBottom");
            m_colorBottomShallow = m_waterSurface.sharedMaterial.GetColor("_ColorBottomShallow");
            m_foamDepth = m_waterSurface.sharedMaterial.GetFloat("_FoamDepth");

            m_colorTopFrozen = new Color(0.98f, 0.98f, 1f);
            m_colorBottomFrozen = Color.Lerp(m_colorBottom, Color.white, 0.5f);
            m_colorBottomShallowFrozen = Color.Lerp(m_colorBottomShallow, Color.white, 0.5f);

            UpdateState();
        }

        private void UpdateState()
        {
            if (s_freezeStatus == 0f)
            {
                RevertChanges();
                return;
            }

            s_matBlock.Clear();
            s_matBlock.SetColor("_FoamColor", new Color(0.95f, 0.96f, 0.98f));
            s_matBlock.SetFloat("_FoamDepth", Mathf.Lerp(m_foamDepth, _FoamDepthFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorTop", Color.Lerp(m_colorTop, m_colorTopFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottom", Color.Lerp(m_colorBottom, m_colorBottomFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottomShallow", Color.Lerp(m_colorBottomShallow, m_colorBottomShallowFrozen, s_freezeStatus));

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

            m_waterSurface.SetPropertyBlock(s_matBlock);

            SetupIceCollider();

            if (m_waterVolume != null)
            {
                m_waterVolume.m_surfaceOffset = m_surfaceOffset - (IsWaterSurfaceFrozen() ? _winterWaterSurfaceOffset : 0);
                m_waterVolume.m_useGlobalWind = !IsWaterSurfaceFrozen();
                m_waterVolume.SetupMaterial();
            }
        }

        private void SetupIceCollider()
        {
            if (m_waterVolume != null && m_iceSurface == null)
            {
                m_iceSurface = m_waterVolume.transform.parent.Find(_iceSurfaceName).gameObject;
            }

            m_iceSurface?.SetActive(IsWaterSurfaceFrozen());
        }

        public static void AddIceCollider(GameObject zonePrefab)
        {
            if (s_iceSurface != null)
                return;

            Transform water = zonePrefab.transform.Find("Water");
            if (water == null)
                return;

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
            s_freezeStatus = seasonState.GetWaterSurfaceFreezeStatus();

            foreach (WaterVariantController controller in s_allControllers)
                controller.UpdateState();

            Seasons.instance.StartCoroutine(UpdateWaterObjects());
        }

        public static bool LocalPlayerIsOnFrozenOcean() => IsWaterSurfaceFrozen()
                                        && Player.m_localPlayer != null
                                        && Player.m_localPlayer.GetCurrentBiome() == Heightmap.Biome.Ocean;

        public static IEnumerator UpdateWaterObjects()
        {
            yield return new WaitForFixedUpdate();

            UpdateShipsPositions();

            yield return new WaitForFixedUpdate();

            foreach (WaterVolume waterVolume in WaterVolume.Instances)
                foreach (IWaterInteractable waterInteractable in waterVolume.m_inWater)
                    if (waterInteractable is Fish)
                        CheckIfFishAboveSurface(waterInteractable as Fish);
                    else if (waterInteractable is Character)
                        CheckIfCharacterAboveSurface(waterInteractable as Character);
        }

        public static bool IsUnderwaterAI(Character character, out BaseAI ai)
        {
            return character.TryGetComponent(out ai) && (ai.m_pathAgentType == Pathfinding.AgentType.Fish || ai.m_pathAgentType == Pathfinding.AgentType.BigFish);
        }

        public static void UpdateShipsPositions()
        {
            foreach (Ship ship in Ship.Instances)
                if (ship.m_nview.IsOwner())
                    PlaceShip(ship);
        }

        public static IEnumerator CheckIfShipBelowSurface(Ship ship)
        {
            while (!ship.m_nview.HasOwner())
                yield return new WaitForFixedUpdate();

            if (!ship.m_nview.IsOwner())
                yield break;

            PlaceShip(ship);
        }

        public static void PlaceShip(Ship ship)
        {
            ship.m_body.isKinematic = false;

            if (ship.m_body.position.y > s_waterLevel + ship.m_waterLevelOffset || !IsWaterSurfaceFrozen())
                return;

            ship.m_body.WakeUp();
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
            if (!fish.m_nview.IsOwner())
                return;

            if (fish.transform.position.y > s_waterLevel - _winterWaterSurfaceOffset)
                fish.transform.position = new Vector3(fish.transform.position.x, s_waterLevel - _winterWaterSurfaceOffset, fish.transform.position.z);

            fish.m_body.velocity = Vector3.zero;
        }

        public static void CheckIfCharacterAboveSurface(Character character)
        {
            if (!character.m_nview.IsOwner())
                return;

            if (IsUnderwaterAI(character, out BaseAI ai))
            {
                if (character.transform.position.y >= s_waterLevel)
                {
                    List<Vector3> hits = new List<Vector3>();
                    Pathfinding.instance.FindGround(character.transform.position, testWater: true, hits, Pathfinding.instance.GetSettings(ai.m_pathAgentType));

                    Vector3 hit = hits.Find(h => h.y < s_waterLevel);
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
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return false;

            if (!character.IsOnGround())
                return false;

            Collider lastGroundCollider = character.GetLastGroundCollider();
            if (lastGroundCollider == null)
                return false;

            return lastGroundCollider.name == WaterVariantController._iceSurfaceName;
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

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.Awake))]
    public static class WaterVolume_Awake_WaterVariantControllerInit
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

            __instance.gameObject.AddComponent<WaterVariantController>().Init(__instance);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateWaterTime))]
    public static class WaterVolume_UpdateWaterTime_WaterVariantControllerInit
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!seasonState.IsActive)
                return;

            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            WaterVolume.s_waterTime = 0f;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_AddWaterVariantControllerToWaterPlane
    {
        private static void Postfix(ZoneSystem __instance)
        {
            if (!UseTextureControllers())
                return;

            if (!seasonState.IsActive)
                return;

            WaterVariantController.AddIceCollider(__instance.m_zonePrefab);

            Transform waterPlane = EnvMan.instance.transform.Find("WaterPlane");
            if (waterPlane != null)
            {
                MeshRenderer watersurface = waterPlane.GetComponentInChildren<MeshRenderer>();
                if (watersurface != null)
                    waterPlane.gameObject.AddComponent<WaterVariantController>().Init(watersurface);
            }
        }
    }

    [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.FindAverageOceanPoint))]
    public static class AudioMan_FindAverageOceanPoint_DisableOceanSounds
    {
        private static bool Prefix()
        {
            return !WaterVariantController.IsWaterSurfaceFrozen();
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

            __result = Player.m_localPlayer.GetCurrentBiome() == Heightmap.Biome.Ocean ? FootStep.GroundMaterial.Default : FootStep.GroundMaterial.Snow;
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

            if (!WaterVariantController.LocalPlayerIsOnFrozenOcean() || !EnvMan.instance.IsNight())
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
            if (!WaterVariantController.LocalPlayerIsOnFrozenOcean())
                return;

            __state = env.m_ambientLoop;
            if (env.m_ambientLoop != usedAudioClips["Wind_BlowingLoop3"])
                env.m_ambientLoop = usedAudioClips["Wind_ColdLoop3"];
        }

        public static void Postfix(EnvSetup env, AudioClip __state)
        {
            if (!WaterVariantController.LocalPlayerIsOnFrozenOcean())
                return;

            env.m_ambientLoop = __state;
        }
    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.FixedUpdate))]
    public static class Leviathan_FixedUpdate_FrozenOceanLeviathan
    {
        private static bool Prefix(Leviathan __instance, Rigidbody ___m_body, ZNetView ___m_nview)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
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
            return !WaterVariantController.IsWaterSurfaceFrozen();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    public static class Player_TeleportTo_FrozenOceanMinimapTeleportation
    {
        private static void Postfix(Player __instance, bool __result, ref Vector3 ___m_teleportTargetPos)
        {
            if (!__result)
                return;

            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            if (___m_teleportTargetPos.y == 0)
                ___m_teleportTargetPos = new Vector3 (___m_teleportTargetPos.x, ___m_teleportTargetPos.y + WaterVariantController.s_waterLevel, ___m_teleportTargetPos.z);
        }
    }

    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), new Type[] { typeof(Vector3), typeof(float), typeof(float), typeof(float) })]
    public static class WaterVolume_CalcWave_FrozenOceanNoWaves
    {
        private static void Prefix(ref float __state)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            __state = WaterVolume.s_globalWindAlpha;
            WaterVolume.s_globalWindAlpha = 0f;
        }

        private static void Postfix(float __state)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            WaterVolume.s_globalWindAlpha = __state;
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.ConsiderJump))]
    public static class Fish_ConsiderJump_FrozenOceanFishNoJumps
    {
        private static void Prefix(ref float ___m_jumpChance, ref float __state)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            __state = ___m_jumpChance;
            ___m_jumpChance = 0f;
        }

        private static void Postfix(ref float ___m_jumpChance, float __state)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            ___m_jumpChance = __state;
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.Start))]
    public static class Ship_Start_FrozenOceanShip
    {
        private static void Postfix(Ship __instance)
        {
            __instance.StartCoroutine(WaterVariantController.CheckIfShipBelowSurface(__instance));
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    public static class Character_Awake_FrozenOceanCharacter
    {
        private static void Postfix(Character __instance)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            WaterVariantController.CheckIfCharacterAboveSurface(__instance);
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.Awake))]
    public static class Fish_Awake_FrozenOceanCharacter
    {
        private static void Postfix(Fish __instance)
        {
            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return;

            WaterVariantController.CheckIfFishAboveSurface(__instance);
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

            if (!WaterVariantController.IsWaterSurfaceFrozen())
                return true;

            Vector3 origin = p;
            origin.y += 2000f;
            __result = Physics.Raycast(origin, Vector3.down, out var hitInfo, 10000f, ___m_blockRayMask);
            if (__result && hitInfo.point.y == WaterVariantController.s_colliderHeight)
                __result = Physics.Raycast(hitInfo.point + new Vector3(0, WaterVariantController._colliderOffset, 0), Vector3.down, 10000f, ___m_blockRayMask);

            return false;
        }
    }

}
