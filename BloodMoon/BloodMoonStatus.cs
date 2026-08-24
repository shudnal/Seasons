using HarmonyLib;
using System.Collections.Generic;
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

    internal static class BloodMoonStatus
    {
        internal static void EnsureRegistered(ObjectDB objectDb)
        {
            if (objectDb == null || objectDb.m_StatusEffects == null || objectDb.m_StatusEffects.Any(effect => effect != null && effect.name == SE_BloodMoon.EffectName))
                return;

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

        internal static void UpdateLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetSEMan() == null)
                return;

            bool shouldHave = BloodMoonInteractionRules.IsLocalParticipantActiveOrMarked();
            bool has = player.GetSEMan().HaveStatusEffect(SE_BloodMoon.EffectHash);
            if (shouldHave && !has)
                player.GetSEMan().AddStatusEffect(SE_BloodMoon.EffectHash);
            else if (!shouldHave && has)
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoon.EffectHash);
        }

        internal static void RemoveLocal()
        {
            Player player = Player.m_localPlayer;
            if (player?.GetSEMan() != null && player.GetSEMan().HaveStatusEffect(SE_BloodMoon.EffectHash))
                player.GetSEMan().RemoveStatusEffect(SE_BloodMoon.EffectHash);
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
