using System;
using System.Collections.Generic;
using System.IO;
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
        }

        /// <summary>A guest's copy of the host's state: read like the real thing, never run forward.</summary>
        public static void SetMirror(WorldState state)
        {
            if (Sim == null) Texts.Lang = ResolveLanguage();
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
            Sim = new Simulation(state, new GameWorld(), ModConfig.ToBalance());
            Sim.EventRaised += OnEvent;
            Sim.RaidInterceptor = Net.StageRaid;
            Sim.PlayerBuyMult = Wares.BuyMultiplier;
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
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Sim.State, Formatting.Indented));
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
            Plugin.Log.LogInfo("Faction simulation unloaded");
        }

        /// <summary>Runs the simulation ahead by a number of days (testing and impatient players).</summary>
        public static void FastForward(double days)
        {
            if (Sim == null) return;
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
