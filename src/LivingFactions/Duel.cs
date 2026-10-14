using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// A practice fight for checking the relic properties with real swings (dev tools only):
    ///   duel &lt;creature&gt; &lt;item&gt; [property] [health%]  hand the player the item (marked), put a still creature in front
    ///   swing [secondary]                         one real attack of the weapon in hand
    ///   duel                                      what the swings did: blows, damage, healing, wear, stamina
    /// </summary>
    internal static class Duel
    {
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        private static Character _target;
        private static ItemDrop.ItemData _weapon;
        private static float _targetStart, _healthStart, _durabilityStart;
        /// <summary>Stamina the player was asked for since the duel began, after the relic's discount (the god mode does not pay it, but is asked).</summary>
        public static float Stamina;
        private static int _swings;
        public static readonly List<float> Blows = new List<float>();

        public static string Command(string[] a)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return "no player";
            if (a.Length < 3) return Report();

            GameObject item = ObjectDB.instance.GetItemPrefab(a[2]);
            GameObject creature = ZNetScene.instance.GetPrefab(a[1]);
            if (item == null || creature == null) return "no such " + (item == null ? "item " + a[2] : "creature " + a[1]);
            if (_target != null) ZNetScene.instance.Destroy(_target.gameObject);

            me.UnequipAllItems();
            // The last duel's weapon goes, or the bag fills up and the new one never reaches the hand.
            if (_weapon != null) me.GetInventory().RemoveItem(_weapon);
            _weapon = item.GetComponent<ItemDrop>().m_itemData.Clone();
            _weapon.m_dropPrefab = item;
            _weapon.m_stack = 1;
            if (a.Length > 3 && a[3] != "none") _weapon.m_customData[Wares.RelicKey] = a[3];
            _weapon.m_durability = _weapon.GetMaxDurability() * 0.5f;
            if (!me.GetInventory().AddItem(_weapon) || !me.EquipItem(_weapon)) return "could not put " + a[2] + " in the hand (bag full?)";

            // A master of the weapon: the game's random spread of an unskilled blow would hide a quarter more or less.
            Skills.Skill skill = Traverse.Create(me.GetSkills()).Method("GetSkill", _weapon.m_shared.m_skillType).GetValue<Skills.Skill>();
            if (skill != null) skill.m_level = 100f;

            // Health below full, so healing shows; below a third, so fury wakes.
            float share = a.Length > 4 ? float.Parse(a[4], Inv) / 100f : 0.5f;
            me.SetHealth(me.GetMaxHealth() * share);

            Vector3 forward = me.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 spot = me.transform.position + forward * 1.6f;
            _target = Object.Instantiate(creature, spot, Quaternion.LookRotation(-forward)).GetComponent<Character>();
            // It stands still and takes the blows.
            foreach (MonoBehaviour ai in _target.GetComponents<BaseAI>()) ai.enabled = false;
            _target.SetMaxHealth(100000f);
            _target.SetHealth(100000f);
            _targetStart = _target.GetHealth();
            _healthStart = me.GetHealth();
            _durabilityStart = _weapon.m_durability;
            Stamina = 0f;
            _swings = 0;
            Blows.Clear();
            return "duel: " + a[2] + " [" + (Wares.AffixOf(_weapon) ?? "no mark") + "] against " + a[1] + ", health " + me.GetHealth().ToString("0", Inv) + "/" + me.GetMaxHealth().ToString("0", Inv);
        }

        public static string Swing(string[] a)
        {
            Player me = Player.m_localPlayer;
            if (me == null || _target == null) return "no duel";
            Vector3 dir = _target.transform.position - me.transform.position;
            dir.y = 0f;
            me.transform.rotation = Quaternion.LookRotation(dir);
            me.SetLookDir(dir.normalized);
            me.AddStamina(me.GetMaxStamina());
            bool started = me.StartAttack(_target, a.Length > 1 && a[1] == "secondary");
            if (started) _swings++;
            return started ? "swing " + _swings : "could not swing";
        }

        public static string Report()
        {
            Player me = Player.m_localPlayer;
            if (me == null || _target == null || _weapon == null) return "no duel";
            float dealt = 0f;
            foreach (float b in Blows) dealt += b;
            return "swings " + _swings + ", blows landed " + Blows.Count
                + ", damage sent " + dealt.ToString("0.0", Inv) + " (" + (Blows.Count > 0 ? dealt / Blows.Count : 0f).ToString("0.0", Inv) + " a blow)"
                + ", taken by the target " + (_targetStart - _target.GetHealth()).ToString("0.0", Inv)
                + ", player health " + _healthStart.ToString("0.0", Inv) + " -> " + me.GetHealth().ToString("0.0", Inv)
                + ", wear " + (_durabilityStart - _weapon.m_durability).ToString("0.00", Inv) + " of " + _weapon.GetMaxDurability().ToString("0", Inv)
                + ", stamina " + Stamina.ToString("0.00", Inv) + " (" + (_swings > 0 ? Stamina / _swings : 0f).ToString("0.00", Inv) + " a swing)";
        }

        public static void Reset()
        {
            _target = null;
            _weapon = null;
            Blows.Clear();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    internal static class DuelStaminaPatch
    {
        // After the merchant's discount (a prefix): what the swing really asks for.
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Player __instance, float v)
        {
            if (ModConfig.DevTools.Value && __instance == Player.m_localPlayer) Duel.Stamina += v;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class DuelBlowPatch
    {
        /// <summary>Notes every blow the local player sends at the practice target, as it leaves the weapon.</summary>
        private static void Prefix(Character __instance, HitData hit)
        {
            if (!ModConfig.DevTools.Value || hit == null || hit.GetAttacker() != Player.m_localPlayer) return;
            Duel.Blows.Add(hit.GetTotalDamage());
        }
    }
}
