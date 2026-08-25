using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal sealed class SE_BloodMoon : StatusEffect
    {
        internal const string EffectName = "SE_Seasons_BloodMoon";
        internal static readonly int EffectHash = EffectName.GetStableHashCode();

        public override string GetTooltipString()
        {
            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            if (participant == null)
                return "Blood Moon";

            if (participant.Phase == BloodMoonParticipantPhase.Marked)
            {
                return "Blood Moon — Marked\n" +
                    "Combat begins at 23:00.\n" +
                    "You cannot sleep while marked.\n" +
                    "Known combat recipes can be Blood Crafted without materials at their source crafting station.\n" +
                    "Blood Craft items disappear when you exit or at dawn.";
            }

            if (participant.GoalReached)
            {
                return "Blood Moon — Goal reached\n" +
                    "Combat progress: 100%\n" +
                    "Full Bloodlust remains active while you stay in the fight.\n" +
                    "Success is secured for this Blood Moon.\n" +
                    "You remain an active participant and can keep fighting to help others.";
            }

            float combatProgress = BloodMoonBloodlust.GetCombatProgressPercent(participant);
            string autoCompleting = BloodMoonNetwork.ClientGlobal.Phase == BloodMoonEventPhase.AutoCompleting
                ? $"\nDisplayed dawn progress: {participant.DisplayProgress:0.#}%\nAutomatic dawn progress does not increase Bloodlust or rewards."
                : string.Empty;
            return $"Blood Moon — Fighting\nCombat progress: {combatProgress:0.#}%\n" +
                "Bloodlust scales continuously with earned combat progress.\n" +
                "If your health is depleted, your Blood Moon participation ends.\n" +
                "No grave is created and no skills are lost.\n" +
                "Blood Craft items disappear when you exit." + autoCompleting;
        }

        public override string GetIconText()
        {
            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            if (participant == null)
                return string.Empty;
            if (participant.Phase == BloodMoonParticipantPhase.Marked)
                return "23:00";
            return participant.GoalReached ? "100%" : $"{BloodMoonBloodlust.GetCombatProgressPercent(participant):0}%";
        }
    }

    internal sealed class SE_BloodMoonRecovery : StatusEffect
    {
        internal const string EffectName = "SE_Seasons_BloodMoonRecovery";
        internal static readonly int EffectHash = EffectName.GetStableHashCode();

        public override string GetTooltipString()
        {
            Player player = Player.m_localPlayer;
            if (!BloodMoonRecovery.TryGetStatus(player, out bool stageOne, out float remaining, out float multiplier))
                return "Blood Moon recovery";

            string protection = stageOne ? "Full damage protection" : $"Incoming damage: {multiplier * 100f:0}%";
            return $"Blood Moon recovery\n{protection}\nRemaining: {Mathf.CeilToInt(remaining)}s";
        }

        public override string GetIconText()
        {
            return BloodMoonRecovery.TryGetStatus(Player.m_localPlayer, out _, out float remaining, out _)
                ? Mathf.CeilToInt(remaining).ToString()
                : string.Empty;
        }
    }

    internal static class BloodMoonStatus
    {
        private static Sprite bloodMoonIcon;

        internal static void EnsureRegistered(ObjectDB objectDb)
        {
            if (objectDb == null || objectDb.m_StatusEffects == null)
                return;

            EnsureIcon();

            if (!objectDb.m_StatusEffects.Any(effect => effect != null && effect.name == SE_BloodMoon.EffectName))
            {
                StatusEffect effect = ScriptableObject.CreateInstance<SE_BloodMoon>();
                effect.name = SE_BloodMoon.EffectName;
                effect.m_nameHash = SE_BloodMoon.EffectHash;
                effect.m_name = "Blood Moon";
                effect.m_tooltip = "Survive the Blood Moon.";
                effect.m_startMessage = string.Empty;
                effect.m_stopMessage = string.Empty;
                effect.m_icon = bloodMoonIcon;
                effect.m_ttl = 0f;
                objectDb.m_StatusEffects.Add(effect);
            }

            if (!objectDb.m_StatusEffects.Any(effect => effect != null && effect.name == SE_BloodMoonRecovery.EffectName))
            {
                StatusEffect recovery = ScriptableObject.CreateInstance<SE_BloodMoonRecovery>();
                recovery.name = SE_BloodMoonRecovery.EffectName;
                recovery.m_nameHash = SE_BloodMoonRecovery.EffectHash;
                recovery.m_name = "Blood Moon recovery";
                recovery.m_tooltip = "Temporary protection after Defeated.";
                recovery.m_startMessage = string.Empty;
                recovery.m_stopMessage = string.Empty;
                recovery.m_icon = bloodMoonIcon;
                recovery.m_cooldownIcon = true;
                recovery.m_ttl = 0f;
                objectDb.m_StatusEffects.Add(recovery);
            }
        }

        internal static void UpdateLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetSEMan() == null)
                return;

            bool shouldHaveBloodMoon = BloodMoonInteractionRules.IsLocalParticipantActiveOrMarked();
            bool hasBloodMoon = player.GetSEMan().HaveStatusEffect(SE_BloodMoon.EffectHash);
            if (shouldHaveBloodMoon && !hasBloodMoon)
                player.GetSEMan().AddStatusEffect(SE_BloodMoon.EffectHash);
            else if (!shouldHaveBloodMoon && hasBloodMoon)
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoon.EffectHash);

            bool shouldHaveRecovery = BloodMoonRecovery.HasProtection(player);
            bool hasRecovery = player.GetSEMan().HaveStatusEffect(SE_BloodMoonRecovery.EffectHash);
            if (shouldHaveRecovery && !hasRecovery)
                player.GetSEMan().AddStatusEffect(SE_BloodMoonRecovery.EffectHash);
            else if (!shouldHaveRecovery && hasRecovery)
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoonRecovery.EffectHash);
        }

        internal static void RemoveLocal()
        {
            Player player = Player.m_localPlayer;
            if (player?.GetSEMan() != null && player.GetSEMan().HaveStatusEffect(SE_BloodMoon.EffectHash))
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoon.EffectHash);
        }

        internal static void RemoveRecoveryLocal()
        {
            Player player = Player.m_localPlayer;
            if (player?.GetSEMan() != null && player.GetSEMan().HaveStatusEffect(SE_BloodMoonRecovery.EffectHash))
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoonRecovery.EffectHash);
        }

        private static void EnsureIcon()
        {
            if (bloodMoonIcon == null)
                Seasons.LoadIcon("blood_moon.png", ref bloodMoonIcon);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    internal static class BloodMoonObjectDbAwakePatch
    {
        private static void Postfix(ObjectDB __instance) => BloodMoonStatus.EnsureRegistered(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class BloodMoonObjectDbCopyPatch
    {
        private static void Postfix(ObjectDB __instance) => BloodMoonStatus.EnsureRegistered(__instance);
    }
}
