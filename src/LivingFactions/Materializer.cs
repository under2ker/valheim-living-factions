using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Turns the simulation's settlements into real pieces and characters wherever a player is close
    /// enough for the area to be loaded. Everything it places is tagged in its ZDO so it can be
    /// recognised later; the simulation records how far each building has been placed.
    /// </summary>
    internal static class Materializer
    {
        public static readonly int HashSettlement = "lf_sid".GetStableHashCode();
        public static readonly int HashBuilding = "lf_bid".GetStableHashCode();

        private const float Range = 200f;

        private struct TerrainJob
        {
            public Vector3 Pos;
            public float Radius;
            /// <summary>When a job that cannot run yet (its ground is still loading) is given up.</summary>
            public float GiveUpAt;
            public bool LevelOnly;
            /// <summary>0 = shape the ground; otherwise only paint it (1 = dirt path, 2 = paved).</summary>
            public int Paint;
            public bool NoPaint;
        }

        private static readonly Queue<TerrainJob> Terrain = new Queue<TerrainJob>();
        private static readonly List<Heightmap> Heightmaps = new List<Heightmap>();
        private static readonly Dictionary<int, float> LastDeath = new Dictionary<int, float>();
        private static readonly HashSet<string> MissingPrefabs = new HashSet<string>();
        private static readonly AccessTools.FieldRef<TerrainComp, bool> TerrainInitialized = AccessTools.FieldRefAccess<TerrainComp, bool>("m_initialized");
        private static readonly System.Reflection.MethodInfo TerrainDoOperation = AccessTools.Method(typeof(TerrainComp), "DoOperation");

        private static readonly HashSet<int> Swept = new HashSet<int>();
        private static float _nextNpcPass;
        private static int _placedTotal;
        private static float _lastPlaced = -10f;

        /// <summary>Nothing queued for the ground and no piece placed for a moment (used by the test runner).</summary>
        public static bool Idle
        {
            get { return Terrain.Count + Flatten.Count + Paints.Count + Smooths.Count == 0 && Time.unscaledTime - _lastPlaced > 1.5f; }
        }

        public static void Reset()
        {
            Terrain.Clear();
            Paints.Clear();
            Flatten.Clear();
            Smooths.Clear();
            TerrainBusy.Clear();
            TileSeen.Clear();
            Audited.Clear();
            ReadySince.Clear();
            LastDeath.Clear();
            Roads.Reset();
            Company.Reset();
        }

        public static void NoteDeath(int settlementId)
        {
            LastDeath[settlementId] = Time.time;
        }

        public static string Describe()
        {
            return "pieces placed this session " + _placedTotal + ", terrain jobs queued " + (Terrain.Count + Flatten.Count) + "+" + Paints.Count + "+" + Smooths.Count + ", seam spots mended " + SeamsHealed + ", missing prefabs [" + string.Join(",", MissingPrefabs) + "]";
        }

        public static void Update()
        {
            // A dedicated server has no ground loaded: the players near a settlement put it up (Net.cs).
            if (!SimHost.Active || ZNetScene.instance == null || ZoneSystem.instance == null || Net.IsDedicated) return;
            DevProbes.Frame();
            ProcessTerrain();
            if (Net.KeepsRoads) HealSeams();

            Vector3 reference = ZNet.instance.GetReferencePosition();
            if (Net.KeepsRoads) Roads.Update(reference);
            Company.Update();
            int budget = ModConfig.PiecesPerFrame.Value;
            bool npcPass = Time.time >= _nextNpcPass;
            if (npcPass) _nextNpcPass = Time.time + 1f;

            List<Settlement> settlements = SimHost.Sim.State.Settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement s = settlements[i];
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                if (Utils.DistanceXZ(reference, centre) > Range || !Net.IsBuilder(s)) continue;
                if (s.Destroyed && npcPass && ZNetScene.instance.IsAreaReady(centre) && !Swept.Contains(s.Id))
                {
                    // Whatever an abandoned settlement left behind is cleared away once.
                    Swept.Add(s.Id);
                    int gone = RemoveTagged(s, centre, 0);
                    foreach (Building ruin in s.Buildings) ruin.SpawnedPieces = 0;
                    if (gone > 0) Plugin.Log.LogInfo("Cleared " + gone + " leftover objects of " + s.Name);
                }
                if (npcPass) Audit(s, centre);
                if (budget > 0) budget = SyncBuildings(s, budget);
                if (npcPass) SyncInhabitants(s, centre);
            }
        }

        // ------------------------------------------------------------------ the world and the record

        private static readonly HashSet<int> Audited = new HashSet<int>();
        private static readonly Dictionary<int, float> ReadySince = new Dictionary<int, float>();

        /// <summary>The faction state was swapped under the world (rewound, or a guest's copy went back): look again.</summary>
        public static void ForgetAudit()
        {
            Audited.Clear();
            ReadySince.Clear();
        }

        /// <summary>
        /// Once a session for each settlement: the record says which buildings stand, the world may
        /// disagree. A world backup restored without the faction state leaves buildings on record with
        /// not one piece in the world: they are put up again, their ground levelled again. Factions
        /// rewound while the world stays leave the opposite: pieces of buildings the record has not got
        /// that far with, or has not got at all. Those come down, and the building goes up again as far
        /// as the record says. A building with only some pieces missing is left alone: that is wear, a
        /// raid or the player's axe.
        /// </summary>
        private static void Audit(Settlement s, Vector3 centre)
        {
            if (Audited.Contains(s.Id) || s.Destroyed) return;
            if (!ZNetScene.instance.IsAreaReady(centre))
            {
                ReadySince.Remove(s.Id);
                return;
            }
            float since;
            if (!ReadySince.TryGetValue(s.Id, out since)) ReadySince[s.Id] = since = Time.time;
            // Pieces of a tile arrive a little after the tile itself, more so for a guest.
            if (Time.time - since < 20f) return;
            // Ground still being worked would read as uneven.
            if (Terrain.Count + Flatten.Count > 0) return;
            Audited.Add(s.Id);
            string report = Reconcile(s, centre, true);
            if (report.Length > 0) Plugin.Log.LogInfo(s.Name + ": the world and the record disagree (" + report + "); set right");
        }

        /// <summary>Compares a settlement's pieces in the world with its record; fixes them when asked. Empty when they agree.</summary>
        internal static string Reconcile(Settlement s, Vector3 centre, bool fix)
        {
            SectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, SectorObjects);
            Dictionary<int, int> pieces = new Dictionary<int, int>();
            foreach (ZDO zdo in SectorObjects)
            {
                if (zdo.GetInt(HashSettlement) != s.Id || Npcs.IsNpcPrefab(zdo.GetPrefab())) continue;
                int id = zdo.GetInt(HashBuilding);
                if (id == 0) continue;
                int n;
                pieces.TryGetValue(id, out n);
                pieces[id] = n + 1;
            }
            int missing = 0, ahead = 0, unknown = 0;
            HashSet<int> onRecord = new HashSet<int>();
            foreach (Building b in s.Buildings)
            {
                onRecord.Add(b.Id);
                int n;
                pieces.TryGetValue(b.Id, out n);
                if (n > b.SpawnedPieces || (n > 0 && b.State == BuildingState.Destroyed))
                {
                    ahead++;
                    if (!fix) continue;
                    RemoveTagged(s, centre, b.Id);
                    b.SpawnedPieces = 0;
                }
                else if (n == 0 && b.SpawnedPieces > 0 && b.State != BuildingState.Destroyed && b.Plot >= 0)
                {
                    missing++;
                    if (!fix) continue;
                    b.SpawnedPieces = 0;
                    b.TerrainRev = 0;
                }
            }
            // Ground from another time (or ground work cut short by quitting) under buildings that stand.
            int uneven = 0;
            foreach (Building b in Terrain.Count + Flatten.Count > 0 ? new List<Building>() : s.Buildings)
            {
                if (b.State == BuildingState.Destroyed || b.SpawnedPieces == 0 || b.TerrainRev == 0 || PadDeviation(s, b) <= UnevenPad) continue;
                uneven++;
                if (fix) b.TerrainRev = 0;
            }
            foreach (int id in pieces.Keys)
            {
                if (onRecord.Contains(id)) continue;
                unknown++;
                if (fix) RemoveTagged(s, centre, id);
            }
            List<string> parts = new List<string>();
            if (missing > 0) parts.Add(missing + " on record but gone from the world");
            if (ahead > 0) parts.Add(ahead + " further along in the world than on record");
            if (unknown > 0) parts.Add(unknown + " in the world but not on record");
            if (uneven > 0) parts.Add(uneven + " standing on uneven ground");
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ buildings

        private static int SyncBuildings(Settlement s, int budget)
        {
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            if (faction == null) return budget;
            BuildStyle style = Blueprints.StyleFor(faction, s.Tier);

            for (int i = 0; i < s.Buildings.Count && budget > 0; i++)
            {
                Building b = s.Buildings[i];
                if (b.State == BuildingState.Destroyed && b.SpawnedPieces == 0) continue;

                Vector3 origin;
                float yaw;
                if (!TryGetOrigin(s, b, out origin, out yaw)) continue;

                if (b.State == BuildingState.Destroyed)
                {
                    RemovePieces(s, b, origin);
                    b.SpawnedPieces = 0;
                    continue;
                }
                if (b.LayoutRev < LayoutRevision)
                {
                    // Buildings laid out by an older blueprint are taken down and put up again.
                    // Revision 5 only moved the furniture of the great hall out of its doorway.
                    bool changed = b.LayoutRev < 4
                        ? b.Kind != BuildingKind.Campfire && b.Kind != BuildingKind.Farm && b.Kind != BuildingKind.Palisade && b.Kind != BuildingKind.Watchtower
                        : b.Kind == BuildingKind.GreatHall;
                    if (changed && b.SpawnedPieces > 0)
                    {
                        RemovePieces(s, b, origin);
                        b.SpawnedPieces = 0;
                    }
                    b.LayoutRev = LayoutRevision;
                }

                List<PieceSpec> blueprint = Blueprints.Get(b.Kind, style, s.Biome, b.Id + s.Seed);
                BuildingDef def = Defs.Buildings[b.Kind];
                int desired = blueprint.Count;
                if (b.State != BuildingState.Complete)
                {
                    float progress = def.Work > 0f ? Mathf.Clamp01(b.Work / def.Work) : 1f;
                    desired = Mathf.FloorToInt(blueprint.Count * progress);
                }

                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                while (b.SpawnedPieces < desired && budget > 0)
                {
                    PieceSpec spec = blueprint[b.SpawnedPieces];
                    Vector3 pos = origin + rotation * spec.Pos;
                    if (b.Kind == BuildingKind.Palisade)
                    {
                        // The wall follows the land instead of standing on a levelled pad.
                        float ground;
                        if (!ZoneSystem.instance.IsZoneLoaded(pos) || !ZoneSystem.instance.GetGroundHeight(pos, out ground)) break;
                        pos.y = ground - 0.25f;
                    }
                    Place(s, b, spec.Prefab, pos, Quaternion.Euler(0f, yaw + spec.Yaw, 0f));
                    b.SpawnedPieces++;
                    budget--;
                }
            }
            return budget;
        }

        /// <summary>World position and yaw of a building, preparing its ground the first time. False while its zone is not loaded.</summary>
        private static bool TryGetOrigin(Settlement s, Building b, out Vector3 origin, out float yaw)
        {
            Vec2 offset = new Vec2(0f, 0f);
            yaw = 0f;
            if (b.Plot >= 0) Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
            origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
            if (!ZoneSystem.instance.IsZoneLoaded(origin)) return false;

            if (float.IsNaN(b.GroundY))
            {
                float radius = Blueprints.Radius(b.Kind);
                float ground;
                if (!PadHeight(origin, radius, out ground)) return false;
                b.GroundY = ground;
                b.TerrainRev = TerrainRevision;
                if (radius > 0f)
                {
                    Vector3 pad = new Vector3(origin.x, ground, origin.z);
                    EnqueueTerrain(new TerrainJob { Pos = pad, Radius = radius + PadMargin });
                    RelevelNeighbours(s, b, origin);
                    EnqueueTerrain(new TerrainJob { Pos = pad, Radius = radius + PadMargin, LevelOnly = true });
                    PaintPath(s, b, pad);
                    // The heart of the settlement gets an open green; buildings a margin to breathe.
                    ClearVegetation(pad, b.Kind == BuildingKind.Campfire ? 11f : radius + 2.5f);
                }
            }
            origin.y = b.GroundY;
            if (b.TerrainRev < TerrainRevision)
            {
                // Pads from older versions (or dug up since) are flattened again at their own height.
                b.TerrainRev = TerrainRevision;
                float radius = Blueprints.Radius(b.Kind);
                if (radius > 0f)
                {
                    // Older versions stacked ground changes and left pads sunk, raised or torn: shape and flatten again.
                    EnqueueTerrain(new TerrainJob { Pos = origin, Radius = radius + PadMargin });
                    // Shaping this pad again tugs at the ones beside it, as when it was first put down.
                    RelevelNeighbours(s, b, origin);
                    EnqueueTerrain(new TerrainJob { Pos = origin, Radius = radius + PadMargin, LevelOnly = true });
                    PaintPath(s, b, origin);
                }
            }
            return true;
        }

        private const int TerrainRevision = 8;
        private const int LayoutRevision = 5;
        private const float PadMargin = 1.5f;

        /// <summary>
        /// The path a building gets when it goes up. Places everyone uses (and about half of the
        /// rest) are joined to the square, paved once the settlement is a town; the others only to
        /// the nearest building that was there before them, so the paths read as house-to-house
        /// habits and not as spokes of a wheel.
        /// </summary>
        private static void PaintPath(Settlement s, Building b, Vector3 pad)
        {
            Vector3 from, to;
            int paint;
            float width;
            if (PathOf(s, b, pad, out from, out to, out paint, out width)) QueueRoad(from, to, paint, width);
        }

        private static readonly Dictionary<int, float> PathsCleared = new Dictionary<int, float>();

        /// <summary>
        /// The paths inside a settlement are kept free of trees, rocks and bushes, as people who walk
        /// them every day would keep them: without this a village in a wood has its paths under the
        /// canopy where nobody sees them. Done for each settlement near the player.
        /// </summary>
        public static void ClearPaths(Vector3 reference)
        {
            if (!ModConfig.Roads.Value || !SimHost.Active) return;
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed) continue;
                Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
                if (Utils.DistanceXZ(reference, centre) > 70f || !ZoneSystem.instance.IsZoneLoaded(centre)) continue;
                // Again every so often: growth of a tile turns up some time after the tile itself (later still
                // for a guest, who hears of it from the host), and a single pass would miss it.
                float due;
                if (PathsCleared.TryGetValue(s.Id, out due) && Time.time < due) continue;
                PathsCleared[s.Id] = Time.time + 20f;
                foreach (Building b in s.Buildings)
                {
                    if (b.SpawnedPieces == 0 || float.IsNaN(b.GroundY) || b.State == BuildingState.Destroyed) continue;
                    Vector3 origin, from, to;
                    float yaw, width;
                    int paint;
                    if (!TryGetOrigin(s, b, out origin, out yaw)) continue;
                    origin.y = b.GroundY;
                    if (FactionMember.IsHome(b.Kind) && b.State == BuildingState.Complete)
                    {
                        // The way in: a sapling that came up on the doorstep keeps everyone out of the house.
                        Vector3 inside, door;
                        FactionMember.DoorPoints(s, b, 0f, out inside, out door);
                        // The whole way in, from the doorstep to the spot inside, not only the step in front.
                        if (ZoneSystem.instance.IsZoneLoaded(door))
                        {
                            for (int k = 0; k <= 3; k++) ClearVegetation(Vector3.Lerp(door, inside, k / 3f), k == 0 ? 2.2f : 1.4f);
                        }
                    }
                    if (!PathOf(s, b, origin, out from, out to, out paint, out width)) continue;
                    float length = Utils.DistanceXZ(from, to);
                    for (float d = 0f; d <= length; d += 2f)
                    {
                        Vector3 p = Vector3.Lerp(from, to, length > 0f ? d / length : 0f);
                        if (!ZoneSystem.instance.IsZoneLoaded(p)) continue;
                        float ground;
                        if (ZoneSystem.instance.GetGroundHeight(p, out ground)) p.y = ground;
                        ClearVegetation(p, 3f);
                    }
                }
            }
        }

        /// <summary>Where the path of a building runs: to the square, or to the nearest older building.</summary>
        private static bool PathOf(Settlement s, Building b, Vector3 pad, out Vector3 from, out Vector3 to, out int paint, out float width)
        {
            from = to = Vector3.zero;
            paint = 1;
            width = 1f;
            if (!ModConfig.Roads.Value || b.Plot < 0) return false;
            Vector3 centre = new Vector3(s.Pos.X, pad.y, s.Pos.Z);
            bool everyone = b.Kind == BuildingKind.Storehouse || b.Kind == BuildingKind.Market || b.Kind == BuildingKind.GreatHall || b.Kind == BuildingKind.Smithy
                || b.Kind == BuildingKind.Longhouse || b.Kind == BuildingKind.Watchtower;
            Vector3 neighbour = Vector3.zero;
            float nearest = float.MaxValue, neighbourRadius = 0f;
            foreach (Building other in s.Buildings)
            {
                if (other == b || other.Id >= b.Id || other.Plot < 0 || other.State == BuildingState.Destroyed) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(other.Plot, s.Seed, out offset, out yaw);
                Vector3 there = new Vector3(s.Pos.X + offset.X, pad.y, s.Pos.Z + offset.Z);
                float d = Utils.DistanceXZ(there, pad);
                if (d < nearest)
                {
                    nearest = d;
                    neighbour = there;
                    neighbourRadius = Blueprints.Radius(other.Kind);
                }
            }
            if (everyone || nearest > 40f || ((b.Id + s.Seed) & 1) == 0)
            {
                from = centre + (pad - centre).normalized * 4f;
                to = pad + (centre - pad).normalized * (Blueprints.Radius(b.Kind) - 0.5f);
                paint = s.Tier >= Tier.Town ? 2 : 1;
                width = 1.3f;
                return true;
            }
            Vector3 dir = (neighbour - pad).normalized;
            from = pad + dir * (Blueprints.Radius(b.Kind) - 0.5f);
            to = neighbour - dir * (neighbourRadius - 0.5f);
            return true;
        }

        private static readonly List<TerrainJob> Smooths = new List<TerrainJob>();

        /// <summary>
        /// Ground that has only just loaded cannot be worked for a few seconds. Jobs wait for it by
        /// the clock: counting attempts instead gave up after a second or two on a fast machine,
        /// which is how freshly reached stretches of road were left unpainted.
        /// </summary>
        private static bool StillWaiting(ref TerrainJob job)
        {
            if (job.GiveUpAt <= 0f) job.GiveUpAt = Time.time + 180f;
            return Time.time < job.GiveUpAt;
        }

        // ------------------------------------------------------------------ torn ground

        private static readonly List<Heightmap> SeamTiles = new List<Heightmap>(), SeamAt = new List<Heightmap>();
        private static readonly Dictionary<long, float> SeamTried = new Dictionary<long, float>();
        private static float _nextSeamCheck;
        public static int SeamsHealed;

        /// <summary>
        /// The game shapes each terrain tile from that tile's own heights, so smoothing that reaches
        /// across the border of two tiles leaves them disagreeing along it: a visible tear. Once the
        /// ground work around the player is finished, every vertex where two tiles differ is levelled
        /// to the mean of the two on both tiles at once, which closes the tear.
        /// </summary>
        private static void HealSeams()
        {
            if (Time.time < _nextSeamCheck) return;
            _nextSeamCheck = Time.time + 6f;
            if (Terrain.Count + Flatten.Count + Smooths.Count > 0 || Player.m_localPlayer == null) return;
            SeamTiles.Clear();
            Heightmap.FindHeightmap(Player.m_localPlayer.transform.position, 140f, SeamTiles);
            int queued = 0;
            foreach (Heightmap hm in SeamTiles)
            {

                int w = hm.m_width;
                float half = w * hm.m_scale * 0.5f;
                Vector3 origin = hm.transform.position;
                for (int side = 0; side < 2; side++)
                {
                    // Every vertex is looked at: a tear can be a single one.
                    for (int k = 0; k <= w && queued < 80; k++)
                    {
                        Vector3 p = side == 0 ? new Vector3(origin.x + half, 0f, origin.z - half + k * hm.m_scale) : new Vector3(origin.x - half + k * hm.m_scale, 0f, origin.z + half);
                        float mine = origin.y + hm.GetHeight(side == 0 ? w : k, side == 0 ? k : w);
                        SeamAt.Clear();
                        Heightmap.FindHeightmap(p, 0.2f, SeamAt);
                        foreach (Heightmap other in SeamAt)
                        {
                            if (other == hm || other.m_width != w) continue;
                            Vector3 o = other.transform.position;
                            int gx = Mathf.RoundToInt((p.x - o.x + half) / other.m_scale), gz = Mathf.RoundToInt((p.z - o.z + half) / other.m_scale);
                            if (gx < 0 || gz < 0 || gx > w || gz > w) continue;
                            float theirs = o.y + other.GetHeight(gx, gz);
                            if (Mathf.Abs(mine - theirs) < 0.06f) continue;
                            long key = ((long)Mathf.RoundToInt(p.x) << 32) ^ (uint)Mathf.RoundToInt(p.z);
                            float tried;
                            // A spot that would not close is left alone for a while rather than worked on forever.
                            if (SeamTried.TryGetValue(key, out tried) && Time.time - tried < 90f) continue;
                            SeamTried[key] = Time.time;
                            float mean = (mine + theirs) * 0.5f;
                            if (mean < ZoneSystem.c_WaterLevel - 1f) continue;
                            EnqueueTerrain(new TerrainJob { Pos = new Vector3(p.x, mean, p.z), Radius = 1.3f, LevelOnly = true, NoPaint = true });
                            queued++;
                            break;
                        }
                    }
                }
            }
            SeamsHealed += queued;
        }

        /// <summary>Evens out the ground at one spot of a road bed, without levelling it to a set height.</summary>
        public static void QueueSmooth(Vector3 pos, float radius)
        {
            Smooths.Add(new TerrainJob { Pos = pos, Radius = radius });
        }

        /// <summary>One spot of path: 1 = beaten earth, 2 = cobbles.</summary>
        public static void QueuePaint(Vector3 pos, float radius, int paint)
        {
            Paints.Enqueue(new TerrainJob { Pos = pos, Radius = radius, Paint = paint });
        }

        public static void QueueRoad(Vector3 from, Vector3 to, int paint, float width)
        {
            float length = Utils.DistanceXZ(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / 1.4f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                Paints.Enqueue(new TerrainJob { Pos = p, Radius = width, Paint = paint });
            }
        }

        /// <summary>The height a pad is levelled to: the average of its footprint, so as much is cut as is filled.</summary>
        private static bool PadHeight(Vector3 origin, float radius, out float height)
        {
            height = 0f;
            float centre;
            if (!ZoneSystem.instance.GetGroundHeight(origin, out centre)) return false;
            float sum = centre * 2f;
            float weight = 2f;
            for (int i = 0; i < 8 && radius > 0f; i++)
            {
                float a = i * Mathf.PI / 4f;
                float h;
                Vector3 p = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.75f;
                if (!ZoneSystem.instance.GetGroundHeight(p, out h)) continue;
                sum += h;
                weight += 1f;
            }
            height = Mathf.Max(sum / weight, ZoneSystem.c_WaterLevel + 0.6f);
            return true;
        }

        /// <summary>Blending a new pad into the land can tug at the pads next to it; flatten those again afterwards.</summary>
        private static void RelevelNeighbours(Settlement s, Building added, Vector3 origin)
        {
            foreach (Building other in s.Buildings)
            {
                if (other == added || float.IsNaN(other.GroundY) || other.State == BuildingState.Destroyed) continue;
                float radius = Blueprints.Radius(other.Kind);
                if (radius <= 0f) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (other.Plot >= 0) Defs.PlotTransform(other.Plot, s.Seed, out offset, out yaw);
                Vector3 pos = new Vector3(s.Pos.X + offset.X, other.GroundY, s.Pos.Z + offset.Z);
                if (Utils.DistanceXZ(pos, origin) < Blueprints.Radius(added.Kind) + radius + PadMargin * 2f + 4f) EnqueueTerrain(new TerrainJob { Pos = pos, Radius = radius + PadMargin, LevelOnly = true });
            }
        }

        private static void Place(Settlement s, Building b, string prefabName, Vector3 pos, Quaternion rot)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                if (MissingPrefabs.Add(prefabName)) Plugin.Log.LogWarning("Blueprint prefab not found: " + prefabName);
                return;
            }
            GameObject go = Object.Instantiate(prefab, pos, rot);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                view.GetZDO().Set(HashSettlement, s.Id);
                view.GetZDO().Set(HashBuilding, b.Id);
            }
            _placedTotal++;
            _lastPlaced = Time.unscaledTime;
        }

        /// <summary>
        /// Takes a building's pieces out of the world. They are found by their tags in the object
        /// database, not by physics, so pieces without colliders and unloaded ones go too.
        /// </summary>
        private static void RemovePieces(Settlement s, Building b, Vector3 origin)
        {
            RemoveTagged(s, origin, b.Id);
        }

        /// <summary>More than this off level under a building, and the pad is levelled again.</summary>
        private const float UnevenPad = 0.4f;

        /// <summary>How far the ground under a building is from its pad height (0 when it cannot tell).</summary>
        internal static float PadDeviation(Settlement s, Building b)
        {
            float radius = Blueprints.Radius(b.Kind);
            if (float.IsNaN(b.GroundY) || radius <= 0f) return 0f;
            Vec2 offset = new Vec2(0f, 0f);
            float yaw;
            if (b.Plot >= 0) Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
            Vector3 origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
            float worst = 0f;
            for (int i = 0; i < 9; i++)
            {
                float ang = i * Mathf.PI / 4f, r = i == 8 ? 0f : radius * 0.8f, h;
                if (!ZoneSystem.instance.GetGroundHeight(origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r, out h)) continue;
                worst = Mathf.Max(worst, Mathf.Abs(h - b.GroundY));
            }
            return worst;
        }

        /// <summary>Everything a settlement put in the world, its people too: for one the record no longer has.</summary>
        internal static int SweepAway(Settlement s)
        {
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            SectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, SectorObjects);
            int removed = 0;
            foreach (ZDO zdo in SectorObjects.ToArray())
            {
                if (zdo.GetInt(HashSettlement) != s.Id) continue;
                RemoveNpc(zdo);
                removed++;
            }
            return removed;
        }

        /// <summary>Removes every non-character object tagged for the settlement (buildingId 0 = all buildings).</summary>
        private static int RemoveTagged(Settlement s, Vector3 around, int buildingId)
        {
            SectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(around), SimulationDistance.OriginalDistance, SectorObjects);
            int removed = 0;
            foreach (ZDO zdo in SectorObjects.ToArray())
            {
                if (zdo.GetInt(HashSettlement) != s.Id || Npcs.IsNpcPrefab(zdo.GetPrefab())) continue;
                if (buildingId != 0 && zdo.GetInt(HashBuilding) != buildingId) continue;
                RemoveNpc(zdo);
                removed++;
            }
            return removed;
        }

        /// <summary>Trees, rocks and bushes on a building site are cleared, as settlers would.</summary>
        internal static void ClearVegetation(Vector3 pad, float radius)
        {
            HashSet<ZNetView> found = new HashSet<ZNetView>();
            foreach (Collider collider in Physics.OverlapSphere(pad, radius))
            {
                ZNetView view = collider.GetComponentInParent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                GameObject root = view.gameObject;
                if (root.GetComponent<Piece>() != null || root.GetComponent<Character>() != null) continue;
                if (view.GetZDO().GetInt(HashSettlement) != 0) continue;
                bool vegetation = root.GetComponent<TreeBase>() != null || root.GetComponent<TreeLog>() != null || root.GetComponent<Destructible>() != null
                    || root.GetComponent<MineRock>() != null || root.GetComponent<MineRock5>() != null || root.GetComponent<Pickable>() != null;
                if (vegetation) found.Add(view);
            }
            foreach (ZNetView view in found)
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
            }
        }

        private static readonly Queue<TerrainJob> Paints = new Queue<TerrainJob>();
        private static readonly Queue<TerrainJob> Flatten = new Queue<TerrainJob>();

        private static void EnqueueTerrain(TerrainJob job)
        {
            if (job.LevelOnly) Flatten.Enqueue(job);
            else Terrain.Enqueue(job);
        }

        private static readonly Dictionary<TerrainComp, float> TerrainBusy = new Dictionary<TerrainComp, float>();

        /// <summary>Seconds a zone's terrain is left alone after a change, so its heightmap is rebuilt first.</summary>
        private const float TerrainSettle = 1.5f;

        private static readonly List<ZDO> CompilerScan = new List<ZDO>();

        private static bool SavedCompilerExists(Heightmap hm)
        {
            if (hm.m_terrainCompilerPrefab == null) return false;
            int prefab = hm.m_terrainCompilerPrefab.name.GetStableHashCode();
            Vector3 centre = hm.transform.position;
            float half = hm.m_width * hm.m_scale * 0.5f;
            CompilerScan.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, CompilerScan);
            foreach (ZDO zdo in CompilerScan)
            {
                if (zdo.GetPrefab() != prefab) continue;
                Vector3 p = zdo.GetPosition();
                if (Mathf.Abs(p.x - centre.x) <= half && Mathf.Abs(p.z - centre.z) <= half) return true;
            }
            return false;
        }

        private static readonly Dictionary<int, float> TileSeen = new Dictionary<int, float>();

        private static bool Compilers(Vector3 pos, float radius, List<TerrainComp> compilers)
        {
            Heightmaps.Clear();
            compilers.Clear();
            Heightmap.FindHeightmap(pos, radius, Heightmaps);
            if (Heightmaps.Count == 0) return false;
            foreach (Heightmap hm in Heightmaps)
            {
                TerrainComp comp = TerrainComp.FindTerrainCompiler(hm.transform.position);
                if (comp == null)
                {
                    // A tile that was ever dug or painted has a compiler saved with the world, and it appears a
                    // little after the tile itself. Making a new one meanwhile leaves two; on the next load the
                    // game keeps the one with more operations and throws the other away with all its work.
                    if (!ZNetScene.instance.IsAreaReady(hm.transform.position) || SavedCompilerExists(hm)) return false;
                    if (!Net.IsHost)
                    {
                        // A guest hears of the tile's saved compiler from the host, some seconds after the tile
                        // itself is there. Until that time has passed, nobody can tell "none" from "not here yet".
                        int tile = hm.GetInstanceID();
                        float seen;
                        if (!TileSeen.TryGetValue(tile, out seen))
                        {
                            TileSeen[tile] = Time.time;
                            return false;
                        }
                        if (Time.time - seen < 25f) return false;
                    }
                    comp = hm.GetAndCreateTerrainCompiler();
                }
                // The compiler initialises a frame or two after it is created.
                if (comp == null || !TerrainInitialized(comp) || !comp.IsOwner()) return false;
                compilers.Add(comp);
            }
            return true;
        }

        /// <summary>
        /// Applies queued ground work. The game's level and smooth operations are relative to the
        /// heightmap as it is currently built, and that is only rebuilt some frames after a change:
        /// two operations on the same zone in quick succession are added on top of each other, which
        /// lifts or sinks the ground past its target and tears it along zone borders. So height jobs
        /// run strictly in order, and never on a zone whose last change has not settled.
        /// </summary>
        private static void ProcessTerrain()
        {
            List<TerrainComp> compilers = new List<TerrainComp>();

            for (int n = Mathf.Min(Paints.Count, 8); n > 0; n--)
            {
                TerrainJob job = Paints.Dequeue();
                float ground;
                if (!Compilers(job.Pos, job.Radius + 1f, compilers))
                {
                    if (StillWaiting(ref job)) Paints.Enqueue(job);
                    continue;
                }
                Vector3 spot = job.Pos;
                if (!ZoneSystem.instance.GetGroundHeight(spot, out ground) || ground < ZoneSystem.c_WaterLevel + 0.2f) continue;
                spot.y = ground;
                TerrainOp.Settings road = new TerrainOp.Settings
                {
                    m_level = false,
                    m_smooth = false,
                    m_paintCleared = true,
                    m_paintType = job.Paint == 2 ? TerrainModifier.PaintType.Paved : TerrainModifier.PaintType.Dirt,
                    m_paintRadius = job.Radius
                };
                foreach (TerrainComp comp in compilers) TerrainDoOperation.Invoke(comp, new object[] { spot, Vector3.zero, road });
            }

            // Road beds only smooth, so they do not wait in line behind the building pads; but, like every
            // height change, never on a zone whose last change has not been rebuilt yet.
            int smoothed = 0;
            // At most one bed a frame, and only a handful looked at: a job that cannot run yet goes to the back of
            // the line, so the wait costs next to nothing however long the line is.
            for (int looked = 0; looked < 6 && Smooths.Count > 0 && smoothed < 1; looked++)
            {
                TerrainJob bed = Smooths[0];
                Smooths.RemoveAt(0);
                if (!Compilers(bed.Pos, bed.Radius + 1f, compilers))
                {
                    if (StillWaiting(ref bed)) Smooths.Add(bed);
                    continue;
                }
                bool settling = false;
                foreach (TerrainComp comp in compilers)
                {
                    float until;
                    if (TerrainBusy.TryGetValue(comp, out until) && Time.time < until) settling = true;
                }
                if (settling)
                {
                    Smooths.Add(bed);
                    continue;
                }
                float bedGround;
                Vector3 bedSpot = bed.Pos;
                if (!ZoneSystem.instance.GetGroundHeight(bedSpot, out bedGround) || bedGround < ZoneSystem.c_WaterLevel + 0.2f) continue;
                bedSpot.y = bedGround;
                TerrainOp.Settings even = new TerrainOp.Settings
                {
                    m_level = false,
                    m_smooth = true,
                    m_smoothRadius = bed.Radius,
                    m_smoothPower = 3f,
                    m_paintCleared = false
                };
                foreach (TerrainComp comp in compilers)
                {
                    TerrainDoOperation.Invoke(comp, new object[] { bedSpot, Vector3.zero, even });
                    TerrainBusy[comp] = Time.time + TerrainSettle;
                }
                smoothed++;
            }

            // Shaping (level + blend) always goes first; the final flattening of every pad comes after all of it,
            // because the blend around one pad reaches onto the pads next to it.
            Queue<TerrainJob> queue = Terrain.Count > 0 ? Terrain : Flatten;
            for (int n = 0; n < 4 && queue.Count > 0; n++)
            {
                TerrainJob job = queue.Peek();
                if (!Compilers(job.Pos, job.Radius + 5f, compilers))
                {
                    queue.Dequeue();
                    if (StillWaiting(ref job)) queue.Enqueue(job);
                    return;
                }
                foreach (TerrainComp comp in compilers)
                {
                    float until;
                    if (TerrainBusy.TryGetValue(comp, out until) && Time.time < until) return;
                }
                queue.Dequeue();
                TerrainOp.Settings settings = new TerrainOp.Settings
                {
                    m_level = true,
                    m_levelRadius = job.Radius,
                    m_square = false,
                    m_smooth = !job.LevelOnly,
                    m_smoothRadius = job.Radius + 4f,
                    m_smoothPower = 3f,
                    m_paintCleared = !job.NoPaint,
                    m_paintType = TerrainModifier.PaintType.Dirt,
                    m_paintRadius = Mathf.Max(1f, job.Radius - 2f)
                };
                foreach (TerrainComp comp in compilers)
                {
                    TerrainDoOperation.Invoke(comp, new object[] { job.Pos, Vector3.zero, settings });
                    TerrainBusy[comp] = Time.time + TerrainSettle;
                }
            }
        }

        // ------------------------------------------------------------------ inhabitants

        private static readonly List<ZDO> SectorObjects = new List<ZDO>();

        /// <summary>
        /// Every inhabitant of the settlement that exists in the world, loaded or not, found by
        /// its tags in the object database. (Object ids do not survive a reload, so no list of
        /// ids is kept; the world itself is the record.)
        /// </summary>
        private static List<ZDO> FindInhabitants(Settlement s, Vector3 centre)
        {
            SectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, SectorObjects);
            List<ZDO> found = new List<ZDO>();
            foreach (ZDO zdo in SectorObjects)
            {
                if (zdo.GetInt(HashSettlement) != s.Id || !Npcs.IsNpcPrefab(zdo.GetPrefab())) continue;
                if (Npcs.IsTemporary((NpcRole)zdo.GetInt(FactionMember.HashRole))) continue;
                found.Add(zdo);
            }
            return found;
        }

        private static void SyncInhabitants(Settlement s, Vector3 centre)
        {
            if (!ZNetScene.instance.IsAreaReady(centre)) return;
            s.NpcRefs.Clear();

            int desired = 0, guards = 0;
            if (!s.Destroyed && s.Population >= 1f)
            {
                desired = Mathf.Clamp(Mathf.RoundToInt(s.Population), 1, ModConfig.MaxVisibleNpcs.Value);
                guards = Mathf.Min(Mathf.RoundToInt(s.Soldiers), Mathf.Max(0, (desired - 1) * 2 / 3));
                // A settlement is never left without a watch.
                if (desired >= 3) guards = Mathf.Max(guards, 1);
            }
            int villagers = Mathf.Max(0, desired - 1 - guards);

            int haveChief = 0, haveGuards = 0, haveVillagers = 0;
            foreach (ZDO zdo in FindInhabitants(s, centre))
            {
                NpcRole role = (NpcRole)zdo.GetInt(FactionMember.HashRole);
                bool wrongFaction = zdo.GetInt(FactionMember.HashFaction) != s.FactionId;
                bool keep = !wrongFaction && ((role == NpcRole.Chief && haveChief < (desired > 0 ? 1 : 0))
                    || (role == NpcRole.Guard && haveGuards < guards)
                    || (role == NpcRole.Villager && haveVillagers < villagers));
                if (!keep)
                {
                    RemoveNpc(zdo);
                    continue;
                }
                if (role == NpcRole.Chief) haveChief++;
                else if (role == NpcRole.Guard) haveGuards++;
                else haveVillagers++;
            }

            if (desired == 0) return;
            // After a death the settlement does not instantly replace the fallen.
            float died;
            if (LastDeath.TryGetValue(s.Id, out died) && Time.time - died < 120f) return;

            int spawned = 0;
            while (spawned < 3)
            {
                NpcRole role;
                if (haveChief < 1) { role = NpcRole.Chief; haveChief++; }
                else if (haveGuards < guards) { role = NpcRole.Guard; haveGuards++; }
                else if (haveVillagers < villagers) { role = NpcRole.Villager; haveVillagers++; }
                else break;
                if (!SpawnNpc(s, centre, role)) break;
                spawned++;
            }
        }

        private static bool SpawnNpc(Settlement s, Vector3 centre, NpcRole role)
        {
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            GameObject prefab = Npcs.PrefabFor(faction);
            if (prefab == null) return false;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = role == NpcRole.Chief ? Random.Range(3f, 5f) : Random.Range(4f, 8f);
            Vector3 pos = centre + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(pos, out ground)) return false;
            pos.y = ground + 0.3f;

            GameObject go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            FactionMember member = go.GetComponent<FactionMember>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (member == null || view == null || view.GetZDO() == null)
            {
                Object.Destroy(go);
                return false;
            }
            member.Setup(s.FactionId, s.Id, role, Random.Range(1, int.MaxValue));
            return true;
        }

        private static void RemoveNpc(ZDO zdo)
        {
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
        }
    }
}
