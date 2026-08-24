using BepInEx.Configuration;
using ConditionalConfigSync;
using System;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> AutumnDay;
        internal static ConfigEntry<string> TestEnemyPrefab;
        internal static ConfigEntry<float> GoalPoints;
        internal static ConfigEntry<float> ProgressShareRadius;
        internal static ConfigEntry<float> EnemyIncomingDamageMultiplier;
        internal static ConfigEntry<float> EnemyOutgoingDamageMultiplier;
        internal static ConfigEntry<float> EnemyMovementSpeedMultiplier;
        internal static ConfigEntry<float> EnemyTargetUpdateIntervalMultiplier;
        internal static ConfigEntry<float> EnemyHuntRange;
        internal static ConfigEntry<float> GroupMergeDistance;
        internal static ConfigEntry<float> GroupSplitDistance;
        internal static ConfigEntry<int> ExtraEnemiesPerParticipant;
        internal static ConfigEntry<int> GroupExtraEnemyCap;
        internal static ConfigEntry<int> ServerExtraEnemyHardCap;
        internal static ConfigEntry<float> SpawnLeaseSeconds;
        internal static ConfigEntry<string> RewardSkills;
        internal static ConfigEntry<BloodMoonRewardMode> RewardMode;
        internal static ConfigEntry<float> CompletionSkillLevels;
        internal static ConfigEntry<float> CompletionPerSkillCap;
        internal static ConfigEntry<float> LiveSkillBonusCap;
        internal static ConfigEntry<string> BloodCraftFoodAndMead;
        internal static ConfigEntry<bool> MusicEnabled;
        internal static ConfigEntry<bool> LogHits;

        private static bool initialized;

        internal const float ForewarningHour = 12f;
        internal const float MarkedHour = 18f;
        internal const float ActiveHour = 23f;
        internal const float AutoCompleteHour = 28.25f;
        internal const float ForcedEndHour = 29.75f;
        internal const float MorningHour = 30f;

        internal static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            Enabled = Server("Blood Moon", "Enabled", true, "Enable the annual Blood Moon event.");
            AutumnDay = Server("Blood Moon", "Autumn day", 9, "Autumn day used as the Blood Moon event day. Forewarning starts three days earlier.");

            TestEnemyPrefab = Server("Blood Moon - Combat", "Extra enemy prefab", "Draugr", "Explicit bootstrap prefab used for Blood Moon extra enemies until the automatic pool contract is implemented.");
            GoalPoints = Server("Blood Moon - Combat", "Goal points", 100f, "Combat points required for Goal Reached.");
            ProgressShareRadius = Server("Blood Moon - Combat", "Progress share radius", 120f, "XZ radius around the credited participant used to share an enemy death with active members of the same combat group.");
            EnemyIncomingDamageMultiplier = Server("Blood Moon - Combat", "Enemy incoming damage multiplier", 1f, "Damage multiplier applied when a Blood Moon participant damages a Blood enemy.");
            EnemyOutgoingDamageMultiplier = Server("Blood Moon - Combat", "Enemy outgoing damage multiplier", 1f, "Damage multiplier applied when a Blood enemy damages a Blood Moon participant.");
            EnemyMovementSpeedMultiplier = Server("Blood Moon - Combat", "Enemy movement speed multiplier", 1f, "Runtime movement speed multiplier for Blood enemies. Existing prefab and ZDO values are not modified.");
            EnemyTargetUpdateIntervalMultiplier = Server("Blood Moon - Combat", "Enemy target update interval multiplier", 1f, "Runtime target-update interval multiplier for Blood enemies. Values below 1 make target decisions more frequent.");
            EnemyHuntRange = Server("Blood Moon - Combat", "Enemy hunt range", 200f, "Maximum XZ range used by Blood enemies when acquiring active participants.");
            GroupMergeDistance = Server("Blood Moon - Combat", "Group merge distance", 120f, "XZ distance used to merge active participants into combat groups.");
            GroupSplitDistance = Server("Blood Moon - Combat", "Group split distance", 160f, "XZ distance used as group split hysteresis.");

            ExtraEnemiesPerParticipant = Server("Blood Moon - Spawning", "Extra enemies per participant", 4, "Extra enemy budget contributed by each active participant before the group and server caps are applied.");
            GroupExtraEnemyCap = Server("Blood Moon - Spawning", "Group extra enemy cap", 12, "Maximum Blood Moon extra enemies assigned to one combat group after participant scaling.");
            ServerExtraEnemyHardCap = Server("Blood Moon - Spawning", "Server extra enemy hard cap", 60, "Absolute server-wide cap for Blood Moon extra enemies.");
            SpawnLeaseSeconds = Server("Blood Moon - Spawning", "Spawn lease seconds", 8f, "Lifetime of a zone-owner spawn lease before the server may reassign it.");

            RewardMode = Server("Blood Moon - Rewards", "Skill reward mode", BloodMoonRewardMode.Hybrid, "Skill reward policy: None, CompletionOnly, LimitedTripleGainOnly, or Hybrid.");
            RewardSkills = Server("Blood Moon - Rewards", "Combat skills", "Swords,Knives,Clubs,Polearms,Spears,Blocking,Axes,Bows,ElementalMagic,BloodMagic,Unarmed,Crossbows", "Comma-separated Skills.SkillType aliases eligible for Blood Moon rewards. Matching is case-insensitive.");
            CompletionSkillLevels = Server("Blood Moon - Rewards", "Completion skill levels", 25f, "Total permanent skill-level-equivalent reward at 100% earned combat progress.");
            CompletionPerSkillCap = Server("Blood Moon - Rewards", "Completion per skill cap", 10f, "Maximum permanent completion reward applied to one skill. Overflow is not redistributed.");
            LiveSkillBonusCap = Server("Blood Moon - Rewards", "Live triple gain cap", 10f, "Maximum bonus level-equivalent supplied by the limited x3 live skill gain per participant.");

            BloodCraftFoodAndMead = Server("Blood Moon - Blood Craft", "Food and mead allowlist", "", "Comma-separated prefab names of food and mead recipes additionally exposed by Blood Craft.");
            MusicEnabled = Server("Blood Moon - Presentation", "Music enabled", true, "Allow Blood Moon presentation to request its event music when available.");
            LogHits = Client("Blood Moon - Diagnostics", "Log hits", false, "Log accepted and rejected Blood Moon hit routing decisions.");
        }

        internal static int GetGroupExtraEnemyCap(int activeParticipantCount)
        {
            int participants = Math.Max(0, activeParticipantCount);
            int scaled = Math.Max(0, ExtraEnemiesPerParticipant.Value) * participants;
            return Math.Min(Math.Max(0, GroupExtraEnemyCap.Value), scaled);
        }

        private static ConfigEntry<T> Server<T>(string section, string key, T defaultValue, string description)
        {
            return Seasons.configSync.AddConfigEntry(Seasons.instance.Config, section, key, defaultValue,
                new ConfigDescription(description), syncMode: ConfigSyncMode.AlwaysServerControlled, serverControlledByDefault: true).SourceConfig;
        }

        private static ConfigEntry<T> Client<T>(string section, string key, T defaultValue, string description)
        {
            return Seasons.configSync.AddConfigEntry(Seasons.instance.Config, section, key, defaultValue,
                new ConfigDescription(description), syncMode: ConfigSyncMode.AlwaysClientControlled).SourceConfig;
        }
    }
}
