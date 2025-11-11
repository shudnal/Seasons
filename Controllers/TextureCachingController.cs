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

        internal static void StartCaching(SeasonalTextureVariants texturesVariants) => ZoneSystem.instance.gameObject.AddComponent<TextureCachingController>().Initialize(texturesVariants);

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

        public void Initialize(SeasonalTextureVariants texturesVariants)
        {
            _texturesVariants = texturesVariants;

            SeasonalTexturePrefabCache.SetCurrentTextureVariants(_texturesVariants);

            _worker = StartCoroutine(GenerateTextures());
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

            if (LoadingIndicator.s_instance == null)
                return;

            if (!_indicatorInitialized)
            {
                LoadingIndicator.SetProgress(0f);
                LoadingIndicator.SetProgressVisibility(visible: true);
                LoadingIndicator.SetText(_indicatorText);

                _indicatorInitialized = true;
            }

            if (_indicatorMaxProgress != 0)
                LoadingIndicator.SetProgress(Mathf.Clamp01(_indicatorProgress / _indicatorMaxProgress));
        }

        public IEnumerator GenerateTextures()
        {
            yield return null;

            Seasons.LogInfo("Setting up loading indicator");
            
            _indicatorProgress = 0;
            _indicatorText = "$seasons_loadscreen_preparing";

            yield return new WaitForSeconds(0.5f);

            yield return StartCoroutine(SeasonalTexturePrefabCache.FillWithGameData());
            
            yield return new WaitForFixedUpdate();

            if (!LoadingIndicator.IsCompletelyInvisible)
            {
                LoadingIndicator.SetProgress(1f);
                LoadingIndicator.SetText("$seasons_loadscreen_saving");
            }

            yield return new WaitForFixedUpdate();

            yield return StartCoroutine(_texturesVariants.SaveCacheOnDisk());

            yield return new WaitForFixedUpdate();

            if (!LoadingIndicator.IsCompletelyInvisible)
                LoadingIndicator.SetProgressVisibility(visible: false);
            
            yield return new WaitForFixedUpdate();

            if (_texturesVariants.Initialized())
                SeasonState.InitializeTextureControllers();
            else
                Seasons.LogInfo("Missing textures variants");

            _worker = null;
        }

        public void OnDestroy()
        {
            if (_worker != null)
                StopCoroutine(_worker);

            _instance = null;
            
            _indicatorInitialized = false;
            _indicatorText = "";
            _indicatorProgress = 0f;
            _indicatorMaxProgress = 0f;
        }
    }
}
