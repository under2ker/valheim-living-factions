using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>Settlements a player has found are remembered and marked on the map.</summary>
    internal static class Discovery
    {
        private static readonly List<Minimap.PinData> Pins = new List<Minimap.PinData>();
        private static float _nextCheck, _nextPins;

        public static void Reset()
        {
            Pins.Clear();
            MapZones.Clear();
            _nextPins = 0f;
            _look = "";
        }

        public static void Update()
        {
            if (!SimHost.Active || Player.m_localPlayer == null || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 2f;
            WorldState state = SimHost.Sim.State;
            long pid = Player.m_localPlayer.GetPlayerID();
            List<int> known;
            if (!state.Discovered.TryGetValue(pid, out known))
            {
                known = new List<int>();
                state.Discovered[pid] = known;
            }

            Vector3 me = Player.m_localPlayer.transform.position;
            bool changed = false;
            foreach (Settlement s in state.Settlements)
            {
                if (s.Destroyed || known.Contains(s.Id)) continue;
                if (Utils.DistanceXZ(me, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 90f) continue;
                known.Add(s.Id);
                Net.Discover(pid, s.Id);
                changed = true;
                Faction f = state.GetFaction(s.FactionId);
                if (MessageHud.instance != null)
                {
                    MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_enter", Texts.Name(s.Name), Texts.Get("tier_" + s.Tier), Texts.FactionName(f)));
                }
            }
            // Showing every settlement is the player's choice in the menu; otherwise only those they have found.
            List<int> shown = known;
            if (ModConfig.MapShowAll.Value)
            {
                shown = new List<int>();
                foreach (Settlement s in state.Settlements) if (!s.Destroyed) shown.Add(s.Id);
            }
            string look = ModConfig.MapZones.Value + "|" + ModConfig.MapShowAll.Value + "|" + Texts.Lang;
            if (look != _look) changed = true;
            _look = look;
            if (changed || Time.time >= _nextPins) RefreshPins(shown);
        }

        private static string _look = "";

        private static void RefreshPins(List<int> known)
        {
            _nextPins = Time.time + 15f;
            if (Minimap.instance == null) return;
            foreach (Minimap.PinData pin in Pins) Minimap.instance.RemovePin(pin);
            Pins.Clear();
            MapZones.Clear();
            WorldState state = SimHost.Sim.State;
            foreach (int id in known)
            {
                Settlement s = state.GetSettlement(id);
                if (s == null || s.Destroyed) continue;
                Faction f = state.GetFaction(s.FactionId);
                string label = Texts.Name(s.Name) + " (" + Texts.Get("tier_" + s.Tier) + ") — " + Texts.FactionName(f);
                MapZones.AddZone(s, f, Pins);
                Minimap.PinData mark = Minimap.instance.AddPin(new Vector3(s.Pos.X, 0f, s.Pos.Z), Minimap.PinType.Icon1, label, false, false);
                Pins.Add(mark);
                MapZones.Mark(mark, f);
            }
            MapZones.UpdateLegend(known);
        }
    }

    /// <summary>When a raid falls on a settlement a player is visiting, the attackers actually show up.</summary>
    internal static class Raids
    {
        /// <summary>Hooked into the simulation. Always lets the sim decide the outcome; the fight is what the player sees of it.</summary>
        public static bool Stage(Settlement from, Settlement target, float committed)
        {
            if (Player.m_localPlayer == null || ZNetScene.instance == null || !ModConfig.StagedRaids.Value) return false;
            Vector3 centre = new Vector3(target.Pos.X, 0f, target.Pos.Z);
            if (Utils.DistanceXZ(Player.m_localPlayer.transform.position, centre) > 170f || !ZNetScene.instance.IsAreaReady(centre)) return false;

            Faction attacker = SimHost.Sim.State.GetFaction(from.FactionId);
            GameObject prefab = attacker != null ? Npcs.PrefabFor(attacker) : null;
            if (prefab == null) return false;

            int count = Mathf.Clamp(Mathf.RoundToInt(committed), 2, 6);
            float direction = Random.Range(0f, Mathf.PI * 2f);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                float angle = direction + Random.Range(-0.4f, 0.4f);
                float dist = Random.Range(28f, 36f);
                Vector3 pos = centre + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                float ground;
                if (!ZoneSystem.instance.IsZoneLoaded(pos) || !ZoneSystem.instance.GetGroundHeight(pos, out ground)) continue;
                pos.y = ground + 0.3f;
                GameObject go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(centre - new Vector3(pos.x, 0f, pos.z)));
                FactionMember member = go.GetComponent<FactionMember>();
                if (member == null)
                {
                    Object.Destroy(go);
                    continue;
                }
                member.Setup(from.FactionId, from.Id, NpcRole.Raider, Random.Range(1, int.MaxValue));
                spawned++;
            }
            if (spawned > 0 && MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("msg_raid", Texts.FactionName(attacker), Texts.Name(target.Name)));
            }
            return false;
        }
    }
}
