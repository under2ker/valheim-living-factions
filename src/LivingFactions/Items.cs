using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>How the game's items translate into the simulation's five goods.</summary>
    internal static class Items
    {
        private struct Worth
        {
            public Res Res;
            public float Units;

            public Worth(Res res, float units) { Res = res; Units = units; }
        }

        // Keyed by item prefab name. Units = how much of the good one item is worth.
        private static readonly Dictionary<string, Worth> Table = new Dictionary<string, Worth>
        {
            { "Wood", new Worth(Res.Wood, 1f) }, { "FineWood", new Worth(Res.Wood, 2f) }, { "RoundLog", new Worth(Res.Wood, 2f) },
            { "ElderBark", new Worth(Res.Wood, 2.5f) }, { "YggdrasilWood", new Worth(Res.Wood, 3f) }, { "Blackwood", new Worth(Res.Wood, 3f) },
            { "Stone", new Worth(Res.Stone, 1f) }, { "Flint", new Worth(Res.Stone, 1.5f) }, { "Obsidian", new Worth(Res.Stone, 3f) },
            { "BlackMarble", new Worth(Res.Stone, 3f) }, { "Grausten", new Worth(Res.Stone, 3f) },
            { "CopperOre", new Worth(Res.Metal, 1f) }, { "TinOre", new Worth(Res.Metal, 0.8f) }, { "Copper", new Worth(Res.Metal, 2f) },
            { "Tin", new Worth(Res.Metal, 1.5f) }, { "Bronze", new Worth(Res.Metal, 3f) }, { "IronScrap", new Worth(Res.Metal, 2f) },
            { "Iron", new Worth(Res.Metal, 4f) }, { "SilverOre", new Worth(Res.Metal, 2.5f) }, { "Silver", new Worth(Res.Metal, 5f) },
            { "BlackMetalScrap", new Worth(Res.Metal, 3f) }, { "BlackMetal", new Worth(Res.Metal, 6f) },
            { "Coins", new Worth(Res.Coins, 1f) },
            { "RawMeat", new Worth(Res.Food, 1f) }, { "DeerMeat", new Worth(Res.Food, 1f) }, { "NeckTail", new Worth(Res.Food, 0.8f) },
            { "FishRaw", new Worth(Res.Food, 1f) }, { "Barley", new Worth(Res.Food, 0.5f) }, { "BarleyFlour", new Worth(Res.Food, 0.6f) },
        };

        /// <summary>The item a settlement hands over when a player buys a good, and how many units one is worth.</summary>
        public static string SalePrefab(Res res, Settlement s, out float unitsPerItem)
        {
            switch (res)
            {
                case Res.Wood: unitsPerItem = 1f; return "Wood";
                case Res.Stone: unitsPerItem = 1f; return "Stone";
                case Res.Food: unitsPerItem = 2f; return "CookedMeat";
                case Res.Metal:
                    if (s.Tier >= Tier.Town) { unitsPerItem = 4f; return "Iron"; }
                    unitsPerItem = 2f;
                    return "Copper";
                default: unitsPerItem = 1f; return "Coins";
            }
        }

        public static bool TryValue(ItemDrop.ItemData item, out Res res, out float units)
        {
            res = Res.Wood;
            units = 0f;
            if (item == null || item.m_dropPrefab == null) return false;
            Worth worth;
            if (Table.TryGetValue(item.m_dropPrefab.name, out worth))
            {
                res = worth.Res;
                units = worth.Units;
                return true;
            }
            // Anything edible counts as food, by how filling it is.
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && item.m_shared.m_food > 0f)
            {
                res = Res.Food;
                units = Mathf.Clamp(item.m_shared.m_food / 15f, 0.5f, 6f);
                return true;
            }
            return false;
        }
    }
}
