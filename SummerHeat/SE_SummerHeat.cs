using System.Text;
using UnityEngine;

namespace Seasons
{
    public class SE_SummerHeat : SE_Stats
    {
        private const int BarSegments = 12;
        private static readonly StringBuilder TooltipBuilder = new StringBuilder(256);
        private float _damageTimer;

        public override void Setup(Character character)
        {
            m_name = "$seasons_status_summer_heat_name";
            m_tooltip = "$seasons_status_summer_heat_description";
            m_icon ??= Seasons.iconWarm;
            m_ttl = 0f;
            m_cooldownIcon = false;
            m_flashIcon = false;

            base.Setup(character);
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);

            if (!SummerHeat.IsReady || !SummerHeat.IsMechanicActive || m_character is not Player player)
            {
                _damageTimer = 0f;
                return;
            }

            float maxFactor = SummerHeat.MaxEffectFactor;
            if (maxFactor <= 0f)
            {
                _damageTimer = 0f;
                return;
            }

            _damageTimer += dt;
            if (_damageTimer < Mathf.Max(0.1f, Seasons.summerHeatDamageTickInterval.Value))
                return;

            _damageTimer = 0f;
            float minSoftCapPercent = Mathf.Clamp01(Seasons.summerHeatDamageНealthPerTickMinHealthPercentage.Value);
            float softCapPercent = Mathf.Lerp(1f, minSoftCapPercent, maxFactor);
            if (player.GetHealthPercentage() <= softCapPercent)
                return;

            if (Seasons.summerHeatDamageMaxOnly.Value && SummerHeat.CurrentZone != HeatZone.Max)
                return;

            float damageAmount = Mathf.Abs(Seasons.summerHeatDamageНealthPerTick.Value);
            if (damageAmount <= 0f)
                return;

            HitData hitData = new HitData();
            hitData.m_damage.m_damage = damageAmount;
            hitData.m_hitType = Seasons.summerHeatDamageHitType.Value;
            hitData.m_point = player.GetTopPoint();
            player.Damage(hitData);
        }

        public override string GetIconText()
        {
            if (!SummerHeat.IsReady || !SummerHeat.IsMechanicActive)
                return string.Empty;

            return Seasons.summerHeatDisplayMode.Value switch
            {
                Seasons.SummerHeatDisplayMode.None => string.Empty,
                Seasons.SummerHeatDisplayMode.Bar => BuildBarText(),
                Seasons.SummerHeatDisplayMode.Percent => $"{SummerHeat.HeatPercent:0}%",
                _ => BuildBarText()
            };
        }

        public override string GetTooltipString()
        {
            TooltipBuilder.Clear();
            TooltipBuilder.Append("$seasons_status_summer_heat_description".Localize()).Append('\n').Append('\n');
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1:0}%</color>\n", "$seasons_status_summer_heat_current".Localize(), SummerHeat.HeatPercent);
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_zone".Localize(), GetZoneText(SummerHeat.CurrentZone).Localize());
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_weather".Localize(), (SummerHeat.IsSunny ? "$seasons_status_summer_heat_sunny" : "$seasons_status_summer_heat_not_sunny").Localize());
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_exposure".Localize(), GetExposureText().Localize());
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_trend".Localize(), GetTrendText().Localize());

            string modifiers = GetModifierSummary();
            if (!string.IsNullOrEmpty(modifiers))
                TooltipBuilder.Append(modifiers);

            AppendActiveFactors(TooltipBuilder);

            if (SummerHeat.MaxEffectFactor > 0f)
            {
                float minSoftCapPercent = Mathf.Clamp01(Seasons.summerHeatDamageНealthPerTickMinHealthPercentage.Value);
                float softCapPercent = Mathf.Lerp(1f, minSoftCapPercent, SummerHeat.MaxEffectFactor) * 100f;
                TooltipBuilder.AppendFormat("<color=red>{0}</color>\n", string.Format("$seasons_status_summer_heat_cap_warning".Localize(), softCapPercent.ToString("0")));
            }

            return TooltipBuilder.ToString();
        }

        public override void ModifyHealthRegen(ref float regenMultiplier)
        {
            if (!SummerHeat.IsMechanicActive)
                return;

            ApplyMultiplier(ref regenMultiplier, GetHeatMultiplier(Seasons.summerHeatHealthRegenMultiplier.Value), regenStyle: true);
        }

        public override void ModifyStaminaRegen(ref float staminaRegen)
        {
            if (!SummerHeat.IsMechanicActive)
                return;

            ApplyMultiplier(ref staminaRegen, GetHeatMultiplier(Seasons.summerHeatStaminaRegenMultiplier.Value), regenStyle: true);
        }

        public override void ModifyEitrRegen(ref float eitrRegen)
        {
            if (!SummerHeat.IsMechanicActive)
                return;

            ApplyMultiplier(ref eitrRegen, GetHeatMultiplier(Seasons.summerHeatEitrRegenMultiplier.Value), regenStyle: true);
        }

        public override void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)
        {
            if (!SummerHeat.IsMechanicActive)
                return;

            drain += baseDrain * GetSignedModifier(Seasons.summerHeatStaminaUseMultiplier.Value);
        }

        public override void ModifyAdrenaline(float baseValue, ref float use)
        {
            if (!SummerHeat.IsMechanicActive)
                return;

            use += baseValue * GetSignedModifier(Seasons.summerHeatAdrenalineMultiplier.Value);
        }

        private static void ApplyMultiplier(ref float value, float multiplier, bool regenStyle)
        {
            if (Mathf.Approximately(multiplier, 1f))
                return;

            if (regenStyle && multiplier > 1f)
                value += multiplier - 1f;
            else
                value *= multiplier;
        }

        private string BuildBarText()
        {
            int filledSegments = Mathf.Clamp(Mathf.RoundToInt(SummerHeat.HeatFactor * BarSegments), 0, BarSegments);
            int blinkingIndex = -1;
            bool blinkAsFilled = false;

            if (SummerHeat.Direction > 0 && filledSegments < BarSegments)
            {
                blinkingIndex = filledSegments;
            }
            else if (SummerHeat.Direction < 0 && filledSegments > 0)
            {
                blinkingIndex = filledSegments - 1;
                blinkAsFilled = true;
            }

            StringBuilder builder = new StringBuilder(BarSegments + 2);
            for (int i = 0; i < BarSegments; ++i)
            {
                if (i == blinkingIndex)
                {
                    builder.Append(BlinkChar(blinkAsFilled));
                }
                else if (i < filledSegments)
                {
                    builder.Append('▄');
                }
                else
                {
                    builder.Append('·');
                }
            }

            return builder.ToString();
        }

        private static char BlinkChar(bool filledSegment)
        {
            return Mathf.PingPong(Time.time * 2f, 1f) > 0.5f
                ? (filledSegment ? '▄' : '·')
                : (filledSegment ? '·' : '▄');
        }

        private static float GetHeatMultiplier(float configuredMultiplier)
        {
            if (Mathf.Approximately(configuredMultiplier, 1f))
                return 1f;

            float bonusMultiplier = Mathf.Max(1f, 2f - configuredMultiplier);
            float bonus = Mathf.Lerp(1f, bonusMultiplier, SummerHeat.GreenFactor);
            float penaltyFactor = Mathf.Max(SummerHeat.RedFactor, SummerHeat.MaxEffectFactor);
            float penalty = Mathf.Lerp(1f, configuredMultiplier, penaltyFactor);

            return penaltyFactor > 0f ? penalty : bonus;
        }

        private static float GetSignedModifier(float configuredValue)
        {
            configuredValue = Mathf.Clamp01(configuredValue);
            float penaltyFactor = Mathf.Max(SummerHeat.RedFactor, SummerHeat.MaxEffectFactor);
            if (penaltyFactor > 0f)
                return configuredValue * penaltyFactor;

            return 0f - configuredValue * SummerHeat.GreenFactor;
        }

        private static string GetZoneText(HeatZone zone)
        {
            return zone switch
            {
                HeatZone.Green => "$seasons_status_summer_heat_zone_green",
                HeatZone.Neutral => "$seasons_status_summer_heat_zone_neutral",
                HeatZone.Red => "$seasons_status_summer_heat_zone_red",
                HeatZone.Max => "$seasons_status_summer_heat_zone_max",
                _ => "$seasons_status_summer_heat_zone_neutral"
            };
        }

        private static string GetExposureText()
        {
            if (SummerHeat.IsInSun)
                return "$seasons_status_summer_heat_exposure_sun";
            if (SummerHeat.IsInShade)
                return "$seasons_status_summer_heat_exposure_shade";
            return "$seasons_status_summer_heat_exposure_none";
        }

        private static string GetTrendText()
        {
            if (SummerHeat.Direction > 0)
                return "$seasons_status_summer_heat_trend_heating";
            if (SummerHeat.Direction < 0)
                return "$seasons_status_summer_heat_trend_cooling";
            return "$seasons_status_summer_heat_trend_stable";
        }

        private static string GetModifierSummary()
        {
            StringBuilder builder = new StringBuilder(128);
            AppendModifierLine(builder, "$seasons_status_summer_heat_modifier_health_regen", GetHeatMultiplier(Seasons.summerHeatHealthRegenMultiplier.Value));
            AppendModifierLine(builder, "$seasons_status_summer_heat_modifier_stamina_regen", GetHeatMultiplier(Seasons.summerHeatStaminaRegenMultiplier.Value));
            AppendModifierLine(builder, "$seasons_status_summer_heat_modifier_eitr_regen", GetHeatMultiplier(Seasons.summerHeatEitrRegenMultiplier.Value));
            AppendModifierLine(builder, "$seasons_status_summer_heat_modifier_stamina_use", 1f + GetSignedModifier(Seasons.summerHeatStaminaUseMultiplier.Value));
            AppendModifierLine(builder, "$seasons_status_summer_heat_modifier_adrenaline", 1f + GetSignedModifier(Seasons.summerHeatAdrenalineMultiplier.Value));

            if (builder.Length == 0)
                return string.Empty;

            return "\n" + builder;
        }

        private static void AppendActiveFactors(StringBuilder builder)
        {
            bool appended = false;
            if (SummerHeat.Instance != null && SummerHeat.Instance.HasCoolingFood())
            {
                builder.AppendFormat("<color=orange>{0}</color>\n", "$seasons_status_summer_heat_factor_cooling_food".Localize());
                appended = true;
            }

            if (SummerHeat.Instance != null && SummerHeat.Instance.HasCampFireHeat())
            {
                builder.AppendFormat("<color=orange>{0}</color>\n", "$seasons_status_summer_heat_factor_campfire".Localize());
                appended = true;
            }

            if (SummerHeat.Instance != null && SummerHeat.Instance.HasEncumberedHeat())
            {
                builder.AppendFormat("<color=orange>{0}</color>\n", "$seasons_status_summer_heat_factor_encumbered".Localize());
                appended = true;
            }

            if (appended)
                builder.Append('\n');
        }

        private static void AppendModifierLine(StringBuilder builder, string label, float multiplier)
        {
            if (Mathf.Approximately(multiplier, 1f))
                return;

            float percent = (multiplier - 1f) * 100f;
            builder.AppendFormat("{0}: <color=orange>{1}%</color>\n", label.Localize(), percent.ToString("+0;-0"));
        }
    }
}
