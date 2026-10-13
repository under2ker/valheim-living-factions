using BepInEx.Configuration;
using LivingFactions.Core;

namespace LivingFactions
{
    internal static class ModConfig
    {
        public static ConfigEntry<float> Pace;
        public static ConfigEntry<bool> RaceFactions;
        public static ConfigEntry<bool> Wars;
        public static ConfigEntry<int> MaxSettlementsPerFaction;
        public static ConfigEntry<int> MaxSettlementsTotal;
        public static ConfigEntry<int> MaxVisibleNpcs;
        public static ConfigEntry<int> PiecesPerFrame;
        public static ConfigEntry<int> WorkersPerTree;
        public static ConfigEntry<float> NightAttackChance;
        public static ConfigEntry<bool> Roads, VisibleWork, NightSleep, NightWatchOn, ChestsOn, StagedRaids, TradeOn, HudWidget, Relics, Archers;
        public static ConfigEntry<bool> Caravans, Mercenaries, TowerGuards, Errands, RoadLamps;
        public static ConfigEntry<float> RoadReach;
        public static ConfigEntry<string> Language;
        public static ConfigEntry<bool> ShowChronicleMessages;
        public static ConfigEntry<bool> DevTools;

        public static void Bind(ConfigFile cfg)
        {
            Pace = cfg.Bind("Simulation", "Pace", 1f, new ConfigDescription("How fast factions develop. 1 = a camp becomes a village in roughly 15-20 in-game days.", new AcceptableValueRange<float>(0.1f, 10f)));
            RaceFactions = cfg.Bind("Simulation", "RaceFactions", true, "Fulings, Dvergr, Greydwarfs and Draugr also form factions with their own settlements.");
            Wars = cfg.Bind("Simulation", "Wars", true, "Factions can go to war, raid and capture each other's settlements.");
            MaxSettlementsPerFaction = cfg.Bind("Simulation", "MaxSettlementsPerFaction", 6, new ConfigDescription("Upper limit of settlements one faction may hold by expansion.", new AcceptableValueRange<int>(1, 20)));
            MaxSettlementsTotal = cfg.Bind("Simulation", "MaxSettlementsTotal", 40, new ConfigDescription("Upper limit of settlements in the whole world.", new AcceptableValueRange<int>(1, 200)));
            MaxVisibleNpcs = cfg.Bind("World", "MaxVisibleNpcs", 10, new ConfigDescription("How many inhabitants of one settlement are present as actual characters.", new AcceptableValueRange<int>(1, 30)));
            PiecesPerFrame = cfg.Bind("World", "PiecesPerFrame", 12, new ConfigDescription("Building pieces placed per frame while a settlement is brought up to date.", new AcceptableValueRange<int>(1, 100)));
            WorkersPerTree = cfg.Bind("World", "WorkersPerTree", 2, new ConfigDescription("How many woodcutters may work on the same tree.", new AcceptableValueRange<int>(1, 2)));
            NightAttackChance = cfg.Bind("World", "NightAttackChance", 0.57f, new ConfigDescription("Chance that the night watch attacks a neutral or wary stranger who ignores its warning and stays close to the settlement after dark. By day it is 0.", new AcceptableValueRange<float>(0f, 1f)));
            Roads = cfg.Bind("Features", "Roads", true, "Settlements lay footpaths to their buildings (paved in towns) and trails to nearby friendly settlements.");
            VisibleWork = cfg.Bind("Features", "VisibleWork", true, "Villagers walk to work spots, swing tools and haul goods to the storehouse.");
            NightSleep = cfg.Bind("Features", "NightSleep", true, "Villagers go indoors at night.");
            NightWatchOn = cfg.Bind("Features", "NightWatch", true, "Guards challenge strangers after dark and may attack (see World/NightAttackChance).");
            ChestsOn = cfg.Bind("Features", "Chests", true, "Storehouse chests hold part of the settlement's stock as real items; homes hold personal loot.");
            StagedRaids = cfg.Bind("Features", "StagedRaids", true, "Raiders physically arrive when a settlement you are visiting is raided.");
            TradeOn = cfg.Bind("Features", "Trade", true, "Settlements trade with players.");
            HudWidget = cfg.Bind("Features", "HudWidget", true, "A small on-screen chronicle of what the factions are doing.");
            Relics = cfg.Bind("Features", "Relics", true, "Factions sell culture-specific relics and unidentified goods.");
            Archers = cfg.Bind("Features", "Archers", true, "Some guards and raiders are archers who keep their distance.");
            Caravans = cfg.Bind("Features", "Caravans", true, "Traders with an escort leave settlements along their roads and arrive by them while you are near.");
            Mercenaries = cfg.Bind("Features", "Mercenaries", true, "Friendly settlements hire out two fighters who follow you for a while.");
            TowerGuards = cfg.Bind("Features", "TowerGuards", true, "One guard keeps watch from the deck of the watchtower.");
            Errands = cfg.Bind("Features", "Errands", true, "Between shifts villagers visit other buildings or walk out along the road and back.");
            RoadLamps = cfg.Bind("Features", "RoadLamps", true, "Lamp posts stand along the roads between settlements, one every 30 m.");
            RoadReach = cfg.Bind("World", "RoadReach", 1500f, new ConfigDescription("Settlements closer than this to a neighbour they are not at war with lay a road to it.", new AcceptableValueRange<float>(0f, 5000f)));
            Language = cfg.Bind("General", "Language", "auto", "Mod text language: auto (follow the game), en or ru.");
            ShowChronicleMessages = cfg.Bind("General", "ShowChronicleMessages", true, "Show notable faction events (wars, new settlements, raids) as on-screen messages.");
            DevTools = cfg.Bind("Dev", "DevTools", false, "Enable the developer command file bridge and autostart (for testing the mod).");
        }

        public static Balance ToBalance()
        {
            return new Balance
            {
                Pace = Pace.Value,
                EnableRaceFactions = RaceFactions.Value,
                EnableWars = Wars.Value,
                MaxSettlementsPerFaction = MaxSettlementsPerFaction.Value,
                MaxSettlementsTotal = MaxSettlementsTotal.Value
            };
        }
    }
}
