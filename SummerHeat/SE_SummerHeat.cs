using System.Text;
using UnityEngine;

namespace Seasons
{
    public class SE_SummerHeat : SE_Stats
    {
        private const char DefaultBarSymbol = '▄';
        private const float PartialSegmentEpsilon = 0.0001f;
        private static readonly StringBuilder TooltipBuilder = new StringBuilder(256);
        private float _damageTimer;

        public override void Setup(Character character)
        {
            StatusEffectHud.EnsureTimeTextRichText();

            m_name = "$seasons_status_summer_heat_name";
            m_tooltip = "$seasons_status_summer_heat_description";
            m_icon ??= Seasons.iconWarm ?? Seasons.iconSummer;
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
                Seasons.SummerHeatDisplayMode.Percent => ColorizeText($"{SummerHeat.HeatPercent:0}%", GetHeatDisplayColor()),
                _ => BuildBarText()
            };
        }

        public override string GetTooltipString()
        {
            TooltipBuilder.Clear();
            TooltipBuilder.Append("$seasons_status_summer_heat_description".Localize()).Append('\n').Append('\n');
            TooltipBuilder.AppendFormat("{0}: {1}\n", "$seasons_status_summer_heat_current".Localize(), ColorizeText($"{SummerHeat.HeatPercent:0}%", GetHeatDisplayColor()));
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_zone".Localize(), GetZoneText(SummerHeat.CurrentZone).Localize());
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_weather".Localize(), (SummerHeat.IsSunny ? "$seasons_status_summer_heat_sunny" : "$seasons_status_summer_heat_not_sunny").Localize());
            TooltipBuilder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_exposure".Localize(), GetExposureText().Localize());

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

            AppendTechnicalInfo(TooltipBuilder);

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
            int total = Mathf.Clamp(Seasons.summerHeatBarSegments.Value, 1, 32);
            char symbol = GetBarSymbol();

            GetBarBrightness(out float emptyAlpha, out float fullAlpha);

            float exactSegments = Mathf.Clamp01(SummerHeat.HeatFactor) * total;
            int filledSegments = Mathf.Clamp(Mathf.FloorToInt(exactSegments), 0, total);
            float partialFraction = exactSegments - filledSegments;

            bool hasPartialSegment = partialFraction > PartialSegmentEpsilon && filledSegments < total;
            int emptySegments = Mathf.Max(0, total - filledSegments - (hasPartialSegment ? 1 : 0));
            float partialAlpha = Mathf.Lerp(emptyAlpha, fullAlpha, Mathf.Clamp01(partialFraction));
            Color heatColor = GetHeatDisplayColor();

            string full = filledSegments > 0 ? new string(symbol, filledSegments) : string.Empty;
            string partial = hasPartialSegment ? symbol.ToString() : string.Empty;
            string empty = emptySegments > 0 ? new string(symbol, emptySegments) : string.Empty;

            return WrapBarText($"{ColorizeText(full, heatColor, fullAlpha)}{ColorizeText(partial, heatColor, partialAlpha)}{ColorizeText(empty, heatColor, emptyAlpha)}");
        }

        private static string WrapBarText(string barText)
        {
            return Seasons.summerHeatBarTagMode.Value switch
            {
                Seasons.SummerHeatBarTagMode.None => barText,
                Seasons.SummerHeatBarTagMode.Sub => $"<sub>{barText}</sub>",
                _ => $"<sup>{barText}</sup>"
            };
        }

        private static char GetBarSymbol()
        {
            string configuredSymbol = Seasons.summerHeatBarSymbol.Value;
            if (string.IsNullOrWhiteSpace(configuredSymbol))
                return DefaultBarSymbol;

            configuredSymbol = configuredSymbol.Trim();
            return configuredSymbol.Length > 0 ? configuredSymbol[0] : DefaultBarSymbol;
        }

        private static void GetBarBrightness(out float emptyAlpha, out float fullAlpha)
        {
            float configuredMin = Mathf.Clamp01(Seasons.summerHeatBarMinBrightness.Value);
            float configuredMax = Mathf.Clamp01(Seasons.summerHeatBarMaxBrightness.Value);

            emptyAlpha = Mathf.Min(configuredMin, configuredMax);
            fullAlpha = Mathf.Max(configuredMin, configuredMax);
        }

        private static Color GetHeatDisplayColor()
        {
            Color bonusColor = Seasons.summerHeatBarBonusColor.Value;
            Color neutralColor = Seasons.summerHeatBarNeutralColor.Value;
            Color penaltyColor = Seasons.summerHeatBarPenaltyColor.Value;
            Color maxColor = Seasons.summerHeatBarMaxColor.Value;

            if (SummerHeat.MaxEffectFactor > 0f)
                return Color.Lerp(penaltyColor, maxColor, SummerHeat.MaxEffectFactor);

            if (SummerHeat.RedFactor > 0f)
                return Color.Lerp(neutralColor, penaltyColor, SummerHeat.RedFactor);

            if (SummerHeat.GreenFactor > 0f)
                return Color.Lerp(neutralColor, bonusColor, SummerHeat.GreenFactor);

            return neutralColor;
        }

        private static string ColorizeText(string value, Color color, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return $"<color=#{ColorHex(color, alpha)}>{value}</color>";
        }

        private static string ColorHex(Color color, float alpha)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f), 0, 255);
            int a = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(alpha) * Mathf.Clamp01(color.a) * 255f), 0, 255);
            return $"{r:x2}{g:x2}{b:x2}{a:x2}";
        }

        private static float GetHeatMultiplier(float configuredEffect)
        {
            configuredEffect = Mathf.Clamp01(configuredEffect);
            if (Mathf.Approximately(configuredEffect, 0f))
                return 1f;

            float bonus = Mathf.Lerp(1f, 1f + configuredEffect, SummerHeat.GreenFactor);
            float penaltyFactor = Mathf.Max(SummerHeat.RedFactor, SummerHeat.MaxEffectFactor);
            float penalty = Mathf.Lerp(1f, 1f - configuredEffect, penaltyFactor);

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

        private static void AppendTechnicalInfo(StringBuilder builder)
        {
            if (!Seasons.summerHeatRavenTechnicalInfo.Value || !TextsDialog_AddActiveEffects_SeasonTooltipWhenBuffDisabled.isActiveEffectsListCall || !SummerHeat.IsReady)
                return;

            bool isDaytime = SummerHeat.Instance == null || SummerHeat.Instance.IsDaytime();
            float nightFactor = Mathf.Clamp(Seasons.summerHeatNightFactor.Value, 0.1f, 1f);
            GetThresholds(isDaytime, nightFactor, out float greenThreshold, out float neutralThreshold, out float maxThreshold);
            float greenFadeWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatGreenFadeWidth.Value), isDaytime, nightFactor));
            float redRampWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatRedRampWidth.Value), isDaytime, nightFactor));
            float greenStart = Mathf.Max(0f, greenThreshold - greenFadeWidth);
            float greenEnd = greenThreshold + greenFadeWidth;
            float redFullThreshold = Mathf.Min(maxThreshold, neutralThreshold + redRampWidth);
            float heatCap = isDaytime ? SummerHeatController.DaytimeHeatCap : SummerHeatController.DaytimeHeatCap * nightFactor;
            float overheatBuffer = ClampPercent(Seasons.summerHeatMaxOverflow.Value);

            Color currentColor = GetHeatDisplayColor();
            Color bonusColor = Seasons.summerHeatBarBonusColor.Value;
            Color penaltyColor = Seasons.summerHeatBarPenaltyColor.Value;
            Color maxColor = Seasons.summerHeatBarMaxColor.Value;

            builder.Append('\n');
            builder.AppendFormat("<color=orange>{0}</color>\n", "$seasons_status_summer_heat_technical".Localize());
            builder.AppendFormat("{0}: {1} / {2}\n",
                "$seasons_status_summer_heat_technical_heat_values".Localize(),
                FormatPercent(SummerHeat.HeatPercent, currentColor),
                FormatPercent(SummerHeat.OverflowHeatPercent, maxColor));
            builder.AppendFormat("{0}: {1} / {2} / {3}\n",
                "$seasons_status_summer_heat_technical_factors".Localize(),
                FormatFactorPercent(SummerHeat.GreenFactor, bonusColor),
                FormatFactorPercent(SummerHeat.RedFactor, penaltyColor),
                FormatBoolColored(SummerHeat.MaxEffectFactor > 0f, maxColor));
            builder.AppendFormat("{0}: <color=orange>{1}</color>\n", "$seasons_status_summer_heat_technical_direction".Localize(), GetTrendText().Localize());
            builder.AppendFormat("{0}: <color=orange>{1}</color> / <color=orange>{2}</color> / <color=orange>{3}</color> / <color=orange>{4}</color>\n",
                "$seasons_status_summer_heat_technical_conditions".Localize(),
                FormatBool(isDaytime),
                FormatBool(SummerHeat.IsSunny),
                FormatBool(SummerHeat.IsInShade),
                FormatBool(SummerHeatVisuals.IsWorldHazeActive()));
            AppendArmorTechnicalInfo(builder);
            builder.AppendFormat("{0}: {1}\n", "$seasons_status_summer_heat_technical_heat_scale".Localize(), BuildTechnicalHeatScale(isDaytime));
            builder.AppendFormat("{0}: {1} / {2} / {3}\n",
                "$seasons_status_summer_heat_technical_comfort_range".Localize(),
                FormatPercent(greenStart, bonusColor),
                FormatPercent(greenThreshold, bonusColor),
                FormatPercent(greenEnd, bonusColor));
            builder.AppendFormat("{0}: {1} / {2}\n",
                "$seasons_status_summer_heat_technical_penalty_ramp".Localize(),
                FormatPercent(neutralThreshold, penaltyColor),
                FormatPercent(redFullThreshold, penaltyColor));
            builder.AppendFormat("{0}: {1} / {2} / {3}\n",
                "$seasons_status_summer_heat_technical_overheated".Localize(),
                FormatPercent(maxThreshold, maxColor),
                FormatPercent(heatCap, maxColor),
                FormatPercent(overheatBuffer, maxColor));
        }

        private static void GetThresholds(bool isDaytime, float nightFactor, out float greenThreshold, out float neutralThreshold, out float maxThreshold)
        {
            float greenValue = ClampPercent(Seasons.summerHeatGreenThreshold.Value);
            float neutralValue = ClampPercent(Mathf.Max(greenValue + 1f, Seasons.summerHeatNeutralThreshold.Value));
            float maxValue = ClampPercent(Mathf.Max(neutralValue + 1f, Seasons.summerHeatMaxThreshold.Value));

            greenThreshold = ScaleHeatPercentForTime(greenValue, isDaytime, nightFactor);
            neutralThreshold = ScaleHeatPercentForTime(neutralValue, isDaytime, nightFactor);
            maxThreshold = ScaleHeatPercentForTime(maxValue, isDaytime, nightFactor);
        }

        private static float ClampPercent(float value) => Mathf.Clamp(value, 0f, 100f);

        private static float ScaleHeatPercentForTime(float value, bool isDaytime, float nightFactor) => isDaytime ? value : value * nightFactor;

        private static string FormatPercent(float value, Color color) => ColorizeText($"{value:0.#}%", color);

        private static string FormatFactorPercent(float value, Color color) => ColorizeText($"{Mathf.Clamp01(value) * 100f:0}%", color);

        private static string FormatBool(bool value) => (value ? "$seasons_status_summer_heat_yes" : "$seasons_status_summer_heat_no").Localize();

        private static string FormatBoolColored(bool value, Color yesColor)
        {
            string text = FormatBool(value);
            return value ? ColorizeText(text, yesColor) : text;
        }

        private static void AppendArmorTechnicalInfo(StringBuilder builder)
        {
            if (!Seasons.summerHeatArmorHeatEnabled.Value || SummerHeat.Instance == null)
                return;

            SummerHeatArmorState armor = SummerHeat.Instance.ArmorState;
            builder.AppendFormat("{0}: {1} / {2}\n",
                "$seasons_status_summer_heat_technical_armor".Localize(),
                FormatArmorModifier(armor.HeatingModifier, positiveIsGood: false),
                FormatArmorModifier(armor.CoolingModifier, positiveIsGood: true));
            builder.AppendFormat("{0}: <color=orange>{1}</color> / <color=orange>{2}</color> / <color=orange>{3}</color> / <color=orange>{4}</color>\n",
                "$seasons_status_summer_heat_technical_armor_slots".Localize(),
                LocalizeState(armor.HeadState),
                LocalizeState(armor.CloakState),
                LocalizeState(armor.ChestState),
                LocalizeState(armor.LegsState));
        }

        private static string FormatArmorModifier(float value, bool positiveIsGood)
        {
            Color bonusColor = Seasons.summerHeatBarBonusColor.Value;
            Color neutralColor = Seasons.summerHeatBarNeutralColor.Value;
            Color penaltyColor = Seasons.summerHeatBarPenaltyColor.Value;
            Color color = Mathf.Approximately(value, 0f)
                ? neutralColor
                : value > 0f == positiveIsGood ? bonusColor : penaltyColor;
            return ColorizeText($"{value * 100f:+0;-0;0}%", color);
        }

        private static string LocalizeState(string token) => string.IsNullOrEmpty(token) ? string.Empty : token.Localize();

        private static string BuildTechnicalHeatScale(bool isDaytime)
        {
            const int segmentCount = 100;
            const char segmentSymbol = '|';

            StringBuilder builder = new StringBuilder(segmentCount * 24);
            for (int i = 0; i < segmentCount; ++i)
            {
                float heatPercent = i * 100f / (segmentCount - 1);
                builder.Append(ColorizeText(segmentSymbol.ToString(), GetHeatDisplayColorForValue(heatPercent, isDaytime)));
            }

            return $"<cspace=-0.08em>{builder}</cspace>";
        }

        private static Color GetHeatDisplayColorForValue(float heatPercent, bool isDaytime)
        {
            float nightFactor = Mathf.Clamp(Seasons.summerHeatNightFactor.Value, 0.1f, 1f);
            GetThresholds(isDaytime, nightFactor, out float greenThreshold, out float neutralThreshold, out float maxThreshold);
            float greenFadeWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatGreenFadeWidth.Value), isDaytime, nightFactor));
            float redRampWidth = Mathf.Max(0.1f, ScaleHeatPercentForTime(ClampPercent(Seasons.summerHeatRedRampWidth.Value), isDaytime, nightFactor));

            Color bonusColor = Seasons.summerHeatBarBonusColor.Value;
            Color neutralColor = Seasons.summerHeatBarNeutralColor.Value;
            Color penaltyColor = Seasons.summerHeatBarPenaltyColor.Value;
            Color maxColor = Seasons.summerHeatBarMaxColor.Value;

            if (heatPercent >= maxThreshold)
                return maxColor;

            if (heatPercent > neutralThreshold)
            {
                float redFullThreshold = Mathf.Min(maxThreshold, neutralThreshold + redRampWidth);
                float redFactor = heatPercent < redFullThreshold ? Mathf.InverseLerp(neutralThreshold, redFullThreshold, heatPercent) : 1f;
                return Color.Lerp(neutralColor, penaltyColor, redFactor);
            }

            float greenStart = Mathf.Max(0f, greenThreshold - greenFadeWidth);
            float greenEnd = greenThreshold + greenFadeWidth;
            if (heatPercent >= greenStart && heatPercent <= greenEnd)
            {
                float greenFactor = heatPercent <= greenThreshold
                    ? Mathf.InverseLerp(greenStart, greenThreshold, heatPercent)
                    : 1f - Mathf.InverseLerp(greenThreshold, greenEnd, heatPercent);
                float greenScaleBlendFactor = Mathf.Lerp(0.3f, 1f, greenFactor);
                return Color.Lerp(neutralColor, bonusColor, greenScaleBlendFactor);
            }

            return neutralColor;
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
