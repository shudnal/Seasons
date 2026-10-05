using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace Seasons
{
    /// <summary>A hoe action using native piece selection and repair targeting, not terrain placement.</summary>
    internal static class SnowClearingTool
    {
        internal const string PrefabName = "Seasons_ClearSnow";
        internal const string NameToken = "$seasons_clear_snow";
        internal const string DescriptionToken = "$seasons_clear_snow_description";
        internal static readonly int HoeHash = "Hoe".GetStableHashCode();

        private static Piece action;
        private static ObjectDB registeredDatabase;
        private static bool inputHookReady;
        private static bool buttonsHookReady;
        private static bool searchHookReady;

        private static bool HooksReady => inputHookReady && buttonsHookReady && searchHookReady;
        internal static bool IsAction(Piece piece) => action && piece == action;
        internal static bool IsHoe(ItemDrop.ItemData tool) => tool != null && tool.m_dropPrefab && tool.m_dropPrefab.name == "Hoe";

        private static void Register(ObjectDB database)
        {
            if (!database || !HooksReady || !Seasons.UseTextureControllers())
                return;
            GameObject hoe = database.GetItemPrefab("Hoe");
            PieceTable table = hoe ? hoe.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (!table)
                return;
            if (!action)
            {
                GameObject hammer = database.GetItemPrefab("Hammer");
                PieceTable hammerTable = hammer ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                if (!hammerTable)
                    return;
                Piece repair = null;
                foreach (GameObject candidate in hammerTable.m_pieces)
                    if (candidate && candidate.TryGetComponent(out Piece piece) && piece.m_repairPiece && !piece.m_removePiece)
                    {
                        repair = piece;
                        break;
                    }
                if (!repair)
                    return;

                byte[] bytes = LocalizationManager.Localizer.ReadEmbeddedFileBytes("clear_snow.png", typeof(SnowClearingTool).Assembly);
                if (bytes == null)
                {
                    Seasons.LogWarning("Cannot register the snow-clearing hoe action: the embedded icon is missing.");
                    return;
                }
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = PrefabName };
                if (!texture.LoadImage(bytes, true))
                {
                    UnityEngine.Object.Destroy(texture);
                    Seasons.LogWarning("Cannot register the snow-clearing hoe action: the embedded icon could not be loaded.");
                    return;
                }
                GameObject prefab = CustomPrefabs.InitPrefabClone(repair.gameObject, PrefabName);
                action = prefab.GetComponent<Piece>();
                action.m_name = NameToken;
                action.m_description = DescriptionToken;
                action.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                action.m_enabled = true;
                action.m_repairPiece = true;
                action.m_removePiece = false;
                action.m_canRotate = false;
                action.m_resources = Array.Empty<Piece.Requirement>();
                action.m_craftingStation = null;
                action.m_dlc = String.Empty;
                action.m_usage = Piece.UsageTagFlags.Misc;
                action.m_category = table.m_categories.Count > 0 && (int)table.m_categories[0] >= 0 &&
                    (int)table.m_categories[0] < (int)Piece.PieceCategory.Max
                    ? table.m_categories[0] : Piece.PieceCategory.Misc;
            }
            AddToTable(table);
            registeredDatabase = database;
        }

        private static void AddToTable(PieceTable table)
        {
            if (action && table && !table.m_pieces.Contains(action.gameObject))
                table.m_pieces.Add(action.gameObject);
        }

        private static void PrepareAvailablePieces(Player player)
        {
            if (!player || !player.m_buildPieces)
                return;
            if (!action || registeredDatabase != ObjectDB.instance)
                Register(ObjectDB.instance);
            if (!action || !HooksReady)
                return;
            // Support a hoe whose table was cloned by another mod, without modifying that table's removal policy.
            if (IsHoe(player.GetRightItem()))
                AddToTable(player.m_buildPieces);
            if (player.m_buildPieces.m_pieces.Contains(action.gameObject))
                player.m_knownRecipes.Add(action.m_name);
        }

        private static void DispatchRepair(Player player, ItemDrop.ItemData tool, Piece selected)
        {
            if (!IsAction(selected))
            {
                player.Repair(tool, selected);
                return;
            }
            // Never fall back to repairing health if an integration hook becomes unavailable.
            if (HooksReady)
                SnowClearingAction.Clear(player, tool);
            else
                player.Message(MessageHud.MessageType.TopLeft, "$msg_nosnow");
        }

        private static bool IsSpecialRepair(Piece piece) => piece.m_repairPiece && !IsAction(piece);

        private static IEnumerable<CodeInstruction> RouteUiRepairFlag(IEnumerable<CodeInstruction> instructions, bool buttons)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo flag = AccessTools.Field(typeof(Piece), nameof(Piece.m_repairPiece));
            MethodInfo replacement = AccessTools.Method(typeof(SnowClearingTool), nameof(IsSpecialRepair));
            int matches = 0;
            foreach (CodeInstruction instruction in code)
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, flag))
                    matches++;
            bool ready = matches > 0;
            if (buttons)
                buttonsHookReady = ready;
            else
                searchHookReady = ready;
            if (!ready)
            {
                if (action)
                    action.m_enabled = false;
                Seasons.LogWarning($"Snow-clearing UI integration is unavailable in BuildUi.{(buttons ? "UpdatePieceButtons" : "UpdateSearch")}; the hoe action is disabled.");
                return code;
            }
            foreach (CodeInstruction instruction in code)
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, flag))
                {
                    // The replacement consumes the same Piece and leaves one bool. Keep all labels and blocks.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
            return code;
        }

        [HarmonyPatch]
        private static class ObjectDB_RegisterSnowClearing
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.Awake));
                yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB));
            }
            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            private static void Postfix(ObjectDB __instance) => Register(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdateAvailablePiecesList))]
        private static class Player_UpdateAvailablePiecesList_SnowClearing
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance) => PrepareAvailablePieces(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
        private static class Player_UpdatePlacement_SnowClearing
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                MethodInfo repair = AccessTools.Method(typeof(Player), nameof(Player.Repair));
                int matches = code.FindAll(instruction => instruction.Calls(repair)).Count;
                inputHookReady = matches == 1;
                if (!inputHookReady)
                {
                    if (action)
                        action.m_enabled = false;
                    Seasons.LogWarning($"Snow-clearing input integration expected one Player.Repair call, found {matches}; the hoe action is disabled.");
                    return code;
                }
                foreach (CodeInstruction instruction in code)
                    if (instruction.Calls(repair))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(SnowClearingTool), nameof(DispatchRepair));
                    }
                return code;
            }
        }

        // Fail closed for an external direct Repair call or a later incompatible UpdatePlacement replacement.
        // The normal snow action bypasses Repair entirely, including other mods' repair callbacks.
        [HarmonyPatch(typeof(Player), nameof(Player.Repair))]
        private static class Player_Repair_ProtectSnowClearing
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static bool Prefix(Piece repairPiece) => !IsAction(repairPiece);
        }

        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.UpdatePieceButtons))]
        private static class BuildUi_Buttons_SnowClearing
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                RouteUiRepairFlag(instructions, buttons: true);
        }

        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.UpdateSearch))]
        private static class BuildUi_Search_SnowClearing
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
                RouteUiRepairFlag(instructions, buttons: false);
        }
    }
}
