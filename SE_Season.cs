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
                m_name = m_season.ToString();
            }
        }

        public override void Setup(Character character)
        {
            base.Setup(character);

            m_season = seasonState.GetCurrentSeason();
            m_name = m_season.ToString();
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
                return seasonState.GetCurrentDay() == daysInSeason.Value && !String.IsNullOrEmpty(MessageNextSeason()) ? MessageNextSeason() : Localization.instance.Localize($"$hud_mapday {seasonState.GetCurrentDay()}");

            long startOfSeason = daysInSeason.Value * EnvMan.instance.m_dayLengthSec;
            TimeSpan span = TimeSpan.FromSeconds(startOfSeason - ZNet.instance.GetTimeSeconds() % startOfSeason);
            if (span <= TimeSpan.Zero && !String.IsNullOrEmpty(MessageNextSeason()))
                return MessageNextSeason();

            return span.TotalHours > 24 ? string.Format("{0:d2}:{1:d2}:{2:d2}", (int)span.TotalHours, span.Minutes, span.Seconds) : span.ToString(span.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

        private string GetSeasonTooltip()
        {
            if (m_season > 0)
                return String.Format(messageSeasonTooltip.Value, messageSeasonTooltip.Value.StartsWith("{0}") ? m_season.ToString() : m_season.ToString().ToLower());

            return "";
        }
        private static string MessageNextSeason()
        {
            return String.Format(messageSeasonIsComing.Value, messageSeasonIsComing.Value.StartsWith("{0}") ? seasonState.GetNextSeason().ToString() : seasonState.GetNextSeason().ToString().ToLower());
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    public static class ObjectDB_Awake_SE_Season
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (__instance.m_StatusEffects.Count > 0 && !__instance.m_StatusEffects.Any(se => se.m_name == statusEffectSeasonName))
            {
                SE_Season seasonEffect = ScriptableObject.CreateInstance<SE_Season>();
                seasonEffect.name = statusEffectSeasonName;
                seasonEffect.m_nameHash = statusEffectSeasonHash;
                seasonEffect.m_icon = icon;

                __instance.m_StatusEffects.Add(seasonEffect);
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    public static class ObjectDB_CopyOtherDB_SE_Season
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (__instance.m_StatusEffects.Count > 0 && !__instance.m_StatusEffects.Any(se => se.m_name == statusEffectSeasonName))
            {
                SE_Season seasonEffect = ScriptableObject.CreateInstance<SE_Season>();
                seasonEffect.name = statusEffectSeasonName;
                seasonEffect.m_nameHash = statusEffectSeasonHash;
                seasonEffect.m_icon = icon;

                __instance.m_StatusEffects.Add(seasonEffect);
            }
        }
    }

}
