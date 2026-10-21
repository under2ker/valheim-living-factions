using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// A thief in the night. Opening one of a settlement's chests after dark starts a count: the time
    /// before someone stirs and sees what is going on. It is short when people sleep in that very
    /// house (ten seconds) and shorter still when someone awake is near; half a minute in a storehouse
    /// nobody sleeps in. Shown on screen, it runs on while the thief is inside the settlement; out of it
    /// in time, they got away (the loss is still found out in the morning, as theft always is). When it
    /// runs out the settlement is raised: everyone wakes, the watch and the people go for the thief.
    /// Only for a thief, though: one caught with the chest still open, or who took something of theirs
    /// (what a player put in themselves is theirs to take back, see Chests). A chest closed with nothing
    /// of theirs gone ends the count. Chests a player built (a trading chest in the village) are the
    /// player's own and start nothing.
    /// </summary>
    internal static class NightThief
    {
        public const float Quiet = 30f, SleepersNear = 10f, AwakeNear = 4f;

        private static int _settlement;
        private static float _due;
        private static int _shown = -1;
        private static Vector3 _where;
        private static Container _chest;
        private static bool _stole, _unchecked;

        public static bool Counting { get { return _settlement != 0; } }

        public static void Reset()
        {
            _settlement = 0;
            _shown = -1;
            _chest = null;
            _stole = _unchecked = false;
        }

        /// <summary>
        /// A chest of the settlement the local player had open has been looked over (Chests): whether
        /// anything of the household's or the stores' was gone. Nothing gone and no chest open: no thief.
        /// </summary>
        public static void Checked(int settlementId, bool stole)
        {
            if (settlementId != _settlement) return;
            _unchecked = false;
            if (stole) _stole = true;
            else if (!_stole && (_chest == null || !_chest.IsInUse()))
            {
                Plugin.Log.LogInfo("[night thief] the chest was closed with nothing of theirs taken");
                Reset();
            }
        }

        public static void Opened(Container chest, Humanoid who)
        {
            Player player = who as Player;
            if (!SimHost.Active || player == null || player != Player.m_localPlayer || !EnvMan.IsNight()) return;
            // Only the settlement's own chests: one the player put up carries no settlement mark.
            Settlement s = Property.OwnerOf(chest);
            if (s == null || s.Destroyed) return;
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            if (f == null) return;
            float delay = Quiet;
            Vector3 at = chest.transform.position;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m.SettlementId != s.Id || m.Humanoid == null || m.Humanoid.IsDead()) continue;
                float d = Vector3.Distance(m.transform.position, at);
                if (m.IsAsleep && d < 6f) delay = Mathf.Min(delay, SleepersNear);
                else if (!m.IsAsleep && d < 12f) delay = Mathf.Min(delay, AwakeNear);
            }
            // Already counting here: a second chest does not buy more time.
            if (_settlement == s.Id) _due = Mathf.Min(_due, Time.time + delay);
            else
            {
                _due = Time.time + delay;
                _stole = false;
            }
            _settlement = s.Id;
            _where = at;
            _shown = -1;
            _chest = chest;
            // Without the chests kept in step with the stores nobody can tell what was taken: as before, opening is enough.
            _unchecked = ModConfig.ChestsOn.Value;
            if (!ModConfig.ChestsOn.Value) _stole = true;
            Plugin.Log.LogInfo("[night thief] chest opened in " + s.Name + ", " + (_due - Time.time).ToString("0") + " s before someone notices");
        }

        public static void Update()
        {
            if (_settlement == 0 || !SimHost.Active) return;
            Player player = Player.m_localPlayer;
            Settlement s = SimHost.Sim.State.GetSettlement(_settlement);
            if (player == null || player.IsDead() || s == null || s.Destroyed)
            {
                Reset();
                return;
            }
            float reach = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 12f;
            if (Utils.DistanceXZ(player.transform.position, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > reach)
            {
                if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_thief_away"));
                Reset();
                return;
            }
            int left = Mathf.CeilToInt(_due - Time.time);
            if (left > 0)
            {
                if (left != _shown && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_thief_count", left));
                _shown = left;
                return;
            }
            bool inTheAct = _chest != null && _chest.IsInUse();
            if (!_stole && !inTheAct)
            {
                // The chest is closed and not looked over yet (that takes a few seconds): wait for it, not for ever.
                if (_unchecked && Time.time - _due < 8f) return;
                Reset();
                return;
            }
            Reset();
            Net.RaiseAlarm(s, player, _where);
        }

        /// <summary>The settlement wakes to a thief: on every player's game, so whoever runs its people's minds sends them.</summary>
        public static void Alarm(Settlement s, Character thief, Vector3 where)
        {
            if (s == null || thief == null) return;
            NightWatch.Raise(s.Id, where);
            Grudges.Declare(s.FactionId, thief, where, 180f);
            FactionMember shouter = null;
            float nearest = float.MaxValue;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m.SettlementId != s.Id) continue;
                m.Rouse();
                float d = Vector3.Distance(m.transform.position, where);
                if (d < nearest)
                {
                    nearest = d;
                    shouter = m;
                }
            }
            if (shouter != null && Chat.instance != null) Chat.instance.SetNpcText(shouter.gameObject, Vector3.up * 2.2f, 30f, 5f, "", Texts.Get("night_thief_shout"), false);
            if (thief == Player.m_localPlayer && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_thief_alarm", Texts.Name(s.Name)));
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class NightChestPatch
    {
        private static void Postfix(Container __instance, Humanoid character, bool hold, bool __result)
        {
            if (!__result || hold) return;
            if (character == Player.m_localPlayer) Chests.MarkOpened(__instance);
            NightThief.Opened(__instance, character);
        }
    }
}
