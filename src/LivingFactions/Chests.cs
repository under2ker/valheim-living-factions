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

        public static void Reset()
        {
            All.Clear();
            OpenedHere.Clear();
            RecentGifts.Clear();
        }

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

        private static readonly Dictionary<ZDOID, float> OpenedHere = new Dictionary<ZDOID, float>();

        /// <summary>The local player opened this chest: what is missing or added when it is next looked at was their doing.</summary>
        public static void MarkOpened(Container chest)
        {
            ZNetView view = chest != null ? chest.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || view.GetZDO().GetInt(Materializer.HashSettlement) == 0) return;
            OpenedHere[view.GetZDO().m_uid] = Time.time;
        }

        /// <summary>
        /// True once after the local player has had this chest open. Only then does a change in it count as
        /// theft or a gift. A chest whose contents differ from the count with nobody here having opened it
        /// (the chest changed hands between players' games before the new contents arrived, as happens to a
        /// guest walking into a village) is counted again and nothing else: before, it read as theft.
        /// </summary>
        private static bool OpenedByMe(ZDO zdo)
        {
            return OpenedHere.Remove(zdo.m_uid);
        }

        private static void SyncStore(Settlement s, Container chest, Res[] goods)
        {
            if (chest.IsInUse()) return;
            ZDO zdo = chest.GetComponent<ZNetView>().GetZDO();
            Inventory inv = chest.GetInventory();
            bool tracked = zdo.GetBool(HashCountSet) && OpenedByMe(zdo), stoleAny = false;
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
                    if (current < last)
                    {
                        float stolen = TakeBackGift(s, zdo, res, (last - current) * units);
                        Taken(s, res, stolen);
                        stoleAny |= stolen > 0f;
                    }
                    else if (current > last) RecordGift(zdo, res, (current - last) * units, Given(s, res, (current - last) * units));
                }
                // An abandoned settlement keeps whatever is left; nobody restocks it. A quarter of the stock is out
                // in the chest, and what the carriers brought in on top of that (Deliver), up to half of it.
                int shown = Mathf.FloorToInt(s.Stock[(int)res] * 0.25f / units), most = Mathf.FloorToInt(s.Stock[(int)res] * MostShown / units);
                // The cap is for the quarter: what the carriers brought lies on top of it, as long as the chest has room.
                int quarter = Mathf.Min(StoreCaps[(int)res], shown);
                int extra = Mathf.Clamp(zdo.GetInt(HashExtra[k]), 0, Mathf.Max(0, most - quarter));
                zdo.Set(HashExtra[k], extra);
                int desired = s.Destroyed ? current : quarter + extra;
                if (desired > current) inv.AddItem(prefab, desired - current);
                else if (desired < current) inv.RemoveItem(name, current - desired);
                zdo.Set(HashCount[k], inv.CountItems(name));
            }
            stoleAny |= SyncHauled(s, zdo, inv, tracked);
            zdo.Set(HashCountSet, true);
            if (tracked) NightThief.Checked(s.Id, stoleAny);
        }

        private static readonly int[] HashExtra = { "lf_x0".GetStableHashCode(), "lf_x1".GetStableHashCode() };
        private static readonly int HashHauled = "lf_haul".GetStableHashCode();

        /// <summary>At most this share of a good's stock is out in the storehouse chests, the carriers' loads included.</summary>
        private const float MostShown = 0.5f;

        /// <summary>
        /// What the carriers brought into a storehouse chest that is not the chest's own kind of goods (the
        /// field's carrots, a hunter's meat): part of the stock too, so taking it is theft; and no more of it
        /// stays than a quarter of that stock (what is eaten is gone from the chest as well).
        /// </summary>
        private static bool SyncHauled(Settlement s, ZDO zdo, Inventory inv, bool tracked)
        {
            string text = zdo.GetString(HashHauled);
            if (text.Length == 0) return false;
            Dictionary<string, int> hauled = Parse(text);
            float[] room = new float[s.Stock.Length];
            for (int r = 0; r < room.Length; r++) room[r] = s.Stock[r] * 0.25f;
            bool stole = false;
            foreach (string key in new List<string>(hauled.Keys))
            {
                ItemDrop drop = DropOf(key);
                Res res;
                float units;
                if (drop == null || !Items.TryValue(drop.m_itemData, out res, out units) || units <= 0f)
                {
                    hauled.Remove(key);
                    continue;
                }
                string name = drop.m_itemData.m_shared.m_name;
                int have = inv.CountItems(name), had = hauled[key];
                if (have < had && tracked)
                {
                    Taken(s, res, (had - have) * units);
                    stole = true;
                }
                int left = Mathf.Min(have, had);
                int keep = s.Destroyed ? left : Mathf.Min(left, Mathf.FloorToInt(Mathf.Max(0f, room[(int)res]) / units));
                if (keep < left) inv.RemoveItem(name, left - keep);
                room[(int)res] -= keep * units;
                hauled[key] = keep;
            }
            zdo.Set(HashHauled, Join(hauled));
            return stole;
        }

        private static ItemDrop DropOf(string prefabName)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }

        /// <summary>
        /// The chest a carrier puts a load of that good into: the storehouse chest that holds it when one is
        /// loaded here, else a chest of the building they worked at (a workshop's), else none.
        /// </summary>
        public static Container DeliveryChest(Settlement s, int workedAt, Res res)
        {
            List<Container> store = new List<Container>();
            Container workshop = null;
            foreach (Container c in All)
            {
                if (c == null) continue;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetInt(Materializer.HashSettlement) != s.Id) continue;
                int bid = view.GetZDO().GetInt(Materializer.HashBuilding);
                Building b = s.Buildings.Find(x => x.Id == bid);
                if (b == null || b.State != BuildingState.Complete) continue;
                if (b.Kind == BuildingKind.Storehouse) store.Add(c);
                else if (bid == workedAt && workshop == null) workshop = c;
            }
            if (store.Count > 0)
            {
                store.Sort((x, y) => Order(x).CompareTo(Order(y)));
                for (int i = 0; i < store.Count && i < StoreRoles.Length; i++)
                {
                    if (System.Array.IndexOf(StoreRoles[i], res) >= 0) return store[i];
                }
            }
            return workshop;
        }

        /// <summary>
        /// A carrier puts what they brought into a chest: real items, there for whoever opens it. Nothing is
        /// made out of thin air (the simulation has already counted the work into the stock), so a load only
        /// shows more of that stock in the storehouse (up to half of it), and taking it out is theft like any
        /// of the settlement's goods. A workshop's chest keeps a modest pile (20) of each. Returns how many
        /// went in: none while a player has the chest open, or when the chest shows all it may.
        /// </summary>
        public static int Deliver(Settlement s, Container chest, Res res, string prefabName, int count)
        {
            if (chest == null || chest.IsInUse() || count <= 0) return 0;
            ZNetView view = chest.GetComponent<ZNetView>();
            ItemDrop drop = DropOf(prefabName);
            if (view == null || !view.IsValid() || drop == null) return 0;
            ZDO zdo = view.GetZDO();
            Building b = s.Buildings.Find(x => x.Id == zdo.GetInt(Materializer.HashBuilding));
            if (b == null) return 0;
            if (!view.IsOwner()) view.ClaimOwnership();
            Inventory inv = chest.GetInventory();
            string name = drop.m_itemData.m_shared.m_name;
            if (b.Kind != BuildingKind.Storehouse)
            {
                count = Mathf.Min(count, 20 - inv.CountItems(name));
                if (count <= 0 || !inv.CanAddItem(drop.gameObject, count) || !inv.AddItem(drop.gameObject, count)) return 0;
                // The household's now, as if it had always been there: not the player's to take, not a gift.
                string snap = zdo.GetString(HashSnap);
                if (snap.Length > 0)
                {
                    Dictionary<string, int> counts = Parse(snap);
                    counts[name] = Get(counts, name) + count;
                    zdo.Set(HashSnap, Join(counts));
                }
                zdo.Set(HashCount[0], zdo.GetInt(HashCount[0]) + count);
                return count;
            }
            Res[] goods = null;
            for (int i = 0; i < StoreRoles.Length && goods == null; i++)
            {
                if (System.Array.IndexOf(StoreRoles[i], res) >= 0) goods = StoreRoles[i];
            }
            if (goods == null) return 0;
            float units;
            string sale = Items.SalePrefab(res, s, out units);
            int k = System.Array.IndexOf(goods, res);
            if (sale == prefabName)
            {
                // The chest's own goods: shown on top of the quarter, never more than half the stock.
                int most = Mathf.FloorToInt(s.Stock[(int)res] * MostShown / units);
                count = Mathf.Min(count, most - inv.CountItems(name));
                if (count <= 0 || !inv.CanAddItem(drop.gameObject, count) || !inv.AddItem(drop.gameObject, count)) return 0;
                zdo.Set(HashExtra[k], zdo.GetInt(HashExtra[k]) + count);
                zdo.Set(HashCount[k], zdo.GetInt(HashCount[k]) + count);
                return count;
            }
            Res valued;
            float each;
            if (!Items.TryValue(drop.m_itemData, out valued, out each) || each <= 0f) return 0;
            Dictionary<string, int> hauled = Parse(zdo.GetString(HashHauled));
            float held = 0f;
            foreach (KeyValuePair<string, int> item in hauled)
            {
                ItemDrop other = DropOf(item.Key);
                Res r;
                float u;
                if (other != null && Items.TryValue(other.m_itemData, out r, out u) && r == valued) held += item.Value * u;
            }
            count = Mathf.Min(count, Mathf.FloorToInt((s.Stock[(int)valued] * 0.25f - held) / each));
            if (count <= 0 || !inv.CanAddItem(drop.gameObject, count) || !inv.AddItem(drop.gameObject, count)) return 0;
            hauled[prefabName] = Get(hauled, prefabName) + count;
            zdo.Set(HashHauled, Join(hauled));
            return count;
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
                zdo.Set(HashSnap, Join(Counts(inv)));
                return;
            }
            int total = Total(inv);
            int last = zdo.GetInt(HashCount[0]);
            bool opened = OpenedByMe(zdo);
            Dictionary<string, int> now = Counts(inv);
            string snap = zdo.GetString(HashSnap);
            if (snap.Length == 0)
            {
                // A chest counted by an older version: only its total is known, as before.
                if (total < last && opened) Taken(s, Res.Coins, (last - total) * 2f);
                if (opened) NightThief.Checked(s.Id, total < last);
            }
            else
            {
                // What a player put in stays theirs: taking it out again is no theft. Only what is missing
                // beyond what players left there was the household's.
                Dictionary<string, int> before = Parse(snap), left = Parse(zdo.GetString(HashLeft));
                int stolen = 0;
                foreach (KeyValuePair<string, int> item in now)
                {
                    int had;
                    before.TryGetValue(item.Key, out had);
                    if (item.Value > had && opened) left[item.Key] = Get(left, item.Key) + item.Value - had;
                }
                foreach (KeyValuePair<string, int> item in before)
                {
                    int missing = item.Value - Get(now, item.Key);
                    if (missing <= 0 || !opened) continue;
                    int theirs = Mathf.Min(missing, Get(left, item.Key));
                    if (theirs > 0) left[item.Key] = Get(left, item.Key) - theirs;
                    stolen += missing - theirs;
                }
                if (stolen > 0) Taken(s, Res.Coins, stolen * 2f);
                if (opened) NightThief.Checked(s.Id, stolen > 0);
                // Nothing of a player's is kept on record beyond what is still in the chest.
                foreach (string key in new List<string>(left.Keys)) left[key] = Mathf.Min(left[key], Get(now, key));
                zdo.Set(HashLeft, Join(left));
            }
            zdo.Set(HashSnap, Join(now));
            zdo.Set(HashCount[0], total);
        }

        private static readonly int HashSnap = "lf_snap".GetStableHashCode();
        private static readonly int HashLeft = "lf_left".GetStableHashCode();

        private static Dictionary<string, int> Counts(Inventory inv)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems()) counts[item.m_shared.m_name] = Get(counts, item.m_shared.m_name) + item.m_stack;
            return counts;
        }

        private static int Get(Dictionary<string, int> counts, string key)
        {
            int n;
            return counts.TryGetValue(key, out n) ? n : 0;
        }

        private static string Join(Dictionary<string, int> counts)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> item in counts)
            {
                if (item.Value <= 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(item.Key).Append('=').Append(item.Value);
            }
            // Never empty once counted, so an empty chest is told apart from one an older version counted.
            return sb.Length == 0 ? ";" : sb.ToString();
        }

        private static Dictionary<string, int> Parse(string text)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (string part in text.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                int n;
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), out n)) counts[part.Substring(0, eq)] = n;
            }
            return counts;
        }

        /// <summary>Gifts put into a storehouse chest lately, by chest and good: units given and the standing they earned.</summary>
        private static readonly Dictionary<string, Vector3> RecentGifts = new Dictionary<string, Vector3>();

        /// <summary>A gift taken back this long after it was put in is the giver changing their mind, not theft.</summary>
        private const float TakeBackSeconds = 600f;

        private static void RecordGift(ZDO zdo, Res res, float units, float gain)
        {
            string key = zdo.m_uid + "/" + (int)res;
            Vector3 gift;
            if (!RecentGifts.TryGetValue(key, out gift) || Time.time - gift.z > TakeBackSeconds) gift = Vector3.zero;
            RecentGifts[key] = new Vector3(gift.x + units, gift.y + gain, Time.time);
        }

        /// <summary>
        /// Goods taken out of a storehouse chest the player had lately given to: up to what they gave, the
        /// gift is undone (the stock and the standing it earned go back) and nothing is stolen. Returns the
        /// units taken beyond that, which are theft.
        /// </summary>
        private static float TakeBackGift(Settlement s, ZDO zdo, Res res, float units)
        {
            string key = zdo.m_uid + "/" + (int)res;
            Vector3 gift;
            if (!RecentGifts.TryGetValue(key, out gift) || Time.time - gift.z > TakeBackSeconds || gift.x <= 0f) return units;
            float back = Mathf.Min(units, gift.x), gain = gift.y * back / gift.x;
            Net.AddStock(s, res, -back);
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            Player player = Player.m_localPlayer;
            if (f != null && player != null && gain > 0f) Net.AddReputation(f, player.GetPlayerID(), -gain);
            RecentGifts[key] = new Vector3(gift.x - back, gift.y - gain, gift.z);
            return units - back;
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
            if (units <= 0f) return;
            Net.AddStock(s, res, -units);
            Player player = Player.m_localPlayer;
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            if (player == null || f == null || s.Destroyed) return;
            Net.NamePlayer(f, player.GetPlayerID(), player.GetPlayerName());
            bool noticed = Net.PlayerStole(f, player.GetPlayerID(), units * Balance.CoinValue[(int)res]);
            if (noticed && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get("msg_theft", Texts.Name(s.Name)));
        }

        private static float Given(Settlement s, Res res, float units)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Net.AddStock(s, res, units);
                return 0f;
            }
            return Net.PlayerGift(s, player.GetPlayerID(), res, units);
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
            sb.Append("carriers put " + FactionMember.Delivered + " loads into chests this session");
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ContainerAwakePatch
    {
        private static void Postfix(Container __instance) { Chests.Register(__instance); }
    }
}
