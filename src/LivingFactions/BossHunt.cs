using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The great beasts of the world and the factions. A chief can set the player a hunt: the next
    /// beast the player has not yet been paid for, with a bounty that grows with how far down the
    /// list it is. Whoever brings word of the kill is paid by any chief of that faction. Killing a
    /// boss is also news in itself: the clans are glad of it, while the races mourn their own (the
    /// Greydwarfs their Elder, the Draugr Bonemass, the Fulings Yagluth).
    /// What a player has taken on and been paid for is kept on the character.
    /// </summary>
    internal static class BossHunt
    {
        private static readonly string[] Bosses = { "Eikthyr", "gd_king", "Bonemass", "Dragon", "GoblinKing", "SeekerQueen", "Fader" };

        /// <summary>The boss each race holds dear: killing it angers them, and they never pay for it.</summary>
        private static readonly Dictionary<string, string> Kin = new Dictionary<string, string>
        {
            { "greydwarf_brood", "gd_king" }, { "draugr_court", "Bonemass" }, { "fuling_horde", "GoblinKing" },
        };

        private const string HuntKey = "lf_hunt", DoneKey = "lf_hunt_done", PaidKey = "lf_hunt_paid_";

        public static int Bounty(string boss)
        {
            return 150 * (System.Array.IndexOf(Bosses, boss) + 1);
        }

        public static string BossName(string boss)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(boss) : null;
            Character c = prefab != null ? prefab.GetComponent<Character>() : null;
            return c != null && Localization.instance != null ? Localization.instance.Localize(c.m_name) : boss;
        }

        public static bool OffersHunts(Faction f)
        {
            return f != null && Defs.Culture(f.CultureId).Group != "greydwarf";
        }

        /// <summary>The beast this faction would pay this player to kill next, or null.</summary>
        public static string Offer(Faction f, Player player)
        {
            string kin;
            Kin.TryGetValue(f.CultureId, out kin);
            foreach (string boss in Bosses)
            {
                if (boss == kin || player.m_customData.ContainsKey(PaidKey + boss)) continue;
                return boss;
            }
            return null;
        }

        /// <summary>The hunt the player has taken on: (boss, faction id), or null.</summary>
        public static KeyValuePair<string, int>? Current(Player player)
        {
            string value;
            if (player == null || !player.m_customData.TryGetValue(HuntKey, out value)) return null;
            string[] parts = value.Split('|');
            int faction;
            if (parts.Length < 2 || !int.TryParse(parts[1], out faction)) return null;
            return new KeyValuePair<string, int>(parts[0], faction);
        }

        public static bool Done(Player player)
        {
            KeyValuePair<string, int>? hunt = Current(player);
            string done;
            return hunt != null && player.m_customData.TryGetValue(DoneKey, out done) && done == hunt.Value.Key;
        }

        public static void Take(Player player, Faction f, string boss)
        {
            player.m_customData[HuntKey] = boss + "|" + f.Id;
            player.m_customData.Remove(DoneKey);
        }

        /// <summary>Pays the bounty from the settlement's coffer. False when the coffer cannot cover it.</summary>
        public static bool Claim(Player player, Settlement s, Faction f)
        {
            KeyValuePair<string, int>? hunt = Current(player);
            if (hunt == null || !Done(player) || hunt.Value.Value != f.Id) return false;
            int coins = Bounty(hunt.Value.Key);
            if (s.Stock[(int)Res.Coins] < coins) return false;
            GameObject coin = ObjectDB.instance.GetItemPrefab("Coins");
            if (coin == null) return false;
            Net.AddStock(s, Res.Coins, -coins);
            int left = coins;
            while (left > 0)
            {
                int stack = Mathf.Min(left, 999);
                if (!player.GetInventory().AddItem(coin, stack)) ItemDrop.DropItem(coin.GetComponent<ItemDrop>().m_itemData.Clone(), stack, player.transform.position + Vector3.up, Quaternion.identity);
                left -= stack;
            }
            Net.AddReputation(f, player.GetPlayerID(), 10f);
            player.m_customData[PaidKey + hunt.Value.Key] = "1";
            player.m_customData.Remove(HuntKey);
            player.m_customData.Remove(DoneKey);
            return true;
        }

        /// <summary>A boss has died. The player who was there is credited: the hunt is done, and every faction hears.</summary>
        public static void BossKilled(Character boss)
        {
            Player me = Player.m_localPlayer;
            if (!SimHost.Active || me == null || Utils.DistanceXZ(me.transform.position, boss.transform.position) > 100f) return;
            string name = Utils.GetPrefabName(boss.gameObject);
            if (System.Array.IndexOf(Bosses, name) < 0) return;
            KeyValuePair<string, int>? hunt = Current(me);
            if (hunt != null && hunt.Value.Key == name) me.m_customData[DoneKey] = name;
            long pid = me.GetPlayerID();
            foreach (Faction f in SimHost.Sim.State.Factions)
            {
                if (f.Eliminated) continue;
                string kin;
                string group = Defs.Culture(f.CultureId).Group;
                float delta = Kin.TryGetValue(f.CultureId, out kin) ? (kin == name ? -25f : 0f) : group == "human" ? 10f : 5f;
                if (delta != 0f) Net.AddReputation(f, pid, delta);
            }
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_boss_slain", BossName(name)));
        }
    }
}
