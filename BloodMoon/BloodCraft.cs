using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodCraft
    {
        internal const string SchemaKey = "Seasons.BloodCraft.Schema";
        internal const string WorldUidKey = "Seasons.BloodCraft.WorldUid";
        internal const string EventIdKey = "Seasons.BloodCraft.EventId";
        internal const string OwnerPlayerIdKey = "Seasons.BloodCraft.OwnerPlayerId";
        private const int Schema = 2;
        private const float PendingValidationGrace = 5f;

        private static readonly Dictionary<Recipe, Recipe> cloneByOriginal = new Dictionary<Recipe, Recipe>();
        private static readonly Dictionary<Recipe, Recipe> originalByClone = new Dictionary<Recipe, Recipe>();
        private static readonly Dictionary<int, Color> craftButtonBaseColors = new Dictionary<int, Color>();
        private static readonly Dictionary<Inventory, float> pendingInventoryValidation = new Dictionary<Inventory, float>();
        private static long cloneEventId = -1L;

        [ThreadStatic]
        private static ItemDrop.ItemData stackCandidate;

        internal static bool IsAvailableFor(Player player)
        {
            if (player == null || player != Player.m_localPlayer || GetCurrentWorldUid() == 0L)
                return false;
            BloodMoonEventPhase phase = BloodMoonNetwork.ClientGlobal.Phase;
            if (phase != BloodMoonEventPhase.Marked && phase != BloodMoonEventPhase.Active && phase != BloodMoonEventPhase.AutoCompleting)
                return false;
            return BloodMoonInteractionRules.IsLocalParticipantActiveOrMarked();
        }

        internal static bool IsBloodRecipe(Recipe recipe)
        {
            return recipe != null && originalByClone.ContainsKey(recipe);
        }

        internal static bool IsTemporary(ItemDrop.ItemData item)
        {
            return TryReadMarker(item, out _, out _);
        }

        internal static bool TryReadMarker(ItemDrop.ItemData item, out long eventId, out long ownerPlayerId)
        {
            return TryReadMarker(item, out _, out eventId, out ownerPlayerId);
        }

        private static bool TryReadMarker(ItemDrop.ItemData item, out long worldUid, out long eventId, out long ownerPlayerId)
        {
            worldUid = 0L;
            eventId = -1L;
            ownerPlayerId = 0L;
            if (item?.m_customData == null || !item.m_customData.TryGetValue(SchemaKey, out string schemaText) ||
                !item.m_customData.TryGetValue(WorldUidKey, out string worldText) || !item.m_customData.TryGetValue(EventIdKey, out string eventText) ||
                !item.m_customData.TryGetValue(OwnerPlayerIdKey, out string ownerText))
                return false;
            return int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int schema) && schema == Schema &&
                long.TryParse(worldText, NumberStyles.Integer, CultureInfo.InvariantCulture, out worldUid) && worldUid != 0L &&
                long.TryParse(eventText, NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) && eventId >= 0L &&
                long.TryParse(ownerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ownerPlayerId) && ownerPlayerId != 0L;
        }

        internal static bool HasMarker(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null)
                return false;
            return item.m_customData.ContainsKey(SchemaKey) || item.m_customData.ContainsKey(WorldUidKey) ||
                item.m_customData.ContainsKey(EventIdKey) || item.m_customData.ContainsKey(OwnerPlayerIdKey);
        }

        internal static bool IsValidFor(Player player, ItemDrop.ItemData item)
        {
            return player != null && TryReadMarker(item, out long worldUid, out long eventId, out long ownerId) &&
                worldUid == GetCurrentWorldUid() && eventId == BloodMoonNetwork.ClientGlobal.EventId && ownerId == player.GetPlayerID() && IsAvailableFor(player);
        }

        internal static void AppendAvailableRecipes(Player player, List<Recipe> available)
        {
            InventoryGui gui = InventoryGui.instance;
            if (player == null || available == null || gui == null || ObjectDB.instance == null || !InventoryGui.IsVisible() || !IsAvailableFor(player))
                return;

            bool craftTab = gui.InCraftTab();
            bool upgradeTab = gui.InUpradeTab();
            if (!craftTab && !upgradeTab)
                return;

            EnsureCloneEvent();
            if (craftTab)
            {
                foreach (Recipe original in ObjectDB.instance.m_recipes)
                {
                    if (!IsEligibleKnownRecipe(player, original))
                        continue;
                    Recipe clone = GetOrCreateClone(original);
                    if (clone != null && !available.Contains(clone))
                        available.Add(clone);
                }
                return;
            }

            HashSet<string> temporaryNames = new HashSet<string>(player.GetInventory().GetAllItems()
                .Where(item => IsValidFor(player, item) && item.m_quality < item.m_shared.m_maxQuality)
                .Select(item => item.m_shared.m_name));
            if (temporaryNames.Count == 0)
                return;

            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe?.m_item == null || !temporaryNames.Contains(recipe.m_item.m_itemData.m_shared.m_name))
                    continue;
                if (!available.Contains(recipe))
                    available.Add(recipe);
            }
        }

        internal static bool HandleCrafting(InventoryGui gui, Player player)
        {
            if (gui == null || player == null || gui.m_craftRecipe == null)
                return false;

            bool newBloodItem = IsBloodRecipe(gui.m_craftRecipe) && gui.m_craftUpgradeItem == null;
            bool temporaryUpgrade = gui.m_craftUpgradeItem != null && IsTemporary(gui.m_craftUpgradeItem);
            if (!newBloodItem && !temporaryUpgrade)
                return false;

            if (!IsAvailableFor(player))
            {
                player.Message(MessageHud.MessageType.Center, "Blood Craft is no longer available.");
                return true;
            }

            if (newBloodItem)
                CraftTemporary(gui, player, gui.m_craftRecipe);
            else
                UpgradeTemporary(gui, player, gui.m_craftUpgradeItem);
            return true;
        }

        internal static void CleanupLocal(Player player)
        {
            CleanupRecipeClones();
            pendingInventoryValidation.Clear();
            if (player == null)
                return;

            Inventory inventory = player.GetInventory();
            List<ItemDrop.ItemData> remove = inventory.GetAllItems().Where(HasMarker).ToList();
            foreach (ItemDrop.ItemData item in remove)
            {
                if (player.IsItemEquiped(item))
                    player.UnequipItem(item, triggerEquipEffects: false);
                inventory.RemoveItem(item);
            }
            if (remove.Count > 0)
            {
                inventory.Changed();
                LogInfo($"[BloodMoon.Craft] Removed {remove.Count} temporary item stack(s) from {player.GetPlayerName()}.");
            }
        }

        internal static void QueueInventoryValidation(Inventory inventory)
        {
            if (inventory != null && inventory.GetAllItems().Any(HasMarker))
                pendingInventoryValidation[inventory] = Time.realtimeSinceStartup;
        }

        internal static void TickLocal()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            Inventory playerInventory = player.GetInventory();
            bool playerHasMarkers = playerInventory.GetAllItems().Any(HasMarker);
            if (pendingInventoryValidation.Count == 0 && !playerHasMarkers)
                return;

            float now = Time.realtimeSinceStartup;
            bool activeSnapshotKnown = BloodMoonNetwork.ClientGlobal.EventId >= 0L;
            if (playerHasMarkers && !activeSnapshotKnown && !pendingInventoryValidation.ContainsKey(playerInventory))
                pendingInventoryValidation[playerInventory] = now;

            foreach (KeyValuePair<Inventory, float> pending in pendingInventoryValidation.ToArray())
            {
                if (!activeSnapshotKnown && now - pending.Value < PendingValidationGrace)
                    continue;
                CleanupInvalidInventory(pending.Key, player);
                pendingInventoryValidation.Remove(pending.Key);
            }

            if (activeSnapshotKnown && playerInventory.GetAllItems().Any(HasMarker))
                CleanupInvalidInventory(playerInventory, player);
        }

        internal static bool CanStack(ItemDrop.ItemData first, ItemDrop.ItemData second)
        {
            bool firstMarked = HasMarker(first);
            bool secondMarked = HasMarker(second);
            if (!firstMarked && !secondMarked)
                return true;
            if (!TryReadMarker(first, out long firstWorld, out long firstEvent, out long firstOwner) ||
                !TryReadMarker(second, out long secondWorld, out long secondEvent, out long secondOwner))
                return false;
            return firstWorld == secondWorld && firstEvent == secondEvent && firstOwner == secondOwner;
        }

        internal static void StyleRecipeRow(InventoryGui gui, Recipe recipe, ItemDrop.ItemData item)
        {
            if (gui == null || gui.m_availableRecipes.Count == 0 || (!IsBloodRecipe(recipe) && !IsTemporary(item)))
                return;

            GameObject element = gui.m_availableRecipes[gui.m_availableRecipes.Count - 1].InterfaceElement;
            if (element == null)
                return;
            Image background = element.GetComponent<Image>();
            if (background != null)
                background.color = Color.Lerp(background.color, new Color(0.55f, 0.12f, 0.12f, background.color.a), 0.65f);
            TMP_Text name = element.transform.Find("name")?.GetComponent<TMP_Text>();
            if (name != null && !name.text.Contains("Blood Craft"))
                name.text += " <color=#b85a5a>[Blood Craft]</color>";
        }

        internal static void StyleCraftButton(InventoryGui gui)
        {
            if (gui?.m_craftButton == null || gui.m_craftButton.image == null)
                return;
            Image image = gui.m_craftButton.image;
            int id = image.GetInstanceID();
            if (!craftButtonBaseColors.TryGetValue(id, out Color baseColor))
            {
                baseColor = image.color;
                craftButtonBaseColors[id] = baseColor;
            }

            bool bloodSelection = IsBloodRecipe(gui.m_selectedRecipe.Recipe) || IsTemporary(gui.m_selectedRecipe.ItemData);
            image.color = bloodSelection ? Color.Lerp(baseColor, new Color(0.55f, 0.12f, 0.12f, baseColor.a), 0.7f) : baseColor;
        }

        private static void CraftTemporary(InventoryGui gui, Player player, Recipe recipe)
        {
            int multiplier = gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
            int amount = Mathf.Max(1, recipe.m_amount) * Mathf.Max(1, multiplier);
            ItemDrop.ItemData item = recipe.m_item.m_itemData.Clone();
            item.m_dropPrefab = recipe.m_item.gameObject;
            item.m_stack = amount;
            item.m_quality = 1;
            item.m_variant = gui.m_craftVariant;
            item.m_worldLevel = (byte)Game.m_worldLevel;
            item.m_crafterID = player.GetPlayerID();
            item.m_crafterName = player.GetPlayerName();
            Mark(item, BloodMoonNetwork.ClientGlobal.EventId, player.GetPlayerID());

            Inventory inventory = player.GetInventory();
            if (!CanAddTemporary(inventory, item))
            {
                player.Message(MessageHud.MessageType.Center, "$inventory_full");
                return;
            }
            if (!inventory.AddItem(item))
                return;

            FinishCraft(gui, player);
            LogInfo($"[BloodMoon.Craft] Crafted temporary {recipe.m_item.gameObject.name} x{amount} for event {BloodMoonNetwork.ClientGlobal.EventId}.");
        }

        private static void UpgradeTemporary(InventoryGui gui, Player player, ItemDrop.ItemData item)
        {
            if (!IsValidFor(player, item) || item.m_quality >= item.m_shared.m_maxQuality)
                return;
            item.m_quality++;
            item.m_durability = item.GetMaxDurability();
            player.GetInventory().Changed();
            FinishCraft(gui, player);
            LogInfo($"[BloodMoon.Craft] Upgraded temporary {item.m_dropPrefab?.name ?? item.m_shared.m_name} to quality {item.m_quality}.");
        }

        private static void FinishCraft(InventoryGui gui, Player player)
        {
            gui.UpdateCraftingPanel();
            CraftingStation station = player.GetCurrentCraftingStation();
            if (station != null)
                station.m_craftItemDoneEffects.Create(player.transform.position, Quaternion.identity);
            else
                gui.m_craftItemDoneEffects.Create(player.transform.position, Quaternion.identity);
        }

        private static bool CanAddTemporary(Inventory inventory, ItemDrop.ItemData item)
        {
            int remaining = item.m_stack;
            foreach (ItemDrop.ItemData existing in inventory.m_inventory)
            {
                if (existing.m_shared.m_name != item.m_shared.m_name || existing.m_quality != item.m_quality || existing.m_worldLevel != item.m_worldLevel || !CanStack(existing, item))
                    continue;
                remaining -= Mathf.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack);
                if (remaining <= 0)
                    return true;
            }
            int emptySlots = inventory.m_width * inventory.m_height - inventory.m_inventory.Count;
            return remaining <= emptySlots * Mathf.Max(1, item.m_shared.m_maxStackSize);
        }

        private static bool IsEligibleKnownRecipe(Player player, Recipe recipe)
        {
            if (recipe?.m_item == null || recipe.m_item.m_itemData?.m_shared == null)
                return false;
            ItemDrop.ItemData item = recipe.m_item.m_itemData;
            if (item.m_shared.m_questItem || item.m_shared.m_buildPieces != null || !player.m_knownRecipes.Contains(item.m_shared.m_name))
                return false;
            if (!string.IsNullOrEmpty(item.m_shared.m_dlc) && (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(item.m_shared.m_dlc)))
                return false;

            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                case ItemDrop.ItemData.ItemType.Trinket:
                case ItemDrop.ItemData.ItemType.Torch:
                    return true;
                case ItemDrop.ItemData.ItemType.Consumable:
                    return IsBattleConsumable(recipe);
                default:
                    return false;
            }
        }

        private static bool IsBattleConsumable(Recipe recipe)
        {
            ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
            bool foodOrDrink = shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f || shared.m_isDrink;
            if (!foodOrDrink)
                return true;
            HashSet<string> allow = new HashSet<string>((BloodMoonConfig.BloodCraftFoodAndMead.Value ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()), StringComparer.OrdinalIgnoreCase);
            return allow.Contains(recipe.m_item.gameObject.name);
        }

        private static Recipe GetOrCreateClone(Recipe original)
        {
            if (original == null)
                return null;
            if (cloneByOriginal.TryGetValue(original, out Recipe clone) && clone != null)
                return clone;

            clone = UnityEngine.Object.Instantiate(original);
            clone.name = original.name + "_SeasonsBloodCraft";
            clone.m_resources = Array.Empty<Piece.Requirement>();
            clone.m_craftingStation = null;
            clone.m_repairStation = null;
            clone.m_minStationLevel = 1;
            clone.m_requireOnlyOneIngredient = false;
            cloneByOriginal[original] = clone;
            originalByClone[clone] = original;
            return clone;
        }

        private static void EnsureCloneEvent()
        {
            long eventId = BloodMoonNetwork.ClientGlobal.EventId;
            if (cloneEventId == eventId)
                return;
            CleanupRecipeClones();
            cloneEventId = eventId;
        }

        private static void CleanupRecipeClones()
        {
            foreach (Recipe clone in originalByClone.Keys.ToArray())
            {
                if (clone != null)
                    UnityEngine.Object.Destroy(clone);
            }
            cloneByOriginal.Clear();
            originalByClone.Clear();
            cloneEventId = -1L;
            craftButtonBaseColors.Clear();
        }

        private static void Mark(ItemDrop.ItemData item, long eventId, long playerId)
        {
            long worldUid = GetCurrentWorldUid();
            item.m_customData[SchemaKey] = Schema.ToString(CultureInfo.InvariantCulture);
            item.m_customData[WorldUidKey] = worldUid.ToString(CultureInfo.InvariantCulture);
            item.m_customData[EventIdKey] = eventId.ToString(CultureInfo.InvariantCulture);
            item.m_customData[OwnerPlayerIdKey] = playerId.ToString(CultureInfo.InvariantCulture);
        }

        private static void CleanupInvalidInventory(Inventory inventory, Player owner)
        {
            if (inventory == null)
                return;
            List<ItemDrop.ItemData> remove = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!HasMarker(item))
                    continue;
                if (!TryReadMarker(item, out long worldUid, out long eventId, out long ownerId) || owner == null || inventory != owner.GetInventory() ||
                    worldUid != GetCurrentWorldUid() || eventId != BloodMoonNetwork.ClientGlobal.EventId || ownerId != owner.GetPlayerID() || !IsAvailableFor(owner))
                    remove.Add(item);
            }
            foreach (ItemDrop.ItemData item in remove)
            {
                if (owner != null && owner.IsItemEquiped(item))
                    owner.UnequipItem(item, triggerEquipEffects: false);
                inventory.RemoveItem(item);
            }
        }

        private static bool CanEnterInventory(Inventory inventory, ItemDrop.ItemData item)
        {
            if (!HasMarker(item))
                return true;
            Player player = Player.m_localPlayer;
            if (player == null)
                return true;
            return inventory == player.GetInventory() && TryReadMarker(item, out long worldUid, out long eventId, out long ownerId) &&
                worldUid == GetCurrentWorldUid() && eventId == BloodMoonNetwork.ClientGlobal.EventId && ownerId == player.GetPlayerID();
        }

        private static void DestroyWorldTemporary(ItemDrop itemDrop)
        {
            if (itemDrop == null || !HasMarker(itemDrop.m_itemData) || itemDrop.m_nview == null || !itemDrop.m_nview.IsValid() || !itemDrop.m_nview.IsOwner())
                return;
            LogWarning($"[BloodMoon.Craft] Destroying temporary world item {itemDrop.gameObject.name}.");
            ZNetScene.instance?.Destroy(itemDrop.gameObject);
        }

        private static long GetCurrentWorldUid()
        {
            return ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
        private static class PlayerGetAvailableRecipesPatch
        {
            private static void Postfix(Player __instance, ref List<Recipe> available) => AppendAvailableRecipes(__instance, available);
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.AddRecipeToList))]
        private static class InventoryGuiAddRecipePatch
        {
            private static void Prefix(Recipe recipe, ItemDrop.ItemData item, ref bool canCraft)
            {
                if (IsBloodRecipe(recipe) || IsTemporary(item))
                    canCraft = true;
            }

            private static void Postfix(InventoryGui __instance, Recipe recipe, ItemDrop.ItemData item)
            {
                StyleRecipeRow(__instance, recipe, item);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetRecipe))]
        private static class InventoryGuiSetRecipePatch
        {
            private static void Postfix(InventoryGui __instance) => StyleCraftButton(__instance);
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        private static class InventoryGuiDoCraftingPatch
        {
            private static bool Prefix(InventoryGui __instance, Player player)
            {
                return !HandleCrafting(__instance, player);
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class InventoryGuiHidePatch
        {
            private static void Postfix() => CleanupRecipeClones();
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.FindFreeStackItem))]
        private static class InventoryFindFreeStackPatch
        {
            private static void Postfix(Inventory __instance, string name, int quality, float worldLevel, ref ItemDrop.ItemData __result)
            {
                ItemDrop.ItemData candidate = stackCandidate;
                if (candidate == null || (__result != null && CanStack(candidate, __result)))
                    return;
                __result = __instance.m_inventory.FirstOrDefault(item => item.m_shared.m_name == name && item.m_quality == quality &&
                    item.m_stack < item.m_shared.m_maxStackSize && (float)item.m_worldLevel == worldLevel && CanStack(candidate, item));
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData) })]
        private static class InventoryAddItemPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result, out ItemDrop.ItemData __state)
            {
                __state = stackCandidate;
                stackCandidate = item;
                if (CanEnterInventory(__instance, item))
                    return true;
                __result = false;
                return false;
            }

            private static void Postfix(ItemDrop.ItemData __state) => stackCandidate = __state;
            private static Exception Finalizer(Exception __exception, ItemDrop.ItemData __state)
            {
                stackCandidate = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
        private static class InventoryAddItemAtPositionPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result, out ItemDrop.ItemData __state)
            {
                __state = stackCandidate;
                stackCandidate = item;
                if (CanEnterInventory(__instance, item))
                    return true;
                __result = false;
                return false;
            }

            private static void Postfix(ItemDrop.ItemData __state) => stackCandidate = __state;
            private static Exception Finalizer(Exception __exception, ItemDrop.ItemData __state)
            {
                stackCandidate = __state;
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
        private static class InventoryAddItemAmountPositionPatch
        {
            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int x, int y, ref bool __result)
            {
                if (!CanEnterInventory(__instance, item))
                {
                    __result = false;
                    return false;
                }
                ItemDrop.ItemData existing = __instance.GetItemAt(x, y);
                if (existing != null && !CanStack(item, existing))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
        private static class InventoryLoadPatch
        {
            private static void Postfix(Inventory __instance) => QueueInventoryValidation(__instance);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        private static class HumanoidDropItemPatch
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!HasMarker(item))
                    return true;
                if (__instance is Player player)
                    player.Message(MessageHud.MessageType.Center, "Blood Craft items cannot be dropped.");
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class HumanoidUseItemPatch
        {
            private static void Prefix(ItemDrop.ItemData item, ref bool fromInventoryGui)
            {
                if (HasMarker(item))
                    fromInventoryGui = true;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.TryUseItemOnInteractable))]
        private static class HumanoidTryUseItemOnInteractablePatch
        {
            private static bool Prefix(ItemDrop.ItemData item)
            {
                return !HasMarker(item);
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Load))]
        private static class ItemDropLoadPatch
        {
            private static void Postfix(ItemDrop __instance) => DestroyWorldTemporary(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class PlayerOnDeathPatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    CleanupLocal(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CustomFixedUpdate))]
        private static class PlayerCustomFixedUpdatePatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    TickLocal();
            }
        }
    }
}
