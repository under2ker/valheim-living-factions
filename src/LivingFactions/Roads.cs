using System.Collections.Generic;
using System.Globalization;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// What feet and carts leave on the ground: a road from each settlement to its nearest
    /// neighbour it is not at war with, a paved square at its heart, and a handful of trails worn
    /// by the people who actually walk them. Everything is paint on the terrain, laid only around
    /// the player as they travel; the game saves it with the world.
    /// </summary>
    internal static class Roads
    {
        private const float Step = 2f;
        /// <summary>Where a road begins, measured from the settlement's centre: the edge of its square.</summary>
        private const float Inset = 9f;
        private const float PaintRange = 80f;
        private const int TrailsPerSettlement = 6;
        /// <summary>
        /// Lamp posts stand 30 m apart: a lamp lights about 10 m around itself, so from each one the
        /// next two are in plain sight and the road reads as a chain of lights at night, while a
        /// kilometre of road still costs only some thirty objects.
        /// </summary>
        private const int LampEvery = 15;
        private const float FurnishRange = 45f;

        public static readonly int HashRoad = "lf_road".GetStableHashCode();
        private static readonly HashSet<long> Furnished = new HashSet<long>();
        private static int _lamps, _cleared;

        private static readonly float[] SquareRadius = { 0f, 4.5f, 6f, 7.5f, 9f };

        public class Road
        {
            public int A, B;
            public Vector3[] Points;
        }

        private static readonly List<Road> All = new List<Road>();
        private static readonly HashSet<long> Painted = new HashSet<long>();
        private static readonly Dictionary<int, int> SquareTier = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> TrailCount = new Dictionary<int, int>();
        private static readonly HashSet<long> TrailKeys = new HashSet<long>();
        private static string _signature = "";
        private static float _nextPlan, _nextPaint;
        private static int _paintedPoints;

        public static void Reset()
        {
            All.Clear();
            Painted.Clear();
            Furnished.Clear();
            _lamps = _cleared = 0;
            SquareTier.Clear();
            TrailCount.Clear();
            TrailKeys.Clear();
            _signature = "";
            Routes.Clear();
            _nextPlan = _nextPaint = 0f;
            _paintedPoints = 0;
        }

        public static void Update(Vector3 reference)
        {
            if (!ModConfig.Roads.Value) return;
            if (Time.time >= _nextPlan)
            {
                _nextPlan = Time.time + 10f;
                Plan();
            }
            if (Time.time < _nextPaint) return;
            _nextPaint = Time.time + 1f;
            PaintRoads(reference);
            Furnish(reference);
            PaintSquares(reference);
            Materializer.ClearPaths(reference);
        }

        // ------------------------------------------------------------------ the network

        /// <summary>Who is joined to whom. Recomputed only when settlements appear, fall or change sides.</summary>
        private static void Plan()
        {
            Simulation sim = SimHost.Sim;
            List<Settlement> alive = new List<Settlement>();
            System.Text.StringBuilder sig = new System.Text.StringBuilder();
            foreach (Settlement s in sim.State.Settlements)
            {
                if (s.Destroyed) continue;
                alive.Add(s);
                sig.Append(s.Id).Append(':').Append(s.FactionId).Append(',');
            }
            foreach (Faction a in sim.State.Factions)
            {
                foreach (Faction b in sim.State.Factions)
                {
                    if (a.Id < b.Id && sim.AreAtWar(a.Id, b.Id)) sig.Append('w').Append(a.Id).Append('-').Append(b.Id);
                }
            }
            if (sig.ToString() == _signature) return;
            _signature = sig.ToString();

            All.Clear();
            Painted.Clear();
            Furnished.Clear();
            HashSet<long> pairs = new HashSet<long>();
            foreach (Settlement s in alive)
            {
                // Neighbours in order of preference; the first one that can be reached over dry land gets the road.
                List<KeyValuePair<float, Settlement>> candidates = new List<KeyValuePair<float, Settlement>>();
                foreach (Settlement t in alive)
                {
                    if (t == s || sim.AreAtWar(s.FactionId, t.FactionId)) continue;
                    float d = Vec2.Distance(s.Pos, t.Pos);
                    if (d > ModConfig.RoadReach.Value || d < 60f) continue;
                    // Kin first: a road to one's own people is worth a longer walk.
                    candidates.Add(new KeyValuePair<float, Settlement>(d * (t.FactionId == s.FactionId ? 1f : 1.6f), t));
                }
                candidates.Sort((x, y) => x.Key.CompareTo(y.Key));
                for (int c = 0; c < candidates.Count && c < 3; c++)
                {
                    Settlement best = candidates[c].Value;
                    int lo = Mathf.Min(s.Id, best.Id), hi = Mathf.Max(s.Id, best.Id);
                    long pair = ((long)lo << 32) | (uint)hi;
                    Road road;
                    if (!Routes.TryGetValue(pair, out road))
                    {
                        road = Build(sim.State.GetSettlement(lo), sim.State.GetSettlement(hi));
                        Routes[pair] = road;
                    }
                    if (road == null) continue;
                    if (pairs.Add(pair)) All.Add(road);
                    break;
                }
            }
        }

        /// <summary>Routes already worked out, by pair of settlements; null = no way over land. The land does not change, so they are kept.</summary>
        private static readonly Dictionary<long, Road> Routes = new Dictionary<long, Road>();

        private const float Cell = 16f;
        private const int MaxNodes = 20000;

        private static float LandHeight(Dictionary<long, float> heights, Vector3 origin, int x, int z)
        {
            long key = ((long)x << 32) | (uint)z;
            float h;
            if (!heights.TryGetValue(key, out h))
            {
                h = WorldGenerator.instance.GetHeight(origin.x + x * Cell, origin.z + z * Cell);
                heights[key] = h;
            }
            return h;
        }

        /// <summary>
        /// The way a road takes: the cheapest walk over dry land between the two settlements, found
        /// on a coarse grid from the world's own height function (so it needs nothing loaded), then
        /// rounded off into curves. Water is never crossed and steep ground is avoided where a
        /// gentler way exists. Null when the two are not joined by land within a sensible detour.
        /// </summary>
        private static Road Build(Settlement a, Settlement b)
        {
            Vector3 from = new Vector3(a.Pos.X, 0f, a.Pos.Z), to = new Vector3(b.Pos.X, 0f, b.Pos.Z);
            float direct = Vector3.Distance(from, to);
            float dry = ZoneSystem.c_WaterLevel + 1f;
            int goalX = Mathf.RoundToInt((to.x - from.x) / Cell), goalZ = Mathf.RoundToInt((to.z - from.z) / Cell);
            float reach = direct * 2.2f + Cell * 6f;

            Dictionary<long, float> heights = new Dictionary<long, float>(), cost = new Dictionary<long, float>();
            Dictionary<long, long> cameFrom = new Dictionary<long, long>();
            // A plain binary heap of (estimate, cell): the framework this runs on has no priority queue.
            List<KeyValuePair<float, long>> open = new List<KeyValuePair<float, long>>();
            long start = 0L, goal = ((long)goalX << 32) | (uint)goalZ;
            cost[start] = 0f;
            HeapPush(open, new KeyValuePair<float, long>(direct, start));
            bool found = false;
            int visited = 0;
            while (open.Count > 0 && visited < MaxNodes)
            {
                long current = HeapPop(open).Value;
                if (current == goal)
                {
                    found = true;
                    break;
                }
                visited++;
                int cx = (int)(current >> 32), cz = (int)(uint)current;
                float here = LandHeight(heights, from, cx, cz), soFar = cost[current];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        long next = ((long)nx << 32) | (uint)nz;
                        Vector3 world = new Vector3(from.x + nx * Cell, 0f, from.z + nz * Cell);
                        // Only within a corridor around the straight line: a road does not go round half the island.
                        if (Vector3.Distance(world, from) + Vector3.Distance(world, to) > reach) continue;
                        float h = LandHeight(heights, from, nx, nz);
                        if (h < dry && next != goal) continue;
                        float step = Cell * (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        // A metre of climb costs as much as six on the flat; the shore is walked along only if need be.
                        float price = soFar + step + Mathf.Abs(h - here) * 6f + (h < dry + 1.5f ? step * 0.6f : 0f);
                        float known;
                        if (cost.TryGetValue(next, out known) && known <= price) continue;
                        cost[next] = price;
                        cameFrom[next] = current;
                        HeapPush(open, new KeyValuePair<float, long>(price + Vector3.Distance(world, to), next));
                    }
                }
            }
            if (!found) return null;

            List<Vector3> way = new List<Vector3>();
            for (long at = goal; ; at = cameFrom[at])
            {
                way.Add(new Vector3(from.x + (int)(at >> 32) * Cell, 0f, from.z + (int)(uint)at * Cell));
                if (at == start) break;
            }
            way.Reverse();
            way[0] = from;
            way[way.Count - 1] = to;

            // Corners of the grid are cut into curves (twice), then the line is walked off in even steps.
            for (int pass = 0; pass < 2 && way.Count > 2; pass++)
            {
                List<Vector3> round = new List<Vector3> { way[0] };
                for (int k = 0; k < way.Count - 1; k++)
                {
                    round.Add(Vector3.Lerp(way[k], way[k + 1], 0.25f));
                    round.Add(Vector3.Lerp(way[k], way[k + 1], 0.75f));
                }
                round.Add(way[way.Count - 1]);
                way = round;
            }
            List<Vector3> points = new List<Vector3>();
            float total = 0f;
            for (int k = 0; k < way.Count - 1; k++) total += Vector3.Distance(way[k], way[k + 1]);
            if (total < Inset * 2f + Step * 2f) return null;
            float want = Inset, walked = 0f;
            for (int k = 0; k < way.Count - 1; k++)
            {
                float leg = Vector3.Distance(way[k], way[k + 1]);
                while (want <= walked + leg && want <= total - Inset)
                {
                    points.Add(Vector3.Lerp(way[k], way[k + 1], leg > 0f ? (want - walked) / leg : 0f));
                    want += Step;
                }
                walked += leg;
            }
            if (points.Count < 2) return null;
            return new Road { A = a.Id, B = b.Id, Points = points.ToArray() };
        }

        private static void HeapPush(List<KeyValuePair<float, long>> heap, KeyValuePair<float, long> item)
        {
            heap.Add(item);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].Key <= heap[i].Key) break;
                KeyValuePair<float, long> swap = heap[parent];
                heap[parent] = heap[i];
                heap[i] = swap;
                i = parent;
            }
        }

        private static KeyValuePair<float, long> HeapPop(List<KeyValuePair<float, long>> heap)
        {
            KeyValuePair<float, long> top = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, least = i;
                if (left < heap.Count && heap[left].Key < heap[least].Key) least = left;
                if (right < heap.Count && heap[right].Key < heap[least].Key) least = right;
                if (least == i) break;
                KeyValuePair<float, long> swap = heap[least];
                heap[least] = heap[i];
                heap[i] = swap;
                i = least;
            }
            return top;
        }

        private static Road RoadOf(int settlementId)
        {
            foreach (Road road in All)
            {
                if (road.A == settlementId || road.B == settlementId) return road;
            }
            return null;
        }

        /// <summary>
        /// A point the given distance out of a settlement along its road; where there is no road,
        /// along the way its people would take anyway. Height is the ground there when it is loaded.
        /// </summary>
        public static Vector3 Along(Settlement s, float distance)
        {
            Vector3 point;
            Road road = RoadOf(s.Id);
            if (road != null)
            {
                int index = Mathf.Clamp(Mathf.RoundToInt((distance - Inset) / Step), 0, road.Points.Length - 1);
                point = road.Points[road.A == s.Id ? index : road.Points.Length - 1 - index];
            }
            else
            {
                float angle = (Mathf.Abs(s.Seed) % 360) * Mathf.Deg2Rad;
                point = new Vector3(s.Pos.X + Mathf.Cos(angle) * distance, 0f, s.Pos.Z + Mathf.Sin(angle) * distance);
            }
            float ground;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(point, out ground)) point.y = ground;
            return point;
        }

        public static bool HasRoad(Settlement s) { return RoadOf(s.Id) != null; }

        // ------------------------------------------------------------------ paint

        private static void PaintRoads(Vector3 reference)
        {
            int budget = 60;
            for (int r = 0; r < All.Count && budget > 0; r++)
            {
                Road road = All[r];
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null) continue;
                // Cheap rejection: is any of this road near the player at all?
                Vector3 first = road.Points[0], last = road.Points[road.Points.Length - 1];
                if (DistanceToSegment(reference, first, last) > PaintRange + 30f) continue;
                for (int i = 0; i < road.Points.Length && budget > 0; i++)
                {
                    Vector3 p = road.Points[i];
                    if (Utils.DistanceXZ(reference, p) > PaintRange) continue;
                    long key = ((long)r << 32) | (uint)i;
                    if (Painted.Contains(key) || !ZoneSystem.instance.IsZoneLoaded(p)) continue;
                    Painted.Add(key);
                    if (UnderBuilding(a, p, 0.5f) || UnderBuilding(b, p, 0.5f)) continue;
                    // The bed is evened out every other point; near buildings their own pads are left alone.
                    // Inside a settlement the ground is already shaped pad by pad; reshaping it there would tug at the pads.
                    // Nor across the border of two terrain tiles, where smoothing would tear the ground.
                    if ((i & 1) == 0 && !Within(a, p) && !Within(b, p) && !NearTileBorder(p, 5.5f)) Materializer.QueueSmooth(p, 4f);
                    // The last stretch into a town is cobbled; the rest is a beaten track.
                    bool cobbled = (a.Tier >= Tier.Town && i * Step < 40f) || (b.Tier >= Tier.Town && (road.Points.Length - 1 - i) * Step < 40f);
                    Materializer.QueuePaint(p, 2.3f, cobbled ? 2 : 1);
                    _paintedPoints++;
                    budget--;
                }
            }
        }

        /// <summary>
        /// Close to the player, where the world's objects exist: trees, bushes, rocks and stumps on
        /// the road are cleared away and the lamp posts are put up. A post already standing (from an
        /// earlier visit) is recognised by its tag and not doubled.
        /// </summary>
        private static void Furnish(Vector3 reference)
        {
            int budget = 10;
            for (int r = 0; r < All.Count && budget > 0; r++)
            {
                Road road = All[r];
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null) continue;
                if (DistanceToSegment(reference, road.Points[0], road.Points[road.Points.Length - 1]) > FurnishRange + 30f) continue;
                for (int i = 0; i < road.Points.Length && budget > 0; i++)
                {
                    Vector3 p = road.Points[i];
                    if (Utils.DistanceXZ(reference, p) > FurnishRange) continue;
                    long key = ((long)r << 32) | (uint)i;
                    if (Furnished.Contains(key) || !ZNetScene.instance.IsAreaReady(p)) continue;
                    float ground;
                    if (!ZoneSystem.instance.GetGroundHeight(p, out ground)) continue;
                    Furnished.Add(key);
                    budget--;
                    if (ground < ZoneSystem.c_WaterLevel + 0.3f || UnderBuilding(a, p, 1.5f) || UnderBuilding(b, p, 1.5f)) continue;
                    p.y = ground;
                    Materializer.ClearVegetation(p, 3.4f);
                    _cleared++;
                    if (ModConfig.RoadLamps.Value && i % LampEvery == LampEvery / 2) PlaceLamp(road, i, p);
                }
            }
        }

        /// <summary>Marks what the current build puts up; posts of an older make are taken down when met.</summary>
        private const int LampMake = 2;
        private static string _lampKit = "none yet";

        private static GameObject FirstPrefab(params string[] names)
        {
            foreach (string name in names)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab != null) return prefab;
            }
            return null;
        }

        private static GameObject PutUp(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            GameObject go = Object.Instantiate(prefab, pos, rot);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null) view.GetZDO().Set(HashRoad, LampMake);
            Fireplace fire = go.GetComponent<Fireplace>();
            if (fire != null)
            {
                // A road lamp is lit when it goes up and never needs feeding.
                fire.m_infiniteFuel = true;
                if (view != null && view.GetZDO() != null) view.GetZDO().Set(ZDOVars.s_fuel, fire.m_maxFuel);
            }
            return go;
        }

        /// <summary>A lamp post: a log pole as tall as a man and a half, with a light at its head.</summary>
        private static void PlaceLamp(Road road, int i, Vector3 p)
        {
            Vector3 along = road.Points[Mathf.Min(i + 1, road.Points.Length - 1)] - road.Points[Mathf.Max(i - 1, 0)];
            along.y = 0f;
            Vector3 side = new Vector3(-along.z, 0f, along.x).normalized;
            // Posts alternate sides of the road, just off its edge.
            if (((i / LampEvery) & 1) != 0) side = -side;
            Vector3 pos = p + side * 3.6f;
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(pos, out ground) || ground < ZoneSystem.c_WaterLevel + 0.3f) return;
            pos.y = ground;
            bool standing = false;
            HashSet<ZNetView> old = new HashSet<ZNetView>();
            foreach (Collider collider in Physics.OverlapSphere(pos, 4f))
            {
                ZNetView there = collider.GetComponentInParent<ZNetView>();
                if (there == null || !there.IsValid()) continue;
                int make = there.GetZDO().GetInt(HashRoad);
                if (make == LampMake) standing = true;
                else if (make != 0) old.Add(there);
            }
            foreach (ZNetView view in old)
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
            }
            if (standing) return;

            GameObject pole = FirstPrefab("wood_pole_log", "wood_pole2");
            GameObject light = FirstPrefab("piece_walltorch", "piece_groundtorch_wood", "piece_groundtorch");
            if (light == null) return;
            Materializer.ClearVegetation(pos, 1.2f);
            Quaternion facing = Quaternion.LookRotation(-side);
            if (pole != null && light.name == "piece_walltorch")
            {
                // The pole's origin is its middle; the torch hangs on the side that faces the road.
                PutUp(pole, pos + Vector3.up * 0.9f, facing);
                PutUp(light, pos + Vector3.up * 1.75f - side * 0.18f, facing);
            }
            else PutUp(light, pos, facing);
            _lampKit = (pole != null && light.name == "piece_walltorch" ? pole.name + " + " : "") + light.name;
            _lamps++;
        }

        private static bool NearTileBorder(Vector3 p, float reach)
        {
            // Tiles are 64 m across, centred on multiples of 64.
            float dx = Mathf.Abs(Mathf.Repeat(p.x + 32f, 64f)), dz = Mathf.Abs(Mathf.Repeat(p.z + 32f, 64f));
            return dx < reach || dx > 64f - reach || dz < reach || dz > 64f - reach;
        }

        private static bool Within(Settlement s, Vector3 p)
        {
            return Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) < Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 10f;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            ab.y = 0f;
            Vector3 ap = p - a;
            ap.y = 0f;
            float t = ab.sqrMagnitude > 0.01f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            return Utils.DistanceXZ(p, a + ab * t);
        }

        /// <summary>Roads go round what stands in the settlement rather than under it.</summary>
        private static bool UnderBuilding(Settlement s, Vector3 p, float margin)
        {
            if (Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 85f) return false;
            foreach (Building b in s.Buildings)
            {
                if (b.Plot < 0 || b.State == BuildingState.Destroyed) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
                if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < Blueprints.Radius(b.Kind) + margin) return true;
            }
            return false;
        }

        /// <summary>The heart of a settlement is cobbled once it is more than a camp, and wider as it grows.</summary>
        private static void PaintSquares(Vector3 reference)
        {
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed) continue;
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                if (Utils.DistanceXZ(reference, centre) > PaintRange || !ZoneSystem.instance.IsZoneLoaded(centre)) continue;
                int tier = (int)s.Tier, done;
                if (SquareTier.TryGetValue(s.Id, out done) && done == tier) continue;
                SquareTier[s.Id] = tier;
                float radius = SquareRadius[Mathf.Clamp(tier, 0, SquareRadius.Length - 1)];
                if (radius <= 0f) continue;
                Materializer.QueuePaint(centre, 1.6f, 2);
                for (float ring = 1.8f; ring <= radius; ring += 1.8f)
                {
                    int spots = Mathf.CeilToInt(2f * Mathf.PI * ring / 1.8f);
                    for (int i = 0; i < spots; i++)
                    {
                        float a = i * 2f * Mathf.PI / spots;
                        Materializer.QueuePaint(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring, 1.6f, 2);
                    }
                }
            }
        }

        /// <summary>
        /// A trail somebody has just set off along. Only the first few each settlement's people take
        /// are worn into the grass, so the ground shows the habits of the place, not a spider's web.
        /// </summary>
        public static bool WornTrail(int settlementId, Vector3 from, Vector3 to)
        {
            if (!ModConfig.Roads.Value) return false;
            float length = Utils.DistanceXZ(from, to);
            if (length < 7f || length > 95f) return false;
            int count;
            TrailCount.TryGetValue(settlementId, out count);
            if (count >= TrailsPerSettlement) return false;
            // The same errand twice is the same trail.
            long key = ((long)settlementId << 40) ^ ((long)Mathf.RoundToInt(to.x / 4f) << 20) ^ (uint)Mathf.RoundToInt(to.z / 4f);
            if (!TrailKeys.Add(key)) return false;
            TrailCount[settlementId] = count + 1;
            Materializer.QueueRoad(from, to, 1, 1.2f);
            return true;
        }

        public static string Describe()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(All.Count + " roads, " + _paintedPoints + " road points painted this session, " + _cleared + " cleared of growth, lamp posts put up " + _lamps + " (" + _lampKit + "), trails worn " + TrailKeys.Count);
            foreach (Road road in All)
            {
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null) continue;
                int wet = 0;
                foreach (Vector3 q in road.Points)
                {
                    if (WorldGenerator.instance.GetHeight(q.x, q.z) < ZoneSystem.c_WaterLevel + 0.3f) wet++;
                }
                sb.AppendLine("  " + a.Name + " - " + b.Name + ": " + ((road.Points.Length - 1) * Step).ToString("0", CultureInfo.InvariantCulture) + " m (straight " + Vec2.Distance(a.Pos, b.Pos).ToString("0", CultureInfo.InvariantCulture)
                    + " m), points under water " + wet);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
