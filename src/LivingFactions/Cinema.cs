using System.Globalization;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>
    /// Camera moves and a day counter for filming the mod (dev tools only). All moves run on real time,
    /// so the game's speed can change under them, and ease in and out.
    ///   orbit &lt;settlement&gt; &lt;radius&gt; &lt;height&gt; &lt;seconds&gt; [start°] [sweep°]   circle round a settlement
    ///   fly &lt;settlement&gt; &lt;bearing°&gt; &lt;from m&gt; &lt;to m&gt; &lt;from h&gt; &lt;to h&gt; &lt;seconds&gt; come in (or pull out) toward its heart
    ///   dolly x y z tx ty tz  x y z tx ty tz  &lt;seconds&gt;               from one framing to another
    ///   follow &lt;text from npcs&gt; [seconds] [side]                        over the shoulder of an inhabitant
    ///   timelapse &lt;days&gt; &lt;seconds&gt; [sun]                                the factions run on; `sun` turns the sky with the days
    ///   daycard on|off [title]                                           a large day count on screen
    ///   still                                                            stop the move where it is
    /// </summary>
    internal static class Cinema
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private enum Move { None, Orbit, Fly, Dolly, Follow }
        private static Move _move;
        private static float _start, _length;
        private static Vector3 _centre, _from, _to, _lookFrom, _lookTo;
        private static float _radius, _heightA, _heightB, _angleA, _angleB, _distA, _distB, _side;
        private static FactionMember _subject;
        private static Vector3 _smoothPos, _smoothLook;

        private static float _lapseStart, _lapseLength;
        private static double _lapseDays, _lapseDone;
        private static bool _lapseSun;

        private static Text _card;
        private static string _cardTitle = "";

        public static bool Moving { get { return _move != None || _lapseLength > 0f; } }
        private const Move None = Move.None;

        public static string Command(string[] a)
        {
            if (!SimHost.Active || Player.m_localPlayer == null) return "no world";
            switch (a[0])
            {
                case "orbit":
                {
                    Settlement s = SimHost.Sim.State.GetSettlement(int.Parse(a[1]));
                    if (s == null) return "no such settlement";
                    _centre = Heart(s);
                    _radius = F(a, 2, 40f);
                    _heightA = _heightB = F(a, 3, 20f);
                    _length = F(a, 4, 20f);
                    _angleA = F(a, 5, 0f) * Mathf.Deg2Rad;
                    _angleB = _angleA + F(a, 6, 120f) * Mathf.Deg2Rad;
                    return Begin(Move.Orbit, "orbit round " + s.Name);
                }
                case "fly":
                {
                    Settlement s = SimHost.Sim.State.GetSettlement(int.Parse(a[1]));
                    if (s == null) return "no such settlement";
                    _centre = Heart(s);
                    _angleA = _angleB = F(a, 2, 0f) * Mathf.Deg2Rad;
                    _distA = F(a, 3, 250f);
                    _distB = F(a, 4, 40f);
                    _heightA = F(a, 5, 80f);
                    _heightB = F(a, 6, 15f);
                    _length = F(a, 7, 15f);
                    return Begin(Move.Fly, "flying to " + s.Name);
                }
                case "dolly":
                    if (a.Length < 14) return "dolly x y z tx ty tz x y z tx ty tz seconds";
                    _from = V(a, 1); _lookFrom = V(a, 4); _to = V(a, 7); _lookTo = V(a, 10);
                    _length = F(a, 13, 10f);
                    return Begin(Move.Dolly, "dolly");
                case "follow":
                {
                    if (a.Length < 2) return "follow <text from npcs> [seconds] [side]";
                    _subject = null;
                    foreach (FactionMember m in FactionMember.All)
                    {
                        if (m.Describe().IndexOf(a[1], System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        _subject = m;
                        break;
                    }
                    if (_subject == null) return "nobody matches " + a[1];
                    _length = F(a, 2, 15f);
                    _side = F(a, 3, 1f);
                    return Begin(Move.Follow, "following " + _subject.Describe());
                }
                case "timelapse":
                    _lapseDays = F(a, 1, 30f);
                    _lapseLength = F(a, 2, 30f);
                    _lapseSun = a.Length > 3 && a[3] == "sun";
                    _lapseStart = Time.unscaledTime;
                    _lapseDone = 0.0;
                    return "timelapse of " + _lapseDays.ToString("0", Inv) + " days in " + _lapseLength.ToString("0", Inv) + " s";
                case "daycard":
                    _cardTitle = a.Length > 2 ? string.Join(" ", a, 2, a.Length - 2) : "";
                    if (a.Length > 1 && a[1] == "off")
                    {
                        if (_card != null) _card.gameObject.SetActive(false);
                        return "day card off";
                    }
                    MakeCard();
                    return "day card on";
                case "still":
                    _move = Move.None;
                    _lapseLength = 0f;
                    return "still";
            }
            return "unknown move";
        }

        private static string Begin(Move move, string what)
        {
            GearView.Off();
            _move = move;
            _start = Time.unscaledTime;
            _smoothPos = Vector3.zero;
            Step(0f);
            return what + " for " + _length.ToString("0.#", Inv) + " s";
        }

        /// <summary>Called every frame.</summary>
        public static void Update()
        {
            if (_move != Move.None)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _start) / Mathf.Max(0.1f, _length));
                Step(t);
                // The camera stays where the move ends until the next one or `cam`.
                if (t >= 1f) _move = Move.None;
            }
            if (_lapseLength > 0f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - _lapseStart) / _lapseLength);
                double due = _lapseDays * t;
                // A little at a time, so the village rises piece by piece instead of all at once.
                if (due - _lapseDone >= 0.02)
                {
                    SimHost.FastForward(due - _lapseDone);
                    _lapseDone = due;
                }
                if (_lapseSun && EnvMan.instance != null)
                {
                    // Night is short in a timelapse: the sun goes round fast and does not linger in the dark.
                    double day = SimHost.Sim.State.Day;
                    float frac = (float)(day - System.Math.Floor(day));
                    EnvMan.instance.m_debugTimeOfDay = true;
                    EnvMan.instance.m_debugTime = 0.22f + frac * 0.6f;
                }
                if (t >= 1f) _lapseLength = 0f;
            }
            if (_card != null && _card.gameObject.activeSelf && SimHost.Active)
            {
                string day = Texts.Get("ui_day", ((int)SimHost.Sim.State.Day).ToString());
                _card.text = _cardTitle.Length > 0 ? "<size=26>" + _cardTitle + "</size>\n" + day : day;
            }
        }

        private static void Step(float t)
        {
            float e = t * t * (3f - 2f * t);
            Vector3 pos, look;
            switch (_move)
            {
                case Move.Orbit:
                {
                    float ang = Mathf.Lerp(_angleA, _angleB, e);
                    pos = _centre + new Vector3(Mathf.Sin(ang) * _radius, _heightA, Mathf.Cos(ang) * _radius);
                    look = _centre;
                    break;
                }
                case Move.Fly:
                {
                    float dist = Mathf.Lerp(_distA, _distB, e);
                    pos = _centre + new Vector3(Mathf.Sin(_angleA) * dist, Mathf.Lerp(_heightA, _heightB, e), Mathf.Cos(_angleA) * dist);
                    look = _centre;
                    break;
                }
                case Move.Dolly:
                    pos = Vector3.Lerp(_from, _to, e);
                    look = Vector3.Lerp(_lookFrom, _lookTo, e);
                    break;
                case Move.Follow:
                {
                    if (_subject == null)
                    {
                        _move = Move.None;
                        return;
                    }
                    Transform s = _subject.transform;
                    Vector3 want = s.position - s.forward * 3.4f + s.right * 1.6f * _side + Vector3.up * 1.9f;
                    Vector3 wantLook = s.position + s.forward * 2f + Vector3.up * 1.2f;
                    // Softened, so the camera does not copy every step and turn.
                    if (_smoothPos == Vector3.zero) { _smoothPos = want; _smoothLook = wantLook; }
                    float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.5f);
                    _smoothPos = Vector3.Lerp(_smoothPos, want, k);
                    _smoothLook = Vector3.Lerp(_smoothLook, wantLook, k);
                    pos = _smoothPos;
                    look = _smoothLook;
                    break;
                }
                default:
                    return;
            }
            // Never under the ground.
            float ground;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out ground)) pos.y = Mathf.Max(pos.y, ground + 1.2f);
            Commands.CamPos = pos;
            Commands.CamTarget = look;
        }

        private static Vector3 Heart(Settlement s)
        {
            Vector3 p = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            float ground;
            if (ZoneSystem.instance.GetGroundHeight(p, out ground)) p.y = ground;
            else p.y = WorldGenerator.instance.GetHeight(p.x, p.z);
            return p + Vector3.up * 2f;
        }

        private static void MakeCard()
        {
            if (_card == null)
            {
                if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;
                Vector2 corner = new Vector2(0f, 0f);
                GameObject go = GUIManager.Instance.CreateText("", GUIManager.CustomGUIFront.transform, corner, corner, new Vector2(330f, 110f), GUIManager.Instance.AveriaSerifBold, 48, new Color(0.96f, 0.78f, 0.42f), true, Color.black, 560f, 160f, false);
                _card = go.GetComponent<Text>();
                _card.alignment = TextAnchor.LowerLeft;
                _card.raycastTarget = false;
            }
            _card.gameObject.SetActive(true);
        }

        private static float F(string[] a, int i, float fallback)
        {
            return a.Length > i ? float.Parse(a[i], Inv) : fallback;
        }

        private static Vector3 V(string[] a, int i)
        {
            return new Vector3(float.Parse(a[i], Inv), float.Parse(a[i + 1], Inv), float.Parse(a[i + 2], Inv));
        }
    }
}
