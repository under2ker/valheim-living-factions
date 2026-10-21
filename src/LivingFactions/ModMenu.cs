using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>
    /// The mod's own settings window, opened from an icon on the inventory screen (TAB).
    ///
    /// It is a list of pages, each a list of rows; a row is a label and what a click does.
    /// To add a setting, add a row in <see cref="BuildPages"/>:
    ///   Toggle(page, "opt_roads", ModConfig.Roads);                           an on/off switch
    ///   Choice(page, "opt_pace", ModConfig.Pace, new[] { 0.5f, 1f, 2f });     a click steps through values
    ///   page.Rows.Add(new Row { Label = () => "...", Click = () => ... });    anything else
    /// To add a whole section, call AddPage("menu_page_xxx") and fill it. Labels are keys of Texts.
    /// </summary>
    internal static class ModMenu
    {
        public class Row
        {
            public Func<string> Label;
            public Action Click;
        }

        public class Page
        {
            public string TitleKey;
            public string NoteKey;
            public readonly List<Row> Rows = new List<Row>();
        }

        private const float Width = 700f, Height = 690f;
        // Thirteen rows fit between the page tabs and the close button (the world page has that many).
        private const int MaxRows = 13;

        private static readonly List<Page> Pages = new List<Page>();
        private static GameObject _panel, _icon;
        private static Text _title, _note;
        private static readonly List<GameObject> Buttons = new List<GameObject>();
        private static int _page;

        public static bool IsOpen { get { return _panel != null && _panel.activeSelf; } }

        // ------------------------------------------------------------------ what is in the menu

        public static Page AddPage(string titleKey, string noteKey = null)
        {
            Page page = new Page { TitleKey = titleKey, NoteKey = noteKey };
            Pages.Add(page);
            return page;
        }

        public static void Toggle(Page page, string key, ConfigEntry<bool> entry)
        {
            page.Rows.Add(new Row
            {
                Label = () => Texts.Get(key) + ":  " + (entry.Value ? "<color=#9BE36A>" + Texts.Get("menu_on") + "</color>" : "<color=#FF8A7A>" + Texts.Get("menu_off") + "</color>"),
                Click = () => entry.Value = !entry.Value
            });
        }

        public static void Choice<T>(Page page, string key, ConfigEntry<T> entry, T[] values, Func<T, string> show = null)
        {
            page.Rows.Add(new Row
            {
                Label = () => Texts.Get(key) + ":  <color=#FFD27F>" + (show != null ? show(entry.Value) : Convert.ToString(entry.Value, System.Globalization.CultureInfo.InvariantCulture)) + "</color>",
                Click = () =>
                {
                    int at = Array.FindIndex(values, v => EqualityComparer<T>.Default.Equals(v, entry.Value));
                    entry.Value = values[(at + 1) % values.Length];
                }
            });
        }

        private static void BuildPages()
        {
            if (Pages.Count > 0) return;

            Page general = AddPage("menu_page_general");
            Choice(general, "opt_language", ModConfig.Language, new[] { "auto", "en", "ru", "pl", "de", "fr", "es", "pt" }, v => Texts.Get("lang_" + v));
            general.Rows[general.Rows.Count - 1].Click += ApplyLanguage;
            Toggle(general, "opt_hud", ModConfig.HudWidget);
            Toggle(general, "opt_messages", ModConfig.ShowChronicleMessages);

            Page life = AddPage("menu_page_life");
            Toggle(life, "opt_work", ModConfig.VisibleWork);
            Toggle(life, "opt_felling", ModConfig.TreeFelling);
            Toggle(life, "opt_errands", ModConfig.Errands);
            Toggle(life, "opt_sleep", ModConfig.NightSleep);
            Toggle(life, "opt_watch", ModConfig.NightWatchOn);
            Choice(life, "opt_night_attack", ModConfig.NightAttackChance, new[] { 0f, 0.25f, 0.57f, 0.8f, 1f }, v => Mathf.RoundToInt(v * 100f) + "%");
            Choice(life, "opt_rep_decay", ModConfig.ReputationDecay, new[] { 0f, 0.25f, 0.5f, 1f, 2f }, v => Mathf.RoundToInt(v * 100f) + "%");
            Toggle(life, "opt_tower", ModConfig.TowerGuards);
            Toggle(life, "opt_archers", ModConfig.Archers);
            Choice(life, "opt_npcs", ModConfig.MaxVisibleNpcs, new[] { 6, 10, 14, 20, 30 });

            Page world = AddPage("menu_page_world");
            Toggle(world, "opt_map_zones", ModConfig.MapZones);
            Toggle(world, "opt_map_all", ModConfig.MapShowAll);
            Toggle(world, "opt_map_npcs", ModConfig.MapNpcs);
            Toggle(world, "opt_roads", ModConfig.Roads);
            Toggle(world, "opt_lamps", ModConfig.RoadLamps);
            Choice(world, "opt_road_reach", ModConfig.RoadReach, new[] { 0f, 800f, 1500f, 2500f, 5000f }, v => v.ToString("0") + " m");
            Toggle(world, "opt_caravans", ModConfig.Caravans);
            Toggle(world, "opt_raids", ModConfig.StagedRaids);
            Toggle(world, "opt_chests", ModConfig.ChestsOn);
            Toggle(world, "opt_loot_sweep", ModConfig.LootSweep);
            Toggle(world, "opt_trade", ModConfig.TradeOn);
            Toggle(world, "opt_relics", ModConfig.Relics);
            Toggle(world, "opt_hire", ModConfig.Mercenaries);

            Page sim = AddPage("menu_page_sim", "menu_note_reload");
            Choice(sim, "opt_pace", ModConfig.Pace, new[] { 0.5f, 1f, 2f, 4f }, v => "x" + v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            Toggle(sim, "opt_wars", ModConfig.Wars);
            Toggle(sim, "opt_races", ModConfig.RaceFactions);
        }

        private static void ApplyLanguage()
        {
            Texts.Lang = Texts.Resolve(ModConfig.Language.Value, Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null);
            CustomItems.Rename();
            foreach (FactionMember member in FactionMember.All) member.Rename();
        }

        // ------------------------------------------------------------------ the window

        public static void Open()
        {
            BuildPages();
            if (_panel == null && !Build()) return;
            if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _panel.SetActive(true);
            GUIManager.BlockInput(true);
            Refresh();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            _panel.SetActive(false);
            GUIManager.BlockInput(false);
        }

        public static void Update()
        {
            EnsureIcon();
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private static bool Build()
        {
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return false;
            Vector2 mid = new Vector2(0.5f, 0.5f), top = new Vector2(0.5f, 1f), bottom = new Vector2(0.5f, 0f);
            _panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform, mid, mid, Vector2.zero, Width, Height, false);
            Font bold = GUIManager.Instance.AveriaSerifBold;
            _title = GUIManager.Instance.CreateText("", _panel.transform, top, top, new Vector2(0f, -46f), bold, 24, GUIManager.Instance.ValheimOrange, true, Color.black, Width - 60f, 40f, false).GetComponent<Text>();
            _title.alignment = TextAnchor.MiddleCenter;
            _note = GUIManager.Instance.CreateText("", _panel.transform, bottom, bottom, new Vector2(0f, 84f), GUIManager.Instance.AveriaSerif ?? bold, 15, new Color(1f, 0.82f, 0.5f), true, Color.black, Width - 90f, 40f, false).GetComponent<Text>();
            _note.alignment = TextAnchor.MiddleCenter;
            _panel.SetActive(false);
            return true;
        }

        private static GameObject Button(string label, Vector2 anchor, Vector2 pos, float width, Action click)
        {
            GameObject go = GUIManager.Instance.CreateButton(label, _panel.transform, anchor, anchor, pos, width, 32f);
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (!IsOpen) return;
                click();
                if (IsOpen) Refresh();
            });
            Text text = go.GetComponentInChildren<Text>();
            if (text != null) text.supportRichText = true;
            Buttons.Add(go);
            return go;
        }

        private static void Refresh()
        {
            foreach (GameObject button in Buttons) UnityEngine.Object.Destroy(button);
            Buttons.Clear();
            _page = Mathf.Clamp(_page, 0, Pages.Count - 1);
            Page page = Pages[_page];
            _title.text = "Living Factions " + Plugin.Version + " — " + Texts.Get("menu_title");
            _note.text = page.NoteKey != null ? Texts.Get(page.NoteKey) : "";

            Vector2 top = new Vector2(0.5f, 1f), bottom = new Vector2(0.5f, 0f);
            // Page tabs across the top; they shrink as pages are added.
            float tab = Mathf.Min(170f, (Width - 70f) / Pages.Count);
            for (int i = 0; i < Pages.Count; i++)
            {
                int index = i;
                string label = Texts.Get(Pages[i].TitleKey);
                Button(i == _page ? "<color=#FFA13C>" + label + "</color>" : label, top, new Vector2((i - (Pages.Count - 1) * 0.5f) * tab, -92f), tab - 6f, () => _page = index);
            }
            for (int i = 0; i < page.Rows.Count && i < MaxRows; i++)
            {
                Row row = page.Rows[i];
                Button(row.Label(), top, new Vector2(0f, -140f - i * 36f), Width - 110f, row.Click);
            }
            Button(Texts.Get("menu_close"), bottom, new Vector2(0f, 44f), 220f, Close);
        }

        // ------------------------------------------------------------------ the icon on the inventory screen

        private static void EnsureIcon()
        {
            if (_icon != null || InventoryGui.instance == null || InventoryGui.instance.m_player == null || ZNetScene.instance == null) return;
            RectTransform host = InventoryGui.instance.m_player;
            _icon = new GameObject("lf_menu_icon", typeof(RectTransform), typeof(Image), typeof(Button));
            _icon.transform.SetParent(host, false);
            RectTransform frame = (RectTransform)_icon.transform;
            // Beside the inventory panel, to the right of the weight readout: the panel itself touches the top of the screen.
            frame.anchorMin = frame.anchorMax = new Vector2(1f, 1f);
            frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(64f, -8f);
            frame.sizeDelta = new Vector2(48f, 48f);
            _icon.GetComponent<Image>().color = new Color(0.1f, 0.07f, 0.04f, 0.85f);

            GameObject picture = new GameObject("picture", typeof(RectTransform), typeof(Image));
            picture.transform.SetParent(_icon.transform, false);
            RectTransform inner = (RectTransform)picture.transform;
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(4f, 4f);
            inner.offsetMax = new Vector2(-4f, -4f);
            Image image = picture.GetComponent<Image>();
            image.raycastTarget = false;
            // The mod's mark: its initials in the game's orange on a dark tile, readable at any size.
            image.color = new Color(0.22f, 0.15f, 0.08f, 1f);
            if (GUIManager.Instance != null)
            {
                Vector2 mid = new Vector2(0.5f, 0.5f);
                Text mark = GUIManager.Instance.CreateText("LF", _icon.transform, mid, mid, Vector2.zero, GUIManager.Instance.AveriaSerifBold, 24, GUIManager.Instance.ValheimOrange, true, Color.black, 48f, 40f, false).GetComponent<Text>();
                mark.alignment = TextAnchor.MiddleCenter;
                mark.raycastTarget = false;
            }

            _icon.GetComponent<Button>().onClick.AddListener(Open);
            // A tooltip without the game's tooltip prefab throws on every hover, so it is added only when one is found.
            UITooltip template = null;
            foreach (UITooltip candidate in InventoryGui.instance.GetComponentsInChildren<UITooltip>(true))
            {
                if (candidate.m_tooltipPrefab != null)
                {
                    template = candidate;
                    break;
                }
            }
            if (template != null)
            {
                UITooltip tip = _icon.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = template.m_tooltipPrefab;
                tip.m_text = "Living Factions";
            }
        }

        // ------------------------------------------------------------------ dev

        public static string DevCommand(string[] a)
        {
            string verb = a.Length > 1 ? a[1] : "state";
            if (verb == "open") Open();
            else if (verb == "close") Close();
            else if (verb == "page" && a.Length > 2 && IsOpen)
            {
                _page = int.Parse(a[2]);
                Refresh();
            }
            else if (verb == "click" && a.Length > 2 && IsOpen)
            {
                Page page = Pages[_page];
                int row = int.Parse(a[2]);
                if (row < 0 || row >= page.Rows.Count) return "no such row";
                page.Rows[row].Click();
                Refresh();
            }
            if (!IsOpen) return "menu closed, icon " + (_icon != null ? "present" : "absent");
            StringBuilder sb = new StringBuilder("[" + Texts.Get(Pages[_page].TitleKey) + "] of " + Pages.Count + " pages, icon " + (_icon != null ? "present" : "absent")
                + ", rows " + Mathf.Min(Pages[_page].Rows.Count, MaxRows) + " of " + Pages[_page].Rows.Count + " shown\n");
            for (int i = 0; i < Pages[_page].Rows.Count; i++) sb.AppendLine("  " + i + ": " + System.Text.RegularExpressions.Regex.Replace(Pages[_page].Rows[i].Label(), "<.*?>", ""));
            return sb.ToString().TrimEnd();
        }
    }
}
