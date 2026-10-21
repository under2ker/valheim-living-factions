using System.Collections.Generic;
using System.Globalization;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// What feet and carts leave on the ground: a road from each settlement to its nearest
    /// neighbour, a paved square at its heart, and a handful of trails worn by the people who
    /// actually walk them. Everything is paint on the terrain, laid only around the player as they
    /// travel; the game saves it with the world.
    ///
    /// Each road leaves a settlement by a street: a straight way from the square out past the wall,
    /// on a bearing chosen to pass between the plots, so that it goes through its gate square-on.
    /// Streets that leave close together are one street. Between the ends of the two streets the
    /// road takes the gentlest way over dry land.
    ///
    /// The plan is a function of the settlements alone (where they are and what stands on their
    /// plots), not of wars or of who holds them: every player works out the same roads, and a road
    /// does not vanish from the plan while its paint stays on the ground.
    /// </summary>
    internal static class Roads
    {
        private const float Step = 2f;
        /// <summary>Where a road begins, measured from the settlement's centre: on the square, past the fire's benches and woodpile (5.5 m).</summary>
        private const float Inset = 6.5f;
        private const float PaintRange = 80f;
        private const int TrailsPerSettlement = 6;
        /// <summary>
        /// Lamp posts stand 30 m apart: a lamp lights about 10 m around itself, so from each one the
        /// next two are in plain sight and the road reads as a chain of lights at night, while a
        /// kilometre of road still costs only some thirty objects.
        /// </summary>
        private const int LampEvery = 15;
        /// <summary>Growth is cleared off the roads this far round the player: more than the 100 m that roadcheck walks from a settlement's centre, so a player a few tens of metres off the centre still clears all of it.</summary>
        private const float FurnishRange = 150f;
        /// <summary>How far a street runs straight out of its settlement: past a city's wall (78 m) and clear of it.</summary>
        private const float StreetOut = 90f;
        /// <summary>Streets of one settlement whose bearings are closer than this (degrees) are one street.</summary>
        private const float StreetMerge = 25f;
        /// <summary>How far (degrees) a street may turn from the way to the neighbour to find a lane between the plots.</summary>
        private const float StreetSwing = 40f;
        /// <summary>Half the width of a road's paint.</summary>
        private const float HalfWidth = 2.3f;
        /// <summary>No lamp post this close to a settlement's centre: they stand outside even a city's wall.</summary>
        private const float LampClear = 84f;
        /// <summary>Steepest grade a road bed is cut to, where the land allows (about 11 degrees).</summary>
        private const float BedGrade = 0.2f;
        /// <summary>Furthest a road bed is cut into or raised over the land.</summary>
        private const float BedDepth = 3f;

        public static readonly int HashRoad = "lf_road".GetStableHashCode();
        /// <summary>When each road point was last cleared: growth the world puts up later (once its zone is loaded) is cleared again.</summary>
        private static readonly Dictionary<long, float> Furnished = new Dictionary<long, float>();
        private static int _lamps, _cleared, _lampsSwept, _graded;

        private static readonly float[] SquareRadius = { 0f, 4.5f, 6f, 7.5f, 9f };

        public class Road
        {
            public int A, B;
            public Vector3[] Points;
            /// <summary>Bearings (degrees, 0 = +Z, 90 = +X) and lengths of the streets at the two ends.</summary>
            public float BearingA, BearingB, StreetA, StreetB;
            /// <summary>The height the road bed is cut to at each point; worked out when first painted.</summary>
            public float[] Bed;
            public float MinX, MaxX, MinZ, MaxZ;

            public bool Near(Vector3 p, float reach)
            {
                return p.x > MinX - reach && p.x < MaxX + reach && p.z > MinZ - reach && p.z < MaxZ + reach;
            }

            /// <summary>Distance from the point to the line of the road (XZ).</summary>
            public float Distance(Vector3 p)
            {
                float best = float.MaxValue;
                for (int k = 1; k < Points.Length; k++)
                {
                    // Points are 2 m apart: a cheap test first.
                    if (Mathf.Abs(Points[k].x - p.x) > best + Step || Mathf.Abs(Points[k].z - p.z) > best + Step) continue;
                    best = Mathf.Min(best, DistanceToSegment(p, Points[k - 1], Points[k]));
                }
                return best;
            }
        }

        private static readonly List<Road> All = new List<Road>();
        private static readonly HashSet<long> Painted = new HashSet<long>();
        private static readonly Dictionary<int, int> SquareTier = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> TrailCount = new Dictionary<int, int>();
        private static readonly HashSet<long> TrailKeys = new HashSet<long>();
        private static string _signature = "";
        private static float _nextPaint, _nextSweep, _checkedAt = -100f;
        private static int _paintedPoints;

        public static void Reset()
        {
            All.Clear();
            Painted.Clear();
            Furnished.Clear();
            _lamps = _cleared = _lampsSwept = _graded = 0;
            SquareTier.Clear();
            TrailCount.Clear();
            TrailKeys.Clear();
            _signature = "";
            Routes.Clear();
            StreetTerrain.Clear();
            InnerRoutes.Clear();
            BedCut.Clear();
            Standing.Clear();
            PlotHashes.Clear();
            PlotOffsets.Clear();
            _nextPaint = _nextSweep = 0f;
            Unfurnished = 0;
            _checkedAt = -100f;
            _paintedPoints = 0;
        }

        public static void Update(Vector3 reference)
        {
            if (!ModConfig.Roads.Value)
            {
                Unfurnished = 0;
                return;
            }
            EnsurePlan();
            if (Time.time < _nextPaint) return;
            _nextPaint = Time.time + 1f;
            PaintRoads(reference);
            Furnish(reference);
            PaintSquares(reference);
            Materializer.ClearPaths(reference);
            if (Time.time >= _nextSweep)
            {
                _nextSweep = Time.time + 8f;
                SweepLamps(reference);
            }
        }

        /// <summary>
        /// The plan is looked at again every few seconds by whoever asks for it (the one keeping the
        /// roads, a builder putting up a wall, the host placing a building), so all of them work from
        /// the same settlements.
        /// </summary>
        private static void EnsurePlan()
        {
            if (_signature.Length > 0 && Time.time - _checkedAt < 5f) return;
            _checkedAt = Time.time;
            Plan();
        }

        // ------------------------------------------------------------------ the network

        private static long Key(Vector3 p)
        {
            return ((long)Mathf.RoundToInt(p.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 2f);
        }

        /// <summary>Who is joined to whom, and by which streets. Recomputed only when settlements appear or fall, or build on new plots.</summary>
        private static void Plan()
        {
            Simulation sim = SimHost.Sim;
            if (sim == null || WorldGenerator.instance == null) return;
            List<Settlement> alive = new List<Settlement>();
            System.Text.StringBuilder sig = new System.Text.StringBuilder();
            Dictionary<int, Dictionary<int, float>> standing = new Dictionary<int, Dictionary<int, float>>();
            foreach (Settlement s in sim.State.Settlements)
            {
                if (s.Destroyed || s.CampaignId != 0) continue;
                alive.Add(s);
                Dictionary<int, float> plots = new Dictionary<int, float>();
                int hash = 17;
                foreach (Building b in s.Buildings)
                {
                    if (b.Plot < 0 || b.State == BuildingState.Destroyed) continue;
                    plots[b.Plot] = Blueprints.Radius(b.Kind);
                    hash = hash * 31 + b.Plot * 64 + (int)b.Kind;
                }
                standing[s.Id] = plots;
                // A city's straight stretch starts further out (its wall is at 78 m).
                if (s.Tier >= Tier.City) hash = hash * 31 + 7;
                sig.Append(s.Id).Append(':').Append(hash).Append(',');
                Standing[s.Id] = plots;
                PlotHashes[s.Id] = hash;
            }
            sig.Append(ModConfig.RoadReach.Value.ToString("0", CultureInfo.InvariantCulture));
            if (sig.ToString() == _signature) return;
            _signature = sig.ToString();

            // Who is joined to whom: each settlement to its nearest neighbour it can reach over land.
            List<Road> roads = new List<Road>();
            HashSet<long> pairs = new HashSet<long>();
            foreach (Settlement s in alive)
            {
                List<KeyValuePair<float, Settlement>> candidates = new List<KeyValuePair<float, Settlement>>();
                foreach (Settlement t in alive)
                {
                    if (t == s) continue;
                    float d = Vec2.Distance(s.Pos, t.Pos);
                    if (d > ModConfig.RoadReach.Value || d < 60f) continue;
                    candidates.Add(new KeyValuePair<float, Settlement>(d, t));
                }
                candidates.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.Id.CompareTo(y.Value.Id));
                for (int c = 0; c < candidates.Count && c < 3; c++)
                {
                    Settlement best = candidates[c].Value;
                    Settlement lo = s.Id < best.Id ? s : best, hi = s.Id < best.Id ? best : s;
                    long pair = ((long)lo.Id << 32) | (uint)hi.Id;
                    if (pairs.Contains(pair)) break;
                    float length = StreetLength(lo, hi), costLo, costHi;
                    float bearingLo = StreetBearing(lo, hi, length, standing[lo.Id], out costLo);
                    float bearingHi = StreetBearing(hi, lo, length, standing[hi.Id], out costHi);
                    Road road = Compose(lo, hi, bearingLo, bearingHi, length);
                    if (road == null) continue;
                    pairs.Add(pair);
                    roads.Add(road);
                    break;
                }
            }

            // Streets of one settlement that leave close together become one: one gate, one way in.
            Dictionary<int, List<KeyValuePair<Road, bool>>> ends = new Dictionary<int, List<KeyValuePair<Road, bool>>>();
            foreach (Road road in roads)
            {
                AddEnd(ends, road.A, road, true);
                AddEnd(ends, road.B, road, false);
            }
            Dictionary<Road, float[]> merged = new Dictionary<Road, float[]>();
            foreach (KeyValuePair<int, List<KeyValuePair<Road, bool>>> pair in ends)
            {
                Settlement s = sim.State.GetSettlement(pair.Key);
                List<KeyValuePair<Road, bool>> list = pair.Value;
                if (list.Count < 2) continue;
                list.Sort((x, y) => BearingOf(x).CompareTo(BearingOf(y)));
                // Groups of streets each within StreetMerge of the next, going round the circle.
                bool[] joinedNext = new bool[list.Count];
                for (int i = 0; i < list.Count; i++) joinedNext[i] = Mathf.Abs(Mathf.DeltaAngle(BearingOf(list[i]), BearingOf(list[(i + 1) % list.Count]))) < StreetMerge;
                int first = 0;
                while (first < list.Count && joinedNext[(first + list.Count - 1) % list.Count]) first++;
                if (first == list.Count) first = 0;
                for (int g = 0; g < list.Count;)
                {
                    List<KeyValuePair<Road, bool>> group = new List<KeyValuePair<Road, bool>>();
                    int i = (first + g) % list.Count;
                    group.Add(list[i]);
                    g++;
                    while (g < list.Count && joinedNext[i])
                    {
                        i = (first + g) % list.Count;
                        group.Add(list[i]);
                        g++;
                    }
                    if (group.Count < 2) continue;
                    // The street of the group that passes most freely between the plots, for all of them.
                    float bestBearing = BearingOf(group[0]), bestCost = float.MaxValue;
                    foreach (KeyValuePair<Road, bool> end in group)
                    {
                        float cost = 0f;
                        foreach (KeyValuePair<Road, bool> other in group) cost += StreetCost(s, BearingOf(end), (other.Value ? other.Key.StreetA : other.Key.StreetB), standing[s.Id]);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestBearing = BearingOf(end);
                        }
                    }
                    foreach (KeyValuePair<Road, bool> end in group)
                    {
                        float[] both;
                        if (!merged.TryGetValue(end.Key, out both)) merged[end.Key] = both = new[] { end.Key.BearingA, end.Key.BearingB };
                        both[end.Value ? 0 : 1] = bestBearing;
                    }
                }
            }

            All.Clear();
            foreach (Road road in roads)
            {
                float[] both;
                Road final = road;
                if (merged.TryGetValue(road, out both) && (both[0] != road.BearingA || both[1] != road.BearingB))
                {
                    Settlement a = sim.State.GetSettlement(road.A), b = sim.State.GetSettlement(road.B);
                    final = Compose(a, b, both[0], both[1], road.StreetA) ?? road;
                }
                All.Add(final);
            }
        }

        private static void AddEnd(Dictionary<int, List<KeyValuePair<Road, bool>>> ends, int id, Road road, bool atA)
        {
            List<KeyValuePair<Road, bool>> list;
            if (!ends.TryGetValue(id, out list)) ends[id] = list = new List<KeyValuePair<Road, bool>>();
            list.Add(new KeyValuePair<Road, bool>(road, atA));
        }

        private static float BearingOf(KeyValuePair<Road, bool> end)
        {
            return Mathf.Repeat(end.Value ? end.Key.BearingA : end.Key.BearingB, 360f);
        }

        /// <summary>Two settlements close together share the way between them: each street runs at most 45 % of it.</summary>
        private static float StreetLength(Settlement a, Settlement b)
        {
            return Mathf.Clamp(Vec2.Distance(a.Pos, b.Pos) * 0.45f, 20f, StreetOut);
        }

        /// <summary>
        /// Where a street stops winding between the buildings and runs straight out: a few metres inside
        /// the line of a town's wall (a city's, once it is one), so it goes through the gate square-on.
        /// The same for a village, which may have its wall one day. Shorter streets of close neighbours less.
        /// </summary>
        private static float StraightFrom(Settlement s, float street)
        {
            float wall = s.Tier >= Tier.City ? Defs.CityWallRadius : Defs.PalisadeRadius;
            return Mathf.Max(Inset + 4f, Mathf.Min(wall - 4f, street - 8f));
        }

        private static Vector3 Dir(float bearing)
        {
            float rad = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        /// <summary>
        /// The bearing a settlement's street to a neighbour leaves on: as near the way to the neighbour
        /// as can be, where its straight stretch through the wall passes between the plots (never over a
        /// standing building if there is any other way, all round the compass if need be), on dry land
        /// and not up a bank. Whole even degrees from the way to the neighbour, so every player finds the same.
        /// </summary>
        private static float StreetBearing(Settlement s, Settlement toward, float length, Dictionary<int, float> standing, out float cost)
        {
            float direct = Mathf.Atan2(toward.Pos.X - s.Pos.X, toward.Pos.Z - s.Pos.Z) * Mathf.Rad2Deg;
            direct = Mathf.Round(direct);
            List<KeyValuePair<float, float>> candidates = new List<KeyValuePair<float, float>>();
            float cheapest = float.MaxValue;
            for (float k = 0f; k <= 180f; k += 2f)
            {
                // Beyond the usual swing only when every bearing near the neighbour runs over a building.
                if (k > StreetSwing && cheapest < Blocked) break;
                foreach (float sign in k == 0f || k == 180f ? new[] { 1f } : new[] { -1f, 1f })
                {
                    float bearing = Mathf.Repeat(direct + sign * k, 360f);
                    float c = StreetCost(s, bearing, length, standing) + k * 0.15f;
                    cheapest = Mathf.Min(cheapest, c);
                    candidates.Add(new KeyValuePair<float, float>(c, bearing));
                }
            }
            // Stable order: by cost, then by how far from the way to the neighbour (the order they were found).
            List<KeyValuePair<float, float>> sorted = new List<KeyValuePair<float, float>>(candidates);
            sorted.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : candidates.IndexOf(x).CompareTo(candidates.IndexOf(y)));
            // The cheapest bearing whose street also finds its way in between the buildings to the square.
            float from = StraightFrom(s, length);
            int hash = PlotHash(s);
            for (int i = 0; i < sorted.Count && i < 12; i++)
            {
                if (Inner(s, sorted[i].Value, from, standing, hash) == null) continue;
                cost = sorted[i].Key;
                return sorted[i].Value;
            }
            cost = sorted[0].Key;
            return sorted[0].Value;
        }

        /// <summary>What a street costs when its straight stretch runs over a standing building.</summary>
        private const float Blocked = 200f;

        private static readonly Dictionary<long, float> StreetTerrain = new Dictionary<long, float>();
        private static readonly Dictionary<int, Vec2[]> PlotOffsets = new Dictionary<int, Vec2[]>();

        /// <summary>Where every plot a city could have lies: fixed for the settlement.</summary>
        private static Vec2[] OffsetsOf(Settlement s)
        {
            Vec2[] offsets;
            if (!PlotOffsets.TryGetValue(s.Id, out offsets))
            {
                offsets = new Vec2[Defs.PlotCount(Tier.City)];
                for (int plot = 0; plot < offsets.Length; plot++)
                {
                    float yaw;
                    Defs.PlotTransform(s, plot, out offsets[plot], out yaw);
                }
                PlotOffsets[s.Id] = offsets;
            }
            return offsets;
        }

        /// <summary>What the straight stretch of a street on this bearing costs: plots it runs over, water and banks on its line.</summary>
        private static float StreetCost(Settlement s, float bearing, float length, Dictionary<int, float> standing)
        {
            Vector3 dir = Dir(bearing);
            float from = StraightFrom(s, length) - 2f, cost = 0f;
            Vec2[] offsets = OffsetsOf(s);
            for (int plot = 0; plot < offsets.Length; plot++)
            {
                Vec2 offset = offsets[plot];
                float along = offset.X * dir.x + offset.Z * dir.z, across = Mathf.Abs(offset.X * dir.z - offset.Z * dir.x);
                float beyond = along < from ? from - along : along > length ? along - length : 0f;
                float distance = Mathf.Sqrt(across * across + beyond * beyond);
                float radius;
                bool built = standing.TryGetValue(plot, out radius);
                if (!built) radius = 5f;
                // A little less room than a new plot must leave (GameWorld.ProbeReason), so a building put up
                // beside the street does not push it elsewhere.
                float overlap = radius + 1f + HalfWidth - distance;
                if (overlap <= 0f) continue;
                cost += built ? Blocked + overlap * 20f : 2f + overlap * 0.5f;
            }
            long key = ((long)s.Id << 32) | ((long)Mathf.RoundToInt(Mathf.Repeat(bearing, 360f)) << 20) | ((long)Mathf.RoundToInt(from) << 10) | (uint)Mathf.RoundToInt(length);
            float land;
            if (!StreetTerrain.TryGetValue(key, out land))
            {
                land = 0f;
                WorldGenerator gen = WorldGenerator.instance;
                float dry = ZoneSystem.c_WaterLevel + 1f, previous = gen.GetHeight(s.Pos.X + dir.x * from, s.Pos.Z + dir.z * from);
                for (float d = from + 4f; d <= length + 16f; d += 4f)
                {
                    float h = gen.GetHeight(s.Pos.X + dir.x * d, s.Pos.Z + dir.z * d);
                    if (h < dry) land += 25f;
                    float grade = Mathf.Abs(h - previous) / 4f;
                    if (grade > 0.25f) land += (grade - 0.25f) * 60f;
                    previous = h;
                }
                StreetTerrain[key] = land;
            }
            return cost + land;
        }

        /// <summary>
        /// The street inside the settlement: from the square to where it runs straight out, the
        /// cheapest way on a 2 m grid that keeps off every standing building (and a little off the empty
        /// plots and the steep ground), then pulled straight wherever a straight line keeps clear. Only
        /// the settlement's own record and the world's heights go into it, so every player finds the same.
        /// </summary>
        private static List<Vector3> Inner(Settlement s, float bearing, float straightFrom, Dictionary<int, float> standing, int hash)
        {
            string key = s.Id + ":" + hash + ":" + bearing.ToString("0", CultureInfo.InvariantCulture) + ":" + straightFrom.ToString("0.0", CultureInfo.InvariantCulture);
            List<Vector3> cached;
            if (InnerRoutes.TryGetValue(key, out cached)) return cached;
            // Room to spare first; a narrower way past the buildings only where there is no other.
            List<Vector3> way = Inner(s, bearing, straightFrom, standing, 0.8f) ?? Inner(s, bearing, straightFrom, standing, 0.3f);
            InnerRoutes[key] = way;
            return way;
        }

        /// <summary>One try at the street inside: null when no way keeps that far (metres past each building's radius) off the buildings.</summary>
        private static List<Vector3> Inner(Settlement s, float bearing, float straightFrom, Dictionary<int, float> standing, float room)
        {

            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z), dir = Dir(bearing);
            Vector3 goalAt = centre + dir * straightFrom;
            Vec2[] offsets = OffsetsOf(s);
            List<KeyValuePair<Vector3, float>> pads = new List<KeyValuePair<Vector3, float>>(), plots = new List<KeyValuePair<Vector3, float>>();
            for (int plot = 0; plot < offsets.Length; plot++)
            {
                Vector3 at = centre + new Vector3(offsets[plot].X, 0f, offsets[plot].Z);
                float radius;
                if (standing.TryGetValue(plot, out radius)) pads.Add(new KeyValuePair<Vector3, float>(at, radius));
                else plots.Add(new KeyValuePair<Vector3, float>(at, 5f));
            }

            const float cell = 2f;
            int n = Mathf.CeilToInt((straightFrom + 2f) / cell);
            float limit = straightFrom + 1f;
            WorldGenerator gen = WorldGenerator.instance;
            Dictionary<long, float> extra = new Dictionary<long, float>(), cost = new Dictionary<long, float>(), heights = new Dictionary<long, float>();
            Dictionary<long, long> cameFrom = new Dictionary<long, long>();
            List<KeyValuePair<float, long>> open = new List<KeyValuePair<float, long>>();
            int gx = Mathf.RoundToInt((goalAt.x - centre.x) / cell), gz = Mathf.RoundToInt((goalAt.z - centre.z) / cell);
            long goal = ((long)gx << 32) | (uint)gz;
            System.Func<int, int, Vector3> world = (x, z) => new Vector3(centre.x + x * cell, 0f, centre.z + z * cell);
            // What standing on a cell costs on top of the walk; NaN = not to be walked on.
            System.Func<int, int, float> penalty = (x, z) =>
            {
                long k = ((long)x << 32) | (uint)z;
                float v;
                if (extra.TryGetValue(k, out v)) return v;
                Vector3 p = world(x, z);
                float d = Utils.DistanceXZ(p, centre);
                v = d > limit ? float.NaN : 0f;
                foreach (KeyValuePair<Vector3, float> pad in pads)
                {
                    if (float.IsNaN(v)) break;
                    float e = Utils.DistanceXZ(p, pad.Key);
                    if (e < pad.Value + room) v = float.NaN;
                    else if (e < pad.Value + 4f) v += (pad.Value + 4f - e) * 2f;
                }
                foreach (KeyValuePair<Vector3, float> plot in plots)
                {
                    if (float.IsNaN(v)) break;
                    float e = Utils.DistanceXZ(p, plot.Key);
                    if (e < plot.Value + 2f) v += (plot.Value + 2f - e) * 0.6f;
                }
                if (!float.IsNaN(v) && gen.GetHeight(p.x, p.z) < ZoneSystem.c_WaterLevel + 0.5f) v += 20f;
                extra[k] = v;
                return v;
            };
            System.Func<int, int, float> height = (x, z) =>
            {
                long k = ((long)x << 32) | (uint)z;
                float h;
                if (!heights.TryGetValue(k, out h)) heights[k] = h = gen.GetHeight(centre.x + x * cell, centre.z + z * cell);
                return h;
            };
            // From anywhere round the square.
            for (int x = -n; x <= n; x++)
            {
                for (int z = -n; z <= n; z++)
                {
                    float d = Mathf.Sqrt(x * x + z * z) * cell;
                    if (d < Inset - 1f || d > Inset + 1f || float.IsNaN(penalty(x, z))) continue;
                    long k = ((long)x << 32) | (uint)z;
                    cost[k] = 0f;
                    HeapPush(open, new KeyValuePair<float, long>(Vector3.Distance(world(x, z), goalAt), k));
                }
            }
            bool found = false;
            int visited = 0;
            while (open.Count > 0 && visited < 20000)
            {
                KeyValuePair<float, long> top = HeapPop(open);
                long current = top.Value;
                int cx = (int)(current >> 32), cz = (int)(uint)current;
                float soFar = cost[current];
                if (top.Key > soFar + Vector3.Distance(world(cx, cz), goalAt) + 0.01f) continue;
                if (current == goal)
                {
                    found = true;
                    break;
                }
                visited++;
                float here = height(cx, cz);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        float extraCost = penalty(nx, nz);
                        if (float.IsNaN(extraCost)) continue;
                        long next = ((long)nx << 32) | (uint)nz;
                        float step = cell * (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        float grade = Mathf.Abs(height(nx, nz) - here) / step;
                        float price = soFar + step * (1f + grade * grade * 8f + extraCost * 0.5f);
                        float known;
                        if (cost.TryGetValue(next, out known) && known <= price) continue;
                        cost[next] = price;
                        cameFrom[next] = current;
                        HeapPush(open, new KeyValuePair<float, long>(price + Vector3.Distance(world(nx, nz), goalAt), next));
                    }
                }
            }
            List<Vector3> way = new List<Vector3>();
            if (found)
            {
                for (long at = goal; ; at = cameFrom[at])
                {
                    way.Add(world((int)(at >> 32), (int)(uint)at));
                    long before;
                    if (!cameFrom.TryGetValue(at, out before)) break;
                }
                way.Reverse();
                way[way.Count - 1] = goalAt;
                // Pulled straight: from each point on, the furthest one a straight line reaches with room to spare.
                List<Vector3> pulled = new List<Vector3> { way[0] };
                int from = 0;
                while (from < way.Count - 1)
                {
                    int to = from + 1;
                    for (int k = way.Count - 1; k > from + 1; k--)
                    {
                        if (Clear(way[from], way[k], pads, room + 1f))
                        {
                            to = k;
                            break;
                        }
                    }
                    pulled.Add(way[to]);
                    from = to;
                }
                way = pulled;
            }
            else return null;
            return way;
        }

        private static readonly HashSet<string> Told = new HashSet<string>();

        /// <summary>
        /// No way between the buildings on any bearing: the road stops just inside the wall's line,
        /// where the settlement's own paths take over, rather than running through a house.
        /// </summary>
        private static List<Vector3> Stopped(Settlement s, float bearing, float straightFrom)
        {
            if (Told.Add(s.Id + ":" + bearing.ToString("0", CultureInfo.InvariantCulture)))
                Plugin.Log.LogWarning(s.Name + ": no way between the buildings for the street at " + bearing.ToString("0", CultureInfo.InvariantCulture) + " deg; the road stops " + straightFrom.ToString("0", CultureInfo.InvariantCulture) + " m out");
            return new List<Vector3> { new Vector3(s.Pos.X, 0f, s.Pos.Z) + Dir(bearing) * straightFrom };
        }

        private static bool Clear(Vector3 a, Vector3 b, List<KeyValuePair<Vector3, float>> pads, float room)
        {
            float length = Utils.DistanceXZ(a, b);
            for (float d = 0f; d <= length; d += 0.5f)
            {
                Vector3 p = Vector3.Lerp(a, b, length > 0f ? d / length : 0f);
                foreach (KeyValuePair<Vector3, float> pad in pads)
                {
                    if (Utils.DistanceXZ(p, pad.Key) < pad.Value + room) return false;
                }
            }
            return true;
        }

        private static readonly Dictionary<string, List<Vector3>> InnerRoutes = new Dictionary<string, List<Vector3>>();

        /// <summary>
        /// A whole road: the street out of the first settlement (between its buildings, then straight
        /// out through where its gate is), the way over the land between the ends of the two streets,
        /// the street into the second. Null when the land does not join them.
        /// </summary>
        private static Road Compose(Settlement a, Settlement b, float bearingA, float bearingB, float street)
        {
            Vector3 ca = new Vector3(a.Pos.X, 0f, a.Pos.Z), cb = new Vector3(b.Pos.X, 0f, b.Pos.Z);
            Vector3 da = Dir(bearingA), db = Dir(bearingB);
            Vector3 endA = ca + da * street, endB = cb + db * street;
            List<Vector3> middle;
            if (Utils.DistanceXZ(endA, endB) < Cell * 2f) middle = new List<Vector3> { endA, endB };
            else
            {
                // The way between keeps out of both settlements: back inside, it would cross the wall again where no gate is.
                float keepA = Mathf.Min(Keep(a), street - 4f), keepB = Mathf.Min(Keep(b), street - 4f);
                string key = Mathf.RoundToInt(endA.x) + "," + Mathf.RoundToInt(endA.z) + ">" + Mathf.RoundToInt(endB.x) + "," + Mathf.RoundToInt(endB.z) + "/" + Mathf.RoundToInt(keepA) + "," + Mathf.RoundToInt(keepB);
                if (!Routes.TryGetValue(key, out middle))
                {
                    // Finest first; a coarser grid reaches further when the fine one runs out of breath.
                    Vector4 keep = new Vector4(keepA, keepB, 0f, 0f);
                    middle = Route(endA, endB, true, Cell, ca, cb, keep) ?? Route(endA, endB, false, Cell, ca, cb, keep) ?? Route(endA, endB, false, Cell * 2f, ca, cb, keep);
                    Routes[key] = middle;
                }
                if (middle == null) return null;
            }

            float fromA = StraightFrom(a, street), fromB = StraightFrom(b, street);
            List<Vector3> innerA = Inner(a, bearingA, fromA, StandingOf(a), PlotHash(a)) ?? Stopped(a, bearingA, fromA);
            List<Vector3> innerB = Inner(b, bearingB, fromB, StandingOf(b), PlotHash(b)) ?? Stopped(b, bearingB, fromB);
            // Straight from a few metres inside the wall to 10 m short of the street's end; only the last of
            // that is rounded into the way between, and the turn from the winding part is done before the wall.
            List<Vector3> way = new List<Vector3>(innerA);
            way.Add(ca + da * (fromA + 6f));
            if (street - 10f > fromA + 8f) way.Add(ca + da * (street - 10f));
            way.AddRange(middle);
            if (street - 10f > fromB + 8f) way.Add(cb + db * (street - 10f));
            way.Add(cb + db * (fromB + 6f));
            for (int k = innerB.Count - 1; k >= 0; k--) way.Add(innerB[k]);

            // Corners are cut into curves (twice), then the line is walked off in even steps. Not between the
            // buildings: a corner there goes round a house, and cutting it would cut into the house.
            for (int pass = 0; pass < 2 && way.Count > 2; pass++)
            {
                List<Vector3> round = new List<Vector3> { way[0] };
                for (int k = 0; k < way.Count - 1; k++)
                {
                    bool inside = Utils.DistanceXZ(way[k], ca) < fromA + 1f || Utils.DistanceXZ(way[k + 1], ca) < fromA + 1f
                        || Utils.DistanceXZ(way[k], cb) < fromB + 1f || Utils.DistanceXZ(way[k + 1], cb) < fromB + 1f;
                    if (inside)
                    {
                        if (k > 0) round.Add(way[k]);
                        continue;
                    }
                    round.Add(Vector3.Lerp(way[k], way[k + 1], 0.25f));
                    round.Add(Vector3.Lerp(way[k], way[k + 1], 0.75f));
                }
                round.Add(way[way.Count - 1]);
                way = round;
            }
            List<Vector3> points = new List<Vector3>();
            float total = 0f;
            for (int k = 0; k < way.Count - 1; k++) total += Vector3.Distance(way[k], way[k + 1]);
            if (total < Step * 4f) return null;
            float want = 0f, walked = 0f;
            for (int k = 0; k < way.Count - 1; k++)
            {
                float leg = Vector3.Distance(way[k], way[k + 1]);
                while (want <= walked + leg && want <= total)
                {
                    points.Add(Vector3.Lerp(way[k], way[k + 1], leg > 0f ? (want - walked) / leg : 0f));
                    want += Step;
                }
                walked += leg;
            }
            if (points.Count < 2) return null;
            Road road = new Road { A = a.Id, B = b.Id, Points = points.ToArray(), BearingA = bearingA, BearingB = bearingB, StreetA = street, StreetB = street };
            road.MinX = road.MinZ = float.MaxValue;
            road.MaxX = road.MaxZ = float.MinValue;
            foreach (Vector3 p in road.Points)
            {
                road.MinX = Mathf.Min(road.MinX, p.x);
                road.MaxX = Mathf.Max(road.MaxX, p.x);
                road.MinZ = Mathf.Min(road.MinZ, p.z);
                road.MaxZ = Mathf.Max(road.MaxZ, p.z);
            }
            return road;
        }

        /// <summary>The settlement's standing buildings by plot (with their radius) and a hash of them, as the last plan saw them.</summary>
        private static readonly Dictionary<int, Dictionary<int, float>> Standing = new Dictionary<int, Dictionary<int, float>>();
        private static readonly Dictionary<int, int> PlotHashes = new Dictionary<int, int>();

        private static Dictionary<int, float> StandingOf(Settlement s)
        {
            Dictionary<int, float> plots;
            return Standing.TryGetValue(s.Id, out plots) ? plots : new Dictionary<int, float>();
        }

        private static int PlotHash(Settlement s)
        {
            int hash;
            return PlotHashes.TryGetValue(s.Id, out hash) ? hash : 0;
        }

        /// <summary>Ways over the land already worked out, by their two ends; null = no way over land. The land does not change, so they are kept.</summary>
        private static readonly Dictionary<string, List<Vector3>> Routes = new Dictionary<string, List<Vector3>>();

        private const float Cell = 8f;
        private const int MaxNodes = 40000;

        /// <summary>The land's height on a grid of half cells (4 m) round the origin.</summary>
        private static float LandHeight(Dictionary<long, float> heights, Vector3 origin, int hx, int hz, float cell)
        {
            long key = ((long)hx << 32) | (uint)hz;
            float h;
            if (!heights.TryGetValue(key, out h))
            {
                h = WorldGenerator.instance.GetHeight(origin.x + hx * cell * 0.5f, origin.z + hz * cell * 0.5f);
                heights[key] = h;
            }
            return h;
        }

        /// <summary>
        /// The way between two points: the cheapest walk over dry land, found on a coarse grid from
        /// the world's own height function (so it needs nothing loaded). Water is never crossed. A
        /// climb costs more the steeper it is, by the square of its grade, and the land halfway between
        /// two cells (8 m apart) is looked at too, so a ridge or a drop between them is not stepped over
        /// unseen; strict, no 4 m steeper than about 19 degrees is taken at all (a bank that a 3 m bed cannot ease). Null when the two are not joined by land
        /// within a sensible detour.
        /// </summary>
        /// <summary>How far from its centre the way between two streets keeps out of a settlement: past its wall, or where one would be.</summary>
        private static float Keep(Settlement s)
        {
            return (s.Tier >= Tier.City ? Defs.CityWallRadius : Defs.PalisadeRadius) + 8f;
        }

        private static List<Vector3> Route(Vector3 from, Vector3 to, bool strict, float cell, Vector3 homeA, Vector3 homeB, Vector4 keep)
        {
            float direct = Vector3.Distance(from, to);
            float dry = ZoneSystem.c_WaterLevel + 1f;
            int goalX = Mathf.RoundToInt((to.x - from.x) / cell), goalZ = Mathf.RoundToInt((to.z - from.z) / cell);
            float reach = direct * 2.2f + cell * 6f;

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
                KeyValuePair<float, long> top = HeapPop(open);
                long current = top.Value;
                if (current == goal)
                {
                    found = true;
                    break;
                }
                int cx = (int)(current >> 32), cz = (int)(uint)current;
                float soFar = cost[current];
                // An entry left behind by a cheaper way found later.
                if (top.Key > soFar + Vector3.Distance(new Vector3(from.x + cx * cell, 0f, from.z + cz * cell), to) + 0.01f) continue;
                visited++;
                float here = LandHeight(heights, from, cx * 2, cz * 2, cell);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        long next = ((long)nx << 32) | (uint)nz;
                        Vector3 world = new Vector3(from.x + nx * cell, 0f, from.z + nz * cell);
                        // Only within a corridor around the straight line: a road does not go round half the island.
                        if (Vector3.Distance(world, from) + Vector3.Distance(world, to) > reach) continue;
                        if (next != goal && (Utils.DistanceXZ(world, homeA) < keep.x || Utils.DistanceXZ(world, homeB) < keep.y)) continue;
                        float h = LandHeight(heights, from, nx * 2, nz * 2, cell);
                        float mid = LandHeight(heights, from, cx * 2 + dx, cz * 2 + dz, cell);
                        if ((h < dry || mid < dry) && next != goal) continue;
                        float step = cell * (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        float grade = Mathf.Max(Mathf.Abs(mid - here), Mathf.Abs(h - mid)) / (step * 0.5f);
                        if (strict && grade > 0.35f && next != goal) continue;
                        // A grade of 0.2 costs half again the walk, 0.4 three times, 0.7 seven times; the shore is walked along only if need be.
                        float price = soFar + step * (1f + grade * grade * 12f) + Mathf.Abs(h - here) * 2f + (h < dry + 1.5f ? step * 0.6f : 0f);
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
                way.Add(new Vector3(from.x + (int)(at >> 32) * cell, 0f, from.z + (int)(uint)at * cell));
                if (at == start) break;
            }
            way.Reverse();
            way[0] = from;
            way[way.Count - 1] = to;
            return way;
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

        /// <summary>The bearing of the settlement's n-th street (in the order of its roads); 0 without one.</summary>
        public static float StreetBearing(Settlement s, int n)
        {
            EnsurePlan();
            foreach (Road road in All)
            {
                if (road.A != s.Id && road.B != s.Id) continue;
                if (n-- == 0) return road.A == s.Id ? road.BearingA : road.BearingB;
            }
            return 0f;
        }

        /// <summary>True when a road (any road: one from elsewhere may pass through) runs within the given distance of the point.</summary>
        public static bool NearRoad(Settlement s, Vector3 p, float distance)
        {
            return OnRoad(p, distance);
        }

        /// <summary>True when the line of any road passes within the given distance of the point.</summary>
        public static bool OnRoad(Vector3 p, float distance)
        {
            if (!ModConfig.Roads.Value) return false;
            EnsurePlan();
            foreach (Road road in All)
            {
                if (road.Near(p, distance) && road.Distance(p) < distance) return true;
            }
            return false;
        }

        /// <summary>
        /// Bearings (degrees, 0 = +Z, 90 = +X) at which roads cross a circle round the settlement's
        /// centre: where its wall needs gates. Every road counts, its own and any passing through, and
        /// every time it crosses.
        /// </summary>
        public static List<float> Crossings(Settlement s, float radius)
        {
            List<float> bearings = new List<float>();
            if (!ModConfig.Roads.Value) return bearings;
            EnsurePlan();
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            foreach (Road road in All)
            {
                if (!road.Near(centre, radius)) continue;
                for (int k = 1; k < road.Points.Length; k++)
                {
                    Vector3 p = road.Points[k - 1], q = road.Points[k];
                    float dp = Utils.DistanceXZ(p, centre), dq = Utils.DistanceXZ(q, centre);
                    if ((dp < radius) == (dq < radius)) continue;
                    Vector3 cross = Vector3.Lerp(p, q, Mathf.Abs(dq - dp) > 0.001f ? (radius - dp) / (dq - dp) : 0.5f);
                    bearings.Add(Mathf.Atan2(cross.x - centre.x, cross.z - centre.z) * Mathf.Rad2Deg);
                }
            }
            return bearings;
        }

        // ------------------------------------------------------------------ paint

        /// <summary>
        /// The road bed: the land along the road, evened out over some 25 m and then held to a grade
        /// people and carts walk easily where cutting or raising the ground at most 2 m allows it. Near
        /// and inside settlements it keeps to the land, where the ground is shaped pad by pad.
        /// </summary>
        private static float[] Bed(Road road)
        {
            int n = road.Points.Length;
            float[] land = new float[n], bed = new float[n], depth = new float[n];
            WorldGenerator gen = WorldGenerator.instance;
            for (int i = 0; i < n; i++)
            {
                land[i] = gen.GetHeight(road.Points[i].x, road.Points[i].z);
                // How far from the nearest settlement's grounds: no cutting there, full depth 20 m on.
                depth[i] = BedDepth * Mathf.Clamp01(OutsideGrounds(road.Points[i]) / 20f);
            }
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = Mathf.Max(0, i - 6); k <= Mathf.Min(n - 1, i + 6); k++)
                {
                    sum += land[k];
                    count++;
                }
                bed[i] = Mathf.Clamp(sum / count, land[i] - depth[i], land[i] + depth[i]);
            }
            // The grade limit and the depth limit pull against each other along a long slope: a few passes leave the
            // middle of a 50 m bank steeper than the limit, so the pass is repeated until it settles (it always does:
            // every pass only moves points towards the limits; capped at 400 passes for a road of ~200 points).
            float rise = BedGrade * Step;
            for (int pass = 0; pass < 400; pass++)
            {
                float moved = 0f;
                for (int i = 1; i < n; i++)
                {
                    float was = bed[i];
                    bed[i] = Mathf.Clamp(bed[i], bed[i - 1] - rise, bed[i - 1] + rise);
                    moved = Mathf.Max(moved, Mathf.Abs(bed[i] - was));
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    float was = bed[i];
                    bed[i] = Mathf.Clamp(bed[i], bed[i + 1] - rise, bed[i + 1] + rise);
                    moved = Mathf.Max(moved, Mathf.Abs(bed[i] - was));
                }
                for (int i = 0; i < n; i++)
                {
                    float was = bed[i];
                    bed[i] = Mathf.Clamp(bed[i], land[i] - depth[i], land[i] + depth[i]);
                    moved = Mathf.Max(moved, Mathf.Abs(bed[i] - was));
                }
                if (moved < 0.001f) break;
            }
            return bed;
        }

        /// <summary>How far a point is outside the grounds of the nearest settlement (negative inside).</summary>
        private static readonly Dictionary<long, float> BedCut = new Dictionary<long, float>();

        private static long BedKey(Vector3 p)
        {
            return ((long)Mathf.RoundToInt(p.x / 4f) << 32) ^ (uint)Mathf.RoundToInt(p.z / 4f);
        }

        private static float OutsideGrounds(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed) continue;
                best = Mathf.Min(best, Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) - Grounds(s));
            }
            return best;
        }

        /// <summary>The grounds of a settlement where its own pads (and its wall) shape the land: no road works the ground there.</summary>
        private static float Grounds(Settlement s)
        {
            // The next ring out too: a settlement builds there when its own rings have no room (Simulation.FindFreePlot).
            float rings = Defs.RingRadius[Mathf.Min(Defs.RingsByTier[(int)s.Tier], Defs.RingRadius.Length - 1)] + 12f;
            if (s.Tier >= Tier.City) return Mathf.Max(rings, Defs.CityWallRadius + 8f);
            if (s.Tier >= Tier.Town) return Mathf.Max(rings, Defs.PalisadeRadius + 8f);
            return rings;
        }

        private static void PaintRoads(Vector3 reference)
        {
            int budget = 60;
            List<int> bedded = new List<int>();
            for (int r = 0; r < All.Count && budget > 0; r++)
            {
                Road road = All[r];
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null || !road.Near(reference, PaintRange)) continue;
                bedded.Clear();
                for (int i = 0; i < road.Points.Length && budget > 0; i++)
                {
                    Vector3 p = road.Points[i];
                    if (Utils.DistanceXZ(reference, p) > PaintRange) continue;
                    long key = Key(p);
                    // The objects round the spot too: a player's building there keeps its ground.
                    if (Painted.Contains(key) || !ZoneSystem.instance.IsZoneLoaded(p)) continue;
                    Painted.Add(key);
                    if (UnderBuilding(p, 0.5f)) continue;
                    if (road.Bed == null) road.Bed = Bed(road);
                    // Inside a settlement the ground is already shaped pad by pad; reshaping it there would tug at the pads.
                    float ground;
                    if (OutsideGrounds(p) > 0f && !NearPad(p, 7.5f) && !PlayerBuilt(p, 7f) && ZoneSystem.instance.GetGroundHeight(p, out ground))
                    {
                        float other;
                        if (Mathf.Abs(road.Bed[i] - ground) > 0.25f && !(BedCut.TryGetValue(BedKey(p), out other) && Mathf.Abs(other - road.Bed[i]) > 0.3f))
                        {
                            // Cut down or built up to the bed, and smoothed round it (also across the border of two
                            // terrain tiles: the torn edge is mended afterwards, HealSeams). Where two roads run together
                            // the first one's bed is kept: two beds at different heights would fight into steps.
                            BedCut[BedKey(p)] = road.Bed[i];
                            Materializer.QueueGrade(new Vector3(p.x, road.Bed[i], p.z), 2.4f, true);
                            _graded++;
                            bedded.Add(i);
                        }
                        // Close enough to the bed already: only evened out toward it (below).
                        else if (!BedCut.TryGetValue(BedKey(p), out other) || Mathf.Abs(other - road.Bed[i]) <= 0.3f) bedded.Add(i);
                    }
                    // The last stretch into a town is cobbled; the rest is a beaten track.
                    bool cobbled = (a.Tier >= Tier.Town && i * Step < 75f) || (b.Tier >= Tier.Town && (road.Points.Length - 1 - i) * Step < 75f);
                    Materializer.QueuePaint(p, HalfWidth, cobbled ? 2 : 1);
                    _paintedPoints++;
                    budget--;
                }
                // After the levelling, in the same line behind it: each levelled disc is flat at its own height, a
                // step of up to 0.4 m from the next on a slope. Drawn toward the bed a spot every metre (at the
                // point and halfway to the next), the road runs up and down the bed evenly instead.
                foreach (int i in bedded)
                {
                    for (int half = 0; half < 2; half++)
                    {
                        int j = Mathf.Min(i + half, road.Points.Length - 1);
                        if (half == 1 && j == i) break;
                        Vector3 spot = Vector3.Lerp(road.Points[i], road.Points[j], 0.5f * half);
                        Materializer.QueueBlend(new Vector3(spot.x, Mathf.Lerp(road.Bed[i], road.Bed[j], 0.5f * half), spot.z), BlendRadius);
                        _blended++;
                    }
                }
            }
        }

        /// <summary>How far round each spot of a road bed the ground is drawn toward it: the road's width and a little.</summary>
        private const float BlendRadius = 3.2f;
        private static int _blended;

        /// <summary>
        /// Close to the player, where the world's objects exist: trees, bushes, rocks and stumps on
        /// the road are cleared away and the lamp posts are put up. A post already standing (from an
        /// earlier visit) is recognised by its tag and not doubled.
        /// </summary>
        private static void Furnish(Vector3 reference)
        {
            int budget = 30, pending = 0;
            for (int r = 0; r < All.Count; r++)
            {
                Road road = All[r];
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null || !road.Near(reference, FurnishRange)) continue;
                for (int i = 0; i < road.Points.Length; i++)
                {
                    Vector3 p = road.Points[i];
                    if (Utils.DistanceXZ(reference, p) > FurnishRange) continue;
                    long key = Key(p);
                    float cleared;
                    // Not waiting for the whole area to be instantiated: ZNetScene.IsAreaReady stays false for ever on the
                    // edge of the loaded area, where the trees and rocks of a road are, and nothing there got cleared.
                    // What stands now is cleared; what comes later is cleared on the next pass (every 45 s).
                    if ((Furnished.TryGetValue(key, out cleared) && Time.time - cleared < 45f) || !ZoneSystem.instance.IsZoneLoaded(p)) continue;
                    // A lamp waits until the ground under it has been cut to the road bed.
                    float ground;
                    if (budget <= 0 || Materializer.GroundWorkNear(p, 10f) || !ZoneSystem.instance.GetGroundHeight(p, out ground))
                    {
                        pending++;
                        continue;
                    }
                    Furnished[key] = Time.time;
                    budget--;
                    if (ground < ZoneSystem.c_WaterLevel + 0.3f) continue;
                    p.y = ground;
                    // Beside the buildings too: clearing leaves every piece alone.
                    Materializer.ClearVegetation(p, 3.4f);
                    _cleared++;
                    if (ModConfig.RoadLamps.Value && i % LampEvery == LampEvery / 2 && !UnderBuilding(p, 1.5f)) PlaceLamp(road, i, p);
                }
            }
            Unfurnished = pending;
        }

        /// <summary>Stretches of road round the player still to be cleared and lit (the test runner waits for none).</summary>
        public static int Unfurnished;

        /// <summary>Marks what the current build puts up; posts of an older make are taken down when met.</summary>
        private const int LampMake = 4;
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

        /// <summary>True when the levelled ground of a building pad (of any settlement) is within reach: road work keeps off it.</summary>
        private static bool NearPad(Vector3 p, float reach)
        {
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed || Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 100f) continue;
                foreach (Building b in s.Buildings)
                {
                    float radius = Blueprints.Radius(b.Kind);
                    if (radius <= 0f || b.State == BuildingState.Destroyed || float.IsNaN(b.GroundY)) continue;
                    Vec2 offset = new Vec2(0f, 0f);
                    float yaw;
                    if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                    if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < radius + 1.5f + reach) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The streets of a settlement inside its grounds, each from the square outwards: where the one
        /// who builds the settlement cuts ramps over the banks (Materializer.EaseStreets), since the road
        /// keeper leaves the ground there alone.
        /// </summary>
        public static List<List<Vector3>> StreetsOf(Settlement s)
        {
            List<List<Vector3>> streets = new List<List<Vector3>>();
            if (!ModConfig.Roads.Value) return streets;
            EnsurePlan();
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            float grounds = Grounds(s) + 4f;
            foreach (Road road in All)
            {
                if (road.A != s.Id && road.B != s.Id) continue;
                List<Vector3> line = new List<Vector3>();
                int n = road.Points.Length;
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = road.Points[road.A == s.Id ? k : n - 1 - k];
                    if (Utils.DistanceXZ(p, centre) > grounds) break;
                    line.Add(p);
                }
                if (line.Count >= 2) streets.Add(line);
            }
            return streets;
        }

        /// <summary>True when something a player built stands near: the road leaves the ground there as it is.</summary>
        internal static bool PlayerBuilt(Vector3 p, float reach)
        {
            float ground;
            if (ZoneSystem.instance.GetGroundHeight(p, out ground)) p.y = ground;
            foreach (Collider collider in Physics.OverlapSphere(p, reach))
            {
                Piece piece = collider.GetComponentInParent<Piece>();
                if (piece == null) continue;
                ZNetView view = piece.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetInt(HashRoad) != 0 || view.GetZDO().GetInt(Materializer.HashSettlement) != 0) continue;
                if (piece.GetCreator() != 0L) return true;
            }
            return false;
        }

        private static bool NearSettlement(Vector3 p, float distance)
        {
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (!s.Destroyed && Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) < distance) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a lamp post may stand: on dry ground outside every settlement's wall, off the line of
        /// every road (a post of one road on another where two meet), off the pads and clear of
        /// anything solid that is not growth (growth is cleared).
        /// </summary>
        private static bool LampSpotFree(Vector3 pos)
        {
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(pos, out ground) || ground < ZoneSystem.c_WaterLevel + 0.3f) return false;
            if (NearSettlement(pos, LampClear) || UnderBuilding(pos, 1.5f)) return false;
            foreach (Road other in All)
            {
                if (other.Near(pos, HalfWidth + 1f) && other.Distance(pos) < HalfWidth + 0.8f) return false;
            }
            foreach (Collider collider in Physics.OverlapSphere(new Vector3(pos.x, ground + 1f, pos.z), 0.7f))
            {
                if (collider.isTrigger || collider.GetComponentInParent<Heightmap>() != null || collider.GetComponentInParent<Character>() != null) continue;
                ZNetView view = collider.GetComponentInParent<ZNetView>();
                if (view == null || !view.IsValid())
                {
                    // Something solid of the world with no object behind it: a location's walls or rocks.
                    return false;
                }
                GameObject root = view.gameObject;
                bool growth = root.GetComponent<TreeBase>() != null || root.GetComponent<TreeLog>() != null || root.GetComponent<Destructible>() != null
                    || root.GetComponent<MineRock>() != null || root.GetComponent<MineRock5>() != null || root.GetComponent<Pickable>() != null;
                if (!growth && view.GetZDO().GetInt(HashRoad) == 0) return false;
            }
            return true;
        }

        /// <summary>A lamp post: a log pole as tall as a man and a half, with a light at its head.</summary>
        private static void PlaceLamp(Road road, int i, Vector3 p)
        {
            Vector3 along = road.Points[Mathf.Min(i + 1, road.Points.Length - 1)] - road.Points[Mathf.Max(i - 1, 0)];
            along.y = 0f;
            Vector3 side = new Vector3(-along.z, 0f, along.x).normalized;
            // Posts alternate sides of the road, just off its edge; the other side when that one is taken.
            if (((i / LampEvery) & 1) != 0) side = -side;
            Vector3 pos = p + side * 3.6f;
            if (!LampSpotFree(pos))
            {
                side = -side;
                pos = p + side * 3.6f;
                if (!LampSpotFree(pos)) return;
            }
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(pos, out ground)) return;
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
                // The torch's plate against the pole, the torch out over the road (its +X: Blueprints.Sconce).
                Vector3 out_ = -side;
                out_.y = 0f;
                out_.Normalize();
                float reach = Blueprints.HalfThickness(pole.name) + 0.21f;
                PutUp(light, pos + Vector3.up * 1.75f + out_ * reach, Quaternion.Euler(0f, Mathf.Atan2(-out_.z, out_.x) * Mathf.Rad2Deg, 0f));
            }
            else PutUp(light, pos, facing);
            _lampKit = (pole != null && light.name == "piece_walltorch" ? pole.name + " + " : "") + light.name;
            _lamps++;
        }

        private static readonly List<ZDO> LampScan = new List<ZDO>();

        /// <summary>
        /// Lamp posts that should not be where they are: of an older make, inside a settlement (where
        /// older versions put them, in its wall or on its pads), or by no road any more. Taken down
        /// around the player; the right ones go up as the road is furnished.
        /// </summary>
        private static void SweepLamps(Vector3 reference)
        {
            if (All.Count == 0) return;
            LampScan.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(reference), new SimulationDistance(1, 0, true), LampScan);
            foreach (ZDO zdo in LampScan.ToArray())
            {
                int make = zdo.GetInt(HashRoad);
                if (make == 0) continue;
                Vector3 at = zdo.GetPosition();
                bool onRoad = false;
                foreach (Road road in All)
                {
                    if (road.Near(at, 6f) && road.Distance(at) < 5.5f)
                    {
                        onRoad = true;
                        break;
                    }
                }
                if (make == LampMake && onRoad && !NearSettlement(at, LampClear - 4f)) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view != null)
                {
                    view.ClaimOwnership();
                    ZNetScene.instance.Destroy(view.gameObject);
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(zdo);
                }
                _lampsSwept++;
            }
        }

        internal static bool NearTileBorder(Vector3 p, float reach)
        {
            // Tiles are 64 m across, centred on multiples of 64.
            float dx = Mathf.Abs(Mathf.Repeat(p.x + 32f, 64f)), dz = Mathf.Abs(Mathf.Repeat(p.z + 32f, 64f));
            return dx < reach || dx > 64f - reach || dz < reach || dz > 64f - reach;
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

        /// <summary>Roads go round what stands in a settlement rather than under it (any settlement: a road may pass through another).</summary>
        private static bool UnderBuilding(Vector3 p, float margin)
        {
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed || Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 85f) continue;
                foreach (Building b in s.Buildings)
                {
                    if (b.Plot < 0 || b.State == BuildingState.Destroyed) continue;
                    Vec2 offset;
                    float yaw;
                    Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                    if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < Blueprints.Radius(b.Kind) + margin) return true;
                }
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

        /// <summary>The steepest 10 m of a profile, as a grade, and where it is (metres from the start).</summary>
        private static float Steepest(float[] heights, out float at)
        {
            at = 0f;
            float worst = 0f;
            for (int i = 5; i < heights.Length; i++)
            {
                float grade = Mathf.Abs(heights[i] - heights[i - 5]) / (5f * Step);
                if (grade > worst)
                {
                    worst = grade;
                    at = i * Step;
                }
            }
            return worst;
        }

        public static string Describe()
        {
            EnsurePlan();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine(All.Count + " roads, " + _paintedPoints + " road points painted this session (" + _graded + " cut to the bed, " + _blended + " spots drawn even with it), " + _cleared + " cleared of growth, lamp posts put up " + _lamps
                + " (" + _lampKit + "), out of place taken down " + _lampsSwept + ", trails worn " + TrailKeys.Count);
            foreach (Road road in All)
            {
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                if (a == null || b == null) continue;
                int wet = 0;
                float[] land = new float[road.Points.Length];
                for (int i = 0; i < road.Points.Length; i++)
                {
                    Vector3 q = road.Points[i];
                    land[i] = WorldGenerator.instance.GetHeight(q.x, q.z);
                    if (land[i] < ZoneSystem.c_WaterLevel + 0.3f) wet++;
                }
                if (road.Bed == null) road.Bed = Bed(road);
                float landAt, bedAt;
                float landWorst = Steepest(land, out landAt), bedWorst = Steepest(road.Bed, out bedAt);
                sb.AppendLine("  " + a.Name + " - " + b.Name + ": " + ((road.Points.Length - 1) * Step).ToString("0", CultureInfo.InvariantCulture) + " m (straight " + Vec2.Distance(a.Pos, b.Pos).ToString("0", CultureInfo.InvariantCulture)
                    + " m), points under water " + wet + ", streets at " + road.BearingA.ToString("0", CultureInfo.InvariantCulture) + " / " + road.BearingB.ToString("0", CultureInfo.InvariantCulture)
                    + " deg, steepest 10 m: land " + (landWorst * 100f).ToString("0", CultureInfo.InvariantCulture) + "% at " + landAt.ToString("0", CultureInfo.InvariantCulture)
                    + " m, bed " + (bedWorst * 100f).ToString("0", CultureInfo.InvariantCulture) + "% at " + bedAt.ToString("0", CultureInfo.InvariantCulture) + " m");
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ checks (dev)

        /// <summary>
        /// `roadcheck <settlement>`: walks every road into the settlement from 100 m out to its square
        /// with the player's own capsule and says what stands in the way; also lamps inside its wall,
        /// field fences across a road, gates that do not meet a road, and the steepest stretch of the
        /// ground as it is now. Lines with BLOCKED, MISALIGNED, LAMP INSIDE or FENCE ON ROAD are faults.
        /// </summary>
        public static string Check(Settlement s)
        {
            EnsurePlan();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            CapsuleCollider body = Player.m_localPlayer != null ? Player.m_localPlayer.GetCollider() : null;
            float r = body != null ? body.radius : 0.4f, h = body != null ? body.height : 1.85f;
            int roads = 0;
            foreach (Road road in All)
            {
                if (!road.Near(centre, 100f)) continue;
                roads++;
                Settlement a = SimHost.Sim.State.GetSettlement(road.A), b = SimHost.Sim.State.GetSettlement(road.B);
                string name = (a != null ? a.Name : "?") + " - " + (b != null ? b.Name : "?");
                Dictionary<string, int> blockers = new Dictionary<string, int>();
                Dictionary<string, string> kindsOf = new Dictionary<string, string>();
                float steepest = 0f, steepAt = 0f, previous = float.NaN;
                int steep = 0;
                int walked = 0, unloaded = 0;
                for (int k = 1; k < road.Points.Length; k++)
                {
                    Vector3 p = road.Points[k - 1], q = road.Points[k];
                    if (Utils.DistanceXZ(p, centre) > 100f && Utils.DistanceXZ(q, centre) > 100f) continue;
                    float gp, gq;
                    if (!ZoneSystem.instance.IsZoneLoaded(p) || !ZoneSystem.instance.GetGroundHeight(p, out gp) || !ZoneSystem.instance.GetGroundHeight(q, out gq))
                    {
                        unloaded++;
                        continue;
                    }
                    walked++;
                    if (!float.IsNaN(previous))
                    {
                        float grade = Mathf.Abs(gq - gp) / Step;
                        // A step on the road outside the settlement's grounds is a fault, however it is walked.
                        if (grade > MaxRoadGrade && OutsideGrounds(q) > 0f && OutsideGrounds(p) > 0f) steep++;
                        if (grade > steepest)
                        {
                            steepest = grade;
                            steepAt = Utils.DistanceXZ(q, centre);
                        }
                    }
                    previous = gq;
                    // Feet a step above the ground, so the ground itself and a low kerb do not count.
                    Vector3 feet = new Vector3(p.x, gp + r + 0.35f, p.z), head = feet + Vector3.up * Mathf.Max(0f, h - 2f * r);
                    Vector3 dir = new Vector3(q.x, gq + r + 0.35f, q.z) - feet;
                    foreach (RaycastHit hit in Physics.CapsuleCastAll(feet, head, r, dir.normalized, dir.magnitude))
                    {
                        if (hit.collider.isTrigger || hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Heightmap>() != null) continue;
                        ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
                        string what = view != null ? Utils.GetPrefabName(view.gameObject) : hit.collider.name;
                        if (what.Contains("door") || hit.collider.GetComponentInParent<ItemDrop>() != null) continue;
                        int n;
                        blockers.TryGetValue(what, out n);
                        blockers[what] = n + 1;
                        // What the object is made of, once per kind: says why the clearing did not take it.
                        if (n == 0 && view != null && !kindsOf.ContainsKey(what))
                        {
                            HashSet<string> kinds = new HashSet<string>();
                            foreach (Component c in view.gameObject.GetComponentsInChildren<Component>(true)) kinds.Add(c.GetType().Name);
                            List<string> names = new List<string>(kinds);
                            kindsOf[what] = "valid " + view.IsValid() + ", " + Utils.DistanceXZ(hit.point, q).ToString("0.0", CultureInfo.InvariantCulture) + " m from the line, parts " + string.Join("/", names.ToArray());
                        }
                    }
                }
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> blocker in blockers) parts.Add(blocker.Key + " x" + blocker.Value + (kindsOf.ContainsKey(blocker.Key) ? " [" + kindsOf[blocker.Key] + "]" : ""));
                sb.AppendLine("road " + name + ": walked " + walked + " steps within 100 m" + (unloaded > 0 ? " (" + unloaded + " not loaded)" : "") + ", steepest step "
                    + (steepest * 100f).ToString("0", CultureInfo.InvariantCulture) + "% at " + steepAt.ToString("0", CultureInfo.InvariantCulture) + " m from the centre: "
                    + (parts.Count == 0 ? "clear" : "BLOCKED by " + string.Join(", ", parts.ToArray()))
                    + (steep > 0 ? ", STEEP: " + steep + " steps over " + (MaxRoadGrade * 100f).ToString("0", CultureInfo.InvariantCulture) + "%" : ""));
            }
            if (roads == 0) sb.AppendLine("no road comes within 100 m");

            // Gates against roads: every crossing should be in a gate's 6 m opening.
            foreach (Building wall in s.Buildings)
            {
                if (wall.Kind != BuildingKind.Palisade || wall.State != BuildingState.Complete || string.IsNullOrEmpty(wall.Wall)) continue;
                float radius = s.Tier >= Tier.City ? Defs.CityWallRadius : Defs.PalisadeRadius;
                List<float> gates = GatesOf(wall.Wall);
                foreach (float crossing in Crossings(s, radius))
                {
                    float off = float.MaxValue;
                    foreach (float g in gates) off = Mathf.Min(off, Mathf.Abs(Mathf.DeltaAngle(g, crossing)) * Mathf.Deg2Rad * radius);
                    sb.AppendLine("road crosses the wall at " + Mathf.Repeat(crossing, 360f).ToString("0.0", CultureInfo.InvariantCulture) + " deg, " + off.ToString("0.0", CultureInfo.InvariantCulture)
                        + " m from the middle of a gate" + (off > 1.5f ? ": MISALIGNED" : ""));
                }
            }

            // Lamps and fences around it.
            LampScan.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, LampScan);
            int lampsInside = 0, fences = 0;
            int fence = "wood_fence".GetStableHashCode();
            foreach (ZDO zdo in LampScan)
            {
                Vector3 at = zdo.GetPosition();
                if (zdo.GetInt(HashRoad) != 0 && Utils.DistanceXZ(at, centre) < LampClear - 4f) lampsInside++;
                if (zdo.GetPrefab() == fence && zdo.GetInt(Materializer.HashSettlement) == s.Id && OnRoad(at, HalfWidth + 0.4f)) fences++;
            }
            sb.AppendLine(lampsInside == 0 ? "no road lamps inside" : lampsInside + " road lamp pieces within " + (LampClear - 4f).ToString("0", CultureInfo.InvariantCulture) + " m: LAMP INSIDE");
            sb.AppendLine(fences == 0 ? "no field fence on a road" : fences + " field fence pieces on a road: FENCE ON ROAD");
            return sb.ToString().TrimEnd();
        }

        /// <summary>How far out of the settlement, along its road, the land is steepest (outside every settlement's grounds); -1 without a road.</summary>
        public static float SteepestOut(Settlement s)
        {
            EnsurePlan();
            Road road = RoadOf(s.Id);
            if (road == null) return -1f;
            int n = road.Points.Length;
            float worst = -1f, at = -1f;
            for (int k = 5; k < n; k++)
            {
                Vector3 p = road.Points[road.A == s.Id ? k : n - 1 - k], q = road.Points[road.A == s.Id ? k - 5 : n - 1 - (k - 5)];
                if (OutsideGrounds(p) < 0f || OutsideGrounds(q) < 0f) continue;
                float grade = Mathf.Abs(WorldGenerator.instance.GetHeight(p.x, p.z) - WorldGenerator.instance.GetHeight(q.x, q.z));
                if (grade > worst)
                {
                    worst = grade;
                    at = Inset + (k - 2.5f) * Step;
                }
            }
            return at;
        }

        private const float MaxRoadGrade = 0.4f;

        /// <summary>
        /// `roadslope <settlement> [metres]`: the ground of its road as it is now, 30 m either side of the
        /// point that far out (default: where the land is steepest), against the land and the bed it
        /// should have been cut to. TOO STEEP when a loaded 10 m of it outside the settlements climbs over 40 %.
        /// </summary>
        public static string Slope(Settlement s, float around)
        {
            EnsurePlan();
            Road road = RoadOf(s.Id);
            if (road == null) return s.Name + " has no road";
            if (road.Bed == null) road.Bed = Bed(road);
            int n = road.Points.Length;
            int centre = Mathf.Clamp(Mathf.RoundToInt((around - Inset) / Step), 0, n - 1);
            float worstGround = 0f, worstBed = 0f, worstLand = 0f, at = 0f;
            int loaded = 0;
            bool fault = false;
            for (int k = Mathf.Max(5, centre - 15); k <= Mathf.Min(n - 1, centre + 15); k++)
            {
                int i = road.A == s.Id ? k : n - 1 - k, j = road.A == s.Id ? k - 5 : n - 1 - (k - 5);
                Vector3 p = road.Points[i], q = road.Points[j];
                float gp, gq;
                if (!ZoneSystem.instance.IsZoneLoaded(p) || !ZoneSystem.instance.IsZoneLoaded(q) || !ZoneSystem.instance.GetGroundHeight(p, out gp) || !ZoneSystem.instance.GetGroundHeight(q, out gq)) continue;
                loaded++;
                float run = 5f * Step;
                float ground = Mathf.Abs(gp - gq) / run, bed = Mathf.Abs(road.Bed[i] - road.Bed[j]) / run;
                float land = Mathf.Abs(WorldGenerator.instance.GetHeight(p.x, p.z) - WorldGenerator.instance.GetHeight(q.x, q.z)) / run;
                if (ground > worstGround)
                {
                    worstGround = ground;
                    at = Inset + (k - 2.5f) * Step;
                }
                worstBed = Mathf.Max(worstBed, bed);
                worstLand = Mathf.Max(worstLand, land);
                // Over 40 % for 10 m (22 degrees) is more than a road should climb.
                if (ground > MaxRoadGrade && OutsideGrounds(p) > 0f && OutsideGrounds(q) > 0f && !UnderBuilding(p, 0.5f)) fault = true;
            }
            if (loaded == 0) return "the road around " + around.ToString("0", CultureInfo.InvariantCulture) + " m is not loaded";
            return "road of " + s.Name + " round " + around.ToString("0", CultureInfo.InvariantCulture) + " m out (" + loaded + " stretches of 10 m): steepest ground now " + (worstGround * 100f).ToString("0", CultureInfo.InvariantCulture)
                + "% at " + at.ToString("0", CultureInfo.InvariantCulture) + " m, land " + (worstLand * 100f).ToString("0", CultureInfo.InvariantCulture) + "%, bed " + (worstBed * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"
                + (fault ? ": TOO STEEP" : "");
        }

        /// <summary>The gate bearings written in a wall's signature (Blueprints.Wall): "stakeT:12.3,190.0".</summary>
        public static List<float> GatesOf(string signature)
        {
            List<float> gates = new List<float>();
            int colon = signature != null ? signature.IndexOf(':') : -1;
            if (colon < 0) return gates;
            foreach (string part in signature.Substring(colon + 1).Split(','))
            {
                float g;
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out g)) gates.Add(g);
            }
            return gates;
        }
    }
}
