using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// People who are on the move rather than at home: caravans (a trader with goods on their back
    /// and an escort) that leave a settlement along its road or arrive by it, and fighters the
    /// player hires. None of them count as inhabitants; each has a time after which they are gone.
    /// </summary>
    internal static class Company
    {
        private const double CaravanSeconds = 260;
        private const double HireSeconds = 900;
        public const int MaxHired = 4;

        private static readonly Dictionary<int, float> NextCaravan = new Dictionary<int, float>();
        private static float _next;

        public static void Reset()
        {
            NextCaravan.Clear();
            _next = 0f;
        }

        public static int HirePrice(Settlement s) { return 120 + 40 * (int)s.Tier; }

        public static int HiredCount()
        {
            int n = 0;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m.Role == NpcRole.Mercenary) n++;
            }
            return n;
        }

        private static FactionMember Spawn(Settlement s, Vector3 pos, NpcRole role, double seconds, int seed = 0)
        {
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            GameObject prefab = faction != null ? Npcs.PrefabFor(faction) : null;
            float ground;
            if (prefab == null || !ZoneSystem.instance.IsZoneLoaded(pos) || !ZoneSystem.instance.GetGroundHeight(pos, out ground) || ground < ZoneSystem.c_WaterLevel) return null;
            pos.y = ground + 0.3f;
            GameObject go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            FactionMember member = go.GetComponent<FactionMember>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (member == null || view == null || view.GetZDO() == null)
            {
                Object.Destroy(go);
                return null;
            }
            member.Expire(seconds);
            member.Setup(s.FactionId, s.Id, role, seed != 0 ? seed : Random.Range(1, int.MaxValue));
            return member;
        }

        // ------------------------------------------------------------------ hired fighters

        /// <summary>Two fighters of the settlement join the player for a while. Returns the key of the reply.</summary>
        public static string Hire(Settlement s, Player player, bool free)
        {
            if (HiredCount() + 2 > MaxHired) return "r_hire_full";
            if (s.Soldiers < 2f) return "r_hire_none";
            int price = HirePrice(s);
            Inventory inv = player.GetInventory();
            if (!free)
            {
                if (inv.CountItems("$item_coins") < price) return "r_hire_poor";
                inv.RemoveItem("$item_coins", price);
                Net.AddStock(s, Res.Coins, price);
            }
            int joined = 0;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = player.transform.position - player.transform.forward * 2.5f + player.transform.right * (i == 0 ? -1.5f : 1.5f);
                if (Spawn(s, pos, NpcRole.Mercenary, HireSeconds) != null) joined++;
            }
            return joined > 0 ? "r_hired" : "r_hire_none";
        }

        /// <summary>
        /// For tests: one hired fighter who is certainly an archer (archers are the fighters whose
        /// seed divides by three), so a check of archery does not hang on luck.
        /// </summary>
        public static string DevArcher(Settlement s, Player player)
        {
            Vector3 pos = player.transform.position + player.transform.forward * 3f;
            FactionMember archer = Spawn(s, pos, NpcRole.Mercenary, 300, 3 * Random.Range(1, 100000));
            return archer != null ? "archer " + archer.DisplayName + " stands by, bow in the pack: " + archer.IsArcher : "no room for an archer";
        }

        // ------------------------------------------------------------------ caravans

        public static void Update()
        {
            if (Time.time < _next || !ModConfig.Caravans.Value) return;
            _next = Time.time + 5f;
            Player player = Player.m_localPlayer;
            if (player == null || EnvMan.IsNight()) return;

            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed || s.Population < 5f || !Net.IsBuilder(s)) continue;
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                if (Utils.DistanceXZ(player.transform.position, centre) > 110f || !ZNetScene.instance.IsAreaReady(centre)) continue;
                if (SimHost.Sim.IsHostileToPlayer(s.FactionId, player.GetPlayerID())) continue;
                float due;
                if (!NextCaravan.TryGetValue(s.Id, out due))
                {
                    // The first one shows up soon after the player arrives; then they come and go at their own pace.
                    NextCaravan[s.Id] = Time.time + Random.Range(40f, 90f);
                    continue;
                }
                if (Time.time < due || OnTheRoad(s.Id)) continue;
                NextCaravan[s.Id] = Time.time + Random.Range(240f, 420f);
                Send(s, Random.value < 0.5f);
            }
        }

        private static bool OnTheRoad(int settlementId)
        {
            foreach (FactionMember m in FactionMember.All)
            {
                if (m.Role == NpcRole.Trader && m.SettlementId == settlementId) return true;
            }
            return false;
        }

        /// <summary>A trader and two guards: leaving by the road, or coming in from it.</summary>
        public static string Send(Settlement s, bool arriving)
        {
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            float at = arriving ? 100f : 12f;
            Vector3 start = arriving ? Roads.Along(s, at) : Roads.Along(s, at);
            Vector3 ahead = Roads.Along(s, at + 6f) - start;
            ahead.y = 0f;
            Vector3 side = ahead.sqrMagnitude > 0.01f ? new Vector3(-ahead.z, 0f, ahead.x).normalized : Vector3.right;
            FactionMember trader = Spawn(s, start, NpcRole.Trader, CaravanSeconds);
            if (trader == null) return "no room for a caravan at " + start;
            trader.StartJourney(at, arriving);
            int guards = 0;
            foreach (float offset in new[] { -1.8f, 1.8f })
            {
                if (Spawn(s, start + side * offset - ahead.normalized * 1.5f, NpcRole.Escort, CaravanSeconds) != null) guards++;
            }
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get(arriving ? "msg_caravan_in" : "msg_caravan_out", Texts.Name(s.Name)));
            return "caravan " + (arriving ? "arriving at " : "leaving ") + s.Name + " with " + guards + " guards, from " + start + (Roads.HasRoad(s) ? " by road" : " (no road)");
        }
    }
}
