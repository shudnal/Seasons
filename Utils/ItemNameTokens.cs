using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seasons
{
    public static class ItemNameTokens
    {
        public static readonly Dictionary<string, string> itemNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class Player_Load_UpdateRegisters
        {
            private static void Prefix() => UpdateRegisters();
        }

        public static void UpdateRegisters()
        {
            if (!ObjectDB.instance)
                return;

            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                if (item == null || item.GetComponent<ItemDrop>() is not ItemDrop itemDrop)
                    continue;

                if (itemDrop.m_itemData is not ItemDrop.ItemData itemData || itemData.m_shared is not ItemDrop.ItemData.SharedData shared || !shared.m_name.StartsWith("$"))
                    continue;

                itemNames[item.name] = shared.m_name;
                itemNames[shared.m_name] = shared.m_name;
            }
        }

        public static string GetItemName(this string input) => itemNames.GetValueOrDefault(input.Trim(), input);
    }
}
