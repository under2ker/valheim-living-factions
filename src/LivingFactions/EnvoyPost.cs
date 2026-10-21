using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The envoy's tent outside every settlement (not war camps nor outposts): a place to talk to a faction
    /// whatever it thinks of you. It stands beyond the rings or the wall, beside the first road out, its mouth
    /// toward the settlement. The envoy (NpcRole.Envoy) waits at the mouth, carries no weapon, is never hurt and
    /// is nobody's enemy; within a few metres of the envoy the faction's people leave a player alone (unless that
    /// player has just struck one of them), so one at odds with the faction can still come and make amends.
    /// The tent's pieces carry the settlement's tag and a building id of their own (BuildingId).
    /// </summary>
    internal static class EnvoyPost
    {
        /// <summary>The building id the tent's pieces are tagged with: no building of the record has it.</summary>
        public const int BuildingId = -7;

        /// <summary>Set on the tent's first piece, with the tent's middle and bearing (its own place is a roof slope's).</summary>
        public static readonly int HashAnchor = "lf_envoy".GetStableHashCode();
        private static readonly int HashAt = "lf_envoy_at".GetStableHashCode(), HashYaw = "lf_envoy_yaw".GetStableHashCode();

        /// <summary>How close to the envoy the faction's people leave a player alone.</summary>
        public const float ShelterRange = 8f;

        /// <summary>How far in front of the tent's mouth the envoy stands.</summary>
        private const float PostAhead = 2.8f;

        public static bool Wanted(Settlement s) { return !s.Destroyed && s.CampaignId == 0 && s.ParentId == 0; }

        private static readonly Dictionary<int, float> NextLook = new Dictionary<int, float>();
        private static readonly Dictionary<int, Vector4> Pending = new Dictionary<int, Vector4>();
        private static readonly Dictionary<int, Vector4> Posts = new Dictionary<int, Vector4>();
        private static readonly List<Vector4> Envoys = new List<Vector4>();
        private static float _nextShelters;
        private static readonly List<ZDO> Scan = new List<ZDO>();

        public static void Reset()
        {
            NextLook.Clear();
            Pending.Clear();
            Posts.Clear();
            Envoys.Clear();
        }

        /// <summary>Where the settlement's envoy stands (x, y, z) and faces (w, degrees), when its tent stands.</summary>
        public static bool PostOf(Settlement s, out Vector3 post, out float yaw)
        {
            Vector4 v;
            post = Vector3.zero;
            yaw = 0f;
            if (!Posts.TryGetValue(s.Id, out v)) return false;
            yaw = v.w;
            post = new Vector3(v.x, v.y, v.z) + Quaternion.Euler(0f, v.w, 0f) * new Vector3(0f, 0f, PostAhead);
            return true;
        }

        /// <summary>True when the position is under an envoy's shelter of that faction (the envoy is there, in the world).</summary>
        public static bool Shelters(int factionId, Vector3 at)
        {
            foreach (Vector4 v in Envoys)
            {
                if ((int)v.w == factionId && Utils.DistanceXZ(at, new Vector3(v.x, 0f, v.z)) < ShelterRange) return true;
            }
            return false;
        }

        /// <summary>Once a second: where the envoys are now, for Shelters (asked by every guard's eyes, often).</summary>
        public static void Update()
        {
            if (Time.time < _nextShelters) return;
            _nextShelters = Time.time + 1f;
            Envoys.Clear();
            foreach (FactionMember m in FactionMember.All)
            {
                if (m == null || m.Role != NpcRole.Envoy) continue;
                Vector3 p = m.transform.position;
                Envoys.Add(new Vector4(p.x, p.y, p.z, m.FactionId));
            }
        }

        /// <summary>
        /// Called for a settlement this peer builds, while a player is near: finds its tent, moves it when the
        /// settlement has grown out to it, and otherwise picks a spot, levels it and puts the tent up.
        /// </summary>
        public static void Furnish(Settlement s, Faction faction)
        {
            float due;
            if (NextLook.TryGetValue(s.Id, out due) && Time.time < due) return;
            NextLook[s.Id] = Time.time + 6f;
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            float dist = Distance(s);
            Vector4 anchor;
            int pieces = FindTent(s, centre, out anchor);
            if (pieces > 0)
            {
                // Grown out to it, no longer a place that keeps one, or broken up so far its marked piece is gone.
                if (!Wanted(s) || anchor == Vector4.zero || Utils.DistanceXZ(new Vector3(anchor.x, 0f, anchor.z), centre) < dist - 6f)
                {
                    // Grown out to it (a camp become a town with a wall), or no longer a place that keeps one.
                    RemoveTent(s, centre);
                    Posts.Remove(s.Id);
                    Plugin.Log.LogInfo(s.Name + ": the envoy's tent is taken down" + (Wanted(s) ? ", to go up again further out" : ""));
                    return;
                }
                Posts[s.Id] = anchor;
                return;
            }
            Posts.Remove(s.Id);
            if (!Wanted(s)) return;

            Vector4 spot;
            if (!Pending.TryGetValue(s.Id, out spot))
            {
                if (!PickSpot(s, centre, dist, out spot)) return;
                Vector3 at = new Vector3(spot.x, spot.y, spot.z);
                if (!ZoneSystem.instance.IsZoneLoaded(at)) return;
                // The ground first, as for a building's pad; the tent goes up when it is done.
                Materializer.QueueGrade(at, 3.8f, true);
                Pending[s.Id] = spot;
                return;
            }
            Vector3 pad = new Vector3(spot.x, spot.y, spot.z);
            if (!ZoneSystem.instance.IsZoneLoaded(pad) || Materializer.GroundWorkNear(pad, 4.5f)) return;
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(pad, out ground)) return;
            pad.y = ground;
            // Trees, bushes and rocks off the tent's ground and the way in front of it.
            Materializer.ClearVegetation(pad, 5.5f);
            Materializer.ClearVegetation(pad + Quaternion.Euler(0f, spot.w, 0f) * new Vector3(0f, 0f, 5f), 3.5f);
            Quaternion rot = Quaternion.Euler(0f, spot.w, 0f);
            bool first = true;
            foreach (PieceSpec spec in Blueprints.EnvoyTent(Blueprints.StyleFor(faction, s.Tier)))
            {
                ZNetView view = Materializer.PlaceTagged(s, BuildingId, spec.Prefab, pad + rot * spec.Pos, rot * Quaternion.Euler(0f, spec.Yaw, 0f));
                if (first && view != null && view.GetZDO() != null)
                {
                    view.GetZDO().Set(HashAnchor, 1);
                    view.GetZDO().Set(HashAt, pad);
                    view.GetZDO().Set(HashYaw, spot.w);
                    first = false;
                }
            }
            Pending.Remove(s.Id);
            Posts[s.Id] = new Vector4(pad.x, pad.y, pad.z, spot.w);
            Plugin.Log.LogInfo(s.Name + ": the envoy's tent is put up at " + pad.ToString("0") + ", " + Utils.DistanceXZ(pad, centre).ToString("0") + " m out");
        }

        /// <summary>How far out the tent stands: beyond the outer ring of homes, or beyond the wall.</summary>
        private static float Distance(Settlement s)
        {
            float ring = Defs.RingRadius[Mathf.Clamp(Defs.RingsByTier[(int)s.Tier] - 1, 0, Defs.RingRadius.Length - 1)] + 8f;
            float wall = s.Tier >= Tier.City ? Defs.CityWallRadius + 3f : s.Tier >= Tier.Town ? Defs.PalisadeRadius + 3f : 0f;
            return Mathf.Max(ring, wall) + 8f;
        }

        /// <summary>
        /// Beside the first road out, where the land is even and dry and no road, nor anyone's building, is in
        /// the way; failing that round the settlement at a bearing of its own. Its mouth toward the settlement.
        /// </summary>
        private static bool PickSpot(Settlement s, Vector3 centre, float dist, out Vector4 spot)
        {
            List<float> bearings = new List<float>();
            foreach (float road in Roads.Crossings(s, dist))
            {
                bearings.Add(road + 14f);
                bearings.Add(road - 14f);
            }
            float own = (s.Seed & 0xFFFF) % 360;
            for (int k = 0; k < 16; k++) bearings.Add(own + k * 22.5f);
            WorldGenerator gen = WorldGenerator.instance;
            foreach (float bearing in bearings)
            {
                float a = bearing * Mathf.Deg2Rad;
                Vector3 at = centre + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * dist;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    float b = i * Mathf.PI / 4f, r = i == 8 ? 0f : 3.5f;
                    float h = gen != null ? gen.GetHeight(at.x + Mathf.Cos(b) * r, at.z + Mathf.Sin(b) * r) : 0f;
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
                if (lo < ZoneSystem.c_WaterLevel + 1f || hi - lo > 2.2f) continue;
                if (Roads.NearRoad(s, at, 5f) || Roads.PlayerBuilt(at, 7f)) continue;
                at.y = Materializer.NaturalHeight(at, 3.5f);
                Vector3 toward = centre - at;
                spot = new Vector4(at.x, at.y, at.z, Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg);
                return true;
            }
            spot = Vector4.zero;
            return false;
        }

        /// <summary>The tent's pieces in the world: how many, and the first one's place and bearing (w).</summary>
        private static int FindTent(Settlement s, Vector3 centre, out Vector4 anchor)
        {
            anchor = Vector4.zero;
            Scan.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, Scan);
            int n = 0;
            foreach (ZDO zdo in Scan)
            {
                if (zdo.GetInt(Materializer.HashSettlement) != s.Id || zdo.GetInt(Materializer.HashBuilding) != BuildingId) continue;
                n++;
                if (zdo.GetInt(HashAnchor) == 0) continue;
                Vector3 p = zdo.GetVec3(HashAt, zdo.GetPosition());
                anchor = new Vector4(p.x, p.y, p.z, zdo.GetFloat(HashYaw, 0f));
            }
            return n;
        }

        private static void RemoveTent(Settlement s, Vector3 centre)
        {
            Scan.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, Scan);
            foreach (ZDO zdo in Scan.ToArray())
            {
                if (zdo.GetInt(Materializer.HashSettlement) != s.Id || zdo.GetInt(Materializer.HashBuilding) != BuildingId) continue;
                Materializer.RemoveObject(zdo);
            }
        }

        /// <summary>For the dev command `envoy`: the tent and the envoy of a settlement.</summary>
        public static string Describe(Settlement s)
        {
            Vector3 post;
            float yaw;
            bool tent = PostOf(s, out post, out yaw);
            FactionMember envoy = null;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m != null && m.Role == NpcRole.Envoy && m.SettlementId == s.Id) envoy = m;
            }
            Player me = Player.m_localPlayer;
            string where = tent ? "tent's post at " + post.ToString("0") + ", " + Utils.DistanceXZ(post, new Vector3(s.Pos.X, 0f, s.Pos.Z)).ToString("0") + " m out" : (Wanted(s) ? "no tent yet" : "keeps no envoy");
            return s.Name + ": " + where + "; envoy " + (envoy != null ? envoy.DisplayName + " " + Utils.DistanceXZ(envoy.transform.position, post).ToString("0.0") + " m from the post" : "none in the world")
                + (me != null ? "; the player is " + (Shelters(s.FactionId, me.transform.position) ? "sheltered" : "not sheltered") : "");
        }
    }
}
