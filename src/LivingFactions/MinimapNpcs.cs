using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The people of the factions around the player as small dots on the minimap: the faction's colour,
    /// red for those hostile to the player, a larger dot for a chief. Only characters with a
    /// FactionMember (the mod's own people) are marked, never other creatures. The dots follow them twice
    /// a second, go when they die or are unloaded, and are kept off the large map.
    /// </summary>
    [HarmonyPatch]
    internal static class MinimapNpcs
    {
        private const float Radius = 120f;
        private const int MaxPins = 40;
        private const float Period = 0.5f;
        private const float DotPixels = 8f, ChiefPixels = 11f;
        private static readonly Color Hostile = new Color32(0xFF, 0x4A, 0x3A, 0xFF);

        private class Dot
        {
            public Minimap.PinData Pin;
            public Color Colour;
            public float Size;
        }

        private static readonly Dictionary<FactionMember, Dot> Dots = new Dictionary<FactionMember, Dot>();
        private static readonly List<KeyValuePair<float, FactionMember>> Near = new List<KeyValuePair<float, FactionMember>>();
        private static readonly HashSet<FactionMember> Keep = new HashSet<FactionMember>();
        private static readonly List<FactionMember> Gone = new List<FactionMember>();
        private static Minimap _owner;
        private static float _next;
        private static Sprite _sprite;

        private static readonly AccessTools.FieldRef<Minimap, bool> PinUpdateRequired = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");

        /// <summary>Takes every dot off the map (the factions unloaded, or the dots switched off).</summary>
        public static void Reset()
        {
            if (Minimap.instance != null && Minimap.instance == _owner)
            {
                foreach (Dot d in Dots.Values) Minimap.instance.RemovePin(d.Pin);
            }
            Dots.Clear();
            _owner = null;
        }

        public static void Update()
        {
            Minimap map = Minimap.instance;
            // A new map (another world) starts without our pins: the old ones went with the old map.
            if (map != _owner)
            {
                Dots.Clear();
                _owner = map;
            }
            if (map == null) return;
            Player me = Player.m_localPlayer;
            if (!ModConfig.MapNpcs.Value || !SimHost.Active || me == null)
            {
                if (Dots.Count > 0) Reset();
                return;
            }
            // Only the minimap shows them: while the large map is open nothing needs to move.
            if (Time.time < _next || map.m_mode != Minimap.MapMode.Small) return;
            _next = Time.time + Period;

            Vector3 at = me.transform.position;
            Near.Clear();
            foreach (FactionMember m in FactionMember.All)
            {
                if (m == null || m.FactionId == 0 || m.Humanoid == null || m.Humanoid.IsDead()) continue;
                float d = Utils.DistanceXZ(at, m.transform.position);
                if (d <= Radius) Near.Add(new KeyValuePair<float, FactionMember>(d, m));
            }
            Near.Sort((a, b) => a.Key.CompareTo(b.Key));

            Keep.Clear();
            WorldState state = SimHost.Sim.State;
            long pid = me.GetPlayerID();
            for (int i = 0; i < Near.Count && i < MaxPins; i++)
            {
                FactionMember m = Near[i].Value;
                Keep.Add(m);
                Dot dot;
                if (!Dots.TryGetValue(m, out dot))
                {
                    dot = new Dot { Pin = map.AddPin(m.transform.position, Minimap.PinType.None, "", false, false) };
                    dot.Pin.m_icon = Sprite();
                    Dots[m] = dot;
                }
                dot.Pin.m_pos = m.transform.position;
                Faction f = state.GetFaction(m.FactionId);
                Standing st = MapZones.StandingOf(f, pid);
                dot.Colour = st == Standing.War || st == Standing.Hostile || Grudges.AngryAt(m, me) ? Hostile : MapZones.ColourOf(f);
                dot.Size = m.Role == NpcRole.Chief ? ChiefPixels : DotPixels;
            }

            Gone.Clear();
            foreach (KeyValuePair<FactionMember, Dot> d in Dots)
            {
                if (d.Key == null || !Keep.Contains(d.Key)) Gone.Add(d.Key);
            }
            foreach (FactionMember m in Gone)
            {
                map.RemovePin(Dots[m].Pin);
                Dots.Remove(m);
            }
            PinUpdateRequired(map) = true;
        }

        /// <summary>For `lf mapinfo`.</summary>
        public static string Info()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Dots.Count + " villager dots (on " + ModConfig.MapNpcs.Value + ", radius " + Radius + ", max " + MaxPins + ")");
            int shown = 0, n = 0;
            foreach (Dot d in Dots.Values) if (d.Pin.m_uiElement != null && d.Pin.m_uiElement.gameObject.activeInHierarchy) shown++;
            sb.Append(", " + shown + " drawn");
            Vector3 at = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            foreach (KeyValuePair<FactionMember, Dot> d in Dots)
            {
                if (n++ >= 6 || d.Key == null) break;
                sb.Append("\n  " + d.Key.Role + " of faction " + d.Key.FactionId + " at " + Utils.DistanceXZ(at, d.Key.transform.position).ToString("0") + " m, pin off by "
                    + Utils.DistanceXZ(d.Value.Pin.m_pos, d.Key.transform.position).ToString("0.0") + " m, colour #" + ColorUtility.ToHtmlStringRGB(d.Value.Colour));
            }
            return sb.ToString();
        }

        /// <summary>Small, coloured, and only on the minimap.</summary>
        [HarmonyPatch(typeof(Minimap), "UpdatePins"), HarmonyPostfix]
        private static void Style(Minimap __instance)
        {
            if (Dots.Count == 0 || __instance != _owner) return;
            bool small = __instance.m_mode == Minimap.MapMode.Small;
            foreach (Dot d in Dots.Values)
            {
                RectTransform ui = d.Pin.m_uiElement;
                if (ui == null) continue;
                if (ui.gameObject.activeSelf != small) ui.gameObject.SetActive(small);
                if (!small) continue;
                ui.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, d.Size);
                ui.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, d.Size);
                if (d.Pin.m_iconElement != null) d.Pin.m_iconElement.color = d.Colour;
            }
        }

        /// <summary>A round dot with a dark rim, so it reads on any ground.</summary>
        private static Sprite Sprite()
        {
            if (_sprite != null) return _sprite;
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] px = new Color[size * size];
            float centre = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre)) / centre;
                    float alpha = Mathf.Clamp01((1f - d) * size / 2f);
                    float shade = d > 0.7f ? 0.15f : 1f;
                    px[y * size + x] = new Color(shade, shade, shade, alpha);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            _sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return _sprite;
        }
    }
}
