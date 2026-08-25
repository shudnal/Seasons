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

        internal static bool IsPresented(Player player, long eventId)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            return player != null && worldUid != 0L && eventId >= 0L && player.m_customData.ContainsKey(GetKey(worldUid, eventId));
        }

        internal static bool Present(Player player, long eventId, string chronicle)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            if (player == null || player != Player.m_localPlayer || worldUid == 0L || eventId < 0L || string.IsNullOrWhiteSpace(chronicle))
                return false;

            long playerId = player.GetPlayerID();
            if (IsPresented(player, eventId))
            {
                BloodMoonOutcomePresentationHandshake.NotifyCompleted(worldUid, eventId, playerId);
                return true;
            }
            if (activePresenter != null)
            {
                if (activePresenter.Matches(worldUid, eventId, playerId))
                {
                    BloodMoonOutcomePresentationHandshake.NotifyStarted(worldUid, eventId, playerId);
                    return true;
                }

                // OutcomeQueue retries pending outcomes. Never destroy another event's active presenter:
                // doing so would prevent its completion marker/ACK and could make multiple queued results
                // continually cancel each other. The next retry starts this event after the current one ends.
                return false;
            }

            string text = SelectDreamText(chronicle);
            if (string.IsNullOrWhiteSpace(text))
            {
                LogWarning($"[BloodMoon.Outcome] No DreamText variant matched event {eventId}; outcome remains pending.");
                return false;
            }

            try
            {
                if (!TryCreatePresenter(worldUid, eventId, playerId, text, out BloodMoonDreamPresenter presenter))
                    return false;

                activePresenter = presenter;
                BloodMoonOutcomePresentationHandshake.NotifyStarted(worldUid, eventId, playerId);
                LogInfo($"[BloodMoon.Outcome] Started DreamText presentation for event {eventId} through the current outcome path.");
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

        internal static void OnPresenterDestroyed(BloodMoonDreamPresenter presenter, bool completed, long worldUid, long eventId, long playerId)
        {
            if (ReferenceEquals(activePresenter, presenter))
                activePresenter = null;
            if (!completed)
                return;

            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != playerId || ZNet.m_world == null || ZNet.m_world.m_uid != worldUid)
                return;

            player.m_customData[GetKey(worldUid, eventId)] = "1";
            BloodMoonOutcomeQueue.OnLocalDreamPresentationCompleted(worldUid, eventId, playerId);
            BloodMoonOutcomePresentationHandshake.NotifyCompleted(worldUid, eventId, playerId);
            LogInfo($"[BloodMoon.Outcome] DreamText presentation completed for event {eventId}.");
        }

        private static bool TryCreatePresenter(long worldUid, long eventId, long playerId, string text, out BloodMoonDreamPresenter presenter)
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
            presenter.Initialize(worldUid, eventId, playerId, sleepText.m_dreamField, background);
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
        private const float FadeInSeconds = 1f;
        private const float HoldSeconds = 3f;
        private const float FadeOutSeconds = 1f;

        private TMPro.TMP_Text dreamField;
        private Image background;
        private float elapsed;
        private bool initialized;
        private bool released;
        private bool completed;
        private long worldUid;
        private long eventId;
        private long playerId;

        internal void Initialize(long currentWorldUid, long currentEventId, long currentPlayerId, TMPro.TMP_Text field, Image backgroundImage)
        {
            worldUid = currentWorldUid;
            eventId = currentEventId;
            playerId = currentPlayerId;
            dreamField = field;
            background = backgroundImage;
            initialized = dreamField != null && background != null;
            if (!initialized)
                throw new InvalidOperationException("DreamText presenter dependencies are missing.");

            BloodMoonFadeInputGuard.AcquireDream();
            BloodMoonPresentation.SetDreamOverlayActive(true);
            dreamField.CrossFadeAlpha(1f, FadeInSeconds, ignoreTimeScale: true);
        }

        internal bool Matches(long expectedWorldUid, long expectedEventId, long expectedPlayerId)
        {
            return initialized && worldUid == expectedWorldUid && eventId == expectedEventId && playerId == expectedPlayerId;
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
            {
                completed = true;
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            ReleaseGuards();
            BloodMoonDreams.OnPresenterDestroyed(this, completed, worldUid, eventId, playerId);
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
