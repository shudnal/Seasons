using Newtonsoft.Json;
using System;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class SeasonalSnowSettings
    {
        public static SeasonSnow Current { get; private set; } = new SeasonSnow(loadDefaults: true);

        public static void ApplySynchronizedSettings()
        {
            try
            {
                SeasonSnow settings = String.IsNullOrEmpty(seasonalSnowJSON.Value)
                    ? new SeasonSnow(loadDefaults: true)
                    : JsonConvert.DeserializeObject<SeasonSnow>(seasonalSnowJSON.Value);

                Current = settings ?? new SeasonSnow();
                SeasonalSnowMeshSettings.RebuildConfiguration();
                SeasonalEnemySnow.RefreshSnowMaterialRanges();
                SeasonalPlayerCapeSnow.RefreshSnowMaterialRanges();

                LogInfo(String.IsNullOrEmpty(seasonalSnowJSON.Value)
                    ? "Seasonal snow settings loaded defaults"
                    : "Seasonal snow settings updated");
            }
            catch (Exception e)
            {
                LogWarning($"Error parsing seasonal snow settings:\n{e}");
            }
        }
    }
}
