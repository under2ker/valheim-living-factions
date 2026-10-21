using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    public class BuildingDef
    {
        public BuildingKind Kind;
        public float Wood, Stone, Metal;
        /// <summary>Builder-hours to finish.</summary>
        public float Work;
        public int Housing;
        public int Storage;
        /// <summary>Resource this building boosts, or null.</summary>
        public Res? Boosts;
        public float Boost;
        public float Defense;
        public Tier MinTier;
        /// <summary>Maximum count at each tier (index = tier). -1 = limited only by need.</summary>
        public int[] MaxByTier;
        /// <summary>False for structures that do not occupy a plot (centre fire, ring wall).</summary>
        public bool UsesPlot = true;
    }

    public class CultureDef
    {
        public string Id;
        public string Group;
        public string NameEn, NameRu;
        public Biome[] HomeBiomes;
        /// <summary>Biomes daughter settlements may be founded in. Null = same as HomeBiomes.</summary>
        public Biome[] ExpandBiomes;
        /// <summary>Starting reputation with every player.</summary>
        public float Disposition;
        public float Aggression, Expansionism, Mercantile, Honor;
        /// <summary>Preferred distance band from the world centre for the first settlement.</summary>
        public float StartMinDist, StartMaxDist;
        public string[] SettlementPrefixes;
        public string[] SettlementSuffixes;
        public string[] PersonNames;
        /// <summary>Women's names, for cultures whose people come in both; null otherwise.</summary>
        public string[] FemaleNames;
    }

    /// <summary>Tunable numbers. The game plugin maps its config onto an instance of this.</summary>
    public class Balance
    {
        /// <summary>Global multiplier on how fast factions develop.</summary>
        public float Pace = 1f;
        /// <summary>Settlements send bands out for what they lack.</summary>
        public bool Expeditions = true;
        /// <summary>Wars are fought by campaigns: scouting, a war camp, an assault, holding what was taken.</summary>
        public bool Campaigns = true;
        /// <summary>Metres a band covers in a day (a person walks about 2.5 km in a 30-minute game day).</summary>
        public float SquadSpeed = 2400f;
        public float MusterDays = 0.15f;
        public float GatherDays = 1f;
        public float ExpeditionRange = 1400f;
        public float ExpeditionCooldownDays = 4f;
        public float CampaignMinSoldiers = 6f;
        public float CampaignRange = 2200f;
        public float CampDays = 1.5f;
        public int CampaignAttempts = 2;
        /// <summary>Allies send part of their soldiers to stand by a friend at war (to its camp, or to the place attacked).</summary>
        public bool AllyAid = true;
        public float AllyShare = 0.3f;
        public float AllyStayDays = 4f;
        /// <summary>Metal a soldier's arms take before a fight; without it a band fights worse armed.</summary>
        public float MetalPerSoldier = 0.5f;
        /// <summary>People move from a starving or threatened place to one of their own that can take them.</summary>
        public bool Migration = true;
        /// <summary>Factions put a few people out where the land gives what they lack, and watch posts on long roads.</summary>
        public bool Outposts = true;
        /// <summary>Chance per day that a faction finds out who killed one of its people unseen.</summary>
        public float CrimeFoundPerDay = 0.3f;
        /// <summary>How fast a player's standing drifts back toward a faction's usual temper (1 as made, 0 never).</summary>
        public float ReputationDecay = 1f;
        /// <summary>Days a settlement goes without a chief after one is killed.</summary>
        public float LeaderlessDays = 3f;
        /// <summary>Share of the usual work a settlement without a chief gets done.</summary>
        public float LeaderlessWork = 0.75f;
        public int MaxSettlementsPerFaction = 6;
        public int MaxSettlementsTotal = 40;
        public bool EnableRaceFactions = true;
        public bool EnableWars = true;

        public float FoodPerPopPerDay = 1f;
        public float WoodPerWorkerDay = 9f;
        public float StonePerWorkerDay = 5.5f;
        public float FoodPerWorkerDay = 4.2f;
        public float MetalPerWorkerDay = 1.2f;
        public float GrowthPerDay = 0.07f;
        public float MinGrowthPerDay = 0.45f;
        public float BaseStorage = 180f;
        public float MinSettlementSpacing = 240f;
        public float ForeignSpacing = 380f;
        public float ExpansionMinDist = 280f;
        public float ExpansionMaxDist = 750f;
        public float ExpansionCooldownDays = 7f;
        public float RaidRange = 1800f;
        public float RaidCooldownDays = 3f;
        public float BorderFrictionRange = 700f;
        public int ChronicleLimit = 200;

        public static readonly float[] CoinValue = { 1f, 1.5f, 2f, 8f, 1f };
    }

    public static class Defs
    {
        public const int StepsPerDay = 24;

        /// <summary>How many people a settlement of each tier can hold, however many houses it has.</summary>
        public static readonly float[] PopCapByTier = { 9f, 18f, 30f, 46f, 70f };

        public static readonly Dictionary<BuildingKind, BuildingDef> Buildings = new Dictionary<BuildingKind, BuildingDef>();
        public static readonly List<CultureDef> Cultures = new List<CultureDef>();

        // Ring layout: radius and plot count per ring.
        public static readonly float[] RingRadius = { 14f, 27f, 40f, 53f, 69f };
        public static readonly int[] RingPlots = { 6, 11, 16, 21, 26 };
        // How many rings a settlement of each tier may build on.
        public static readonly int[] RingsByTier = { 1, 2, 3, 4, 5 };
        /// <summary>The wall encloses the first four rings; a city's fifth ring grows outside it.</summary>
        public const float PalisadeRadius = 60.5f;
        /// <summary>A city's wall: outside its last ring of plots (69 m) and the buildings on it.</summary>
        public const float CityWallRadius = 78f;
        /// <summary>A home's Housing is two for each bed its blueprint puts in it: the beds are wide enough for two.</summary>
        public const int SleepersPerBed = 2;

        static Defs()
        {
            int[] any = { -1, -1, -1, -1, -1 };
            Add(new BuildingDef { Kind = BuildingKind.Campfire, Work = 0, Housing = 4, MinTier = Tier.Camp, MaxByTier = new[] { 1, 1, 1, 1, 1 }, UsesPlot = false });
            // A home houses two for each bed its blueprint has (SleepersPerBed): hut 1, house 2, longhouse 4, great hall 3.
            Add(new BuildingDef { Kind = BuildingKind.Hut, Wood = 20, Work = 14, Housing = 2, MinTier = Tier.Camp, MaxByTier = new[] { 3, 4, 4, 4, 4 } });
            Add(new BuildingDef { Kind = BuildingKind.House, Wood = 45, Stone = 10, Work = 34, Housing = 4, MinTier = Tier.Hamlet, MaxByTier = any });
            Add(new BuildingDef { Kind = BuildingKind.Longhouse, Wood = 110, Stone = 40, Work = 90, Housing = 8, MinTier = Tier.Village, MaxByTier = any });
            Add(new BuildingDef { Kind = BuildingKind.Storehouse, Wood = 45, Stone = 8, Work = 30, Storage = 320, MinTier = Tier.Camp, MaxByTier = new[] { 1, 1, 2, 3, 4 } });
            Add(new BuildingDef { Kind = BuildingKind.Woodcutter, Wood = 25, Work = 20, Boosts = Res.Wood, Boost = 0.35f, MinTier = Tier.Camp, MaxByTier = new[] { 1, 1, 2, 3, 3 } });
            Add(new BuildingDef { Kind = BuildingKind.Stonecutter, Wood = 30, Stone = 10, Work = 26, Boosts = Res.Stone, Boost = 0.35f, MinTier = Tier.Camp, MaxByTier = new[] { 1, 1, 2, 3, 3 } });
            Add(new BuildingDef { Kind = BuildingKind.Farm, Wood = 30, Work = 26, Boosts = Res.Food, Boost = 0.35f, MinTier = Tier.Camp, MaxByTier = new[] { 1, 2, 3, 4, 5 } });
            Add(new BuildingDef { Kind = BuildingKind.HunterLodge, Wood = 30, Work = 20, Boosts = Res.Food, Boost = 0.25f, MinTier = Tier.Camp, MaxByTier = new[] { 1, 1, 1, 2, 2 } });
            Add(new BuildingDef { Kind = BuildingKind.Smithy, Wood = 60, Stone = 60, Work = 70, Boosts = Res.Metal, Boost = 1f, MinTier = Tier.Village, MaxByTier = new[] { 0, 0, 1, 1, 2 } });
            Add(new BuildingDef { Kind = BuildingKind.Watchtower, Wood = 50, Stone = 20, Work = 44, Defense = 0.3f, MinTier = Tier.Hamlet, MaxByTier = new[] { 0, 1, 2, 3, 4 } });
            Add(new BuildingDef { Kind = BuildingKind.Market, Wood = 80, Stone = 30, Work = 60, MinTier = Tier.Village, MaxByTier = new[] { 0, 0, 1, 1, 1 } });
            Add(new BuildingDef { Kind = BuildingKind.Palisade, Wood = 260, Stone = 40, Work = 180, Defense = 0.8f, MinTier = Tier.Town, MaxByTier = new[] { 0, 0, 0, 1, 1 }, UsesPlot = false });
            Add(new BuildingDef { Kind = BuildingKind.GreatHall, Wood = 220, Stone = 120, Metal = 20, Work = 230, Housing = 6, MinTier = Tier.Town, MaxByTier = new[] { 0, 0, 0, 1, 1 } });

            string[] norsePre = { "Hrafn", "Ulf", "Bjorn", "Stein", "Eik", "Skog", "Vind", "Frost", "Gull", "Jarn", "Hval", "Thor", "Sig", "Ask", "Dal", "Fjall", "Brim", "Ketil", "Geir", "Orm" };
            string[] norseSuf = { "heim", "stad", "vik", "gard", "by", "holt", "nes", "dal", "borg", "fell", "lund", "thorp" };
            Biome[] humanLands = { Biome.Meadows, Biome.BlackForest, Biome.Plains, Biome.Mountain };
            string[] norseNames = { "Bjorn", "Ragnar", "Ivar", "Gunnar", "Harald", "Leif", "Torsten", "Hakon", "Ulf", "Eirik", "Knut", "Olaf", "Sigurd", "Arne", "Rolf", "Sten" };
            string[] norseFemale = { "Sigrid", "Astrid", "Freydis", "Ingrid", "Ylva", "Solveig", "Thyra", "Gudrun", "Helga", "Ragnhild", "Liv", "Runa" };

            Cultures.Add(new CultureDef
            {
                Id = "clan_raven", Group = "human", NameEn = "Raven Clan", NameRu = "Клан Ворона",
                HomeBiomes = new[] { Biome.Meadows, Biome.BlackForest }, ExpandBiomes = humanLands, Disposition = 5,
                Aggression = 0.4f, Expansionism = 0.6f, Mercantile = 0.5f, Honor = 0.7f,
                StartMinDist = 220, StartMaxDist = 900,
                SettlementPrefixes = norsePre, SettlementSuffixes = norseSuf, PersonNames = norseNames, FemaleNames = norseFemale
            });
            Cultures.Add(new CultureDef
            {
                Id = "clan_wolf", Group = "human", NameEn = "Wolf Clan", NameRu = "Клан Волка",
                HomeBiomes = new[] { Biome.BlackForest, Biome.Meadows, Biome.Mountain }, ExpandBiomes = humanLands, Disposition = -10,
                Aggression = 0.8f, Expansionism = 0.5f, Mercantile = 0.2f, Honor = 0.6f,
                StartMinDist = 500, StartMaxDist = 1800,
                SettlementPrefixes = norsePre, SettlementSuffixes = norseSuf, PersonNames = norseNames, FemaleNames = norseFemale
            });
            Cultures.Add(new CultureDef
            {
                Id = "clan_stag", Group = "human", NameEn = "Stag Clan", NameRu = "Клан Оленя",
                HomeBiomes = new[] { Biome.Meadows, Biome.Plains }, ExpandBiomes = humanLands, Disposition = 15,
                Aggression = 0.15f, Expansionism = 0.5f, Mercantile = 0.9f, Honor = 0.8f,
                StartMinDist = 400, StartMaxDist = 1500,
                SettlementPrefixes = norsePre, SettlementSuffixes = norseSuf, PersonNames = norseNames, FemaleNames = norseFemale
            });
            Cultures.Add(new CultureDef
            {
                Id = "fuling_horde", Group = "fuling", NameEn = "Fuling Horde", NameRu = "Орда фулингов",
                HomeBiomes = new[] { Biome.Plains }, Disposition = -60,
                Aggression = 0.85f, Expansionism = 0.6f, Mercantile = 0.2f, Honor = 0.3f,
                StartMinDist = 1500, StartMaxDist = 7000,
                SettlementPrefixes = new[] { "Grak", "Zug", "Mok", "Urg", "Naz", "Bol", "Dru", "Skar", "Ghaz", "Tor" },
                SettlementSuffixes = new[] { "gash", "dum", "rok", "nak", "ghul", "tar", "mog" },
                PersonNames = new[] { "Grok", "Zarg", "Muk", "Urzh", "Nagh", "Bolg", "Druz", "Skrag" }
            });
            Cultures.Add(new CultureDef
            {
                Id = "dvergr_hold", Group = "dvergr", NameEn = "Dvergr Hold", NameRu = "Чертог двергов",
                HomeBiomes = new[] { Biome.Mistlands, Biome.BlackForest, Biome.Mountain }, Disposition = 5,
                Aggression = 0.2f, Expansionism = 0.3f, Mercantile = 0.8f, Honor = 0.9f,
                StartMinDist = 900, StartMaxDist = 8000,
                SettlementPrefixes = new[] { "Dur", "Nid", "Brok", "Sind", "Dval", "Mot", "Reg", "Alv", "Thra", "Fund" },
                SettlementSuffixes = new[] { "heim", "hold", "smid", "berg", "grof", "hall" },
                PersonNames = new[] { "Durin", "Brokk", "Sindri", "Dvalin", "Nori", "Regin", "Alviss", "Thrain" }
            });
            Cultures.Add(new CultureDef
            {
                Id = "greydwarf_brood", Group = "greydwarf", NameEn = "Greydwarf Brood", NameRu = "Выводок грейдворфов",
                HomeBiomes = new[] { Biome.BlackForest }, Disposition = -45,
                Aggression = 0.6f, Expansionism = 0.85f, Mercantile = 0.05f, Honor = 0.1f,
                StartMinDist = 500, StartMaxDist = 3000,
                SettlementPrefixes = new[] { "Moss", "Root", "Thorn", "Bark", "Gloom", "Fern", "Mire", "Knot" },
                SettlementSuffixes = new[] { "hollow", "nest", "thicket", "den", "grove", "burrow" },
                PersonNames = new[] { "Hrrk", "Gnarl", "Mossk", "Thrum", "Krrn", "Brak" }
            });
            Cultures.Add(new CultureDef
            {
                Id = "draugr_court", Group = "draugr", NameEn = "Draugr Court", NameRu = "Двор драугров",
                HomeBiomes = new[] { Biome.Swamp }, Disposition = -70,
                Aggression = 0.75f, Expansionism = 0.3f, Mercantile = 0.05f, Honor = 0.4f,
                StartMinDist = 1200, StartMaxDist = 6000,
                SettlementPrefixes = new[] { "Hel", "Nar", "Dauth", "Grav", "Myrk", "Sot", "Rot", "Bein" },
                SettlementSuffixes = new[] { "haug", "grav", "myr", "tun", "hjem", "vard" },
                PersonNames = new[] { "Hrolf the Drowned", "Old Styr", "Agnar Bog-King", "Thorkel Rot", "Yrsa Pale" }
            });
        }

        private static void Add(BuildingDef def) { Buildings[def.Kind] = def; }

        public static CultureDef Culture(string id)
        {
            for (int i = 0; i < Cultures.Count; i++)
            {
                if (Cultures[i].Id == id) return Cultures[i];
            }
            return Cultures[0];
        }

        /// <summary>Resting relation between two cultures, before per-faction personality.</summary>
        public static float Affinity(CultureDef a, CultureDef b)
        {
            if (a.Group == b.Group) return a.Group == "human" ? 10f : 30f;
            string x = string.CompareOrdinal(a.Group, b.Group) < 0 ? a.Group : b.Group;
            string y = x == a.Group ? b.Group : a.Group;
            switch (x + "/" + y)
            {
                case "dvergr/human": return 15f;
                case "fuling/human": return -35f;
                case "greydwarf/human": return -40f;
                case "draugr/human": return -60f;
                case "dvergr/fuling": return -30f;
                case "dvergr/greydwarf": return -40f;
                case "fuling/greydwarf": return -10f;
                case "draugr/dvergr": return -50f;
                case "draugr/fuling": return -45f;
                case "draugr/greydwarf": return -30f;
                default: return -20f;
            }
        }

        // Rows: biome. Columns: wood, stone, food, metal.
        private static readonly float[,] BiomeYield =
        {
            /* Meadows     */ { 1.0f, 0.8f, 1.2f, 0.3f },
            /* BlackForest */ { 1.4f, 1.1f, 0.9f, 1.0f },
            /* Swamp       */ { 1.0f, 0.6f, 0.8f, 1.2f },
            /* Mountain    */ { 0.7f, 1.5f, 0.7f, 1.3f },
            /* Plains      */ { 0.6f, 0.9f, 1.5f, 0.4f },
            /* Mistlands   */ { 1.1f, 1.2f, 0.8f, 1.1f },
            /* Ashlands    */ { 0.5f, 1.2f, 0.6f, 1.4f },
            /* DeepNorth   */ { 0.6f, 1.3f, 0.6f, 1.2f },
            /* Ocean       */ { 0.2f, 0.2f, 1.0f, 0.1f },
            /* Other       */ { 0.8f, 0.8f, 0.8f, 0.5f },
        };

        public static float Yield(Biome biome, Res res)
        {
            int col = (int)res;
            if (col > 3) return 1f;
            return BiomeYield[(int)biome, col];
        }

        public static int PlotCount(Tier tier)
        {
            int rings = RingsByTier[(int)tier];
            int n = 0;
            for (int i = 0; i < rings; i++) n += RingPlots[i];
            return n;
        }

        /// <summary>
        /// Offset of a plot from the settlement centre and its yaw (degrees, 0 = +Z): facing the centre,
        /// give or take some degrees, so that no two houses stand quite alike. Settlements founded from
        /// 0.7.0 on also have their plots shifted a little along and across the ring; older ones keep
        /// their places (their ground was levelled there), only turned.
        /// </summary>
        public static void PlotTransform(Settlement s, int plot, out Vec2 offset, out float yawDeg)
        {
            PlotTransform(plot, s.Seed, out offset, out yawDeg);
            uint h = (uint)(s.Seed * 73856093) ^ (uint)((plot + 1) * 19349663);
            float u1 = Unit(ref h), u2 = Unit(ref h), u3 = Unit(ref h);
            yawDeg += (u1 - 0.5f) * 36f;
            if (s.Layout < 1) return;
            float len = offset.Length();
            if (len < 1f) return;
            Vec2 radial = offset * (1f / len);
            Vec2 along = new Vec2(-radial.Z, radial.X);
            offset = offset + radial * ((u2 - 0.5f) * 2.4f) + along * ((u3 - 0.5f) * 1.6f);
        }

        private static float Unit(ref uint h)
        {
            h ^= h << 13;
            h ^= h >> 17;
            h ^= h << 5;
            return (h & 0xFFFF) / 65535f;
        }

        /// <summary>The exact ring position and the yaw straight at the centre (the layout before 0.7.0).</summary>
        public static void PlotTransform(int plot, int seed, out Vec2 offset, out float yawDeg)
        {
            int ring = 0;
            int index = plot;
            while (ring < RingPlots.Length - 1 && index >= RingPlots[ring])
            {
                index -= RingPlots[ring];
                ring++;
            }
            // Each ring is rotated by a seed-dependent phase so settlements do not look identical.
            float phase = ((seed * 31 + ring * 977) & 1023) / 1024f * (float)(Math.PI * 2);
            float angle = phase + index * (float)(Math.PI * 2) / RingPlots[ring];
            offset = Vec2.FromAngle(angle, RingRadius[ring]);
            yawDeg = (float)(Math.Atan2(-offset.X, -offset.Z) * 180.0 / Math.PI);
        }

        public static bool TierRequirementsMet(Settlement s, Tier next)
        {
            int complete = 0, production = 0;
            bool store = false, tower = false, market = false, smithy = false, wall = false, hall = false;
            foreach (Building b in s.Buildings)
            {
                if (b.State != BuildingState.Complete || b.Kind == BuildingKind.Campfire) continue;
                complete++;
                switch (b.Kind)
                {
                    case BuildingKind.Storehouse: store = true; break;
                    case BuildingKind.Watchtower: tower = true; break;
                    case BuildingKind.Market: market = true; break;
                    case BuildingKind.Smithy: smithy = true; break;
                    case BuildingKind.Palisade: wall = true; break;
                    case BuildingKind.GreatHall: hall = true; break;
                }
                if (Buildings[b.Kind].Boosts.HasValue && b.Kind != BuildingKind.Smithy) production++;
            }
            switch (next)
            {
                case Tier.Hamlet: return s.Population >= 8 && complete >= 3 && store;
                case Tier.Village: return s.Population >= 15 && complete >= 6 && production >= 2 && store && tower;
                case Tier.Town: return s.Population >= 26 && complete >= 11 && market && smithy;
                case Tier.City: return s.Population >= 42 && complete >= 17 && wall && hall;
                default: return false;
            }
        }
    }

    /// <summary>Deterministic PRNG whose state lives in the saved world.</summary>
    public struct Rng
    {
        private ulong _s;

        public Rng(ulong state) { _s = state == 0 ? 0x9E3779B97F4A7C15UL : state; }

        public ulong State { get { return _s; } }

        public ulong NextU64()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return _s * 0x2545F4914F6CDD1DUL;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Next() { return (NextU64() >> 40) / 16777216f; }

        public float Range(float min, float max) { return min + (max - min) * Next(); }

        public int Int(int maxExclusive) { return maxExclusive <= 0 ? 0 : (int)(NextU64() % (ulong)maxExclusive); }

        public bool Chance(float p) { return Next() < p; }
    }

    public static class Util
    {
        public static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float MoveToward(float v, float target, float maxDelta)
        {
            if (Math.Abs(target - v) <= maxDelta) return target;
            return v + Math.Sign(target - v) * maxDelta;
        }
    }
}
