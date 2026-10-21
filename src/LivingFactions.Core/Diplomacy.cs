using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    public struct DiploResult
    {
        public bool Ok;
        /// <summary>Localization key suffix explaining the outcome.</summary>
        public string Reason;

        public static DiploResult Yes(string reason) { return new DiploResult { Ok = true, Reason = reason }; }
        public static DiploResult No(string reason) { return new DiploResult { Ok = false, Reason = reason }; }
    }

    // Everything between a faction and a player: reputation, pacts, gifts and trade.
    public partial class Simulation
    {
        public const float HostileBelow = -50f;
        public const float WaryBelow = -15f;
        public const float FriendlyFrom = 30f;
        public const float AllianceFrom = 70f;

        public PlayerRelation PlayerRel(Faction f, long playerId)
        {
            PlayerRelation rel;
            if (!f.Players.TryGetValue(playerId, out rel))
            {
                rel = new PlayerRelation { Reputation = Defs.Culture(f.CultureId).Disposition };
                f.Players[playerId] = rel;
            }
            return rel;
        }

        public Standing GetStanding(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (rel.AtWar) return Standing.War;
            if (rel.Allied) return Standing.Allied;
            if (rel.Reputation <= HostileBelow) return Standing.Hostile;
            if (rel.Reputation <= WaryBelow) return Standing.Wary;
            if (rel.Reputation < FriendlyFrom) return Standing.Neutral;
            return Standing.Friendly;
        }

        public bool IsHostileToPlayer(int factionId, long playerId)
        {
            Faction f = State.GetFaction(factionId);
            if (f == null) return false;
            Standing st = GetStanding(f, playerId);
            return st == Standing.War || st == Standing.Hostile;
        }

        public void AddReputation(Faction f, long playerId, float delta)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.Reputation = Util.Clamp(rel.Reputation + delta, -100f, 100f);
            if (rel.Allied && rel.Reputation < 40f)
            {
                rel.Allied = false;
                Raise(new SimEvent { Kind = EventKind.PlayerAllianceBroken, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name } });
            }
            if (!rel.AtWar && rel.Reputation <= -75f)
            {
                rel.AtWar = true;
                rel.Allied = false;
                rel.LastHostileDay = State.Day;
                // Arg 2 says who started it.
                Raise(new SimEvent { Kind = EventKind.PlayerWar, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name, "faction" } });
            }
        }

        private void DailyPlayerRelations()
        {
            foreach (Faction f in State.Factions)
            {
                if (f.Eliminated) continue;
                float disposition = Defs.Culture(f.CultureId).Disposition;
                foreach (var pair in f.Players)
                {
                    PlayerRelation rel = pair.Value;
                    rel.GiftFatigue *= 0.85f;
                    DailySuspicion(f, rel, pair.Key);
                    if (rel.AtWar) continue;
                    // Deeds are slowly forgotten, good ones slower than the pull back up from bad ones,
                    // and that pull is weaker while there is blood between them.
                    float rate = (rel.Reputation > disposition ? 0.15f : 0.3f / (1f + rel.Grievance / 15f)) * Math.Max(0f, Balance.ReputationDecay);
                    rel.Reputation = Util.MoveToward(rel.Reputation, disposition, rate);
                    rel.Grievance *= 0.92f;
                    if (rel.Grievance < 0.5f) rel.Grievance = 0f;
                }
            }

            // Taking sides: allies expect loyalty, enemies of your enemies warm to you.
            foreach (Faction f in State.Factions)
            {
                if (f.Eliminated) continue;
                foreach (Faction other in State.Factions)
                {
                    if (other.Eliminated || other.Id == f.Id) continue;
                    Treaty treaty = TreatyBetween(f.Id, other.Id);
                    foreach (var pair in other.Players)
                    {
                        PlayerRelation theirs = pair.Value;
                        if (treaty == Treaty.War && theirs.Allied) AddReputation(f, pair.Key, -0.4f);
                        if (treaty == Treaty.War && theirs.AtWar) AddReputation(f, pair.Key, 0.2f);
                        if (treaty == Treaty.Alliance && theirs.AtWar) AddReputation(f, pair.Key, -0.3f);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ pacts

        public DiploResult PlayerDeclareWar(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (rel.AtWar) return DiploResult.No("already_war");
            rel.AtWar = true;
            rel.Allied = false;
            rel.Reputation = Math.Min(rel.Reputation, -75f);
            rel.LastHostileDay = State.Day;
            Raise(new SimEvent { Kind = EventKind.PlayerWar, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name, "player" } });
            return DiploResult.Yes("war_declared");
        }

        /// <summary>Coins the faction wants as reparations before it will agree to peace.</summary>
        public int PeaceCost(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            float cost = 40f + Math.Max(0f, -rel.Reputation) * 1.5f + f.Aggression * 60f;
            return (int)Math.Round(cost / 5f) * 5;
        }

        public DiploResult PlayerProposePeace(Faction f, long playerId, int coinsPaid)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (!rel.AtWar) return DiploResult.No("not_at_war");
            if (State.Day - rel.LastHostileDay < 1.0) return DiploResult.No("blood_too_fresh");
            if (coinsPaid < PeaceCost(f, playerId)) return DiploResult.No("not_enough_coins");
            rel.AtWar = false;
            rel.Reputation = Math.Max(rel.Reputation, -30f);
            Settlement capital = State.GetSettlement(f.CapitalId);
            if (capital != null) capital.Stock[(int)Res.Coins] += coinsPaid;
            Raise(new SimEvent { Kind = EventKind.PlayerPeace, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name } });
            return DiploResult.Yes("peace_made");
        }

        public DiploResult PlayerProposeAlliance(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (rel.AtWar) return DiploResult.No("at_war");
            if (rel.Allied) return DiploResult.No("already_allied");
            if (rel.Reputation < AllianceFrom) return DiploResult.No("not_trusted");
            rel.Allied = true;
            Raise(new SimEvent { Kind = EventKind.PlayerAlliance, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name } });
            return DiploResult.Yes("alliance_made");
        }

        public DiploResult PlayerBreakAlliance(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (!rel.Allied) return DiploResult.No("not_allied");
            rel.Allied = false;
            rel.Reputation = Math.Min(rel.Reputation, 20f) - 15f * f.Honor;
            Raise(new SimEvent { Kind = EventKind.PlayerAllianceBroken, FactionId = f.Id, PlayerId = playerId, Args = new[] { f.Name, rel.Name } });
            return DiploResult.Yes("alliance_broken");
        }

        // ------------------------------------------------------------------ deeds

        /// <summary>
        /// What a gift would earn right now, without giving it: a gift is worth more when it is what the
        /// settlement is short of, at most 15, and less with every gift given lately.
        /// </summary>
        public float GiftGain(Settlement s, long playerId, Res res, float amount)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null || amount <= 0f) return 0f;
            PlayerRelation rel = PlayerRel(f, playerId);
            float value = amount * Balance.CoinValue[(int)res];
            float need = res != Res.Coins && NeededResource(s) == res ? 2f : 1f;
            float gain = Math.Min(15f, value * 0.12f * need / (1f + rel.GiftFatigue / 40f));
            return rel.AtWar ? gain * 0.25f : gain;
        }

        /// <summary>A gift of goods to a settlement. Returns the reputation gained.</summary>
        public float PlayerGift(Settlement s, long playerId, Res res, float amount)
        {
            Faction giver = State.GetFaction(s.FactionId);
            if (giver != null && RecentlyStole(giver, playerId))
            {
                // Their own goods handed back: the stores have them again, nobody thanks the thief.
                s.Stock[(int)res] += amount;
                ClampStock(s);
                return 0f;
            }
            Faction f = State.GetFaction(s.FactionId);
            if (f == null || amount <= 0f) return 0f;
            PlayerRelation rel = PlayerRel(f, playerId);
            float value = amount * Balance.CoinValue[(int)res];
            float gain = GiftGain(s, playerId, res, amount);
            rel.GiftFatigue += value * 0.12f;
            float cap = res == Res.Coins ? StorageCap(s) * 4f : StorageCap(s);
            s.Stock[(int)res] = Math.Min(cap, s.Stock[(int)res] + amount);
            AddReputation(f, playerId, gain);
            if (gain >= 1f)
            {
                Raise(new SimEvent { Kind = EventKind.PlayerGift, FactionId = f.Id, SettlementId = s.Id, PlayerId = playerId, Args = new[] { rel.Name, s.Name, res.ToString(), ((int)amount).ToString() } });
            }
            return gain;
        }

        /// <summary>
        /// A player killed one of the faction's people. Seen by others of the faction, it is known at once;
        /// unseen, the faction only finds out later, if at all (DailySuspicion). A chief weighs more.
        /// Caravan folk and hired fighters are not counted among a settlement's people.
        /// </summary>
        public void PlayerKilledMember(Settlement s, long playerId, bool wasSoldier, bool witnessed = true, bool chief = false, bool counted = true)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null) return;
            if (counted)
            {
                s.Population = Math.Max(0f, s.Population - 1f);
                if (wasSoldier) s.Soldiers = Math.Max(0f, s.Soldiers - 1f);
                s.Soldiers = Math.Min(s.Soldiers, s.Population);
                if (chief) ChiefFell(s, f);
            }
            PlayerRelation rel = PlayerRel(f, playerId);
            float penalty = rel.AtWar ? 2f : 12f * (0.75f + 0.5f * f.Honor) * (chief ? 2.5f : 1f);
            if (witnessed)
            {
                rel.LastHostileDay = State.Day;
                Raise(new SimEvent { Kind = EventKind.PlayerKilledMember, FactionId = f.Id, SettlementId = s.Id, PlayerId = playerId, Args = new[] { rel.Name, f.Name, s.Name } });
                AddReputation(f, playerId, -penalty);
                if (!rel.AtWar) rel.Grievance += penalty;
            }
            else if (!rel.AtWar)
            {
                rel.Suspicion += penalty;
                rel.SuspicionSettlementId = s.Id;
            }
            if (s.Population < 1f && !s.Destroyed) DestroySettlement(s, 0);
        }

        /// <summary>Killings nobody saw come out sooner or later, or are never pinned on anyone.</summary>
        private void DailySuspicion(Faction f, PlayerRelation rel, long playerId)
        {
            if (rel.Suspicion <= 0f) return;
            if (_rng.Chance(Balance.CrimeFoundPerDay))
            {
                float penalty = rel.Suspicion * 0.8f;
                rel.Suspicion = 0f;
                rel.LastHostileDay = State.Day;
                Raise(new SimEvent { Kind = EventKind.PlayerCrimeFound, FactionId = f.Id, SettlementId = rel.SuspicionSettlementId, PlayerId = playerId, Args = new[] { rel.Name } });
                if (!rel.AtWar)
                {
                    AddReputation(f, playerId, -penalty);
                    rel.Grievance += penalty;
                }
                return;
            }
            // The trail goes cold.
            rel.Suspicion *= 0.85f;
            if (rel.Suspicion < 1f) rel.Suspicion = 0f;
        }

        /// <summary>A chief has died: the settlement is without a head for a few days, then picks a new one.</summary>
        public void ChiefFell(Settlement s, Faction f)
        {
            if (s.LeaderlessUntil > State.Day) return;
            s.LeaderlessUntil = State.Day + Balance.LeaderlessDays;
            Raise(new SimEvent { Kind = EventKind.ChiefFell, FactionId = f.Id, SettlementId = s.Id, Args = new[] { s.Name } });
        }

        public bool IsLeaderless(Settlement s) { return s.LeaderlessUntil > State.Day; }

        /// <summary>The new chief takes over, and may lead the faction a little differently.</summary>
        private void DailyLeadership(Settlement s)
        {
            if (s.LeaderlessUntil < 0 || s.LeaderlessUntil > State.Day) return;
            s.LeaderlessUntil = -1;
            Faction f = State.GetFaction(s.FactionId);
            if (f == null) return;
            if (f.CapitalId == s.Id) f.Aggression = Util.Clamp(f.Aggression + (_rng.Next() - 0.5f) * 0.2f, 0f, 1f);
            Raise(new SimEvent { Kind = EventKind.NewChief, FactionId = f.Id, SettlementId = s.Id, Args = new[] { s.Name } });
        }

        /// <summary>A member died to something other than a player (monsters, another faction).</summary>
        public void MemberDied(Settlement s, bool wasSoldier)
        {
            s.Population = Math.Max(0f, s.Population - 1f);
            if (wasSoldier) s.Soldiers = Math.Max(0f, s.Soldiers - 1f);
            s.Soldiers = Math.Min(s.Soldiers, s.Population);
        }

        public void PlayerAttackedMember(Faction f, long playerId, float loss = 2f)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.LastHostileDay = State.Day;
            if (!rel.AtWar) AddReputation(f, playerId, -loss);
        }

        public void PlayerDamagedProperty(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.LastHostileDay = State.Day;
            rel.LastDamagedDay = State.Day;
            if (!rel.AtWar) AddReputation(f, playerId, -4f);
        }

        /// <summary>
        /// Most of a building's pieces are gone in the world: broken by a player, by monsters or in a
        /// fight. It is a ruin now, as after a raid: what it gave the settlement it gives no more, and
        /// the settlement builds anew in time, at its own cost. A player who was striking the faction's
        /// buildings just before is named for it and is held to account.
        /// </summary>
        public void BuildingWrecked(Settlement s, int buildingId)
        {
            Faction f = s != null ? State.GetFaction(s.FactionId) : null;
            Building b = s != null ? s.Buildings.Find(x => x.Id == buildingId) : null;
            if (f == null || b == null || b.State != BuildingState.Complete || b.Kind == BuildingKind.Campfire || b.Kind == BuildingKind.Farm) return;
            b.State = BuildingState.Destroyed;
            ClampStock(s);
            long culprit = 0;
            string name = "";
            foreach (KeyValuePair<long, PlayerRelation> kv in f.Players)
            {
                if (State.Day - kv.Value.LastDamagedDay > 0.1 || (culprit != 0 && kv.Value.LastDamagedDay < f.Players[culprit].LastDamagedDay)) continue;
                culprit = kv.Key;
                name = kv.Value.Name;
            }
            if (culprit != 0)
            {
                PlayerRelation rel = f.Players[culprit];
                float penalty = Defs.Buildings[b.Kind].Wood * 0.15f + 4f;
                if (!rel.AtWar)
                {
                    AddReputation(f, culprit, -Math.Min(20f, penalty));
                    rel.Grievance += Math.Min(20f, penalty);
                }
            }
            Raise(new SimEvent { Kind = EventKind.BuildingWrecked, FactionId = f.Id, SettlementId = s.Id, BuildingId = b.Id, PlayerId = culprit, Args = new[] { s.Name, b.Kind.ToString(), name } });
        }

        /// <summary>A player took goods from a settlement's chests. Returns false when nobody cares (open war).</summary>
        public bool PlayerStole(Faction f, long playerId, float value)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (rel.AtWar) return false;
            rel.LastHostileDay = State.Day;
            rel.LastStoleDay = State.Day;
            AddReputation(f, playerId, -Math.Min(12f, 2f + value * 0.08f) * (0.75f + 0.5f * f.Honor));
            return true;
        }

        /// <summary>
        /// For a day after a theft the faction neither buys from the thief nor takes gifts: its own goods
        /// would come back to it as a deal or a kindness.
        /// </summary>
        public bool RecentlyStole(Faction f, long playerId)
        {
            return State.Day - PlayerRel(f, playerId).LastStoleDay < 1.0;
        }

        /// <summary>A player killed something the faction considers an enemy near one of its settlements.</summary>
        public void PlayerHelped(Faction f, long playerId, float weight)
        {
            if (!PlayerRel(f, playerId).AtWar) AddReputation(f, playerId, weight);
        }

        // ------------------------------------------------------------------ trade

        private float StandingBuyMult(Standing st)
        {
            switch (st)
            {
                case Standing.Allied: return 0.95f;
                case Standing.Friendly: return 1.1f;
                case Standing.Neutral: return 1.3f;
                default: return 1.6f;
            }
        }

        private float StandingSellMult(Standing st)
        {
            switch (st)
            {
                case Standing.Allied: return 0.9f;
                case Standing.Friendly: return 0.75f;
                case Standing.Neutral: return 0.6f;
                default: return 0.45f;
            }
        }

        public bool WillTrade(Faction f, long playerId)
        {
            Standing st = GetStanding(f, playerId);
            return st != Standing.War && st != Standing.Hostile;
        }

        /// <summary>
        /// Set by the game layer: a multiplier on what a given player pays to buy (1 = no change), for
        /// items that give a trading discount. Null in the simulation harness.
        /// </summary>
        public Func<long, float> PlayerBuyMult;

        /// <summary>Coins a player pays per unit when buying from the settlement.</summary>
        public float BuyPrice(Settlement s, Res res, long playerId)
        {
            Faction f = State.GetFaction(s.FactionId);
            float scarcity = Util.Clamp(DesiredStock(s, res) / (s.Stock[(int)res] + 1f), 0.6f, 2.5f);
            float player = PlayerBuyMult != null ? PlayerBuyMult(playerId) : 1f;
            return Balance.CoinValue[(int)res] * scarcity * StandingBuyMult(GetStanding(f, playerId)) * player;
        }

        /// <summary>Coins the settlement pays per unit when a player sells to it.</summary>
        public float SellPrice(Settlement s, Res res, long playerId)
        {
            Faction f = State.GetFaction(s.FactionId);
            float scarcity = Util.Clamp(DesiredStock(s, res) / (s.Stock[(int)res] + 1f), 0.4f, 2f);
            return Balance.CoinValue[(int)res] * scarcity * StandingSellMult(GetStanding(f, playerId));
        }

        /// <summary>Player buys goods. Returns the coin cost, or -1 when the deal is refused.</summary>
        public int PlayerBuy(Settlement s, Res res, int amount, long playerId, int playerCoins)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null || !WillTrade(f, playerId)) return -1;
            // The settlement keeps a reserve: it will not sell itself into hunger.
            float reserve = res == Res.Food ? s.Population * 3f : 10f;
            if (s.Stock[(int)res] - amount < reserve) return -1;
            int cost = Math.Max(1, (int)Math.Ceiling(BuyPrice(s, res, playerId) * amount));
            if (cost > playerCoins) return -1;
            s.Stock[(int)res] -= amount;
            s.Stock[(int)Res.Coins] += cost;
            AddReputation(f, playerId, Math.Min(1f, cost * 0.01f));
            return cost;
        }

        /// <summary>Player sells goods. Returns the coins paid out, or -1 when the deal is refused.</summary>
        public int PlayerSell(Settlement s, Res res, int amount, long playerId)
        {
            Faction thief = State.GetFaction(s.FactionId);
            if (thief != null && RecentlyStole(thief, playerId)) return -1;
            Faction f = State.GetFaction(s.FactionId);
            if (f == null || !WillTrade(f, playerId)) return -1;
            int pay = (int)Math.Floor(SellPrice(s, res, playerId) * amount);
            if (pay <= 0 || pay > s.Stock[(int)Res.Coins]) return -1;
            s.Stock[(int)res] = Math.Min(StorageCap(s), s.Stock[(int)res] + amount);
            s.Stock[(int)Res.Coins] -= pay;
            AddReputation(f, playerId, Math.Min(1f, pay * 0.01f));
            return pay;
        }
    }
}
