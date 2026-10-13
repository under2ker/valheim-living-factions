using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Jotunn.Entities;
using Jotunn.Managers;
using LivingFactions.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Playing together. The simulation runs in one place only: on whoever hosts the world (a player
    /// or a dedicated server). Everyone else holds a copy of its state that the host sends out, and
    /// reads it exactly as the host reads the real thing.
    ///
    ///   host -> everyone   the state, every few seconds and right after anything a player did
    ///   host -> everyone   who builds which settlement (the nearest player; a dedicated server
    ///                      has no ground loaded and builds nothing itself)
    ///   anyone -> host     what a player did: a gift, a purchase, a kill, a declaration of war.
    ///                      The player's own copy is changed at once, so nothing waits on the wire
    ///   builder -> host    how far each building has been put up in the world
    ///   host -> builder    "a raid is arriving at the settlement you are standing in"
    /// </summary>
    internal static class Net
    {
        private static CustomRPC _state, _duty, _call, _report, _raid, _dev;

        /// <summary>Settlement -> the player (peer id) who puts it up in the world.</summary>
        private static readonly Dictionary<int, long> Builders = new Dictionary<int, long>();
        private static readonly HashSet<long> RoadKeepers = new HashSet<long>();
        private static string _dutySent = "";
        private static float _nextDuty, _nextState, _nextReport;
        private static bool _dirty;
        private static int _peersSeen;
        private static double _lastEventDay = double.NaN;

        private struct Progress
        {
            public int Pieces, TerrainRev, LayoutRev;
            public float GroundY;

            public bool Same(Progress o)
            {
                return Pieces == o.Pieces && TerrainRev == o.TerrainRev && LayoutRev == o.LayoutRev && (GroundY == o.GroundY || (float.IsNaN(GroundY) && float.IsNaN(o.GroundY)));
            }
        }

        /// <summary>What the host last told us about each building, and what we have changed since and not yet heard back.</summary>
        private static readonly Dictionary<long, Progress> Known = new Dictionary<long, Progress>(), Unconfirmed = new Dictionary<long, Progress>();

        public static bool IsHost { get { return ZNet.instance != null && ZNet.instance.IsServer(); } }
        public static bool IsDedicated { get { return ZNet.instance != null && ZNet.instance.IsDedicated(); } }
        private static long Me { get { return ZNet.GetUID(); } }
        private static bool Alone { get { return ZNet.instance == null || ZNet.instance.GetPeers().Count == 0; } }

        public static void Init()
        {
            _state = NetworkManager.Instance.AddRPC("LF_State", null, OnState);
            _duty = NetworkManager.Instance.AddRPC("LF_Duty", null, OnDuty);
            _raid = NetworkManager.Instance.AddRPC("LF_Raid", null, OnRaid);
            _call = NetworkManager.Instance.AddRPC("LF_Call", OnCall, null);
            _report = NetworkManager.Instance.AddRPC("LF_Report", OnReport, null);
            _dev = NetworkManager.Instance.AddRPC("LF_Dev", OnDev, null);
        }

        public static void Reset()
        {
            Builders.Clear();
            RoadKeepers.Clear();
            Known.Clear();
            Unconfirmed.Clear();
            _dutySent = "";
            _dirty = false;
            _peersSeen = 0;
            _lastEventDay = double.NaN;
        }

        // ------------------------------------------------------------------ who builds what

        /// <summary>True when this player is the one who puts this settlement up in the world.</summary>
        public static bool IsBuilder(Settlement s)
        {
            if (IsDedicated) return false;
            if (IsHost && Alone) return true;
            long who;
            return Builders.TryGetValue(s.Id, out who) && who == Me;
        }

        /// <summary>True when this player looks after roads, lamps and torn ground around them.</summary>
        public static bool KeepsRoads
        {
            get
            {
                if (IsDedicated) return false;
                if (IsHost && Alone) return true;
                return RoadKeepers.Contains(Me);
            }
        }

        private static List<KeyValuePair<long, Vector3>> Players()
        {
            List<KeyValuePair<long, Vector3>> all = new List<KeyValuePair<long, Vector3>>();
            if (!IsDedicated && Player.m_localPlayer != null) all.Add(new KeyValuePair<long, Vector3>(Me, Player.m_localPlayer.transform.position));
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer.IsReady()) all.Add(new KeyValuePair<long, Vector3>(peer.m_uid, peer.m_refPos));
            }
            return all;
        }

        private static void AssignDuties()
        {
            List<KeyValuePair<long, Vector3>> players = Players();
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                long now, best = 0L;
                bool has = Builders.TryGetValue(s.Id, out now);
                float bestDist = 230f;
                bool keep = false;
                foreach (KeyValuePair<long, Vector3> p in players)
                {
                    float d = Utils.DistanceXZ(p.Value, centre);
                    // Whoever builds a place keeps it while they stay around: two builders in turn would put pieces up twice.
                    if (has && p.Key == now && d < 270f) keep = true;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = p.Key;
                    }
                }
                if (keep) continue;
                if (best != 0L) Builders[s.Id] = best;
                else if (has) Builders.Remove(s.Id);
            }
            RoadKeepers.Clear();
            foreach (KeyValuePair<long, Vector3> p in players)
            {
                bool senior = true;
                foreach (KeyValuePair<long, Vector3> q in players)
                {
                    if (q.Key < p.Key && Utils.DistanceXZ(p.Value, q.Value) < 260f) senior = false;
                }
                if (senior) RoadKeepers.Add(p.Key);
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, long> b in Builders) sb.Append(b.Key).Append(':').Append(b.Value).Append(',');
            sb.Append('|');
            foreach (long keeper in RoadKeepers) sb.Append(keeper).Append(',');
            string duty = sb.ToString();
            if (duty == _dutySent) return;
            _dutySent = duty;
            ZPackage pkg = new ZPackage();
            pkg.Write(duty);
            _duty.SendPackage(ZNet.instance.GetPeers(), pkg);
        }

        private static IEnumerator OnDuty(long sender, ZPackage pkg)
        {
            string[] halves = pkg.ReadString().Split('|');
            Builders.Clear();
            RoadKeepers.Clear();
            foreach (string pair in halves[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = pair.Split(':');
                Builders[int.Parse(kv[0], CultureInfo.InvariantCulture)] = long.Parse(kv[1], CultureInfo.InvariantCulture);
            }
            if (halves.Length > 1)
            {
                foreach (string keeper in halves[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) RoadKeepers.Add(long.Parse(keeper, CultureInfo.InvariantCulture));
            }
            yield break;
        }

        // ------------------------------------------------------------------ the state

        public static void HostUpdate()
        {
            if (Alone)
            {
                _peersSeen = 0;
                return;
            }
            int peers = ZNet.instance.GetPeers().Count;
            if (peers != _peersSeen)
            {
                // Somebody has just joined: they get the state and the duties without waiting for the next round.
                _peersSeen = peers;
                _dirty = true;
                _dutySent = "";
                _nextState = Mathf.Min(_nextState, Time.unscaledTime + 1f);
            }
            if (Time.unscaledTime >= _nextDuty)
            {
                _nextDuty = Time.unscaledTime + 1f;
                AssignDuties();
            }
            if (Time.unscaledTime < _nextState && !(_dirty && Time.unscaledTime >= _nextState - 7f)) return;
            _nextState = Time.unscaledTime + 8f;
            _dirty = false;
            ZPackage pkg = new ZPackage();
            pkg.Write(JsonConvert.SerializeObject(SimHost.Sim.State, Formatting.None));
            _state.SendPackage(ZNet.instance.GetPeers(), pkg);
        }

        private static long Key(int settlementId, int buildingId)
        {
            return ((long)settlementId << 32) | (uint)buildingId;
        }

        private static Progress Of(Building b)
        {
            return new Progress { Pieces = b.SpawnedPieces, GroundY = b.GroundY, TerrainRev = b.TerrainRev, LayoutRev = b.LayoutRev };
        }

        private static IEnumerator OnState(long sender, ZPackage pkg)
        {
            WorldState state;
            try
            {
                state = JsonConvert.DeserializeObject<WorldState>(pkg.ReadString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read the faction state sent by the host: " + e);
                yield break;
            }
            if (state == null || IsHost) yield break;

            // Whatever was put up in the last moment goes on record first: the copy that has just arrived is
            // older than that, and taking it as it is would have the same pieces placed a second time.
            if (SimHost.Active) ReportProgress();

            // What we have built since the host last heard from us stays ours until the host says the same.
            Known.Clear();
            foreach (Settlement s in state.Settlements)
            {
                foreach (Building b in s.Buildings)
                {
                    long key = Key(s.Id, b.Id);
                    Progress ours;
                    if (Unconfirmed.TryGetValue(key, out ours))
                    {
                        if (ours.Same(Of(b))) Unconfirmed.Remove(key);
                        else
                        {
                            b.SpawnedPieces = ours.Pieces;
                            b.GroundY = ours.GroundY;
                            b.TerrainRev = ours.TerrainRev;
                            b.LayoutRev = ours.LayoutRev;
                        }
                    }
                    Known[key] = Of(b);
                }
            }
            bool first = !SimHost.Active;
            SimHost.SetMirror(state);
            if (first) Plugin.Log.LogInfo("Faction state received from the host: " + state.Factions.Count + " factions, " + state.Settlements.Count + " settlements, day " + state.Day.ToString("0.0"));

            // News the host has written into the chronicle since the last copy.
            double newest = _lastEventDay;
            foreach (SimEvent e in state.Chronicle)
            {
                if (!double.IsNaN(_lastEventDay) && e.Day > _lastEventDay) SimHost.Announce(e);
                if (double.IsNaN(newest) || e.Day > newest) newest = e.Day;
            }
            _lastEventDay = double.IsNaN(newest) ? 0.0 : newest;
        }

        public static void ClientUpdate()
        {
            if (!SimHost.Active || Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 1f;
            ReportProgress();
        }

        /// <summary>Tells the host what this player has built since the last time, and remembers it as ours until the host agrees.</summary>
        private static void ReportProgress()
        {
            ZPackage pkg = new ZPackage();
            int count = 0;
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                foreach (Building b in s.Buildings)
                {
                    long key = Key(s.Id, b.Id);
                    Progress now = Of(b), was;
                    bool changed = !Known.TryGetValue(key, out was) || !was.Same(now);
                    if (!changed && !Unconfirmed.ContainsKey(key)) continue;
                    Known[key] = now;
                    Unconfirmed[key] = now;
                    pkg.Write(s.Id);
                    pkg.Write(b.Id);
                    pkg.Write(now.Pieces);
                    pkg.Write(now.GroundY);
                    pkg.Write(now.TerrainRev);
                    pkg.Write(now.LayoutRev);
                    count++;
                }
            }
            if (count == 0) return;
            ZPackage framed = new ZPackage();
            framed.Write(count);
            framed.Write(pkg.GetArray());
            _report.SendPackage(ZNet.instance.GetServerPeer().m_uid, framed);
        }

        private static IEnumerator OnReport(long sender, ZPackage framed)
        {
            if (!SimHost.Active || !IsHost) yield break;
            int count = framed.ReadInt();
            ZPackage pkg = new ZPackage(framed.ReadByteArray());
            for (int i = 0; i < count; i++)
            {
                int sid = pkg.ReadInt(), bid = pkg.ReadInt(), pieces = pkg.ReadInt();
                float ground = pkg.ReadSingle();
                int terrain = pkg.ReadInt(), layout = pkg.ReadInt();
                Settlement s = SimHost.Sim.State.GetSettlement(sid);
                if (s == null) continue;
                foreach (Building b in s.Buildings)
                {
                    if (b.Id != bid) continue;
                    b.SpawnedPieces = pieces;
                    b.GroundY = ground;
                    b.TerrainRev = terrain;
                    b.LayoutRev = layout;
                }
            }
            _dirty = true;
        }

        // ------------------------------------------------------------------ raids a player can see

        /// <summary>The simulation is sending a raid: whoever is building the target settlement sees it arrive.</summary>
        public static bool StageRaid(Settlement from, Settlement target, float committed)
        {
            long who;
            if (Alone || !Builders.TryGetValue(target.Id, out who) || who == Me) return Raids.Stage(from, target, committed);
            ZPackage pkg = new ZPackage();
            pkg.Write(from.Id);
            pkg.Write(target.Id);
            pkg.Write(committed);
            _raid.SendPackage(who, pkg);
            return false;
        }

        private static IEnumerator OnRaid(long sender, ZPackage pkg)
        {
            int fromId = pkg.ReadInt(), targetId = pkg.ReadInt();
            float committed = pkg.ReadSingle();
            if (!SimHost.Active) yield break;
            Settlement from = SimHost.Sim.State.GetSettlement(fromId), target = SimHost.Sim.State.GetSettlement(targetId);
            if (from != null && target != null) Raids.Stage(from, target, committed);
        }

        // ------------------------------------------------------------------ testing

        /// <summary>A test guest asks the host to run a dev command (only a host started with dev tools listens).</summary>
        public static string DevOnHost(string command)
        {
            if (IsHost) return Commands.Run(command);
            ZPackage pkg = new ZPackage();
            pkg.Write(command);
            _dev.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
            return "asked the host to run: " + command;
        }

        private static IEnumerator OnDev(long sender, ZPackage pkg)
        {
            string command = pkg.ReadString();
            if (!ModConfig.DevTools.Value || !IsHost) yield break;
            string result;
            try
            {
                result = Commands.Run(command);
            }
            catch (Exception e)
            {
                result = "ERROR " + e.Message;
            }
            Plugin.Log.LogInfo("[dev] for a guest: " + command + " => " + (result.Length > 300 ? result.Substring(0, 300) : result));
            _dirty = true;
        }

        public static string Describe()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(IsDedicated ? "dedicated server" : IsHost ? (Alone ? "host, alone" : "host with " + ZNet.instance.GetPeers().Count + " guests") : "guest");
            sb.Append(", builds settlements [");
            foreach (KeyValuePair<int, long> b in Builders) if (b.Value == Me) sb.Append(b.Key).Append(' ');
            sb.Append("], keeps roads ").Append(KeepsRoads).Append(", unconfirmed building reports ").Append(Unconfirmed.Count);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ what players do

        private static void Send(string what, params object[] args)
        {
            if (IsHost || ZRoutedRpc.instance == null) return;
            ZPackage pkg = new ZPackage();
            pkg.Write(what);
            pkg.Write(args.Length);
            foreach (object arg in args)
            {
                if (arg is int)
                {
                    pkg.Write((byte)0);
                    pkg.Write((int)arg);
                }
                else if (arg is long)
                {
                    pkg.Write((byte)1);
                    pkg.Write((long)arg);
                }
                else if (arg is float)
                {
                    pkg.Write((byte)2);
                    pkg.Write((float)arg);
                }
                else if (arg is bool)
                {
                    pkg.Write((byte)3);
                    pkg.Write((bool)arg);
                }
                else
                {
                    pkg.Write((byte)4);
                    pkg.Write(arg != null ? arg.ToString() : "");
                }
            }
            _call.SendPackage(ZNet.instance.GetServerPeer().m_uid, pkg);
        }

        private static IEnumerator OnCall(long sender, ZPackage pkg)
        {
            if (!SimHost.Active || !IsHost) yield break;
            string what = pkg.ReadString();
            object[] a = new object[pkg.ReadInt()];
            for (int i = 0; i < a.Length; i++)
            {
                switch (pkg.ReadByte())
                {
                    case 0: a[i] = pkg.ReadInt(); break;
                    case 1: a[i] = pkg.ReadLong(); break;
                    case 2: a[i] = pkg.ReadSingle(); break;
                    case 3: a[i] = pkg.ReadBool(); break;
                    default: a[i] = pkg.ReadString(); break;
                }
            }
            try
            {
                Apply(what, a);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("A player's '" + what + "' could not be applied: " + e.Message);
            }
            _dirty = true;
        }

        /// <summary>The host does for a remote player what that player's own game has already done to its copy.</summary>
        private static void Apply(string what, object[] a)
        {
            Simulation sim = SimHost.Sim;
            WorldState st = sim.State;
            switch (what)
            {
                case "war": sim.PlayerDeclareWar(st.GetFaction((int)a[0]), (long)a[1]); break;
                case "peace": sim.PlayerProposePeace(st.GetFaction((int)a[0]), (long)a[1], (int)a[2]); break;
                case "ally": sim.PlayerProposeAlliance(st.GetFaction((int)a[0]), (long)a[1]); break;
                case "unally": sim.PlayerBreakAlliance(st.GetFaction((int)a[0]), (long)a[1]); break;
                case "gift": sim.PlayerGift(st.GetSettlement((int)a[0]), (long)a[1], (Res)(int)a[2], (float)a[3]); break;
                case "buy": sim.PlayerBuy(st.GetSettlement((int)a[0]), (Res)(int)a[1], (int)a[2], (long)a[3], (int)a[4]); break;
                case "sell": sim.PlayerSell(st.GetSettlement((int)a[0]), (Res)(int)a[1], (int)a[2], (long)a[3]); break;
                case "killed": sim.PlayerKilledMember(st.GetSettlement((int)a[0]), (long)a[1], (bool)a[2]); break;
                case "died": sim.MemberDied(st.GetSettlement((int)a[0]), (bool)a[1]); break;
                case "attacked": sim.PlayerAttackedMember(st.GetFaction((int)a[0]), (long)a[1]); break;
                case "damaged": sim.PlayerDamagedProperty(st.GetFaction((int)a[0]), (long)a[1]); break;
                case "stole": sim.PlayerStole(st.GetFaction((int)a[0]), (long)a[1], (float)a[2]); break;
                case "helped": sim.PlayerHelped(st.GetFaction((int)a[0]), (long)a[1], (float)a[2]); break;
                case "rep": sim.AddReputation(st.GetFaction((int)a[0]), (long)a[1], (float)a[2]); break;
                case "name": sim.PlayerRel(st.GetFaction((int)a[0]), (long)a[1]).Name = (string)a[2]; break;
                case "stock":
                    Settlement s = st.GetSettlement((int)a[0]);
                    if (s != null) s.Stock[(int)a[1]] = Mathf.Max(0f, s.Stock[(int)a[1]] + (float)a[2]);
                    break;
                case "found":
                    List<int> known;
                    if (!st.Discovered.TryGetValue((long)a[0], out known)) st.Discovered[(long)a[0]] = known = new List<int>();
                    if (!known.Contains((int)a[1])) known.Add((int)a[1]);
                    break;
            }
        }

        public static DiploResult PlayerDeclareWar(Faction f, long pid)
        {
            DiploResult r = SimHost.Sim.PlayerDeclareWar(f, pid);
            Send("war", f.Id, pid);
            return r;
        }

        public static DiploResult PlayerProposePeace(Faction f, long pid, int coins)
        {
            DiploResult r = SimHost.Sim.PlayerProposePeace(f, pid, coins);
            Send("peace", f.Id, pid, coins);
            return r;
        }

        public static DiploResult PlayerProposeAlliance(Faction f, long pid)
        {
            DiploResult r = SimHost.Sim.PlayerProposeAlliance(f, pid);
            Send("ally", f.Id, pid);
            return r;
        }

        public static DiploResult PlayerBreakAlliance(Faction f, long pid)
        {
            DiploResult r = SimHost.Sim.PlayerBreakAlliance(f, pid);
            Send("unally", f.Id, pid);
            return r;
        }

        public static float PlayerGift(Settlement s, long pid, Res res, float amount)
        {
            float r = SimHost.Sim.PlayerGift(s, pid, res, amount);
            Send("gift", s.Id, pid, (int)res, amount);
            return r;
        }

        public static int PlayerBuy(Settlement s, Res res, int amount, long pid, int coins)
        {
            int r = SimHost.Sim.PlayerBuy(s, res, amount, pid, coins);
            Send("buy", s.Id, (int)res, amount, pid, coins);
            return r;
        }

        public static int PlayerSell(Settlement s, Res res, int amount, long pid)
        {
            int r = SimHost.Sim.PlayerSell(s, res, amount, pid);
            Send("sell", s.Id, (int)res, amount, pid);
            return r;
        }

        public static void PlayerKilledMember(Settlement s, long pid, bool soldier)
        {
            SimHost.Sim.PlayerKilledMember(s, pid, soldier);
            Send("killed", s.Id, pid, soldier);
        }

        public static void MemberDied(Settlement s, bool soldier)
        {
            SimHost.Sim.MemberDied(s, soldier);
            Send("died", s.Id, soldier);
        }

        public static void PlayerAttackedMember(Faction f, long pid)
        {
            SimHost.Sim.PlayerAttackedMember(f, pid);
            Send("attacked", f.Id, pid);
        }

        public static void PlayerDamagedProperty(Faction f, long pid)
        {
            SimHost.Sim.PlayerDamagedProperty(f, pid);
            Send("damaged", f.Id, pid);
        }

        public static bool PlayerStole(Faction f, long pid, float value)
        {
            bool r = SimHost.Sim.PlayerStole(f, pid, value);
            Send("stole", f.Id, pid, value);
            return r;
        }

        public static void PlayerHelped(Faction f, long pid, float weight)
        {
            SimHost.Sim.PlayerHelped(f, pid, weight);
            Send("helped", f.Id, pid, weight);
        }

        public static void AddReputation(Faction f, long pid, float delta)
        {
            SimHost.Sim.AddReputation(f, pid, delta);
            Send("rep", f.Id, pid, delta);
        }

        public static void NamePlayer(Faction f, long pid, string name)
        {
            if (f == null) return;
            SimHost.Sim.PlayerRel(f, pid).Name = name;
            Send("name", f.Id, pid, name);
        }

        public static void AddStock(Settlement s, Res res, float delta)
        {
            s.Stock[(int)res] = Mathf.Max(0f, s.Stock[(int)res] + delta);
            Send("stock", s.Id, (int)res, delta);
        }

        public static void Discover(long pid, int settlementId)
        {
            Send("found", pid, settlementId);
        }
    }
}
