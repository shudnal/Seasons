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
                LogInfo($"[BloodMoon.Presentation] Phase {lastPhase} -> {snapshot.Phase}.");
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
            lastPhase = BloodMoonEventPhase.Dormant;
            BloodMoonEnvironment.CleanupRegistration();
            BloodMoonEnvironment.EnsureRegistered();
            BloodMoonStatus.RemoveLocal();
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
                behaviour.SetFade(0f);
        }

        internal static string BuildChronicle(BloodMoonParticipantState participant)
        {
            if (participant == null)
                return string.Empty;
            string outcome = participant.ExitReason switch
            {
                BloodMoonParticipantExitReason.Defeated => "Defeated",
                BloodMoonParticipantExitReason.Withdrawn => "Withdrawn",
                BloodMoonParticipantExitReason.Disconnected => "Disconnected",
                _ when participant.GoalReached => "Success",
                _ when participant.AutoCompleted => "Survived until dawn",
                _ => "Survived"
            };
            return $"Blood Moon — {outcome}\nCombat progress: {participant.DisplayProgress:0.#}%\nCombat points: {participant.CombatPoints:0.##}";
        }

        internal static void PublishChronicle(string chronicle)
        {
            Player player = Player.m_localPlayer;
            if (player == null || string.IsNullOrWhiteSpace(chronicle))
                return;
            string key = $"Blood Moon {DateTime.Now:yyyy-MM-dd HH:mm}";
            player.AddKnownText(key, chronicle);
            player.Message(MessageHud.MessageType.Center, chronicle);
        }

        internal static void Cleanup()
        {
            BloodMoonStatus.RemoveLocal();
            BloodMoonEnvironment.CleanupRegistration();
            if (behaviour != null)
            {
                UnityEngine.Object.Destroy(behaviour);
                behaviour = null;
            }
        }

        private static void TriggerPhaseHook(BloodMoonEventPhase phase)
        {
            if (!BloodMoonConfig.MusicEnabled.Value || MusicMan.instance == null)
                return;
            // Intentionally no track names here. The hook remains stable for owner-provided music assets.
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
