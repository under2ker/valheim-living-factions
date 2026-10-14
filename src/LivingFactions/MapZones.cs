using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>
    /// The lands of the factions on the map: a tinted circle around every settlement the player
    /// knows of, the faction's colour on its pin, and a legend of the factions in the corner of the
    /// large map. The circles are pins the game sizes to the world itself (as it does for event
    /// areas), so they stay true to the ground at every zoom.
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

        public static Color ColourOf(Faction f)
        {
            Color c;
            return f != null && Colours.TryGetValue(f.CultureId, out c) ? c : Color.white;
        }

        public static void Clear()
        {
            Tints.Clear();
            Zones.Clear();
        }

        /// <summary>A settlement's own mark in its faction's colour.</summary>
        public static void Mark(Minimap.PinData pin, Faction f)
        {
            if (pin != null) Tints[pin] = ColourOf(f);
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
