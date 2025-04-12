using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonBiomeSettings
    {
        public static Color s_meadowsColor = new Color(0.573f, 0.655f, 0.361f);
        public static Color s_blackforestColor = new Color(0.420f, 0.455f, 0.247f);
        public static Color s_heathColor = new Color(0.906f, 0.671f, 0.470f);
        public static Color s_swampColor = new Color(0.639f, 0.447f, 0.345f);
        public static Color s_mistlandsColor = new Color(0.2f, 0.2f, 0.2f);

        [Serializable]
        public class SeasonalBiomeColors
        {
            public string biome;
            public string spring;
            public string summer;
            public string fall;
            public string winter;

            public Heightmap.Biome GetBiome()
            {
                return ParseBiome(biome);
            }

            public Dictionary<Season, Heightmap.Biome> GetSeasonalOverride()
            {
                Dictionary<Season, Heightmap.Biome> result = new Dictionary<Season, Heightmap.Biome>();

                if (!spring.IsNullOrWhiteSpace())
                    result.Add(Season.Spring, ParseBiome(spring));

                if (!summer.IsNullOrWhiteSpace())
                    result.Add(Season.Summer, ParseBiome(summer));

                if (!fall.IsNullOrWhiteSpace())
                    result.Add(Season.Fall, ParseBiome(fall));

                if (!winter.IsNullOrWhiteSpace())
                    result.Add(Season.Winter, ParseBiome(winter));

                return result;
            }
        }

        [NonSerialized]
        private Dictionary<Heightmap.Biome, Dictionary<Season, Heightmap.Biome>> _seasonalBiomeColorOverride;

        [NonSerialized]
        private Dictionary<Heightmap.Biome, Color> _seasonalWinterMapColors;

        [NonSerialized]
        private static readonly Dictionary<Color, Color> s_winterColors = new Dictionary<Color, Color>();

        [NonSerialized]
        private static readonly Dictionary<string, Heightmap.Biome> s_nameToBiome = new Dictionary<string, Heightmap.Biome>();

        [JsonIgnore]
        internal Dictionary<Heightmap.Biome, Dictionary<Season, Heightmap.Biome>> SeasonalBiomeColorOverride
        {
            get 
            {
                if (_seasonalBiomeColorOverride == null)
                    ParseSeasonalGroundColors();

                return _seasonalBiomeColorOverride;
            }
        }

        [JsonIgnore]
        internal Dictionary<Heightmap.Biome, Color> SeasonalWinterMapColors
        {
            get
            {
                if (_seasonalWinterMapColors == null)
                    ParseWinterMapColors();

                return _seasonalWinterMapColors;
            }
        }

        public List<SeasonalBiomeColors> seasonalGroundColors = new List<SeasonalBiomeColors>();

        public Dictionary<string, string> winterMapColors = new Dictionary<string, string>();

        public SeasonBiomeSettings(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            s_nameToBiome.Clear();

            seasonalGroundColors.Add(new SeasonalBiomeColors()
            {
                biome = Heightmap.Biome.Meadows.ToString(),
                fall = Heightmap.Biome.Plains.ToString(),
                winter = Heightmap.Biome.Mountain.ToString(),
            });

            seasonalGroundColors.Add(new SeasonalBiomeColors()
            {
                biome = Heightmap.Biome.BlackForest.ToString(),
                fall = Heightmap.Biome.Swamp.ToString(),
                winter = Heightmap.Biome.Mountain.ToString(),
            });

            seasonalGroundColors.Add(new SeasonalBiomeColors()
            {
                biome = Heightmap.Biome.Plains.ToString(),
                spring = Heightmap.Biome.Meadows.ToString(),
                winter = Heightmap.Biome.Mountain.ToString(),
            });

            seasonalGroundColors.Add(new SeasonalBiomeColors()
            {
                biome = Heightmap.Biome.Mistlands.ToString(),
                winter = Heightmap.Biome.Mountain.ToString(),
            });

            seasonalGroundColors.Add(new SeasonalBiomeColors()
            {
                biome = Heightmap.Biome.Swamp.ToString(),
                winter = Heightmap.Biome.Mountain.ToString(),
            });

            winterMapColors[Heightmap.Biome.Meadows.ToString()] = ToHexRGBA(GetWinterColor(Minimap.instance ? Minimap.instance.m_meadowsColor : s_meadowsColor));
            winterMapColors[Heightmap.Biome.BlackForest.ToString()] = ToHexRGBA(GetWinterColor(Minimap.instance ? Minimap.instance.m_blackforestColor : s_blackforestColor));
            winterMapColors[Heightmap.Biome.Plains.ToString()] = ToHexRGBA(GetWinterColor(Minimap.instance ? Minimap.instance.m_heathColor : s_heathColor));
            winterMapColors[Heightmap.Biome.Swamp.ToString()] = ToHexRGBA(GetWinterColor(Minimap.instance ? Minimap.instance.m_swampColor : s_swampColor));
            winterMapColors[Heightmap.Biome.Mistlands.ToString()] = ToHexRGBA(GetWinterColor(Minimap.instance ? Minimap.instance.m_mistlandsColor : s_mistlandsColor));
        }

        private void ParseSeasonalGroundColors()
        {
            _seasonalBiomeColorOverride = new Dictionary<Heightmap.Biome, Dictionary<Season, Heightmap.Biome>>();

            foreach (SeasonalBiomeColors colorOverride in seasonalGroundColors)
                _seasonalBiomeColorOverride[colorOverride.GetBiome()] = colorOverride.GetSeasonalOverride();
        }

        private void ParseWinterMapColors()
        {
            _seasonalWinterMapColors = new Dictionary<Heightmap.Biome, Color>();

            foreach (KeyValuePair<string, string> winterColor in winterMapColors)
                if (ColorUtility.TryParseHtmlString(winterColor.Value, out Color color))
                    _seasonalWinterMapColors[ParseBiome(winterColor.Key)] = color;
        }

        private static Heightmap.Biome ParseBiome(string biomeName)
        {
            if (s_nameToBiome.Count == 0)
                foreach (Heightmap.Biome biomeEnum in Enum.GetValues(typeof(Heightmap.Biome)))
                    s_nameToBiome[biomeEnum.ToString().ToLowerInvariant()] = biomeEnum;

            if (s_nameToBiome.TryGetValue(biomeName.ToLowerInvariant(), out Heightmap.Biome biomeByName))
                return biomeByName;

            return Enum.TryParse(biomeName, out Heightmap.Biome biome) ? biome : int.TryParse(biomeName, out int biomeValue) ? (Heightmap.Biome)biomeValue : Heightmap.Biome.None;
        }
        
        private static Color GetWinterColor(Color color)
        {
            if (!s_winterColors.ContainsKey(color))
            {
                Color newColor = new Color(0.98f, 0.98f, 1f, color.a);
                s_winterColors[color] = new HSLColor(Color.Lerp(color, newColor, 0.6f)).ToRGBA();
            }

            return s_winterColors[color];
        }

        private static string ToHexRGBA(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }
    }
}