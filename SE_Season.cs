using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static Seasons.Seasons;
using static Seasons.SeasonStats;

namespace Seasons
{
    public class SE_Season: SE_Stats
    {
        private Season m_season = Season.Spring;
        private bool m_indoors = false;

        [Header("Skills modifiers")]
        public Dictionary<Skills.SkillType, float> m_customRaiseSkills = new Dictionary<Skills.SkillType, float>();
        public Dictionary<Skills.SkillType, float> m_customSkillLevels = new Dictionary<Skills.SkillType, float>();
        public Dictionary<Skills.SkillType, float> m_customModifyAttackSkills = new Dictionary<Skills.SkillType, float>();

        private static readonly StringBuilder _sb = new StringBuilder(100);
        private static readonly Stats emptyStats = new Stats();

        public override void UpdateStatusEffect(float dt)
        {
            if (m_season != seasonState.GetCurrentSeason())
                Setup(m_character);
            else if (seasonalStatsOutdoorsOnly.Value && m_character != null && m_character == Player.m_localPlayer && m_character.InInterior() != m_indoors)
                Setup(m_character);
            else
                base.UpdateStatusEffect(dt);
        }

        public override void Setup(Character character)
        {
            m_season = seasonState.GetCurrentSeason();
            m_indoors = m_character != null && m_character == Player.m_localPlayer && m_character.InInterior();

            UpdateSeasonStatusEffect();

            base.Setup(character);
        }

        public override string GetTooltipString()
        {
            if (!showCurrentSeasonInRaven.Value)
                return "";

            _sb.Clear();
            _sb.AppendFormat("{0}\n", GetSeasonTooltip());
            
            if (seasonsTimerFormatInRaven.Value == TimerFormatRaven.CurrentDay || seasonsTimerFormatInRaven.Value == TimerFormatRaven.CurrentDayAndTimeToEnd)
                _sb.AppendFormat("{0} / {1}\n", Localization.instance.Localize($"$hud_mapday {seasonState.GetCurrentDay()}"), seasonState.GetDaysInSeason());

            if (seasonsTimerFormatInRaven.Value == TimerFormatRaven.TimeToEnd || seasonsTimerFormatInRaven.Value == TimerFormatRaven.CurrentDayAndTimeToEnd)
                _sb.AppendFormat("{0}: {1}\n", MessageNextSeason(), TimerString(seasonState.GetEndOfCurrentSeason() - seasonState.GetTotalSeconds()));

            string statsTooltip = base.GetTooltipString();
            if (statsTooltip.Length > 0)
                _sb.Append(statsTooltip);

            foreach (KeyValuePair<Skills.SkillType, float> item in m_customSkillLevels.Where(kvp => kvp.Value != 0f))
                _sb.AppendFormat("{0} <color=orange>{1}</color>\n", SkillLocalized(item.Key), item.Value.ToString("+0;-0"));

            foreach (KeyValuePair<Skills.SkillType, float> item in m_customModifyAttackSkills.Where(kvp => kvp.Value != 0f))
                _sb.AppendFormat("$inventory_dmgmod: {0} <color=orange>{1}%</color>\n", SkillLocalized(item.Key), item.Value.ToString("+0;-0"));

            _sb.Append("\n");
            return _sb.ToString();

            static string SkillLocalized(Skills.SkillType skill)
            {
                return Localization.instance.Localize(skill == Skills.SkillType.All ? "$inventory_skills" : "$skill_" + skill.ToString().ToLower());
            }
        }

        public override string GetIconText()
        {
            if (seasonsTimerFormat.Value == TimerFormat.None)
                return "";
            else if (seasonsTimerFormat.Value == TimerFormat.CurrentDay)
                return seasonState.GetCurrentDay() >= seasonState.GetDaysInSeason() && !String.IsNullOrEmpty(MessageNextSeason()) ? MessageNextSeason() : Localization.instance.Localize($"$hud_mapday {seasonState.GetCurrentDay()}");

            double secondsToEndOfSeason = seasonState.GetEndOfCurrentSeason() - seasonState.GetTotalSeconds();
            if (secondsToEndOfSeason <= 0d)
                return MessageNextSeason();
            else
                return TimerString(secondsToEndOfSeason);
        }
        
        public override void ModifyRaiseSkill(Skills.SkillType skill, ref float value)
        {
            if (m_customRaiseSkills.ContainsKey(skill))
                value += m_customRaiseSkills[skill];
            else if (m_customRaiseSkills.ContainsKey(Skills.SkillType.All))
                value += m_customRaiseSkills[Skills.SkillType.All];
        }

        public override void ModifySkillLevel(Skills.SkillType skill, ref float value)
        {
            if (m_customSkillLevels.ContainsKey(skill))
                value += m_customSkillLevels[skill];
            else if (m_customSkillLevels.ContainsKey(Skills.SkillType.All))
                value += m_customSkillLevels[Skills.SkillType.All];
        }

        public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
        {
            if (m_customModifyAttackSkills.ContainsKey(skill))
                hitData.m_damage.Modify(m_customModifyAttackSkills[skill]);
            else if (m_customModifyAttackSkills.ContainsKey(Skills.SkillType.All))
                hitData.m_damage.Modify(m_customModifyAttackSkills[Skills.SkillType.All]);
        }

        private string GetSeasonTooltip()
        {
            return Seasons.GetSeasonTooltip(m_season);
        }
        
        public void UpdateSeasonStatusEffect()
        {
            m_name = GetSeasonName(m_season);
            m_icon = GetSeasonIcon(m_season);

            Stats statsToSet = !controlStats.Value || seasonalStatsOutdoorsOnly.Value && m_indoors ? emptyStats : SeasonState.seasonStats.GetSeasonStats();
            statsToSet.SetStatusEffectStats(this);
        }

        public static void UpdateSeasonStatusEffectStats()
        {
            if (ObjectDB.instance == null)
                return;

            if (Player.m_localPlayer == null)
                return;

            (Player.m_localPlayer.GetSEMan().GetStatusEffect(statusEffectSeasonHash) as SE_Season)?.Setup(Player.m_localPlayer);
        }

        private static string MessageNextSeason()
        {
            return GetSeasonIsComing(seasonState.GetNextSeason());
        }
    
        private static string TimerString(double secondsToEndOfSeason)
        {
            if (secondsToEndOfSeason < 60)
                return DateTime.FromBinary(599266080000000000).AddSeconds(secondsToEndOfSeason).ToString(@"ss\s");

            TimeSpan span = TimeSpan.FromSeconds(secondsToEndOfSeason);
            if (hideSecondsInTimer.Value)
                if (span.Hours > 0)
                    return $"{(int)span.TotalHours}{new DateTime(span.Ticks).ToString(@"\h mm\m")}";
                else
                    return new DateTime(span.Ticks).ToString(@"mm\m ss\s");
            else
                if (span.TotalHours > 24)
                    return string.Format("{0:d2}:{1:d2}:{2:d2}", (int)span.TotalHours, span.Minutes, span.Seconds);
                else
                    return span.ToString(span.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    public static class ObjectDB_Awake_AddStatusEffects
    {
        public static void AddCustomStatusEffects(ObjectDB odb)
        {
            if (odb.m_StatusEffects.Count > 0)
            {
                if (!odb.m_StatusEffects.Any(se => se.name == statusEffectSeasonName))
                {
                    SE_Season seasonEffect = ScriptableObject.CreateInstance<SE_Season>();
                    seasonEffect.name = statusEffectSeasonName;
                    seasonEffect.m_nameHash = statusEffectSeasonHash;
                    seasonEffect.m_icon = iconSpring;

                    odb.m_StatusEffects.Add(seasonEffect);
                }

                SE_Stats warm = odb.m_StatusEffects.Find(se => se.name == "Warm") as SE_Stats;
                if (warm != null && !odb.m_StatusEffects.Any(se => se.name == statusEffectOverheatName))
                {
                    SE_Stats overheat = ScriptableObject.CreateInstance<SE_Stats>();
                    overheat.name = statusEffectOverheatName;
                    overheat.m_nameHash = statusEffectOverheatHash;
                    overheat.m_icon = warm.m_icon;
                    overheat.m_name = warm.m_name;
                    overheat.m_tooltip = warm.m_tooltip;
                    overheat.m_startMessage = warm.m_startMessage;
                    overheat.m_staminaRegenMultiplier = 0.8f;
                    overheat.m_eitrRegenMultiplier = 0.8f;

                    odb.m_StatusEffects.Add(overheat);
                }
            }
        }

        private static void Postfix(ObjectDB __instance)
        {
            AddCustomStatusEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    public static class ObjectDB_CopyOtherDB_SE_Season
    {
        private static void Postfix(ObjectDB __instance)
        {
            ObjectDB_Awake_AddStatusEffects.AddCustomStatusEffects(__instance);
        }
    }

    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.AddActiveEffects))]
    public static class TextsDialog_AddActiveEffects_SeasonTooltipWhenBuffDisabled
    {
        public static bool isActiveEffectsListCall = false;

        private static void Prefix()
        {
            isActiveEffectsListCall = true;
        }

        private static void Postfix()
        {
            isActiveEffectsListCall = false;
        }
    }

    [HarmonyPatch(typeof(SEMan), nameof(SEMan.GetHUDStatusEffects))]
    public static class SEMan_GetHUDStatusEffects_SeasonTooltipWhenBuffDisabled
    {
        private static void Postfix(Character ___m_character, List<StatusEffect> ___m_statusEffects, List<StatusEffect> effects)
        {
            if (TextsDialog_AddActiveEffects_SeasonTooltipWhenBuffDisabled.isActiveEffectsListCall && Player.m_localPlayer != null && ___m_character == Player.m_localPlayer && !effects.Any(effect => effect is SE_Season))
            {
                StatusEffect seasonStatusEffect = ___m_statusEffects.Find(se => se is SE_Season);
                if (seasonStatusEffect != null)
                    effects.Insert(0, seasonStatusEffect);
            }
        }
    }

}
