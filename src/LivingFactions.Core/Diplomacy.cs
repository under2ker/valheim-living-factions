using System;

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
                    if (rel.AtWar) continue;
                    // Deeds are slowly forgotten, good ones slower than the pull back up from bad ones.
                    float rate = rel.Reputation > disposition ? 0.15f : 0.3f;
                    rel.Reputation = Util.MoveToward(rel.Reputation, disposition, rate);
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

        /// <summary>A gift of goods to a settlement. Returns the reputation gained.</summary>
        public float PlayerGift(Settlement s, long playerId, Res res, float amount)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null || amount <= 0f) return 0f;
            PlayerRelation rel = PlayerRel(f, playerId);
            float value = amount * Balance.CoinValue[(int)res];
            float need = res != Res.Coins && NeededResource(s) == res ? 2f : 1f;
            float gain = Math.Min(15f, value * 0.12f * need / (1f + rel.GiftFatigue / 40f));
            if (rel.AtWar) gain *= 0.25f;
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

        /// <summary>A player killed one of the faction's people.</summary>
        public void PlayerKilledMember(Settlement s, long playerId, bool wasSoldier)
        {
            Faction f = State.GetFaction(s.FactionId);
            if (f == null) return;
            s.Population = Math.Max(0f, s.Population - 1f);
            if (wasSoldier) s.Soldiers = Math.Max(0f, s.Soldiers - 1f);
            s.Soldiers = Math.Min(s.Soldiers, s.Population);
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.LastHostileDay = State.Day;
            Raise(new SimEvent { Kind = EventKind.PlayerKilledMember, FactionId = f.Id, SettlementId = s.Id, PlayerId = playerId, Args = new[] { rel.Name, f.Name, s.Name } });
            AddReputation(f, playerId, rel.AtWar ? -2f : -12f * (0.75f + 0.5f * f.Honor));
            if (s.Population < 1f && !s.Destroyed) DestroySettlement(s, 0);
        }

        /// <summary>A member died to something other than a player (monsters, another faction).</summary>
        public void MemberDied(Settlement s, bool wasSoldier)
        {
            s.Population = Math.Max(0f, s.Population - 1f);
            if (wasSoldier) s.Soldiers = Math.Max(0f, s.Soldiers - 1f);
            s.Soldiers = Math.Min(s.Soldiers, s.Population);
        }

        public void PlayerAttackedMember(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.LastHostileDay = State.Day;
            if (!rel.AtWar) AddReputation(f, playerId, -2f);
        }

        public void PlayerDamagedProperty(Faction f, long playerId)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            rel.LastHostileDay = State.Day;
            if (!rel.AtWar) AddReputation(f, playerId, -4f);
        }

        /// <summary>A player took goods from a settlement's chests. Returns false when nobody cares (open war).</summary>
        public bool PlayerStole(Faction f, long playerId, float value)
        {
            PlayerRelation rel = PlayerRel(f, playerId);
            if (rel.AtWar) return false;
            rel.LastHostileDay = State.Day;
            AddReputation(f, playerId, -Math.Min(12f, 2f + value * 0.08f) * (0.75f + 0.5f * f.Honor));
            return true;
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
