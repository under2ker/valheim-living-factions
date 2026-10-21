using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The simulation's bands on the move (Squads.cs) become people in the world when a player is near
    /// them: up to six of them, walking the way the band goes. They are spawned by the player nearest
    /// the band, follow the band's place in the simulation and go when it is out of sight. Killing one
    /// costs the band a man.
    /// </summary>
    internal static class SquadPresence
    {
        private const float SpawnRange = 130f;
        private const float KeepRange = 200f;
        private static float _next;

        public static void Reset() { _next = 0f; }

        public static void Update()
        {
            if (!SimHost.Active || Time.time < _next || ZNetScene.instance == null || ZoneSystem.instance == null) return;
            _next = Time.time + 3f;
            Player me = Player.m_localPlayer;
            if (me == null) return;
            HashSet<int> present = new HashSet<int>();
            foreach (FactionMember m in FactionMember.All) if (m.SquadId != 0) present.Add(m.SquadId);
            foreach (Squad q in SimHost.Sim.State.Squads)
            {
                if (q.State == SquadState.Done || q.State == SquadState.Gathering || present.Contains(q.Id) || q.Men < 0.6f) continue;
                Vector3 at = new Vector3(q.Pos.X, 0f, q.Pos.Z);
                if (Utils.DistanceXZ(me.transform.position, at) > SpawnRange || !NearestPlayerIsMe(at)) continue;
                if (!ZoneSystem.instance.IsZoneLoaded(at) || !ZNetScene.instance.IsAreaReady(at)) continue;
                Spawn(q, at);
            }
        }

        private static bool NearestPlayerIsMe(Vector3 at)
        {
            float mine = Utils.DistanceXZ(Player.m_localPlayer.transform.position, at);
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p != Player.m_localPlayer && Utils.DistanceXZ(p.transform.position, at) < mine) return false;
            }
            return true;
        }

        private static void Spawn(Squad q, Vector3 at)
        {
            Faction faction = SimHost.Sim.State.GetFaction(q.FactionId);
            GameObject prefab = faction != null ? Npcs.PrefabFor(faction) : null;
            if (prefab == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(q.Men), 1, 6);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = at + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
                float ground;
                if (!ZoneSystem.instance.GetGroundHeight(pos, out ground) || ground < ZoneSystem.c_WaterLevel) continue;
                pos.y = ground + 0.3f;
                GameObject go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                FactionMember member = go.GetComponent<FactionMember>();
                if (member == null || go.GetComponent<ZNetView>() == null || go.GetComponent<ZNetView>().GetZDO() == null)
                {
                    Object.Destroy(go);
                    continue;
                }
                member.Expire(900);
                member.JoinSquad(q.Id);
                member.Setup(q.FactionId, q.HomeId, NpcRole.Squad, Random.Range(1, int.MaxValue));
            }
        }

        public static Squad Find(int id)
        {
            foreach (Squad q in SimHost.Sim.State.Squads) if (q.Id == id) return q;
            return null;
        }

        /// <summary>Where the band is heading now, in the world.</summary>
        public static bool Destination(Squad q, out Vector3 dest, out bool arrived)
        {
            dest = Vector3.zero;
            arrived = false;
            Vec2 to;
            switch (q.State)
            {
                case SquadState.Outbound: to = q.Target; break;
                case SquadState.AtTarget: to = q.Target; arrived = true; break;
                case SquadState.Returning:
                    Settlement home = SimHost.Sim.State.GetSettlement(q.HomeId);
                    if (home == null) return false;
                    to = home.Pos;
                    break;
                default: return false;
            }
            dest = new Vector3(to.X, 0f, to.Z);
            return true;
        }

        public static float KeepDistance { get { return KeepRange; } }
    }
}
