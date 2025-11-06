namespace Seasons
{
    public static class SeasonsVars
    {
        public const string s_cropSurvivedWinterDayName = "Seasons_Survived_Winter_Day";
        public static int s_cropSurvivedWinterDayHash = s_cropSurvivedWinterDayName.GetStableHashCode();

        public const string s_cropStartedFreezingName = "Seasons_Started_Freezing";
        public static int s_cropStartedFreezingHash = s_cropStartedFreezingName.GetStableHashCode();

        public static int s_treeRegrowthHaveGrowSpace = "Seasons_HaveGrowSpace".GetStableHashCode();

        public const string s_statusEffectSeasonName = "Season";
        public static int s_statusEffectSeasonHash = s_statusEffectSeasonName.GetStableHashCode();

        public const string s_statusEffectOverheatName = "Overheat";
        public static int s_statusEffectOverheatHash = s_statusEffectOverheatName.GetStableHashCode();

        public static int s_iceFloeWatermark = "Seasons_IceFloe".GetStableHashCode();
        public static int s_iceFloeMass = "Seasons_IceFloeMass".GetStableHashCode();
        public static int s_iceFloesSpawned = "Seasons_IceFloesSpawned".GetStableHashCode();

        public static int s_terrainDecultivated = "Seasons_Terrain_Decultivated".GetStableHashCode();
    }
}
