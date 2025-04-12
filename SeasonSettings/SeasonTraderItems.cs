using BepInEx;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    [Serializable]
    public class SeasonTraderItems 
    {
        [Serializable]
        public class TradeableItem
        {
            public string prefab;
            public int stack = 1;
            public int price = 1;
            public string requiredGlobalKey = "";

            public override string ToString()
            {
                return $"{prefab}x{stack}, {price} coins {(!requiredGlobalKey.IsNullOrWhiteSpace() ? $", {requiredGlobalKey}" : "")}";
            }
        }

        public Dictionary<string, List<TradeableItem>> Spring = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Summer = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Fall = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<TradeableItem>> Winter = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);

        public SeasonTraderItems(bool loadDefaults = false)
        {
            if (!loadDefaults)
                return;

            Spring.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Honey", price = 200, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "RawMeat", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "NeckTail", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "DeerMeat", price = 200, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "WolfMeat", price = 350, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "LoxMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "HareMeat", price = 500, stack = 10, requiredGlobalKey = "defeated_queen" },
                                 new TradeableItem { prefab = "SerpentMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_serpent" }
                             });

            Fall.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Raspberry", price = 150, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Blueberries", price = 200, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Carrot", price = 300, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "Turnip", price = 350, stack = 10, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "Onion", price = 400, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "Barley", price = 500, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "Flax", price = 500, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                                 new TradeableItem { prefab = "Cloudberry", price = 300, stack = 10, requiredGlobalKey = "defeated_goblinking" },
                             });

            Winter.Add("haldor", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Honey", price = 300, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "Acorn", price = 100, stack = 1, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "BeechSeeds", price = 50, stack = 10, requiredGlobalKey = "defeated_eikthyr" },
                                 new TradeableItem { prefab = "BirchSeeds", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "FirCone", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "PineCone", price = 150, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "CarrotSeeds", price = 50, stack = 10, requiredGlobalKey = "defeated_gdking" },
                                 new TradeableItem { prefab = "TurnipSeeds", price = 80, stack = 10, requiredGlobalKey = "defeated_bonemass" },
                                 new TradeableItem { prefab = "OnionSeeds", price = 100, stack = 10, requiredGlobalKey = "defeated_dragon" },
                                 new TradeableItem { prefab = "SerpentMeat", price = 500, stack = 5, requiredGlobalKey = "defeated_serpent" },
                                 new TradeableItem { prefab = "SerpentScale", price = 300, stack = 5, requiredGlobalKey = "defeated_serpent" },
                                 new TradeableItem { prefab = "Bloodbag", price = 500, stack = 10, requiredGlobalKey = "killed_surtling" },
                             });

            Summer.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetMidsummerCrown", price = 100, stack = 1},
                             });

            Fall.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetPointyHat", price = 300, stack = 1},
                             });

            Winter.Add("hildir", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "HelmetYule", price = 100, stack = 1},
                             });

            Summer.Add("bogwitch", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Root", price = 250, stack = 5},
                             });

            Fall.Add("bogwitch", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Pukeberries", price = 100, stack = 10},
                             });

            Winter.Add("bogwitch", new List<TradeableItem>
                             {
                                 new TradeableItem { prefab = "Resin", price = 200, stack = 20},
                             });
        }

        public void AddSeasonalTraderItems(Trader trader, List<Trader.TradeItem> itemList)
        {
            foreach (TradeableItem item in GetCurrentSeasonalTraderItems(trader))
            {
                if (string.IsNullOrEmpty(item.requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                {
                    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.prefab);

                    if (itemPrefab == null)
                        continue;

                    ItemDrop prefab = itemPrefab.GetComponent<ItemDrop>();
                    if (prefab == null)
                        continue;

                    if (itemList.Exists(x => x.m_prefab == prefab))
                    {
                        Trader.TradeItem itemTrader = itemList.First(x => x.m_prefab == prefab);
                        itemTrader.m_price = item.price;
                        itemTrader.m_stack = item.stack;
                        itemTrader.m_requiredGlobalKey = item.requiredGlobalKey;
                    }
                    else
                    {
                        itemList.Add(new Trader.TradeItem
                        {
                            m_prefab = prefab,
                            m_price = item.price,
                            m_stack = item.stack,
                            m_requiredGlobalKey = item.requiredGlobalKey
                        });
                    }
                }
            }
        }

        private List<TradeableItem> GetCurrentSeasonalTraderItems(Trader trader)
        {
            List<string> traderNames = new List<string>
            {
                trader.name,
                Utils.GetPrefabName(trader.gameObject),
                trader.m_name,
                trader.m_name.ToLower().Replace("$npc_", ""),
                Localization.instance.Localize(trader.m_name),
            };

            Season season = seasonState.GetCurrentSeason();

            foreach (string traderName in traderNames)
            {
                List<TradeableItem> list = GetSeasonItems(traderName, season);
                if (list != null)
                    return list;
            }

            return new List<TradeableItem>();
        }

        private Dictionary<string, List<TradeableItem>> GetSeasonList(Season season)
        {
            return season switch
            {
                Season.Spring => Spring,
                Season.Summer => Summer,
                Season.Fall => Fall,
                Season.Winter => Winter,
                _ => new Dictionary<string, List<TradeableItem>>()
            };
        }

        private List<TradeableItem> GetSeasonItems(string trader, Season season)
        {
            Dictionary<string, List<TradeableItem>> list = GetSeasonList(season);
            if (list.ContainsKey(trader))
                return list[trader];

            return null;
        }
    }
}