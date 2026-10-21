using System;

namespace LivingFactions.Core
{
    /// <summary>
    /// Outposts: a handful of a settlement's people out where the land gives more of what home lacks
    /// (a stand of good timber, a stony slope, rich soil, ore), or a watch post halfway along a long road
    /// between two of the faction's places. An outpost stays small, never grows into a village, works
    /// mostly at its one thing and sends what it gathers home in loads; a road post keeps more soldiers
    /// and makes the way safer for the faction's bands.
    /// </summary>
    public partial class Simulation
    {
        public const int MaxOutpostsPerFaction = 2;
        public const float OutpostPeople = 6f;

        public static bool IsOutpost(Settlement s) { return s.ParentId != 0; }

        /// <summary>Settlements that count as the faction's towns and villages: outposts and war camps aside.</summary>
        public int CountTowns(int factionId)
        {
            int n = 0;
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed && s.FactionId == factionId && !IsOutpost(s) && s.CampaignId == 0) n++;
            }
            return n;
        }

        private int CountOutposts(int factionId)
        {
            int n = 0;
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed && s.FactionId == factionId && IsOutpost(s)) n++;
            }
            return n;
        }

        /// <summary>How much of a good an outpost's people get done against a village's: its own thing more, the rest little.</summary>
        private static float OutpostWork(Settlement s, Res res)
        {
            if (!IsOutpost(s) || res == Res.Food && s.OutpostGoal != Res.Food) return 1f;
            if (s.RoadPost) return 0.5f;
            return res == s.OutpostGoal ? 1.6f : 0.4f;
        }

        /// <summary>An outpost builds a hut or two, the workshop of its trade, and a road post a tower.</summary>
        private BuildingKind? OutpostProject(Settlement s)
        {
            if (Housing(s) - s.Population < 1.5f && CountBuildings(s, BuildingKind.Hut, false) < 2) return BuildingKind.Hut;
            if (s.RoadPost) return CountBuildings(s, BuildingKind.Watchtower, false) == 0 ? BuildingKind.Watchtower : (BuildingKind?)null;
            BuildingKind trade = s.OutpostGoal == Res.Stone ? BuildingKind.Stonecutter : s.OutpostGoal == Res.Food ? BuildingKind.Farm : BuildingKind.Woodcutter;
            if (s.OutpostGoal == Res.Metal) return null;
            return CountBuildings(s, trade, false) == 0 ? trade : (BuildingKind?)null;
        }

        private void DailyOutposts()
        {
            if (!Balance.Outposts) return;
            // What the outposts have gathered goes home.
            for (int i = 0; i < State.Settlements.Count; i++)
            {
                Settlement o = State.Settlements[i];
                if (o.Destroyed || !IsOutpost(o) || o.RoadPost) continue;
                Settlement home = State.GetSettlement(o.ParentId);
                if (home == null || home.Destroyed || home.FactionId != o.FactionId)
                {
                    home = NearestTown(o.FactionId, o.Pos);
                    if (home == null) continue;
                    o.ParentId = home.Id;
                }
                int r = (int)o.OutpostGoal;
                float load = o.Stock[r] - 10f;
                if (load < 12f || o.Population < 2f) continue;
                bool out_ = false;
                foreach (Squad q in State.Squads) if (q.HomeId == o.Id && q.Carrier && q.State != SquadState.Done) out_ = true;
                if (out_) continue;
                Squad carrier = Muster(o, SquadKind.Reinforcement, 1f, 0f, home.Pos, 2f * Vec2.Distance(o.Pos, home.Pos) / Balance.SquadSpeed);
                carrier.TargetSettlementId = home.Id;
                carrier.Carrier = true;
                carrier.Civilians = true;
                carrier.Cargo[r] = load;
                o.Stock[r] -= load;
            }

            foreach (Faction f in State.Factions)
            {
                if (f.Eliminated || State.Day - f.LastOutpostDay < 8.0 || CountOutposts(f.Id) >= MaxOutpostsPerFaction) continue;
                if (CountLiveSettlements() >= Balance.MaxSettlementsTotal) break;
                if (!_rng.Chance(0.25f)) continue;
                f.LastOutpostDay = State.Day;
                if (!TryRoadPost(f)) TryResourceOutpost(f);
            }
        }

        /// <summary>An outpost holds only so many: whoever comes back beyond that (a carrier, men from a fight) goes on home.</summary>
        private void KeepOutpostSmall(Settlement o)
        {
            if (!IsOutpost(o) || o.Population <= OutpostPeople) return;
            float extra = o.Population - OutpostPeople;
            o.Population = OutpostPeople;
            o.Soldiers = Math.Min(o.Soldiers, o.Population);
            Settlement home = State.GetSettlement(o.ParentId);
            if (home != null && !home.Destroyed && home.FactionId == o.FactionId) home.Population += extra;
        }

        private Settlement NearestTown(int factionId, Vec2 pos)
        {
            Settlement best = null;
            float bestD = float.MaxValue;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != factionId || IsOutpost(s) || s.CampaignId != 0) continue;
                float d = Vec2.Distance(s.Pos, pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>The place that can best spare a few people for an outpost, or null.</summary>
        private Settlement OutpostParent(Faction f)
        {
            Settlement best = null;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != f.Id || IsOutpost(s) || s.CampaignId != 0 || s.Tier < Tier.Village || IsLeaderless(s)) continue;
                if (s.Population < 14f || s.Stock[(int)Res.Food] < 40f || s.Stock[(int)Res.Wood] < 40f) continue;
                if (best == null || s.Population > best.Population) best = s;
            }
            return best;
        }

        /// <summary>Where the land gives much more of what home lacks, within a day's walk: a few people go to work it.</summary>
        private bool TryResourceOutpost(Faction f)
        {
            Settlement home = OutpostParent(f);
            if (home == null) return false;
            Res need = NeededResource(home);
            if (need == Res.Coins) return false;
            Vec2 best = home.Pos;
            Biome bestBiome = home.Biome;
            float bestScore = Defs.Yield(home.Biome, need) * 1.4f;
            for (int k = 0; k < 12; k++)
            {
                Vec2 p = home.Pos + Vec2.FromAngle(_rng.Range(0f, 6.28f), _rng.Range(300f, 1000f));
                Biome b;
                if (!World.ProbeSite(p, 14f, out b) || b == Biome.Ocean || b == Biome.Other || World.IsReserved(p, 30f) || Crowded(p, 250f)) continue;
                float score = Defs.Yield(b, need) * (1f - Danger(p, b, f.Id) * 0.5f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    bestBiome = b;
                }
            }
            if (best.X == home.Pos.X && best.Z == home.Pos.Z) return false;
            Settlement o = FoundOutpost(f, home, best, bestBiome, need, false);
            Raise(new SimEvent { Kind = EventKind.OutpostFounded, FactionId = f.Id, SettlementId = o.Id, OtherSettlementId = home.Id, Args = new[] { f.Name, o.Name, need.ToString(), home.Name } });
            return true;
        }

        /// <summary>Halfway along a long way between two of the faction's places, with nothing of theirs near: a watch post.</summary>
        private bool TryRoadPost(Faction f)
        {
            Settlement home = OutpostParent(f);
            if (home == null) return false;
            foreach (Settlement other in State.Settlements)
            {
                if (other == home || other.Destroyed || other.FactionId != f.Id || IsOutpost(other) || other.CampaignId != 0) continue;
                float d = Vec2.Distance(home.Pos, other.Pos);
                if (d < 900f || d > 2600f) continue;
                Vec2 mid = (home.Pos + other.Pos) * 0.5f;
                if (OwnNear(f.Id, mid, 350f)) continue;
                for (int k = 0; k < 8; k++)
                {
                    Vec2 p = mid + Vec2.FromAngle(_rng.Range(0f, 6.28f), _rng.Range(0f, 120f));
                    Biome b;
                    if (!World.ProbeSite(p, 12f, out b) || World.IsReserved(p, 30f) || Crowded(p, 250f)) continue;
                    Settlement o = FoundOutpost(f, home, p, b, Res.Wood, true);
                    Raise(new SimEvent { Kind = EventKind.OutpostFounded, FactionId = f.Id, SettlementId = o.Id, OtherSettlementId = home.Id, Args = new[] { f.Name, o.Name, "road", home.Name } });
                    return true;
                }
            }
            return false;
        }

        private Settlement FoundOutpost(Faction f, Settlement home, Vec2 site, Biome biome, Res goal, bool road)
        {
            Settlement o = FoundSettlement(f, site, biome, false);
            o.ParentId = home.Id;
            o.OutpostGoal = goal;
            o.RoadPost = road;
            o.Population = 3f;
            o.Soldiers = road ? 2f : 1f;
            for (int i = 0; i < 5; i++) o.Stock[i] *= 0.5f;
            home.Population -= 3f;
            home.Soldiers = Math.Min(home.Soldiers, home.Population);
            home.Stock[(int)Res.Food] -= 20f;
            home.Stock[(int)Res.Wood] -= 30f;
            return o;
        }

        private bool OwnNear(int factionId, Vec2 p, float range)
        {
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed && s.FactionId == factionId && Vec2.Distance(s.Pos, p) < range) return true;
            }
            return false;
        }

        private bool Crowded(Vec2 p, float range)
        {
            foreach (Settlement s in State.Settlements)
            {
                if (!s.Destroyed && Vec2.Distance(s.Pos, p) < range) return true;
            }
            return false;
        }
    }
}
