using HarmonyLib;
using System;
using System.Linq;
using System.Text;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    public class SE_Season: SE_Stats
    {
        private Season m_season = Season.Spring;

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);

            Season newSeason = seasonState.GetCurrentSeason();
            if (newSeason != m_season)
            {
                m_season = newSeason;
                UpdateShowStatus();
            }
        }

        public override void Setup(Character character)
        {
            base.Setup(character);

            m_season = seasonState.GetCurrentSeason();
            UpdateShowStatus();
        }

        public override string GetTooltipString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("{0}\n", GetSeasonTooltip());

            string statsTooltip = base.GetTooltipString();
            if (statsTooltip.Length > 0)
            {
                sb.Append(statsTooltip);
            }
            
            return sb.ToString();
        }

        public override string GetIconText()
        {
            if (seasonsTimerFormat.Value == TimerFormat.None)
                return "";
            else if (seasonsTimerFormat.Value == TimerFormat.CurrentDay)
                return seasonState.GetCurrentDay() >= seasonState.GetDaysInSeason() && !String.IsNullOrEmpty(MessageNextSeason()) ? MessageNextSeason() : Localization.instance.Localize($"$hud_mapday {seasonState.GetCurrentDay()}");

            double secondsToEndOfSeason = seasonState.GetEndOfCurrentSeason() - ZNet.instance.GetTimeSeconds();
            if (secondsToEndOfSeason <= 0d)
                return MessageNextSeason();
            
            TimeSpan span = TimeSpan.FromSeconds(secondsToEndOfSeason);
            return span.TotalHours > 24 ? string.Format("{0:d2}:{1:d2}:{2:d2}", (int)span.TotalHours, span.Minutes, span.Seconds) : span.ToString(span.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

        private string GetSeasonTooltip()
        {
            if (m_season > 0)
                return Seasons.GetSeasonTooltip(m_season);

            return "";
        }
        
        public void UpdateShowStatus()
        {
            m_name = GetSeasonName(m_season);
            m_icon = GetSeasonIcon(m_season);
        }

        public static void UpdateSeasonStatusEffectShowStatus()
        {
            if (ObjectDB.instance == null)
                return;

            SE_Season statusEffect = ObjectDB.instance.GetStatusEffect(statusEffectSeasonHash) as SE_Season;
            if (statusEffect != null)
                statusEffect.UpdateShowStatus();
        }

        private static string MessageNextSeason()
        {
            return GetSeasonIsComing(seasonState.GetNextSeason());
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
}
