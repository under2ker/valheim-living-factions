using System;
using System.Collections.Generic;
using System.Linq;
using LivingFactions.Core;
using Newtonsoft.Json;

namespace LivingFactions.Harness
{
    /// <summary>A stand-in world: biome sectors by angle and distance, with patches of water.</summary>
    internal class FakeWorld : IWorldQuery
    {
        private readonly int _seed;

        public FakeWorld(int seed) { _seed = seed; }

        public float WorldRadius { get { return 10000f; } }

        private float Noise(float x, float z, int salt)
        {
            int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
            uint h = (uint)(xi * 374761393 + zi * 668265263 + (_seed + salt) * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        public bool ProbeSite(Vec2 pos, float radius, out Biome biome)
        {
            float dist = pos.Length();
            float cellX = pos.X / 400f, cellZ = pos.Z / 400f;
            float n = Noise(cellX, cellZ, 1);
            if (dist < 700f) biome = n < 0.75f ? Biome.Meadows : Biome.BlackForest;
            else if (dist < 2500f) biome = n < 0.3f ? Biome.Meadows : n < 0.65f ? Biome.BlackForest : n < 0.8f ? Biome.Swamp : n < 0.9f ? Biome.Mountain : Biome.Plains;
            else biome = n < 0.15f ? Biome.Meadows : n < 0.35f ? Biome.BlackForest : n < 0.5f ? Biome.Swamp : n < 0.65f ? Biome.Mountain : n < 0.85f ? Biome.Plains : Biome.Mistlands;
            // Roughly one probe in seven lands in water or on a cliff.
            return Noise(pos.X / 30f, pos.Z / 30f, 7) > 0.14f && dist < WorldRadius;
        }

        public bool IsReserved(Vec2 pos, float radius)
        {
            return pos.Length() < 120f + radius;
        }
    }

    internal static class Program
    {
        private static int _failures;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) _failures++;
        }

        private static Simulation NewSim(int seed, Balance balance = null)
        {
            WorldState state = new WorldState { RngState = (ulong)seed * 2654435761UL + 12345UL };
            Simulation sim = new Simulation(state, new FakeWorld(seed), balance ?? new Balance());
            sim.Seed();
            return sim;
        }

        private static string Invariants(Simulation sim)
        {
            foreach (Settlement s in sim.State.Settlements)
            {
                if (float.IsNaN(s.Population) || s.Population < 0) return s.Name + ": bad population " + s.Population;
                if (s.Soldiers < 0 || s.Soldiers > s.Population + 0.001f) return s.Name + ": soldiers " + s.Soldiers + " > pop " + s.Population;
                float cap = sim.StorageCap(s);
                for (int i = 0; i < 5; i++)
                {
                    if (float.IsNaN(s.Stock[i]) || s.Stock[i] < -0.001f) return s.Name + ": bad stock " + (Res)i + " " + s.Stock[i];
                    if (i < 4 && s.Stock[i] > cap + 0.5f) return s.Name + ": stock over cap " + (Res)i + " " + s.Stock[i] + " > " + cap;
                }
                HashSet<int> plots = new HashSet<int>();
                foreach (Building b in s.Buildings)
                {
                    if (b.Plot >= 0 && !plots.Add(b.Plot)) return s.Name + ": plot " + b.Plot + " used twice";
                }
                if (!s.Destroyed && sim.State.GetFaction(s.FactionId) == null) return s.Name + ": orphan settlement";
                if (s.Buildings.Count(b => b.State == BuildingState.UnderConstruction) > 1) return s.Name + ": two projects at once";
            }
            foreach (Faction f in sim.State.Factions)
            {
                foreach (var rel in f.Relations)
                {
                    if (rel.Key <= f.Id) return f.Name + ": relation stored on the wrong side";
                    if (float.IsNaN(rel.Value.Value) || rel.Value.Value < -100.01f || rel.Value.Value > 100.01f) return f.Name + ": relation out of range";
                }
            }
            return null;
        }

        private static string Hash(WorldState state)
        {
            return JsonConvert.SerializeObject(state);
        }

        private static void PrintSummary(Simulation sim)
        {
            WorldState st = sim.State;
            Console.WriteLine();
            Console.WriteLine("=== Day " + st.Day.ToString("0") + " ===");
            foreach (Faction f in st.Factions)
            {
                Console.WriteLine((f.Eliminated ? "[gone] " : "") + f.Name + "  aggr " + f.Aggression.ToString("0.00") + " exp " + f.Expansionism.ToString("0.00") + " merc " + f.Mercantile.ToString("0.00") + "  strength " + sim.Strength(f).ToString("0.0"));
                foreach (Settlement s in st.Settlements.Where(x => x.FactionId == f.Id))
                {
                    string stock = string.Join(" ", Enumerable.Range(0, 5).Select(i => ((Res)i).ToString().Substring(0, 1) + s.Stock[i].ToString("0")));
                    string kinds = string.Join(",", s.Buildings.Where(b => b.State == BuildingState.Complete).GroupBy(b => b.Kind).Select(g => g.Key + (g.Count() > 1 ? "x" + g.Count() : "")));
                    Console.WriteLine("   " + (s.Destroyed ? "[ruin] " : "") + s.Name.PadRight(14) + s.Tier.ToString().PadRight(8) + s.Biome.ToString().PadRight(12) + " pop " + s.Population.ToString("0").PadLeft(3) + " sol " + s.Soldiers.ToString("0").PadLeft(2) + "  " + stock + "  @" + s.Pos + "  [" + kinds + "]");
                }
            }
            Console.WriteLine("Treaties:");
            for (int i = 0; i < st.Factions.Count; i++)
            {
                for (int j = i + 1; j < st.Factions.Count; j++)
                {
                    Faction a = st.Factions[i], b = st.Factions[j];
                    if (a.Eliminated || b.Eliminated) continue;
                    Relation r = sim.Rel(a, b);
                    Console.WriteLine("   " + a.Name + " / " + b.Name + ": " + r.Treaty + " " + r.Value.ToString("0"));
                }
            }
        }

        private static int Main(string[] args)
        {
            int seed = args.Length > 0 ? int.Parse(args[0]) : 42;
            int days = args.Length > 1 ? int.Parse(args[1]) : 150;
            bool verbose = args.Contains("-v");
            Texts.Lang = args.Contains("--ru") ? 1 : 0;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // ---------------------------------------------------------- long run
            Simulation sim = NewSim(seed);
            int shown = 0;
            sim.EventRaised += e =>
            {
                bool minor = e.Kind == EventKind.BuildingStarted || e.Kind == EventKind.BuildingCompleted || e.Kind == EventKind.TradeCaravan;
                if (verbose || !minor)
                {
                    Console.WriteLine("  d" + e.Day.ToString("0.0").PadLeft(6) + "  " + Texts.Describe(e, sim.State));
                    shown++;
                }
            };
            Console.WriteLine("Seeded " + sim.State.Factions.Count + " factions, " + sim.State.Settlements.Count + " settlements (seed " + seed + ")");
            foreach (SimEvent e in sim.State.Chronicle) Console.WriteLine("  d   0.0  " + Texts.Describe(e, sim.State));

            string broken = null;
            double firstHamlet = -1, firstVillage = -1, firstTown = -1, firstCity = -1;
            int[] checkpoints = { 10, 30, 60, 100, days };
            for (int day = 1; day <= days; day++)
            {
                sim.AdvanceTo(day, 1000);
                if (broken == null) broken = Invariants(sim);
                foreach (Settlement s in sim.State.Settlements)
                {
                    if (firstHamlet < 0 && s.Tier >= Tier.Hamlet) firstHamlet = day;
                    if (firstVillage < 0 && s.Tier >= Tier.Village) firstVillage = day;
                    if (firstTown < 0 && s.Tier >= Tier.Town) firstTown = day;
                    if (firstCity < 0 && s.Tier >= Tier.City) firstCity = day;
                }
                if (checkpoints.Contains(day)) PrintSummary(sim);
            }

            Console.WriteLine();
            Console.WriteLine("First hamlet day " + firstHamlet + ", village " + firstVillage + ", town " + firstTown + ", city " + firstCity);
            var kinds = sim.State.Chronicle.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine("Recent chronicle mix: " + string.Join(", ", kinds.Select(k => k.Key + "=" + k.Value)));

            Console.WriteLine();
            Console.WriteLine("Checks:");
            Check(sim.State.Factions.Count >= 5, "world seeding places most cultures (" + sim.State.Factions.Count + "/7)");
            Check(broken == null, "invariants held every day" + (broken != null ? " — " + broken : ""));
            Check(Math.Abs(sim.State.Day - days) < 0.01, "simulation clock matches the requested day");
            Check(firstHamlet > 0 && firstHamlet <= 12, "a camp becomes a hamlet within 12 days (day " + firstHamlet + ")");
            Check(firstVillage > 0 && firstVillage <= 40, "a village appears within 40 days (day " + firstVillage + ")");
            Check(firstTown > 0 && firstTown <= 110, "a town appears within 110 days (day " + firstTown + ")");
            Check(sim.State.Settlements.Count > sim.State.Factions.Count, "factions found daughter settlements (" + sim.State.Settlements.Count + " settlements)");
            Check(sim.State.Settlements.Count <= sim.Balance.MaxSettlementsTotal, "settlement cap respected");

            // ---------------------------------------------------------- forecast
            string badLine = null;
            int forecastLines = 0;
            HashSet<string> wordings = new HashSet<string>();
            for (int lang = 0; lang < 2; lang++)
            {
                Texts.Lang = lang;
                foreach (Settlement s in sim.State.Settlements)
                {
                    foreach (Forecast.Line line in Forecast.ForSettlement(sim, s))
                    {
                        forecastLines++;
                        wordings.Add(line.Text);
                        if (line.Text.Contains("fc_") || line.Text.Contains("{") || line.Text.Contains("tier_") || line.Text.Contains("b_")) badLine = line.Text;
                    }
                }
                foreach (Faction f in sim.State.Factions)
                {
                    string text = Forecast.ForFaction(sim, f).Text;
                    forecastLines++;
                    if (text.Contains("fc_") || text.Contains("{") || text.Contains("tier_") || text.Contains("b_")) badLine = text;
                }
            }
            Texts.Lang = 0;
            Check(badLine == null && forecastLines > 20, "forecast reads in both languages (" + forecastLines + " lines)" + (badLine != null ? " — " + badLine : ""));
            Check(wordings.Count > sim.State.Settlements.Count * 2, "forecast differs from place to place (" + wordings.Count + " different lines)");

            // ---------------------------------------------------------- determinism
            Simulation a = NewSim(7), b = NewSim(7);
            a.AdvanceTo(40, 100000);
            b.AdvanceTo(40, 100000);
            Check(Hash(a.State) == Hash(b.State), "same seed gives the same history");

            // ---------------------------------------------------------- save / load mid-run
            Simulation c = NewSim(7);
            c.AdvanceTo(20, 100000);
            string json = JsonConvert.SerializeObject(c.State, Formatting.Indented);
            WorldState loaded = JsonConvert.DeserializeObject<WorldState>(json);
            Simulation c2 = new Simulation(loaded, new FakeWorld(7), new Balance());
            c2.AdvanceTo(40, 100000);
            Check(Hash(c2.State) == Hash(a.State), "save + load mid-run does not change the future (" + json.Length / 1024 + " KB save)");

            // ---------------------------------------------------------- catch-up stepping
            Simulation d = NewSim(7);
            int stepsRun = d.AdvanceTo(10, 50);
            Check(stepsRun == 50 && d.State.Day < 10, "AdvanceTo respects its step budget");

            // ---------------------------------------------------------- player diplomacy
            Simulation p = NewSim(11, new Balance { EnableWars = false });
            p.AdvanceTo(12, 100000);
            const long me = 1001;
            Faction raven = p.State.Factions.First(f => f.CultureId == "clan_raven");
            Settlement home = p.State.Settlements.First(s => s.FactionId == raven.Id);
            p.PlayerRel(raven, me).Name = "Tester";
            Check(p.GetStanding(raven, me) == Standing.Neutral, "a human clan starts neutral");
            Faction fulings = p.State.Factions.FirstOrDefault(f => f.CultureId == "fuling_horde");
            if (fulings != null) Check(p.IsHostileToPlayer(fulings.Id, me), "fulings start hostile");

            Res needed = p.NeededResource(home);
            float repBefore = p.PlayerRel(raven, me).Reputation;
            float gain1 = p.PlayerGift(home, me, needed, 20);
            float gain2 = p.PlayerGift(home, me, needed, 20);
            Check(gain1 > 0 && p.PlayerRel(raven, me).Reputation > repBefore, "a gift raises reputation (+" + gain1.ToString("0.0") + ")");
            Check(gain2 < gain1, "repeated gifts give diminishing returns (+" + gain2.ToString("0.0") + ")");
            Check(!p.PlayerProposeAlliance(raven, me).Ok, "alliance refused without trust");

            p.PlayerRel(raven, me).Reputation = 75;
            Check(p.GetStanding(raven, me) == Standing.Friendly, "high reputation reads as friendly");
            Check(p.PlayerProposeAlliance(raven, me).Ok && p.GetStanding(raven, me) == Standing.Allied, "alliance accepted with trust");

            float popBefore = home.Population;
            for (int i = 0; i < 3; i++) p.PlayerKilledMember(home, me, false);
            Check(home.Population <= popBefore - 3 + 0.001f, "killing members removes population");
            Check(p.GetStanding(raven, me) != Standing.Allied, "murder breaks the alliance (rep " + p.PlayerRel(raven, me).Reputation.ToString("0") + ")");
            for (int i = 0; i < 12 && !p.PlayerRel(raven, me).AtWar; i++) p.PlayerKilledMember(home, me, false);
            Check(p.PlayerRel(raven, me).AtWar && p.IsHostileToPlayer(raven.Id, me), "enough blood means the faction declares war");
            Check(p.PlayerBuy(home, Res.Wood, 5, me, 9999) < 0, "no trade during war");

            int cost = p.PeaceCost(raven, me);
            Check(!p.PlayerProposePeace(raven, me, cost).Ok, "peace refused while the blood is fresh");
            if (!home.Destroyed)
            {
                p.AdvanceTo(p.State.Day + 2, 100000);
                cost = p.PeaceCost(raven, me);
                Check(!p.PlayerProposePeace(raven, me, cost - 5).Ok, "peace refused when underpaid");
                Check(p.PlayerProposePeace(raven, me, cost).Ok && !p.IsHostileToPlayer(raven.Id, me), "peace bought for " + cost + " coins");
            }

            // Trade maths on a fresh neutral clan.
            Faction stag = p.State.Factions.First(f => f.CultureId == "clan_stag");
            Settlement market = p.State.Settlements.First(s => s.FactionId == stag.Id);
            market.Stock[(int)Res.Wood] = 100;
            market.Stock[(int)Res.Coins] = 500;
            float coinsBefore = market.Stock[(int)Res.Coins];
            int paid = p.PlayerBuy(market, Res.Wood, 10, me, 1000);
            Check(paid > 0 && Math.Abs(market.Stock[(int)Res.Wood] - 90) < 0.01f && Math.Abs(market.Stock[(int)Res.Coins] - coinsBefore - paid) < 0.01f, "buying moves goods and coins (10 wood for " + paid + ")");
            int got = p.PlayerSell(market, Res.Wood, 10, me);
            Check(got > 0 && got < paid, "selling pays less than buying costs (" + got + " < " + paid + ")");
            Check(p.PlayerBuy(market, Res.Wood, 10, me, 1) < 0, "cannot buy without coins");
            // The merchant's axe: a player hook cuts buying prices by 10%, and leaves selling alone.
            float buyPlain = p.BuyPrice(market, Res.Wood, me), sellPlain = p.SellPrice(market, Res.Wood, me);
            p.PlayerBuyMult = pid => pid == me ? 0.9f : 1f;
            float buyDiscount = p.BuyPrice(market, Res.Wood, me), sellDiscount = p.SellPrice(market, Res.Wood, me);
            float buyOther = p.BuyPrice(market, Res.Wood, me + 1);
            p.PlayerBuyMult = null;
            Check(Math.Abs(buyDiscount - buyPlain * 0.9f) < 0.0001f && sellDiscount == sellPlain && buyOther == buyPlain,
                "a merchant's hook cuts the buying price by 10% only for its owner (" + buyPlain.ToString("0.00") + " -> " + buyDiscount.ToString("0.00") + ")");

            string after = Invariants(p);
            Check(after == null, "invariants hold after the diplomacy scenario" + (after != null ? " — " + after : ""));

            // ---------------------------------------------------------- many seeds
            int wars = 0, alliances = 0, captures = 0, worstFactions = 99;
            string anyBroken = null;
            for (int s = 1; s <= 25; s++)
            {
                Simulation m = NewSim(s * 101);
                worstFactions = Math.Min(worstFactions, m.State.Factions.Count);
                m.EventRaised += e =>
                {
                    if (e.Kind == EventKind.WarDeclared) wars++;
                    if (e.Kind == EventKind.AllianceFormed) alliances++;
                    if (e.Kind == EventKind.SettlementCaptured) captures++;
                };
                for (int day = 1; day <= 200 && anyBroken == null; day++)
                {
                    m.AdvanceTo(day, 100000);
                    anyBroken = Invariants(m);
                    if (anyBroken != null) anyBroken = "seed " + s * 101 + " day " + day + ": " + anyBroken;
                }
            }
            Check(anyBroken == null, "25 seeds x 200 days keep every invariant" + (anyBroken != null ? " — " + anyBroken : ""));
            Check(worstFactions >= 4, "every seed places at least 4 factions (worst " + worstFactions + ")");
            {
                // Every name the cultures can make reads in Russian letters when the mod speaks Russian.
                int saved = Texts.Lang;
                Texts.Lang = 1;
                string latin = null;
                foreach (CultureDef cd in Defs.Cultures)
                {
                    List<string> all = new List<string>(cd.PersonNames);
                    if (cd.FemaleNames != null) all.AddRange(cd.FemaleNames);
                    foreach (string pre in cd.SettlementPrefixes) foreach (string suf in cd.SettlementSuffixes) all.Add(pre + suf);
                    all.Add(cd.SettlementPrefixes[0] + cd.SettlementSuffixes[0] + " 12");
                    foreach (string n in all)
                    {
                        string ru = Texts.Name(n);
                        foreach (char ch in ru) if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) latin = n + " -> " + ru;
                    }
                }
                Check(latin == null && Texts.Name("Vindfell") == "Виндфелл" && Texts.Name("Hrafnheim 12") == "Храфнхейм 12", "names read in Russian" + (latin != null ? " — " + latin : ""));
                Texts.Lang = 0;
                Check(Texts.Name("Vindfell") == "Vindfell", "names stay as they are in English");
                // Every language the menu offers has every line, and a choice wins over the game's own language.
                string gap = null;
                for (int lang = 2; lang < Texts.Codes.Length; lang++)
                {
                    foreach (string key in Texts.Keys)
                    {
                        if (!key.StartsWith("lang_") && !Texts.Translated(lang, key)) gap = Texts.Codes[lang] + " " + key;
                    }
                }
                Check(gap == null, Texts.Codes.Length + " languages have every line" + (gap != null ? " — missing " + gap : ""));
                Check(Texts.Resolve("auto", "Polish") == 2 && Texts.Resolve("pl", "Russian") == 2 && Texts.Resolve("auto", "Russian") == 1 && Texts.Resolve("auto", "Korean") == 0 && Texts.Resolve("de", null) == 3,
                    "the language follows the game unless the player chose one");
                Texts.Lang = 2;
                Check(Texts.Name("Barkthicket") == "Gąszcz Kory" && Texts.Name("Vindfell") == "Vindfell" && Texts.Name("Hrolf the Drowned") == "Hrolf Topielec", "names follow the mod's language: " + Texts.Name("Barkthicket"));
                Texts.Lang = 3;
                Check(Texts.Name("Barkthicket") == "Borkendickicht" && Texts.Name("Mosshollow 12") == "Moosmulde 12", "German puts the halves together: " + Texts.Name("Mosshollow 12"));
                Texts.Lang = 2;
                Check(Texts.Get("ui_trade") == "Handluj" && Texts.Get("ui_peace", 40) == "Zaproponuj pokój (40 monet)", "Polish reads as Polish");
                Texts.Lang = saved;
            }
            Check(wars > 0, "wars break out across seeds (" + wars + " wars, " + alliances + " alliances, " + captures + " captures in 25 worlds)");

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECK(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }
    }
}
