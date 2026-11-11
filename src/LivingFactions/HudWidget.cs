using System.Collections.Generic;
using System.Text;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>A small chronicle under the minimap: the day, who is at war, what the nearest known settlement is up to and the latest news.</summary>
    internal static class HudWidget
    {
        private static Text _text;
        private static float _next;
        private static Vector2 _draftPosition;
        private static bool _draftCollapsed;
        private static Vector2Int _screen;

        public static bool Editing { get; private set; }
        public static bool CanEdit => SimHost.Active && Player.m_localPlayer != null && Hud.instance != null;
        public static bool Collapsed => Editing ? _draftCollapsed : ModConfig.ChronicleCollapsed.Value;
        public static bool OnLowerHalf => _text != null && _text.rectTransform.position.y
            - _text.rectTransform.rect.height * Mathf.Abs(_text.rectTransform.lossyScale.y) * 0.5f < Screen.height * 0.5f;

        public static void Update()
        {
            if (!Editing && !ModMenu.IsOpen && Player.m_localPlayer != null && !Typing() && ModConfig.ChronicleKey.Value.IsDown())
            {
                // Off or on for good (the setting is saved), said on screen with the key to turn it back.
                ModConfig.HudWidget.Value = !ModConfig.HudWidget.Value;
                if (MessageHud.instance != null)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get(ModConfig.HudWidget.Value ? "msg_chronicle_shown" : "msg_chronicle_hidden", ModConfig.ChronicleKey.Value.ToString()));
            }
            // Hiding follows the game's HUD at once; only the text is refreshed on a timer.
            bool show = CanEdit && (Editing || (ModConfig.HudWidget.Value && !Hud.IsUserHidden() && !DiplomacyUI.IsOpen));
            if (!show)
            {
                if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
                _next = 0f;
                return;
            }
            Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
            if (Time.unscaledTime < _next && screen == _screen) return;
            _screen = screen;
            _next = Time.unscaledTime + 3f;
            if (_text == null)
            {
                // In front of the game's own interface: a HUD mod's panels drawn over the back layer hid it.
                if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
                Vector2 corner = new Vector2(1f, 1f);
                GameObject go = GUIManager.Instance.CreateText("", GUIManager.CustomGUIFront.transform, corner, corner, new Vector2(-180f, -300f), GUIManager.Instance.AveriaSerifBold, 13, Color.white, true, Color.black, 330f, 260f, false);
                _text = go.GetComponent<Text>();
                _text.alignment = TextAnchor.UpperRight;
                _text.rectTransform.pivot = Vector2.one;
                go.AddComponent<ChronicleDrag>();
            }
            _text.gameObject.SetActive(true);
            _text.raycastTarget = Editing;
            _text.text = Collapsed ? Heading(SimHost.Sim.State) : Compose();
            RectTransform panel = _text.rectTransform;
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(
                Screen.height / Mathf.Max(0.01f, Mathf.Abs(panel.lossyScale.y)), Mathf.Max(22f, _text.preferredHeight + 4f)));
            Place();
            if (!_reported)
            {
                // One line in the log, so a report of an empty corner can be told from one of a text off screen.
                _reported = true;
                Plugin.Log.LogInfo("Chronicle shown at " + _text.rectTransform.position + " on a " + Screen.width + "x" + Screen.height + " screen" + (_mapUsed ? ", under the minimap" : ""));
            }
        }

        private static bool _reported, _mapUsed;

        /// <summary>A key pressed while typing (chat, console, a sign) is a letter, not a command.</summary>
        private static bool Typing()
        {
            return (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible();
        }

        public static string Describe()
        {
            if (_text == null) return "chronicle widget not made yet (shown " + (ModConfig.HudWidget.Value && SimHost.Active) + ")";
            Vector3 p = _text.rectTransform.position;
            return "chronicle widget " + (_text.gameObject.activeInHierarchy ? "visible" : "hidden") + " at " + p + " on " + Screen.width + "x" + Screen.height + (_mapUsed ? " under the minimap" : "") + ", " + _text.text.Split('\n').Length + " lines" + (Editing ? ", editing" : "");
        }

        private static readonly Vector3[] MapCorners = new Vector3[4];

        public static void BeginEdit()
        {
            _draftPosition = new Vector2(ModConfig.ChronicleX.Value, ModConfig.ChronicleY.Value);
            _draftCollapsed = ModConfig.ChronicleCollapsed.Value;
            Editing = true;
            _next = 0f;
        }

        public static void ToggleCollapsed()
        {
            if (!Editing) return;
            _draftCollapsed = !_draftCollapsed;
            _next = 0f;
        }

        public static void EndEdit(bool save)
        {
            if (!Editing) return;
            if (save)
            {
                var config = Plugin.Instance.Config;
                bool autoSave = config.SaveOnConfigSet;
                config.SaveOnConfigSet = false;
                try
                {
                    ModConfig.ChronicleX.Value = _draftPosition.x;
                    ModConfig.ChronicleY.Value = _draftPosition.y;
                    ModConfig.ChronicleCollapsed.Value = _draftCollapsed;
                    config.Save();
                }
                finally { config.SaveOnConfigSet = autoSave; }
            }
            Editing = false;
            _next = 0f;
            if (_text != null) _text.raycastTarget = false;
        }

        public static void MovePreview(Vector2 position)
        {
            if (!Editing || _text == null) return;
            PlaceAt(position);
            Vector3 p = _text.rectTransform.position;
            _draftPosition = new Vector2(p.x / Screen.width, p.y / Screen.height);
        }

        private static void PlaceAt(Vector2 position)
        {
            RectTransform panel = _text.rectTransform;
            float width = Mathf.Min(Screen.width, panel.rect.width * Mathf.Abs(panel.lossyScale.x));
            float height = Mathf.Min(Screen.height, panel.rect.height * Mathf.Abs(panel.lossyScale.y));
            panel.position = new Vector3(Mathf.Clamp(position.x, width, Screen.width), Mathf.Clamp(position.y, height, Screen.height), panel.position.z);
        }

        private static void Place()
        {
            _mapUsed = false;
            Vector2 saved = Editing ? _draftPosition : new Vector2(ModConfig.ChronicleX.Value, ModConfig.ChronicleY.Value);
            if (saved.x >= 0f && saved.y >= 0f)
            {
                PlaceAt(new Vector2(saved.x * Screen.width, saved.y * Screen.height));
                return;
            }
            RectTransform panel = _text.rectTransform;
            RectTransform map = Minimap.instance != null && Minimap.instance.m_smallRoot != null && Minimap.instance.m_smallRoot.activeInHierarchy ? Minimap.instance.m_smallRoot.GetComponent<RectTransform>() : null;
            if (map != null)
            {
                map.GetWorldCorners(MapCorners);
                // Corners come as bottom-left, top-left, top-right, bottom-right.
                Vector3 hook = MapCorners[3];
                // A minimap moved or scaled by another mod can put the hook off the screen: then the corner.
                if (hook.x > Screen.width * 0.5f && hook.x <= Screen.width + 1f && hook.y > Screen.height * 0.3f && hook.y <= Screen.height + 1f)
                {
                    PlaceAt(new Vector2(hook.x, hook.y - 10f * panel.lossyScale.y));
                    _mapUsed = true;
                    return;
                }
            }
            PlaceAt(new Vector2(Screen.width - 20f, Screen.height * 0.72f));
        }

        private static string Heading(WorldState state)
        {
            return "<color=#FFA13C>" + Texts.Get("ui_chronicle") + " · " + Texts.Get("ui_day", ((int)state.Day).ToString()) + "</color>";
        }

        public static string Compose()
        {
            Simulation sim = SimHost.Sim;
            WorldState state = sim.State;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Heading(state));

            int wars = 0;
            for (int i = 0; i < state.Factions.Count && wars < 2; i++)
            {
                for (int j = i + 1; j < state.Factions.Count && wars < 2; j++)
                {
                    Faction a = state.Factions[i], b = state.Factions[j];
                    if (a.Eliminated || b.Eliminated || !sim.AreAtWar(a.Id, b.Id)) continue;
                    sb.AppendLine("<color=#FF6A5A>" + Texts.Get("tr_War") + ": " + Texts.FactionName(a) + " / " + Texts.FactionName(b) + "</color>");
                    wars++;
                }
            }

            // What the nearest settlement the player knows is about to do.
            Vector3 me = Player.m_localPlayer.transform.position;
            List<int> known;
            Settlement nearest = null;
            float best = 2500f;
            if (state.Discovered.TryGetValue(Player.m_localPlayer.GetPlayerID(), out known))
            {
                foreach (int id in known)
                {
                    Settlement s = state.GetSettlement(id);
                    if (s == null || s.Destroyed) continue;
                    float d = Utils.DistanceXZ(me, new Vector3(s.Pos.X, 0f, s.Pos.Z));
                    if (d < best)
                    {
                        best = d;
                        nearest = s;
                    }
                }
            }
            if (nearest != null)
            {
                // Who lives there, what they are at, what troubles them, what comes next.
                List<Forecast.Line> lines = Forecast.ForSettlement(sim, nearest);
                for (int i = 0; i < lines.Count; i++) sb.AppendLine(i == 0 ? "<color=#FFE9A8>" + lines[i].Text + "</color>" : Colour(lines[i]));
            }

            // The other peoples take turns: every few seconds the widget tells of the next one.
            List<Faction> others = new List<Faction>();
            foreach (Faction f in state.Factions)
            {
                if (!f.Eliminated && (nearest == null || f.Id != nearest.FactionId)) others.Add(f);
            }
            if (others.Count > 0)
            {
                Faction turn = others[(int)(Time.unscaledTime / RotateSeconds) % others.Count];
                sb.AppendLine("<color=#FFA13C>" + Texts.Get("hud_elsewhere") + "</color>");
                sb.AppendLine(Colour(Forecast.ForFaction(sim, turn)));
            }

            int shown = 0;
            for (int i = state.Chronicle.Count - 1; i >= 0 && shown < 2; i--)
            {
                SimEvent e = state.Chronicle[i];
                if (e.Kind == EventKind.BuildingStarted || e.Kind == EventKind.BuildingCompleted || e.Kind == EventKind.TradeCaravan) continue;
                if (shown == 0) sb.AppendLine("<color=#FFA13C>" + Texts.Get("hud_news") + "</color>");
                sb.AppendLine("<color=#CCCCCC>" + Texts.Describe(e, state) + "</color>");
                shown++;
            }
            return sb.ToString().TrimEnd();
        }

        private const float RotateSeconds = 7f;

        private static string Colour(Forecast.Line line)
        {
            string colour = line.Mood == Forecast.Mood.Trouble ? "#FFA64A" : line.Mood == Forecast.Mood.Good ? "#A8E890" : "#E6E6E6";
            return "<color=" + colour + ">" + line.Text + "</color>";
        }
    }

    internal sealed class ChronicleDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private Vector2 _offset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _offset = (Vector2)transform.position - eventData.pressPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) HudWidget.MovePreview(eventData.position + _offset);
        }
    }
}
