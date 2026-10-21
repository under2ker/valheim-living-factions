using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Jotunn.Managers;
using LivingFactions.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>
    /// The lands of the factions on the map: a tinted circle around every settlement the player
    /// knows of, the faction's colour on its pin, and a legend of the factions in the corner of the
    /// large map. The circles are pins the game sizes to the world itself (as it does for event
    /// areas), so they stay true to the ground at every zoom. Each mark is named briefly (name, and size
    /// or what kind of place it is); zoomed in, or with the pointer on it, the name opens up into the
    /// faction and its standing with the player, the people and what they lack, and their troubles.
    /// </summary>
    [HarmonyPatch]
    internal static class MapZones
    {
        /// <summary>How far a settlement's land reaches, by tier: camp, hamlet, village, town, city.</summary>
        private static readonly float[] Reach = { 90f, 130f, 170f, 220f, 280f };

        private static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            { "clan_raven", new Color32(0x5B, 0x7B, 0xD5, 0xFF) },
            { "clan_wolf", new Color32(0x4A, 0x50, 0x5C, 0xFF) },
            { "clan_stag", new Color32(0xE8, 0xAE, 0x4C, 0xFF) },
            { "fuling_horde", new Color32(0xE2, 0x5C, 0x34, 0xFF) },
            { "dvergr_hold", new Color32(0x45, 0xC8, 0xCE, 0xFF) },
            { "greydwarf_brood", new Color32(0x7C, 0xC4, 0x46, 0xFF) },
            { "draugr_court", new Color32(0xA0, 0x62, 0xD8, 0xFF) },
        };

        /// <summary>Pin -> the colour it is drawn in (zones translucent, settlement marks solid).</summary>
        private static readonly Dictionary<Minimap.PinData, Color> Tints = new Dictionary<Minimap.PinData, Color>();
        private static readonly HashSet<Minimap.PinData> Zones = new HashSet<Minimap.PinData>();
        private static Sprite _ring;
        private static Text _legend;

        /// <summary>Settlement mark -> its brief name and the full one.</summary>
        private static readonly Dictionary<Minimap.PinData, string[]> Labels = new Dictionary<Minimap.PinData, string[]>();
        private static Minimap.PinData _hovered;

        /// <summary>Zoomed in closer than this (the game names pins below 0.5) every settlement tells all it has to tell.</summary>
        private const float DetailZoom = 0.07f;

        private static readonly Func<Minimap, Vector3, Vector3> ScreenToWorld = PointerToWorld();

        private static Func<Minimap, Vector3, Vector3> PointerToWorld()
        {
            System.Reflection.MethodInfo method = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });
            if (method != null) return AccessTools.MethodDelegate<Func<Minimap, Vector3, Vector3>>(method);
            Plugin.Log.LogWarning("Minimap.ScreenToWorldPoint not found: settlements on the map open up only when zoomed in.");
            return null;
        }

        public static Color ColourOf(Faction f)
        {
            Color c;
            return f != null && Colours.TryGetValue(f.CultureId, out c) ? c : Color.white;
        }

        public static void Clear()
        {
            Tints.Clear();
            Zones.Clear();
            Labels.Clear();
            _hovered = null;
        }

        /// <summary>A settlement's own mark in its faction's colour, with the full name for a close look.</summary>
        public static void Mark(Minimap.PinData pin, Faction f, string full)
        {
            if (pin == null) return;
            Tints[pin] = ColourOf(f);
            Labels[pin] = new[] { pin.m_name, full };
        }

        /// <summary>How a faction stands with a player, without opening a record for one it has never dealt with.</summary>
        public static Standing StandingOf(Faction f, long playerId)
        {
            if (f == null) return Standing.Neutral;
            if (f.Players.ContainsKey(playerId)) return SimHost.Sim.GetStanding(f, playerId);
            float rep = Defs.Culture(f.CultureId).Disposition;
            if (rep <= Simulation.HostileBelow) return Standing.Hostile;
            if (rep <= Simulation.WaryBelow) return Standing.Wary;
            return rep < Simulation.FriendlyFrom ? Standing.Neutral : Standing.Friendly;
        }

        public static string StandingColour(Standing standing)
        {
            switch (standing)
            {
                case Standing.War:
                case Standing.Hostile: return "#FF5A4A";
                case Standing.Wary: return "#FFA64A";
                case Standing.Friendly: return "#9BE36A";
                case Standing.Allied: return "#5AD7FF";
                default: return "#E8E8E8";
            }
        }

        /// <summary>
        /// The two names of a settlement on the map. Brief: its name and size, or what it is when it is
        /// not a plain settlement (war camp, outpost). Full: also whose it is and how they stand with the
        /// player, its people, warriors and what it lacks, and what threatens it.
        /// </summary>
        public static void Names(Settlement s, Faction f, long playerId, out string brief, out string full)
        {
            WorldState state = SimHost.Sim.State;
            string kind = s.CampaignId != 0 ? "<color=#FF7A5A>" + Texts.Get("map_warcamp") + "</color>"
                : s.ParentId != 0 ? "<color=#FFD27F>" + Texts.Get(s.RoadPost ? "map_roadpost" : "map_outpost") + "</color>"
                : Texts.Get("tier_" + s.Tier);
            brief = Texts.Name(s.Name) + " (" + kind + ")";

            StringBuilder sb = new StringBuilder(brief);
            Standing standing = StandingOf(f, playerId);
            sb.Append("\n<size=80%><color=#" + ColorUtility.ToHtmlStringRGB(ColourOf(f)) + ">" + Texts.FactionName(f) + "</color>, <color=" + StandingColour(standing) + ">" + Texts.Get("st_" + standing) + "</color>");
            sb.Append("\n" + Texts.Get("ui_population") + " " + s.Population.ToString("0") + ", " + Texts.Get("ui_soldiers") + " " + s.Soldiers.ToString("0"));
            if (s.CampaignId == 0) sb.Append("\n" + Texts.Get("ui_needs") + ": <color=#FFD27F>" + Texts.Get("res_" + SimHost.Sim.NeededResource(s)) + "</color>");

            List<string> troubles = new List<string>();
            foreach (Campaign c in state.Campaigns)
            {
                if (c.TargetId == s.Id && (c.Stage == CampaignStage.Camp || c.Stage == CampaignStage.Assault))
                {
                    troubles.Add(Texts.Get("map_threat", Texts.FactionName(state.GetFaction(c.FactionId))));
                    break;
                }
            }
            if (s.HungerDays > 0f) troubles.Add(Texts.Get("hud_hunger"));
            if (f != null)
            {
                List<string> enemies = new List<string>();
                foreach (KeyValuePair<int, Relation> r in f.Relations)
                {
                    Faction other = r.Value.Treaty == Treaty.War ? state.GetFaction(r.Key) : null;
                    if (other != null && enemies.Count < 2) enemies.Add(Texts.FactionName(other));
                }
                if (enemies.Count > 0) troubles.Add(Texts.Get("map_wars", string.Join(", ", enemies.ToArray())));
            }
            foreach (string t in troubles) sb.Append("\n<color=#FF8A6A>" + t + "</color>");
            sb.Append("</size>");
            full = sb.ToString();
        }

        /// <summary>The land of one settlement. Added before the settlement's mark, so the mark is drawn on top.</summary>
        public static void AddZone(Settlement s, Faction f, List<Minimap.PinData> pins)
        {
            Color colour = ColourOf(f);
            if (!ModConfig.MapZones.Value || Minimap.instance == null) return;
            float radius = Reach[Mathf.Clamp((int)s.Tier, 0, Reach.Length - 1)];
            Minimap.PinData zone = Minimap.instance.AddPin(new Vector3(s.Pos.X, 0f, s.Pos.Z), Minimap.PinType.EventArea, "", false, false);
            zone.m_icon = Ring();
            zone.m_worldSize = radius * 2f;
            zone.m_animate = false;
            Tints[zone] = new Color(colour.r, colour.g, colour.b, 0.85f);
            Zones.Add(zone);
            pins.Add(zone);
        }

        /// <summary>A disc with a stronger rim: the faction's land, its edge plain to see.</summary>
        private static Sprite Ring()
        {
            if (_ring != null) return _ring;
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] px = new Color[size * size];
            float centre = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / centre;
                    float fill = d <= 1f ? 0.42f : 0f;
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.96f) / 0.04f);
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Max(fill, rim));
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _ring;
        }

        /// <summary>The factions the player has met, each in its colour, in the corner of the large map.</summary>
        public static void UpdateLegend(List<int> known)
        {
            if (Minimap.instance == null || Minimap.instance.m_largeRoot == null) return;
            if (_legend == null)
            {
                if (GUIManager.Instance == null) return;
                RectTransform root = Minimap.instance.m_largeRoot.GetComponent<RectTransform>();
                Vector2 corner = new Vector2(0f, 1f);
                GameObject go = GUIManager.Instance.CreateText("", root, corner, corner, new Vector2(150f, -150f), GUIManager.Instance.AveriaSerifBold, 16, Color.white, true, Color.black, 260f, 240f, false);
                _legend = go.GetComponent<Text>();
                _legend.alignment = TextAnchor.UpperLeft;
                _legend.raycastTarget = false;
            }
            WorldState state = SimHost.Sim.State;
            List<int> seen = new List<int>();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (int id in known)
            {
                Settlement s = state.GetSettlement(id);
                if (s == null || s.Destroyed || seen.Contains(s.FactionId)) continue;
                seen.Add(s.FactionId);
                Faction f = state.GetFaction(s.FactionId);
                sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(ColourOf(f)) + ">■</color> " + Texts.FactionName(f));
            }
            _legend.text = sb.ToString();
            _legend.gameObject.SetActive(ModConfig.MapZones.Value && sb.Length > 0);
        }

        public static string Describe()
        {
            foreach (Minimap.PinData z in Zones)
            {
                RectTransform r = z.m_uiElement;
                return Zones.Count + " zones; first: world size " + z.m_worldSize + (r == null ? ", no ui element" : ", ui " + r.sizeDelta + " scale " + r.localScale + " active " + r.gameObject.activeInHierarchy + " parent " + r.parent.name);
            }
            return "no zones";
        }

        /// <summary>For `lf mapinfo`: what the settlement marks say and which of their names is shown.</summary>
        public static string Info()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Labels.Count + " settlement marks, " + Zones.Count + " zones");
            if (Minimap.instance != null) sb.Append(", map " + Minimap.instance.m_mode + " zoom " + Minimap.instance.LargeZoom.ToString("0.000"));
            int n = 0;
            foreach (KeyValuePair<Minimap.PinData, string[]> l in Labels)
            {
                if (n++ >= 4) break;
                TMP_Text text = l.Key.m_NamePinData != null ? l.Key.m_NamePinData.PinNameText : null;
                sb.Append("\n  [" + l.Value[0] + "] full [" + l.Value[1].Replace("\n", " / ") + "]");
                if (text != null) sb.Append(" shown " + (text.text == l.Value[1] ? "full" : text.text == l.Value[0] ? "brief" : "other") + " active " + text.gameObject.activeInHierarchy + " align " + text.alignment);
            }
            return sb.ToString();
        }

        /// <summary>Zoomed in, or with the pointer on a settlement, its mark tells everything; otherwise only its name.</summary>
        [HarmonyPatch(typeof(Minimap), "Update"), HarmonyPostfix]
        private static void Detail(Minimap __instance)
        {
            if (Labels.Count == 0 || __instance.m_mode != Minimap.MapMode.Large) return;
            float zoom = __instance.LargeZoom;
            bool close = zoom < DetailZoom;
            Minimap.PinData hover = null;
            if (!close && ScreenToWorld != null)
            {
                Vector3 at = ScreenToWorld(__instance, ZInput.pointerPosition);
                float best = __instance.m_removeRadius * zoom * 2f;
                foreach (Minimap.PinData pin in Labels.Keys)
                {
                    float d = Utils.DistanceXZ(at, pin.m_pos);
                    if (d < best) { best = d; hover = pin; }
                }
            }
            bool named = zoom < __instance.m_showNamesZoom;
            foreach (KeyValuePair<Minimap.PinData, string[]> l in Labels)
            {
                Minimap.PinNameData name = l.Key.m_NamePinData;
                if (name == null || name.PinNameText == null || name.PinNameGameObject == null) continue;
                string want = close || l.Key == hover ? l.Value[1] : l.Value[0];
                if (name.PinNameText.text != want)
                {
                    name.PinNameText.text = want;
                    name.PinNameText.textWrappingMode = TextWrappingModes.NoWrap;
                    name.PinNameText.overflowMode = TextOverflowModes.Overflow;
                    // The extra lines go down from the name, not up over the mark.
                    name.PinNameText.alignment = (TextAlignmentOptions)(((int)name.PinNameText.alignment & 0xFF) | (int)TextAlignmentOptions.Top);
                }
                // Too far out the game names nothing; the settlement under the pointer is named all the same.
                if (!named && l.Key == hover) name.PinNameGameObject.SetActive(true);
                else if (!named && l.Key == _hovered) name.PinNameGameObject.SetActive(false);
            }
            _hovered = hover;
        }

        private const float MinZonePixels = 50f;

        [HarmonyPatch(typeof(Minimap), "UpdatePins"), HarmonyPostfix]
        private static void Tint()
        {
            if (Tints.Count == 0) return;
            foreach (KeyValuePair<Minimap.PinData, Color> pin in Tints)
            {
                bool zone = Zones.Contains(pin.Key);
                // Lands are for the large map: on the small one a single settlement would fill the whole corner.
                if (zone && pin.Key.m_uiElement != null) pin.Key.m_uiElement.gameObject.SetActive(Minimap.instance.m_mode == Minimap.MapMode.Large);
                if (pin.Key.m_iconElement != null) pin.Key.m_iconElement.color = pin.Value;
                // Zoomed far out a settlement's land would shrink to a dot; it stays a patch of colour you can see.
                RectTransform rect = pin.Key.m_uiElement;
                if (rect != null && zone && Minimap.instance.m_mode == Minimap.MapMode.Large && rect.sizeDelta.x < MinZonePixels) rect.sizeDelta = new Vector2(MinZonePixels, MinZonePixels);
            }
        }
    }
}
