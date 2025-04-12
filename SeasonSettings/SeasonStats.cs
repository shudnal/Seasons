using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonStats
    {
        [Serializable]
        public class Stats
        {
            [Header("__SE_Stats__")]
            [Header("HP per tick")]
            public float m_tickInterval;

            public float m_healthPerTickMinHealthPercentage;

            public float m_healthPerTick;

            public string m_healthHitType = "";
            
            [Header("Stamina")]
            public float m_staminaDrainPerSec;

            public float m_runStaminaDrainModifier;

            public float m_jumpStaminaUseModifier;

            [Header("Regen modifiers")]
            public float m_healthRegenMultiplier = 1f;

            public float m_staminaRegenMultiplier = 1f;

            public float m_eitrRegenMultiplier = 1f;

            [Header("Skills modifiers")]
            public Dictionary<string, float> m_raiseSkills = new Dictionary<string, float>();
            public Dictionary<string, float> m_skillLevels = new Dictionary<string, float>();
            public Dictionary<string, float> m_modifyAttackSkills = new Dictionary<string, float>();

            [Header("Hit modifier")]
            public Dictionary<string, string> m_damageModifiers = new Dictionary<string, string>();

            [Header("Sneak")]
            public float m_noiseModifier;

            public float m_stealthModifier;

            [Header("Carry weight")]
            public float m_addMaxCarryWeight;

            [Header("Speed")]
            public float m_speedModifier;

            [Header("Fall")]
            public float m_maxMaxFallSpeed;

            public float m_fallDamageModifier;

            public void SetStatusEffectStats(SE_Season statusEffect)
            {
                foreach (FieldInfo property in GetType().GetFields())
                {
                    FieldInfo field = statusEffect.GetType().GetField(property.Name);
                    if (field == null)
                        continue;
                        
                    field.SetValue(statusEffect, property.GetValue(this));
                }

                statusEffect.m_mods.Clear();
                foreach (KeyValuePair<string, string> damageMod in m_damageModifiers)
                    if (Enum.TryParse(damageMod.Key, out HitData.DamageType m_type) && Enum.TryParse(damageMod.Value, out HitData.DamageModifier m_modifier))
                        statusEffect.m_mods.Add(new HitData.DamageModPair() { m_type = m_type, m_modifier = m_modifier });

                statusEffect.m_hitType = HitData.HitType.Undefined;
                if (Enum.TryParse(m_healthHitType, out HitData.HitType hitType))
                    statusEffect.m_hitType = hitType;

                statusEffect.m_customRaiseSkills.Clear();
                foreach (KeyValuePair<string, float> skillPair in m_raiseSkills)
                    if (ParseSkill(skillPair.Key, out Skills.SkillType skill))
                        statusEffect.m_customRaiseSkills.Add(skill, skillPair.Value);

                statusEffect.m_customSkillLevels.Clear();
                foreach (KeyValuePair<string, float> skillPair in m_skillLevels)
                    if (ParseSkill(skillPair.Key, out Skills.SkillType skill))
                        statusEffect.m_customSkillLevels.Add(skill, skillPair.Value);

                statusEffect.m_customModifyAttackSkills.Clear();
                foreach (KeyValuePair<string, float> skillPair in m_modifyAttackSkills)
                    if (ParseSkill(skillPair.Key, out Skills.SkillType skill))
                        statusEffect.m_customModifyAttackSkills.Add(skill, skillPair.Value);
            }

            public bool ParseSkill(string skillName, out Skills.SkillType skill)
            {
                if (Enum.TryParse(skillName, out skill))
                    return true;

                Skills.SkillType fromSkillManager = (Skills.SkillType)Math.Abs(skillName.GetStableHashCode());
                if (Player.m_localPlayer.m_skills.m_skills.Any(skl => skl.m_skill == fromSkillManager))
                {
                    skill = fromSkillManager;
                    return true;
                }

                return false;
            }
        }

        public Stats Spring = new Stats();
        public Stats Summer = new Stats();
        public Stats Fall = new Stats();
        public Stats Winter = new Stats();

        public SeasonStats(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.m_tickInterval = 5f;
            Spring.m_healthPerTick = 1f;
            Spring.m_damageModifiers.Add(HitData.DamageType.Poison.ToString(), HitData.DamageModifier.Resistant.ToString());

            Spring.m_raiseSkills.Add(Skills.SkillType.Jump.ToString(), 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Sneak.ToString(), 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Run.ToString(), 1.2f);
            Spring.m_raiseSkills.Add(Skills.SkillType.Swim.ToString(), 1.2f);
            Spring.m_skillLevels.Add(Skills.SkillType.Jump.ToString(), 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Sneak.ToString(), 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Run.ToString(), 15f);
            Spring.m_skillLevels.Add(Skills.SkillType.Swim.ToString(), 15f);

            Summer.m_runStaminaDrainModifier = -0.1f;
            Summer.m_jumpStaminaUseModifier = -0.1f;
            Summer.m_healthRegenMultiplier = 1.1f;
            Summer.m_noiseModifier = -0.2f;
            Summer.m_stealthModifier = 0.2f;
            Summer.m_speedModifier = 0.05f;

            Summer.m_raiseSkills.Add(Skills.SkillType.Jump.ToString(), 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Sneak.ToString(), 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Run.ToString(), 1.1f);
            Summer.m_raiseSkills.Add(Skills.SkillType.Swim.ToString(), 1.1f);
            Summer.m_skillLevels.Add(Skills.SkillType.Jump.ToString(), 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Sneak.ToString(), 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Run.ToString(), 10f);
            Summer.m_skillLevels.Add(Skills.SkillType.Swim.ToString(), 10f);

            Fall.m_eitrRegenMultiplier = 1.1f;
            Fall.m_raiseSkills.Add(Skills.SkillType.WoodCutting.ToString(), 1.2f);
            Fall.m_raiseSkills.Add(Skills.SkillType.Fishing.ToString(), 1.2f);
            Fall.m_raiseSkills.Add(Skills.SkillType.Pickaxes.ToString(), 1.2f);
            Fall.m_skillLevels.Add(Skills.SkillType.WoodCutting.ToString(), 15f);
            Fall.m_skillLevels.Add(Skills.SkillType.Fishing.ToString(), 15f);
            Fall.m_skillLevels.Add(Skills.SkillType.Pickaxes.ToString(), 15f);

            Winter.m_staminaRegenMultiplier = 1.1f;
            Winter.m_noiseModifier = 0.2f;
            Winter.m_stealthModifier = -0.2f;
            Winter.m_speedModifier = -0.05f;
            Winter.m_fallDamageModifier = -0.3f;

            Winter.m_damageModifiers.Add(HitData.DamageType.Fire.ToString(), HitData.DamageModifier.Resistant.ToString());

            Winter.m_raiseSkills.Add(Skills.SkillType.WoodCutting.ToString(), 1.1f);
            Winter.m_raiseSkills.Add(Skills.SkillType.Fishing.ToString(), 1.1f);
            Winter.m_raiseSkills.Add(Skills.SkillType.Pickaxes.ToString(), 1.1f);
            Winter.m_skillLevels.Add(Skills.SkillType.WoodCutting.ToString(), 10f);
            Winter.m_skillLevels.Add(Skills.SkillType.Fishing.ToString(), 10f);
            Winter.m_skillLevels.Add(Skills.SkillType.Pickaxes.ToString(), 10f);
        }

        public Stats GetSeasonStats()
        {
            return GetSeasonStats(seasonState.GetCurrentSeason());
        }

        private Stats GetSeasonStats(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new Stats(),
            };
        }
    }
}