using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace Seasons.Controllers
{
    [HarmonyPatch(typeof(Game), nameof(Game.UpdateRespawn))]
    public static class Game_UpdateRespawn_WaitForTextureCache
    {
        public static bool Prefix() => !TextureCachingController.InProcess;
    }

    public class TextureCachingController : MonoBehaviour
    {
        private static TextureCachingController _instance;

        public static TextureCachingController instance => _instance;

        public static bool InProcess => instance != null && instance._worker != null;

        public static LoadingIndicator LoadingIndicator => Hud.instance?.m_loadingIndicator;

        internal static void StartCaching(SeasonalTextureVariants texturesVariants, bool force)
        {
            if (!ZoneSystem.instance || InProcess)
                return;
            TextureCachingController controller = instance ? instance : ZoneSystem.instance.gameObject.AddComponent<TextureCachingController>();
            controller.Initialize(texturesVariants, force);
        }

        private SeasonalTextureVariants _texturesVariants;

        private Coroutine _worker;

        private static bool _indicatorInitialized;
        private static string _indicatorText;
        private static float _indicatorProgress;
        private static float _indicatorMaxProgress;

        public void Awake()
        {
            _instance = this;
            Seasons.LogInfo("Starting up texture caching process");
        }

        public void Initialize(SeasonalTextureVariants texturesVariants, bool force)
        {
            _texturesVariants = texturesVariants;

            _worker = StartCoroutine(GenerateTextures(force));
        }

        public static void SetupLoadingIndicator(float maxProgress)
        {
            _indicatorProgress = 0;
            _indicatorMaxProgress = maxProgress;
        }

        public static void UpdateLoadingIndicator(float counter)
        {
            if (!instance)
                return;

            _indicatorProgress += counter;

            if (LoadingIndicator == null)
                return;

            if (!_indicatorInitialized)
            {
                LoadingIndicator.SetProgress(0f);
                LoadingIndicator.SetShowProgress(true);
                LoadingIndicator.SetText(_indicatorText);

                _indicatorInitialized = true;
            }

            if (_indicatorMaxProgress != 0)
                LoadingIndicator.SetProgress(Mathf.Clamp01(_indicatorProgress / _indicatorMaxProgress));
        }

        public IEnumerator GenerateTextures(bool force)
        {
            ZoneSystem sourceZone = ZoneSystem.instance;
            try
            {
                var wait = new WaitForFixedUpdate();

                yield return wait;

                Seasons.LogInfo("Setting up loading indicator");

                _indicatorProgress = 0;
                _indicatorText = "$seasons_loadscreen_preparing";

                if (!sourceZone || sourceZone != ZoneSystem.instance)
                    yield break;

                yield return _texturesVariants.InitializeCache(force);

                if (LoadingIndicator?.IsVisible == true)
                    LoadingIndicator.SetShowProgress(false);

                if (!sourceZone || sourceZone != ZoneSystem.instance)
                    yield break;

                _worker = null;
                if (_texturesVariants.Initialized())
                {
                    SeasonState.InitializeTextureControllers();
                    _texturesVariants.RequestSeason();
                }
                else
                    Seasons.LogInfo("Missing textures variants");

            }
            finally
            {
                _worker = null;
                if (LoadingIndicator)
                    LoadingIndicator.SetShowProgress(false);
            }
        }

        public void OnDestroy()
        {
            if (_worker != null)
                StopCoroutine(_worker);

            if (_instance != this)
                return;
            _instance = null;

            _indicatorInitialized = false;
            _indicatorText = "";
            _indicatorProgress = 0f;
            _indicatorMaxProgress = 0f;
        }
    }
}
