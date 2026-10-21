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
        public static ConfigEntry<float> ReputationDecay;
        public static ConfigEntry<int> HiredLimit;
        public static ConfigEntry<float> HireMinutes;
        public static ConfigEntry<bool> Roads, VisibleWork, NightSleep, NightWatchOn, ChestsOn, StagedRaids, TradeOn, HudWidget, Relics, Archers;
        public static ConfigEntry<bool> Caravans, Mercenaries, TowerGuards, Errands, RoadLamps;
        public static ConfigEntry<float> RoadReach;
        public static ConfigEntry<bool> MapZones, MapShowAll, MapNpcs, TreeFelling;
        public static ConfigEntry<bool> LootSweep;
        public static ConfigEntry<int> LootSweepMinutes;
        public static ConfigEntry<string> Language;
        public static ConfigEntry<bool> ShowChronicleMessages;
        public static ConfigEntry<KeyboardShortcut> ChronicleKey;
        public static ConfigEntry<bool> DevTools;
        public static ConfigEntry<int> AngerHitsAllied, AngerHitsFriendly, AngerHitsNeutral, AngerHitsWary;
        public static ConfigEntry<float> ReputationPerHit, GrudgeMinutes, PursuitRange, HelpRange, WitnessRange;
        public static ConfigEntry<bool> FriendsDefend;

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
            ReputationDecay = cfg.Bind("World", "ReputationDecay", 1f, new ConfigDescription("How fast standing with a faction drifts back toward its usual temper, the good and the bad alike: 1 as made, 0.5 half as fast, 0 never. Alliances and friendships last longer the lower it is. In a game for several the host's setting counts.", new AcceptableValueRange<float>(0f, 2f)));
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
            HiredLimit = cfg.Bind("World", "HiredLimit", 6, new ConfigDescription("Most hired fighters at your back at once when the faction you hire from is allied with you; friends let you have two thirds of it, anyone else two.", new AcceptableValueRange<int>(2, 12)));
            HireMinutes = cfg.Bind("World", "HireMinutes", 15f, new ConfigDescription("Minutes hired fighters stay with a friend of their faction; an ally keeps them half as long again and more, anyone else two thirds of it.", new AcceptableValueRange<float>(5f, 60f)));
            TowerGuards = cfg.Bind("Features", "TowerGuards", true, "One guard keeps watch from the deck of the watchtower.");
            Errands = cfg.Bind("Features", "Errands", true, "Between shifts villagers visit other buildings or walk out along the road and back.");
            RoadLamps = cfg.Bind("Features", "RoadLamps", true, "Lamp posts stand along the roads between settlements, one every 30 m.");
            TreeFelling = cfg.Bind("Features", "TreeFelling", true, "Woodcutters fell trees at least 10 m from the buildings and plant a sapling in each stump's place.");
            LootSweep = cfg.Bind("Features", "LootSweep", true, "Items left lying on the ground inside settlements, camps and outposts are cleared away now and then (not those dropped in the last minute, near a player or in a player's own base).");
            LootSweepMinutes = cfg.Bind("Features", "LootSweepMinutes", 10, new ConfigDescription("How often loose items are cleared from settlement ground, in minutes.", new AcceptableValueRange<int>(1, 120)));
            MapZones = cfg.Bind("Map", "FactionZones", true, "The land of each settlement is drawn on the map in its faction's colour, with a legend of the factions on the large map.");
            MapShowAll = cfg.Bind("Map", "ShowAllSettlements", false, "Every settlement is on the map from the start, not only those you have found.");
            MapNpcs = cfg.Bind("Map", "VillagersOnMinimap", true, "The people of the factions near you are small dots on the minimap, in their faction's colour (red when hostile to you).");
            RoadReach = cfg.Bind("World", "RoadReach", 1500f, new ConfigDescription("Settlements closer than this to a neighbour they are not at war with lay a road to it.", new AcceptableValueRange<float>(0f, 5000f)));
            Language = cfg.Bind("General", "Language", "auto", "Mod text language: auto (follow the game when the mod speaks its language, otherwise English) or one of en, ru, pl, de, fr, es, pt.");
            ShowChronicleMessages = cfg.Bind("General", "ShowChronicleMessages", true, "Show notable faction events (wars, new settlements, raids) as on-screen messages.");
            ChronicleKey = cfg.Bind("General", "ChronicleKey", new KeyboardShortcut(UnityEngine.KeyCode.H, UnityEngine.KeyCode.LeftAlt), "Key that hides the chronicle under the minimap, or shows it again (the same as Features/HudWidget and the switch in the mod menu).");
            AngerHitsAllied = cfg.Bind("Conflict", "HitsToAngerAllied", 5, new ConfigDescription("Blows an allied faction's people take from you before they fight back. A stray blow is forgiven.", new AcceptableValueRange<int>(1, 50)));
            AngerHitsFriendly = cfg.Bind("Conflict", "HitsToAngerFriendly", 3, new ConfigDescription("The same for a friendly faction.", new AcceptableValueRange<int>(1, 50)));
            AngerHitsNeutral = cfg.Bind("Conflict", "HitsToAngerNeutral", 2, new ConfigDescription("The same for a neutral faction.", new AcceptableValueRange<int>(1, 50)));
            AngerHitsWary = cfg.Bind("Conflict", "HitsToAngerWary", 1, new ConfigDescription("The same for a faction that is wary of you. Hostile factions and those at war fight you anyway.", new AcceptableValueRange<int>(1, 50)));
            ReputationPerHit = cfg.Bind("Conflict", "ReputationPerHit", 1.5f, new ConfigDescription("Standing lost with a faction for each blow on one of its people.", new AcceptableValueRange<float>(0f, 20f)));
            GrudgeMinutes = cfg.Bind("Conflict", "GrudgeMinutes", 5f, new ConfigDescription("How long people stay angry after the last blow (twice as long after a killing, four times after their chief's).", new AcceptableValueRange<float>(0.5f, 60f)));
            PursuitRange = cfg.Bind("Conflict", "PursuitRange", 60f, new ConfigDescription("How far from the place of the fight angry people follow you before going back to their work.", new AcceptableValueRange<float>(10f, 300f)));
            HelpRange = cfg.Bind("Conflict", "HelpRange", 35f, new ConfigDescription("How close people of an allied faction must be to join a fight.", new AcceptableValueRange<float>(5f, 200f)));
            WitnessRange = cfg.Bind("Conflict", "WitnessRange", 35f, new ConfigDescription("A killing seen by someone of the faction within this distance is known at once; unseen, it may come out days later.", new AcceptableValueRange<float>(5f, 200f)));
            FriendsDefend = cfg.Bind("Conflict", "FriendsDefend", true, "Fighters of friendly factions, not only allied ones, stand with you when something attacks you near them.");
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
                MaxSettlementsTotal = MaxSettlementsTotal.Value,
                ReputationDecay = ReputationDecay.Value
            };
        }
    }
}
