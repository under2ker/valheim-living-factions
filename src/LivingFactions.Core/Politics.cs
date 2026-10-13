using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    // Relations between factions: drift, incidents, treaties, raids and caravans.
    public partial class Simulation
    {
        /// <summary>
        /// Lets the game stage a raid physically when a player is watching. Arguments: attacker,
        /// target, committed soldiers. Return true to take over; the sim then skips its own resolution.
        /// </summary>
        public Func<Settlement, Settlement, float, bool> RaidInterceptor;

        /// <summary>The single shared relation object for a pair; stored on the lower-id faction.</summary>
        public Relation Rel(Faction a, Faction b)
        {
            Faction lo = a.Id < b.Id ? a : b;
            Faction hi = a.Id < b.Id ? b : a;
            Relation r;
            if (!lo.Relations.TryGetValue(hi.Id, out r))
            {
                float baseline = Defs.Affinity(Defs.Culture(lo.CultureId), Defs.Culture(hi.CultureId)) + _rng.Range(-12f, 12f);
                r = new Relation { Value = baseline, Baseline = baseline, SinceDay = State.Day };
                lo.Relations[hi.Id] = r;
            }
            return r;
        }

        public Treaty TreatyBetween(int factionA, int factionB)
        {
            if (factionA == factionB) return Treaty.Alliance;
            Faction a = State.GetFaction(factionA);
            Faction b = State.GetFaction(factionB);
            if (a == null || b == null) return Treaty.Peace;
            Faction lo = a.Id < b.Id ? a : b;
            Relation r;
            return lo.Relations.TryGetValue(a.Id < b.Id ? b.Id : a.Id, out r) ? r.Treaty : Treaty.Peace;
        }

        public bool AreAtWar(int factionA, int factionB)
        {
            return factionA != factionB && TreatyBetween(factionA, factionB) == Treaty.War;
        }

        public bool IsAtWarWithAnyone(Faction f)
        {
            foreach (Faction other in State.Factions)
            {
                if (other.Id == f.Id || other.Eliminated) continue;
                if (TreatyBetween(f.Id, other.Id) == Treaty.War) return true;
            }
            return false;
        }

        public float Strength(Faction f)
        {
            float total = 0f;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != f.Id) continue;
                total += s.Soldiers * Gear(s) + s.Population * 0.15f;
            }
            return total;
        }

        private static float Gear(Settlement s)
        {
            return 1f + Math.Min(0.6f, s.Stock[(int)Res.Metal] / 80f) + 0.1f * (int)s.Tier;
        }

        private void DailyPolitics()
        {
            List<Faction> alive = new List<Faction>();
            foreach (Faction f in State.Factions)
            {
                if (!f.Eliminated) alive.Add(f);
            }

            for (int i = 0; i < alive.Count; i++)
            {
                for (int j = i + 1; j < alive.Count; j++)
                {
                    Faction a = alive[i];
                    Faction b = alive[j];
                    Relation r = Rel(a, b);
                    UpdateRelation(a, b, r);
                    if (r.Treaty == Treaty.War) TryRaid(a, b, r);
                    else if (r.Value > 0f) TryCaravan(a, b, r);
                }
            }
        }

        private void UpdateRelation(Faction a, Faction b, Relation r)
        {
            int borders = 0;
            Settlement nearA = null, nearB = null;
            float nearest = float.MaxValue;
            bool marketA = false, marketB = false;
            foreach (Settlement sa in State.Settlements)
            {
                if (sa.Destroyed) continue;
                if (sa.FactionId == a.Id && Has(sa, BuildingKind.Market)) marketA = true;
                if (sa.FactionId == b.Id && Has(sa, BuildingKind.Market)) marketB = true;
                if (sa.FactionId != a.Id) continue;
                foreach (Settlement sb in State.Settlements)
                {
                    if (sb.Destroyed || sb.FactionId != b.Id) continue;
                    float d = Vec2.Distance(sa.Pos, sb.Pos);
                    if (d < Balance.BorderFrictionRange) borders++;
                    if (d < nearest)
                    {
                        nearest = d;
                        nearA = sa;
                        nearB = sb;
                    }
                }
            }

            if (r.Treaty == Treaty.War)
            {
                r.Value = Math.Max(-100f, r.Value - 0.5f);
                r.Weariness += 0.6f;
                float maxAggression = Math.Max(a.Aggression, b.Aggression);
                bool tired = r.Weariness >= 12f + 20f * maxAggression;
                bool longWar = State.Day - r.SinceDay >= 6 && _rng.Chance(0.05f + r.Weariness / 250f);
                if (tired || longWar)
                {
                    r.Treaty = Treaty.Peace;
                    r.Value = -25f;
                    r.Weariness = 0f;
                    r.SinceDay = State.Day;
                    Raise(new SimEvent { Kind = EventKind.PeaceMade, FactionId = a.Id, OtherFactionId = b.Id, Args = new[] { a.Name, b.Name } });
                }
                return;
            }

            float friction = Math.Min(2f, borders * 0.6f) * (r.Treaty == Treaty.Alliance ? 0.5f : 1f);
            float trade = marketA && marketB ? 0.5f * (a.Mercantile + b.Mercantile) : 0f;
            // Nothing binds two peoples like a shared enemy.
            float commonEnemy = 0f;
            foreach (Faction third in State.Factions)
            {
                if (third.Eliminated || third.Id == a.Id || third.Id == b.Id) continue;
                if (TreatyBetween(a.Id, third.Id) == Treaty.War && TreatyBetween(b.Id, third.Id) == Treaty.War) commonEnemy = 1.5f;
            }
            r.Value = Util.MoveToward(r.Value, r.Baseline, 0.35f);
            r.Value = Util.Clamp(r.Value - friction + trade + commonEnemy, -100f, 100f);

            // Small unpredictable events between neighbours keep relations moving.
            if (nearest < 3000f && _rng.Chance(0.04f))
            {
                float bias = (a.Aggression + b.Aggression) * 0.5f;
                float delta = _rng.Range(-16f, 12f) - bias * 6f + (a.Honor + b.Honor) * 2f;
                r.Value = Util.Clamp(r.Value + delta, -100f, 100f);
                Raise(new SimEvent
                {
                    Kind = EventKind.Incident,
                    FactionId = a.Id,
                    OtherFactionId = b.Id,
                    Args = new[] { a.Name, b.Name, (delta >= 0f ? "good" : "bad") + _rng.Int(4), ((int)Math.Round(delta)).ToString() }
                });
            }

            if (r.Treaty == Treaty.Alliance)
            {
                if (r.Value < 30f)
                {
                    r.Treaty = Treaty.Peace;
                    r.SinceDay = State.Day;
                    Raise(new SimEvent { Kind = EventKind.AllianceBroken, FactionId = a.Id, OtherFactionId = b.Id, Args = new[] { a.Name, b.Name } });
                }
                return;
            }

            if (r.Value >= 55f && _rng.Chance(0.1f))
            {
                r.Treaty = Treaty.Alliance;
                r.SinceDay = State.Day;
                Raise(new SimEvent { Kind = EventKind.AllianceFormed, FactionId = a.Id, OtherFactionId = b.Id, Args = new[] { a.Name, b.Name } });
                return;
            }

            if (!Balance.EnableWars) return;
            Faction aggressor = a.Aggression >= b.Aggression ? a : b;
            Faction victim = aggressor == a ? b : a;
            float sa2 = Strength(aggressor);
            float sv = Math.Max(1f, Strength(victim));
            bool grudge = r.Value <= -55f && (sa2 >= sv * 0.6f || aggressor.Aggression > 0.7f) && _rng.Chance(0.08f + 0.25f * aggressor.Aggression);
            bool opportunism = r.Value < -25f && aggressor.Aggression > 0.7f && sa2 >= sv * 2f && _rng.Chance(0.03f);
            // A war nobody can reach is not worth declaring.
            if ((grudge || opportunism) && nearest < Balance.RaidRange * 1.5f && State.Day - r.SinceDay >= 3)
            {
                DeclareWar(aggressor, victim, r);
            }
        }

        private void DeclareWar(Faction aggressor, Faction victim, Relation r)
        {
            r.Treaty = Treaty.War;
            r.Value = Math.Min(r.Value, -70f);
            r.Weariness = 0f;
            r.SinceDay = State.Day;
            Raise(new SimEvent { Kind = EventKind.WarDeclared, FactionId = aggressor.Id, OtherFactionId = victim.Id, Args = new[] { aggressor.Name, victim.Name } });

            // Allies of the victim take offence; players allied with the victim are expected to pick a side.
            foreach (Faction other in State.Factions)
            {
                if (other.Eliminated || other.Id == aggressor.Id || other.Id == victim.Id) continue;
                if (TreatyBetween(other.Id, victim.Id) == Treaty.Alliance)
                {
                    Relation ro = Rel(other, aggressor);
                    ro.Value = Util.Clamp(ro.Value - 30f, -100f, 100f);
                }
            }
        }

        private void TryRaid(Faction a, Faction b, Relation r)
        {
            TryRaidFrom(a, b, r);
            TryRaidFrom(b, a, r);
        }

        private void TryRaidFrom(Faction attacker, Faction defender, Relation r)
        {
            Settlement from = null;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != attacker.Id) continue;
                if (s.Soldiers < 3f || State.Day - s.LastRaidDay < Balance.RaidCooldownDays) continue;
                if (from == null || s.Soldiers > from.Soldiers) from = s;
            }
            if (from == null || !_rng.Chance(0.45f)) return;

            Settlement target = null;
            float best = Balance.RaidRange;
            foreach (Settlement s in State.Settlements)
            {
                if (s.Destroyed || s.FactionId != defender.Id) continue;
                // A place that was just fought over is left alone for a while.
                if (State.Day - s.LastAttackedDay < 2.0) continue;
                float d = Vec2.Distance(s.Pos, from.Pos);
                if (d < best)
                {
                    best = d;
                    target = s;
                }
            }
            if (target == null) return;

            from.LastRaidDay = State.Day;
            target.LastAttackedDay = State.Day;
            float committed = from.Soldiers * _rng.Range(0.6f, 0.9f);
            if (RaidInterceptor != null && RaidInterceptor(from, target, committed)) return;
            ResolveRaid(attacker, defender, from, target, committed, r);
        }

        private void ResolveRaid(Faction attacker, Faction defender, Settlement from, Settlement target, float committed, Relation r)
        {
            float atk = committed * Gear(from) * _rng.Range(0.75f, 1.25f);
            float def = (target.Soldiers * Gear(target) + target.Population * 0.12f) * (1f + DefenseBonus(target)) * _rng.Range(0.75f, 1.25f);
            float atkLoss, defLoss;
            if (atk > def)
            {
                float ratio = atk / Math.Max(0.5f, def);
                atkLoss = committed * _rng.Range(0.10f, 0.25f);
                defLoss = target.Soldiers * _rng.Range(0.4f, 0.7f);
                float civilians = Math.Max(0f, target.Population - target.Soldiers) * _rng.Range(0.05f, 0.2f);
                target.Soldiers = Math.Max(0f, target.Soldiers - defLoss);
                target.Population = Math.Max(0f, target.Population - defLoss - civilians);

                float cap = StorageCap(from);
                for (int i = 0; i < 5; i++)
                {
                    float loot = target.Stock[i] * 0.35f;
                    target.Stock[i] -= loot;
                    from.Stock[i] = Math.Min(i == (int)Res.Coins ? cap * 4f : cap, from.Stock[i] + loot);
                }

                Raise(new SimEvent { Kind = EventKind.RaidWon, FactionId = attacker.Id, OtherFactionId = defender.Id, SettlementId = target.Id, OtherSettlementId = from.Id, Args = new[] { attacker.Name, target.Name, from.Name } });

                int burn = 1 + (ratio > 2f ? 1 : 0);
                for (int n = 0; n < burn; n++) BurnRandomBuilding(target, attacker.Id);

                bool weak = target.Population < 2.5f;
                bool overrun = ratio >= 2.5f && target.Tier <= Tier.Hamlet && _rng.Chance(0.5f);
                if (weak || overrun) CaptureSettlement(target, attacker, defender, committed * 0.3f);
            }
            else
            {
                atkLoss = committed * _rng.Range(0.30f, 0.55f);
                defLoss = target.Soldiers * _rng.Range(0.10f, 0.25f);
                target.Soldiers = Math.Max(0f, target.Soldiers - defLoss);
                target.Population = Math.Max(0f, target.Population - defLoss);
                Raise(new SimEvent { Kind = EventKind.RaidRepelled, FactionId = attacker.Id, OtherFactionId = defender.Id, SettlementId = target.Id, OtherSettlementId = from.Id, Args = new[] { attacker.Name, target.Name, from.Name } });
            }
            from.Soldiers = Math.Max(0f, from.Soldiers - atkLoss);
            from.Population = Math.Max(1f, from.Population - atkLoss);
            r.Weariness += (atkLoss + defLoss) * 0.8f;
        }

        private void BurnRandomBuilding(Settlement s, int byFactionId)
        {
            List<Building> candidates = new List<Building>();
            foreach (Building b in s.Buildings)
            {
                if (b.State == BuildingState.Complete && b.Kind != BuildingKind.Campfire) candidates.Add(b);
            }
            if (candidates.Count == 0) return;
            Building victim = candidates[_rng.Int(candidates.Count)];
            victim.State = BuildingState.Destroyed;
            ClampStock(s);
            Raise(new SimEvent { Kind = EventKind.BuildingDestroyed, FactionId = s.FactionId, OtherFactionId = byFactionId, SettlementId = s.Id, BuildingId = victim.Id, Args = new[] { s.Name, victim.Kind.ToString() } });
        }

        private void CaptureSettlement(Settlement target, Faction attacker, Faction defender, float garrison)
        {
            target.FactionId = attacker.Id;
            target.Population = Math.Max(3f, target.Population * 0.6f + garrison);
            target.Soldiers = Math.Min(target.Population, garrison);
            target.HungerDays = 0f;
            target.LastAttackedDay = State.Day + 4.0;
            Raise(new SimEvent { Kind = EventKind.SettlementCaptured, FactionId = attacker.Id, OtherFactionId = defender.Id, SettlementId = target.Id, Args = new[] { attacker.Name, target.Name, defender.Name } });
            if (CountSettlements(defender.Id) == 0) defender.Eliminated = true;
            else EnsureCapital(defender);
        }

        private void TryCaravan(Faction a, Faction b, Relation r)
        {
            if (!_rng.Chance(0.12f * (a.Mercantile + b.Mercantile))) return;
            Settlement sa = null, sb = null;
            float best = 2500f;
            foreach (Settlement x in State.Settlements)
            {
                if (x.Destroyed || x.FactionId != a.Id || x.Tier < Tier.Hamlet) continue;
                foreach (Settlement y in State.Settlements)
                {
                    if (y.Destroyed || y.FactionId != b.Id || y.Tier < Tier.Hamlet) continue;
                    float d = Vec2.Distance(x.Pos, y.Pos);
                    if (d < best)
                    {
                        best = d;
                        sa = x;
                        sb = y;
                    }
                }
            }
            if (sa == null) return;

            // Each side sends what it has most of and receives what it lacks.
            Res giveA = Surplus(sa), giveB = Surplus(sb);
            if (giveA == giveB) return;
            float amountA = sa.Stock[(int)giveA] * 0.15f;
            float amountB = sb.Stock[(int)giveB] * 0.15f;
            if (amountA < 5f || amountB < 5f) return;
            sa.Stock[(int)giveA] -= amountA;
            sb.Stock[(int)giveB] -= amountB;
            sb.Stock[(int)giveA] = Math.Min(StorageCap(sb), sb.Stock[(int)giveA] + amountA);
            sa.Stock[(int)giveB] = Math.Min(StorageCap(sa), sa.Stock[(int)giveB] + amountB);
            sa.Stock[(int)Res.Coins] += 8f;
            sb.Stock[(int)Res.Coins] += 8f;
            r.Value = Util.Clamp(r.Value + 1.5f, -100f, 100f);
            Raise(new SimEvent { Kind = EventKind.TradeCaravan, FactionId = a.Id, OtherFactionId = b.Id, SettlementId = sa.Id, OtherSettlementId = sb.Id, Args = new[] { sa.Name, sb.Name, giveA.ToString(), giveB.ToString() } });
        }

        private Res Surplus(Settlement s)
        {
            Res best = Res.Wood;
            float bestRatio = -1f;
            for (int i = 0; i < 4; i++)
            {
                float desired = DesiredStock(s, (Res)i);
                if (desired <= 0f) continue;
                float ratio = s.Stock[i] / desired;
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    best = (Res)i;
                }
            }
            return best;
        }
    }
}
