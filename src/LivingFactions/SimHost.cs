using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using LivingFactions.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>Owns the simulation for the loaded world: loading, saving, the clock and reactions to events.</summary>
    internal static class SimHost
    {
        public static Simulation Sim;

        /// <summary>Events raised since the game side last looked; the materializer and UI drain this.</summary>
        public static readonly Queue<SimEvent> Pending = new Queue<SimEvent>();

        private static string _savePath;
        private static bool _hooked;

        public static bool Active { get { return Sim != null; } }

        public static string DataDir
        {
            get { return Path.Combine(BepInEx.Paths.ConfigPath, "LivingFactions"); }
        }

        public static double DayLength
        {
            get { return EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800; }
        }

        public static double WorldDay
        {
            get { return ZNet.instance.GetTimeSeconds() / DayLength; }
        }

        public static void Update()
        {
            // The simulation belongs to whoever hosts the world.
            if (ZNet.instance == null || ZoneSystem.instance == null || ZNetScene.instance == null)
            {
                if (Sim != null) Unload();
                return;
            }
            if (!ZNet.instance.IsServer())
            {
                // A guest in somebody else's world reads the copy of the state the host sends (Net.cs).
                Net.ClientUpdate();
                return;
            }
            if (Sim == null)
            {
                if (!WorldReady()) return;
                Load();
            }
            Sim.AdvanceTo(WorldDay + Sim.State.ClockOffset, 6);
            Net.HostUpdate();
            if (_restoredNote != null && MessageHud.instance != null && Player.m_localPlayer != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_state_restored", _restoredNote));
                _restoredNote = null;
            }
        }

        /// <summary>A guest's copy of the host's state: read like the real thing, never run forward.</summary>
        public static void SetMirror(WorldState state)
        {
            if (Sim == null)
            {
                Texts.Lang = ResolveLanguage();
                CustomItems.Rename();
            }
            // The host took the factions back: what stands near this guest is looked at again.
            else if (state.Day < Sim.State.Day - 0.5) Materializer.ForgetAudit();
            Sim = new Simulation(state, new GameWorld(), ModConfig.ToBalance());
            Sim.PlayerBuyMult = Wares.BuyMultiplier;
        }

        private static bool WorldReady()
        {
            if (WorldGenerator.instance == null || EnvMan.instance == null) return false;
            if (ZNet.instance.IsDedicated()) return ZoneSystem.instance.m_locationInstances.Count > 0;
            return Player.m_localPlayer != null;
        }

        private static void Load()
        {
            if (!_hooked)
            {
                ZNet.WorldSaveStarted += Save;
                _hooked = true;
            }

            World world = ZNet.instance.GetWorld();
            Directory.CreateDirectory(DataDir);
            _savePath = Path.Combine(DataDir, Sanitize(world.m_name) + "_" + world.m_uid + ".json");

            WorldState state = null;
            if (File.Exists(_savePath))
            {
                try
                {
                    state = JsonConvert.DeserializeObject<WorldState>(File.ReadAllText(_savePath));
                    Plugin.Log.LogInfo("Loaded faction state: " + state.Factions.Count + " factions, " + state.Settlements.Count + " settlements, day " + state.Day.ToString("0.0"));
                    state = MatchWorld(state);
                }
                catch (Exception e)
                {
                    // Never overwrite a save we could not read.
                    string bad = _savePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(_savePath, bad, true);
                    Plugin.Log.LogError("Could not read " + _savePath + " (kept as " + bad + "): " + e);
                }
            }
            if (state == null)
            {
                state = new WorldState { RngState = (ulong)world.m_seed * 2654435761UL + (ulong)world.m_uid + 77UL };
            }

            Texts.Lang = ResolveLanguage();
            CustomItems.Rename();
            Install(state);
            if (!state.Seeded)
            {
                // The factions arrive "now", whatever day the world is already on.
                state.Steps = (long)Math.Floor(WorldDay * Defs.StepsPerDay);
                state.Day = state.Steps / (double)Defs.StepsPerDay;
                Sim.Seed();
                Plugin.Log.LogInfo("Seeded " + state.Factions.Count + " factions on day " + state.Day.ToString("0.0"));
                foreach (Settlement s in state.Settlements)
                {
                    Plugin.Log.LogInfo("  " + Sim.State.GetFaction(s.FactionId).Name + ": " + s.Name + " at " + s.Pos + " (" + s.Biome + ")");
                }
                Save();
            }
        }

        private static void Install(WorldState state)
        {
            if (Sim != null) Sim.EventRaised -= OnEvent;
            Sim = new Simulation(state, new GameWorld(), ModConfig.ToBalance());
            Sim.EventRaised += OnEvent;
            Sim.RaidInterceptor = Net.StageRaid;
            Sim.PlayerBuyMult = Wares.BuyMultiplier;
        }

        // ------------------------------------------------------------------ world backups

        private const int SnapshotsKept = 60;
        private static string _restoredNote;

        private static string HistoryDir
        {
            get { return Path.Combine(Path.Combine(DataDir, "history"), Path.GetFileNameWithoutExtension(_savePath)); }
        }

        /// <summary>
        /// The faction state is kept beside the world, not inside it, so restoring a backup of the world
        /// leaves it in the future: days ahead, with deaths and deeds the world no longer has. Every save
        /// therefore also keeps a copy stamped with the world's clock, and a world that loads with a clock
        /// behind the state gets the latest copy that is not ahead of it.
        /// </summary>
        private static WorldState MatchWorld(WorldState state)
        {
            double now = ZNet.instance.GetTimeSeconds();
            if (state.SavedAtTime <= now + 60.0) return state;
            string best = null;
            long bestTime = -1;
            if (Directory.Exists(HistoryDir))
            {
                foreach (string file in Directory.GetFiles(HistoryDir, "*.json.gz"))
                {
                    long time;
                    string stamp = Path.GetFileName(file).Split('.')[0];
                    if (!long.TryParse(stamp, out time) || time > now + 60.0 || time <= bestTime) continue;
                    bestTime = time;
                    best = file;
                }
            }
            if (best == null)
            {
                Plugin.Log.LogWarning("The world's clock (" + now.ToString("0") + " s) is behind the faction state (" + state.SavedAtTime.ToString("0") + " s), as after restoring a backup, but no earlier copy of the state is kept; going on with this one.");
                return state;
            }
            try
            {
                using (FileStream fs = File.OpenRead(best))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gz))
                {
                    WorldState older = JsonConvert.DeserializeObject<WorldState>(reader.ReadToEnd());
                    Plugin.Log.LogInfo("The world was restored from a backup (clock " + now.ToString("0") + " s, state " + state.SavedAtTime.ToString("0") + " s): faction state of " + bestTime + " s taken back, day " + older.Day.ToString("0.0"));
                    // The newer state is not thrown away: it stays beside the save under its own name.
                    File.Copy(_savePath, _savePath + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                    _restoredNote = older.Day.ToString("0");
                    return older;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not read the kept copy " + best + ": " + e.Message);
                return state;
            }
        }

        /// <summary>The kept copies of the state with the day each was on, oldest first.</summary>
        private static List<KeyValuePair<string, double>> KeptDays()
        {
            List<KeyValuePair<string, double>> days = new List<KeyValuePair<string, double>>();
            if (!Directory.Exists(HistoryDir)) return days;
            string[] files = Directory.GetFiles(HistoryDir, "*.json.gz");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                try
                {
                    // Only the day is wanted: read up to it, not the whole state.
                    using (FileStream fs = File.OpenRead(file))
                    using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (JsonTextReader reader = new JsonTextReader(new StreamReader(gz)))
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1 || (string)reader.Value != "Day") continue;
                            days.Add(new KeyValuePair<string, double>(file, reader.ReadAsDouble() ?? 0.0));
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not read the kept copy " + file + ": " + e.Message);
                }
            }
            return days;
        }

        /// <summary>
        /// Takes the factions back to a kept copy while the world stays as it is: the way back from an
        /// "lf advance" too far. Buildings the factions had not got to by then come down, settlements
        /// founded since go, and the clock is set so the copy's day is today.
        /// </summary>
        public static string Rewind(string[] a)
        {
            if (Sim == null) return "no faction simulation running";
            if (!ZNet.instance.IsServer()) return "only the host can take the factions back";
            // Copies stamped later than the world's clock belong to a timeline the world was taken back from.
            double now = ZNet.instance.GetTimeSeconds();
            List<KeyValuePair<string, double>> kept = KeptDays().FindAll(k =>
            {
                long time;
                return long.TryParse(Path.GetFileName(k.Key).Split('.')[0], out time) && time <= now + 60.0;
            });
            if (a.Length < 2)
            {
                if (kept.Count == 0) return "no copies of the faction state are kept yet; one is made at every world save";
                List<string> list = new List<string>();
                foreach (KeyValuePair<string, double> k in kept) list.Add(k.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                return "now day " + Sim.State.Day.ToString("0") + "; kept copies by day: " + string.Join(", ", list.ToArray()) + ". lf rewind <day> takes the factions back to the latest copy not after that day, lf rewind -<days> counts back from today";
            }
            double day;
            if (!double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out day)) return "lf rewind <day>, or lf rewind -<days> to go back that many days";
            // "-30": thirty days back from today.
            if (a[1].StartsWith("-")) day += Sim.State.Day;
            string file = null;
            double found = -1.0;
            foreach (KeyValuePair<string, double> k in kept)
            {
                if (k.Value > day + 0.05 || k.Value < found) continue;
                found = k.Value;
                file = k.Key;
            }
            if (file == null) return kept.Count == 0 ? "no copies of the faction state are kept yet" : "no kept copy on or before day " + day.ToString("0") + "; the earliest is day " + kept[0].Value.ToString("0");
            if (Math.Abs(found - Sim.State.Day) < 0.05) return "the factions are on day " + found.ToString("0") + " already";

            WorldState older;
            try
            {
                using (FileStream fs = File.OpenRead(file))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gz))
                {
                    older = JsonConvert.DeserializeObject<WorldState>(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                return "could not read " + file + ": " + e.Message;
            }

            // The present is kept too, so this can be undone with another rewind.
            Save();
            File.Copy(_savePath, _savePath + ".before-rewind-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            // Stamped a second earlier: the save below, in the same second, would write over it.
            KeepCopy(JsonConvert.SerializeObject(Sim.State, Formatting.Indented), ZNet.instance.GetTimeSeconds() - 1.0);
            double from = Sim.State.Day;
            int swept = 0;
            foreach (Settlement s in Sim.State.Settlements)
            {
                Settlement then = older.GetSettlement(s.Id);
                if (then != null && Math.Abs(then.Pos.X - s.Pos.X) < 1f && Math.Abs(then.Pos.Z - s.Pos.Z) < 1f) continue;
                swept += Materializer.SweepAway(s);
            }
            // Today is the copy's day: the world's clock has moved on, the factions have not.
            older.ClockOffset = older.Day - WorldDay;
            Install(older);
            Pending.Clear();
            Net.Reset();
            Forestry.Reset();
            Chests.Reset();
            Materializer.ForgetAudit();
            Save();
            Plugin.Log.LogInfo("Factions rewound from day " + from.ToString("0.0") + " to day " + older.Day.ToString("0.0") + " (" + Path.GetFileName(file) + "), " + swept + " objects of settlements not founded then removed");
            if (MessageHud.instance != null && Player.m_localPlayer != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_state_rewound", older.Day.ToString("0")));
            return "factions taken back from day " + from.ToString("0") + " to day " + older.Day.ToString("0") + "; buildings near you are set right within half a minute" + (swept > 0 ? "; " + swept + " objects of settlements founded since removed" : "");
        }

        private static void KeepCopy(string json, double time)
        {
            try
            {
                Directory.CreateDirectory(HistoryDir);
                string file = Path.Combine(HistoryDir, ((long)time).ToString("D12") + ".json.gz");
                using (FileStream fs = File.Create(file))
                using (GZipStream gz = new GZipStream(fs, CompressionMode.Compress))
                using (StreamWriter writer = new StreamWriter(gz))
                {
                    writer.Write(json);
                }
                string[] all = Directory.GetFiles(HistoryDir, "*.json.gz");
                Array.Sort(all, StringComparer.Ordinal);
                for (int i = 0; i < all.Length - SnapshotsKept; i++) File.Delete(all[i]);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not keep a copy of the faction state: " + e.Message);
            }
        }

        private static int ResolveLanguage()
        {
            return Texts.Resolve(ModConfig.Language.Value, Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null);
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public static void Save()
        {
            if (Sim == null || _savePath == null) return;
            try
            {
                string tmp = _savePath + ".tmp";
                Sim.State.SavedAtTime = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Sim.State.SavedAtTime;
                string json = JsonConvert.SerializeObject(Sim.State, Formatting.Indented);
                File.WriteAllText(tmp, json);
                KeepCopy(json, Sim.State.SavedAtTime);
                if (File.Exists(_savePath)) File.Copy(_savePath, _savePath + ".bak", true);
                File.Copy(tmp, _savePath, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Saving faction state failed: " + e);
            }
        }

        private static void Unload()
        {
            Save();
            Sim.EventRaised -= OnEvent;
            Sim = null;
            Pending.Clear();
            Net.Reset();
            Rewards.Reset();
            Forestry.Reset();
            Materializer.Reset();
            Discovery.Reset();
            NightWatch.Reset();
            Chests.Reset();
            FactionMember.ResetClaims();
            Duel.Reset();
            Plugin.Log.LogInfo("Faction simulation unloaded");
        }

        /// <summary>Runs the simulation ahead by a number of days (testing and impatient players).</summary>
        public static void FastForward(double days)
        {
            if (Sim == null) return;
            // A copy from before the jump, so "lf rewind" can undo it. A second earlier, so the
            // next save does not write over it.
            if (_savePath != null && ZNet.instance != null)
                KeepCopy(JsonConvert.SerializeObject(Sim.State, Formatting.Indented), ZNet.instance.GetTimeSeconds() - 1.0);
            Sim.State.ClockOffset += days;
            Sim.AdvanceTo(WorldDay + Sim.State.ClockOffset, 1000000);
        }

        private static void OnEvent(SimEvent e)
        {
            Pending.Enqueue(e);
            Announce(e);
        }

        /// <summary>Writes an event to the log and shows it to the player if it is worth a message.</summary>
        public static void Announce(SimEvent e)
        {
            string text = Texts.Describe(e, Sim.State);
            bool minor = e.Kind == EventKind.BuildingStarted || e.Kind == EventKind.BuildingCompleted || e.Kind == EventKind.TradeCaravan;
            if (!minor) Plugin.Log.LogInfo("[chronicle d" + e.Day.ToString("0.0") + "] " + text);
            if (!ModConfig.ShowChronicleMessages.Value || MessageHud.instance == null || Player.m_localPlayer == null) return;

            Settlement s = Sim.State.GetSettlement(e.SettlementId);
            bool near = s != null && Utils.DistanceXZ(Player.m_localPlayer.transform.position, new Vector3(s.Pos.X, 0f, s.Pos.Z)) < 150f;
            switch (e.Kind)
            {
                case EventKind.WarDeclared:
                case EventKind.PeaceMade:
                case EventKind.AllianceFormed:
                case EventKind.AllianceBroken:
                case EventKind.SettlementCaptured:
                case EventKind.SettlementDestroyed:
                case EventKind.PlayerWar:
                case EventKind.PlayerPeace:
                case EventKind.PlayerAlliance:
                case EventKind.PlayerAllianceBroken:
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
                    break;
                case EventKind.SettlementFounded:
                case EventKind.TierUp:
                case EventKind.RaidWon:
                case EventKind.RaidRepelled:
                case EventKind.PlayerKilledMember:
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
                    break;
                default:
                    if (near) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
                    break;
            }
        }
    }
}
