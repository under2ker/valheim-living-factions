using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    public enum SquadKind { Expedition, Raid, Scouts, Reinforcement }
    public enum SquadState { Gathering, Outbound, AtTarget, Returning, Done }

    /// <summary>
    /// A band of a settlement's people away from home: it gathers, walks across the map at a person's
    /// pace, does its work at the far end (gathering, fighting, looking) and walks back with what it
    /// got. While away its men are not counted at home, and it carries food for the road.
    /// </summary>
    public class Squad
    {
        public int Id;
        public SquadKind Kind;
        public SquadState State;
        public int FactionId;
        public int HomeId;
        /// <summary>The settlement it marches on (raids, scouting), 0 for a place in the wild.</summary>
        public int TargetSettlementId;
        public Vec2 Pos;
        public Vec2 Target;
        public Biome TargetBiome;
        public float Men;
        /// <summary>Of the men, how many are soldiers (they go back to being soldiers at home).</summary>
        public float Soldiers;
        /// <summary>Food carried for the road.</summary>
        public float Supplies;
        /// <summary>What an expedition went for.</summary>
        public Res Goal;
        /// <summary>Goods carried home, by resource.</summary>
        public float[] Cargo = new float[5];
        public double StateSinceDay;
        public double ReadyDay;
        /// <summary>The campaign it marches for, 0 for none.</summary>
        public int CampaignId;
        /// <summary>Men it set out with (or went into the fight with).</summary>
        public float StartMen;
        /// <summary>How well a war band is armed (1 bare, up to 1.6 and more with the metal it was given); 0 for others.</summary>
        public float Gear;
        /// <summary>People moving house rather than soldiers on duty.</summary>
        public bool Civilians;
        /// <summary>A carrier from an outpost: it leaves its load and goes back.</summary>
        public bool Carrier;
    }

    public partial class Simulation
    {
        // ------------------------------------------------------------------ the bands on the move

        public IEnumerable<Squad> SquadsFrom(Settlement s)
        {
            foreach (Squad q in State.Squads)
            {
                if (q.HomeId == s.Id && q.State != SquadState.Done) yield return q;
            }
        }

        private bool HasSquadOut(Settlement s, SquadKind kind)
        {
            foreach (Squad q in SquadsFrom(s)) if (q.Kind == kind) return true;
            return false;
        }

        /// <summary>A band leaves: its men (soldiers first for a raid) and its food come off the settlement.</summary>
        private Squad Muster(Settlement home, SquadKind kind, float men, float soldiers, Vec2 target, float roadDays)
        {
            Squad q = new Squad
            {
                Id = State.NextSquadId++,
                Kind = kind,
                State = SquadState.Gathering,
                FactionId = home.FactionId,
                HomeId = home.Id,
                Pos = home.Pos,
                Target = target,
                Men = men,
                Soldiers = soldiers,
                StartMen = men,
                StateSinceDay = State.Day,
                ReadyDay = State.Day + Balance.MusterDays,
            };
            // A war band is armed from the settlement's metal before it goes.
            if (kind == SquadKind.Raid) q.Gear = Arm(home, soldiers);
            home.Population = Math.Max(1f, home.Population - men);
            home.Soldiers = Math.Max(0f, Math.Min(home.Soldiers - soldiers, home.Population));
            float food = Math.Min(home.Stock[(int)Res.Food], men * Balance.FoodPerPopPerDay * (roadDays + 1f));
            home.Stock[(int)Res.Food] -= food;
            q.Supplies = food;
            State.Squads.Add(q);
            return q;
        }

        /// <summary>Danger of a place for a band of the faction: the wilds of its biome and enemies close by.</summary>
        public float Danger(Vec2 pos, Biome biome, int factionId)
        {
            float d;
            switch (biome)
            {
                case Biome.Meadows: d = 0.05f; break;
                case Biome.BlackForest: d = 0.15f; break;
                case Biome.Swamp:
                case Biome.Mountain: d = 0.3f; break;
                case Biome.Plains: d = 0.35f; break;
                case Biome.Mistlands: d = 0.45f; break;
                default: d = 0.5f; break;
            }
            bool watched = false;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed) continue;
                // The faction's own places, a road post above all, keep the country about them safer.
                if (s.FactionId == factionId && Vec2.Distance(s.Pos, pos) < (s.RoadPost ? 500f : 300f)) watched = true;
                if (s.FactionId == factionId || !AreAtWar(s.FactionId, factionId)) continue;
                if (Vec2.Distance(s.Pos, pos) < 400f) d += 0.3f;
            }
            if (watched) d *= 0.6f;
            return Math.Min(0.9f, d);
        }

        private void StepSquads(float dt)
        {
            for (int i = 0; i < State.Squads.Count; i++) StepSquad(State.Squads[i], dt);
            State.Squads.RemoveAll(q => q.State == SquadState.Done);
        }

        private void StepSquad(Squad q, float dt)
        {
            Settlement home = State.GetSettlement(q.HomeId);
            if (home == null || home.Destroyed || home.FactionId != q.FactionId)
            {
                // Home is gone or taken: the band joins the nearest settlement of its own people, if any.
                home = NearestOwn(q.FactionId, q.Pos);
                if (home == null)
                {
                    q.State = SquadState.Done;
                    return;
                }
                q.HomeId = home.Id;
                if (q.State != SquadState.Gathering) q.State = SquadState.Returning;
            }

            // The road eats food; a band without food goes hungry and loses men.
            q.Supplies -= q.Men * Balance.FoodPerPopPerDay * dt;
            if (q.Supplies < 0f)
            {
                q.Supplies = 0f;
                q.Men -= q.Men * 0.05f * dt;
            }

            switch (q.State)
            {
                case SquadState.Gathering:
                    if (State.Day >= q.ReadyDay) SetState(q, SquadState.Outbound);
                    break;
                case SquadState.Outbound:
                    if (Walk(q, q.Target, dt)) Arrive(q, home);
                    break;
                case SquadState.AtTarget:
                    WorkAtTarget(q, home, dt);
                    break;
                case SquadState.Returning:
                    if (Walk(q, home.Pos, dt)) ComeHome(q, home);
                    break;
            }
            if (q.State != SquadState.Done && q.Men < 0.6f)
            {
                q.State = SquadState.Done;
                Raise(new SimEvent { Kind = EventKind.SquadLost, FactionId = q.FactionId, SettlementId = home.Id, Args = new[] { home.Name, q.Kind.ToString() } });
            }
        }

        private void SetState(Squad q, SquadState state)
        {
            q.State = state;
            q.StateSinceDay = State.Day;
        }

        /// <summary>Moves toward a point at a person's pace; on the way the wilds may cost men. True on arrival.</summary>
        private bool Walk(Squad q, Vec2 to, float dt)
        {
            Vec2 d = to - q.Pos;
            float len = d.Length();
            float step = Balance.SquadSpeed * dt;
            Biome biome;
            World.ProbeSite(q.Pos, 4f, out biome);
            // Trouble on the road: a fight with whatever lives there now and then.
            if (_rng.Chance(Danger(q.Pos, biome, q.FactionId) * 0.25f * dt)) Skirmish(q, 0.1f, 0.35f);
            if (len <= step)
            {
                q.Pos = to;
                return true;
            }
            q.Pos = q.Pos + d * (step / len);
            return false;
        }

        private void Skirmish(Squad q, float minLoss, float maxLoss)
        {
            float lost = q.Men * _rng.Range(minLoss, maxLoss);
            q.Men -= lost;
            q.Soldiers = Math.Min(q.Soldiers, q.Men);
            // A beaten band drops some of its load.
            for (int i = 0; i < 5; i++) q.Cargo[i] *= 1f - lost / Math.Max(1f, q.Men + lost);
        }

        private void Arrive(Squad q, Settlement home)
        {
            switch (q.Kind)
            {
                case SquadKind.Expedition:
                    SetState(q, SquadState.AtTarget);
                    break;
                case SquadKind.Raid:
                    RaidArrives(q, home);
                    SetState(q, SquadState.Returning);
                    break;
                case SquadKind.Scouts:
                    ScoutsArrive(q);
                    SetState(q, SquadState.Returning);
                    break;
                case SquadKind.Reinforcement:
                    Reinforce(q);
                    break;
            }
        }

        /// <summary>
        /// Arms a band from the settlement's metal: half a unit a soldier mends blades and rings mail.
        /// What is short is fought without. Returns the band's gear (like Gear() for a settlement).
        /// </summary>
        private float Arm(Settlement s, float soldiers)
        {
            float need = soldiers * Balance.MetalPerSoldier;
            float used = Math.Min(need, s.Stock[(int)Res.Metal]);
            s.Stock[(int)Res.Metal] -= used;
            return 1f + 0.6f * (need > 0f ? used / need : 1f) + 0.1f * (int)s.Tier;
        }

        /// <summary>
        /// Men sent to a place: to one of their own they join it (soldiers, food and all); to an ally's
        /// they stay as guests and fight beside its people until their time is up; anywhere else they turn back.
        /// </summary>
        private void Reinforce(Squad q)
        {
            Settlement at = State.GetSettlement(q.TargetSettlementId);
            if (at == null || at.Destroyed)
            {
                SetState(q, SquadState.Returning);
                return;
            }
            if (at.FactionId == q.FactionId && q.Carrier)
            {
                float room = StorageCap(at);
                for (int i = 0; i < 5; i++)
                {
                    at.Stock[i] = Math.Min(i == (int)Res.Coins ? room * 4f : room, at.Stock[i] + q.Cargo[i]);
                    q.Cargo[i] = 0f;
                }
                SetState(q, SquadState.Returning);
                return;
            }
            if (at.FactionId == q.FactionId)
            {
                at.Population += q.Men;
                at.Soldiers = Math.Min(at.Population, at.Soldiers + q.Soldiers);
                float cap = StorageCap(at);
                for (int i = 0; i < 5; i++) at.Stock[i] = Math.Min(i == (int)Res.Coins ? cap * 4f : cap, at.Stock[i] + q.Cargo[i]);
                at.Stock[(int)Res.Food] = Math.Min(cap, at.Stock[(int)Res.Food] + q.Supplies);
                q.State = SquadState.Done;
                return;
            }
            if (AreAllied(q.FactionId, at.FactionId))
            {
                SetState(q, SquadState.AtTarget);
                q.ReadyDay = State.Day + Balance.AllyStayDays;
                return;
            }
            SetState(q, SquadState.Returning);
        }

        /// <summary>Allied soldiers staying at a place, ready to fight for it.</summary>
        private float GuestSoldiers(Settlement s)
        {
            float men = 0f;
            foreach (Squad q in State.Squads)
            {
                if (IsGuestAt(q, s)) men += q.Soldiers;
            }
            return men;
        }

        private bool IsGuestAt(Squad q, Settlement s)
        {
            return q.Kind == SquadKind.Reinforcement && q.State == SquadState.AtTarget && q.TargetSettlementId == s.Id && q.FactionId != s.FactionId;
        }

        /// <summary>The guests at a place take their share of its losses in a fight.</summary>
        private void GuestLosses(Settlement s, float share)
        {
            foreach (Squad q in State.Squads)
            {
                if (!IsGuestAt(q, s)) continue;
                q.Men = Math.Max(0f, q.Men - q.Soldiers * share);
                q.Soldiers = Math.Min(q.Soldiers * (1f - share), q.Men);
            }
        }

        /// <summary>Guests at a place go home (the campaign they came for is over, or the place changed hands).</summary>
        private void SendGuestsHome(Settlement s)
        {
            foreach (Squad q in State.Squads)
            {
                if (q.Kind == SquadKind.Reinforcement && q.State == SquadState.AtTarget && q.TargetSettlementId == s.Id) SetState(q, SquadState.Returning);
            }
        }

        private void WorkAtTarget(Squad q, Settlement home, float dt)
        {
            if (q.Kind == SquadKind.Reinforcement)
            {
                // Guests stay their time, unless the place is lost or the friendship is over.
                Settlement at = State.GetSettlement(q.TargetSettlementId);
                if (at == null || at.Destroyed || !AreAllied(q.FactionId, at.FactionId) || State.Day >= q.ReadyDay) SetState(q, SquadState.Returning);
                return;
            }
            int r = (int)q.Goal;
            float rate = r == (int)Res.Food ? Balance.FoodPerWorkerDay : r == (int)Res.Stone ? Balance.StonePerWorkerDay : r == (int)Res.Metal ? Balance.MetalPerWorkerDay : Balance.WoodPerWorkerDay;
            // A band on a picked spot works harder than people at home with chores to do.
            q.Cargo[r] += q.Men * rate * Defs.Yield(q.TargetBiome, q.Goal) * 1.5f * dt;
            if (_rng.Chance(Danger(q.Pos, q.TargetBiome, q.FactionId) * 0.4f * dt)) Skirmish(q, 0.1f, 0.4f);
            if (State.Day - q.StateSinceDay >= Balance.GatherDays) SetState(q, SquadState.Returning);
        }

        private void ComeHome(Squad q, Settlement home)
        {
            // Allies who went into a campaign's assault with this band go back to their own lot, as many as came through.
            if (q.Kind == SquadKind.Raid && q.CampaignId != 0) ReturnAllies(q);
            home.Population += q.Men;
            home.Soldiers = Math.Min(home.Population, home.Soldiers + q.Soldiers);
            float cap = StorageCap(home);
            float brought = 0f;
            for (int i = 0; i < 5; i++)
            {
                if (q.Cargo[i] <= 0f) continue;
                float room = Math.Max(0f, (i == (int)Res.Coins ? cap * 4f : cap) - home.Stock[i]);
                float add = Math.Min(room, q.Cargo[i]);
                home.Stock[i] += add;
                brought += add;
            }
            home.Stock[(int)Res.Food] = Math.Max(home.Stock[(int)Res.Food], Math.Min(cap, home.Stock[(int)Res.Food] + q.Supplies));
            q.State = SquadState.Done;
            if (q.Kind == SquadKind.Expedition)
                Raise(new SimEvent { Kind = EventKind.ExpeditionBack, FactionId = q.FactionId, SettlementId = home.Id, Args = new[] { home.Name, q.Goal.ToString(), ((int)brought).ToString() } });
            if (q.Kind == SquadKind.Scouts) ScoutsReport(q);
        }

        private Settlement NearestOwn(int factionId, Vec2 pos)
        {
            Settlement best = null;
            float bestD = float.MaxValue;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != factionId) continue;
                float d = Vec2.Distance(s.Pos, pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>One of the band fell in the world.</summary>
        public void SquadLoss(int squadId)
        {
            foreach (Squad q in State.Squads)
            {
                if (q.Id != squadId) continue;
                q.Men = Math.Max(0f, q.Men - 1f);
                q.Soldiers = Math.Min(q.Soldiers, q.Men);
            }
        }

        // ------------------------------------------------------------------ expeditions

        /// <summary>
        /// Once a day each settlement short of something may send a band out for it: to the place within
        /// reach where that thing is plentiful, weighed against the distance and the danger of the way.
        /// </summary>
        private void DailyExpeditions()
        {
            if (!Balance.Expeditions) return;
            for (int i = 0; i < State.Settlements.Count; i++)
            {
                Settlement s = State.Settlements[i];
                if (s.Destroyed || s.Tier < Tier.Hamlet || s.Population < 8f || IsLeaderless(s)) continue;
                if (State.Day - s.LastExpeditionDay < Balance.ExpeditionCooldownDays || HasSquadOut(s, SquadKind.Expedition)) continue;
                Res need = NeededResource(s);
                if (s.Stock[(int)need] >= DesiredStock(s, need) * 0.5f) continue;
                if (need == Res.Coins) continue;
                SendExpedition(s, need);
            }
        }

        /// <summary>The best place within reach for the thing, weighed against distance and danger; false when home is as good.</summary>
        private bool SendExpedition(Settlement s, Res need)
        {
            Vec2 best = s.Pos;
            Biome bestBiome = s.Biome;
            float bestScore = Defs.Yield(s.Biome, need) * 1.2f;
            for (int k = 0; k < 10; k++)
            {
                float angle = _rng.Range(0f, (float)(Math.PI * 2));
                float dist = _rng.Range(250f, Balance.ExpeditionRange);
                Vec2 p = s.Pos + Vec2.FromAngle(angle, dist);
                Biome b;
                World.ProbeSite(p, 6f, out b);
                if (b == Biome.Ocean || b == Biome.Other) continue;
                float score = Defs.Yield(b, need) / (0.6f + dist / 1000f) * (1f - Danger(p, b, s.FactionId));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    bestBiome = b;
                }
            }
            if (best.X == s.Pos.X && best.Z == s.Pos.Z) return false;

            float men = Util.Clamp(s.Population * 0.12f, 2f, 6f);
            float road = 2f * Vec2.Distance(best, s.Pos) / Balance.SquadSpeed + Balance.GatherDays;
            if (s.Stock[(int)Res.Food] < men * Balance.FoodPerPopPerDay * road) return false;
            s.LastExpeditionDay = State.Day;
            Squad q = Muster(s, SquadKind.Expedition, men, Math.Min(s.Soldiers * 0.2f, 1f), best, road);
            q.Goal = need;
            q.TargetBiome = bestBiome;
            Raise(new SimEvent { Kind = EventKind.ExpeditionOut, FactionId = s.FactionId, SettlementId = s.Id, Args = new[] { s.Name, need.ToString(), bestBiome.ToString() } });
            return true;
        }

        /// <summary>Sends an expedition out from a settlement at once, whatever its stores (testing).</summary>
        public string DebugExpedition(int settlementId, Res? resource = null)
        {
            Settlement s = State.GetSettlement(settlementId);
            if (s == null) return "no such settlement";
            _rng = new Rng(State.RngState);
            s.LastExpeditionDay = -100;
            Res need = resource ?? NeededResource(s);
            bool sent = SendExpedition(s, need);
            State.RngState = _rng.State;
            return sent ? "expedition out of " + s.Name + " for " + need : "no expedition for " + need + " (no better place within reach, or no food for the road)";
        }

        public string DescribeSquads(Vec2 from)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(State.Squads.Count + " bands");
            foreach (Squad q in State.Squads)
            {
                Settlement home = State.GetSettlement(q.HomeId);
                Settlement target = State.GetSettlement(q.TargetSettlementId);
                sb.Append("\n  #" + q.Id + " " + q.Kind + (q.Civilians ? "(people)" : "") + " " + q.State + " from " + (home != null ? home.Name : "?") + (target != null ? " on " + target.Name : "")
                    + " men " + q.Men.ToString("0.0") + " cargo " + (int)(q.Cargo[0] + q.Cargo[1] + q.Cargo[2] + q.Cargo[3] + q.Cargo[4])
                    + " at " + q.Pos + " " + Vec2.Distance(q.Pos, from).ToString("0") + " m away");
            }
            foreach (Campaign c in State.Campaigns)
            {
                Settlement target = State.GetSettlement(c.TargetId);
                Settlement camp = c.CampId != 0 ? State.GetSettlement(c.CampId) : null;
                sb.Append("\n  campaign #" + c.Id + " of faction " + c.FactionId + " on " + (target != null ? target.Name : "?") + " (" + c.TargetId + "): " + c.Stage
                    + (c.Why != null ? " why " + c.Why : "")
                    + (c.CampId != 0 ? " camp " + c.CampId + (camp != null && camp.Destroyed ? " (gone)" : "") : "")
                    + " estimate " + c.EnemyEstimate.ToString("0.0")
                    + (c.Countered ? " countered" : "") + (c.Allies.Count > 0 ? " allies " + c.Allies.Count : ""));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ raids on foot

        /// <summary>A war band reaches the settlement it marched on and fights there (Politics.ResolveRaid).</summary>
        private void RaidArrives(Squad q, Settlement home)
        {
            Settlement target = State.GetSettlement(q.TargetSettlementId);
            Faction attacker = State.GetFaction(q.FactionId);
            Faction defender = target != null ? State.GetFaction(target.FactionId) : null;
            // A campaign's aim may have changed hands while its band walked: the campaign is ended on the
            // next daily step and its bands are called home. It must not fight for a place that is no
            // longer the enemy's, not even one that is at war with it for a war of its own.
            Campaign campaign = q.CampaignId != 0 ? GetCampaign(q.CampaignId) : null;
            if (campaign != null && (target == null || target.Destroyed || target.FactionId != campaign.EnemyId)) return;
            if (target == null || target.Destroyed || attacker == null || defender == null || !AreAtWar(attacker.Id, defender.Id)) return;
            Relation r;
            if (!attacker.Relations.TryGetValue(defender.Id, out r)) return;
            target.LastAttackedDay = State.Day;
            // A player at the target sees the band come in; the outcome is the simulation's either way.
            if (RaidInterceptor != null) RaidInterceptor(home, target, q.Men);
            ResolveRaid(attacker, defender, home, target, q.Men, r, q);
        }
    }
}
