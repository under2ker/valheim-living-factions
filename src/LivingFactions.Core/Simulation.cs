using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    /// <summary>
    /// The strategic layer: runs for the whole world regardless of what the game has loaded.
    /// One step is one in-game hour.
    /// </summary>
    public partial class Simulation
    {
        public WorldState State;
        public IWorldQuery World;
        public Balance Balance;

        /// <summary>Raised for everything worth telling the player or reacting to in the game world.</summary>
        public event Action<SimEvent> EventRaised;

        private Rng _rng;

        public Simulation(WorldState state, IWorldQuery world, Balance balance)
        {
            State = state;
            World = world;
            Balance = balance ?? new Balance();
            _rng = new Rng(state.RngState);
        }

        // ------------------------------------------------------------------ time

        /// <summary>Runs whole steps until the simulation has caught up with <paramref name="day"/>.</summary>
        public int AdvanceTo(double day, int maxSteps)
        {
            const double step = 1.0 / Defs.StepsPerDay;
            int steps = 0;
            while (State.Day + step <= day && steps < maxSteps)
            {
                Step();
                steps++;
            }
            return steps;
        }

        public void Step()
        {
            _rng = new Rng(State.RngState);
            const float dt = 1f / Defs.StepsPerDay;
            double before = State.Day;
            State.Steps++;
            State.Day = State.Steps / (double)Defs.StepsPerDay;

            // Indexed loop: settlements can be founded during the step.
            for (int i = 0; i < State.Settlements.Count; i++)
            {
                Settlement s = State.Settlements[i];
                if (!s.Destroyed) StepSettlement(s, dt * Balance.Pace);
            }

            StepSquads(dt * Balance.Pace);
            if (Math.Floor(State.Day) > Math.Floor(before)) Daily();
            for (int i = 0; i < State.Settlements.Count; i++) KeepOutpostSmall(State.Settlements[i]);
            State.RngState = _rng.State;
        }

        private void Daily()
        {
            foreach (Faction f in State.Factions)
            {
                if (f.Eliminated) continue;
                if (CountSettlements(f.Id) == 0)
                {
                    f.Eliminated = true;
                    continue;
                }
                EnsureCapital(f);
                TryExpand(f);
            }
            for (int i = 0; i < State.Settlements.Count; i++)
            {
                if (State.Settlements[i].Destroyed) continue;
                DailyFortune(State.Settlements[i]);
                DailyLeadership(State.Settlements[i]);
            }
            if (State.Seeded) LateArrivals();
            DailyPolitics();
            DailyExpeditions();
            DailyCampaigns();
            DailyMigration();
            DailyOutposts();
            DailyPlayerRelations();
            if (State.Chronicle.Count > Balance.ChronicleLimit)
            {
                State.Chronicle.RemoveRange(0, State.Chronicle.Count - Balance.ChronicleLimit);
            }
        }

        /// <summary>Small strokes of luck and misfortune so that no two settlements share a history.</summary>
        private void DailyFortune(Settlement s)
        {
            if (!_rng.Chance(0.035f * Balance.Pace)) return;
            int roll = _rng.Int(4);
            if (roll == 0)
            {
                float bonus = 15f + s.Population * 1.5f;
                s.Stock[(int)Res.Food] = Math.Min(StorageCap(s), s.Stock[(int)Res.Food] + bonus);
                Raise(new SimEvent { Kind = EventKind.GoodHarvest, FactionId = s.FactionId, SettlementId = s.Id, Args = new[] { s.Name } });
            }
            else if (roll == 1 && s.Population >= 10f)
            {
                s.Population -= Math.Max(1f, s.Population * _rng.Range(0.08f, 0.18f));
                s.Soldiers = Math.Min(s.Soldiers, s.Population);
                Raise(new SimEvent { Kind = EventKind.Sickness, FactionId = s.FactionId, SettlementId = s.Id, Args = new[] { s.Name } });
            }
            else if (roll == 2 && s.Population >= 6f)
            {
                // Watchtowers and soldiers blunt what comes out of the wilds.
                float loss = Math.Max(0f, _rng.Range(1f, 3f) - s.Soldiers * 0.3f - DefenseBonus(s) * 2f);
                if (loss < 0.5f) return;
                s.Population -= loss;
                s.Soldiers = Math.Max(0f, Math.Min(s.Soldiers - loss * 0.5f, s.Population));
                Raise(new SimEvent { Kind = EventKind.BeastAttack, FactionId = s.FactionId, SettlementId = s.Id, Args = new[] { s.Name } });
            }
            else if (roll == 3 && s.Population + 2f <= Housing(s))
            {
                s.Population += 2f;
                Raise(new SimEvent { Kind = EventKind.Newcomers, FactionId = s.FactionId, SettlementId = s.Id, Args = new[] { s.Name } });
            }
        }

        // ------------------------------------------------------------------ helpers

        internal void Raise(SimEvent e)
        {
            e.Day = State.Day;
            State.Chronicle.Add(e);
            Action<SimEvent> handler = EventRaised;
            if (handler != null) handler(e);
        }

        public int CountSettlements(int factionId)
        {
            int n = 0;
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed && s.FactionId == factionId) n++;
            }
            return n;
        }

        public int CountLiveSettlements()
        {
            int n = 0;
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed) n++;
            }
            return n;
        }

        private void EnsureCapital(Faction f)
        {
            Settlement cap = State.GetSettlement(f.CapitalId);
            if (cap != null && !cap.Destroyed && cap.FactionId == f.Id) return;
            Settlement best = null;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != f.Id || s.CampaignId != 0) continue;
                if (best == null || s.Population > best.Population) best = s;
            }
            if (best != null) f.CapitalId = best.Id;
        }

        public static int CountBuildings(Settlement s, BuildingKind kind, bool completeOnly)
        {
            int n = 0;
            foreach (Building b in s.Buildings)
            {
                if (b.Kind != kind || b.State == BuildingState.Destroyed) continue;
                if (completeOnly && b.State != BuildingState.Complete) continue;
                n++;
            }
            return n;
        }

        public static bool Has(Settlement s, BuildingKind kind)
        {
            return CountBuildings(s, kind, true) > 0;
        }

        public static float Housing(Settlement s)
        {
            float h = 0;
            foreach (Building b in s.Buildings)
            {
                if (b.State == BuildingState.Complete) h += Defs.Buildings[b.Kind].Housing;
            }
            return h;
        }

        public float StorageCap(Settlement s)
        {
            float cap = Balance.BaseStorage;
            foreach (Building b in s.Buildings)
            {
                if (b.State == BuildingState.Complete) cap += Defs.Buildings[b.Kind].Storage;
            }
            return cap;
        }

        public static float Boost(Settlement s, Res res)
        {
            float boost = 0;
            foreach (Building b in s.Buildings)
            {
                if (b.State != BuildingState.Complete) continue;
                BuildingDef def = Defs.Buildings[b.Kind];
                if (def.Boosts == res) boost += def.Boost;
            }
            return boost;
        }

        public static float DefenseBonus(Settlement s)
        {
            float d = 0;
            foreach (Building b in s.Buildings)
            {
                if (b.State == BuildingState.Complete) d += Defs.Buildings[b.Kind].Defense;
            }
            return d;
        }

        public static Building CurrentProject(Settlement s)
        {
            foreach (Building b in s.Buildings)
            {
                if (b.State == BuildingState.UnderConstruction) return b;
            }
            return null;
        }

        /// <summary>Stock level the settlement tries to keep of a resource.</summary>
        public float DesiredStock(Settlement s, Res res)
        {
            float cap = StorageCap(s);
            int tier = (int)s.Tier;
            float want;
            switch (res)
            {
                case Res.Wood: want = 90 + 60 * tier; break;
                case Res.Stone: want = 30 + 35 * tier; break;
                case Res.Food: want = s.Population * Balance.FoodPerPopPerDay * 8f + 20; break;
                case Res.Metal: want = Has(s, BuildingKind.Smithy) ? 20 + 12 * tier : 0; break;
                default: want = 50 + 50 * tier; break;
            }
            return Math.Min(cap, want);
        }

        /// <summary>The resource the settlement is shortest of; gifts of it are valued double.</summary>
        public Res NeededResource(Settlement s)
        {
            Res best = Res.Wood;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float desired = DesiredStock(s, (Res)i);
                if (desired <= 0) continue;
                float ratio = s.Stock[i] / desired;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = (Res)i;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ settlement step

        private void StepSettlement(Settlement s, float dt)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null) return;
            float pop = s.Population;
            float cap = StorageCap(s);
            ClampStock(s);

            // Soldiers are drawn from the population and do not gather.
            bool atWar = IsAtWarWithAnyone(f);
            float soldierTarget = pop >= 4 ? Util.Clamp(pop * (0.10f + 0.15f * f.Aggression + (atWar ? 0.08f : 0f)), 1f, pop * 0.4f) : 0f;
            // A road post is mostly a watch; a working outpost keeps one on guard.
            if (IsOutpost(s)) soldierTarget = s.RoadPost ? pop * 0.5f : Math.Min(1f, pop * 0.3f);
            s.Soldiers = Util.MoveToward(s.Soldiers, soldierTarget, dt);
            if (s.Soldiers > pop) s.Soldiers = pop;
            float workers = Math.Max(0f, pop - s.Soldiers);

            // Without a chief nobody starts anything new, and work goes slower.
            bool leaderless = IsLeaderless(s);
            if (leaderless) workers *= Balance.LeaderlessWork;
            Building project = CurrentProject(s);
            if (project == null && !leaderless && s.CampaignId == 0) project = StartProject(s, f);
            float builders = project != null ? Util.Clamp(workers * 0.3f, Math.Min(1f, workers), 4 + 2 * (int)s.Tier) : 0f;
            float gatherers = workers - builders;

            // Food comes first.
            float foodRate = Balance.FoodPerWorkerDay * Defs.Yield(s.Biome, Res.Food) * (1f + Boost(s, Res.Food)) * s.Fertility;
            float foodNeed = pop * Balance.FoodPerPopPerDay;
            float foodDays = s.Stock[(int)Res.Food] / Math.Max(0.1f, foodNeed);
            float foodEffort = foodDays < 4f ? 1.6f : (foodDays > 14f ? 0.7f : 1.15f);
            float foodWorkers = Util.Clamp(foodNeed * foodEffort / Math.Max(0.1f, foodRate), 0f, gatherers);
            float rest = gatherers - foodWorkers;

            float wWood = Math.Max(0.15f, 1f - s.Stock[(int)Res.Wood] / Math.Max(1f, DesiredStock(s, Res.Wood)));
            float wStone = Math.Max(0.10f, 1f - s.Stock[(int)Res.Stone] / Math.Max(1f, DesiredStock(s, Res.Stone)));
            float wMetal = Has(s, BuildingKind.Smithy) ? Math.Max(0.08f, 0.6f * (1f - s.Stock[(int)Res.Metal] / Math.Max(1f, DesiredStock(s, Res.Metal)))) : 0f;
            // An outpost out for ore digs it without a smithy.
            if (IsOutpost(s) && s.OutpostGoal == Res.Metal) wMetal = 1f;
            float wSum = wWood + wStone + wMetal;

            Produce(s, Res.Food, foodWorkers * foodRate * OutpostWork(s, Res.Food) * dt, cap);
            Produce(s, Res.Wood, rest * wWood / wSum * Balance.WoodPerWorkerDay * Defs.Yield(s.Biome, Res.Wood) * (1f + Boost(s, Res.Wood)) * s.Industry * OutpostWork(s, Res.Wood) * dt, cap);
            Produce(s, Res.Stone, rest * wStone / wSum * Balance.StonePerWorkerDay * Defs.Yield(s.Biome, Res.Stone) * (1f + Boost(s, Res.Stone)) * s.Industry * OutpostWork(s, Res.Stone) * dt, cap);
            if (wMetal > 0f)
            {
                Produce(s, Res.Metal, rest * wMetal / wSum * Balance.MetalPerWorkerDay * Defs.Yield(s.Biome, Res.Metal) * OutpostWork(s, Res.Metal) * dt, cap);
            }
            float income = pop * 0.2f + (Has(s, BuildingKind.Market) ? 2f * (1 + (int)s.Tier) * (0.5f + f.Mercantile) : 0f);
            Produce(s, Res.Coins, income * dt, cap * 4f);

            // Eating, starving, growing.
            s.Stock[(int)Res.Food] -= foodNeed * dt;
            if (s.Stock[(int)Res.Food] < 0f)
            {
                s.Stock[(int)Res.Food] = 0f;
                float hungerBefore = s.HungerDays;
                s.HungerDays += dt;
                s.Population = Math.Max(0f, pop - Math.Max(0.3f, pop * 0.06f) * dt);
                if (Math.Floor(hungerBefore / 4f) < Math.Floor(s.HungerDays / 4f) || (hungerBefore < 1f && s.HungerDays >= 1f))
                {
                    Raise(new SimEvent { Kind = EventKind.Starvation, FactionId = f.Id, SettlementId = s.Id, Args = new[] { s.Name } });
                }
                if (s.Population < 1.5f && s.HungerDays > 3f)
                {
                    DestroySettlement(s, 0);
                    return;
                }
            }
            else
            {
                s.HungerDays = Math.Max(0f, s.HungerDays - dt * 2f);
                float housing = Math.Min(Housing(s), Defs.PopCapByTier[(int)s.Tier]);
                if (IsOutpost(s)) housing = Math.Min(housing, OutpostPeople);
                if (foodDays > 2f && pop < housing && s.CampaignId == 0)
                {
                    float growth = Math.Max(Balance.MinGrowthPerDay, pop * Balance.GrowthPerDay) * dt * (foodDays > 6f ? 1f : 0.5f) * s.Fertility;
                    s.Population = Math.Min(housing, pop + growth);
                }
            }

            // Construction.
            if (project != null && builders > 0f)
            {
                project.Work += builders * dt * 24f;
                BuildingDef def = Defs.Buildings[project.Kind];
                if (project.Work >= def.Work)
                {
                    project.Work = def.Work;
                    project.State = BuildingState.Complete;
                    Raise(new SimEvent { Kind = EventKind.BuildingCompleted, FactionId = f.Id, SettlementId = s.Id, BuildingId = project.Id, Args = new[] { s.Name, project.Kind.ToString() } });
                }
            }

            KeepOutpostSmall(s);
            if (s.Tier < Tier.City && !IsOutpost(s) && Defs.TierRequirementsMet(s, s.Tier + 1))
            {
                s.Tier = s.Tier + 1;
                Raise(new SimEvent { Kind = EventKind.TierUp, FactionId = f.Id, SettlementId = s.Id, Args = new[] { s.Name, s.Tier.ToString() } });
            }
        }

        /// <summary>Goods beyond what the stores can hold are lost (a storehouse burned, say).</summary>
        internal void ClampStock(Settlement s)
        {
            float cap = StorageCap(s);
            for (int i = 0; i < 4; i++)
            {
                if (s.Stock[i] > cap) s.Stock[i] = cap;
            }
        }

        private static void Produce(Settlement s, Res res, float amount, float cap)
        {
            if (amount <= 0f) return;
            int i = (int)res;
            if (s.Stock[i] >= cap) return;
            s.Stock[i] = Math.Min(cap, s.Stock[i] + amount);
        }

        // ------------------------------------------------------------------ construction planning

        private Building StartProject(Settlement s, Faction f)
        {
            // Ruins are cleared first, once the game side has removed their pieces.
            for (int i = s.Buildings.Count - 1; i >= 0; i--)
            {
                Building ruin = s.Buildings[i];
                if (ruin.State == BuildingState.Destroyed && ruin.SpawnedPieces == 0) s.Buildings.RemoveAt(i);
            }

            BuildingKind? choice = ChooseProject(s, f);
            if (!choice.HasValue) return null;
            BuildingDef def = Defs.Buildings[choice.Value];
            if (s.Stock[(int)Res.Wood] < def.Wood || s.Stock[(int)Res.Stone] < def.Stone || s.Stock[(int)Res.Metal] < def.Metal) return null;

            int plot = -1;
            if (def.UsesPlot)
            {
                plot = FindFreePlot(s);
                if (plot < 0) return null;
            }

            s.Stock[(int)Res.Wood] -= def.Wood;
            s.Stock[(int)Res.Stone] -= def.Stone;
            s.Stock[(int)Res.Metal] -= def.Metal;
            Building b = new Building { Id = s.NextBuildingId++, Kind = choice.Value, Plot = plot, State = BuildingState.UnderConstruction };
            s.Buildings.Add(b);
            Raise(new SimEvent { Kind = EventKind.BuildingStarted, FactionId = f.Id, SettlementId = s.Id, BuildingId = b.Id, Args = new[] { s.Name, b.Kind.ToString() } });
            return b;
        }

        private int FindFreePlot(Settlement s)
        {
            // The plots of the settlement's size first; if not one of them is dry and level enough
            // (a site on a shore or a slope), the next ring out, or it would never build at all.
            // Plots that can be walked to from the fire come first: a house on a ledge nobody can climb to
            // is no part of the village. Only a settlement on rough ground with no such plot left takes a steeper one.
            int count = Defs.PlotCount(s.Tier);
            int outer = s.Tier >= Tier.City ? count : Defs.PlotCount(s.Tier + 1);
            int plot = FreePlot(s, 0, count, false);
            if (plot < 0) plot = FreePlot(s, count, outer, false);
            if (plot < 0) plot = FreePlot(s, 0, count, true);
            if (plot < 0) plot = FreePlot(s, count, outer, true);
            return plot;
        }

        private int FreePlot(Settlement s, int from, int count, bool lenient)
        {
            for (int plot = from; plot < count; plot++)
            {
                bool taken = false;
                foreach (Building b in s.Buildings)
                {
                    if (b.Plot == plot)
                    {
                        taken = true;
                        break;
                    }
                }
                if (taken) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(s, plot, out offset, out yaw);
                Biome biome;
                if (World.ProbeSite(s.Pos + offset, 6f, out biome) && World.GentleWay(s.Pos, s.Pos + offset, lenient)) return plot;
            }
            return -1;
        }

        private bool CanAdd(Settlement s, BuildingKind kind)
        {
            BuildingDef def = Defs.Buildings[kind];
            // A road post may have its tower while still a camp.
            if (s.Tier < def.MinTier && !(IsOutpost(s) && kind == BuildingKind.Watchtower)) return false;
            int max = def.MaxByTier[(int)s.Tier];
            if (max >= 0 && CountBuildings(s, kind, false) >= max) return false;

            // A faction has one seat of power and only a few market towns; the rest stay villages.
            if (kind == BuildingKind.GreatHall)
            {
                Faction f = State.GetFaction(s.FactionId);
                return f != null && f.CapitalId == s.Id;
            }
            if (kind == BuildingKind.Market)
            {
                int markets = 0, settlements = 0;
                foreach (Settlement other in State.Settlements)
                {
                    if (other.Destroyed || other.FactionId != s.FactionId) continue;
                    settlements++;
                    if (CountBuildings(other, BuildingKind.Market, false) > 0) markets++;
                }
                return markets < 1 + settlements / 3;
            }
            return true;
        }

        private BuildingKind? ChooseProject(Settlement s, Faction f)
        {
            if (IsOutpost(s)) return OutpostProject(s);
            float housing = Housing(s);
            float pop = s.Population;

            // 1. People need roofs.
            if (housing - pop < 1.5f)
            {
                BuildingKind? home = BestHousing(s);
                if (home.HasValue) return home;
            }

            // 2. Somewhere to keep goods.
            int stores = CountBuildings(s, BuildingKind.Storehouse, false);
            if (CanAdd(s, BuildingKind.Storehouse))
            {
                bool full = false;
                float cap = StorageCap(s);
                for (int i = 0; i < 4; i++)
                {
                    if (s.Stock[i] >= cap * 0.85f) full = true;
                }
                if ((stores == 0 && s.Buildings.Count >= 2) || full) return BuildingKind.Storehouse;
            }

            // 3. Whatever the next tier demands.
            if (s.Tier < Tier.City)
            {
                Tier next = s.Tier + 1;
                if (next == Tier.Village && pop >= 10 && CanAdd(s, BuildingKind.Watchtower) && CountBuildings(s, BuildingKind.Watchtower, false) == 0) return BuildingKind.Watchtower;
                if (next == Tier.Town && pop >= 18)
                {
                    if (CanAdd(s, BuildingKind.Smithy) && CountBuildings(s, BuildingKind.Smithy, false) == 0) return BuildingKind.Smithy;
                    if (CanAdd(s, BuildingKind.Market) && CountBuildings(s, BuildingKind.Market, false) == 0) return BuildingKind.Market;
                }
                if (next == Tier.City && pop >= 30)
                {
                    if (CanAdd(s, BuildingKind.Palisade) && CountBuildings(s, BuildingKind.Palisade, false) == 0) return BuildingKind.Palisade;
                    if (CanAdd(s, BuildingKind.GreatHall) && CountBuildings(s, BuildingKind.GreatHall, false) == 0) return BuildingKind.GreatHall;
                }
            }

            // 4. A workshop for the scarcest resource, if it is actually short.
            Res needed = NeededResource(s);
            bool shortOf = s.Stock[(int)needed] < DesiredStock(s, needed) * 0.6f;
            BuildingKind? work = null;
            if (!shortOf) { }
            else if (needed == Res.Food)
            {
                bool wild = s.Biome == Biome.BlackForest || s.Biome == Biome.Mountain || s.Biome == Biome.Swamp;
                BuildingKind first = wild ? BuildingKind.HunterLodge : BuildingKind.Farm;
                BuildingKind second = wild ? BuildingKind.Farm : BuildingKind.HunterLodge;
                work = CanAdd(s, first) ? first : (CanAdd(s, second) ? second : (BuildingKind?)null);
            }
            else if (needed == Res.Wood && CanAdd(s, BuildingKind.Woodcutter)) work = BuildingKind.Woodcutter;
            else if (needed == Res.Stone && CanAdd(s, BuildingKind.Stonecutter)) work = BuildingKind.Stonecutter;
            if (work.HasValue) return work;

            // 5. Round out the remaining workshops, then defences.
            BuildingKind[] filler = { BuildingKind.Woodcutter, BuildingKind.Farm, BuildingKind.Stonecutter, BuildingKind.HunterLodge };
            foreach (BuildingKind kind in filler)
            {
                if (CanAdd(s, kind) && CountBuildings(s, kind, false) == 0) return kind;
            }
            if (CanAdd(s, BuildingKind.Watchtower) && (f.Aggression > 0.5f || IsAtWarWithAnyone(f))) return BuildingKind.Watchtower;

            // 6. Build ahead of population growth.
            if (housing - pop < 4f) return BestHousing(s);
            return null;
        }

        private BuildingKind? BestHousing(Settlement s)
        {
            if (Housing(s) >= Defs.PopCapByTier[(int)s.Tier] + 2f) return null;
            if (s.Tier >= Tier.Village && CountBuildings(s, BuildingKind.House, false) >= 2 && CanAdd(s, BuildingKind.Longhouse)) return BuildingKind.Longhouse;
            if (s.Tier >= Tier.Hamlet && CountBuildings(s, BuildingKind.Hut, false) >= 2 && CanAdd(s, BuildingKind.House)) return BuildingKind.House;
            if (CanAdd(s, BuildingKind.Hut)) return BuildingKind.Hut;
            if (CanAdd(s, BuildingKind.House)) return BuildingKind.House;
            return null;
        }

        // ------------------------------------------------------------------ founding and expansion

        public void Seed()
        {
            if (State.Seeded) return;
            _rng = new Rng(State.RngState);
            State.SeedAngle = _rng.Range(0f, (float)(Math.PI * 2));
            foreach (CultureDef culture in Defs.Cultures) TryFoundFaction(culture, 400);
            State.Seeded = true;
            State.RngState = _rng.State;
        }

        /// <summary>Cultures that found no room at first keep looking and arrive later as newcomers.</summary>
        private void LateArrivals()
        {
            foreach (CultureDef culture in Defs.Cultures)
            {
                bool present = false;
                foreach (Faction f in State.Factions)
                {
                    if (f.CultureId == culture.Id) present = true;
                }
                if (!present) TryFoundFaction(culture, 40);
            }
        }

        private bool TryFoundFaction(CultureDef culture, int attempts)
        {
            bool human = culture.Group == "human";
            if (!human && !Balance.EnableRaceFactions) return false;
            if (CountLiveSettlements() >= Balance.MaxSettlementsTotal) return false;

            // Human clans start in different directions from the world centre.
            int index = Defs.Cultures.IndexOf(culture);
            float preferredAngle = human ? State.SeedAngle + index * (float)(Math.PI * 2 / 3) : float.NaN;
            Vec2 site;
            Biome biome;
            if (!FindStartSite(culture, preferredAngle, attempts, out site, out biome)) return false;

            Faction f = new Faction
            {
                Id = State.NextFactionId++,
                CultureId = culture.Id,
                Name = culture.NameEn,
                Aggression = Util.Clamp01(culture.Aggression + _rng.Range(-0.1f, 0.1f)),
                Expansionism = Util.Clamp01(culture.Expansionism + _rng.Range(-0.1f, 0.1f)),
                Mercantile = Util.Clamp01(culture.Mercantile + _rng.Range(-0.1f, 0.1f)),
                Honor = Util.Clamp01(culture.Honor + _rng.Range(-0.1f, 0.1f)),
                ColorIndex = index
            };
            State.Factions.Add(f);
            Raise(new SimEvent { Kind = EventKind.FactionFounded, FactionId = f.Id, Args = new[] { f.Name } });
            Settlement s = FoundSettlement(f, site, biome);
            f.CapitalId = s.Id;
            return true;
        }

        private bool FindStartSite(CultureDef culture, float preferredAngle, int attempts, out Vec2 site, out Biome biome)
        {
            for (int n = 0; n < attempts; n++)
            {
                // A full search widens as it fails; a short daily retry samples the whole widened range.
                int attempt = attempts >= 400 ? n : _rng.Int(400);
                float widen = 1f + attempt / 100f;
                float maxDist = Math.Min(World.WorldRadius * 0.9f, culture.StartMaxDist * widen);
                float dist = _rng.Range(culture.StartMinDist, maxDist);
                float spread = float.IsNaN(preferredAngle) ? (float)Math.PI : 0.6f + attempt * 0.02f;
                float angle = (float.IsNaN(preferredAngle) ? 0f : preferredAngle) + _rng.Range(-spread, spread);
                site = Vec2.FromAngle(angle, dist);
                if (IsGoodSite(site, culture.HomeBiomes, 0, out biome)) return true;
            }
            site = default(Vec2);
            biome = Biome.Other;
            return false;
        }

        private bool IsGoodSite(Vec2 site, Biome[] allowed, int factionId, out Biome biome)
        {
            if (!World.ProbeSite(site, 22f, out biome)) return false;
            if (Array.IndexOf(allowed, biome) < 0) return false;
            if (World.IsReserved(site, 40f)) return false;
            foreach (Settlement other in State.Settlements)
            {
                if (other.Destroyed) continue;
                float min = other.FactionId == factionId ? Balance.MinSettlementSpacing : Balance.ForeignSpacing;
                if (Vec2.Distance(other.Pos, site) < min) return false;
            }
            return true;
        }

        private Settlement FoundSettlement(Faction f, Vec2 site, Biome biome, bool announce = true)
        {
            CultureDef culture = Defs.Culture(f.CultureId);
            Settlement s = new Settlement
            {
                Id = State.NextSettlementId++,
                FactionId = f.Id,
                Name = MakeSettlementName(culture),
                Pos = site,
                Biome = biome,
                Tier = Tier.Camp,
                Population = 4f + _rng.Int(4),
                Soldiers = 1f,
                Fertility = _rng.Range(0.8f, 1.25f),
                Industry = _rng.Range(0.8f, 1.25f),
                Seed = (int)(_rng.NextU64() & 0x7FFFFFFF),
                FoundedDay = State.Day,
                LastExpansionDay = State.Day,
                Layout = 1
            };
            s.Stock[(int)Res.Wood] = _rng.Range(45f, 80f);
            s.Stock[(int)Res.Stone] = _rng.Range(8f, 25f);
            s.Stock[(int)Res.Food] = _rng.Range(35f, 55f);
            s.Stock[(int)Res.Coins] = 20f;
            s.Buildings.Add(new Building { Id = s.NextBuildingId++, Kind = BuildingKind.Campfire, State = BuildingState.Complete });
            State.Settlements.Add(s);
            if (announce) Raise(new SimEvent { Kind = EventKind.SettlementFounded, FactionId = f.Id, SettlementId = s.Id, Args = new[] { f.Name, s.Name } });
            return s;
        }

        /// <summary>Founds a settlement at an exact spot, skipping the site checks (testing and scenarios).</summary>
        public Settlement DebugFound(int factionId, Vec2 site, Biome biome)
        {
            Faction f = State.GetFaction(factionId);
            if (f == null) return null;
            _rng = new Rng(State.RngState);
            Settlement s = FoundSettlement(f, site, biome);
            f.Eliminated = false;
            State.RngState = _rng.State;
            return s;
        }

        /// <summary>Removes a settlement outright (testing and clean-up).</summary>
        public bool DebugDestroy(int settlementId)
        {
            Settlement s = State.GetSettlement(settlementId);
            if (s == null || s.Destroyed) return false;
            DestroySettlement(s, 0);
            return true;
        }

        /// <summary>Puts a place in another faction's hands at once, as a won assault would (testing).</summary>
        public string DebugCapture(int settlementId, int factionId)
        {
            Settlement s = State.GetSettlement(settlementId);
            Faction owner = State.GetFaction(factionId);
            if (s == null || s.Destroyed || owner == null) return "no such settlement or faction";
            Faction defender = State.GetFaction(s.FactionId);
            if (defender == null || defender.Id == owner.Id) return "it already belongs to them";
            CaptureSettlement(s, owner, defender, 3f);
            return s.Name + " taken by " + owner.Name;
        }

        private string MakeSettlementName(CultureDef culture)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                string name = culture.SettlementPrefixes[_rng.Int(culture.SettlementPrefixes.Length)] + culture.SettlementSuffixes[_rng.Int(culture.SettlementSuffixes.Length)];
                bool used = false;
                foreach (Settlement s in State.Settlements)
                {
                    if (s.Name == name) used = true;
                }
                if (!used) return name;
            }
            return culture.SettlementPrefixes[0] + culture.SettlementSuffixes[0] + " " + State.NextSettlementId;
        }

        private void TryExpand(Faction f)
        {
            if (CountTowns(f.Id) >= Balance.MaxSettlementsPerFaction) return;
            if (CountLiveSettlements() >= Balance.MaxSettlementsTotal) return;
            CultureDef culture = Defs.Culture(f.CultureId);
            Biome[] allowed = culture.ExpandBiomes ?? culture.HomeBiomes;
            float cooldown = Balance.ExpansionCooldownDays / (0.5f + f.Expansionism) / Math.Max(0.1f, Balance.Pace);

            int count = State.Settlements.Count;
            for (int i = 0; i < count; i++)
            {
                Settlement s = State.Settlements[i];
                if (s.Destroyed || s.FactionId != f.Id || s.CampaignId != 0) continue;
                if (s.Tier < Tier.Hamlet || s.Population < 12f) continue;
                if (s.Stock[(int)Res.Food] < 55f || s.Stock[(int)Res.Wood] < 70f) continue;
                if (State.Day - s.LastExpansionDay < cooldown) continue;
                if (!_rng.Chance(0.15f + 0.35f * f.Expansionism)) continue;

                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Vec2 site = s.Pos + Vec2.FromAngle(_rng.Range(0f, (float)(Math.PI * 2)), _rng.Range(Balance.ExpansionMinDist, Balance.ExpansionMaxDist));
                    Biome biome;
                    if (!IsGoodSite(site, allowed, f.Id, out biome)) continue;
                    s.Population -= 5f;
                    s.Soldiers = Math.Min(s.Soldiers, s.Population);
                    s.Stock[(int)Res.Food] -= 45f;
                    s.Stock[(int)Res.Wood] -= 60f;
                    s.LastExpansionDay = State.Day;
                    FoundSettlement(f, site, biome);
                    return;
                }
                // No room around this settlement; do not retry every day.
                s.LastExpansionDay = State.Day - cooldown * 0.5f;
            }
        }

        internal void DestroySettlement(Settlement s, int byFactionId)
        {
            s.Destroyed = true;
            s.Population = 0f;
            s.Soldiers = 0f;
            foreach (Building b in s.Buildings)
            {
                if (b.State != BuildingState.Destroyed) b.State = BuildingState.Destroyed;
            }
            ClampStock(s);
            Faction f = State.GetFaction(s.FactionId);
            Raise(new SimEvent { Kind = EventKind.SettlementDestroyed, FactionId = s.FactionId, OtherFactionId = byFactionId, SettlementId = s.Id, Args = new[] { s.Name, f != null ? f.Name : "?" } });
        }
    }
}
