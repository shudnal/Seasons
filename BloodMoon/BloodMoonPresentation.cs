using System;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonPresentation
    {
        private static BloodMoonPresentationBehaviour behaviour;
        private static BloodMoonEventPhase lastPhase = BloodMoonEventPhase.Dormant;

        internal static void EnsureBehaviour()
        {
            if (behaviour != null || Seasons.instance == null)
                return;
            behaviour = Seasons.instance.gameObject.GetComponent<BloodMoonPresentationBehaviour>();
            if (behaviour == null)
                behaviour = Seasons.instance.gameObject.AddComponent<BloodMoonPresentationBehaviour>();
        }

        internal static void EnsureEnvironmentRegistered() => BloodMoonEnvironment.EnsureRegistered();

        internal static void OnGlobalSnapshot(BloodMoonGlobalSnapshot snapshot)
        {
            EnsureBehaviour();
            if (snapshot == null)
                return;

            bool forceEnvironment = snapshot.Phase == BloodMoonEventPhase.Active || snapshot.Phase == BloodMoonEventPhase.AutoCompleting ||
                snapshot.Phase == BloodMoonEventPhase.Resolving && snapshot.ResolutionStep < BloodMoonResolutionStep.RestoringWorldSystems;
            if (forceEnvironment)
                BloodMoonEnvironment.AcquireForcedEnvironment();
            else
                BloodMoonEnvironment.ReleaseForcedEnvironment();

            if (lastPhase != snapshot.Phase)
            {
                LogInfo($"[BloodMoon][event:{snapshot.EventId}][phase] Presentation {lastPhase} -> {snapshot.Phase}.");
                TriggerPhaseHook(snapshot.Phase);
                lastPhase = snapshot.Phase;
            }
            BloodMoonStatus.UpdateLocal();
        }

        internal static void Tick(float dt)
        {
            EnsureBehaviour();
            BloodMoonStatus.UpdateLocal();
            if (behaviour != null)
                behaviour.VisualFactor = BloodMoonEnvironment.GetVisualFactor();
        }

        internal static void OnWorldChanged()
        {
            CleanupTransientState();
            BloodMoonEnvironment.CleanupRegistration();
            BloodMoonEnvironment.EnsureRegistered();
        }

        internal static void OnEnrolled()
        {
            BloodMoonStatus.UpdateLocal();
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "The Blood Moon has marked you.");
        }

        internal static void SetResolutionFade(bool begin)
        {
            EnsureBehaviour();
            if (behaviour != null)
                behaviour.SetFade(begin ? 1f : 0f);
        }

        internal static void OnResolutionComplete()
        {
            BloodMoonStatus.RemoveLocal();
            BloodMoonEnvironment.ReleaseForcedEnvironment();
            if (behaviour != null)
            {
                behaviour.VisualFactor = 0f;
                behaviour.SetFade(0f);
            }
        }

        internal static string BuildChronicle(BloodMoonParticipantState participant)
        {
            if (participant == null)
                return string.Empty;
            string outcome = participant.ExitReason switch
            {
                BloodMoonParticipantExitReason.Defeated => participant.GoalReached ? "Success, later defeated" : "Defeated",
                BloodMoonParticipantExitReason.Withdrawn => participant.GoalReached ? "Success, later withdrawn" : "Withdrawn",
                BloodMoonParticipantExitReason.Disconnected => participant.GoalReached ? "Success, later disconnected" : "Disconnected",
                _ when participant.GoalReached => "Success",
                _ when participant.AutoCompleted => "Survived until dawn",
                _ => "Survived"
            };
            float combatProgress = Mathf.Clamp(participant.CombatPoints / Mathf.Max(1f, BloodMoonConfig.GoalPoints.Value) * 100f, 0f, 100f);
            return $"Blood Moon — {outcome}\nCombat progress: {combatProgress:0.#}%\nDisplayed progress: {participant.DisplayProgress:0.#}%\nCombat points: {participant.CombatPoints:0.##}";
        }

        internal static void PublishChronicle(string chronicle)
        {
            Player player = Player.m_localPlayer;
            if (player == null || string.IsNullOrWhiteSpace(chronicle))
                return;

            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (eventId < 0L && BloodMoonController.Instance?.State != null)
                eventId = BloodMoonController.Instance.State.EventId;
            string key = eventId >= 0L ? $"Blood Moon {eventId}" : "Blood Moon";

            if (!player.m_knownTexts.TryGetValue(key, out string existing) || !string.Equals(existing, chronicle, StringComparison.Ordinal))
                player.AddKnownText(key, chronicle);
            player.Message(MessageHud.MessageType.Center, chronicle);
        }

        internal static void CleanupTransientState()
        {
            lastPhase = BloodMoonEventPhase.Dormant;
            BloodMoonStatus.RemoveLocal();
            if (behaviour != null)
            {
                behaviour.VisualFactor = 0f;
                behaviour.SetFade(0f);
            }
        }

        internal static void Cleanup()
        {
            CleanupTransientState();
            BloodMoonEnvironment.CleanupRegistration();
            if (behaviour != null)
            {
                UnityEngine.Object.Destroy(behaviour);
                behaviour = null;
            }
        }

        private static void TriggerPhaseHook(BloodMoonEventPhase phase)
        {
            if (BloodMoonConfig.MusicEnabled == null || !BloodMoonConfig.MusicEnabled.Value || MusicMan.instance == null)
                return;
            // Track names are intentionally absent until owner-provided Blood Moon music assets exist.
            if (phase == BloodMoonEventPhase.Forewarning || phase == BloodMoonEventPhase.Marked || phase == BloodMoonEventPhase.Active || phase == BloodMoonEventPhase.Resolving)
                LogInfo($"[BloodMoon.Music] Hook: {phase}.");
        }
    }

    internal sealed class BloodMoonPresentationBehaviour : MonoBehaviour
    {
        internal float VisualFactor;
        private float fadeTarget;
        private float fadeAlpha;

        internal void SetFade(float target)
        {
            fadeTarget = Mathf.Clamp01(target);
        }

        private void Update()
        {
            fadeAlpha = Mathf.MoveTowards(fadeAlpha, fadeTarget, Time.unscaledDeltaTime * 1.5f);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (VisualFactor > 0f && BloodMoonNetwork.ClientGlobal.Phase != BloodMoonEventPhase.Active && BloodMoonNetwork.ClientGlobal.Phase != BloodMoonEventPhase.AutoCompleting)
            {
                Color old = GUI.color;
                GUI.color = new Color(0.55f, 0f, 0f, Mathf.Clamp01(VisualFactor) * 0.16f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = old;
            }

            if (fadeAlpha > 0.001f)
            {
                Color old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }
    }
}
