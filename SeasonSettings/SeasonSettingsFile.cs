using System;

namespace Seasons
{
    [Serializable]
    public class SeasonSettingsFile
    {
        public int? daysInSeason;
        public int? nightLength;
        public bool? torchAsFiresource;
        public float? torchDurabilityDrain;
        public float? plantsGrowthMultiplier;
        public float? beehiveProductionMultiplier;
        public float? foodDrainMultiplier;
        public float? staminaDrainMultiplier;
        public float? fireplaceDrainMultiplier;
        public float? sapCollectingSpeedMultiplier;
        public bool? rainProtection;
        public float? woodFromTreesMultiplier;
        public float? windIntensityMultiplier;
        public float? restedBuffDurationMultiplier;
        public float? livestockProcreationMultiplier;
        public bool? overheatIn2WarmClothes;
        public float? meatFromAnimalsMultiplier;
        public float? treesRegrowthChance;

        public SeasonSettingsFile(SeasonSettings settings)
        {
            daysInSeason = settings.m_daysInSeason;
            nightLength = settings.m_nightLength;
            torchAsFiresource = settings.m_torchAsFiresource;
            torchDurabilityDrain = settings.m_torchDurabilityDrain;
            plantsGrowthMultiplier = settings.m_plantsGrowthMultiplier;
            beehiveProductionMultiplier = settings.m_beehiveProductionMultiplier;
            foodDrainMultiplier = settings.m_foodDrainMultiplier;
            staminaDrainMultiplier = settings.m_staminaDrainMultiplier;
            fireplaceDrainMultiplier = settings.m_fireplaceDrainMultiplier;
            sapCollectingSpeedMultiplier = settings.m_sapCollectingSpeedMultiplier;
            rainProtection = settings.m_rainProtection;
            woodFromTreesMultiplier = settings.m_woodFromTreesMultiplier;
            windIntensityMultiplier = settings.m_windIntensityMultiplier;
            restedBuffDurationMultiplier = settings.m_restedBuffDurationMultiplier;
            livestockProcreationMultiplier = settings.m_livestockProcreationMultiplier;
            overheatIn2WarmClothes = settings.m_overheatIn2WarmClothes;
            meatFromAnimalsMultiplier = settings.m_meatFromAnimalsMultiplier;
            treesRegrowthChance = settings.m_treesRegrowthChance;
        }

        public SeasonSettingsFile()
        {
        }
    }
}