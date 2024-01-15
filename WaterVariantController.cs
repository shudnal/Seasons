using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.SeasonLightings;
using static Seasons.Seasons;

namespace Seasons
{
    internal class WaterVariantController : MonoBehaviour
    {
        private WaterVolume m_waterVolume;
        private MeshRenderer m_waterSurface;
        private MeshCollider m_snowCollider;
        private float m_surfaceOffset;
        private int layer;

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

        const float c_FoamDepthFrozen = 10f;
        const float _WaveVel = 0f;
        const float _WaveFoam = 0f;
        const float _Glossiness = 1.0f;
        const float _Metallic = 0.1f;
        const float _DepthFade = 20f;
        const float _ShoreFade = 0f;

        public static bool IsFrozen() => s_freezeStatus == 1f;
        public static bool IsFreezing() => s_freezeStatus > 0f;

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
                m_waterSurface.gameObject.layer = layer;
            };

            if (m_waterVolume != null)
            {
                m_waterVolume.m_surfaceOffset = m_surfaceOffset;
                m_waterVolume.m_useGlobalWind = true;
            }
                
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
            layer = m_waterSurface.gameObject.layer;

            m_colorTop = m_waterSurface.sharedMaterial.GetColor("_ColorTop");
            m_colorBottom = m_waterSurface.sharedMaterial.GetColor("_ColorBottom");
            m_colorBottomShallow = m_waterSurface.sharedMaterial.GetColor("_ColorBottomShallow");
            m_foamDepth = m_waterSurface.sharedMaterial.GetFloat("_FoamDepth");

            m_colorTopFrozen = Color.white;
            m_colorBottomFrozen = Color.Lerp(m_colorBottom, Color.white, 0.5f);
            m_colorBottomShallowFrozen = Color.Lerp(m_colorBottomShallow, Color.white, 0.5f);

            UpdateState();
        }

        private void UpdateState()
        {
            if (!IsFreezing())
            {
                RevertChanges();
                return;
            }

            InitSnowCollider();

            s_matBlock.Clear();
            s_matBlock.SetColor("_FoamColor", Color.white);
            s_matBlock.SetFloat("_FoamDepth", Mathf.Lerp(m_foamDepth, c_FoamDepthFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorTop", Color.Lerp(m_colorTop, m_colorTopFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottom", Color.Lerp(m_colorBottom, m_colorBottomFrozen, s_freezeStatus));
            s_matBlock.SetColor("_ColorBottomShallow", Color.Lerp(m_colorBottomShallow, m_colorBottomShallowFrozen, s_freezeStatus));

            if (IsFrozen())
            {
                s_matBlock.SetFloat(WaterVolume.s_shaderWaterTime, 0f);
                s_matBlock.SetFloat(WaterVolume.s_shaderUseGlobalWind, 0f);

                s_matBlock.SetFloat("_DepthFade", Seasons._DepthFade.Value);
                s_matBlock.SetFloat("_Glossiness", Seasons._Glossiness.Value);
                s_matBlock.SetFloat("_Metallic", Seasons._Metallic.Value);
                s_matBlock.SetFloat("_ShoreFade", _ShoreFade);
                s_matBlock.SetFloat("_WaveVel", _WaveVel);
                s_matBlock.SetFloat("_WaveFoam", _WaveFoam);

                if (m_waterVolume != null)
                {
                    m_waterVolume.m_surfaceOffset = m_surfaceOffset - 3f;
                    m_waterVolume.m_useGlobalWind = false;
                }

                m_waterSurface.gameObject.layer = 0; // Default
            }

            m_waterSurface.SetPropertyBlock(s_matBlock);
        }

        private void InitSnowCollider()
        {
            if (m_waterVolume == null)
                return;

            if (m_snowCollider == null && IsFrozen())
            {
                m_snowCollider = m_waterVolume.m_waterSurface.gameObject.AddComponent<MeshCollider>();
                m_snowCollider.sharedMesh = m_waterSurface.GetComponent<MeshFilter>().sharedMesh;
            }

            if (m_snowCollider != null)
                m_snowCollider.enabled = IsFrozen();
        }

        public static void UpdateWaterState()
        {
            s_freezeStatus = seasonState.GetWaterSurfaceFreezeStatus();

            foreach (WaterVariantController controller in s_allControllers)
                controller.UpdateState();
        }

        public static bool PlayerIsOnFrozenOcean() => IsFrozen()
                                        && Player.m_localPlayer != null
                                        && Player.m_localPlayer.GetCurrentBiome() == Heightmap.Biome.Ocean;

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

            if (!WaterVariantController.IsFrozen())
                return;

            WaterVolume.s_waterTime = 0f;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
    public static class ZoneSystem_Start_AddWaterVariantControllerToWaterPlane
    {
        private static void Postfix()
        {
            if (!controlYggdrasil.Value)
                return;

            Transform waterPlane = EnvMan.instance.transform.Find("WaterPlane");
            if (waterPlane == null)
                return;

            MeshRenderer watersurface = waterPlane.GetComponentInChildren<MeshRenderer>();
            if (watersurface == null)
                return;

            waterPlane.gameObject.AddComponent<WaterVariantController>().Init(watersurface);
        }
    }

    [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.FindAverageOceanPoint))]
    public static class AudioMan_FindAverageOceanPoint_DisableOceanSounds
    {
        private static bool Prefix()
        {
            return !WaterVariantController.IsFrozen();
        }
    }

    [HarmonyPatch(typeof(FootStep), nameof(FootStep.GetGroundMaterial))]
    public static class FootStep_GetGroundMaterial_FrozenOceanFootstep
    {
        private static bool Prefix(Character character, ref FootStep.GroundMaterial __result)
        {
            if (character == null || character != Player.m_localPlayer)
                return true;

            if (!WaterVariantController.IsFrozen())
                return true;

            Collider lastGroundCollider = character.GetLastGroundCollider();
            if (lastGroundCollider == null)
                return true;

            if (lastGroundCollider.name != "WaterSurface")
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

            if (!WaterVariantController.PlayerIsOnFrozenOcean() || !EnvMan.instance.IsNight())
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
            if (!WaterVariantController.PlayerIsOnFrozenOcean())
                return;

            __state = env.m_ambientLoop;
            if (env.m_ambientLoop != usedAudioClips["Wind_BlowingLoop3"])
                env.m_ambientLoop = usedAudioClips["Wind_ColdLoop3"];
        }

        public static void Postfix(EnvSetup env, AudioClip __state)
        {
            if (!WaterVariantController.PlayerIsOnFrozenOcean())
                return;

            env.m_ambientLoop = __state;
        }

    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.FixedUpdate))]
    public static class Leviathan_FixedUpdate_FrozenOceanLeviathan
    {
        private static bool Prefix(Leviathan __instance, Rigidbody ___m_body, ZNetView ___m_nview)
        {
            if (!WaterVariantController.IsFrozen())
                return true;
            
            if (___m_nview.IsValid() && ___m_nview.IsOwner())
            {
                Vector3 position2 = __instance.transform.position;
                position2.y = Floating.GetLiquidLevel(__instance.transform.position, 0) - 5f;
                __instance.transform.position = position2;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Leviathan), nameof(Leviathan.OnHit))]
    public static class Leviathan_OnHit_FrozenOceanLeviathan
    {
        private static bool Prefix()
        {
            return !WaterVariantController.IsFrozen();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateBodyFriction))]
    public static class Character_UpdateBodyFriction_FrozenOceanSurface
    {
        private static bool Prefix(Character __instance, CapsuleCollider ___m_collider)
        {
            if (__instance != Player.m_localPlayer)
                return true;

            if (!WaterVariantController.PlayerIsOnFrozenOcean())
                return true;

            ___m_collider.material.staticFriction = 1f;
            ___m_collider.material.dynamicFriction = 1f;
            ___m_collider.material.frictionCombine = PhysicMaterialCombine.Maximum;

            return false;
        }
    }

    



}
