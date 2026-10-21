using System.Collections.Generic;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Who a faction's people are angry with, in the world, right now. The simulation keeps standing;
    /// this keeps the heat of the moment:
    ///  - a player's blows are counted per faction, and only after a number of them that depends on
    ///    standing (five for an ally, one for someone already wary) do its people turn on the player;
    ///    anything else that strikes one of them is an enemy at once;
    ///  - the anger is tied to where it happened: the people round there fight the offender, follow
    ///    only so far from that spot, and take it up again if the offender comes back before it cools;
    ///  - a killing angers everyone near at once and for longer;
    ///  - allied factions' people nearby join in, and friends of the player stand by the player when
    ///    something attacks the player.
    /// The anger lives on the machine that runs the people's AI (the host or the player building there).
    /// </summary>
    internal static class Grudges
    {
        private class Grudge
        {
            public int Hits;
            public float LastHit = -999f;
            public float AngryUntil = -999f;
            public Vector3 Where;
        }

        private class Defence
        {
            public Vector3 Where;
            public float Until;
        }

        private static readonly Dictionary<string, Grudge> ByKey = new Dictionary<string, Grudge>();
        private static readonly Dictionary<Character, Defence> PlayerAttackers = new Dictionary<Character, Defence>();

        /// <summary>A blow is forgotten if no other follows within this many seconds.</summary>
        private const float CalmSeconds = 90f;

        public static void Reset()
        {
            ByKey.Clear();
            PlayerAttackers.Clear();
        }

        private static string Key(int factionId, Character who)
        {
            ZNetView view = who.GetComponent<ZNetView>();
            return factionId + ":" + (view != null && view.GetZDO() != null ? view.GetZDO().m_uid.ToString() : who.GetInstanceID().ToString());
        }

        /// <summary>How many blows the faction takes from this player before its people fight back.</summary>
        public static int Patience(Faction f, Player player)
        {
            switch (SimHost.Sim.GetStanding(f, player.GetPlayerID()))
            {
                case Standing.Allied: return ModConfig.AngerHitsAllied.Value;
                case Standing.Friendly: return ModConfig.AngerHitsFriendly.Value;
                case Standing.Neutral: return ModConfig.AngerHitsNeutral.Value;
                case Standing.Wary: return ModConfig.AngerHitsWary.Value;
                default: return 0;
            }
        }

        /// <summary>One of the faction's people was struck (on the machine that runs it). Returns true when that made them angry.</summary>
        public static bool OnHit(FactionMember victim, Character attacker)
        {
            if (!SimHost.Active || victim == null || attacker == null || attacker == victim.Humanoid) return false;
            FactionMember other = FactionMember.Of(attacker);
            // Friends sparring by accident, or one of their own: no feud.
            if (other != null && (other.FactionId == victim.FactionId || SimHost.Sim.AreAllied(other.FactionId, victim.FactionId))) return false;
            Faction f = SimHost.Sim.State.GetFaction(victim.FactionId);
            if (f == null) return false;
            Grudge g = Get(f.Id, attacker);
            if (Time.time - g.LastHit > CalmSeconds) g.Hits = 0;
            g.Hits++;
            g.LastHit = Time.time;
            g.Where = victim.transform.position;
            Player player = attacker as Player;
            int needed = player != null ? Patience(f, player) : 1;
            if (g.Hits < needed) return false;
            bool fresh = Time.time > g.AngryUntil;
            g.AngryUntil = Time.time + ModConfig.GrudgeMinutes.Value * 60f;
            return fresh;
        }

        /// <summary>One of the faction's people was killed: everyone near is angry at once, and longer.</summary>
        public static void OnKilled(FactionMember victim, Character killer, bool chief)
        {
            if (!SimHost.Active || victim == null || killer == null) return;
            Grudge g = Get(victim.FactionId, killer);
            g.Hits = Mathf.Max(g.Hits, 99);
            g.LastHit = Time.time;
            g.Where = victim.transform.position;
            g.AngryUntil = Time.time + ModConfig.GrudgeMinutes.Value * 60f * (chief ? 4f : 2f);
        }

        /// <summary>The night watch decides to drive a stranger off.</summary>
        public static void Declare(int factionId, Character offender, Vector3 where, float seconds)
        {
            Grudge g = Get(factionId, offender);
            g.Where = where;
            g.LastHit = Time.time;
            g.AngryUntil = Mathf.Max(g.AngryUntil, Time.time + seconds);
        }

        /// <summary>Someone attacked the player: friends of the player close by stand with the player for a while.</summary>
        public static void OnPlayerAttacked(Player victim, Character attacker)
        {
            if (!SimHost.Active || victim == null || attacker == null || attacker == victim) return;
            PlayerAttackers[attacker] = new Defence { Where = victim.transform.position, Until = Time.time + 30f };
        }

        /// <summary>Is this member angry with that character, here and now?</summary>
        public static bool AngryAt(FactionMember m, Character other)
        {
            if (m == null || other == null || ByKey.Count == 0) return false;
            if (Angry(m, m.FactionId, other, ModConfig.PursuitRange.Value)) return true;
            // Allies of the wronged faction join in, nearer the trouble.
            foreach (Faction f in SimHost.Sim.State.Factions)
            {
                if (f.Id != m.FactionId && !f.Eliminated && SimHost.Sim.AreAllied(f.Id, m.FactionId) && Angry(m, f.Id, other, ModConfig.HelpRange.Value)) return true;
            }
            return false;
        }

        private static bool Angry(FactionMember m, int factionId, Character other, float range)
        {
            Grudge g;
            if (!ByKey.TryGetValue(Key(factionId, other), out g) || Time.time > g.AngryUntil) return false;
            // They fight round the place it happened, and do not follow far from it: run off and they
            // go back to their work; come back before it cools and it starts again.
            return Utils.DistanceXZ(m.transform.position, g.Where) < range && Utils.DistanceXZ(other.transform.position, g.Where) < ModConfig.PursuitRange.Value;
        }

        /// <summary>Does this member stand with the local player against that attacker?</summary>
        public static bool DefendsAgainst(FactionMember m, Character other)
        {
            Defence d;
            if (m == null || other == null || !PlayerAttackers.TryGetValue(other, out d)) return false;
            if (Time.time > d.Until)
            {
                PlayerAttackers.Remove(other);
                return false;
            }
            Player player = Player.m_localPlayer;
            if (player == null || m.Role == NpcRole.Villager) return false;
            Faction f = SimHost.Sim.State.GetFaction(m.FactionId);
            if (f == null) return false;
            Standing st = SimHost.Sim.GetStanding(f, player.GetPlayerID());
            if (st != Standing.Allied && !(st == Standing.Friendly && ModConfig.FriendsDefend.Value)) return false;
            FactionMember attacker = FactionMember.Of(other);
            if (attacker != null && (attacker.FactionId == m.FactionId || SimHost.Sim.AreAllied(attacker.FactionId, m.FactionId))) return false;
            return Utils.DistanceXZ(m.transform.position, d.Where) < ModConfig.HelpRange.Value;
        }

        /// <summary>Anyone of the faction near enough and with a clear line of sight to see what happened.</summary>
        public static bool Witnessed(FactionMember victim, Vector3 where)
        {
            float range = ModConfig.WitnessRange.Value;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m == victim || m.Humanoid == null || m.Humanoid.IsDead() || m.FactionId != victim.FactionId) continue;
                Vector3 eye = m.transform.position + Vector3.up * 1.6f;
                if (Vector3.Distance(eye, where) > range) continue;
                if (!Physics.Linecast(eye, where + Vector3.up * 1f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain"))) return true;
            }
            return false;
        }

        public static string Describe()
        {
            int angry = 0;
            foreach (Grudge g in ByKey.Values) if (Time.time < g.AngryUntil) angry++;
            return "grudges " + ByKey.Count + ", angry " + angry + ", defending the player against " + PlayerAttackers.Count;
        }

        public static string DescribeFor(int factionId, Character who)
        {
            Grudge g;
            if (!ByKey.TryGetValue(Key(factionId, who), out g)) return "no grudge";
            return "hits " + g.Hits + (Time.time < g.AngryUntil ? ", angry for " + (g.AngryUntil - Time.time).ToString("0") + " s" : ", calm");
        }

        private static Grudge Get(int factionId, Character who)
        {
            string key = Key(factionId, who);
            Grudge g;
            if (!ByKey.TryGetValue(key, out g)) ByKey[key] = g = new Grudge();
            return g;
        }
    }

    /// <summary>Blows land where the struck character is run: count them there, for people and for players.</summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class GrudgeDamagePatch
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            if (!SimHost.Active || hit == null || hit.GetTotalDamage() <= 0f) return;
            Character attacker = hit.GetAttacker();
            if (attacker == null) return;
            Player player = __instance as Player;
            if (player != null)
            {
                Grudges.OnPlayerAttacked(player, attacker);
                return;
            }
            FactionMember member = FactionMember.Of(__instance);
            if (member == null || __instance.IsDead()) return;
            if (Grudges.OnHit(member, attacker)) member.Alarm(attacker);
            else if (attacker is Player) member.Protest();
        }
    }

    /// <summary>The game makes everything near a struck creature furious with the player; the people of the factions keep their own counsel (Grudges).</summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.SetAggravated))]
    internal static class NoVanillaAggroPatch
    {
        private static bool Prefix(BaseAI __instance, bool aggro)
        {
            return !aggro || FactionMember.Of(__instance.GetComponent<Character>()) == null;
        }
    }

    /// <summary>A struck member turns to face the attacker only if it is an enemy by now: an ally's stray blow is not a fight.</summary>
    [HarmonyPatch(typeof(MonsterAI), "OnDamaged")]
    internal static class StrayBlowPatch
    {
        private static void Postfix(MonsterAI __instance, Character attacker, ref Character ___m_targetCreature)
        {
            if (attacker == null || ___m_targetCreature != attacker) return;
            if (FactionMember.Of(__instance.GetComponent<Character>()) == null) return;
            if (!BaseAI.IsEnemy(__instance.GetComponent<Character>(), attacker)) ___m_targetCreature = null;
        }
    }
}
