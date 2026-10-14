using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Woodcutters really fell trees, and the settlement plants a sapling of the same kind in each
    /// stump's place, so the wood round a village is cleared in patches of young growth instead of
    /// vanishing. Only trees at least 10 m from every building are felled, away from the village, and
    /// only by the player who puts the settlement up (one felling per tree in a game for several).
    /// The wood itself is still counted by the simulation: the log is carried off, not left lying.
    /// </summary>
    internal static class Forestry
    {
        public const float KeepClear = 10f;
        public const int SwingsToFell = 8;

        private struct Pending
        {
            public Vector3 Pos;
            public string Sapling;
            public int SettlementId;
            public float LogsGoneAt, PlantAt;
        }

        private static readonly List<Pending> Queue = new List<Pending>();
        private static int _felled, _planted;

        public static void Reset()
        {
            Queue.Clear();
        }

        public static string Describe()
        {
            return "trees felled " + _felled + ", saplings planted " + _planted + ", waiting " + Queue.Count;
        }

        /// <summary>The sapling that grows into this kind of tree; null for trees nobody can plant again.</summary>
        public static string SaplingFor(TreeBase tree)
        {
            string name = Utils.GetPrefabName(tree.gameObject);
            string sapling = name.StartsWith("Beech") ? "Beech_Sapling" : name.StartsWith("Birch") ? "Birch_Sapling" : name.StartsWith("Oak") ? "Oak_Sapling"
                : name.StartsWith("Pine") ? "PineTree_Sapling" : name.StartsWith("FirTree") ? "FirTree_Sapling" : null;
            return sapling != null && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(sapling) != null ? sapling : null;
        }

        /// <summary>True when a tree may be felled for this settlement: far enough from its buildings, and replantable.</summary>
        public static bool MayFell(TreeBase tree, Settlement s)
        {
            if (tree == null || !ModConfig.TreeFelling.Value || SaplingFor(tree) == null) return false;
            Vector3 p = tree.transform.position;
            foreach (Building b in s.Buildings)
            {
                if (b.Plot < 0 || b.State == BuildingState.Destroyed) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
                if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < Blueprints.Radius(b.Kind) + KeepClear) return false;
            }
            return Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 12f + KeepClear;
        }

        /// <summary>The last swing: the tree comes down, falling away from the village.</summary>
        public static bool Fell(TreeBase tree, Settlement s)
        {
            if (!MayFell(tree, s) || !Net.IsBuilder(s)) return false;
            // Cleared for a building (or taken by the player) while the woodcutter was still at it.
            ZNetView view = tree.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            Vector3 p = tree.transform.position;
            // Nobody stands under it.
            Player me = Player.m_localPlayer;
            if (me != null && Utils.DistanceXZ(me.transform.position, p) < 8f) return false;
            string sapling = SaplingFor(tree);
            Vector3 away = new Vector3(p.x - s.Pos.X, 0f, p.z - s.Pos.Z).normalized;
            HitData hit = new HitData();
            hit.m_damage.m_chop = 100000f;
            hit.m_toolTier = 100;
            hit.m_point = p + Vector3.up;
            hit.m_dir = away;
            tree.Damage(hit);
            _felled++;
            Queue.Add(new Pending { Pos = p, Sapling = sapling, SettlementId = s.Id, LogsGoneAt = Time.time + 9f, PlantAt = Time.time + 12f });
            return true;
        }

        /// <summary>Carries the fallen trunk off and plants the sapling once the stump is cleared.</summary>
        public static void Update()
        {
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Pending job = Queue[i];
                if (Time.time < job.LogsGoneAt) continue;
                if (!ZoneSystem.instance.IsZoneLoaded(job.Pos))
                {
                    Queue.RemoveAt(i);
                    continue;
                }
                // The trunk (and whatever it broke into) is taken to the storehouse.
                foreach (Collider c in Physics.OverlapSphere(job.Pos, 22f))
                {
                    TreeLog log = c.GetComponentInParent<TreeLog>();
                    ZNetView view = log != null ? log.GetComponent<ZNetView>() : null;
                    if (view == null || !view.IsValid()) continue;
                    view.ClaimOwnership();
                    ZNetScene.instance.Destroy(view.gameObject);
                }
                if (Time.time < job.PlantAt) continue;
                Queue.RemoveAt(i);
                Plant(job);
            }
        }

        private static void Plant(Pending job)
        {
            // The stump goes, the sapling takes its place.
            foreach (Collider c in Physics.OverlapSphere(job.Pos, 1.6f))
            {
                Destructible stump = c.GetComponentInParent<Destructible>();
                ZNetView view = stump != null ? stump.GetComponent<ZNetView>() : null;
                if (view == null || !view.IsValid() || stump.GetComponent<global::Plant>() != null) continue;
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(job.Sapling);
            float ground;
            if (prefab == null || !ZoneSystem.instance.GetGroundHeight(job.Pos, out ground)) return;
            GameObject go = Object.Instantiate(prefab, new Vector3(job.Pos.x, ground, job.Pos.z), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            ZNetView sapling = go.GetComponent<ZNetView>();
            // Marked as the settlement's own, so clearing paths never pulls it up.
            if (sapling != null && sapling.GetZDO() != null) sapling.GetZDO().Set(Materializer.HashSettlement, job.SettlementId);
            _planted++;
        }
    }
}
