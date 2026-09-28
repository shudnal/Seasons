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

            public bool ShouldSerializebuildup() => buildup != SnowBuildup.Seasonal;

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

            // Vanilla
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
            pieces.Add("charcoal_kiln", new PieceSnow(position: new SnowPosition(y: 1.5f), scale: new SnowScale(y: 3.5f)));
            pieces.Add("piece_beehive", new PieceSnow());
            pieces.Add("stone_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("flint_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("grausten_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("coal_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_barrel", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("skull_pile", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_arch", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.46f)));
            pieces.Add("smelter", new PieceSnow(position: new SnowPosition(y: 3.8f)));
            pieces.Add("piece_bed02", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.24f)));
            pieces.Add("bed", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.14f)));
            pieces.Add("piece_chest_grausten", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 0.75f)));
            pieces.Add("stave_gate", new PieceSnow(buildup: SnowBuildup.Reduced, position: new SnowPosition(y: 6.14f), scale: new SnowScale(x: 1.11f, y: 1.0f, z: 1.3f)));
            pieces.Add("stone_fence", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_2x1", position: new SnowPosition(y: 0.19f), scale: new SnowScale(x: 1.9f, y: 1.0f, z: 0.75f)));
            pieces.Add("wood_fence_gate", new PieceSnow(buildup: SnowBuildup.Disabled));

            // Additional vanilla non-roof pieces with native snow caps.
            // Roofs, wood_fence and crafting facilities retain the normal seasonal range.
            pieces.Add("wood_wall_half", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodwall", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_a", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_67_a", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_45_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_67_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_pole", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_pole2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_beam_1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_beam", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_top", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_top_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_roof_top_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_door", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_gate", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_window", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_dragon1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stake_wall", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_sharpstakes", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_pole_log", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_pole_log_4", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_log", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_wall_log_4x0.5", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_log_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_log_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_wall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_wall_2x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_wall_4x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_pillar", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodiron_pole", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodiron_beam", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodiron_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodiron_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("woodiron_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_decowall", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_pole", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_pole4", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_beam", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_beam4x4", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_arch", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_gate", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_raven", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("darkwood_wolf", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_2x1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_base_1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_basecorner", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tip", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_out_1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_outcorner", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_column_1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_column_2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("crystal_wall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_dvergr_stake_wall", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_dvergr_sharpstakes", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_hexagonal_door", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_quarterwall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_halfwall_1x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_arch", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_decowall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_decowall_divider", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_decowall_tree", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_roof_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_roof_26_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_roof_45_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_roof_67_a", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_roof_67_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_arch_big", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_pole_1m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_pole_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_beam_1m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_beam_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_cross_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_cross_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_cross_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_stakewall_blackwood", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_wall_1x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_window_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_wall_4x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_window_4x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_wall_arch", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_wall_arch_inverted", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbase_small", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbase_medium", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbeam_small", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbeam_medium", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbase_tapered", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillarbase_tapered_inverted", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillar_arch_small", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_grausten_pillar_arch", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_grausten_stonestair", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_flametal_pillar", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("Piece_flametal_beam", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_pole_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_pole_4m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_beam_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_beam_4m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_wall_cross_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_wall_cross_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_wall_cross_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_pole_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_beam_2m", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_beam_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_beam_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stave_deco_beam_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_quarterwall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_halfwall_1x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_26", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_26_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_45", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_45_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_67", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_67_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_26_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_26_upsidedown_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_45_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_45_upsidedown_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_67_upsidedown", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("scale_wall_roof_67_upsidedown_flipped", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_icecube", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_bed", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_throne01", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_throne02", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_blackmarble_throne", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_bone_throne", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_moose_throne", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chair02", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chair03", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_logbench01", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_bench01", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_blackmarble_bench", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_blackwood_bench01", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_bench_runed", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_table", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_table_oak", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_table_round", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_blackmarble_table", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_table_runed", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_table_runed_small", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_wood", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_private", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_blackmetal", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_chest_warderobe", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_dvergr_lantern_pole", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_fine_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_core_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_yggdrasil_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackwood_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_frost_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_tin_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_copper_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_bronze_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_iron_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_silver_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_blackmetal_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_flametal_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bar_gold_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("bone_stack", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_barber", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("piece_cartographytable", new PieceSnow(buildup: SnowBuildup.Reduced));

            // Native donor variants outside the standard build menu.
            pieces.Add("ashwood_arch_bottom", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_arch_top", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_beam_26_alt", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("ashwood_wall_beam_45_alt", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_2x2x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_base_2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_out_2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tile_floor_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tile_floor_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tile_wall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tile_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("blackmarble_tile_wall_2x4", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("stone_floor", new PieceSnow(buildup: SnowBuildup.Reduced));
            pieces.Add("wood_ledge", new PieceSnow(buildup: SnowBuildup.Reduced));

            // Clay pieces.
            pieces.Add("BCP_Clay2_Roof45", new PieceSnow(copyFrom: "piece_grausten_roof_45"));
            pieces.Add("BCP_Clay2_Roof45Arch", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch"));
            pieces.Add("BCP_Clay2_Roof45Arch_Corner", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch_corner"));
            pieces.Add("BCP_Clay2_Roof45Arch_Corner2", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch_corner2"));
            pieces.Add("BCP_Clay2_Roof45_Corner", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner"));
            pieces.Add("BCP_Clay2_Roof45_Corner2", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner2"));
            pieces.Add("BCP_ClayWall_Cross26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_cross_26"));
            pieces.Add("BCP_ClayWall_Cross26Alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top"));
            pieces.Add("BCP_ClayWall_Cross45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_cross_45"));
            pieces.Add("BCP_ClayWall_Cross45Alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top_45", position: new SnowPosition(x: -0.03f, y: -0.06f), scale: new SnowScale(y: 1.03f)));
            pieces.Add("BCP_ClayWall_Roof26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_26"));
            pieces.Add("BCP_ClayWall_Roof26UpsideDown", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_26_upsidedown"));
            pieces.Add("BCP_ClayWall_Roof45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45"));
            pieces.Add("BCP_ClayWall_Roof45UpsideDown", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_45_upsidedown"));

            // Core wood pieces.
            pieces.Add("BCW_CoreWood_Roof26", new PieceSnow(copyFrom: "wood_roof"));
            pieces.Add("BCW_CoreWood_Roof45", new PieceSnow(copyFrom: "wood_roof_45"));
            pieces.Add("BCW_CoreWood_RoofICorner26", new PieceSnow(copyFrom: "wood_roof_icorner"));
            pieces.Add("BCW_CoreWood_RoofICorner45", new PieceSnow(copyFrom: "wood_roof_icorner_45"));
            pieces.Add("BCW_CoreWood_RoofOCorner26", new PieceSnow(copyFrom: "wood_roof_ocorner"));
            pieces.Add("BCW_CoreWood_RoofOCorner45", new PieceSnow(copyFrom: "wood_roof_ocorner_45"));
            pieces.Add("BCW_CoreWood_RoofTop26", new PieceSnow(copyFrom: "wood_roof_top"));
            pieces.Add("BCW_CoreWood_RoofTop45", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("BCW_CoreWood_WallRoof26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_a"));
            pieces.Add("BCW_CoreWood_WallRoof45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45"));
            pieces.Add("BCW_CoreWood_WallRoofTop26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top"));
            pieces.Add("BCW_CoreWood_WallRoofTop45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top_45"));
            pieces.Add("BCW_CoreWood_WallRoof_UpsideDown26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_upsidedown"));
            pieces.Add("BCW_CoreWood_WallRoof_UpsideDown45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45_upsidedown"));

            // Fine wood, clay and stone pieces.
            pieces.Add("BFP_ClayRoof26", new PieceSnow(copyFrom: "darkwood_roof"));
            pieces.Add("BFP_ClayRoof45", new PieceSnow(copyFrom: "darkwood_roof_45"));
            pieces.Add("BFP_ClayRoofICorner26", new PieceSnow(copyFrom: "darkwood_roof_icorner"));
            pieces.Add("BFP_ClayRoofICorner45", new PieceSnow(copyFrom: "darkwood_roof_icorner_45"));
            pieces.Add("BFP_ClayRoofOCorner26", new PieceSnow(copyFrom: "darkwood_roof_ocorner"));
            pieces.Add("BFP_ClayRoofOCorner45", new PieceSnow(copyFrom: "darkwood_roof_ocorner_45"));
            pieces.Add("BFP_ClayRoofTop26", new PieceSnow(copyFrom: "darkwood_roof_top"));
            pieces.Add("BFP_ClayRoofTop45", new PieceSnow(copyFrom: "darkwood_roof_top_45"));
            pieces.Add("BFP_FineWoodRoof26", new PieceSnow(copyFrom: "wood_roof"));
            pieces.Add("BFP_FineWoodRoof45", new PieceSnow(copyFrom: "wood_roof_45"));
            pieces.Add("BFP_FineWoodRoofCross26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top"));
            pieces.Add("BFP_FineWoodRoofCross45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top_45"));
            pieces.Add("BFP_FineWoodRoofICorner26", new PieceSnow(copyFrom: "wood_roof_icorner"));
            pieces.Add("BFP_FineWoodRoofICorner45", new PieceSnow(copyFrom: "wood_roof_icorner_45"));
            pieces.Add("BFP_FineWoodRoofOCorner26", new PieceSnow(copyFrom: "wood_roof_ocorner"));
            pieces.Add("BFP_FineWoodRoofOCorner45", new PieceSnow(copyFrom: "wood_roof_ocorner_45"));
            pieces.Add("BFP_FineWoodRoofTop26", new PieceSnow(copyFrom: "wood_roof_top"));
            pieces.Add("BFP_FineWoodRoofTop45", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("BFP_FineWoodWallRoof26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_a"));
            pieces.Add("BFP_FineWoodWallRoof26_UpsideDown", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_upsidedown"));
            pieces.Add("BFP_FineWoodWallRoof45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45"));
            pieces.Add("BFP_FineWoodWallRoof45_UpsideDown", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45_upsidedown"));
            pieces.Add("BFP_StoneRoof26", new PieceSnow(copyFrom: "darkwood_roof"));
            pieces.Add("BFP_StoneRoof45", new PieceSnow(copyFrom: "darkwood_roof_45"));
            pieces.Add("BFP_StoneRoofICorner26", new PieceSnow(copyFrom: "darkwood_roof_icorner"));
            pieces.Add("BFP_StoneRoofICorner45", new PieceSnow(copyFrom: "darkwood_roof_icorner_45"));
            pieces.Add("BFP_StoneRoofOCorner26", new PieceSnow(copyFrom: "darkwood_roof_ocorner"));
            pieces.Add("BFP_StoneRoofOCorner45", new PieceSnow(copyFrom: "darkwood_roof_ocorner_45"));
            pieces.Add("BFP_StoneRoofTop26", new PieceSnow(copyFrom: "darkwood_roof_top"));
            pieces.Add("BFP_StoneRoofTop45", new PieceSnow(copyFrom: "darkwood_roof_top_45"));

            // Balrond roof variants.
            pieces.Add("ashwood_roof26_bal", new PieceSnow(copyFrom: "wood_roof"));
            pieces.Add("ashwood_roof45_bal", new PieceSnow(copyFrom: "wood_roof_45"));
            pieces.Add("ashwood_roof_icorner_45_bal", new PieceSnow(copyFrom: "wood_roof_icorner_45"));
            pieces.Add("ashwood_roof_ocorner_45_bal", new PieceSnow(copyFrom: "wood_roof_ocorner_45"));
            pieces.Add("ashwood_roof_top45_bal", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("core_wood_roof26_bal", new PieceSnow(copyFrom: "wood_roof"));
            pieces.Add("darkwood_roof_quarter_26_bal", new PieceSnow(copyFrom: "darkwood_roof"));
            pieces.Add("darkwood_roof_quarter_45_bal", new PieceSnow(copyFrom: "darkwood_roof_45"));
            pieces.Add("darkwood_roof_top_half_45_bal", new PieceSnow(copyFrom: "darkwood_roof_top_45"));
            pieces.Add("darkwood_roof_top_half_bal", new PieceSnow(copyFrom: "darkwood_roof_top"));
            pieces.Add("fineood_wall_roof_26_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_26"));
            pieces.Add("finewood_wall_roof_26_upsidedown_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_26_upsidedown"));
            pieces.Add("finewood_wall_roof_45_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45"));
            pieces.Add("finewood_wall_roof_45_upsidedown_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_roof_45_upsidedown"));
            pieces.Add("piece_grausten_roof_45_top_bal", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("piece_hardwood_roof_45_arch_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch"));
            pieces.Add("piece_hardwood_roof_45_arch_corner2_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch_corner2"));
            pieces.Add("piece_hardwood_roof_45_arch_corner_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45_arch_corner"));
            pieces.Add("piece_hardwood_roof_45_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45"));
            pieces.Add("piece_hardwood_roof_45_corner2_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner2"));
            pieces.Add("piece_hardwood_roof_45_corner_bal", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner"));
            pieces.Add("piece_hardwood_roof_45_top_bal", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("wood_roof_top_half_45_bal", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("wood_roof_top_half_bal", new PieceSnow(copyFrom: "wood_roof_top"));
            pieces.Add("wood_roof_top_quarter_45_bal", new PieceSnow(copyFrom: "wood_roof_45"));
            pieces.Add("wood_roof_top_quarter_bal", new PieceSnow(copyFrom: "wood_roof"));

            // Copper roofs.
            pieces.Add("copper_roof", new PieceSnow(copyFrom: "darkwood_roof"));
            pieces.Add("copper_roof_45", new PieceSnow(copyFrom: "darkwood_roof_45"));
            pieces.Add("copper_roof_icorner", new PieceSnow(copyFrom: "darkwood_roof_icorner"));
            pieces.Add("copper_roof_icorner_45", new PieceSnow(copyFrom: "darkwood_roof_icorner_45"));
            pieces.Add("copper_roof_ocorner", new PieceSnow(copyFrom: "darkwood_roof_ocorner"));
            pieces.Add("copper_roof_ocorner_45", new PieceSnow(copyFrom: "darkwood_roof_ocorner_45"));
            pieces.Add("copper_roof_top", new PieceSnow(copyFrom: "darkwood_roof_top"));
            pieces.Add("copper_roof_top_45", new PieceSnow(copyFrom: "darkwood_roof_top_45"));

            // OdinArchitect roof variants.
            pieces.Add("rae_big_wood_roof", new PieceSnow(copyFrom: "wood_roof"));
            pieces.Add("rae_big_wood_roof_46", new PieceSnow(copyFrom: "wood_roof_45"));
            pieces.Add("rae_big_wood_roof_icorner", new PieceSnow(copyFrom: "wood_roof_icorner"));
            pieces.Add("rae_big_wood_roof_icorner_46", new PieceSnow(copyFrom: "wood_roof_icorner_45"));
            pieces.Add("rae_big_wood_roof_ocorner", new PieceSnow(copyFrom: "wood_roof_ocorner"));
            pieces.Add("rae_big_wood_roof_ocorner_46", new PieceSnow(copyFrom: "wood_roof_ocorner_45"));
            pieces.Add("rae_big_wood_roof_top", new PieceSnow(copyFrom: "wood_roof_top"));
            pieces.Add("rae_big_wood_roof_top_46", new PieceSnow(copyFrom: "wood_roof_top_45"));
            pieces.Add("rae_stone_wall_roof", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_a", position: new SnowPosition(x: -0.15f, z: 0.05f), scale: new SnowScale(z: 2.5f)));
            pieces.Add("rae_stone_wall_roof45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45", scale: new SnowScale(z: 2.3f)));
            pieces.Add("rae_stone_wall_roof46_alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45_upsidedown", position: new SnowPosition(y: 1.75f, z: 0.08f), scale: new SnowScale(z: 2.2f)));
            pieces.Add("rae_stone_wall_roof_alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_upsidedown", position: new SnowPosition(y: 0.8f, z: 0.1f), scale: new SnowScale(z: 2.2f)));
            pieces.Add("rae_stone_wall_roof_x", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top_45", scale: new SnowScale(x: 0.95f, z: 1.5f)));
            pieces.Add("rae_stone_wall_roof_xs", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_top", scale: new SnowScale(x: 0.95f, z: 1.5f)));

            // Additional construction variants
            pieces.Add("BCP_ClayArch_Bottom", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_arch_bottom"));
            pieces.Add("BCP_ClayArch_Top", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_arch_top"));
            pieces.Add("BCP_ClayBeam1m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_beam_1m", position: new SnowPosition(z: -0.001f)));
            pieces.Add("BCP_ClayBeam2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_beam_2m", position: new SnowPosition(z: -0.001f)));
            pieces.Add("BCP_ClayDecoWall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_decowall_2x2"));
            pieces.Add("BCP_ClayHalfWall_1x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_halfwall_1x2"));
            pieces.Add("BCP_ClayPillarArch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillar_arch"));
            pieces.Add("BCP_ClayPillarArch_Small", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillar_arch_small"));
            pieces.Add("BCP_ClayPillarBase_Medium", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_medium"));
            pieces.Add("BCP_ClayPillarBase_Small", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_small"));
            pieces.Add("BCP_ClayPillarBase_Tapered", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_tapered"));
            pieces.Add("BCP_ClayPillarBase_TaperedInverted", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_tapered_inverted"));
            pieces.Add("BCP_ClayPillarBeam_Medium", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbeam_medium"));
            pieces.Add("BCP_ClayPillarBeam_Small", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbeam_small"));
            pieces.Add("BCP_ClayPole1m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_pole_1m"));
            pieces.Add("BCP_ClayPole_2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_pole_2m"));
            pieces.Add("BCP_ClayQuarterWall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_quarterwall_1x1"));
            pieces.Add("BCP_ClayWall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_2x2"));
            pieces.Add("BCP_ClayWall_Beam26Alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_beam_26_alt"));
            pieces.Add("BCP_ClayWall_Beam45Alt", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_beam_45_alt"));
            pieces.Add("BFP_ClayArch2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_arch", position: new SnowPosition(y: 0.46f)));
            pieces.Add("BFP_ClayBase1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_base_1"));
            pieces.Add("BFP_ClayBase2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_base_2"));
            pieces.Add("BFP_ClayBaseCorner", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_basecorner"));
            pieces.Add("BFP_ClayBlock1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("BFP_ClayBlock2x1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x1x1"));
            pieces.Add("BFP_ClayBlock2x2x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x1"));
            pieces.Add("BFP_ClayBlock2x2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x2", position: new SnowPosition(y: 0.73f)));
            pieces.Add("BFP_ClayBlockOut1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_1"));
            pieces.Add("BFP_ClayBlockOut2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_2"));
            pieces.Add("BFP_ClayBlock_OutCorner", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_outcorner"));
            pieces.Add("BFP_ClayColumn1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_column_1"));
            pieces.Add("BFP_ClayColumn2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_column_2"));
            pieces.Add("BFP_ClayFloor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_floor", position: new SnowPosition(y: 0.23f)));
            pieces.Add("BFP_ClayFloorTriangle", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_floor_triangle"));
            pieces.Add("BFP_ClayPillar", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_pillar"));
            pieces.Add("BFP_ClayStair", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_stair"));
            pieces.Add("BFP_ClayTileFloor1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_floor_1x1"));
            pieces.Add("BFP_ClayTileFloor2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_floor_2x2"));
            pieces.Add("BFP_ClayTileWall1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_1x1", position: new SnowPosition(x: -0.01914843f, y: 0.224839017f)));
            pieces.Add("BFP_ClayTileWall2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_2x2", position: new SnowPosition(x: -0.00107443f, y: 0.7735491f, z: 0.08772652f)));
            pieces.Add("BFP_ClayTileWall2x4", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_2x4", position: new SnowPosition(x: 0.01196333f, y: 1.76693439f, z: 0.106212579f)));
            pieces.Add("BFP_ClayTip", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tip"));
            pieces.Add("BFP_FineWoodBeam1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("BFP_FineWoodBeam26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_26"));
            pieces.Add("BFP_FineWoodBeam2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("BFP_FineWoodBeam45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_45"));
            pieces.Add("BFP_FineWoodDecoWall", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_decowall"));
            pieces.Add("BFP_FineWoodDoor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_door"));
            pieces.Add("BFP_FineWoodDragon", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_dragon1"));
            pieces.Add("BFP_FineWoodFloor1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor_1x1"));
            pieces.Add("BFP_FineWoodFloor2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.19f)));
            pieces.Add("BFP_FineWoodGate", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("BFP_FineWoodHalfWall", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half"));
            pieces.Add("BFP_FineWoodLedge", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_ledge"));
            pieces.Add("BFP_FineWoodPole1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("BFP_FineWoodPole2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("BFP_FineWoodStair", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_stair"));
            pieces.Add("BFP_FineWoodStepLadder", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_stepladder"));
            pieces.Add("BFP_FineWoodWall", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("BFP_FineWoodWindowShutter", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_window"));
            pieces.Add("BFP_HeavyGate", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_gate"));
            pieces.Add("Piece_hardwood_pillar_arch_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillar_arch"));
            pieces.Add("Piece_hardwood_pillar_arch_small_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillar_arch_small"));
            pieces.Add("Piece_hardwood_pillarbase_medium_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_medium"));
            pieces.Add("Piece_hardwood_pillarbase_small_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_small"));
            pieces.Add("Piece_hardwood_pillarbase_tapered_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_tapered"));
            pieces.Add("Piece_hardwood_pillarbase_tapered_inverted_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbase_tapered_inverted"));
            pieces.Add("Piece_hardwood_pillarbeam_medium_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbeam_medium"));
            pieces.Add("Piece_hardwood_pillarbeam_small_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_pillarbeam_small"));
            pieces.Add("blackmarble_2x2x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x1"));
            pieces.Add("blackmarble_floor4m_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor"));
            pieces.Add("finewood_arch_bottom_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_arch_bottom"));
            pieces.Add("finewood_arch_top_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_arch_top"));
            pieces.Add("gabro_wall2x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_2x1"));
            pieces.Add("grausten_round_column_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_column_2"));
            pieces.Add("ig_tall_stairs", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_stair"));
            pieces.Add("iron_beam_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("iron_beam_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("iron_dragon", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_dragon1"));
            pieces.Add("iron_pole_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("iron_pole_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("piece_dvergr_stake_wall_big", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_dvergr_stake_wall"));
            pieces.Add("rae_BigLog27", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_log_26"));
            pieces.Add("rae_BigLogHorizontal_4m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_log"));
            pieces.Add("rae_BigLogHorizontal_8m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_log_4x0.5"));
            pieces.Add("rae_BigLogVertical_4m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole_log_4"));
            pieces.Add("rae_BigLogVertical_8m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole_log_4"));
            pieces.Add("rae_Bigger_Stone_Floor_4x4", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor_2x2", position: new SnowPosition(y: 0.23f)));
            pieces.Add("rae_StoneArchBig", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_arch", position: new SnowPosition(y: 0.46f)));
            pieces.Add("rae_StoneArchBig_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_arch", position: new SnowPosition(y: 0.46f)));
            pieces.Add("rae_StonePillar_4m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_pillar"));
            pieces.Add("rae_StonePillar_8m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_pillar"));
            pieces.Add("rae_StoneWallBig", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_2x1"));
            pieces.Add("rae_WoodBeamBig27", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_26"));
            pieces.Add("rae_WoodBeamBig46", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_45"));
            pieces.Add("rae_WoodBeamBig_2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("rae_WoodBeamBig_4m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("rae_WoodPoleBig_2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("rae_WoodPoleBig_4m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("rae_crystal_beam_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("rae_crystal_beam_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("rae_crystal_floorslab", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor"));
            pieces.Add("rae_crystal_pole_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("rae_crystal_pole_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("rae_darkwood_gate_crystal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_gate"));
            pieces.Add("rae_darkwood_gate_iron", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_gate"));
            pieces.Add("rae_irondeco_fence_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_decowall"));
            pieces.Add("refined_stakewall_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stake_wall"));
            pieces.Add("stone_beam_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("stone_beam_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("stone_floor4m_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor"));
            pieces.Add("stone_floor_1_new", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor"));
            pieces.Add("stone_floor_triangle_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_floor_triangle"));
            pieces.Add("stone_pole_long", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("stone_pole_short", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("stonemarble_1x1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("stonemarble_2x1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x1x1"));
            pieces.Add("stonemarble_2x1x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x1"));
            pieces.Add("stonemarble_2x2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x2", position: new SnowPosition(y: 0.73f)));
            pieces.Add("stonemarble_base_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_base_1"));
            pieces.Add("stonemarble_base_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_base_2"));
            pieces.Add("stonemarble_basecorner", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_basecorner"));
            pieces.Add("stonemarble_column_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_column_1"));
            pieces.Add("stonemarble_column_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_column_2"));
            pieces.Add("stonemarble_floor_triangle", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_floor_triangle"));
            pieces.Add("stonemarble_out_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_1"));
            pieces.Add("stonemarble_out_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_2"));
            pieces.Add("stonemarble_outcorner", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_outcorner"));
            pieces.Add("stonemarble_stair_corner", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_dvergr_spiralstair"));
            pieces.Add("stonemarble_stair_corner_left", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_dvergr_spiralstair_right"));
            pieces.Add("stonemarble_tile_wall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_1x1"));
            pieces.Add("stonemarble_tile_wall_2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_2x2"));
            pieces.Add("stonemarble_tile_wall_2x4", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_wall_2x4"));
            pieces.Add("stonemarble_tip", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tip"));
            pieces.Add("stonewall_hardrock_1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_1x1"));
            pieces.Add("stonewall_hardrock_2x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_2x1"));
            pieces.Add("stonewall_hardrock_4x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_4x2", scale: new SnowScale(z: 2f)));
            pieces.Add("stonewall_hardrock_arch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_arch", position: new SnowPosition(y: 0.46f), scale: new SnowScale(x: 0.92f)));
            pieces.Add("stonewall_hardrock_pillar", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_pillar"));
            pieces.Add("stonewall_hardrock_stairs", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_stair"));
            pieces.Add("thin_iron_beam_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("thin_iron_beam_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("thin_iron_pole_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("thin_iron_pole_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("thin_rae_crystal_beam_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("thin_rae_crystal_beam_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("thin_rae_crystal_pole_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("thin_rae_crystal_pole_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("thin_wood_beam_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("thin_wood_beam_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("thin_wood_pole_1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("thin_wood_pole_2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("wood_dragon_dark_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_dragon1"));
            pieces.Add("wood_ramp_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_roof"));
            pieces.Add("wood_spiralstair_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_dvergr_spiralstair"));
            pieces.Add("wood_spiralstair_right_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_dvergr_spiralstair_right"));
            pieces.Add("wood_stair_1m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_stair"));

            // Additional snow caps and placement corrections.
            pieces.Add("BCP_ClayArch_Big", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_arch_big"));
            pieces.Add("BCP_ClayDecoWall_Divider", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_decowall_divider"));
            pieces.Add("BCP_ClayDecoWall_Tree", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_decowall_tree"));
            pieces.Add("BCP_ClayDeco_Floor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_deco_floor"));
            pieces.Add("BCP_ClayFloor_1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_floor_1x1"));
            pieces.Add("BCP_ClayFloor_2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_floor_2x2"));
            pieces.Add("BCP_ClayStair2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_stair", position: new SnowPosition(y: 0.9f)));
            pieces.Add("BCP_ClayWall1x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_wall_1x2"));
            pieces.Add("BCP_ClayWall2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_wall_2x2"));
            pieces.Add("BCP_ClayWall4x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_wall_4x2"));
            pieces.Add("BCP_ClayWall_Arch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_arch"));
            pieces.Add("BCP_ClayWall_Arch2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_wall_arch"));
            pieces.Add("BCP_ClayWall_ArchInverted", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_wall_arch_inverted"));
            pieces.Add("BCP_ClayWall_Beam45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_beam_45"));
            pieces.Add("BCP_ClayWindow2x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_window_2x2"));
            pieces.Add("BCP_ClayWindow4x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_grausten_window_4x2"));
            pieces.Add("BFP_FineWoodArch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_arch"));
            pieces.Add("BFP_FineWoodWolf", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_wolf"));
            pieces.Add("ClayBeam_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("ClayPole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("Hayze_gate_01", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("Hayze_gate_03", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("Hayze_gate_04", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("Hayze_gate_06", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("ashwood_wall_roof_45", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_roof_45"));
            pieces.Add("clay_floor_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor_2x2", position: new SnowPosition(y: 0.23f)));
            pieces.Add("dvergrprops_wood_floor_custom", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("dvergrprops_wood_floor_custom_big", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("finewood_floor1x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor_1x1"));
            pieces.Add("finewood_floor2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("finewood_stair_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_stair", position: new SnowPosition(y: 0.9f)));
            pieces.Add("finewood_wall_arch_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_arch"));
            pieces.Add("gabro_barkwood_beam_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_1"));
            pieces.Add("iron_beam_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_flametal_beam"));
            pieces.Add("iron_pole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "Piece_flametal_pillar"));
            pieces.Add("piece_hardwood_floor2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("piece_woodgate_crystal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("rae_darkwood_iron_wolf", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_wolf"));
            pieces.Add("stonemoss_tile_floor_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_tile_floor_2x2"));
            pieces.Add("wood_gate_cage_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("BCP_ClayWall_Beam26", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_wall_beam_26"));
            pieces.Add("BFP_ClayArch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_1"));
            pieces.Add("BFP_ClayBlock2x2_Enforced", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x2", position: new SnowPosition(y: 0.73f)));
            pieces.Add("BFP_FineWoodQuarterWall", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "crystal_wall_1x1"));
            pieces.Add("ClayCube_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("IG_Big_Hardwood_Floor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("IG_Big_Chevron_Floor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("IG_Chevron_Floor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("IG_Hardwood_Floor", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("blackmarble_1x1_enforced_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("blackmarble_2x2_enforced_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x2", position: new SnowPosition(y: 0.73f)));
            pieces.Add("blackmarble_arch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_1"));
            pieces.Add("blackmarble_beam_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("blackmarble_pole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole_log"));
            pieces.Add("coppermarble_1x1_enforced_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("crystal_tile_floor_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("darkwood_beam_26_deco_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_beam_26"));
            pieces.Add("darkwood_beam_45_deco_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_beam_45"));
            pieces.Add("darkwood_beam_deco_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_beam"));
            pieces.Add("darkwood_pole_deco_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_pole"));
            pieces.Add("darkwoodwall_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("decr_wall_half_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half"));
            pieces.Add("dvergrprops_wood_stair_custom", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_stair"));
            pieces.Add("emberwood_pillar2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("emberwood_pillar4_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_pole4", position: new SnowPosition(x: 0f, y: 2.7015f, z: 0f)));
            pieces.Add("finewood_beam2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_beam_2m"));
            pieces.Add("finewood_beam_26_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_26"));
            pieces.Add("finewood_beam_45_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam_45"));
            pieces.Add("finewood_beam_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_beam_1m"));
            pieces.Add("finewood_pole2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("finewood_pole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("gabro_barkwood_beam2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_beam"));
            pieces.Add("gabro_barkwood_pole2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("gabro_barkwood_pole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("gabro_counter1x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("gabro_counter_corner_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("gabro_floor1x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_1x1"));
            pieces.Add("gabro_floor2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_floor_2x2", position: new SnowPosition(y: 0.23f)));
            pieces.Add("gabro_stair_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_stair"));
            pieces.Add("obsidian_tile_floor_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: -0.15f)));
            pieces.Add("piece_dvergr_wood_custom", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_hexagonal_door"));
            pieces.Add("piece_woodgate_darkwood", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("piece_woodgate_iron", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_gate"));
            pieces.Add("rae_brickstone_wall_2x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x1x1", position: new SnowPosition(x: 1.00061f, y: -0.284f, z: 0.01012f)));
            pieces.Add("rae_woodwall_3", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("rae_woodwall_4", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("stone_1x1_enforced_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_1x1"));
            pieces.Add("stone_Wall_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x1"));
            pieces.Add("stone_pole2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole2"));
            pieces.Add("stone_pole_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("stone_railing_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half", position: new SnowPosition(x: -0.521f, y: 0.7f, z: 0.011f)));
            pieces.Add("stonemarble_2x2x2_enforced", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_2x2x2", position: new SnowPosition(y: 0.73f)));
            pieces.Add("stonemarble_arch", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "blackmarble_out_1"));
            pieces.Add("tiledwood_floor_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "ashwood_deco_floor"));
            pieces.Add("wood_beam4_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_beam4x4"));
            pieces.Add("wood_iron_log_beam4_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_log_4x0.5"));
            pieces.Add("wood_iron_log_pole4_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole_log_4"));
            pieces.Add("wood_pole4_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "darkwood_pole4", position: new SnowPosition(x: 0f, y: 2.7015f, z: 0f)));
            pieces.Add("wood_pole_log1m_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_pole"));
            pieces.Add("wood_railing_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half", position: new SnowPosition(x: -0.521f, y: 0.7f, z: 0.011f)));
            pieces.Add("wood_wall_quarter", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "crystal_wall_1x1"));
            pieces.Add("wood_wall_quarter_upper", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "crystal_wall_1x1"));
            pieces.Add("wood_windowiron_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_window"));
            pieces.Add("woodwall_deco_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("rae_brickstone_wall_4x2", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_4x2"));
            pieces.Add("rae_brickstone_wall_1x1", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_1x1", position: new SnowPosition(x: 0.66f, z: 0.08f)));
            pieces.Add("rae_brickstone_wall_singleslab", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "stone_wall_1x1", position: new SnowPosition(x: -0.03f, y: 0.08f, z: 0.08f)));
            pieces.Add("wood_wall_half_upper", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half", position: new SnowPosition(y: 0.8f)));
            pieces.Add("finewood_wall_2x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "woodwall"));
            pieces.Add("finewood_wall_2x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "crystal_wall_1x1"));
            pieces.Add("finewood_wall_1x2_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_wall_half"));
            pieces.Add("finewood_wall_1x1_bal", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "crystal_wall_1x1", position: new SnowPosition(y: 0.2f)));
            pieces.Add("roof_wood_45_right", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner2", position: new SnowPosition(x: -0.2f, y: 1f, z: 0f)));
            pieces.Add("roof_darkwood_45_right", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner2", position: new SnowPosition(x: -0.2f, y: 1f, z: 0f)));
            pieces.Add("roof_wood_45_left", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner", position: new SnowPosition(x: -0.2f, y: 1f, z: 0f)));
            pieces.Add("roof_darkwood_45_left", new PieceSnow(copyFrom: "piece_grausten_roof_45_corner", position: new SnowPosition(x: 0.3f, y: 1f, z: 0f)));
            pieces.Add("roof_wood_2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: 0.3f)));
            pieces.Add("roof_darkwood_2m", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "wood_floor", position: new SnowPosition(y: 0.3f)));

            // OdinArchitect storage cabinets.
            pieces.Add("rae_iron_storage_big", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_chest_blackmetal", position: new SnowPosition(y: 0.79f, z: -0.3f), scale: new SnowScale(x: 0.89f)));
            pieces.Add("rae_bronze_storage_big", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_chest_blackmetal", position: new SnowPosition(y: 0.79f, z: -0.3f), scale: new SnowScale(x: 0.89f)));
            pieces.Add("rae_iron_storage_small", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_chest_blackmetal", position: new SnowPosition(y: 0.29f, z: -0.3f), scale: new SnowScale(x: 0.89f)));
            pieces.Add("rae_bronze_storage_small", new PieceSnow(buildup: SnowBuildup.Reduced, copyFrom: "piece_chest_blackmetal", position: new SnowPosition(y: 0.29f, z: -0.3f), scale: new SnowScale(x: 0.89f)));

            // Vanilla
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
