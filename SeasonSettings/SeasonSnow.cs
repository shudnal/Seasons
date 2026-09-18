using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace Seasons
{
    [Serializable]
    public sealed class SeasonSnow
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public enum SnowBuildup
        {
            Seasonal,
            Reduced,
            Ignore,
            Disabled
        }

        [Serializable]
        public sealed class PieceSnow
        {
            public SnowBuildup buildup = SnowBuildup.Seasonal;
            public string copyFrom;
            public SnowPosition position;
            public SnowScale scale;

            public PieceSnow(SnowBuildup buildup = SnowBuildup.Seasonal, string copyFrom = null,
                SnowPosition position = null, SnowScale scale = null)
            {
                this.buildup = buildup;
                this.copyFrom = copyFrom;
                this.position = position;
                this.scale = scale;
            }
        }

        [Serializable]
        public sealed class SnowPosition
        {
            public float? x;
            public float? y;
            public float? z;

            public SnowPosition(float? x = null, float? y = null, float? z = null)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        [Serializable]
        public sealed class SnowScale
        {
            public float? x;
            public float? y;
            public float? z;

            public SnowScale(float? x = null, float? y = null, float? z = null)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        [Serializable]
        public sealed class MaterialSnow
        {
            public float min;
            public float max;
            public string[] renderers;

            public MaterialSnow(float min, float max, params string[] renderers)
            {
                this.min = min;
                this.max = max;
                this.renderers = renderers == null || renderers.Length == 0 ? null : renderers;
            }
        }

        public Dictionary<string, PieceSnow> pieces = new Dictionary<string, PieceSnow>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MaterialSnow> creatureMaterials = new Dictionary<string, MaterialSnow>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, MaterialSnow> playerCapeMaterials = new Dictionary<string, MaterialSnow>(StringComparer.OrdinalIgnoreCase);

        public SeasonSnow() : this(loadDefaults: false) { }

        public SeasonSnow(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            pieces.Add("wood_floor_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_floor", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: -0.19f)));
            pieces.Add("wood_stair", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_stepladder", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_floor_2x2", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.23f)));
            pieces.Add("stone_stair", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_2x2x2", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.73f)));
            pieces.Add("blackmarble_floor", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.23f)));
            pieces.Add("blackmarble_floor_triangle", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_stair", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_dvergr_spiralstair", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_dvergr_spiralstair_right", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_floor_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_floor_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_deco_floor", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_stair", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.9f)));
            pieces.Add("Piece_grausten_floor_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_floor_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_floor_4x4", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: -0.02f)));
            pieces.Add("Piece_grausten_stone_ladder", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("charcoal_kiln", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_beehive", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("flint_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("grausten_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("coal_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_barrel", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("skull_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_arch", new PieceSnow(position: new SnowPosition(y: 0.46f)));
            pieces.Add("smelter", new PieceSnow(position: new SnowPosition(y: 3.8f)));
            pieces.Add("piece_bed02", new PieceSnow(position: new SnowPosition(y: 0.24f)));
            pieces.Add("bed", new PieceSnow(position: new SnowPosition(y: 0.14f)));
            pieces.Add("piece_chest_grausten", new PieceSnow(position: new SnowPosition(y: 0.75f)));
            pieces.Add("stave_gate", new PieceSnow(position: new SnowPosition(y: 6.14f), scale: new SnowScale(x: 1.11f, y: 1.0f, z: 1.3f)));
            pieces.Add("stone_fence", new PieceSnow(copyFrom: "stone_wall_2x1", position: new SnowPosition(y: 0.19f), scale: new SnowScale(x: 1.9f, y: 1.0f, z: 0.75f)));
            pieces.Add("wood_fence_gate", new PieceSnow(buildup: SnowBuildup.Disabled));

            creatureMaterials.Add("Abomination_mat", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("goblin_armor", new MaterialSnow(0.65f, 0.79f));
            creatureMaterials.Add("BruteArmor_mat", new MaterialSnow(0.65f, 0.79f));
            creatureMaterials.Add("GoblinStuff_mat", new MaterialSnow(0.65f, 0.8f));
            creatureMaterials.Add("BruteHipCloth_mat", new MaterialSnow(0.65f, 0.8f));
            creatureMaterials.Add("dvergerArbalest_mat", new MaterialSnow(0.77f, 0.79f));
            creatureMaterials.Add("RangerAshlands_mat", new MaterialSnow(0.77f, 0.79f));
            creatureMaterials.Add("dvergermage_mat", new MaterialSnow(0.77f, 0.79f));
            creatureMaterials.Add("DvergerMageICe_mat", new MaterialSnow(0.77f, 0.79f));
            creatureMaterials.Add("DvergerMageSupport_mat", new MaterialSnow(0.77f, 0.79f));
            creatureMaterials.Add("goblin", new MaterialSnow(0.7f, 0.73f));
            creatureMaterials.Add("GoblinBrute_hildir_mat", new MaterialSnow(0.7f, 0.79f));
            creatureMaterials.Add("GoblinBrute_mat", new MaterialSnow(0.7f, 0.75f));
            creatureMaterials.Add("GoblinShaman_mat", new MaterialSnow(0.45f, 0.65f, "Shaman"));
            creatureMaterials.Add("GoblinShaman_Hildir_mat", new MaterialSnow(0.66f, 0.72f, "Shaman"));
            creatureMaterials.Add("DvergerBody", new MaterialSnow(0.75f, 0.765f));
            creatureMaterials.Add("DvergerBodyashlands_mat", new MaterialSnow(0.74f, 0.765f));
            creatureMaterials.Add("Skeleton", new MaterialSnow(0.68f, 0.78f));
            creatureMaterials.Add("Skeleton_dark", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Skeleton_Swamps", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("SkeletonBig", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Skeleton_Mountains", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Skeleton_Meadows", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Draugr_mat", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Draugr_Archer_mat", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("Draugr_elite_mat", new MaterialSnow(0.7f, 0.8f));
            creatureMaterials.Add("troll", new MaterialSnow(0.65f, 0.72f));
            creatureMaterials.Add("lox", new MaterialSnow(0.7f, 0.75f, "Furr1"));
            creatureMaterials.Add("Bjorn_mat", new MaterialSnow(0.75f, 0.77f));
            creatureMaterials.Add("Boar Skin Valheim", new MaterialSnow(0.75f, 0.77f));
            creatureMaterials.Add("Deer 2", new MaterialSnow(0.755f, 0.765f));
            creatureMaterials.Add("greydwarf", new MaterialSnow(0.7f, 0.74f));

            playerCapeMaterials.Add("CapeLinen", new MaterialSnow(0.76f, 0.8f));
            playerCapeMaterials.Add("Ashcape_Mat", new MaterialSnow(0.76f, 0.79f));
            playerCapeMaterials.Add("asksvincape_mat", new MaterialSnow(0.7f, 0.79f));
            playerCapeMaterials.Add("NordCape_mat", new MaterialSnow(0.4f, 0.74f));
            playerCapeMaterials.Add("MageCape_mat", new MaterialSnow(0.75f, 0.78f));
            playerCapeMaterials.Add("feathercape_mat", new MaterialSnow(0.76f, 0.79f));
            playerCapeMaterials.Add("LoxCape_Mat", new MaterialSnow(0.75f, 0.79f));
            playerCapeMaterials.Add("CapeTrollHide", new MaterialSnow(0.75f, 0.79f));
            playerCapeMaterials.Add("CapeDeerHide", new MaterialSnow(0.76f, 0.81f));
            playerCapeMaterials.Add("WolfCape", new MaterialSnow(0.5f, 0.75f, "WolfCape_cloth"));
            playerCapeMaterials.Add("WolfCapeChain", new MaterialSnow(0.5f, 0.7f, "WolfCape"));
        }
    }
}
