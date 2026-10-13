using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>The conversation window: standing, the settlement's state, gifts, trade, news and pacts.</summary>
    internal static class DiplomacyUI
    {
        private const float Width = 680f, Height = 680f;
        private const float BodyHeight = 240f;

        private static GameObject _panel;
        private static Text _title, _body;
        private static readonly List<GameObject> Buttons = new List<GameObject>();
        private static readonly List<Action> Actions = new List<Action>();
        private static readonly List<string> Labels = new List<string>();

        private static FactionMember _npc;
        private static Player _player;
        private static string _page = "main";
        private static string _note = "";
        private static bool _devOpened;

        public static bool IsOpen { get { return _panel != null && _panel.activeSelf; } }

        public static void Open(FactionMember npc, Player player)
        {
            if (!SimHost.Active || npc == null || player == null) return;
            if (_panel == null && !Build()) return;
            if (_npc != null && _npc != npc) _npc.SetTalking(null);
            _npc = npc;
            _player = player;
            npc.SetTalking(player);
            _page = "main";
            Faction f = SimHost.Sim.State.GetFaction(npc.FactionId);
            _note = f != null ? Texts.Get("greet_" + SimHost.Sim.GetStanding(f, player.GetPlayerID())) : "";
            _panel.SetActive(true);
            GUIManager.BlockInput(true);
            Refresh();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            _panel.SetActive(false);
            GUIManager.BlockInput(false);
            _devOpened = false;
            if (_npc != null) _npc.SetTalking(null);
            _npc = null;
        }

        public static void Update()
        {
            ModMenu.Update();
            if (!IsOpen) return;
            bool gone = _npc == null || _player == null || _player.IsDead() || !SimHost.Active
                || (!_devOpened && Vector3.Distance(_npc.transform.position, _player.transform.position) > 8f);
            if (gone || Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private static bool Build()
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return false;
            Vector2 mid = new Vector2(0.5f, 0.5f), top = new Vector2(0.5f, 1f);
            _panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform, mid, mid, Vector2.zero, Width, Height, false);
            Font bold = GUIManager.Instance.AveriaSerifBold;
            Font regular = GUIManager.Instance.AveriaSerif ?? bold;
            _title = GUIManager.Instance.CreateText("", _panel.transform, top, top, new Vector2(0f, -48f), bold, 24, GUIManager.Instance.ValheimOrange, true, Color.black, Width - 60f, 40f, false).GetComponent<Text>();
            _title.alignment = TextAnchor.MiddleCenter;
            _body = GUIManager.Instance.CreateText("", _panel.transform, top, top, new Vector2(0f, -74f - BodyHeight / 2f), regular, 17, Color.white, true, Color.black, Width - 80f, BodyHeight, false).GetComponent<Text>();
            _body.alignment = TextAnchor.UpperLeft;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;
            _panel.SetActive(false);
            return true;
        }

        private static void ClearButtons()
        {
            foreach (GameObject button in Buttons) UnityEngine.Object.Destroy(button);
            Buttons.Clear();
            Actions.Clear();
            Labels.Clear();
        }

        private static void AddButton(string label, Action action)
        {
            if (Buttons.Count >= 9) return;
            Vector2 top = new Vector2(0.5f, 1f);
            float y = -74f - BodyHeight - 28f - Buttons.Count * 36f;
            GameObject go = GUIManager.Instance.CreateButton(label, _panel.transform, top, top, new Vector2(0f, y), Width - 110f, 32f);
            go.GetComponent<Button>().onClick.AddListener(() => Click(action));
            Buttons.Add(go);
            Actions.Add(action);
            Labels.Add(label);
        }

        private static void Click(Action action)
        {
            if (!IsOpen || !SimHost.Active || _npc == null) return;
            action();
            if (IsOpen) Refresh();
        }

        private static void Go(string page)
        {
            _page = page;
        }

        // ------------------------------------------------------------------ pages

        private static void Refresh()
        {
            Simulation sim = SimHost.Sim;
            Settlement s = sim.State.GetSettlement(_npc.SettlementId);
            Faction f = sim.State.GetFaction(_npc.FactionId);
            if (s == null || f == null)
            {
                Close();
                return;
            }
            long pid = _player.GetPlayerID();
            PlayerRelation rel = sim.PlayerRel(f, pid);
            rel.Name = _player.GetPlayerName();
            Standing standing = sim.GetStanding(f, pid);

            _title.text = Texts.FactionName(f) + " — " + Texts.Name(s.Name) + " (" + Texts.Get("tier_" + s.Tier) + (f.CapitalId == s.Id ? ", " + Texts.Get("ui_capital") : "") + ")";
            ClearButtons();
            StringBuilder body = new StringBuilder();
            if (_note.Length > 0) body.AppendLine("<color=#FFD27F>" + _npc.DisplayName + ":</color> «" + _note + "»").AppendLine();

            switch (_page)
            {
                case "gift": GiftPage(sim, s, f, pid, body); break;
                case "trade": TradePage(sim, s, f, pid, body); break;
                case "wares": WaresPage(sim, s, f, pid, rel, body); break;
                case "news": NewsPage(sim, f, body); break;
                case "politics": PoliticsPage(sim, f, body); break;
                case "war":
                    body.AppendLine(Texts.Get("ui_war_confirm", Texts.FactionName(f)));
                    AddButton(Texts.Get("ui_war"), () =>
                    {
                        _note = Texts.Get("r_" + sim.PlayerDeclareWar(f, pid).Reason);
                        Go("main");
                    });
                    AddButton(Texts.Get("ui_back"), () => Go("main"));
                    break;
                default: MainPage(sim, s, f, pid, rel, standing, body); break;
            }
            _body.text = body.ToString().TrimEnd();
        }

        private static string StockLine(Settlement s)
        {
            return Texts.Get("res_Wood") + " " + s.Stock[0].ToString("0") + ", " + Texts.Get("res_Stone") + " " + s.Stock[1].ToString("0") + ", " + Texts.Get("res_Food") + " " + s.Stock[2].ToString("0")
                + ", " + Texts.Get("res_Metal") + " " + s.Stock[3].ToString("0") + ", " + Texts.Get("res_Coins") + " " + s.Stock[4].ToString("0");
        }

        private static void MainPage(Simulation sim, Settlement s, Faction f, long pid, PlayerRelation rel, Standing standing, StringBuilder body)
        {
            body.AppendLine(Texts.Get("ui_relation") + ": <color=" + StandingColor(standing) + ">" + Texts.Get("st_" + standing) + "</color> (" + rel.Reputation.ToString("+0;-0;0") + ")");
            body.AppendLine(Texts.Get("ui_population") + ": " + s.Population.ToString("0") + ", " + Texts.Get("ui_soldiers") + ": " + s.Soldiers.ToString("0"));
            body.AppendLine(Texts.Get("ui_stock") + ": " + StockLine(s));
            body.AppendLine(Texts.Get("ui_needs") + ": <color=#FFD27F>" + Texts.Get("res_" + sim.NeededResource(s)) + "</color>");

            if (sim.WillTrade(f, pid))
            {
                AddButton(Texts.Get("ui_gift"), () => Go("gift"));
                if (ModConfig.TradeOn.Value) AddButton(Texts.Get("ui_trade"), () => Go("trade"));
            }
            else if (rel.AtWar)
            {
                // Tribute is the only thing an enemy will still accept.
                AddButton(Texts.Get("ui_gift"), () => Go("gift"));
            }
            if (ModConfig.Relics.Value && ModConfig.TradeOn.Value && sim.WillTrade(f, pid)) AddButton(Texts.Get("ui_wares"), () => Go("wares"));
            if (ModConfig.Mercenaries.Value && sim.WillTrade(f, pid))
            {
                AddButton(Texts.Get("ui_hire", Company.HirePrice(s)), () => _note = Texts.Get(Company.Hire(s, _player, false)));
            }
            AddButton(Texts.Get("ui_news"), () => Go("news"));
            AddButton(Texts.Get("ui_politics"), () => Go("politics"));
            if (rel.AtWar)
            {
                int cost = sim.PeaceCost(f, pid);
                AddButton(Texts.Get("ui_peace", cost), () =>
                {
                    Inventory inv = _player.GetInventory();
                    int coins = inv.CountItems("$item_coins");
                    DiploResult result = sim.PlayerProposePeace(f, pid, Math.Min(coins, cost));
                    if (result.Ok) inv.RemoveItem("$item_coins", cost);
                    _note = Texts.Get("r_" + result.Reason);
                });
            }
            else
            {
                if (rel.Allied) AddButton(Texts.Get("ui_break"), () => _note = Texts.Get("r_" + sim.PlayerBreakAlliance(f, pid).Reason));
                else AddButton(Texts.Get("ui_alliance"), () => _note = Texts.Get("r_" + sim.PlayerProposeAlliance(f, pid).Reason));
                AddButton(Texts.Get("ui_war"), () => Go("war"));
            }
            AddButton(Texts.Get("ui_leave"), Close);
        }

        private static string StandingColor(Standing standing)
        {
            switch (standing)
            {
                case Standing.War:
                case Standing.Hostile: return "#FF5A4A";
                case Standing.Wary: return "#FFA64A";
                case Standing.Friendly: return "#9BE36A";
                case Standing.Allied: return "#5AD7FF";
                default: return "#FFFFFF";
            }
        }

        private static void WaresPage(Simulation sim, Settlement s, Faction f, long pid, PlayerRelation rel, StringBuilder body)
        {
            Inventory inv = _player.GetInventory();
            body.AppendLine(Texts.Get("ui_your_coins", inv.CountItems("$item_coins")));

            // Something of theirs the player carries without knowing what it is.
            foreach (ItemDrop.ItemData owned in inv.GetAllItems())
            {
                if (!Wares.IsMystery(owned, f)) continue;
                ItemDrop.ItemData item = owned;
                AddButton(Texts.Get("ui_identify", Localization.instance.Localize(item.m_shared.m_name)), () =>
                {
                    if (rel.Reputation < Wares.IdentifyReputation)
                    {
                        _note = Texts.Get("r_not_trusted");
                        return;
                    }
                    _note = Texts.Get("r_identified", Texts.Get("affix_" + Wares.Identify(item, f)));
                });
                break;
            }

            List<Ware> wares = Wares.For(f);
            if (wares.Count == 0) body.AppendLine(Texts.Get("r_no_wares"));
            foreach (Ware ware in wares)
            {
                Ware w = ware;
                GameObject prefab = ObjectDB.instance.GetItemPrefab(w.Item);
                if (prefab == null) continue;
                string name = Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                string label = w.Kind == "relic" ? Texts.Get("ui_ware_relic", name, Texts.Get("affix_" + w.Affix), w.Price)
                    : w.Kind == "mystery" ? Texts.Get("ui_ware_mystery", name, w.Price)
                    : Texts.Get("ui_ware_sell", name, w.Amount, w.Price);
                bool locked = rel.Reputation < w.MinReputation;
                if (locked) label += Texts.Get("ui_ware_locked", w.MinReputation);
                AddButton(label, () =>
                {
                    if (locked)
                    {
                        _note = Texts.Get("r_not_trusted");
                        return;
                    }
                    if (inv.CountItems("$item_coins") < w.Price)
                    {
                        _note = Texts.Get("r_not_enough_coins");
                        return;
                    }
                    if (!Wares.Deliver(w, f, inv))
                    {
                        _note = Texts.Get("r_pack_full");
                        return;
                    }
                    inv.RemoveItem("$item_coins", w.Price);
                    s.Stock[(int)Res.Coins] += w.Price;
                    sim.AddReputation(f, pid, Math.Min(1.5f, w.Price * 0.004f));
                    _note = Texts.Get("r_deal_done");
                });
                if (Buttons.Count >= 7) break;
            }
            AddButton(Texts.Get("ui_back"), () => Go("main"));
        }

        private class Offer
        {
            public ItemDrop.ItemData Item;
            public Res Res;
            public float Units;
            public int Count;
        }

        /// <summary>The goods in the player's pack the settlement has a use for, most valuable first.</summary>
        private static List<Offer> PlayerGoods(bool includeCoins)
        {
            Dictionary<string, Offer> byName = new Dictionary<string, Offer>();
            foreach (ItemDrop.ItemData item in _player.GetInventory().GetAllItems())
            {
                Res res;
                float units;
                if (!Items.TryValue(item, out res, out units)) continue;
                if (res == Res.Coins && !includeCoins) continue;
                Offer offer;
                if (!byName.TryGetValue(item.m_shared.m_name, out offer))
                {
                    offer = new Offer { Item = item, Res = res, Units = units };
                    byName[item.m_shared.m_name] = offer;
                }
                offer.Count += item.m_stack;
            }
            return byName.Values.OrderByDescending(o => o.Count * o.Units * Balance.CoinValue[(int)o.Res]).ToList();
        }

        private static void GiftPage(Simulation sim, Settlement s, Faction f, long pid, StringBuilder body)
        {
            body.AppendLine(Texts.Get("ui_needs") + ": <color=#FFD27F>" + Texts.Get("res_" + sim.NeededResource(s)) + "</color>");
            List<Offer> goods = PlayerGoods(true);
            if (goods.Count == 0) body.AppendLine(Texts.Get("ui_nothing_to_give"));
            foreach (Offer offer in goods.Take(6))
            {
                Offer o = offer;
                int amount = Math.Min(o.Count, o.Res == Res.Coins ? 50 : 20);
                AddButton(Texts.Get("ui_give", Localization.instance.Localize(o.Item.m_shared.m_name), amount), () =>
                {
                    _player.GetInventory().RemoveItem(o.Item.m_shared.m_name, amount);
                    float gain = sim.PlayerGift(s, pid, o.Res, amount * o.Units);
                    _note = gain >= 1f ? Texts.Get("r_gift_thanks", gain.ToString("0")) : Texts.Get("r_gift_meh");
                });
            }
            AddButton(Texts.Get("ui_back"), () => Go("main"));
        }

        private static void TradePage(Simulation sim, Settlement s, Faction f, long pid, StringBuilder body)
        {
            Inventory inv = _player.GetInventory();
            body.AppendLine(Texts.Get("ui_stock") + ": " + StockLine(s));
            body.AppendLine(Texts.Get("ui_your_coins", inv.CountItems("$item_coins")));

            foreach (Res res in new[] { Res.Wood, Res.Stone, Res.Food, Res.Metal })
            {
                Res r = res;
                float unitsPerItem;
                string prefabName = Items.SalePrefab(r, s, out unitsPerItem);
                GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                if (prefab == null) continue;
                int count = r == Res.Metal ? 5 : 10;
                int units = Mathf.RoundToInt(count * unitsPerItem);
                if (s.Stock[(int)r] < units + 15f) continue;
                int cost = Math.Max(1, (int)Math.Ceiling(sim.BuyPrice(s, r, pid) * units));
                string itemName = Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                AddButton(Texts.Get("ui_buy", count, itemName, cost), () =>
                {
                    if (!inv.CanAddItem(prefab, count))
                    {
                        _note = Texts.Get("r_pack_full");
                        return;
                    }
                    int paid = sim.PlayerBuy(s, r, units, pid, inv.CountItems("$item_coins"));
                    if (paid < 0)
                    {
                        _note = Texts.Get(inv.CountItems("$item_coins") < cost ? "r_not_enough_coins" : "r_deal_refused");
                        return;
                    }
                    inv.RemoveItem("$item_coins", paid);
                    inv.AddItem(prefab, count);
                    _note = Texts.Get("r_deal_done");
                });
            }

            GameObject coinPrefab = ObjectDB.instance.GetItemPrefab("Coins");
            foreach (Offer offer in PlayerGoods(false).Take(3))
            {
                Offer o = offer;
                int amount = Math.Min(o.Count, 10);
                int units = Math.Max(1, Mathf.RoundToInt(amount * o.Units));
                int pay = (int)Math.Floor(sim.SellPrice(s, o.Res, pid) * units);
                if (pay <= 0) continue;
                AddButton(Texts.Get("ui_sell", amount, Localization.instance.Localize(o.Item.m_shared.m_name), pay), () =>
                {
                    int got = sim.PlayerSell(s, o.Res, units, pid);
                    if (got < 0)
                    {
                        _note = Texts.Get("r_deal_refused");
                        return;
                    }
                    inv.RemoveItem(o.Item.m_shared.m_name, amount);
                    if (coinPrefab != null) inv.AddItem(coinPrefab, got);
                    _note = Texts.Get("r_deal_done");
                });
            }
            AddButton(Texts.Get("ui_back"), () => Go("main"));
        }

        private static void NewsPage(Simulation sim, Faction f, StringBuilder body)
        {
            List<SimEvent> log = sim.State.Chronicle;
            int shown = 0;
            for (int i = log.Count - 1; i >= 0 && shown < 9; i--)
            {
                SimEvent e = log[i];
                if (e.FactionId != f.Id && e.OtherFactionId != f.Id) continue;
                if (e.Kind == EventKind.BuildingStarted) continue;
                body.AppendLine("<color=#BBBBBB>" + Texts.Get("ui_day", ((int)e.Day).ToString()) + ":</color> " + Texts.Describe(e, sim.State));
                shown++;
            }
            if (shown == 0) body.AppendLine(Texts.Get("ui_no_news"));
            AddButton(Texts.Get("ui_back"), () => Go("main"));
        }

        private static void PoliticsPage(Simulation sim, Faction f, StringBuilder body)
        {
            body.AppendLine(Texts.FactionName(f) + ": " + Texts.Get("ui_strength").ToLowerInvariant() + " " + sim.Strength(f).ToString("0") + ", " + sim.CountSettlements(f.Id) + " " + Texts.Get("ui_settlements"));
            foreach (Faction other in sim.State.Factions)
            {
                if (other.Eliminated || other.Id == f.Id) continue;
                Relation r = sim.Rel(f, other);
                string color = r.Treaty == Treaty.War ? "#FF5A4A" : r.Treaty == Treaty.Alliance ? "#5AD7FF" : r.Value >= 25f ? "#9BE36A" : r.Value <= -25f ? "#FFA64A" : "#FFFFFF";
                body.AppendLine("<color=" + color + ">" + Texts.FactionName(other) + "</color>: " + Texts.Get("tr_" + r.Treaty) + " (" + r.Value.ToString("+0;-0;0") + "), " + Texts.Get("ui_strength").ToLowerInvariant() + " " + sim.Strength(other).ToString("0"));
            }
            AddButton(Texts.Get("ui_back"), () => Go("main"));
        }

        // ------------------------------------------------------------------ testing hooks

        public static string DevCommand(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "state";
            if (verb == "open")
            {
                if (Player.m_localPlayer == null) return "no player";
                FactionMember nearest = null;
                float best = 60f;
                foreach (FactionMember member in FactionMember.All)
                {
                    float d = Vector3.Distance(member.transform.position, Player.m_localPlayer.transform.position);
                    if (d < best && (a.Length < 3 || member.Role.ToString().StartsWith(a[2], StringComparison.OrdinalIgnoreCase)))
                    {
                        best = d;
                        nearest = member;
                    }
                }
                if (nearest == null) return "no faction member nearby";
                // Opened from the test bridge: the player stays where they are and distance does not close the window.
                Open(nearest, Player.m_localPlayer);
                _devOpened = true;
            }
            else if (verb == "click" && a.Length > 2)
            {
                int index = int.Parse(a[2]);
                if (!IsOpen || index < 0 || index >= Actions.Count) return "no such button";
                Click(Actions[index]);
            }
            else if (verb == "close") Close();

            if (!IsOpen) return "closed";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[" + _page + "] " + _title.text);
            sb.AppendLine(_body.text);
            for (int i = 0; i < Labels.Count; i++) sb.AppendLine("  (" + i + ") " + Labels[i]);
            return sb.ToString().TrimEnd();
        }
    }
}
