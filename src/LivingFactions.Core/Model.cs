using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    public enum Res { Wood = 0, Stone = 1, Food = 2, Metal = 3, Coins = 4 }

    public enum Biome { Meadows, BlackForest, Swamp, Mountain, Plains, Mistlands, Ashlands, DeepNorth, Ocean, Other }

    public enum Tier { Camp = 0, Hamlet = 1, Village = 2, Town = 3, City = 4 }

    public enum BuildingKind
    {
        Campfire, Hut, House, Longhouse, Storehouse, Woodcutter, Stonecutter, Farm, HunterLodge,
        Smithy, Watchtower, Palisade, Market, GreatHall
    }

    public enum BuildingState { Planned, UnderConstruction, Complete, Destroyed }

    public enum Treaty { Peace, War, Alliance }

    /// <summary>How a faction treats one player, derived from reputation and the war/alliance flags.</summary>
    public enum Standing { War, Hostile, Wary, Neutral, Friendly, Allied }

    public enum EventKind
    {
        FactionFounded, SettlementFounded, BuildingStarted, BuildingCompleted, TierUp, Starvation,
        GoodHarvest, Sickness, BeastAttack, Newcomers,
        WarDeclared, PeaceMade, AllianceFormed, AllianceBroken, RaidWon, RaidRepelled, BuildingDestroyed,
        SettlementCaptured, SettlementDestroyed, TradeCaravan, Incident,
        PlayerWar, PlayerPeace, PlayerAlliance, PlayerAllianceBroken, PlayerGift, PlayerKilledMember,
        // New kinds go at the end: saves keep the numbers.
        ChiefFell, NewChief, PlayerCrimeFound,
        ExpeditionOut, ExpeditionBack, SquadLost, RaidMarch,
        CampaignBegun, CampaignScouted, CampaignCamp, CampaignAssault, CampaignWon, CampaignOver, BuildingWrecked,
        CampaignCounter, AllyAid, Migration, OutpostFounded
    }

    public struct Vec2
    {
        public float X;
        public float Z;

        public Vec2(float x, float z) { X = x; Z = z; }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Z + b.Z); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Z - b.Z); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Z * s); }

        public float Length() { return (float)Math.Sqrt(X * X + Z * Z); }

        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length(); }

        public static Vec2 FromAngle(float radians, float radius)
        {
            return new Vec2((float)Math.Cos(radians) * radius, (float)Math.Sin(radians) * radius);
        }

        public override string ToString() { return "(" + X.ToString("0") + ", " + Z.ToString("0") + ")"; }
    }

    public class Building
    {
        public int Id;
        public BuildingKind Kind;
        public BuildingState State;
        /// <summary>Index into the settlement's plot ring layout, -1 for the centre.</summary>
        public int Plot = -1;
        /// <summary>Builder-hours already invested.</summary>
        public float Work;
        /// <summary>Game-side bookkeeping: how many blueprint pieces already exist in the world.</summary>
        public int SpawnedPieces;
        /// <summary>Game-side bookkeeping: world height the plot was levelled to (NaN = not yet).</summary>
        public float GroundY = float.NaN;
        /// <summary>Game-side bookkeeping: which revision of ground preparation this building has had.</summary>
        public int TerrainRev;
        /// <summary>Game-side bookkeeping: which revision of the blueprint the placed pieces follow.</summary>
        public int LayoutRev;
        /// <summary>Game-side bookkeeping for a settlement wall: its kind and where its gates are, as placed.</summary>
        public string Wall = "";
        /// <summary>Game-side bookkeeping: the day its pad was last levelled again for being uneven (a cliff edge stays uneven; it is not tried every session).</summary>
        public double RelevelDay = -100;
        /// <summary>2 once a ramp has been cut along its path over any steep bank (Materializer.EaseBanks; 1 = only smoothed, by 0.6).</summary>
        public int Graded;
    }

    public class Settlement
    {
        public int Id;
        public int FactionId;
        public string Name;
        public Vec2 Pos;
        public Biome Biome;
        public Tier Tier;
        public float Population;
        public float Soldiers;
        public float[] Stock = new float[5];
        public List<Building> Buildings = new List<Building>();
        /// <summary>Game-side bookkeeping: the inhabitants that exist as characters ("user:id:role:faction").</summary>
        public List<string> NpcRefs = new List<string>();
        public int NextBuildingId = 1;
        public int Seed;
        public double FoundedDay;
        public double LastRaidDay = -100;
        /// <summary>Until this day the settlement has no chief (one was killed): it works and builds less.</summary>
        public double LeaderlessUntil = -1;
        public double LastAttackedDay = -100;
        /// <summary>Lasting quality of the land and its people, rolled at founding (about 0.8 to 1.25).</summary>
        public float Fertility = 1f;
        public float Industry = 1f;
        public double LastExpansionDay = -100;
        public double LastExpeditionDay = -100;
        /// <summary>A war camp set up by a campaign: it holds a garrison, does not grow and goes when the campaign ends.</summary>
        public int CampaignId;
        /// <summary>1 for settlements founded from 0.7.0 on: their plots are shifted a little off the rings (Defs.PlotTransform).</summary>
        public int Layout;
        /// <summary>For an outpost, the settlement it works for (Outposts.cs); 0 for every other place.</summary>
        public int ParentId;
        /// <summary>What an outpost is out there for.</summary>
        public Res OutpostGoal;
        /// <summary>An outpost that watches a road rather than working the land.</summary>
        public bool RoadPost;
        /// <summary>Days of food shortage in a row; drives starvation and abandonment.</summary>
        public float HungerDays;
        public bool Destroyed;
    }

    public class Relation
    {
        public float Value;
        /// <summary>Resting value the relation drifts back to (culture affinity plus personality).</summary>
        public float Baseline;
        public Treaty Treaty = Treaty.Peace;
        public double SinceDay;
        /// <summary>Accumulated losses in the current war; pushes both sides toward peace.</summary>
        public float Weariness;
    }

    public class PlayerRelation
    {
        public float Reputation;
        public bool AtWar;
        public bool Allied;
        /// <summary>Recent gift value; gifts give diminishing returns until it decays.</summary>
        public float GiftFatigue;
        public double LastHostileDay = -100;
        public string Name = "";
        /// <summary>Blood between them: grows with each killing, fades slowly, and while it lasts standing comes back slower.</summary>
        public float Grievance;
        /// <summary>Standing the faction would lose for killings nobody saw, once it finds out who did them.</summary>
        public float Suspicion;
        /// <summary>Where the last unseen killing was, for the news when it comes out.</summary>
        public int SuspicionSettlementId;
        /// <summary>The last time the player was caught taking from the faction's chests.</summary>
        public double LastStoleDay = -100;
        /// <summary>When this player last struck a building of the faction: who to name when one comes down.</summary>
        public double LastDamagedDay = -100;
    }

    public class Faction
    {
        public int Id;
        public string Name;
        public string CultureId;
        /// <summary>0..1 personality traits rolled at creation around the culture's defaults.</summary>
        public float Aggression;
        public float Expansionism;
        public float Mercantile;
        public float Honor;
        public int CapitalId;
        /// <summary>When the faction last looked for a place for an outpost.</summary>
        public double LastOutpostDay = -100;
        public int ColorIndex;
        public bool Eliminated;
        /// <summary>Key is the other faction's id.</summary>
        public Dictionary<int, Relation> Relations = new Dictionary<int, Relation>();
        /// <summary>Key is the player's persistent id.</summary>
        public Dictionary<long, PlayerRelation> Players = new Dictionary<long, PlayerRelation>();
    }

    public class SimEvent
    {
        public EventKind Kind;
        public double Day;
        public int FactionId;
        public int OtherFactionId;
        public int SettlementId;
        public int OtherSettlementId;
        public int BuildingId;
        public long PlayerId;
        public string[] Args;
    }

    public class WorldState
    {
        /// <summary>The save format this build writes and understands. A state of a newer format is not read.</summary>
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        /// <summary>
        /// Timeline generation. The host raises it when it takes the factions back (a rewind), so that guests
        /// can throw away a world built from a future that no longer exists. Older saves have no field and start at 0.
        /// </summary>
        public int Epoch;
        public bool Seeded;
        /// <summary>Hour-steps run so far. The clock is derived from this so it never drifts.</summary>
        public long Steps;
        /// <summary>Simulated time in days since the world began.</summary>
        public double Day;
        /// <summary>Days the simulation runs ahead of the game clock (set when the sim starts in an old world, or by fast-forwarding).</summary>
        public double ClockOffset;
        /// <summary>The world's own clock (seconds) when this state was saved: tells a restored world backup from the present.</summary>
        public double SavedAtTime;
        public ulong RngState = 0x9E3779B97F4A7C15UL;
        /// <summary>Direction the first human clan settled in; the others are spread around from it.</summary>
        public float SeedAngle;
        /// <summary>Settlements each player has found (key = player id).</summary>
        public Dictionary<long, List<int>> Discovered = new Dictionary<long, List<int>>();
        public int NextFactionId = 1;
        public int NextSettlementId = 1;
        public List<Faction> Factions = new List<Faction>();
        public List<Settlement> Settlements = new List<Settlement>();
        /// <summary>Rolling chronicle, newest last.</summary>
        public List<SimEvent> Chronicle = new List<SimEvent>();
        public List<Squad> Squads = new List<Squad>();
        public int NextSquadId = 1;
        public List<Campaign> Campaigns = new List<Campaign>();
        public int NextCampaignId = 1;

        public Faction GetFaction(int id)
        {
            for (int i = 0; i < Factions.Count; i++)
            {
                if (Factions[i].Id == id) return Factions[i];
            }
            return null;
        }

        public Settlement GetSettlement(int id)
        {
            for (int i = 0; i < Settlements.Count; i++)
            {
                if (Settlements[i].Id == id) return Settlements[i];
            }
            return null;
        }
    }

    /// <summary>What the simulation needs to know about the game world. Must work for unloaded areas.</summary>
    public interface IWorldQuery
    {
        /// <summary>Radius of the playable world in metres.</summary>
        float WorldRadius { get; }

        /// <summary>Probe a candidate site. Returns false when it is water, too steep or mixed-biome.</summary>
        bool ProbeSite(Vec2 pos, float radius, out Biome biome);

        /// <summary>True when something else already owns the spot (vanilla location, player base, spawn).</summary>
        bool IsReserved(Vec2 pos, float radius);

        /// <summary>
        /// True when the land from one point to the other can be walked: it does not climb much more than
        /// the distance allows, and nowhere along it is a bank too steep to get up. Lenient allows more.
        /// </summary>
        bool GentleWay(Vec2 from, Vec2 to, bool lenient);
    }
}
