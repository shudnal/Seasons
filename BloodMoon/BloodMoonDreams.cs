using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDreams
    {
        private const string PresentedPrefix = "Seasons.BloodMoon.DreamPresented.";
        private static BloodMoonDreamPresenter activePresenter;

        internal static bool Present(Player player, long eventId, string chronicle)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (player == null || player != Player.m_localPlayer || worldUid == 0L || eventId < 0L || string.IsNullOrWhiteSpace(chronicle))
                return false;

            string markerKey = GetKey(worldUid, eventId);
            if (player.m_customData.ContainsKey(markerKey))
                return true;

            string text = SelectDreamText(chronicle);
            if (string.IsNullOrWhiteSpace(text))
            {
                LogWarning($"[BloodMoon.Outcome] No DreamText variant matched event {eventId}; outcome remains pending.");
                return false;
            }

            try
            {
                if (!TryCreatePresenter(eventId, text, out BloodMoonDreamPresenter presenter))
                    return false;

                activePresenter = presenter;
                player.m_customData[markerKey] = "1";
                LogInfo($"[BloodMoon.Outcome] Presented DreamText for event {eventId} through the current outcome path.");
                return true;
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Outcome] DreamText presentation failed for event {eventId}: {ex.Message}");
                CleanupTransientPresentation();
                return false;
            }
        }

        internal static void CleanupTransientPresentation()
        {
            if (activePresenter != null)
                UnityEngine.Object.Destroy(activePresenter.gameObject);
            activePresenter = null;
            BloodMoonPresentation.SetDreamOverlayActive(false);
        }

        internal static void OnPresenterDestroyed(BloodMoonDreamPresenter presenter)
        {
            if (ReferenceEquals(activePresenter, presenter))
                activePresenter = null;
        }

        private static bool TryCreatePresenter(long eventId, string text, out BloodMoonDreamPresenter presenter)
        {
            presenter = null;
            Hud hud = Hud.instance;
            GameObject source = hud?.m_sleepingProgress;
            if (source == null || source.transform.parent == null)
            {
                LogWarning($"[BloodMoon.Outcome] Native sleep DreamText UI is not available for event {eventId}; delivery will retry.");
                return false;
            }

            GameObject clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
            clone.name = "SeasonsBloodMoonDreamText";
            clone.SetActive(false);
            clone.transform.SetAsLastSibling();

            SleepText sleepText = clone.GetComponentInChildren<SleepText>(includeInactive: true);
            if (sleepText == null || sleepText.m_dreamField == null)
            {
                UnityEngine.Object.Destroy(clone);
                LogWarning($"[BloodMoon.Outcome] Native SleepText fields are unavailable for event {eventId}; delivery will retry.");
                return false;
            }

            sleepText.CancelInvoke();
            sleepText.enabled = false;
            if (sleepText.m_textField != null)
                sleepText.m_textField.gameObject.SetActive(false);

            string localized = Localization.instance != null ? Localization.instance.Localize(text) : text;
            sleepText.m_dreamField.text = localized;
            sleepText.m_dreamField.enabled = true;
            sleepText.m_dreamField.CrossFadeAlpha(0f, 0f, ignoreTimeScale: true);

            GameObject backgroundObject = new GameObject("BloodMoonDreamBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.transform.SetParent(clone.transform, worldPositionStays: false);
            backgroundObject.transform.SetAsFirstSibling();
            RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = backgroundObject.GetComponent<Image>();
            background.color = Color.black;
            background.raycastTarget = false;

            presenter = clone.AddComponent<BloodMoonDreamPresenter>();
            presenter.Initialize(eventId, sleepText.m_dreamField, background);
            clone.SetActive(true);
            return true;
        }

        private static string SelectDreamText(string chronicle)
        {
            if (chronicle.Contains("Success, later defeated"))
                return "You dream of reaching the end of a crimson road, though the night takes its final toll.";
            if (chronicle.Contains("Success, later withdrawn"))
                return "You dream of standing beneath the fading red moon, then choosing a road away from its last shadows.";
            if (chronicle.Contains("Success, later disconnected"))
                return "You dream of surviving the crimson night before the memory breaks apart into silence.";
            if (chronicle.Contains("— Success"))
                return "You dream of a red moon sinking beyond the horizon, and of standing when its light finally fades.";
            if (chronicle.Contains("— Defeated"))
                return "You dream of red light closing over you, then waking before the final darkness.";
            if (chronicle.Contains("— Withdrawn"))
                return "You dream of turning from a blood-red horizon while distant horns fade behind you.";
            if (chronicle.Contains("— Disconnected"))
                return "You dream of a crimson night that breaks apart before its ending can be seen.";
            if (chronicle.Contains("Survived until dawn"))
                return "You dream of the red moon paling into dawn while the last shadows retreat.";
            if (chronicle.Contains("— Survived"))
                return "You dream of a long crimson night finally giving way to morning.";
            return string.Empty;
        }

        private static string GetKey(long worldUid, long eventId)
        {
            return PresentedPrefix + worldUid.ToString(CultureInfo.InvariantCulture) + "." + eventId.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class BloodMoonDreamPresenter : MonoBehaviour
    {
        private const float FadeInSeconds = 1.5f;
        private const float HoldSeconds = 4.5f;
        private const float FadeOutSeconds = 1.5f;

        private TMPro.TMP_Text dreamField;
        private Image background;
        private float elapsed;
        private bool initialized;
        private bool released;
        private long eventId;

        internal void Initialize(long currentEventId, TMPro.TMP_Text field, Image backgroundImage)
        {
            eventId = currentEventId;
            dreamField = field;
            background = backgroundImage;
            initialized = dreamField != null && background != null;
            if (!initialized)
                throw new InvalidOperationException("DreamText presenter dependencies are missing.");

            BloodMoonFadeInputGuard.AcquireDream();
            BloodMoonPresentation.SetDreamOverlayActive(true);
            dreamField.CrossFadeAlpha(1f, FadeInSeconds, ignoreTimeScale: true);
        }

        private void Update()
        {
            if (!initialized)
                return;

            elapsed += Time.unscaledDeltaTime;
            float fadeOutAt = FadeInSeconds + HoldSeconds;
            if (elapsed >= fadeOutAt && elapsed - Time.unscaledDeltaTime < fadeOutAt)
            {
                dreamField.CrossFadeAlpha(0f, FadeOutSeconds, ignoreTimeScale: true);
                background.CrossFadeAlpha(0f, FadeOutSeconds, ignoreTimeScale: true);
            }

            if (elapsed >= fadeOutAt + FadeOutSeconds)
                UnityEngine.Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            ReleaseGuards();
            BloodMoonDreams.OnPresenterDestroyed(this);
            if (initialized)
                LogInfo($"[BloodMoon.Outcome] DreamText presentation completed for event {eventId}.");
        }

        private void ReleaseGuards()
        {
            if (released)
                return;
            released = true;
            BloodMoonPresentation.SetDreamOverlayActive(false);
            BloodMoonFadeInputGuard.ReleaseDream();
        }
    }
}
