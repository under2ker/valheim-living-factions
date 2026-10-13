using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The settlement's goods as real items. The three chests of a storehouse hold a quarter of the
    /// settlement's stock (wood and stone, food, metal and coins) and follow it as it changes; every
    /// home has a chest of personal belongings. Chests are a two-way window onto the simulation:
    /// what a player takes is gone from the stock (and is theft), what they put in is a gift.
    /// </summary>
    internal static class Chests
    {
        private static readonly int HashFilled = "lf_filled".GetStableHashCode();
        private static readonly int[] HashCount = { "lf_n0".GetStableHashCode(), "lf_n1".GetStableHashCode() };
        private static readonly int HashCountSet = "lf_nset".GetStableHashCode();

        private static readonly List<Container> All = new List<Container>();
        private static float _next;

        private static readonly Res[][] StoreRoles =
        {
            new[] { Res.Wood, Res.Stone },
            new[] { Res.Food },
            new[] { Res.Metal, Res.Coins },
        };

        private static readonly int[] StoreCaps = { 100, 100, 40, 30, 200 };

        public static void Register(Container container) { All.Add(container); }

        public static void Reset() { All.Clear(); }

        public static void Update()
        {
            if (!SimHost.Active || Time.time < _next || !ModConfig.ChestsOn.Value) return;
            _next = Time.time + 4f;

            // Group the settlement chests that are loaded right now by building.
            Dictionary<long, List<Container>> groups = new Dictionary<long, List<Container>>();
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Container c = All[i];
                if (c == null)
                {
                    All.RemoveAt(i);
                    continue;
                }
                ZNetView view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                int sid = view.GetZDO().GetInt(Materializer.HashSettlement);
                if (sid == 0) continue;
                long key = ((long)sid << 32) | (uint)view.GetZDO().GetInt(Materializer.HashBuilding);
                List<Container> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<Container>();
                    groups[key] = list;
                }
                list.Add(c);
            }

            if (ModConfig.TradeOn.Value && Time.time >= _nextPlayerChests)
            {
                _nextPlayerChests = Time.time + 20f;
                TradeWithPlayerChests();
            }

            foreach (KeyValuePair<long, List<Container>> group in groups)
            {
                Settlement s = SimHost.Sim.State.GetSettlement((int)(group.Key >> 32));
                if (s == null) continue;
                int bid = (int)(group.Key & 0xFFFFFFFF);
                Building b = null;
                foreach (Building candidate in s.Buildings)
                {
                    if (candidate.Id == bid) b = candidate;
                }
                if (b == null) continue;

                if (b.Kind == BuildingKind.Storehouse)
                {
                    // A stable order, so each chest keeps its goods: sorted along a fixed world axis.
                    group.Value.Sort((x, y) => Order(x).CompareTo(Order(y)));
                    for (int i = 0; i < group.Value.Count && i < StoreRoles.Length; i++) SyncStore(s, group.Value[i], StoreRoles[i]);
                }
                else
                {
                    foreach (Container c in group.Value) SyncPersonal(s, b, c);
                }
            }
        }

        private static float _nextPlayerChests;

        /// <summary>
        /// A chest the player builds inside a settlement that is willing to trade becomes a trading
        /// post: the settlement buys the goods it has a use for out of it and leaves coins in return.
        /// </summary>
        private static void TradeWithPlayerChests()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            GameObject coins = ObjectDB.instance.GetItemPrefab("Coins");
            foreach (Container chest in All)
            {
                if (chest == null || chest.IsInUse()) continue;
                ZNetView view = chest.GetComponent<ZNetView>();
                Piece piece = chest.GetComponent<Piece>();
                if (view == null || !view.IsValid() || !view.IsOwner() || piece == null || piece.GetCreator() == 0L) continue;
                if (view.GetZDO().GetInt(Materializer.HashSettlement) != 0) continue;

                foreach (Settlement s in SimHost.Sim.State.Settlements)
                {
                    if (s.Destroyed) continue;
                    float reach = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 8f;
                    if (Utils.DistanceXZ(chest.transform.position, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > reach) continue;
                    Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
                    if (f == null || !SimHost.Sim.WillTrade(f, player.GetPlayerID())) break;

                    Inventory inv = chest.GetInventory();
                    int earned = 0;
                    foreach (ItemDrop.ItemData item in inv.GetAllItems().ToArray())
                    {
                        Res res;
                        float units;
                        if (!Items.TryValue(item, out res, out units) || res == Res.Coins) continue;
                        int amount = Mathf.Min(item.m_stack, 20);
                        int paid = Net.PlayerSell(s, res, Mathf.Max(1, Mathf.RoundToInt(amount * units)), player.GetPlayerID());
                        if (paid <= 0) continue;
                        inv.RemoveItem(item, amount);
                        earned += paid;
                    }
                    if (earned > 0)
                    {
                        if (coins != null) inv.AddItem(coins, earned);
                        if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get("msg_chest_trade", Texts.Name(s.Name), earned));
                    }
                    break;
                }
            }
        }

        private static float Order(Container c)
        {
            Vector3 p = c.transform.position;
            return p.x * 1.37f + p.z;
        }

        private static string SharedName(GameObject prefab)
        {
            return prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
        }

        private static void SyncStore(Settlement s, Container chest, Res[] goods)
        {
            if (chest.IsInUse()) return;
            ZDO zdo = chest.GetComponent<ZNetView>().GetZDO();
            Inventory inv = chest.GetInventory();
            bool tracked = zdo.GetBool(HashCountSet);
            for (int k = 0; k < goods.Length; k++)
            {
                Res res = goods[k];
                float units;
                GameObject prefab = ObjectDB.instance.GetItemPrefab(Items.SalePrefab(res, s, out units));
                if (prefab == null) continue;
                string name = SharedName(prefab);
                int current = inv.CountItems(name);
                if (tracked)
                {
                    int last = zdo.GetInt(HashCount[k]);
                    if (current < last) Taken(s, res, (last - current) * units);
                    else if (current > last) Given(s, res, (current - last) * units);
                }
                // An abandoned settlement keeps whatever is left; nobody restocks it.
                int desired = s.Destroyed ? current : Mathf.Min(StoreCaps[(int)res], Mathf.FloorToInt(s.Stock[(int)res] * 0.25f / units));
                if (desired > current) inv.AddItem(prefab, desired - current);
                else if (desired < current) inv.RemoveItem(name, current - desired);
                zdo.Set(HashCount[k], inv.CountItems(name));
            }
            zdo.Set(HashCountSet, true);
        }

        private static void SyncPersonal(Settlement s, Building b, Container chest)
        {
            if (chest.IsInUse()) return;
            ZDO zdo = chest.GetComponent<ZNetView>().GetZDO();
            Inventory inv = chest.GetInventory();
            if (!zdo.GetBool(HashFilled))
            {
                zdo.Set(HashFilled, true);
                FillPersonal(s, b, inv);
                zdo.Set(HashCount[0], Total(inv));
                return;
            }
            int total = Total(inv);
            int last = zdo.GetInt(HashCount[0]);
            if (total < last) Taken(s, Res.Coins, (last - total) * 2f);
            zdo.Set(HashCount[0], total);
        }

        private static int Total(Inventory inv)
        {
            int n = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems()) n += item.m_stack;
            return n;
        }

        /// <summary>What a household keeps: a little coin, food, and odds and ends that improve with the settlement.</summary>
        private static void FillPersonal(Settlement s, Building b, Inventory inv)
        {
            System.Random rnd = new System.Random(s.Seed * 31 + b.Id);
            int tier = (int)s.Tier;
            Add(inv, "Coins", rnd.Next(4, 14) * (1 + tier));
            Add(inv, Pick(rnd, "CookedMeat", "Raspberry", "Honey", "CookedDeerMeat", "Mushroom"), rnd.Next(2, 6));
            Add(inv, Pick(rnd, "LeatherScraps", "DeerHide", "Flint", "Resin", "Feathers"), rnd.Next(2, 8));
            if (rnd.Next(100) < 60) Add(inv, tier >= 2 ? "ArrowFlint" : "ArrowWood", rnd.Next(8, 21));
            if (tier >= 2 && rnd.Next(100) < 50) Add(inv, Pick(rnd, "Bronze", "Copper", "Tin"), rnd.Next(1, 4));
            if (tier >= 3 && rnd.Next(100) < 50) Add(inv, Pick(rnd, "Iron", "Amber", "AmberPearl"), rnd.Next(1, 3));
            if (b.Kind == BuildingKind.GreatHall) Add(inv, Pick(rnd, "Ruby", "AmberPearl", "SilverNecklace"), rnd.Next(1, 3));
        }

        private static string Pick(System.Random rnd, params string[] options) { return options[rnd.Next(options.Length)]; }

        private static void Add(Inventory inv, string prefabName, int amount)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab != null && amount > 0) inv.AddItem(prefab, amount);
        }

        private static void Taken(Settlement s, Res res, float units)
        {
            if (res != Res.Coins || units > 0f) Net.AddStock(s, res, -units);
            Player player = Player.m_localPlayer;
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            if (player == null || f == null || s.Destroyed) return;
            Net.NamePlayer(f, player.GetPlayerID(), player.GetPlayerName());
            bool noticed = Net.PlayerStole(f, player.GetPlayerID(), units * Balance.CoinValue[(int)res]);
            if (noticed && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get("msg_theft", Texts.Name(s.Name)));
        }

        private static void Given(Settlement s, Res res, float units)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Net.AddStock(s, res, units);
                return;
            }
            Net.PlayerGift(s, player.GetPlayerID(), res, units);
        }

        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Container c in All)
            {
                if (c == null) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetInt(Materializer.HashSettlement) == 0) continue;
                sb.Append("s" + view.GetZDO().GetInt(Materializer.HashSettlement) + " b" + view.GetZDO().GetInt(Materializer.HashBuilding) + ":");
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems()) sb.Append(" " + item.m_dropPrefab.name + "x" + item.m_stack);
                sb.AppendLine();
            }
            return sb.Length == 0 ? "no settlement chests loaded" : sb.ToString().TrimEnd();
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance) { Chests.Register(__instance); }
    }
}
