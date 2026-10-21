using System;
using System.Collections.Generic;
using System.Reflection;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Settlements keep their ground clean: every few minutes the items lying loose on the ground
    /// within a village, camp, war camp or outpost are taken away (the remains of fights, drops of
    /// passing raiders), so they do not pile up over the hours. Only loaded items are touched, and
    /// only by the player who builds the settlement. Anything that fell in the last minute, lies
    /// near a player, has been placed as a piece or lies in a player's own base stays.
    /// </summary>
    internal static class LootSweep
    {
        private const double MinAgeSeconds = 60.0;
        private const float PlayerKeepRange = 8f;
        private const int DestroysPerFrame = 20;

        private static readonly FieldInfo InstancesField = typeof(ItemDrop).GetField("s_instances", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Queue<ItemDrop> Pending = new Queue<ItemDrop>();
        private static float _next;
        private static int _removed;

        public static void Reset()
        {
            Pending.Clear();
            _next = 0f;
            _removed = 0;
        }

        public static void Update()
        {
            if (!SimHost.Active || ZNetScene.instance == null || ZNet.instance == null) return;
            if (Pending.Count > 0)
            {
                // Taken away a few at a time, re-checked just before: a player may have come close meanwhile.
                for (int n = 0; n < DestroysPerFrame && Pending.Count > 0; n++)
                {
                    ItemDrop item = Pending.Dequeue();
                    if (item != null && Remove(item)) _removed++;
                }
                if (Pending.Count == 0 && _removed > 0)
                {
                    Plugin.Log.LogInfo("[loot sweep] " + _removed + " loose items taken off settlement ground");
                    _removed = 0;
                }
                return;
            }
            if (!ModConfig.LootSweep.Value) return;
            if (_next == 0f) _next = Time.time + ModConfig.LootSweepMinutes.Value * 60f;
            if (Time.time < _next) return;
            _next = Time.time + ModConfig.LootSweepMinutes.Value * 60f;

            Counts counts = new Counts();
            foreach (ItemDrop item in Collect(Mine(), false, counts)) Pending.Enqueue(item);
        }

        /// <summary>The dev <c>sweep</c> command: sweeps now, all at once, and says what was kept and why.</summary>
        public static string Command(Settlement only, bool ignoreAge)
        {
            if (!SimHost.Active || ZNetScene.instance == null) return "no simulation";
            if (InstancesField == null) return "ItemDrop.s_instances not found";
            List<Settlement> where = new List<Settlement>();
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (!s.Destroyed && (only == null || s == only)) where.Add(s);
            }
            Counts counts = new Counts();
            int removed = 0;
            foreach (ItemDrop item in Collect(where, ignoreAge, counts))
            {
                if (Remove(item)) removed++;
            }
            return "sweep " + (only != null ? only.Name : where.Count + " settlements") + ": removed " + removed
                + ", kept young " + counts.Young + ", near player " + counts.NearPlayer + ", placed " + counts.Placed
                + ", player base " + counts.PlayerBase + ", outside " + counts.Outside + ", loaded items " + counts.Loaded;
        }

        /// <summary>How far from its centre a settlement's ground reaches.</summary>
        public static float Reach(Settlement s)
        {
            float reach = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 12f;
            if (s.Tier >= Tier.City) reach = Mathf.Max(reach, Defs.CityWallRadius + 6f);
            return reach;
        }

        private class Counts
        {
            public int Loaded, Young, NearPlayer, Placed, PlayerBase, Outside;
        }

        /// <summary>The settlements this player puts up in the world, and so also keeps clean.</summary>
        private static List<Settlement> Mine()
        {
            List<Settlement> mine = new List<Settlement>();
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (!s.Destroyed && Net.IsBuilder(s)) mine.Add(s);
            }
            return mine;
        }

        private static List<ItemDrop> Collect(List<Settlement> where, bool ignoreAge, Counts counts)
        {
            List<ItemDrop> found = new List<ItemDrop>();
            List<ItemDrop> loaded = InstancesField != null ? InstancesField.GetValue(null) as List<ItemDrop> : null;
            if (where.Count == 0 || loaded == null) return found;

            List<KeyValuePair<Vector3, float>> grounds = new List<KeyValuePair<Vector3, float>>();
            foreach (Settlement s in where)
            {
                float reach = Reach(s);
                grounds.Add(new KeyValuePair<Vector3, float>(new Vector3(s.Pos.X, 0f, s.Pos.Z), reach * reach));
            }
            List<KeyValuePair<Vector3, float>> bases = PlayerBases();

            DateTime now = ZNet.instance.GetTime();
            // A copy: destroying items rearranges the game's own list.
            foreach (ItemDrop item in loaded.ToArray())
            {
                if (item == null) continue;
                counts.Loaded++;
                Vector3 p = item.transform.position;
                if (!Inside(grounds, p))
                {
                    counts.Outside++;
                    continue;
                }
                ZNetView view = item.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                ZDO zdo = view.GetZDO();
                // The settlement's own things (and live fish in a pond) are not loot.
                if (zdo.GetInt(Materializer.HashSettlement) != 0 || item.GetComponent<Fish>() != null) continue;
                if (item.IsPiece() || zdo.GetBool(ZDOVars.s_piece) || item.transform.parent != null)
                {
                    counts.Placed++;
                    continue;
                }
                long spawned = zdo.GetLong(ZDOVars.s_spawnTime, 0L);
                if (!ignoreAge && (spawned == 0L || (now - new DateTime(spawned)).TotalSeconds < MinAgeSeconds))
                {
                    counts.Young++;
                    continue;
                }
                if (Player.IsPlayerInRange(p, PlayerKeepRange))
                {
                    counts.NearPlayer++;
                    continue;
                }
                if (Inside(bases, p))
                {
                    counts.PlayerBase++;
                    continue;
                }
                found.Add(item);
            }
            return found;
        }

        private static bool Inside(List<KeyValuePair<Vector3, float>> circles, Vector3 p)
        {
            foreach (KeyValuePair<Vector3, float> c in circles)
            {
                float dx = p.x - c.Key.x, dz = p.z - c.Key.z;
                if (dx * dx + dz * dz <= c.Value) return true;
            }
            return false;
        }

        /// <summary>The areas of a player's own workbench or fire: what lies there is theirs.</summary>
        private static List<KeyValuePair<Vector3, float>> PlayerBases()
        {
            List<KeyValuePair<Vector3, float>> bases = new List<KeyValuePair<Vector3, float>>();
            foreach (EffectArea area in EffectArea.GetAllAreas())
            {
                if (area == null || (area.m_type & EffectArea.Type.PlayerBase) == 0) continue;
                Piece piece = area.GetComponentInParent<Piece>();
                if (piece == null || piece.GetCreator() == 0L) continue;
                ZNetView view = piece.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.GetZDO().GetInt(Materializer.HashSettlement) != 0) continue;
                float radius = area.GetRadius();
                bases.Add(new KeyValuePair<Vector3, float>(area.transform.position, radius * radius));
            }
            return bases;
        }

        private static bool Remove(ItemDrop item)
        {
            ZNetView view = item.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (Player.IsPlayerInRange(item.transform.position, PlayerKeepRange)) return false;
            view.ClaimOwnership();
            ZNetScene.instance.Destroy(item.gameObject);
            return true;
        }
    }
}
