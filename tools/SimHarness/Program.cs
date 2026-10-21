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

        public bool GentleWay(Vec2 from, Vec2 to, bool lenient) { return true; }

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
                if (!s.Destroyed && s.ParentId != 0 && (s.Tier != Tier.Camp || s.Population > Simulation.OutpostPeople + 0.01f)) return s.Name + ": an outpost grew into a " + s.Tier + " of " + s.Population;
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

        // ---------------------------------------------------------- campaigns that lose their target

        /// <summary>
        /// A world with an attacker and a defender, a synthetic pair of places 600 m apart and a war
        /// between them, then a campaign on the defender's place (at its war camp, or still scouting).
        /// </summary>
        private static Simulation CampaignWorld(int seed, bool atCamp, out Campaign campaign, out Settlement home, out Settlement target)
        {
            campaign = null;
            home = null;
            target = null;
            Balance balance = new Balance { Expeditions = false, Outposts = false, Migration = false, AllyAid = false, RaidRange = 0f };
            Simulation sim = NewSim(seed, balance);
            Faction attacker = null, defender = null;
            foreach (Faction f in sim.State.Factions)
            {
                if (f.Eliminated) continue;
                if (attacker == null) attacker = f;
                else if (defender == null) defender = f;
            }
            // Only the two sides that matter take part: no other war or campaign runs beside the test.
            foreach (Faction f in sim.State.Factions) if (f.Id != attacker.Id && f.Id != defender.Id) f.Eliminated = true;
            if (attacker == null || defender == null) return null;
            home = sim.DebugFound(attacker.Id, new Vec2(2000f, 2000f), Biome.Meadows);
            target = sim.DebugFound(defender.Id, new Vec2(2600f, 2000f), Biome.Meadows);
            home.Population = 30f;
            home.Soldiers = 20f;
            home.Stock[(int)Res.Food] = 150f;
            home.Stock[(int)Res.Wood] = 150f;
            home.Stock[(int)Res.Stone] = 120f;
            home.Stock[(int)Res.Metal] = 60f;
            target.Population = 12f;
            target.Soldiers = 5f;
            sim.Rel(attacker, defender).Treaty = Treaty.War;
            sim.Rel(attacker, defender).Value = -80f;
            sim.DebugCampaign(attacker.Id, target.Id, atCamp);
            campaign = sim.CampaignOf(attacker.Id);
            CampaignStage wanted = atCamp ? CampaignStage.Camp : CampaignStage.Scouting;
            if (campaign == null || campaign.Stage != wanted) return null;
            return sim;
        }

        private static Simulation CampaignForTest(bool atCamp, out Campaign campaign, out Settlement home, out Settlement target)
        {
            for (int seed = 1; seed <= 25; seed++)
            {
                Simulation sim = CampaignWorld(seed * 101, atCamp, out campaign, out home, out target);
                if (sim != null) return sim;
            }
            campaign = null;
            home = null;
            target = null;
            return null;
        }

        /// <summary>Regression: a campaign must notice when the place it is aimed at changes hands.</summary>
        private static void CampaignHandoverChecks()
        {
            // 1. At the war camp the target passes to a third faction: no assault, and the campaign ends.
            Campaign c;
            Settlement home, target;
            Simulation sim = CampaignForTest(true, out c, out home, out target);
            Check(sim != null, "a campaign can be started at its war camp for the test");
            if (sim == null) return;
            Faction attacker = sim.State.GetFaction(c.FactionId);
            Faction enemy = sim.State.GetFaction(c.EnemyId);
            Faction third = sim.State.Factions.First(f => f.Id != attacker.Id && f.Id != enemy.Id);
            int campId = c.CampId;
            List<SimEvent> events = new List<SimEvent>();
            sim.EventRaised += e => events.Add(e);
            double flipDay = sim.State.Day;
            target.FactionId = third.Id;
            string json = JsonConvert.SerializeObject(sim.State, Formatting.Indented);
            // One day is enough: the campaign must end at the first daily step, before CampDays could launch a band.
            sim.AdvanceTo(flipDay + 1.0, 100000);
            WorldState loaded = JsonConvert.DeserializeObject<WorldState>(json);
            Simulation mirror = new Simulation(loaded, sim.World, sim.Balance);
            mirror.AdvanceTo(loaded.Day + 1.0, 100000);
            Check(Hash(sim.State) == Hash(mirror.State), "a campaign whose target changed hands continues the same after save and load");
            Check(c.Stage == CampaignStage.Over && c.Why == "other", "a campaign ends when its target passes to a third faction (" + c.Stage + ", " + c.Why + ")");
            Settlement camp = sim.State.GetSettlement(campId);
            Check(camp != null && camp.Destroyed, "the war camp of that campaign is given up");
            Check(!events.Any(e => e.FactionId == attacker.Id && (e.Kind == EventKind.CampaignAssault || e.Kind == EventKind.CampaignCamp || e.Kind == EventKind.CampaignWon)),
                "it neither pitches a camp nor marches after the place changed hands");
            Check(!sim.State.Squads.Any(q => q.CampaignId == c.Id && q.State != SquadState.Done && q.State != SquadState.Returning), "no band of it is left on the road");
            Check(sim.CampaignOf(attacker.Id) == null, "the faction is free to begin another campaign");
            int ends = events.Count(e => e.Kind == EventKind.CampaignOver && e.FactionId == attacker.Id && e.SettlementId == c.TargetId);
            Check(ends == 1, "the campaign's end is announced once (" + ends + ")");
            SimEvent over = events.FirstOrDefault(e => e.Kind == EventKind.CampaignOver && e.FactionId == attacker.Id);
            string overText = over != null ? Texts.Describe(over, sim.State) : "";
            Check(over != null && overText.Length > 0 && !overText.Contains("ev_") && !overText.Contains("{"), "the end reads in words, not as a raw key (" + overText + ")");

            // 2. At the war camp the target is already ours (taken by another band): the campaign ends too.
            Simulation ours = CampaignForTest(true, out Campaign c2, out Settlement home2, out Settlement target2);
            if (ours != null)
            {
                Faction attacker2 = ours.State.GetFaction(c2.FactionId);
                List<SimEvent> won = new List<SimEvent>();
                ours.EventRaised += e => won.Add(e);
                target2.FactionId = attacker2.Id;
                ours.AdvanceTo(ours.State.Day + 1.0, 100000);
                Check(c2.Stage == CampaignStage.Over && c2.Why == "ours", "a campaign ends when its target is already ours (" + c2.Stage + ", " + c2.Why + ")");
                Check(!won.Any(e => e.Kind == EventKind.CampaignWon || e.Kind == EventKind.CampaignAssault), "it claims no victory and sends no band for a place already ours");
            }
            else Check(false, "a campaign can be started for the 'already ours' test");

            // 3. Scouts come back after the place changed hands: no war camp is pitched.
            Simulation scouting = CampaignForTest(false, out Campaign c3, out Settlement home3, out Settlement target3);
            if (scouting != null)
            {
                Faction enemy3 = scouting.State.GetFaction(c3.EnemyId);
                Faction third3 = scouting.State.Factions.First(f => f.Id != c3.FactionId && f.Id != enemy3.Id);
                Settlement scoutTarget = scouting.State.GetSettlement(c3.TargetId);
                Settlement scoutHome = scouting.State.GetSettlement(c3.HomeId);
                Squad scouts = scouting.State.Squads.First(q => q.Id == c3.SquadId);
                scouts.State = SquadState.Returning;
                scouts.Pos = scoutHome.Pos + new Vec2(1f, 0f);
                List<SimEvent> after = new List<SimEvent>();
                scouting.EventRaised += e => after.Add(e);
                scoutTarget.FactionId = third3.Id;
                scouting.Step();
                Check(c3.Stage == CampaignStage.Over && c3.Why == "other", "scouts back after the place changed hands end the campaign (" + c3.Stage + ", " + c3.Why + ")");
                Check(!after.Any(e => e.Kind == EventKind.CampaignCamp || e.Kind == EventKind.CampaignScouted), "no war camp is pitched for it");
            }
            else Check(false, "a campaign can be started for the scouting test");

            // 4. A band already marching does not fight for a place that is no longer the enemy's.
            Simulation marching = CampaignForTest(true, out Campaign c4, out Settlement home4, out Settlement target4);
            if (marching != null)
            {
                Faction enemy4 = marching.State.GetFaction(c4.EnemyId);
                Faction third4 = marching.State.Factions.First(f => f.Id != c4.FactionId && f.Id != enemy4.Id);
                Faction attacker4 = marching.State.GetFaction(c4.FactionId);
                c4.StageSinceDay = marching.State.Day - marching.Balance.CampDays;
                while (c4.Stage == CampaignStage.Camp && marching.State.Day < flipDay + 4.0) marching.Step();
                Check(c4.Stage == CampaignStage.Assault, "a campaign still marches when its target is the enemy's (" + c4.Stage + ")");
                // The third faction is at war with the attacker for a war of its own: the campaign band
                // must still not join that fight for the old aim.
                marching.Rel(attacker4, third4).Treaty = Treaty.War;
                List<SimEvent> after = new List<SimEvent>();
                marching.EventRaised += e => after.Add(e);
                target4.FactionId = third4.Id;
                marching.AdvanceTo(marching.State.Day + 1.0, 100000);
                Check(c4.Stage == CampaignStage.Over && c4.Why == "other", "a marching band turns home when the place changes hands (" + c4.Stage + ", " + c4.Why + ")");
                Check(!after.Any(e => e.Kind == EventKind.CampaignWon || e.Kind == EventKind.RaidWon || e.Kind == EventKind.RaidRepelled || e.Kind == EventKind.CampaignAssault),
                    "no battle and no second assault after the place changed hands");
                Check(!marching.State.Squads.Any(q => q.CampaignId == c4.Id && q.State != SquadState.Done && q.State != SquadState.Returning), "the band is called home and disbands");
            }
            else Check(false, "a campaign can be started for the marching test");

            // 5. Holding is not broken: the place taken is held (the new check must not end the campaign).
            Simulation holding = CampaignForTest(true, out Campaign c5, out Settlement home5, out Settlement target5);
            if (holding != null)
            {
                Faction attacker5 = holding.State.GetFaction(c5.FactionId);
                Faction enemy5 = holding.State.GetFaction(c5.EnemyId);
                c5.StageSinceDay = holding.State.Day - holding.Balance.CampDays;
                while (c5.Stage == CampaignStage.Camp && holding.State.Day < flipDay + 4.0) holding.Step();
                Check(c5.Stage == CampaignStage.Assault, "the holding test reaches the assault (" + c5.Stage + ")");
                target5.Population = 1f;
                target5.Soldiers = 0f;
                List<SimEvent> battle = new List<SimEvent>();
                holding.EventRaised += e => battle.Add(e);
                while (c5.Stage == CampaignStage.Assault && holding.State.Day < flipDay + 14.0) holding.Step();
                Check(c5.Stage == CampaignStage.Holding && target5.FactionId == attacker5.Id,
                    "taking the place still brings the holding stage (" + c5.Stage + ")");
                Check(battle.Any(e => e.Kind == EventKind.CampaignWon), "the campaign still claims a place its band takes");
                // A holding campaign must not be ended by the new owner check: the place is meant to be ours.
                holding.AdvanceTo(holding.State.Day + 1.0, 100000);
                Check(c5.Stage == CampaignStage.Holding && c5.Why == null, "the holding stage is not ended for the place belonging to the campaign's faction (" + c5.Stage + ", " + c5.Why + ")");
                target5.FactionId = enemy5.Id;
                holding.AdvanceTo(holding.State.Day + 3.0, 100000);
                Check(c5.Stage == CampaignStage.Over && c5.Why == "lost", "a place lost again still ends the campaign as before (" + c5.Why + ")");
            }
            else Check(false, "a campaign can be started for the holding test");

            // 6. A band still gathering is called home when the campaign ends (the retreat in End).
            Simulation retreating = CampaignForTest(true, out Campaign c6, out Settlement home6, out Settlement target6);
            if (retreating != null)
            {
                Faction enemy6 = retreating.State.GetFaction(c6.EnemyId);
                Faction third6 = retreating.State.Factions.First(f => f.Id != c6.FactionId && f.Id != enemy6.Id);
                c6.StageSinceDay = retreating.State.Day - retreating.Balance.CampDays;
                while (c6.Stage == CampaignStage.Camp && retreating.State.Day < flipDay + 4.0) retreating.Step();
                Check(c6.Stage == CampaignStage.Assault, "the retreat test reaches the assault (" + c6.Stage + ")");
                Squad band = retreating.State.Squads.First(q => q.Id == c6.SquadId);
                band.ReadyDay = retreating.State.Day + 5.0; // still in the camp when the campaign ends
                target6.FactionId = third6.Id;
                retreating.AdvanceTo(retreating.State.Day + 1.0, 100000);
                Check(c6.Stage == CampaignStage.Over && band.State == SquadState.Returning,
                    "a band still gathering is given the order to turn home (" + c6.Stage + ", " + band.State + ")");
            }
            else Check(false, "a campaign can be started for the retreat test");
        }

        /// <summary>Regression: scouts lost before or after their report must not leave the campaign waiting for peace.</summary>
        private static void CampaignScoutLossChecks()
        {
            // 1. The scouts saw the place and fell on the way home: no report ever comes, the campaign ends.
            Campaign c;
            Settlement home, target;
            Simulation sim = CampaignForTest(false, out c, out home, out target);
            Check(sim != null, "a scouting campaign can be started for the test");
            if (sim != null)
            {
                Squad scouts = sim.State.Squads.First(q => q.Id == c.SquadId);
                c.EnemyEstimate = 10f; // they reached the place and saw its defenders
                scouts.State = SquadState.Returning;
                scouts.Pos = home.Pos + new Vec2(1200f, 0f); // still on the way home
                List<SimEvent> events = new List<SimEvent>();
                sim.EventRaised += e => events.Add(e);
                sim.SquadLoss(scouts.Id);
                sim.Step(); // the band falls apart here, before it comes home
                Check(c.Stage == CampaignStage.Scouting && c.EnemyEstimate >= 0f, "the campaign is still scouting for that step (" + c.Stage + ")");
                string json = JsonConvert.SerializeObject(sim.State, Formatting.Indented);
                sim.AdvanceTo(sim.State.Day + 1.0, 100000);
                WorldState loaded = JsonConvert.DeserializeObject<WorldState>(json);
                Simulation mirror = new Simulation(loaded, sim.World, sim.Balance);
                mirror.AdvanceTo(loaded.Day + 1.0, 100000);
                Check(c.Stage == CampaignStage.Over && c.Why == "scouts", "a campaign ends when its scouts die after seeing the place (" + c.Stage + ", " + c.Why + ")");
                Check(!events.Any(e => e.Kind == EventKind.CampaignCamp || e.Kind == EventKind.CampaignScouted), "no war camp is pitched without a report");
                Check(c.CampId == 0, "no war camp of that campaign was ever set up");
                Check(sim.CampaignOf(c.FactionId) == null, "the faction may begin another campaign after the scouts are lost");
                Check(Hash(sim.State) == Hash(mirror.State), "a campaign whose scouts were lost carries on the same after save and load");
                Campaign mirrored = mirror.State.Campaigns.FirstOrDefault(x => x.Id == c.Id);
                Check(mirrored != null && mirrored.Stage == CampaignStage.Over && mirrored.Why == "scouts", "the lost scouts are remembered after save and load");
            }

            // 2. Scouts lost before they saw the place still end the campaign, as before.
            Simulation early = CampaignForTest(false, out Campaign c2, out Settlement home2, out Settlement target2);
            if (early != null)
            {
                Squad scouts2 = early.State.Squads.First(q => q.Id == c2.SquadId);
                scouts2.State = SquadState.Returning;
                scouts2.Pos = home2.Pos + new Vec2(1200f, 0f);
                early.SquadLoss(scouts2.Id);
                early.Step();
                early.AdvanceTo(early.State.Day + 1.0, 100000);
                Check(c2.Stage == CampaignStage.Over && c2.Why == "scouts", "scouts lost before they saw the place end the campaign as before (" + c2.Stage + ", " + c2.Why + ")");
            }
            else Check(false, "a scouting campaign can be started for the early-loss test");

            // 3. A report that comes home still moves the campaign on to its war camp.
            Simulation report = CampaignForTest(false, out Campaign c3, out Settlement home3, out Settlement target3);
            if (report != null)
            {
                Squad scouts3 = report.State.Squads.First(q => q.Id == c3.SquadId);
                c3.EnemyEstimate = 1f; // a weak place: the campaign goes on
                scouts3.State = SquadState.Returning;
                scouts3.Pos = home3.Pos + new Vec2(1f, 0f);
                report.Step();
                Check(c3.Stage != CampaignStage.Scouting, "the scouts' report still moves the campaign on (" + c3.Stage + ")");
            }
            else Check(false, "a scouting campaign can be started for the report test");
        }

        /// <summary>Regression: a taken war camp or outpost becomes a plain place of the new owner.</summary>
        private static void CaptureCleanupChecks()
        {
            Campaign c;
            Settlement home, target;
            Simulation sim = CampaignForTest(true, out c, out home, out target);
            Check(sim != null, "a campaign can be started for the capture test");
            if (sim == null) return;
            Faction attacker = sim.State.GetFaction(c.FactionId);
            // The place about to be taken is the enemy's war camp, outpost and road post all at once,
            // with a chief just lost: none of that may pass to the new owner.
            target.CampaignId = c.Id;
            target.ParentId = home.Id;
            target.OutpostGoal = Res.Metal;
            target.RoadPost = true;
            target.LeaderlessUntil = sim.State.Day + 3.0;
            // As if every building stood in the world already, laid out by the latest blueprints.
            List<Building> built = target.Buildings.ToList();
            foreach (Building b in built) b.LayoutRev = 14;
            List<SimEvent> events = new List<SimEvent>();
            sim.EventRaised += e => events.Add(e);
            // Let the campaign march at once and make the place easy to take.
            c.StageSinceDay = sim.State.Day - sim.Balance.CampDays;
            double until = sim.State.Day + 4.0;
            while (c.Stage == CampaignStage.Camp && sim.State.Day < until) sim.Step();
            target.Population = 1f;
            target.Soldiers = 0f;
            until = sim.State.Day + 14.0;
            while (target.FactionId != attacker.Id && sim.State.Day < until) sim.Step();
            Check(target.FactionId == attacker.Id && events.Any(e => e.Kind == EventKind.SettlementCaptured), "the campaign takes the place for its faction");
            Check(target.CampaignId == 0, "the taken place keeps no campaign tie (" + target.CampaignId + ")");
            Check(target.ParentId == 0, "it keeps no parent settlement tie (" + target.ParentId + ")");
            Check(target.OutpostGoal == Res.Wood && !target.RoadPost, "it is no longer an outpost or a road post");
            Check(target.LeaderlessUntil < 0f, "the old chief's mourning does not pass to the new owner (" + target.LeaderlessUntil + ")");
            Check(built.Any(b => b.Kind == BuildingKind.Campfire), "the taken camp has a fire for the layout test");
            Check(built.All(b => b.Kind == BuildingKind.Campfire ? b.LayoutRev == 0 : b.LayoutRev == 14),
                "only the fire of a taken war camp is laid out again (" + string.Join(", ", built.Select(b => b.Kind + " " + b.LayoutRev)) + ")");
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
            Check(sim.State.Settlements.Count(x => x.CampaignId == 0) <= sim.Balance.MaxSettlementsTotal, "settlement cap respected (war camps aside)");

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

            // ---------------------------------------------------------- campaigns and a target that changes hands
            CampaignHandoverChecks();
            // ---------------------------------------------------------- scouts lost with or without their report
            CampaignScoutLossChecks();
            // ---------------------------------------------------------- a taken camp must not stay a camp of the old owner
            CaptureCleanupChecks();

            // ---------------------------------------------------------- many seeds
            int wars = 0, alliances = 0, captures = 0, worstFactions = 99, camps = 0, counters = 0, aid = 0, moves = 0, won = 0, outposts = 0, roadPosts = 0;
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
                    if (e.Kind == EventKind.CampaignCamp) camps++;
                    if (e.Kind == EventKind.CampaignWon) won++;
                    if (e.Kind == EventKind.CampaignCounter) counters++;
                    if (e.Kind == EventKind.AllyAid) aid++;
                    if (e.Kind == EventKind.Migration) moves++;
                    if (e.Kind == EventKind.OutpostFounded) { if (e.Args[2] == "road") roadPosts++; else outposts++; }
                };
                for (int day = 1; day <= 200 && anyBroken == null; day++)
                {
                    m.AdvanceTo(day, 100000);
                    anyBroken = Invariants(m);
                    if (anyBroken != null) anyBroken = "seed " + s * 101 + " day " + day + ": " + anyBroken;
                }
            }
            Console.WriteLine("  info  campaigns across seeds: " + camps + " war camps, " + won + " places taken, " + counters + " counterattacks, " + aid + " allies came to help, " + moves + " moves of people, " + outposts + " outposts, " + roadPosts + " road posts");
            Check(outposts > 0, "factions put outposts where the land gives what they lack (" + outposts + ")");
            Check(camps > 0 && aid > 0, "campaigns set up camps and allies come to help (" + camps + " camps, " + aid + " aid)");
            Check(won > 0, "campaigns still take places across seeds (" + won + " places taken)");
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
