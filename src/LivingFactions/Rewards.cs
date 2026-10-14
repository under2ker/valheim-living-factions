using System.Collections.Generic;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The relic axes that are not sold. Each comes its own way, and each only once to a player:
    ///   Raven's Wing   a Raven Clan jarl's thanks to whoever strikes down two raiders attacking one of their places
    ///   Blackgnaw      taken from a Fuling chieftain
    ///   Rune Cleaver   a gift of a Dvergr thane to an ally of long standing (standing 80+), asked for in talk
    ///   Rootbiter      carried by the chief of a Greydwarf brood
    ///   Barrow Moon    lies with the chief of the Draugr Court
    /// What the player has been given is kept on the character, so it holds in any world.
    /// </summary>
    internal static class Rewards
    {
        public const string RavenAxe = "LF_RavenWingAxe";
        public const string FulingAxe = "LF_BlackgnawAxe";
        public const string DvergrAxe = "LF_RuneAxe";
        public const string GreydwarfAxe = "LF_RootAxe";
        public const string DraugrAxe = "LF_BarrowMoonAxe";
        public const int ThaneStanding = 80;

        /// <summary>Chief's culture -> the axe they carry and its property.</summary>
        private static readonly Dictionary<string, KeyValuePair<string, string>> Trophies = new Dictionary<string, KeyValuePair<string, string>>
        {
            { "fuling_horde", new KeyValuePair<string, string>(FulingAxe, "fury") },
            { "greydwarf_brood", new KeyValuePair<string, string>(GreydwarfAxe, "living") },
            { "draugr_court", new KeyValuePair<string, string>(DraugrAxe, "thirst") },
        };

        /// <summary>Raven Clan places the player is fighting for: settlement -> (until when, raiders the player has struck down).</summary>
        private static readonly Dictionary<int, KeyValuePair<float, int>> RavenRaids = new Dictionary<int, KeyValuePair<float, int>>();
        private static float _nextMend;

        public static void Reset()
        {
            RavenRaids.Clear();
        }

        public static bool Has(Player player, string prefab)
        {
            return player != null && player.m_customData.ContainsKey("lf_given_" + prefab);
        }

        /// <summary>Puts the relic into the player's bag (or at their feet when it is full) and remembers it was given.</summary>
        public static bool Give(Player player, string prefab, string affix, Faction from, Vector3 dropAt)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            if (player == null || item == null || Has(player, prefab)) return false;
            ItemDrop.ItemData data = item.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = item;
            data.m_stack = 1;
            data.m_durability = data.GetMaxDurability();
            data.m_customData[Wares.RelicKey] = affix;
            data.m_crafterName = Texts.FactionName(from);
            player.m_customData["lf_given_" + prefab] = "1";
            if (player == Player.m_localPlayer && player.GetInventory().AddItem(data)) return true;
            ItemDrop.DropItem(data, 1, dropAt + Vector3.up, Quaternion.identity);
            return true;
        }

        /// <summary>A chieftain of a race has fallen to a player: the axe they carried is left on the ground.</summary>
        public static void ChiefKilled(FactionMember chief, Player killer)
        {
            if (chief.Role != NpcRole.Chief || killer != Player.m_localPlayer) return;
            Faction f = SimHost.Sim.State.GetFaction(chief.FactionId);
            KeyValuePair<string, string> trophy;
            if (f == null || !Trophies.TryGetValue(f.CultureId, out trophy) || Has(killer, trophy.Key)) return;
            if (Give(killer, trophy.Key, trophy.Value, f, chief.transform.position) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_trophy", Texts.FactionName(f)));
        }

        /// <summary>
        /// A raider has fallen to this player near a settlement. Two of them at a Raven Clan place within
        /// ten minutes is help the jarl remembers, whichever way the raid goes: the simulation settles
        /// its outcome when it starts, the player's part is the fight they put up.
        /// </summary>
        public static void RaiderKilled(Settlement near, Player killer)
        {
            if (killer != Player.m_localPlayer || near == null || Has(killer, RavenAxe)) return;
            Faction f = SimHost.Sim.State.GetFaction(near.FactionId);
            if (f == null || f.CultureId != "clan_raven") return;
            KeyValuePair<float, int> fight;
            if (!RavenRaids.TryGetValue(near.Id, out fight) || Time.time > fight.Key) fight = new KeyValuePair<float, int>(Time.time + 600f, 0);
            int count = fight.Value + 1;
            RavenRaids[near.Id] = new KeyValuePair<float, int>(fight.Key, count);
            if (count < 2) return;
            RavenRaids.Remove(near.Id);
            if (Give(killer, RavenAxe, "keen", f, killer.transform.position) && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_jarl_gift", Texts.Name(near.Name)));
        }

        /// <summary>A Dvergr thane's gift is there for the asking once the player is a long-standing ally.</summary>
        public static bool ThaneWillGive(Faction f, PlayerRelation rel, Player player)
        {
            return f != null && f.CultureId == "dvergr_hold" && rel.Allied && rel.Reputation >= ThaneStanding && !Has(player, DvergrAxe);
        }

        public static bool ThaneGift(Faction f, Player player)
        {
            return Give(player, DvergrAxe, "lasting", f, player.transform.position);
        }

        /// <summary>Rootbiter mends itself while it is in the hand.</summary>
        public static void Update()
        {
            Player me = Player.m_localPlayer;
            if (me == null || Time.time < _nextMend) return;
            _nextMend = Time.time + 5f;
            ItemDrop.ItemData weapon = me.GetCurrentWeapon();
            if (Wares.AffixOf(weapon) != "living") return;
            float max = weapon.GetMaxDurability();
            weapon.m_durability = Mathf.Min(max, weapon.m_durability + max * 0.02f);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class ThirstPatch
    {
        /// <summary>Barrow Moon: a twentieth of every blow's damage heals whoever strikes with it.</summary>
        private static void Prefix(HitData hit)
        {
            Player me = Player.m_localPlayer;
            if (hit == null || me == null || hit.GetAttacker() != me || Wares.AffixOf(me.GetCurrentWeapon()) != "thirst") return;
            float dealt = hit.GetTotalDamage();
            if (dealt > 0f) me.Heal(dealt * 0.05f, true);
        }
    }
}
