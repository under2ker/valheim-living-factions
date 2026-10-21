using System.Collections.Generic;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>Who fights whom: faction members follow the simulation's wars and the player's standing.</summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    internal static class IsEnemyPatch
    {
        private static bool Prefix(Character a, Character b, ref bool __result)
        {
            if (!SimHost.Active || a == null || b == null || a == b) return true;
            FactionMember ma = FactionMember.Of(a);
            FactionMember mb = FactionMember.Of(b);
            if (ma == null && mb == null) return true;
            if ((ma != null && ma.Role == NpcRole.Envoy) || (mb != null && mb.Role == NpcRole.Envoy))
            {
                __result = false;
                return false;
            }

            if (ma != null && mb != null)
            {
                // A hired sword fights whoever is hostile to the player who pays.
                FactionMember hired = ma.Role == NpcRole.Mercenary ? ma : mb.Role == NpcRole.Mercenary ? mb : null;
                if (hired != null && Player.m_localPlayer != null && (hired == ma ? mb : ma).IsHostileTo(Player.m_localPlayer))
                {
                    __result = true;
                    return false;
                }
                __result = (ma.FactionId != mb.FactionId && SimHost.Sim.AreAtWar(ma.FactionId, mb.FactionId))
                    || Grudges.AngryAt(ma, b) || Grudges.AngryAt(mb, a) || Grudges.DefendsAgainst(ma, b) || Grudges.DefendsAgainst(mb, a);
                return false;
            }

            FactionMember member = ma ?? mb;
            Character other = ma != null ? b : a;
            Player player = other as Player;
            if (player != null)
            {
                __result = member.IsHostileTo(player);
                return false;
            }
            if (other.IsTamed())
            {
                // Tamed animals share their masters' standing.
                __result = Player.m_localPlayer != null && member.IsHostileTo(Player.m_localPlayer);
                return false;
            }
            // A creature out in the water is nobody's business: no one wades in after a Neck, nor stands
            // on the shore glaring at it.
            if (other.IsSwimming() || other.transform.position.y < ZoneSystem.c_WaterLevel - 0.5f)
            {
                __result = false;
                return false;
            }
            // Wild creatures: the game's own rules, using the member's creature faction, and anything
            // that has struck one of them or set on the player near friends.
            if (Grudges.AngryAt(member, other) || Grudges.DefendsAgainst(member, other))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>Faction buildings do not rot in the rain or collapse for lack of support.</summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class NoWearPatch
    {
        private static bool Prefix(ZNetView ___m_nview)
        {
            if (___m_nview == null || !___m_nview.IsValid()) return true;
            return ___m_nview.GetZDO().GetInt(Materializer.HashSettlement) == 0 && ___m_nview.GetZDO().GetInt(Roads.HashRoad) == 0;
        }
    }

    /// <summary>Road lamps never need feeding.</summary>
    [HarmonyPatch(typeof(Fireplace), "Awake")]
    internal static class RoadLampPatch
    {
        private static void Postfix(Fireplace __instance)
        {
            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.GetZDO().GetInt(Roads.HashRoad) != 0) __instance.m_infiniteFuel = true;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class HoverTextPatch
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            FactionMember member = FactionMember.Of(__instance);
            if (member != null) __result = member.HoverText();
        }
    }

    /// <summary>Deaths of inhabitants reach the simulation: population drops and killers are remembered.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static partial class DeathPatch
    {
        private static void Prefix(Character __instance, HitData ___m_lastHit)
        {
            if (!SimHost.Active) return;
            FactionMember member = FactionMember.Of(__instance);
            Character slayer = ___m_lastHit != null ? ___m_lastHit.GetAttacker() : null;
            Player killer = slayer as Player;
            // Whoever or whatever killed one of them, the people round about take it up at once.
            if (member != null && slayer != null) Grudges.OnKilled(member, slayer, member.Role == NpcRole.Chief);
            if (member == null)
            {
                if (__instance.IsBoss()) BossHunt.BossKilled(__instance);
                if (killer != null) CreditMonsterKill(__instance, killer);
                return;
            }
            if (member.Role == NpcRole.Raider)
            {
                // The raid itself is settled by the simulation; what matters here is whose side the player took.
                if (killer != null) CreditRaiderKill(member, killer);
                return;
            }
            Settlement settlement = SimHost.Sim.State.GetSettlement(member.SettlementId);
            if (settlement == null) return;
            if (Npcs.IsTemporary(member.Role))
            {
                // A band on the move is one man short.
                if (member.SquadId != 0) Net.SquadLoss(member.SquadId);
                // Caravans and hired fighters are not counted among the inhabitants; only a murder is remembered.
                if (killer != null) Net.PlayerKilledMember(settlement, killer.GetPlayerID(), member.Role != NpcRole.Trader, Grudges.Witnessed(member, __instance.transform.position), false, false);
                return;
            }
            Materializer.NoteDeath(settlement.Id);
            if (killer != null)
            {
                Rewards.ChiefKilled(member, killer);
                Rewards.GuardKilled(member, killer);
            }
            bool soldier = member.Role != NpcRole.Villager;
            bool chief = member.Role == NpcRole.Chief;
            if (killer != null)
            {
                // Seen by one of theirs, it is known at once; unseen, it may come out days later.
                bool seen = Grudges.Witnessed(member, __instance.transform.position);
                Net.NamePlayer(SimHost.Sim.State.GetFaction(settlement.FactionId), killer.GetPlayerID(), killer.GetPlayerName());
                Net.PlayerKilledMember(settlement, killer.GetPlayerID(), soldier, seen, chief);
                if (!seen && MessageHud.instance != null && killer == Player.m_localPlayer)
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get("msg_unseen_kill"));
            }
            else
            {
                Net.MemberDied(settlement, soldier);
                if (chief) Net.ChiefFell(settlement);
            }
        }
    }

    internal static partial class DeathPatch
    {
        /// <summary>Killing monsters close to a settlement is a favour to the people living there.</summary>
        private static void CreditMonsterKill(Character victim, Player killer)
        {
            if (victim.IsTamed() || victim.IsPlayer() || victim.m_faction == Character.Faction.AnimalsVeg) return;
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed || Utils.DistanceXZ(victim.transform.position, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 75f) continue;
                Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
                if (f == null) continue;
                // Races do not thank you for killing their wild kin.
                string group = Defs.Culture(f.CultureId).Group;
                bool kin = (group == "fuling" && victim.m_faction == Character.Faction.PlainsMonsters) || (group == "greydwarf" && victim.m_faction == Character.Faction.ForestMonsters)
                    || (group == "draugr" && victim.m_faction == Character.Faction.Undead) || (group == "dvergr" && victim.m_faction == Character.Faction.Dverger);
                if (kin) continue;
                Net.NamePlayer(f, killer.GetPlayerID(), killer.GetPlayerName());
                Net.PlayerHelped(f, killer.GetPlayerID(), victim.IsBoss() ? 8f : 1f);
                return;
            }
        }

        private static void CreditRaiderKill(FactionMember raider, Player killer)
        {
            WorldState state = SimHost.Sim.State;
            Faction attackers = state.GetFaction(raider.FactionId);
            if (attackers != null) Net.PlayerAttackedMember(attackers, killer.GetPlayerID());
            foreach (Settlement s in state.Settlements)
            {
                if (s.Destroyed || s.FactionId == raider.FactionId) continue;
                if (Utils.DistanceXZ(raider.transform.position, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 90f) continue;
                Faction defenders = state.GetFaction(s.FactionId);
                if (defenders == null) continue;
                Net.NamePlayer(defenders, killer.GetPlayerID(), killer.GetPlayerName());
                Net.PlayerHelped(defenders, killer.GetPlayerID(), 3f);
                Rewards.RaiderKilled(s, killer);
                return;
            }
        }
    }

    /// <summary>An envoy is never hurt: they come to talk, and no weapon reaches them (the blow still costs standing, HurtPatch).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class EnvoyUnharmedPatch
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Character __instance)
        {
            FactionMember member = SimHost.Active ? FactionMember.Of(__instance) : null;
            return member == null || member.Role != NpcRole.Envoy;
        }
    }

    /// <summary>Striking an inhabitant or their property costs reputation.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class HurtPatch
    {
        private static readonly Dictionary<int, float> LastPenalty = new Dictionary<int, float>();

        private static void Prefix(Character __instance, HitData hit)
        {
            if (!SimHost.Active || hit == null) return;
            FactionMember member = FactionMember.Of(__instance);
            if (member == null) return;
            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null || attacker != Player.m_localPlayer) return;
            member.LastHurt = Time.time;
            // One swing that hits several of them counts once.
            float last;
            if (LastPenalty.TryGetValue(member.FactionId, out last) && Time.time - last < 0.4f) return;
            LastPenalty[member.FactionId] = Time.time;
            Faction faction = SimHost.Sim.State.GetFaction(member.FactionId);
            if (faction == null) return;
            long pid = attacker.GetPlayerID();
            PlayerRelation rel = SimHost.Sim.PlayerRel(faction, pid);
            float before = rel.Reputation;
            bool allied = rel.Allied;
            Net.NamePlayer(faction, pid, attacker.GetPlayerName());
            Net.PlayerAttackedMember(faction, pid, ModConfig.ReputationPerHit.Value);
            Warnings.AfterOffence(faction, rel, before, allied);
        }
    }

    /// <summary>
    /// A player's hoe, pickaxe or cultivator changed the ground (TerrainOp, sent to the tile's owner): the mod
    /// leaves the seams there alone. The mod's own work goes to TerrainComp.DoOperation directly, not this way.
    /// </summary>
    [HarmonyPatch(typeof(TerrainComp), "RPC_ApplyOperation")]
    internal static class PlayerGroundWorkPatch
    {
        private static void Prefix(ZPackage pkg)
        {
            if (pkg == null) return;
            // Read from a copy: the game reads the same package after this.
            ZPackage copy = new ZPackage(pkg.GetArray());
            Materializer.NotePlayerGroundWork(copy.ReadVector3(), 10f);
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class PropertyDamagePatch
    {
        private static float _lastPenalty = -999f;

        private static void Prefix(ZNetView ___m_nview, HitData hit)
        {
            if (!SimHost.Active || hit == null || ___m_nview == null || !___m_nview.IsValid()) return;
            int settlementId = ___m_nview.GetZDO().GetInt(Materializer.HashSettlement);
            if (settlementId == 0) return;
            // Only the striker's own game counts the blow: with several players, another's game running the same
            // damage would have charged it twice.
            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null || attacker != Player.m_localPlayer || Time.time - _lastPenalty < 5f) return;
            _lastPenalty = Time.time;
            Settlement settlement = SimHost.Sim.State.GetSettlement(settlementId);
            Faction faction = settlement != null ? SimHost.Sim.State.GetFaction(settlement.FactionId) : null;
            if (faction == null) return;
            Net.NamePlayer(faction, attacker.GetPlayerID(), attacker.GetPlayerName());
            Net.PlayerDamagedProperty(faction, attacker.GetPlayerID());
        }
    }
}
