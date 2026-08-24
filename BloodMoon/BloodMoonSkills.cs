using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonSkills
    {
        private const string RewardRecordPrefix = "Seasons.BloodMoon.Reward.";
        private const float Epsilon = 0.0001f;

        [Serializable]
        private sealed class RewardEnvelope
        {
            public long WorldUid;
            public long EventId;
            public Dictionary<int, float> BonusBySkill = new Dictionary<int, float>();
        }

        [Serializable]
        private sealed class RewardApplicationRecord
        {
            public bool Completed;
            public Dictionary<int, float> TargetProgress = new Dictionary<int, float>();
        }

        private sealed class RaiseState
        {
            internal bool Track;
            internal long EventId;
            internal long PlayerId;
            internal Skills.SkillType Skill;
            internal float BeforeProgress;
            internal float BaseProjectedDelta;
        }

        private static string cachedSkillConfig = string.Empty;
        private static HashSet<Skills.SkillType> cachedEligibleSkills = new HashSet<Skills.SkillType>();
        private static readonly HashSet<string> loggedUnknownAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<long, float> localLiveBonusUsed = new Dictionary<long, float>();
        private static long localEventId = -1L;
        private static long localReportSequence;
        private static int rewardApplicationDepth;

        internal static bool IsEligible(Skills.SkillType skill)
        {
            RefreshEligibleSkills();
            return cachedEligibleSkills.Contains(skill);
        }

        internal static string SerializeCompletionReward(BloodMoonParticipantState participant)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            RewardEnvelope envelope = new RewardEnvelope
            {
                WorldUid = state?.WorldUid ?? GetCurrentWorldUid(),
                EventId = state?.EventId ?? BloodMoonNetwork.ClientGlobal.EventId
            };

            if (participant == null || !CompletionRewardsEnabled())
                return JsonConvert.SerializeObject(envelope);

            float goalPoints = Mathf.Max(1f, BloodMoonConfig.GoalPoints.Value);
            float earnedProgress = Mathf.Clamp01(participant.CombatPoints / goalPoints);
            float budget = Mathf.Max(0f, BloodMoonConfig.CompletionSkillLevels.Value) * earnedProgress;
            if (budget <= Epsilon || participant.SkillContribution == null || participant.SkillContribution.Count == 0)
                return JsonConvert.SerializeObject(envelope);

            List<KeyValuePair<int, float>> selected = participant.SkillContribution
                .Where(entry => entry.Value > Epsilon && IsEligible((Skills.SkillType)entry.Key))
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .Take(5)
                .ToList();
            float totalContribution = selected.Sum(entry => entry.Value);
            if (totalContribution <= Epsilon)
                return JsonConvert.SerializeObject(envelope);

            float perSkillCap = Mathf.Max(0f, BloodMoonConfig.CompletionPerSkillCap.Value);
            foreach (KeyValuePair<int, float> entry in selected)
            {
                float calculated = budget * entry.Value / totalContribution;
                float applied = Mathf.Min(calculated, perSkillCap);
                if (applied > Epsilon)
                    envelope.BonusBySkill[entry.Key] = applied;
            }

            return JsonConvert.SerializeObject(envelope);
        }

        internal static void ApplySerializedReward(Player player, string payload)
        {
            if (player == null || string.IsNullOrWhiteSpace(payload))
                return;

            RewardEnvelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<RewardEnvelope>(payload);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Skill] Ignoring invalid reward payload: {ex.Message}");
                return;
            }

            long currentWorldUid = GetCurrentWorldUid();
            if (envelope == null || envelope.WorldUid == 0L || envelope.WorldUid != currentWorldUid || envelope.EventId < 0L || envelope.BonusBySkill == null || envelope.BonusBySkill.Count == 0)
                return;

            string recordKey = RewardRecordPrefix + envelope.WorldUid.ToString(CultureInfo.InvariantCulture) + "." + envelope.EventId.ToString(CultureInfo.InvariantCulture);
            RewardApplicationRecord record = ReadApplicationRecord(player, recordKey);
            if (record == null)
            {
                record = new RewardApplicationRecord();
                foreach (KeyValuePair<int, float> reward in envelope.BonusBySkill)
                {
                    Skills.SkillType type = (Skills.SkillType)reward.Key;
                    if (!Skills.IsSkillValid(type) || type == Skills.SkillType.None || type == Skills.SkillType.All || reward.Value <= 0f)
                        continue;
                    Skills.Skill skill = player.m_skills.GetSkill(type);
                    if (skill?.m_info == null)
                        continue;
                    record.TargetProgress[reward.Key] = Mathf.Min(100f, GetLevelEquivalent(skill) + reward.Value);
                }
                player.m_customData[recordKey] = JsonConvert.SerializeObject(record);
            }

            if (record.Completed)
                return;

            rewardApplicationDepth++;
            try
            {
                foreach (KeyValuePair<int, float> target in record.TargetProgress)
                {
                    Skills.SkillType type = (Skills.SkillType)target.Key;
                    if (!Skills.IsSkillValid(type) || type == Skills.SkillType.None || type == Skills.SkillType.All)
                        continue;
                    SetLevelEquivalent(player, type, target.Value);
                }
                record.Completed = true;
                player.m_customData[recordKey] = JsonConvert.SerializeObject(record);
                LogInfo($"[BloodMoon.Skill] Applied completion reward for world {envelope.WorldUid}, event {envelope.EventId} to {player.GetPlayerName()}.");
            }
            finally
            {
                rewardApplicationDepth--;
            }
        }

        internal static void AcceptServerReport(BloodMoonParticipantState participant, long sequence, Skills.SkillType skill, float baseEquivalent, float liveBonusEquivalent)
        {
            if (participant == null || sequence <= participant.LastSkillReportSequence || !IsEligible(skill))
                return;
            participant.LastSkillReportSequence = sequence;
            baseEquivalent = Mathf.Clamp(baseEquivalent, 0f, 2f);
            liveBonusEquivalent = Mathf.Clamp(liveBonusEquivalent, 0f, 2f);
            if (baseEquivalent > Epsilon)
                Add(participant.SkillContribution, (int)skill, baseEquivalent);
            if (liveBonusEquivalent > Epsilon)
            {
                float used = participant.LiveSkillBonusEquivalent?.Values.Sum() ?? 0f;
                float remaining = Mathf.Max(0f, BloodMoonConfig.LiveSkillBonusCap.Value - used);
                Add(participant.LiveSkillBonusEquivalent, (int)skill, Mathf.Min(liveBonusEquivalent, remaining));
            }
        }

        internal static string DumpLocal()
        {
            RefreshEligibleSkills();
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            float used = GetLocalLiveBonusUsed(eventId);
            return $"eligible=[{string.Join(",", cachedEligibleSkills.OrderBy(skill => (int)skill))}] liveBonusUsed={used:0.###}/{Mathf.Max(0f, BloodMoonConfig.LiveSkillBonusCap.Value):0.###} sequence={localReportSequence}";
        }

        internal static void ResetLocal(long eventId)
        {
            localEventId = eventId;
            localReportSequence = 0L;
            localLiveBonusUsed.Clear();
        }

        private static void BeforeRaise(Player player, Skills.SkillType skillType, ref float value, out RaiseState state)
        {
            state = null;
            if (rewardApplicationDepth > 0 || player == null || player != Player.m_localPlayer || value <= 0f || !BloodMoonInteractionRules.IsActiveParticipant(player) || !IsEligible(skillType))
                return;

            Skills.Skill skill = player.m_skills.GetSkill(skillType);
            if (skill?.m_info == null || skill.m_level >= 100f)
                return;

            EnsureLocalEvent();
            float regularMultiplier = 1f;
            player.GetSEMan()?.ModifyRaiseSkill(skillType, ref regularMultiplier);
            float baseFactor = value * regularMultiplier;
            float baseProjectedDelta = SimulateRaiseDelta(skill, baseFactor);
            if (baseProjectedDelta <= Epsilon)
                return;

            state = new RaiseState
            {
                Track = true,
                EventId = BloodMoonNetwork.ClientGlobal.EventId,
                PlayerId = player.GetPlayerID(),
                Skill = skillType,
                BeforeProgress = GetLevelEquivalent(skill),
                BaseProjectedDelta = baseProjectedDelta
            };

            if (!LiveBonusEnabled())
                return;

            float remaining = Mathf.Max(0f, BloodMoonConfig.LiveSkillBonusCap.Value - GetLocalLiveBonusUsed(state.EventId));
            if (remaining <= Epsilon)
                return;

            float scale = FindLiveScale(skill, baseFactor, baseProjectedDelta, remaining);
            value *= scale;
        }

        private static void AfterRaise(Player player, RaiseState state)
        {
            if (state == null || !state.Track || player == null || state.EventId != BloodMoonNetwork.ClientGlobal.EventId)
                return;

            Skills.Skill skill = player.m_skills.GetSkill(state.Skill);
            if (skill?.m_info == null)
                return;
            float totalDelta = Mathf.Max(0f, GetLevelEquivalent(skill) - state.BeforeProgress);
            float baseDelta = Mathf.Min(totalDelta, state.BaseProjectedDelta);
            float liveBonusDelta = Mathf.Max(0f, totalDelta - baseDelta);
            if (baseDelta <= Epsilon && liveBonusDelta <= Epsilon)
                return;

            if (liveBonusDelta > Epsilon)
                localLiveBonusUsed[state.EventId] = GetLocalLiveBonusUsed(state.EventId) + liveBonusDelta;

            long sequence = ++localReportSequence;
            BloodMoonNetwork.SendSkillGain(state.EventId, state.PlayerId, sequence, state.Skill, baseDelta, liveBonusDelta);
        }

        private static float FindLiveScale(Skills.Skill skill, float baseFactor, float baseDelta, float remainingBonus)
        {
            float fullDelta = SimulateRaiseDelta(skill, baseFactor * 3f);
            if (Mathf.Max(0f, fullDelta - baseDelta) <= remainingBonus + Epsilon)
                return 3f;

            float low = 1f;
            float high = 3f;
            for (int i = 0; i < 16; ++i)
            {
                float mid = (low + high) * 0.5f;
                float extra = Mathf.Max(0f, SimulateRaiseDelta(skill, baseFactor * mid) - baseDelta);
                if (extra <= remainingBonus)
                    low = mid;
                else
                    high = mid;
            }
            return low;
        }

        private static float SimulateRaiseDelta(Skills.Skill skill, float factor)
        {
            if (skill?.m_info == null || skill.m_level >= 100f || factor <= 0f)
                return 0f;

            float before = GetLevelEquivalent(skill);
            float level = skill.m_level;
            float accumulator = skill.m_accumulator + skill.m_info.m_increseStep * factor * Game.m_skillGainRate;
            float requirement = NextRequirement(level);
            if (accumulator >= requirement)
            {
                level = Mathf.Clamp(level + 1f, 0f, 100f);
                accumulator = 0f;
            }
            float after = level >= 100f ? 100f : level + Mathf.Clamp01(accumulator / NextRequirement(level));
            return Mathf.Max(0f, after - before);
        }

        private static float GetLevelEquivalent(Skills.Skill skill)
        {
            if (skill == null)
                return 0f;
            if (skill.m_level >= 100f)
                return 100f;
            return skill.m_level + Mathf.Clamp01(skill.m_accumulator / NextRequirement(skill.m_level));
        }

        private static void SetLevelEquivalent(Player player, Skills.SkillType type, float target)
        {
            Skills.Skill skill = player.m_skills.GetSkill(type);
            if (skill?.m_info == null)
                return;

            float before = GetLevelEquivalent(skill);
            float clamped = Mathf.Clamp(target, 0f, 100f);
            if (clamped >= 100f)
            {
                skill.m_level = 100f;
                skill.m_accumulator = 0f;
            }
            else
            {
                float level = Mathf.Floor(clamped);
                float fraction = clamped - level;
                skill.m_level = level;
                skill.m_accumulator = NextRequirement(level) * fraction;
            }

            if (player.m_skills.m_useSkillCap)
                player.m_skills.RebalanceSkills(type);
            if (Mathf.Floor(clamped) > Mathf.Floor(before))
                player.OnSkillLevelup(type, skill.m_level);
        }

        private static float NextRequirement(float level)
        {
            return Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f + 0.5f;
        }

        private static float GetLocalLiveBonusUsed(long eventId)
        {
            float local = localLiveBonusUsed.TryGetValue(eventId, out float value) ? value : 0f;
            BloodMoonParticipantState participant = BloodMoonInteractionRules.GetLocalParticipant();
            if (participant != null && participant.LiveSkillBonusEquivalent != null)
                local = Mathf.Max(local, participant.LiveSkillBonusEquivalent.Values.Sum());
            return local;
        }

        private static bool CompletionRewardsEnabled()
        {
            return BloodMoonConfig.RewardMode.Value == BloodMoonRewardMode.CompletionOnly || BloodMoonConfig.RewardMode.Value == BloodMoonRewardMode.Hybrid;
        }

        private static bool LiveBonusEnabled()
        {
            return BloodMoonConfig.RewardMode.Value == BloodMoonRewardMode.LimitedTripleGainOnly || BloodMoonConfig.RewardMode.Value == BloodMoonRewardMode.Hybrid;
        }

        private static void EnsureLocalEvent()
        {
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (localEventId == eventId)
                return;
            localEventId = eventId;
            localReportSequence = 0L;
            localLiveBonusUsed.Clear();
        }

        private static void RefreshEligibleSkills()
        {
            string config = BloodMoonConfig.RewardSkills?.Value ?? string.Empty;
            if (string.Equals(config, cachedSkillConfig, StringComparison.Ordinal))
                return;

            cachedSkillConfig = config;
            cachedEligibleSkills = new HashSet<Skills.SkillType>();
            foreach (string rawAlias in config.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string alias = rawAlias.Trim();
                if (Enum.TryParse(alias, true, out Skills.SkillType skill) && Skills.IsSkillValid(skill) && skill != Skills.SkillType.None && skill != Skills.SkillType.All)
                {
                    cachedEligibleSkills.Add(skill);
                    continue;
                }
                if (loggedUnknownAliases.Add(alias))
                    LogWarning($"[BloodMoon.Skill] Unknown combat skill alias '{alias}' ignored.");
            }
        }

        private static RewardApplicationRecord ReadApplicationRecord(Player player, string key)
        {
            if (!player.m_customData.TryGetValue(key, out string json) || string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<RewardApplicationRecord>(json);
            }
            catch (Exception ex)
            {
                LogWarning($"[BloodMoon.Skill] Invalid reward application record '{key}': {ex.Message}");
                return null;
            }
        }

        private static void Add(Dictionary<int, float> dictionary, int key, float value)
        {
            if (dictionary == null || value <= 0f)
                return;
            dictionary[key] = dictionary.TryGetValue(key, out float current) ? current + value : value;
        }

        private static long GetCurrentWorldUid()
        {
            return ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
        private static class PlayerRaiseSkillPatch
        {
            private static void Prefix(Player __instance, Skills.SkillType skill, ref float value, out RaiseState __state)
            {
                BeforeRaise(__instance, skill, ref value, out __state);
            }

            private static void Postfix(Player __instance, RaiseState __state)
            {
                AfterRaise(__instance, __state);
            }
        }
    }
}
