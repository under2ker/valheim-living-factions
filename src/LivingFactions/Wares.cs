using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>One line of the trade list: something a culture offers for coins.</summary>
    internal class Ware
    {
        public string Culture;
        /// <summary>sell = ordinary goods, relic = an item with a named property, mystery = an unidentified relic.</summary>
        public string Kind;
        public string Item;
        public int Amount;
        public int Price;
        public int MinReputation;
        public string Affix;
    }

    /// <summary>
    /// The special wares of each faction, read from BepInEx/config/LivingFactions/trade.cfg so that
    /// assortment and prices can be changed without rebuilding the mod. Relics are ordinary items
    /// carrying one property (stored in the item's custom data); a mystery item hides its property
    /// until the faction trusts the buyer enough to tell them what it is.
    /// </summary>
    internal static class Wares
    {
        public const string RelicKey = "lf_relic";
        /// <summary>Stag's Horn: buying from the traders is 10% cheaper and wielding it costs 20% less stamina.</summary>
        public const string MerchantAffix = "merchant";
        public const string MysteryKey = "lf_unid";
        public const int IdentifyReputation = 30;

        private static readonly string[] Affixes = { "keen", "stout", "light", "lasting" };
        private static List<Ware> _all;

        private const string Defaults = @"# Living Factions trade list. One offer per line:
#   culture  kind  item  amount  price  minReputation  [affix]
# culture: clan_raven clan_wolf clan_stag fuling_horde dvergr_hold greydwarf_brood draugr_court, or * for all
# kind:    sell    - ordinary goods for coins
#          relic   - an item with a property: keen (+25% damage), stout (+20% armour and block),
#                    light (half weight), lasting (double durability)
#          mystery - an unidentified relic; its property is revealed by the faction at reputation 30
# item:    the game's item prefab name. Delete this file to get the defaults back.

*            sell    ArrowFlint           20   18    0
*            sell    Torch                 1    4  -15
clan_raven   sell    ArrowFire            20   40   10
clan_raven   relic   Bow                   1  180   30  light
clan_raven   mystery SpearFlint            1   60    0
clan_wolf    sell    ShieldWood            1   30    0
clan_wolf    relic   LF_WolfFangAxe        1  420   40  keen
clan_wolf    mystery AxeBronze             1  120    0
clan_stag    sell    Honey                10   25    0
clan_stag    sell    CapeLinen             1  120   20
clan_stag    relic   LF_StagHornAxe         1 1500   60  merchant
clan_stag    relic   ShieldBronzeBuckler   1  260   30  lasting
clan_stag    mystery KnifeFlint            1   45    0
dvergr_hold  sell    Bronze                5   95    0
dvergr_hold  relic   SwordIron             1  520   40  keen
dvergr_hold  relic   HelmetIron            1  380   30  stout
dvergr_hold  mystery MaceBronze            1  140    0
fuling_horde relic   AtgeirBronze          1  360   20  keen
fuling_horde relic   HelmetBronze          1  300   20  stout
fuling_horde mystery Club                  1   30  -30
draugr_court relic   SwordIron             1  450   10  lasting
";

        private static string FilePath { get { return Path.Combine(SimHost.DataDir, "trade.cfg"); } }

        public static void Reload() { _all = null; }

        public static List<Ware> For(Faction f)
        {
            if (_all == null) Load();
            List<Ware> list = new List<Ware>();
            foreach (Ware w in _all)
            {
                if (w.Culture == "*" || w.Culture == f.CultureId) list.Add(w);
            }
            return list;
        }

        private static void Load()
        {
            _all = new List<Ware>();
            try
            {
                Directory.CreateDirectory(SimHost.DataDir);
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Defaults);
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 6)
                    {
                        Plugin.Log.LogWarning("trade.cfg: skipped line '" + line + "'");
                        continue;
                    }
                    _all.Add(new Ware
                    {
                        Culture = p[0], Kind = p[1].ToLowerInvariant(), Item = p[2],
                        Amount = int.Parse(p[3], CultureInfo.InvariantCulture), Price = int.Parse(p[4], CultureInfo.InvariantCulture),
                        MinReputation = int.Parse(p[5], CultureInfo.InvariantCulture), Affix = p.Length > 6 ? p[6].ToLowerInvariant() : ""
                    });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read trade.cfg: " + e.Message);
            }
        }

        /// <summary>Puts the bought goods into the inventory, marked as a relic or a mystery where the offer says so.</summary>
        public static bool Deliver(Ware w, Faction f, Inventory inv)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(w.Item);
            if (prefab == null || !inv.CanAddItem(prefab, w.Amount)) return false;
            if (w.Kind == "sell") return inv.AddItem(prefab, w.Amount);

            ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_durability = item.GetMaxDurability();
            if (w.Kind == "relic")
            {
                item.m_customData[RelicKey] = w.Affix;
                item.m_crafterName = Texts.FactionName(f);
            }
            else
            {
                item.m_customData[MysteryKey] = f.CultureId;
                item.m_crafterName = "?";
            }
            bool added = inv.AddItem(item);
            Plugin.Log.LogInfo("Delivered " + w.Kind + " " + w.Item + (w.Affix != "" ? " [" + w.Affix + "]" : "") + ": " + (added ? "in inventory" : "refused"));
            return added;
        }

        public static bool IsMystery(ItemDrop.ItemData item, Faction f)
        {
            string culture;
            return item != null && item.m_customData.TryGetValue(MysteryKey, out culture) && culture == f.CultureId;
        }

        /// <summary>The faction tells the owner what the thing is; which property it has was fixed when it was made.</summary>
        public static string Identify(ItemDrop.ItemData item, Faction f)
        {
            int roll = Mathf.Abs((item.m_dropPrefab != null ? item.m_dropPrefab.name : "").GetStableHashCode() + f.Id * 7919);
            string affix = Affixes[roll % Affixes.Length];
            item.m_customData.Remove(MysteryKey);
            item.m_customData[RelicKey] = affix;
            item.m_crafterName = Texts.FactionName(f);
            item.m_durability = item.GetMaxDurability();
            return affix;
        }

        public static string AffixOf(ItemDrop.ItemData item)
        {
            string affix;
            return item != null && item.m_customData.TryGetValue(RelicKey, out affix) ? affix : null;
        }

        /// <summary>True while the player carries an item with this relic property.</summary>
        public static bool Carries(Player player, string affix)
        {
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (AffixOf(item) == affix) return true;
            }
            return false;
        }

        /// <summary>The price multiplier for buying from the traders: the local player with a merchant's axe pays 10% less.</summary>
        public static float BuyMultiplier(long playerId)
        {
            Player player = Player.m_localPlayer;
            return player != null && player.GetPlayerID() == playerId && Carries(player, MerchantAffix) ? 0.9f : 1f;
        }
    }

    [HarmonyPatch]
    internal static class RelicPatches
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float)), HarmonyPostfix]
        private static void Damage(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
        {
            string affix = Wares.AffixOf(__instance);
            if (affix == "keen") __result.Modify(1.25f);
            // Blackgnaw: a quarter harder in the hand of someone with less than a third of their health left.
            Player me = Player.m_localPlayer;
            if (affix == "fury" && me != null && me.GetCurrentWeapon() == __instance && me.GetHealth() < me.GetMaxHealth() / 3f) __result.Modify(1.25f);
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetArmor), typeof(int), typeof(float)), HarmonyPostfix]
        private static void Armor(ItemDrop.ItemData __instance, ref float __result)
        {
            if (Wares.AffixOf(__instance) == "stout") __result *= 1.2f;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float)), HarmonyPostfix]
        private static void Block(ItemDrop.ItemData __instance, ref float __result)
        {
            if (Wares.AffixOf(__instance) == "stout") __result *= 1.2f;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight)), HarmonyPostfix]
        private static void Weight(ItemDrop.ItemData __instance, ref float __result)
        {
            if (Wares.AffixOf(__instance) == "light") __result *= 0.5f;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetMaxDurability), typeof(int)), HarmonyPostfix]
        private static void Durability(ItemDrop.ItemData __instance, ref float __result)
        {
            if (Wares.AffixOf(__instance) == "lasting") __result *= 2f;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina)), HarmonyPrefix]
        private static void UseStamina(Player __instance, ref float v)
        {
            // Stamina spent by the local player while wielding a merchant's axe costs 20% less.
            if (__instance == Player.m_localPlayer && Wares.AffixOf(__instance.GetCurrentWeapon()) == Wares.MerchantAffix) v *= 0.8f;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool)), HarmonyPostfix]
        private static void Tooltip(ItemDrop.ItemData item, ref string __result)
        {
            if (item == null) return;
            string affix = Wares.AffixOf(item);
            if (affix != null) __result += "\n\n<color=orange>" + Texts.Get("relic_title") + ": " + Texts.Get("affix_" + affix) + "</color>";
            else if (item.m_customData.ContainsKey(Wares.MysteryKey)) __result += "\n\n<color=#B080FF>" + Texts.Get("relic_unknown") + "</color>";
        }
    }
}
