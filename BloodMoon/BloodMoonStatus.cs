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

            string state = participant.GoalReached ? "Goal reached" : participant.Phase.ToString();
            return $"Blood Moon\n{state}\nCombat progress: {participant.DisplayProgress:0.#}%";
        }

        public override string GetIconText()
        {
            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            return participant == null ? string.Empty : $"{participant.DisplayProgress:0}%";
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
        internal static void EnsureRegistered(ObjectDB objectDb)
        {
            if (objectDb == null || objectDb.m_StatusEffects == null)
                return;

            if (!objectDb.m_StatusEffects.Any(effect => effect != null && effect.name == SE_BloodMoon.EffectName))
            {
                StatusEffect effect = ScriptableObject.CreateInstance<SE_BloodMoon>();
                effect.name = SE_BloodMoon.EffectName;
                effect.m_nameHash = SE_BloodMoon.EffectHash;
                effect.m_name = "Blood Moon";
                effect.m_tooltip = "Survive the Blood Moon.";
                effect.m_startMessage = "The Blood Moon has marked you.";
                effect.m_startMessageType = MessageHud.MessageType.Center;
                effect.m_stopMessage = string.Empty;
                effect.m_icon = Seasons.iconFall;
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
                recovery.m_icon = Seasons.iconFall;
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
