using System.Collections.Generic;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// The ground under the roads, laid straight into the heights of each terrain tile.
    ///
    /// The game's own tools could not do it well: a levelling makes a flat disc, so a bed cut disc by disc
    /// up a slope came out as a stair, and smoothing (which the game lets move the ground at most 1 m and
    /// which the next levelling wipes out) could not take the steps and the sharp edges of the deeper cuts
    /// away again. Here every ground point of a tile near a road gets its height at once from the road bed:
    /// on the road itself (and a little to each side) exactly the bed's height at that spot along the road,
    /// so the way rises and falls evenly; beside it the bank runs smoothly back to the land, the wider the
    /// deeper the cut or the fill. The height depends only on where the point is, so the two tiles of a
    /// border work out the same heights on it and do not tear apart.
    /// </summary>
    internal static partial class Roads
    {
        /// <summary>The make of the beds: a tile laid by an older make is laid again.</summary>
        private const int BedMake = 3;
        /// <summary>Flat at the bed's height this far from the road's middle: the paint and a little more.</summary>
        private const float BedCore = HalfWidth + 0.7f;
        /// <summary>The bank beside the road: at least this wide, and wider for a deep cut (this much run for each metre of height), up to the most.</summary>
        private const float BankMin = 3f, BankPerMetre = 2.5f, BankMax = 8f;
        private const float BedReach = BedCore + BankMax;
        /// <summary>Tiles this close to the player are laid: past where the road is cleared and lit (a lamp waits for its tile).</summary>
        private const float BedRange = FurnishRange + 64f;

        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcLevel = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_levelDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcSmooth = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_smoothDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> TcModified = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");

        /// <summary>The heightmaps of tiles that are laid to the current plan: a lamp waits for its tile.</summary>
        private static readonly HashSet<int> BedTilesDone = new HashSet<int>();
        private static readonly List<Heightmap> BedFound = new List<Heightmap>();
        private static readonly List<Heightmap> BedCandidates = new List<Heightmap>();
        private static readonly Dictionary<int, float> BedRetryAt = new Dictionary<int, float>();
        private static readonly List<Piece> BedPieces = new List<Piece>();
        private static float _nextBed;
        private static int _bedTiles, _bedVertices;

        private struct BedSegment
        {
            public Vector3 A, B;
            public float BedA, BedB;
            public int Road;
        }

        private static readonly List<BedSegment> Segments = new List<BedSegment>();
        private static readonly List<Vector4> Pads = new List<Vector4>();
        private static readonly List<Vector3> Pieces = new List<Vector3>();
        private static readonly List<Vector3> SettlementGrounds = new List<Vector3>();

        private const float PadBankReach = 24f;
        private const float PadBankGrade = 0.35f;
        private static readonly Dictionary<int, Dictionary<int, int>> PadBankTiles = new Dictionary<int, Dictionary<int, int>>();
        private static readonly List<Vector4> BankPads = new List<Vector4>();
        private static readonly List<Heightmap> BankTiles = new List<Heightmap>();

        /// <summary>Grades the shoulders around finished pads, including the low ground left between them.</summary>
        internal static void ShapePadBanks(Settlement settlement, Vector3 reference)
        {
            Vector3 centre = new Vector3(settlement.Pos.X, 0f, settlement.Pos.Z);
            if (settlement.CampaignId != 0 || Utils.DistanceXZ(centre, reference) > FurnishRange) return;

            BankPads.Clear();
            int plan = 17;
            float reach = 0f;
            foreach (Building building in settlement.Buildings)
            {
                float radius = Blueprints.Radius(building.Kind);
                if (radius <= 0f || building.SpawnedPieces == 0 || building.State == BuildingState.Destroyed || float.IsNaN(building.GroundY)) continue;
                Vec2 offset = default(Vec2);
                if (building.Plot >= 0) Defs.PlotTransform(settlement, building.Plot, out offset, out _);
                // The levelling brush extends past the footprint. Its last row of vertices can
                // straddle the edge of the disc and leave teeth between the pad and its shoulder.
                Vector4 pad = new Vector4(centre.x + offset.X, building.GroundY, centre.z + offset.Z, radius);
                BankPads.Add(pad);
                plan = unchecked(plan * 31 + pad.GetHashCode());
                reach = Mathf.Max(reach, Utils.DistanceXZ(centre, new Vector3(pad.x, 0f, pad.z)) + pad.w + PadBankReach);
            }
            if (BankPads.Count == 0) return;

            BankTiles.Clear();
            Heightmap.FindHeightmap(centre, reach, BankTiles);
            foreach (Heightmap tile in BankTiles)
            {
                if (tile == null || tile.IsDistantLod) continue;
                Vector3 tileCentre = tile.transform.position;
                float half = tile.m_width * tile.m_scale * 0.5f;
                int tileId = tile.GetInstanceID();
                Dictionary<int, int> graded;
                int done;
                if (PadBankTiles.TryGetValue(tileId, out graded) && graded.TryGetValue(settlement.Id, out done) && done == plan) continue;
                if (Utils.DistanceXZ(reference, tileCentre) > FurnishRange || Materializer.GroundWorkInTile(tileCentre, half)) continue;

                // Do not create a terrain compiler for a tile beyond the influence of every pad.
                bool reachesTile = false;
                foreach (Vector4 pad in BankPads)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(tileCentre.x - pad.x) - half);
                    float dz = Mathf.Max(0f, Mathf.Abs(tileCentre.z - pad.z) - half);
                    float limit = pad.w + PadBankReach;
                    if (dx * dx + dz * dz >= limit * limit) continue;
                    reachesTile = true;
                    break;
                }
                if (!reachesTile) continue;
                TerrainComp comp = Materializer.ReadyCompiler(tile);
                if (comp == null) continue;
                int moved = LayPadBanks(tile, comp, half);
                if (graded == null) PadBankTiles.Add(tileId, graded = new Dictionary<int, int>());
                graded[settlement.Id] = plan;
                if (moved == 0) continue;
                Materializer.SaveTerrainTile(comp, false);
                Plugin.Log.LogInfo(settlement.Name + ": graded " + moved + " ground points between pads at " + tileCentre.ToString("0"));
                return;
            }
        }

        internal static void InvalidatePadBanks(Heightmap tile)
        {
            if (tile != null) PadBankTiles.Remove(tile.GetInstanceID());
        }

        private static int LayPadBanks(Heightmap tile, TerrainComp comp, float half)
        {
            Vector3 centre = tile.transform.position;
            int w = tile.m_width;
            float scale = tile.m_scale;
            ReadPlayerPieces(centre, half * 1.42f + 8f);
            int moved = 0;
            float water = ZoneSystem.c_WaterLevel + 0.3f;
            for (int z = 0; z <= w; z++)
            {
                for (int x = 0; x <= w; x++)
                {
                    Vector3 p = new Vector3(centre.x + x * scale - half, 0f, centre.z + z * scale - half);
                    float ground = centre.y + tile.GetHeight(x, z);
                    if (ground < water) continue;
                    int i = z * (w + 1) + x;
                    bool border = x == 0 || x == w || z == 0 || z == w;
                    float basis = border ? PadBankBorderHeight(tile, p, ground) : ground;
                    float land = border ? WorldGenerator.instance.GetHeight(p.x, p.z) : ground - TerrainOffset(comp, i);
                    float target;
                    if (!TryPadBankHeight(p, basis, land, BankPads, out target)) continue;
                    if (target < water || Materializer.PlayerWorkedNear(p, 1.5f) || ClearOfPlayerPieces(p) < 1f) continue;
                    Materializer.NoteModVertex(p);
                    if (SetTerrainVertex(comp, i, ground, target)) moved++;
                }
            }
            return moved;
        }

        private static float PadBankBorderHeight(Heightmap tile, Vector3 point, float ground)
        {
            // Both tiles share this vertex. Start from the western tile (then southern at a corner),
            // so an older height difference doesn't get carried forward into the new bank.
            Vector3 first = tile.transform.position;
            foreach (Heightmap neighbour in BankTiles)
            {
                if (neighbour == null || neighbour == tile || neighbour.IsDistantLod || neighbour.m_scale != tile.m_scale) continue;
                Vector3 centre = neighbour.transform.position;
                if (centre.x > first.x || (centre.x == first.x && centre.z >= first.z)) continue;
                float half = neighbour.m_width * neighbour.m_scale * 0.5f;
                float x = point.x - centre.x + half, z = point.z - centre.z + half;
                if (x < 0f || x > 2f * half || z < 0f || z > 2f * half) continue;
                ground = centre.y + neighbour.GetHeight(Mathf.RoundToInt(x / neighbour.m_scale), Mathf.RoundToInt(z / neighbour.m_scale));
                first = centre;
            }
            return ground;
        }

        private static bool TryPadBankHeight(Vector3 p, float ground, float land, List<Vector4> pads, out float target)
        {
            target = ground;
            float low = float.NegativeInfinity, high = float.PositiveInfinity;
            float lowFloor = 0f, highFloor = 0f, lowDistance = 0f, highDistance = 0f;
            foreach (Vector4 pad in pads)
            {
                float dx = p.x - pad.x, dz = p.z - pad.z;
                float distanceSquared = dx * dx + dz * dz;
                if (distanceSquared <= pad.w * pad.w) return false;
                float reach = pad.w + PadBankReach;
                if (distanceSquared >= reach * reach) continue;
                float distance = Mathf.Sqrt(distanceSquared) - pad.w;
                // Relax the grade toward the original land. A rapidly growing fixed allowance can
                // leave a second steep bank at the outside of a pad, even on a gentle street.
                float fade = Mathf.InverseLerp(8f, PadBankReach, distance);
                fade = fade * fade * (3f - 2f * fade);
                float rise = distance * PadBankGrade;
                float lower = pad.y - rise, upper = pad.y + rise;
                lower = Mathf.Lerp(lower, Mathf.Min(lower, land), fade);
                upper = Mathf.Lerp(upper, Mathf.Max(upper, land), fade);
                if (lower > low)
                {
                    low = lower;
                    lowFloor = pad.y;
                    lowDistance = distance;
                }
                if (upper < high)
                {
                    high = upper;
                    highFloor = pad.y;
                    highDistance = distance;
                }
            }
            // If two pads are too far apart in height for a gentle slope, distribute the rise across
            // their gap. The nearer floor has more influence, all the way up to its own edge.
            if (float.IsNegativeInfinity(low)) return false;
            target = low > high
                ? Mathf.Lerp(lowFloor, highFloor, lowDistance / (lowDistance + highDistance))
                : Mathf.Clamp(ground, low, high);
            return true;
        }

        private static void ReadPlayerPieces(Vector3 centre, float radius)
        {
            Pieces.Clear();
            BedPieces.Clear();
            Piece.GetAllPiecesInRadius(centre, radius, BedPieces);
            foreach (Piece piece in BedPieces)
            {
                if (piece == null || piece.GetCreator() == 0L) continue;
                ZNetView view = piece.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || view.GetZDO().GetInt(HashRoad) != 0 || view.GetZDO().GetInt(Materializer.HashSettlement) != 0) continue;
                Pieces.Add(piece.transform.position);
            }
        }

        private static float ClearOfPlayerPieces(Vector3 p)
        {
            float share = 1f;
            foreach (Vector3 piece in Pieces)
            {
                float off = Utils.DistanceXZ(p, piece);
                if (off < 8f) share = Mathf.Min(share, Mathf.Clamp01((off - 3f) / 5f));
            }
            return share;
        }

        internal static float TerrainOffset(TerrainComp comp, int index)
        {
            return Mathf.Clamp(TcLevel(comp)[index] + TcSmooth(comp)[index], -8f, 8f);
        }

        internal static bool SetTerrainVertex(TerrainComp comp, int index, float ground, float target)
        {
            float offset = TerrainOffset(comp, index);
            float next = Mathf.Clamp(offset + target - ground, -8f, 8f);
            if (Mathf.Abs(next - offset) < 0.02f) return false;
            TcLevel(comp)[index] = next;
            TcSmooth(comp)[index] = 0f;
            TcModified(comp)[index] = true;
            return true;
        }

        /// <summary>Prepares already loaded terrain ahead of the paving and lighting passes.</summary>
        private static void ShapeBeds(Vector3 reference)
        {
            if (Time.time < _nextBed || All.Count == 0) return;
            _nextBed = Time.time + 0.15f;
            BedCandidates.Clear();
            Heightmap.FindHeightmap(reference, BedRange, BedCandidates);
            BedCandidates.RemoveAll(tile => tile == null || tile.IsDistantLod || BedTilesDone.Contains(tile.GetInstanceID()));
            BedCandidates.Sort((a, b) => Utils.DistanceXZ(reference, a.transform.position).CompareTo(Utils.DistanceXZ(reference, b.transform.position)));
            float started = Time.realtimeSinceStartup;
            int laid = 0;
            foreach (Heightmap tile in BedCandidates)
            {
                int id = tile.GetInstanceID();
                float retry;
                if (BedRetryAt.TryGetValue(id, out retry) && Time.time < retry) continue;
                if (!ZoneSystem.instance.IsZoneLoaded(tile.transform.position)) continue;
                if (LayTile(tile)) laid++;
                if (!BedTilesDone.Contains(id)) BedRetryAt[id] = Time.time + 0.3f;
                else BedRetryAt.Remove(id);
                if (laid >= 2 || Time.realtimeSinceStartup - started > 0.004f) break;
            }
        }

        /// <summary>
        /// True near a road, where the beds are laid straight into the tiles and both sides of a tile border get
        /// the same heights: mending a seam there (toward the mean of two tiles of which one is laid and the other
        /// not yet) is what tears it.
        /// </summary>
        public static bool InBedCorridor(Vector3 p)
        {
            if (!ModConfig.Roads.Value) return false;
            bool near = false;
            foreach (Road road in All)
            {
                if (road.Near(p, BedReach) && road.Distance(p) < BedReach)
                {
                    near = true;
                    break;
                }
            }
            if (!near) return false;
            // Both tiles of the border laid: whatever still differs there is mended as anywhere else.
            return !BedReady(p, 0.5f);
        }

        /// <summary>
        /// Every terrain tile touched by a brush must have its bed before paving or furnishing the road.
        /// </summary>
        internal static bool BedReady(Vector3 p, float radius)
        {
            if (radius > 0f)
            {
                // FindHeightmap only lists loaded tiles. Near its edge a brush can also need a tile
                // that has not loaded yet, so check the four sides of the brush's bounding square.
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        if (!ZoneSystem.instance.IsZoneLoaded(p + new Vector3(x * radius, 0f, z * radius))) return false;
            }
            BedFound.Clear();
            Heightmap.FindHeightmap(p, radius, BedFound);
            bool ready = false;
            foreach (Heightmap tile in BedFound)
            {
                if (tile == null || tile.IsDistantLod) continue;
                if (!BedTilesDone.Contains(tile.GetInstanceID())) return false;
                ready = true;
            }
            return ready;
        }

        /// <summary>Tiles a road crosses that wait to be laid (for the test reports).</summary>
        public static int BedTilesWaiting
        {
            get
            {
                int n = 0;
                foreach (int id in BedWaitSince.Keys) if (!BedTilesDone.Contains(id)) n++;
                return n;
            }
        }
        /// <summary>Tiles a road crosses that could not be laid yet: since when, and when the reason was last logged.</summary>
        private static readonly Dictionary<int, float> BedWaitSince = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> BedWaitLogged = new Dictionary<int, float>();

        private static void BedWaiting(Heightmap hm, int id)
        {
            float since, logged;
            if (!BedWaitSince.TryGetValue(id, out since)) BedWaitSince[id] = since = Time.time;
            if (Time.time - since < 20f || (BedWaitLogged.TryGetValue(id, out logged) && Time.time - logged < 30f)) return;
            BedWaitLogged[id] = Time.time;
            Plugin.Log.LogWarning("road bed: tile at " + hm.transform.position.ToString("F0") + " waits " + (Time.time - since).ToString("F0")
                + " s: " + Materializer.CompilerBlock(hm));
        }

        /// <summary>
        /// Lays every road bed that crosses the tile into its heights. Returns true when it changed the tile
        /// (one tile per call: its rebuild comes first); false when there was nothing to do or it cannot be done yet.
        /// </summary>
        private static bool LayTile(Heightmap hm)
        {
            int w = hm.m_width;
            float scale = hm.m_scale, half = w * scale * 0.5f;
            Vector3 c = hm.transform.position;
            float minX = c.x - half - BedReach, maxX = c.x + half + BedReach, minZ = c.z - half - BedReach, maxZ = c.z + half + BedReach;

            // The stretches of road that come near the tile, with the bed's height at both ends.
            Segments.Clear();
            int roadsHash = BedMake;
            for (int r = 0; r < All.Count; r++)
            {
                Road road = All[r];
                if (road.MaxX < minX || road.MinX > maxX || road.MaxZ < minZ || road.MinZ > maxZ) continue;
                for (int k = 1; k < road.Points.Length; k++)
                {
                    Vector3 a = road.Points[k - 1], b = road.Points[k];
                    if (Mathf.Max(a.x, b.x) < minX || Mathf.Min(a.x, b.x) > maxX || Mathf.Max(a.z, b.z) < minZ || Mathf.Min(a.z, b.z) > maxZ) continue;
                    if (road.Bed == null) road.Bed = Bed(road);
                    Segments.Add(new BedSegment { A = a, B = b, BedA = road.Bed[k - 1], BedB = road.Bed[k], Road = r });
                    unchecked
                    {
                        roadsHash = roadsHash * 31 + a.x.GetHashCode();
                        roadsHash = roadsHash * 31 + a.z.GetHashCode();
                        roadsHash = roadsHash * 31 + b.x.GetHashCode();
                        roadsHash = roadsHash * 31 + b.z.GetHashCode();
                        roadsHash = roadsHash * 31 + road.Bed[k - 1].GetHashCode();
                        roadsHash = roadsHash * 31 + road.Bed[k].GetHashCode();
                    }
                }
            }
            int id = hm.GetInstanceID();
            // A tile no road comes near needs nothing: not even a compiler (made for nothing, it is saved with the world).
            if (Segments.Count == 0)
            {
                BedTilesDone.Add(id);
                return false;
            }
            TerrainComp comp = Materializer.ReadyCompiler(hm);
            ZNetView view = comp != null ? comp.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid())
            {
                BedWaiting(hm, id);
                return false;
            }
            // Laid before, by this make and for these roads (in this session or another, by this player or another).
            int mark = "lf_bed_plan".GetStableHashCode();
            ZDO zdo = view.GetZDO();
            if (zdo.GetInt(mark) == roadsHash)
            {
                foreach (BedSegment segment in Segments) Materializer.NoteModArea(segment.A, BedReach);
                BedTilesDone.Add(id);
                return false;
            }

            // What is not the road's to shape: the grounds of the settlements, building pads, players' bases.
            SettlementGrounds.Clear();
            Pads.Clear();
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed) continue;
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                if (Utils.DistanceXZ(centre, c) > half * 1.5f + BedReach + 120f) continue;
                SettlementGrounds.Add(new Vector3(s.Pos.X, Grounds(s), s.Pos.Z));
                foreach (Building b in s.Buildings)
                {
                    float radius = Blueprints.Radius(b.Kind);
                    if (radius <= 0f || b.State == BuildingState.Destroyed || float.IsNaN(b.GroundY)) continue;
                    Vec2 offset = new Vec2(0f, 0f);
                    float yaw;
                    if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                    Pads.Add(new Vector4(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z, radius + 1.5f));
                }
            }
            ReadPlayerPieces(c, half * 1.5f + BedReach);

            int pitch = w + 1, moved = 0;
            float water = ZoneSystem.c_WaterLevel;
            for (int y = 0; y <= w; y++)
            {
                for (int x = 0; x <= w; x++)
                {
                    Vector3 v = new Vector3(c.x + (x - w / 2) * scale, 0f, c.z + (y - w / 2) * scale);
                    // The bed's height as each road near the point has it, weighed by nearness: where two roads
                    // meet or run side by side, their beds run into each other instead of meeting in a step.
                    float cur = c.y + hm.GetHeight(x, y);
                    float sum = 0f, weights = 0f, land = float.NaN;
                    bool inBank = false;
                    int lastRoad = -1;
                    float roadBest = float.MaxValue, roadBed = 0f;
                    for (int k = 0; k <= Segments.Count; k++)
                    {
                        // Each road's nearest stretch is found first; segments of one road are contiguous in the list.
                        bool flush = k == Segments.Count || Segments[k].Road != lastRoad;
                        if (flush && lastRoad >= 0 && roadBest <= BedReach * BedReach)
                        {
                            if (float.IsNaN(land)) land = WorldGenerator.instance.GetHeight(v.x, v.z);
                            float target = BedTarget(Mathf.Sqrt(roadBest), roadBed, land);
                            inBank |= target != land || roadBest <= BedCore * BedCore;
                            float wt = 1f / (roadBest + 1f);
                            sum += target * wt;
                            weights += wt;
                        }
                        if (k == Segments.Count) break;
                        BedSegment seg = Segments[k];
                        if (flush)
                        {
                            lastRoad = seg.Road;
                            roadBest = float.MaxValue;
                        }
                        float t;
                        float d = SegmentDistanceSquared(v, seg.A, seg.B, out t);
                        if (d < roadBest)
                        {
                            roadBest = d;
                            roadBed = Mathf.Lerp(seg.BedA, seg.BedB, t);
                        }
                    }
                    if (!inBank) continue;
                    float goal = sum / weights;
                    if (goal < water + 0.3f || cur < water - 0.5f) continue;

                    // Nothing inside a settlement's grounds (its pads shape the land there), faded in over 8 m.
                    float share = 1f;
                    foreach (Vector3 g in SettlementGrounds) share = Mathf.Min(share, Mathf.Clamp01((Utils.DistanceXZ(v, g) - g.y) / 8f));
                    if (share <= 0f) continue;
                    // Nor up to a building's pad, faded in from 1 m to 7 m off its edge.
                    foreach (Vector4 pad in Pads)
                    {
                        float off = Utils.DistanceXZ(v, new Vector3(pad.x, 0f, pad.z)) - pad.w;
                        if (off < 7f) share = Mathf.Min(share, Mathf.Clamp01((off - 1f) / 6f));
                    }
                    // Leave a buffer around a player's building and around recent player ground work.
                    if (ClearOfPlayerPieces(v) < 1f || Materializer.PlayerWorkedNear(v, 1.5f)) continue;

                    float final = Mathf.Lerp(land, goal, share);
                    Materializer.NoteModVertex(v);
                    if (SetTerrainVertex(comp, y * pitch + x, cur, final)) moved++;
                }
            }
            zdo.Set(mark, roadsHash);
            BedTilesDone.Add(id);
            if (moved == 0) return false;
            Materializer.SaveTerrainTile(comp, false);
            _bedTiles++;
            _bedVertices += moved;
            Plugin.Log.LogInfo("road bed: tile at " + c.ToString("0") + " laid, " + moved + " ground points moved, " + Segments.Count + " stretches of road");
            return true;
        }

        /// <summary>The height a point this far from a road's middle gets: the bed on the road, a smooth bank back to the land beside it.</summary>
        private static float BedTarget(float distance, float bed, float land)
        {
            if (distance <= BedCore) return bed;
            float bank = Mathf.Clamp(Mathf.Abs(bed - land) * BankPerMetre, BankMin, BankMax);
            float s = (distance - BedCore) / bank;
            if (s >= 1f) return land;
            return Mathf.Lerp(bed, land, s * s * (3f - 2f * s));
        }

        private static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = dx * dx + dz * dz;
            t = len > 0f ? Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / len) : 0f;
            float px = a.x + dx * t - p.x, pz = a.z + dz * t - p.z;
            return px * px + pz * pz;
        }
    }
}
