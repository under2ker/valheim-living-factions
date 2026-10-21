using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    public enum CampaignStage { Scouting, Camp, Assault, Holding, Over }

    /// <summary>
    /// A war aim pursued over days instead of a single raid: scouts go to look at the target, a war
    /// camp is set up on the way, the main band marches from the camp, and what is taken is held or the
    /// camp is given up. Each step costs men and food, so campaigns are rare and a faction that cannot
    /// pay for one does not start it.
    /// </summary>
    public class Campaign
    {
        public int Id;
        public int FactionId;
        public int EnemyId;
        public int HomeId;
        public int TargetId;
        public CampaignStage Stage;
        public double StageSinceDay;
        public double BegunDay;
        /// <summary>The war camp (a settlement with CampaignId set), 0 until it is up.</summary>
        public int CampId;
        /// <summary>What the scouts reckoned the defenders to be; -1 until they are back.</summary>
        public float EnemyEstimate = -1f;
        public int Attempts;
        public int SquadId;
        /// <summary>When the side that lost the place strikes back; -1 until the place is taken.</summary>
        public double CounterDay = -1;
        public bool Countered;
        public int CounterSquadId;
        /// <summary>Allied men who went into the assault with the band, by the allied band they came from.</summary>
        public List<AllyShare> Allies = new List<AllyShare>();
        /// <summary>Why it ended (End's reason), for the record and for testing.</summary>
        public string Why;
    }

    public class AllyShare
    {
        public int SquadId;
        public float Men;
    }

    public partial class Simulation
    {
        public Campaign CampaignOf(int factionId)
        {
            foreach (Campaign c in State.Campaigns) if (c.FactionId == factionId && c.Stage != CampaignStage.Over) return c;
            return null;
        }

        public Campaign GetCampaign(int id)
        {
            foreach (Campaign c in State.Campaigns) if (c.Id == id) return c;
            return null;
        }

        private void DailyCampaigns()
        {
            if (!Balance.Campaigns || !Balance.EnableWars) return;
            foreach (Faction f in State.Factions)
            {
                if (f.Eliminated || CampaignOf(f.Id) != null) continue;
                TryBeginCampaign(f);
            }
            for (int i = 0; i < State.Campaigns.Count; i++) StepCampaign(State.Campaigns[i]);
            // Finished campaigns are kept a while for the record, then dropped.
            State.Campaigns.RemoveAll(c => c.Stage == CampaignStage.Over && State.Day - c.StageSinceDay > 10.0);
        }

        /// <summary>A warlike faction at war, with men and food to spare, picks the nearest enemy place within reach.</summary>
        private void TryBeginCampaign(Faction f)
        {
            if (f.Aggression < 0.35f || !_rng.Chance(0.15f + 0.3f * f.Aggression)) return;
            Settlement home = null;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != f.Id || s.CampaignId != 0 || s.Soldiers < Balance.CampaignMinSoldiers) continue;
                if (s.Stock[(int)Res.Food] < 40f) continue;
                if (home == null || s.Soldiers > home.Soldiers) home = s;
            }
            if (home == null) return;
            Settlement target = null;
            float best = Balance.CampaignRange;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId == f.Id || !AreAtWar(f.Id, s.FactionId)) continue;
                float d = Vec2.Distance(s.Pos, home.Pos);
                if (d < best)
                {
                    best = d;
                    target = s;
                }
            }
            if (target == null) return;
            Campaign c = new Campaign
            {
                Id = State.NextCampaignId++,
                FactionId = f.Id,
                EnemyId = target.FactionId,
                HomeId = home.Id,
                TargetId = target.Id,
                Stage = CampaignStage.Scouting,
                StageSinceDay = State.Day,
                BegunDay = State.Day,
            };
            State.Campaigns.Add(c);
            Squad scouts = Muster(home, SquadKind.Scouts, 1f, 1f, target.Pos, 2f * best / Balance.SquadSpeed);
            scouts.TargetSettlementId = target.Id;
            scouts.CampaignId = c.Id;
            c.SquadId = scouts.Id;
            Raise(new SimEvent { Kind = EventKind.CampaignBegun, FactionId = f.Id, OtherFactionId = target.FactionId, SettlementId = target.Id, OtherSettlementId = home.Id, Args = new[] { f.Name, target.Name, home.Name } });
        }

        private void StepCampaign(Campaign c)
        {
            if (c.Stage == CampaignStage.Over) return;
            Settlement target = State.GetSettlement(c.TargetId);
            Settlement home = State.GetSettlement(c.HomeId);
            Settlement camp = c.CampId != 0 ? State.GetSettlement(c.CampId) : null;
            bool war = AreAtWar(c.FactionId, c.EnemyId);
            if (home == null || home.Destroyed || home.FactionId != c.FactionId)
            {
                End(c, "home");
                return;
            }
            if (target == null || target.Destroyed)
            {
                End(c, "target");
                return;
            }
            // The place changed hands: it is no longer the enemy's, so there is nothing here to take.
            // A campaign in its assault may have just won the place itself (the assault branch below
            // turns that into the holding stage); a campaign holding a place is watched by its own stage.
            if (c.Stage != CampaignStage.Holding
                && !(c.Stage == CampaignStage.Assault && target.FactionId == c.FactionId)
                && target.FactionId != c.EnemyId)
            {
                End(c, target.FactionId == c.FactionId ? "ours" : "other");
                return;
            }
            if (!war && c.Stage != CampaignStage.Holding)
            {
                End(c, "peace");
                return;
            }
            if (c.CampId != 0 && (camp == null || camp.Destroyed || camp.FactionId != c.FactionId) && c.Stage != CampaignStage.Holding)
            {
                // The camp was taken or burned: the campaign is lost.
                End(c, "camp");
                return;
            }

            // A campaign wears a people out: men away, food sent after them. It hastens the peace.
            Faction self = State.GetFaction(c.FactionId), enemy = State.GetFaction(c.EnemyId);
            Relation r;
            if (self != null && enemy != null && self.Relations.TryGetValue(enemy.Id, out r) && c.Stage != CampaignStage.Scouting) r.Weariness += 0.4f;

            switch (c.Stage)
            {
                case CampaignStage.Scouting:
                    // The scouts' report moves it on (ScoutsReport). Scouts that never come back end it,
                    // whether they fell before they saw the place or on the way home after seeing it:
                    // then there is no report and no one to carry the campaign on, and it must not wait
                    // for peace to end.
                    if (!SquadAlive(c.SquadId)) End(c, "scouts");
                    break;
                case CampaignStage.Camp:
                    SupplyLine(c, home, camp);
                    if (State.Day - c.StageSinceDay >= Balance.CampDays) LaunchAssault(c, camp, target);
                    break;
                case CampaignStage.Assault:
                    if (SquadAlive(c.SquadId)) break;
                    c.Allies.Clear();
                    if (target.FactionId == c.FactionId)
                    {
                        SetStage(c, CampaignStage.Holding);
                        // The side that lost it gathers to strike back, a day or so later.
                        c.CounterDay = State.Day + _rng.Range(0.5f, 1.5f);
                        Raise(new SimEvent { Kind = EventKind.CampaignWon, FactionId = c.FactionId, OtherFactionId = c.EnemyId, SettlementId = target.Id, Args = new[] { FactionName(c.FactionId), target.Name } });
                        // The camp's people move into the place they took.
                        if (camp != null) StrikeCamp(camp, target);
                    }
                    else if (++c.Attempts < Balance.CampaignAttempts && camp != null && camp.Soldiers >= 2f) SetStage(c, CampaignStage.Camp);
                    else End(c, "repelled");
                    break;
                case CampaignStage.Holding:
                    if (target.FactionId != c.FactionId)
                    {
                        End(c, "lost");
                        break;
                    }
                    // Home keeps sending men and food to the place it took until it is safe.
                    SupplyLine(c, home, target);
                    if (!c.Countered && war && State.Day >= c.CounterDay)
                    {
                        c.Countered = true;
                        Counterattack(c, target);
                    }
                    if (State.Day - c.StageSinceDay >= 3.0 && !SquadAlive(c.CounterSquadId)) End(c, "held");
                    break;
            }
        }

        private bool SquadAlive(int id)
        {
            foreach (Squad q in State.Squads) if (q.Id == id && q.State != SquadState.Done) return true;
            return false;
        }

        private void SetStage(Campaign c, CampaignStage stage)
        {
            c.Stage = stage;
            c.StageSinceDay = State.Day;
        }

        private string FactionName(int id)
        {
            Faction f = State.GetFaction(id);
            return f != null ? f.Name : "?";
        }

        /// <summary>Scouts reach the target: what they see, they will tell (with a scout's error).</summary>
        private void ScoutsArrive(Squad q)
        {
            Campaign c = GetCampaign(q.CampaignId);
            Settlement target = State.GetSettlement(q.TargetSettlementId);
            if (c == null || target == null) return;
            c.EnemyEstimate = (target.Soldiers + target.Population * 0.12f) * (1f + DefenseBonus(target)) * _rng.Range(0.8f, 1.25f);
        }

        /// <summary>Scouts are home: the campaign goes on to a war camp, or is called off if the enemy is too strong.</summary>
        private void ScoutsReport(Squad q)
        {
            Campaign c = GetCampaign(q.CampaignId);
            if (c == null || c.Stage != CampaignStage.Scouting) return;
            Settlement home = State.GetSettlement(c.HomeId);
            Settlement target = State.GetSettlement(c.TargetId);
            // Scouts and the daily step run before the day's campaigns: the home or the place may have
            // been lost while they walked. Then the campaign is over, and no war camp is set up.
            if (home == null || home.Destroyed || home.FactionId != c.FactionId)
            {
                End(c, "home");
                return;
            }
            if (target == null || target.Destroyed)
            {
                End(c, "target");
                return;
            }
            // The place may also have changed hands while they walked.
            if (target.FactionId != c.EnemyId)
            {
                End(c, target.FactionId == c.FactionId ? "ours" : "other");
                return;
            }
            // What the faction can bring: most of home's soldiers and some from its other places within reach.
            float strength = home.Soldiers * 0.6f * Gear(home);
            foreach (Settlement s in State.Settlements)
            {
                if (s == home || s.Destroyed || s.FactionId != c.FactionId || s.CampaignId != 0) continue;
                if (Vec2.Distance(s.Pos, home.Pos) < Balance.CampaignRange) strength += s.Soldiers * 0.35f * Gear(s);
            }
            Raise(new SimEvent { Kind = EventKind.CampaignScouted, FactionId = c.FactionId, SettlementId = target.Id, Args = new[] { FactionName(c.FactionId), target.Name } });
            if (c.EnemyEstimate > strength * 1.1f)
            {
                End(c, "strong");
                return;
            }
            SetUpCamp(c, home, target);
        }

        /// <summary>A war camp a little more than halfway, on dry and level ground, garrisoned from home.</summary>
        private void SetUpCamp(Campaign c, Settlement home, Settlement target)
        {
            Faction f = State.GetFaction(c.FactionId);
            Vec2 way = target.Pos - home.Pos;
            Vec2 site = home.Pos;
            Biome biome = home.Biome;
            bool found = false;
            // On the way, a good way off the target (out of its sight, 200 m at least), wherever the land is dry and level.
            float length = Math.Max(1f, way.Length());
            Vec2 back = way * (-1f / length);
            // Water or broken land in the way: anywhere round the target on home's side will do, and at
            // the last, ground by home itself, from where the band marches the whole way.
            float toHome = (float)Math.Atan2(back.Z, back.X);
            for (int k = 0; k < 36 && !found; k++)
            {
                Vec2 p;
                if (k < 12) p = target.Pos + back * Math.Max(200f, length * _rng.Range(0.3f, 0.45f)) + Vec2.FromAngle(_rng.Range(0f, 6.28f), _rng.Range(0f, 60f));
                else if (k < 28) p = target.Pos + Vec2.FromAngle(toHome + _rng.Range(-1.8f, 1.8f), _rng.Range(200f, Math.Max(220f, Math.Min(600f, length))));
                else p = home.Pos + Vec2.FromAngle(_rng.Range(0f, 6.28f), _rng.Range(50f, 140f));
                if (Vec2.Distance(p, target.Pos) < 180f) continue;
                // Rough country: after a few tries a smaller patch of level ground will do.
                if (World.ProbeSite(p, k % 2 == 0 ? 14f : 9f, out biome))
                {
                    site = p;
                    found = true;
                }
            }
            if (!found || f == null)
            {
                End(c, "nocamp");
                return;
            }
            Settlement camp = FoundSettlement(f, site, biome, false);
            camp.CampaignId = c.Id;
            float garrison = Math.Max(2f, home.Soldiers * 0.5f);
            home.Soldiers -= garrison;
            home.Population = Math.Max(1f, home.Population - garrison);
            // Its other places within reach send a share of their soldiers too.
            foreach (Settlement s in State.Settlements)
            {
                if (s == home || s.Destroyed || s.FactionId != c.FactionId || s.CampaignId != 0 || Vec2.Distance(s.Pos, home.Pos) >= Balance.CampaignRange) continue;
                float sent = s.Soldiers * 0.3f;
                s.Soldiers -= sent;
                s.Population = Math.Max(1f, s.Population - sent);
                garrison += sent;
            }
            camp.Population = garrison + 1f;
            camp.Soldiers = garrison;
            // Food for the camp comes from home.
            float food = Math.Min(Math.Min(home.Stock[(int)Res.Food] * 0.4f, garrison * Balance.FoodPerPopPerDay * 8f), StorageCap(camp));
            home.Stock[(int)Res.Food] -= food;
            camp.Stock[(int)Res.Food] = food;
            c.CampId = camp.Id;
            SetStage(c, CampaignStage.Camp);
            Raise(new SimEvent { Kind = EventKind.CampaignCamp, FactionId = c.FactionId, OtherFactionId = c.EnemyId, SettlementId = camp.Id, OtherSettlementId = target.Id, Args = new[] { f.Name, camp.Name, target.Name } });
            // Word of the camp goes round: friends of either side may come to stand by them.
            CallAllies(c, c.FactionId, c.EnemyId, camp);
            CallAllies(c, c.EnemyId, c.FactionId, target);
        }

        /// <summary>The main band marches from the camp on the target; the fight is the raid's (ResolveRaid).</summary>
        private void LaunchAssault(Campaign c, Settlement camp, Settlement target)
        {
            // The place may have changed hands since the camp was set up: never march on our own or another's land.
            if (target == null || target.Destroyed)
            {
                End(c, "target");
                return;
            }
            if (target.FactionId != c.EnemyId)
            {
                End(c, target.FactionId == c.FactionId ? "ours" : "other");
                return;
            }
            if (camp == null || camp.Soldiers < 2f)
            {
                End(c, "weak");
                return;
            }
            float men = camp.Soldiers * 0.85f;
            Squad band = Muster(camp, SquadKind.Raid, men, men, target.Pos, 2f * Vec2.Distance(camp.Pos, target.Pos) / Balance.SquadSpeed);
            band.TargetSettlementId = target.Id;
            band.CampaignId = c.Id;
            // Allies waiting at the camp go with the band.
            c.Allies.Clear();
            foreach (Squad g in State.Squads)
            {
                if (!IsGuestAt(g, camp) || g.Soldiers < 0.5f) continue;
                float join = g.Soldiers * 0.85f;
                g.Men -= join;
                g.Soldiers -= join;
                band.Men += join;
                band.Soldiers += join;
                c.Allies.Add(new AllyShare { SquadId = g.Id, Men = join });
            }
            band.StartMen = band.Men;
            c.SquadId = band.Id;
            SetStage(c, CampaignStage.Assault);
            Raise(new SimEvent { Kind = EventKind.CampaignAssault, FactionId = c.FactionId, OtherFactionId = c.EnemyId, SettlementId = target.Id, OtherSettlementId = camp.Id, Args = new[] { FactionName(c.FactionId), target.Name, camp.Name } });
        }

        /// <summary>The campaign is over: the camp's people go home (or into the place taken) and the camp is left.</summary>
        private void End(Campaign c, string why)
        {
            if (c.Stage == CampaignStage.Over) return;
            Settlement camp = c.CampId != 0 ? State.GetSettlement(c.CampId) : null;
            if (camp != null && !camp.Destroyed && camp.FactionId == c.FactionId)
            {
                Settlement home = State.GetSettlement(c.HomeId);
                if (home == null || home.Destroyed || home.FactionId != c.FactionId) home = NearestOwnNot(c.FactionId, camp.Pos, camp.Id);
                StrikeCamp(camp, home);
            }
            // The friends who came for it go home, from the camp and from the place defended.
            Settlement defended = State.GetSettlement(c.TargetId);
            if (defended != null) SendGuestsHome(defended);
            // Bands still on the road are called home: a campaign that is over must not fight on.
            foreach (Squad q in State.Squads)
            {
                if (q.CampaignId != c.Id) continue;
                if (q.State == SquadState.Gathering || q.State == SquadState.Outbound) SetState(q, SquadState.Returning);
            }
            c.Why = why;
            SetStage(c, CampaignStage.Over);
            if (why != "held") Raise(new SimEvent { Kind = EventKind.CampaignOver, FactionId = c.FactionId, OtherFactionId = c.EnemyId, SettlementId = c.TargetId, Args = new[] { FactionName(c.FactionId), why } });
        }

        private void StrikeCamp(Settlement camp, Settlement into)
        {
            SendGuestsHome(camp);
            if (into != null && into != camp)
            {
                into.Population += camp.Population;
                into.Soldiers = Math.Min(into.Population, into.Soldiers + camp.Soldiers);
                into.Stock[(int)Res.Food] = Math.Min(StorageCap(into), into.Stock[(int)Res.Food] + camp.Stock[(int)Res.Food]);
            }
            camp.Population = 0f;
            camp.Soldiers = 0f;
            camp.Destroyed = true;
        }

        /// <summary>
        /// Every other day or so home sends what it can spare to its men in the field: a quarter of its
        /// soldiers while it keeps enough of its own, and food.
        /// </summary>
        private void SupplyLine(Campaign c, Settlement home, Settlement to)
        {
            if (to == null || to.Destroyed || home.Soldiers < 4f || home.Stock[(int)Res.Food] < 20f) return;
            foreach (Squad q in State.Squads)
            {
                if (q.Kind == SquadKind.Reinforcement && q.FactionId == c.FactionId && q.TargetSettlementId == to.Id && q.State != SquadState.Done) return;
            }
            if (!_rng.Chance(0.5f)) return;
            float men = home.Soldiers * 0.25f;
            float road = Vec2.Distance(home.Pos, to.Pos) / Balance.SquadSpeed;
            Squad q2 = Muster(home, SquadKind.Reinforcement, men, men, to.Pos, road);
            q2.TargetSettlementId = to.Id;
            q2.CampaignId = c.Id;
            float food = home.Stock[(int)Res.Food] * 0.15f;
            home.Stock[(int)Res.Food] -= food;
            q2.Cargo[(int)Res.Food] = food;
        }

        /// <summary>
        /// Friends of a side at war, near enough, may send a share of their soldiers to stand by it: to
        /// its war camp, or to the place the enemy comes for. Never against a friend of their own.
        /// </summary>
        private void CallAllies(Campaign c, int sideId, int otherId, Settlement at)
        {
            if (!Balance.AllyAid || at == null || at.Destroyed) return;
            Faction side = State.GetFaction(sideId);
            foreach (Faction ally in State.Factions)
            {
                if (ally.Eliminated || ally.Id == sideId || ally.Id == otherId || !AreAllied(ally.Id, sideId) || AreAllied(ally.Id, otherId)) continue;
                if (!_rng.Chance(0.35f + 0.4f * ally.Honor)) continue;
                Settlement from = null;
                float best = Balance.CampaignRange;
                foreach (Settlement s in State.Settlements)
                {
                    if (s.Destroyed || s.FactionId != ally.Id || s.CampaignId != 0 || s.Soldiers < 4f) continue;
                    float d = Vec2.Distance(s.Pos, at.Pos);
                    if (d < best)
                    {
                        best = d;
                        from = s;
                    }
                }
                if (from == null) continue;
                float men = from.Soldiers * Balance.AllyShare;
                Squad q = Muster(from, SquadKind.Reinforcement, men, men, at.Pos, best / Balance.SquadSpeed * 2f + Balance.AllyStayDays);
                q.TargetSettlementId = at.Id;
                q.CampaignId = c.Id;
                Raise(new SimEvent { Kind = EventKind.AllyAid, FactionId = ally.Id, OtherFactionId = sideId, SettlementId = at.Id, OtherSettlementId = from.Id, Args = new[] { ally.Name, at.Name, side != null ? side.Name : "?" } });
            }
        }

        /// <summary>The side that lost the place gathers the soldiers it has nearest and marches to take it back.</summary>
        private void Counterattack(Campaign c, Settlement taken)
        {
            Faction enemy = State.GetFaction(c.EnemyId);
            if (enemy == null || enemy.Eliminated) return;
            Settlement from = null;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != enemy.Id || s.CampaignId != 0 || s.Soldiers < 4f) continue;
                if (Vec2.Distance(s.Pos, taken.Pos) > Balance.CampaignRange) continue;
                if (from == null || s.Soldiers > from.Soldiers) from = s;
            }
            if (from == null) return;
            float men = from.Soldiers * 0.6f;
            Squad band = Muster(from, SquadKind.Raid, men, men, taken.Pos, 2f * Vec2.Distance(from.Pos, taken.Pos) / Balance.SquadSpeed);
            band.TargetSettlementId = taken.Id;
            from.LastRaidDay = State.Day;
            c.CounterSquadId = band.Id;
            Raise(new SimEvent { Kind = EventKind.CampaignCounter, FactionId = enemy.Id, OtherFactionId = c.FactionId, SettlementId = taken.Id, OtherSettlementId = from.Id, Args = new[] { enemy.Name, taken.Name, from.Name } });
        }

        /// <summary>The allies' share of a campaign band, as many as came through, back to the allied bands they came from.</summary>
        private void ReturnAllies(Squad band)
        {
            Campaign c = GetCampaign(band.CampaignId);
            if (c == null || c.Allies.Count == 0) return;
            float through = band.StartMen > 0f ? Math.Min(1f, band.Men / band.StartMen) : 0f;
            foreach (AllyShare share in c.Allies)
            {
                float back = Math.Min(band.Men, share.Men * through);
                band.Men -= back;
                band.Soldiers = Math.Max(0f, Math.Min(band.Soldiers - back, band.Men));
                foreach (Squad g in State.Squads)
                {
                    if (g.Id != share.SquadId) continue;
                    g.Men += back;
                    g.Soldiers += back;
                }
            }
            c.Allies.Clear();
        }

        // ------------------------------------------------------------------ people moving house

        /// <summary>
        /// Once a day: from a place that starves, or that an enemy war camp has come up close to, some of
        /// the people who are not soldiers move to the nearest place of their own that is fed and safe.
        /// </summary>
        private void DailyMigration()
        {
            if (!Balance.Migration) return;
            for (int i = 0; i < State.Settlements.Count; i++)
            {
                Settlement s = State.Settlements[i];
                if (s.Destroyed || s.CampaignId != 0 || s.Population < 4f) continue;
                bool hungry = s.HungerDays > 1.5f;
                bool threatened = s.Tier <= Tier.Hamlet && EnemyCampNear(s, 450f);
                if (!hungry && !threatened) continue;
                foreach (Squad q in State.Squads)
                {
                    if (q.HomeId == s.Id && q.Civilians && q.State != SquadState.Done) goto next;
                }
                {
                    Settlement to = null;
                    float best = 1600f;
                    foreach (Settlement o in State.Settlements)
                    {
                        if (o == s || o.Destroyed || o.FactionId != s.FactionId || o.CampaignId != 0 || IsOutpost(o) || o.HungerDays > 0f || EnemyCampNear(o, 450f)) continue;
                        if (o.Stock[(int)Res.Food] < o.Population * 2f + 10f) continue;
                        float d = Vec2.Distance(o.Pos, s.Pos);
                        if (d < best)
                        {
                            best = d;
                            to = o;
                        }
                    }
                    if (to == null || !_rng.Chance(0.5f)) continue;
                    float people = Math.Min(Math.Max(0f, s.Population - s.Soldiers) * (threatened ? 0.3f : 0.2f), 5f);
                    if (people < 1f) continue;
                    Squad q2 = Muster(s, SquadKind.Reinforcement, people, 0f, to.Pos, best / Balance.SquadSpeed);
                    q2.TargetSettlementId = to.Id;
                    q2.Civilians = true;
                    Raise(new SimEvent { Kind = EventKind.Migration, FactionId = s.FactionId, SettlementId = to.Id, OtherSettlementId = s.Id, Args = new[] { to.Name, s.Name, ((int)Math.Round(people)).ToString() } });
                }
            next:;
            }
        }

        private bool EnemyCampNear(Settlement s, float range)
        {
            foreach (Settlement o in State.Settlements)
            {
                if (o.Destroyed || o.CampaignId == 0 || !AreAtWar(o.FactionId, s.FactionId)) continue;
                if (Vec2.Distance(o.Pos, s.Pos) < range) return true;
            }
            return false;
        }

        private Settlement NearestOwnNot(int factionId, Vec2 pos, int notId)
        {
            Settlement best = null;
            float bestD = float.MaxValue;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != factionId || s.Id == notId) continue;
                float d = Vec2.Distance(s.Pos, pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Starts a campaign at once against a given settlement (testing).</summary>
        public string DebugCampaign(int factionId, int targetId, bool straightToCamp = false)
        {
            Faction f = State.GetFaction(factionId);
            Settlement target = State.GetSettlement(targetId);
            if (f == null || target == null) return "no such faction or settlement";
            if (!AreAtWar(f.Id, target.FactionId)) return "not at war with " + target.Name;
            if (CampaignOf(f.Id) != null) return "a campaign is already on";
            _rng = new Rng(State.RngState);
            float aggression = f.Aggression;
            f.Aggression = 1f;
            int before = State.Campaigns.Count;
            for (int i = 0; i < 20 && State.Campaigns.Count == before; i++) TryBeginCampaign(f);
            f.Aggression = aggression;
            Campaign c = CampaignOf(f.Id);
            if (c != null && straightToCamp)
            {
                // The scouts are taken as back already, with good news.
                foreach (Squad q in State.Squads) if (q.Id == c.SquadId) q.State = SquadState.Done;
                c.TargetId = targetId;
                c.EnemyId = target.FactionId;
                SetUpCamp(c, State.GetSettlement(c.HomeId), target);
            }
            State.RngState = _rng.State;
            if (c == null && straightToCamp && State.Campaigns.Count > before) c = State.Campaigns[State.Campaigns.Count - 1];
            return c == null ? "no settlement of theirs can spare the men" : "campaign " + c.Id + " on " + State.GetSettlement(c.TargetId).Name + ": " + c.Stage + (c.Why != null ? " (" + c.Why + ")" : "") + (c.CampId != 0 ? ", camp " + c.CampId : "");
        }
    }
}
