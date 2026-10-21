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
            /// <summary>A road bed or a ramp: levelled to Pos.y (and smoothed round it when Smooth), in the line of the road beds.</summary>
            public bool Grade, Smooth;
            /// <summary>
            /// With Grade: no levelling, the ground round the spot only drawn toward Pos.y, fully at the middle and
            /// less and less out to Radius (the game's smoothing). Run along a road bed after its levelling, a
            /// spot every metre, it takes the steps out from between the levelled discs.
            /// </summary>
            public bool Blend;
            /// <summary>Mending a tear between two tiles (HealSeams): not counted as ground the mod shaped.</summary>
            public bool Seam;
            /// <summary>How far off the land a grade may go (0 = as far as a pad); a ramp between two pads cut into a slope needs more.</summary>
            public float Drift;
            /// <summary>When the job was queued: a levelling that has waited long goes first, whatever the pads.</summary>
            public float Queued;
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

        /// <summary>
        /// Nothing queued for the ground and no piece placed for a moment (used by the test runner). A job whose
        /// ground is not loaded at all (a road's paint beyond the loaded land) is not waited for: it cannot run
        /// before the player goes there, and was waited for for three minutes.
        /// </summary>
        public static bool Idle
        {
            get
            {
                if (Roads.Unfurnished != 0 || Time.unscaledTime - _lastPlaced <= 1.5f) return false;
                return Pending(Terrain) + Pending(Flatten) + Pending(Paints) + Pending(Smooths) == 0;
            }
        }

        private static int Pending(IEnumerable<TerrainJob> jobs)
        {
            int n = 0;
            foreach (TerrainJob job in jobs)
            {
                if (job.GiveUpAt > 0f && !ZoneSystem.instance.IsZoneLoaded(job.Pos)) continue;
                n++;
            }
            return n;
        }

        public static void Reset()
        {
            Terrain.Clear();
            Paints.Clear();
            Flatten.Clear();
            Smooths.Clear();
            RampNotes.Clear();
            StreetsEased.Clear();
            TerrainBusy.Clear();
            TerrainBusyAt.Clear();
            ModWorked.Clear();
            PlayerWorked.Clear();
            TileSeen.Clear();
            Audited.Clear();
            ReadySince.Clear();
            PeakPieces.Clear();
            WrecksChecked.Clear();
            LastDeath.Clear();
            Roads.Reset();
            Company.Reset();
            EnvoyPost.Reset();
            PadsLooked.Clear();
            PadRetries.Clear();
        }

        public static void NoteDeath(int settlementId)
        {
            LastDeath[settlementId] = Time.time;
        }

        public static string Describe()
        {
            string head = "pieces placed this session " + _placedTotal + ", terrain jobs queued " + (Terrain.Count + Flatten.Count) + "+" + Paints.Count + "+" + Smooths.Count
                + ", seam spots mended " + SeamsHealed + ", missing prefabs [" + string.Join(",", MissingPrefabs) + "]";
            return head + "\nramps: paths with a ramp " + _ramps + ", paths steep " + _pathsSteep + ", paths left flat " + _pathsFlat + ", paths not loaded yet " + _pathsUnloaded
                + ", ramps not fitting " + _rampsSkipped + "; grades done " + _gradesDone + ", refused " + _gradesRefused + ", dropped after waiting " + _gradeDrops;
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
                if (npcPass) CheckWrecks(s, centre);
                if (budget > 0) budget = SyncBuildings(s, budget);
                if (npcPass && !s.Destroyed) SettlePads(s);
                if (npcPass && !s.Destroyed)
                {
                    Faction owner = SimHost.Sim.State.GetFaction(s.FactionId);
                    if (owner != null) EnvoyPost.Furnish(s, owner);
                }
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
            PeakPieces.Clear();
            WrecksChecked.Clear();
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

        private static readonly Dictionary<int, float> WrecksChecked = new Dictionary<int, float>();
        private static readonly Dictionary<long, int> PeakPieces = new Dictionary<long, int>();

        /// <summary>
        /// While a settlement is near, its buildings are counted every few seconds: one that has lost
        /// more than half of its pieces (to the player's axe, to monsters, in a fight) is a ruin, and the
        /// host is told, so its loss counts for everyone: the settlement is weaker for it and builds it
        /// again in time at its own cost, instead of it standing on record whole, or being put up again
        /// for nothing as if a backup had been restored. Fields and the fire are left out: crops come and go.
        /// </summary>
        private static void CheckWrecks(Settlement s, Vector3 centre)
        {
            if (s.Destroyed || !Audited.Contains(s.Id) || !ZNetScene.instance.IsAreaReady(centre)) return;
            float due;
            if (WrecksChecked.TryGetValue(s.Id, out due) && Time.time < due) return;
            WrecksChecked[s.Id] = Time.time + 6f;
            Dictionary<int, int> pieces = CountPieces(s, centre);
            foreach (Building b in s.Buildings)
            {
                if (b.State != BuildingState.Complete || b.Kind == BuildingKind.Campfire || b.Kind == BuildingKind.Farm || b.Plot < 0 && b.Kind != BuildingKind.Palisade) continue;
                long key = ((long)s.Id << 32) | (uint)b.Id;
                int n, peak;
                pieces.TryGetValue(b.Id, out n);
                // Measured against the most pieces seen standing, not the blueprint: a piece whose prefab is
                // missing (another mod took it out) was never there to lose.
                PeakPieces.TryGetValue(key, out peak);
                if (b.SpawnedPieces < peak) peak = 0;
                PeakPieces[key] = peak = Mathf.Max(peak, n);
                if (peak < 6 || b.LayoutRev < LayoutRevision || n * 2 >= peak) continue;
                PeakPieces.Remove(key);
                Plugin.Log.LogInfo(s.Name + ": " + b.Kind + " #" + b.Id + " has " + n + " of " + peak + " pieces left; a ruin now");
                Net.BuildingWrecked(s, b.Id);
            }
        }

        private static Dictionary<int, int> CountPieces(Settlement s, Vector3 centre)
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
            return pieces;
        }

        /// <summary>Compares a settlement's pieces in the world with its record; fixes them when asked. Empty when they agree.</summary>
        internal static string Reconcile(Settlement s, Vector3 centre, bool fix)
        {
            Dictionary<int, int> pieces = CountPieces(s, centre);
            int missing = 0, ahead = 0, unknown = 0;
            HashSet<int> onRecord = new HashSet<int> { EnvoyPost.BuildingId };
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
            // A pad far off the land is a pit dug by mistake (or a mound): the building comes down, the
            // ground is levelled back to the land and it goes up again there.
            int pits = 0;
            foreach (Building b in s.Buildings)
            {
                float radius = Blueprints.Radius(b.Kind);
                if (b.State == BuildingState.Destroyed || float.IsNaN(b.GroundY) || radius <= 0f) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
                if (Mathf.Abs(b.GroundY - NaturalHeight(origin, radius)) <= MaxPadDrift + 0.5f) continue;
                pits++;
                if (!fix) continue;
                if (b.SpawnedPieces > 0) RemoveTagged(s, centre, b.Id);
                b.SpawnedPieces = 0;
                b.GroundY = float.NaN;
                b.TerrainRev = 0;
            }
            // Ground from another time (or ground work cut short by quitting) under buildings that stand.
            int uneven = 0;
            foreach (Building b in Terrain.Count + Flatten.Count > 0 ? new List<Building>() : s.Buildings)
            {
                if (b.State == BuildingState.Destroyed || b.SpawnedPieces == 0 || b.TerrainRev == 0) continue;
                if (SimHost.Sim.State.Day - b.RelevelDay < 30.0 || PadDeviation(s, b) <= UnevenPad) continue;
                uneven++;
                if (!fix) continue;
                b.TerrainRev = 0;
                b.RelevelDay = SimHost.Sim.State.Day;
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
            if (pits > 0) parts.Add(pits + " in a pit or on a mound, off the land");
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ buildings

        private static int SyncBuildings(Settlement s, int budget)
        {
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            if (faction == null) return budget;
            BuildStyle style = Blueprints.StyleFor(faction, s.Tier);

            RefitHeights(s);
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
                    // Revision 5 only moved the furniture of the great hall out of its doorway; revision 6
                    // gave the Fulings round huts under cone roofs, 7 raised their roofs clear of the doorway and
                    // gave the Greydwarfs dens among roots, 8 opened field fences where a road runs through, 9 turned
                    // every house a little its own way and gave it its own clutter, 10 and 11 hung the banners on their posts,
                    // gave every home a bed for each one it houses and its yard things (the square's too: the fire
                    // and the war camps go up again as well), 12 turned the sharpened stakes of war camps and Fuling gates
                    // to point out, 13 roofed the Fulings' storehouses, smithies and hunters' lodges with their hides, 14 gave
                    // the three human clans buildings of their own (Raven shingles and carvings, Wolf logs and hipped
                    // roofs, Stag porches).
                    bool changed = (b.LayoutRev < 4
                        ? b.Kind != BuildingKind.Campfire && b.Kind != BuildingKind.Farm && b.Kind != BuildingKind.Palisade && b.Kind != BuildingKind.Watchtower
                        : (b.LayoutRev < 5 && b.Kind == BuildingKind.GreatHall) || (b.LayoutRev < 7 && (style.Id == "goblin" || style.Id == "root") && Blueprints.RoundRadius(b.Kind) > 0f)
                          || (b.LayoutRev < 8 && b.Kind == BuildingKind.Farm))
                          || (b.LayoutRev < 11 && (b.Plot >= 0 || b.Kind == BuildingKind.Campfire))
                          || (b.LayoutRev < 12 && ((b.Kind == BuildingKind.Campfire && s.CampaignId != 0) || (b.Kind == BuildingKind.Palisade && style.Id == "goblin")))
                          || (b.LayoutRev < 13 && style.Id == "goblin" && (b.Kind == BuildingKind.Storehouse || b.Kind == BuildingKind.Smithy || b.Kind == BuildingKind.HunterLodge))
                          || (b.LayoutRev < 14 && (style.Id == "raven" || style.Id == "wolf" || style.Id == "stag") && b.Plot >= 0 && b.Kind != BuildingKind.Farm);
                    if (changed && b.SpawnedPieces > 0)
                    {
                        RemovePieces(s, b, origin);
                        b.SpawnedPieces = 0;
                    }
                    b.LayoutRev = LayoutRevision;
                }

                List<PieceSpec> blueprint;
                if (b.Kind == BuildingKind.Palisade)
                {
                    // Gates where the roads come in; a new road through it, or the town becoming a
                    // city, and the wall is taken down and put up again as it should be now.
                    string signature;
                    List<float> gates = Roads.Crossings(s, s.Tier >= Tier.City ? Defs.CityWallRadius : Defs.PalisadeRadius);
                    // A gate once opened stays open: the paint of a road through it stays on the ground
                    // even when the road is planned elsewhere. Kept only at the same radius (a town's wall,
                    // or a city's), and after the gates of today's roads, which win where two are close.
                    int colon = b.Wall.IndexOf(':');
                    if (colon > 0 && b.Wall[colon - 1] == (s.Tier >= Tier.City ? 'C' : 'T'))
                    {
                        List<float> old = Roads.GatesOf(b.Wall);
                        // The pair a wall gets when no road comes is not kept once roads do.
                        bool fallback = old.Count == 2 && old[0] == 0f && old[1] == 180f;
                        if (!(fallback && gates.Count > 0)) gates.AddRange(old);
                    }
                    blueprint = Blueprints.Wall(style, s.Tier, gates, out signature);
                    if (b.Wall != signature)
                    {
                        if (b.SpawnedPieces > 0) RemovePieces(s, b, origin);
                        b.SpawnedPieces = 0;
                        b.Wall = signature;
                    }
                }
                else if (b.Kind == BuildingKind.Campfire && s.CampaignId != 0) blueprint = Blueprints.WarCamp(style, s.Seed);
                else blueprint = Blueprints.Get(b.Kind, style, s.Biome, b.Id + s.Seed);
                BuildingDef def = Defs.Buildings[b.Kind];
                int desired = blueprint.Count;
                if (b.State != BuildingState.Complete)
                {
                    float progress = def.Work > 0f ? Mathf.Clamp01(b.Work / def.Work) : 1f;
                    desired = Mathf.FloorToInt(blueprint.Count * progress);
                }

                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                List<KeyValuePair<Vector3, Vector4>> paths = null;
                while (b.SpawnedPieces < desired && budget > 0)
                {
                    PieceSpec spec = blueprint[b.SpawnedPieces];
                    Vector3 pos = origin + rotation * spec.Pos;
                    if (spec.Yard)
                    {
                        // A woodpile or a cart is left out where it would stand on a road or somebody's path.
                        if (paths == null) paths = FencePaths(s);
                        if (YardInTheWay(s, pos, spec.Reach, paths))
                        {
                            b.SpawnedPieces++;
                            continue;
                        }
                    }
                    if (b.Kind == BuildingKind.Palisade)
                    {
                        // The wall follows the land instead of standing on a levelled pad.
                        float ground;
                        if (!ZoneSystem.instance.IsZoneLoaded(pos) || !ZoneSystem.instance.GetGroundHeight(pos, out ground)) break;
                        pos.y = ground - 0.25f + spec.Pos.y;
                        // Trees and bushes on the line of the wall (and in its gates) are cleared as it goes up.
                        if (spec.Pos.y < 2f) ClearVegetation(pos, 3.5f);
                    }
                    Place(s, b, spec.Prefab, pos, Quaternion.Euler(0f, yaw + spec.Yaw, 0f));
                    b.SpawnedPieces++;
                    budget--;
                }
            }
            return budget;
        }

        /// <summary>
        /// The steepest a walk between two pads may be: their floors differ by no more than this much for each
        /// metre of open ground between their levelled edges (and Fit's tolerance). Steeper than that, the bank
        /// between them could not be ramped without cutting into one of the floors, and stayed a step.
        /// </summary>
        private const float PadGrade = 0.35f, PadFitTolerance = 0.25f;

        /// <summary>Pads further apart than this (edge to edge) do not bind each other's height.</summary>
        private const float PadFitReach = 14f;

        /// <summary>
        /// The height a pad is levelled to, fitted to the pads already there: as near the height its own ground
        /// asks for (wanted) as walking between it and every pad close by allows (PadGrade over the open ground
        /// between their edges), and never further off the land than a pad may be (MaxPadDrift). Where the
        /// neighbours ask for more than one height, the middle of what they allow.
        /// </summary>
        private static float FitHeight(Settlement s, Building b, Vector3 origin, float radius, float wanted)
        {
            float lo = float.MinValue, hi = float.MaxValue;
            foreach (Building other in s.Buildings)
            {
                if (other == b || other.State == BuildingState.Destroyed || float.IsNaN(other.GroundY)) continue;
                float r = Blueprints.Radius(other.Kind);
                if (r <= 0f) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (other.Plot >= 0) Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                float gap = Utils.DistanceXZ(origin, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) - radius - r - 2f * PadMargin;
                if (gap > PadFitReach) continue;
                float allowed = PadGrade * Mathf.Max(0f, gap) + PadFitTolerance;
                lo = Mathf.Max(lo, other.GroundY - allowed);
                hi = Mathf.Min(hi, other.GroundY + allowed);
            }
            float h = lo <= hi ? Mathf.Clamp(wanted, lo, hi) : lo == float.MinValue ? wanted : (lo + hi) * 0.5f;
            float natural = NaturalHeight(origin, radius);
            return Mathf.Clamp(h, natural - MaxPadDrift, natural + MaxPadDrift);
        }

        /// <summary>
        /// Settlements laid out before TerrainRevision 9 had each pad at the height of its own ground, and on a
        /// slope two pads side by side could differ by more than anyone walks up: a bank no ramp could take. Once,
        /// every pad is fitted to those before it (the fire first, then by age); a pad that moves is taken down
        /// and goes up again at its new height (the revision's own relevelling then shapes the ground for it).
        /// </summary>
        private static void RefitHeights(Settlement s)
        {
            bool due = false;
            foreach (Building b in s.Buildings)
            {
                if (b.TerrainRev > 0 && b.TerrainRev < FitRevision && !float.IsNaN(b.GroundY)) due = true;
            }
            if (!due) return;
            List<Building> order = new List<Building>(s.Buildings);
            order.Sort((x, y) => x.Kind == BuildingKind.Campfire && y.Kind != BuildingKind.Campfire ? -1 : y.Kind == BuildingKind.Campfire && x.Kind != BuildingKind.Campfire ? 1 : x.Id.CompareTo(y.Id));
            // Fitted in turn: a pad is bound by those before it only, as when they were first put down.
            Dictionary<Building, float> was = new Dictionary<Building, float>();
            foreach (Building b in order)
            {
                if (!float.IsNaN(b.GroundY)) was[b] = b.GroundY;
                b.GroundY = float.NaN;
            }
            int moved = 0;
            float most = 0f;
            Vector3 centre = new Vector3(s.Pos.X, 0f, s.Pos.Z);
            foreach (Building b in order)
            {
                float old;
                if (!was.TryGetValue(b, out old)) continue;
                float radius = b.Kind == BuildingKind.Campfire && s.CampaignId != 0 ? WarCampRadius : Blueprints.Radius(b.Kind);
                if (b.State == BuildingState.Destroyed || radius <= 0f || b.TerrainRev >= FitRevision)
                {
                    b.GroundY = old;
                    continue;
                }
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
                float fitted = FitHeight(s, b, origin, radius, old);
                b.GroundY = fitted;
                if (Mathf.Abs(fitted - old) < 0.05f) continue;
                moved++;
                most = Mathf.Max(most, Mathf.Abs(fitted - old));
                if (b.SpawnedPieces > 0) RemoveTagged(s, centre, b.Id);
                b.SpawnedPieces = 0;
                // Levelled again by the revision (TryGetOrigin), and its path looked at again.
                b.TerrainRev = 0;
                b.Graded = 0;
            }
            if (moved > 0) Plugin.Log.LogInfo(s.Name + ": " + moved + " pads fitted to their neighbours (moved up to " + most.ToString("0.0") + " m), put up again");
        }

        private static readonly Dictionary<int, float> PadsLooked = new Dictionary<int, float>();
        private static readonly Dictionary<long, int> PadRetries = new Dictionary<long, int>();

        /// <summary>
        /// When all ground work is done, the pads are looked over (every 30 s while the settlement is near) and one
        /// not level (0.1 m) is flattened again, three times at most in a session. Shaping one pad blends its edge
        /// into the land and can tug at a neighbour's; each such pad is queued to be flattened again, but a shaping
        /// that came after a neighbour's last flattening (a pad refitted, a building put up) left it as it was.
        /// </summary>
        private static void SettlePads(Settlement s)
        {
            float due;
            if (!PadsSettled() || Smooths.Count > 0 || (PadsLooked.TryGetValue(s.Id, out due) && Time.time < due)) return;
            PadsLooked[s.Id] = Time.time + 30f;
            foreach (Building b in s.Buildings)
            {
                float radius = Blueprints.Radius(b.Kind);
                if (b.State == BuildingState.Destroyed || float.IsNaN(b.GroundY) || radius <= 0f || b.TerrainRev < TerrainRevision) continue;
                if (PadDeviation(s, b) <= 0.1f) continue;
                long key = ((long)s.Id << 32) | (uint)b.Id;
                int tries;
                PadRetries.TryGetValue(key, out tries);
                if (tries >= 3) continue;
                PadRetries[key] = tries + 1;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                EnqueueTerrain(new TerrainJob { Pos = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z), Radius = radius + PadMargin, LevelOnly = true, NoPaint = true });
                Plugin.Log.LogInfo(s.Name + ": " + b.Kind + " #" + b.Id + " stands " + PadDeviation(s, b).ToString("0.00") + " m off level, flattened again");
            }
        }

        /// <summary>The terrain revision from which pads are fitted to their neighbours (RefitHeights).</summary>
        private const int FitRevision = 9;

        /// <summary>World position and yaw of a building, preparing its ground the first time. False while its zone is not loaded.</summary>
        private static bool TryGetOrigin(Settlement s, Building b, out Vector3 origin, out float yaw)
        {
            Vec2 offset = new Vec2(0f, 0f);
            yaw = 0f;
            if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
            origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
            if (!ZoneSystem.instance.IsZoneLoaded(origin)) return false;

            if (float.IsNaN(b.GroundY))
            {
                // A war camp spreads its tents round the fire on a wider levelled ground.
                float radius = b.Kind == BuildingKind.Campfire && s.CampaignId != 0 ? WarCampRadius : Blueprints.Radius(b.Kind);
                float ground;
                if (!PadHeight(origin, radius, out ground)) return false;
                ground = FitHeight(s, b, origin, radius, ground);
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

        private const int TerrainRevision = 9;
        private const int LayoutRevision = 14;
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
        private static readonly HashSet<int> RampNotes = new HashSet<int>();
        private static readonly HashSet<int> StreetsEased = new HashSet<int>();

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
                if (Net.IsBuilder(s))
                {
                    OpenFences(s, centre);
                    // The streets once a session, when the pads round them are done.
                    // Only once the pads are levelled: a path looked at while its pads wait is still the land, and would be
                    // taken for flat and never looked at again.
                    if (!StreetsEased.Contains(s.Id) && PadsSettled() && EaseStreets(s)) StreetsEased.Add(s.Id);
                }
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
                    // Looked at once every session, not once ever: ground work cut short by quitting, or a bank
                    // that came later (a new pad beside the path), is seen to the next time.
                    // Every pass while the settlement is near: a bank can come up after the first look (the ground of a
                    // neighbouring pad, a street cut beside it). A path that is flat costs only a few heights to check.
                    if (b.State == BuildingState.Complete && Net.IsBuilder(s) && PadsSettled() && EaseBanks(s, b, from, to)) b.Graded = 2;
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

        /// <summary>
        /// Where a path goes over a bank too steep to walk (a pad cut into a slope, a step between two
        /// pads levelled at different heights), the ground along it is cut into a ramp: from the floor
        /// of one pad to the next (or to the ground where the path ends) in a straight slope, spot by
        /// spot where the ground is off it. Never on a pad itself, and next to one only levelled, not
        /// smoothed, so the pad keeps its level floor; never further off the land than a pad may be.
        /// False while the ground along the path is not all loaded yet.
        /// </summary>
        private static bool EaseBanks(Settlement s, Building b, Vector3 from, Vector3 to)
        {
            float length = Utils.DistanceXZ(from, to);
            int n = Mathf.Max(2, Mathf.CeilToInt(length / 1.5f) + 1);
            float[] ground = new float[n], target = new float[n];
            bool[] fixedAt = new bool[n];
            Vector3[] spots = new Vector3[n];
            bool steep = false;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)(n - 1));
                spots[i] = p;
                if (!ZoneSystem.instance.IsZoneLoaded(p) || !ZoneSystem.instance.GetGroundHeight(p, out ground[i]))
                {
                    _pathsUnloaded++;
                    return false;
                }
                // Over 0.6 m in 1.5 m (about 22 degrees) is a bank, not a slope.
                if (i > 0 && Mathf.Abs(ground[i] - ground[i - 1]) > 0.6f) steep = true;
                // On any pad the path keeps to that pad's floor: with the pads fitted to each other (FitHeight) the
                // floors along a path differ by no more than a walk, and the ramp runs between them.
                float floor;
                if (PadFloor(s, p, out floor))
                {
                    fixedAt[i] = true;
                    target[i] = floor;
                }
            }
            if (!steep)
            {
                _pathsFlat++;
                return true;
            }
            _pathsSteep++;
            // The ends stand where they are; between fixed spots the ramp runs straight.
            if (!fixedAt[0])
            {
                fixedAt[0] = true;
                target[0] = ground[0];
            }
            if (!fixedAt[n - 1])
            {
                fixedAt[n - 1] = true;
                target[n - 1] = ground[n - 1];
            }
            int last = 0;
            for (int i = 1; i < n; i++)
            {
                if (!fixedAt[i]) continue;
                for (int k = last + 1; k < i; k++) target[k] = Mathf.Lerp(target[last], target[i], (k - last) / (float)(i - last));
                last = i;
            }
            // All of the ramp or none of it: half a ramp leaves a new step where it stops.
            for (int i = 0; i < n; i++)
            {
                if (fixedAt[i] || Mathf.Abs(ground[i] - target[i]) < 0.3f) continue;
                if (Mathf.Abs(target[i] - NaturalHeight(spots[i], 0.65f)) > RampDrift - 0.2f)
                {
                    // Said once for each building: this is looked at again on every pass.
                    if (RampNotes.Add(s.Id * 100000 + b.Id)) Plugin.Log.LogInfo(s.Name + ": no ramp along the path of " + b.Kind + " #" + b.Id + " at " + spots[i].ToString("0") + ", it would cut " + (target[i] - NaturalHeight(spots[i], 0.65f)).ToString("+0.0;-0.0") + " m off the land");
                    _rampsSkipped++;
                    return true;
                }
            }
            int queued = 0, fixedCount = 0, pending = 0, close = 0;
            for (int i = 0; i < n; i++)
            {
                if (fixedAt[i]) fixedCount++;
                if (fixedAt[i] || Mathf.Abs(ground[i] - target[i]) < 0.3f)
                {
                    if (!fixedAt[i]) close++;
                    continue;
                }
                Vector3 p = spots[i];
                // Already on its way (queued by an earlier pass): not queued again.
                if (GroundWorkNear(p, 1.2f))
                {
                    pending++;
                    continue;
                }
                // A spot that a pad's floor must keep out of is not graded at all (checked here, not at the refusal:
                // a refused job is queued again on every pass).
                bool smoothIt = !OnAPad(s, p, 4.5f), onFloor = false;
                PadsNear(p, 1.3f, ref smoothIt, ref onFloor);
                if (onFloor)
                {
                    _gradesRefused++;
                    continue;
                }
                QueueGrade(new Vector3(p.x, target[i], p.z), 1.3f, smoothIt, RampDrift);
                queued++;
            }
            if (RampNotes.Add(-(s.Id * 100000 + b.Id) - 1))
                Plugin.Log.LogInfo(s.Name + " " + b.Kind + " #" + b.Id + ": steep path of " + n + " spots, " + fixedCount + " on a floor, " + close + " already level with the target, " + pending + " pending, " + queued + " queued");
            _ramps += queued > 0 ? 1 : 0;
            if (queued > 0) Plugin.Log.LogInfo(s.Name + ": a ramp of " + queued + " spots cut along a path over a bank");
            return true;
        }

        /// <summary>
        /// `pathcheck`: the steepest step (per 1.5 m) along each building's path off the pads, as the
        /// ground is now. BANK where it is over 0.6 m, more than anyone walks up.
        /// </summary>
        internal static string PathCheck(Settlement s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int banks = 0, paths = 0, noPlot = 0, ruined = 0, noGround = 0, noPath = 0, unloaded = 0;
            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            sb.AppendLine("player at " + me.ToString("0") + ", " + Vector3.Distance(me, new Vector3(s.Pos.X, me.y, s.Pos.Z)).ToString("0") + " m from the centre");
            foreach (Building b in s.Buildings)
            {
                if (b.Plot < 0) { noPlot++; continue; }
                if (b.State == BuildingState.Destroyed) { ruined++; continue; }
                if (float.IsNaN(b.GroundY)) { noGround++; continue; }
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                Vector3 pad = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z), from, to;
                int paint;
                float width;
                if (!PathOf(s, b, pad, out from, out to, out paint, out width)) { noPath++; continue; }
                float length = Utils.DistanceXZ(from, to), last = float.NaN, worst = 0f, lastAt = 0f, at = 0f, beforeH = 0f, afterH = 0f;
                bool loaded = true;
                for (float d = 0f; d <= length; d += 1.5f)
                {
                    Vector3 p = Vector3.Lerp(from, to, length > 0f ? d / length : 0f);
                    float ground;
                    if (!ZoneSystem.instance.IsZoneLoaded(p) || !ZoneSystem.instance.GetGroundHeight(p, out ground))
                    {
                        loaded = false;
                        break;
                    }
                    float floor;
                    if (!float.IsNaN(last) && !PadFloor(s, p, out floor) && Mathf.Abs(ground - last) > worst)
                    {
                        worst = Mathf.Abs(ground - last);
                        at = Utils.DistanceXZ(p, pad);
                        beforeH = last;
                        afterH = ground;
                    }
                    last = ground;
                    lastAt = d;
                }
                if (!loaded) { unloaded++; continue; }
                paths++;
                if (worst > 0.6f) banks++;
                sb.AppendLine(b.Kind + " #" + b.Id + " (graded " + b.Graded + "): steepest step " + worst.ToString("0.00", CultureInfo.InvariantCulture) + " m per 1.5 m" + (worst > 0.6f ? ": BANK" : "")
                    + (worst > 0.6f ? " at " + at.ToString("0.0", CultureInfo.InvariantCulture) + " m from its centre (" + beforeH.ToString("0.00", CultureInfo.InvariantCulture) + " -> " + afterH.ToString("0.00", CultureInfo.InvariantCulture)
                        + ", floor " + b.GroundY.ToString("0.00", CultureInfo.InvariantCulture) + ", natural " + NaturalHeight(pad, 0.65f).ToString("0.00", CultureInfo.InvariantCulture) + ")" : ""));
            }
            return paths + " paths, " + banks + " over a bank (skipped: no plot " + noPlot + ", ruined " + ruined + ", no ground " + noGround + ", no path " + noPath + ", not loaded " + unloaded + ")\n" + sb.ToString().TrimEnd();
        }

        /// <summary>The floor height of the pad a point is on (within its levelled ground), if any.</summary>
        /// <summary>
        /// The floor a path's own ends stand on: the building's own pad, or the square's (the fire). Other buildings'
        /// pads are not floors for this path: in a crowded village most of a path lies on someone else's pad, and
        /// fixing it to that floor left the land between the ends unramped.
        /// </summary>
        private static bool OwnFloor(Settlement s, Building b, Vector3 p, out float floor)
        {
            floor = 0f;
            foreach (Building other in s.Buildings)
            {
                if (other != b && other.Kind != BuildingKind.Campfire) continue;
                float radius = Blueprints.Radius(other.Kind);
                if (radius <= 0f || other.State == BuildingState.Destroyed || float.IsNaN(other.GroundY)) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (other.Plot >= 0) Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < radius + PadMargin)
                {
                    floor = other.GroundY;
                    return true;
                }
            }
            return false;
        }

        private static bool PadFloor(Settlement s, Vector3 p, out float floor)
        {
            floor = 0f;
            foreach (Building other in s.Buildings)
            {
                float radius = Blueprints.Radius(other.Kind);
                if (radius <= 0f || other.State == BuildingState.Destroyed || float.IsNaN(other.GroundY)) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (other.Plot >= 0) Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < radius + PadMargin)
                {
                    floor = other.GroundY;
                    return true;
                }
            }
            return false;
        }

        private static readonly int FencePrefab = "wood_fence".GetStableHashCode();

        /// <summary>The settlement's paths, each as (from, to, half width): where a field's fence leaves a way through.</summary>
        private static List<KeyValuePair<Vector3, Vector4>> FencePaths(Settlement s)
        {
            List<KeyValuePair<Vector3, Vector4>> paths = new List<KeyValuePair<Vector3, Vector4>>();
            if (!ModConfig.Roads.Value) return paths;
            foreach (Building other in s.Buildings)
            {
                if (other.Plot < 0 || other.State == BuildingState.Destroyed) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                Vector3 pad = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z), from, to;
                int paint;
                float width;
                if (!PathOf(s, other, pad, out from, out to, out paint, out width)) continue;
                // The path of a field ends at its edge: the fence leaves its way in there.
                if (other.Kind == BuildingKind.Farm) to += (to - from).normalized * 1.5f;
                paths.Add(new KeyValuePair<Vector3, Vector4>(from, new Vector4(to.x, to.y, to.z, width)));
            }
            return paths;
        }

        /// <summary>A field's fence keeps out of a road and of the settlement's own paths.</summary>
        private static bool FenceInTheWay(Settlement s, Vector3 pos, List<KeyValuePair<Vector3, Vector4>> paths)
        {
            if (Roads.NearRoad(s, pos, 3f)) return true;
            foreach (KeyValuePair<Vector3, Vector4> path in paths)
            {
                if (DistanceToSegment(pos, path.Key, new Vector3(path.Value.x, path.Value.y, path.Value.z)) < path.Value.w + 0.9f) return true;
            }
            return false;
        }

        /// <summary>
        /// A yard thing reaching that far from its spot keeps 2.9 m off the line of a road and clear of the
        /// settlement's paths (their half width and a little more).
        /// </summary>
        private static bool YardInTheWay(Settlement s, Vector3 pos, float reach, List<KeyValuePair<Vector3, Vector4>> paths)
        {
            if (Roads.NearRoad(s, pos, 2.9f + reach)) return true;
            foreach (KeyValuePair<Vector3, Vector4> path in paths)
            {
                if (DistanceToSegment(pos, path.Key, new Vector3(path.Value.x, path.Value.y, path.Value.z)) < path.Value.w * 0.5f + 0.4f + reach) return true;
            }
            return false;
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

        /// <summary>
        /// Field fences put up before a road or a path came through (or by an older version) are taken
        /// out where they bar the way. A field is not counted as a ruin for it (CheckWrecks leaves fields out).
        /// </summary>
        private static void OpenFences(Settlement s, Vector3 centre)
        {
            SectorObjects.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(centre), SimulationDistance.OriginalDistance, SectorObjects);
            int opened = 0, yard = 0;
            List<KeyValuePair<Vector3, Vector4>> paths = null;
            Dictionary<int, Building> byId = new Dictionary<int, Building>();
            Dictionary<int, List<KeyValuePair<int, Vector4>>> yards = new Dictionary<int, List<KeyValuePair<int, Vector4>>>();
            foreach (Building b in s.Buildings) byId[b.Id] = b;
            foreach (ZDO zdo in SectorObjects.ToArray())
            {
                if (zdo.GetInt(HashSettlement) != s.Id || Npcs.IsNpcPrefab(zdo.GetPrefab())) continue;
                Building owner;
                if (!byId.TryGetValue(zdo.GetInt(HashBuilding), out owner)) continue;
                if (zdo.GetPrefab() == FencePrefab && owner.Kind == BuildingKind.Farm)
                {
                    if (paths == null) paths = FencePaths(s);
                    if (!FenceInTheWay(s, zdo.GetPosition(), paths)) continue;
                    RemoveNpc(zdo);
                    opened++;
                }
                else
                {
                    List<KeyValuePair<int, Vector4>> spots;
                    if (!yards.TryGetValue(owner.Id, out spots)) yards[owner.Id] = spots = YardSpots(s, owner);
                    Vector3 at = zdo.GetPosition();
                    foreach (KeyValuePair<int, Vector4> spot in spots)
                    {
                        if (spot.Key != zdo.GetPrefab() || Utils.DistanceXZ(at, new Vector3(spot.Value.x, 0f, spot.Value.z)) > 0.4f) continue;
                        if (paths == null) paths = FencePaths(s);
                        if (YardInTheWay(s, at, spot.Value.w, paths))
                        {
                            RemoveNpc(zdo);
                            yard++;
                        }
                        break;
                    }
                }
            }
            if (opened > 0) Plugin.Log.LogInfo(s.Name + ": " + opened + " field fence pieces taken out of the way of a road or path");
            if (yard > 0) Plugin.Log.LogInfo(s.Name + ": " + yard + " pieces of yards taken off a road");
        }

        /// <summary>
        /// The streets inside a settlement, where the road keeper leaves the ground alone: their ground
        /// evened out over some 10 m and held to a walkable grade (a step of at most 0.45 m in 1.5 m),
        /// cut at most 1.8 m, spot by spot where it is off. On a pad's level ground the street keeps to
        /// the pad; next to a pad the ground is only levelled, not smoothed. False while not all loaded.
        /// </summary>
        private static bool EaseStreets(Settlement s)
        {
            int queued = 0;
            foreach (List<Vector3> street in Roads.StreetsOf(s))
            {
                List<Vector3> spots = new List<Vector3>();
                for (int k = 0; k < street.Count - 1; k++)
                {
                    float leg = Utils.DistanceXZ(street[k], street[k + 1]);
                    for (float d = 0f; d < leg; d += 1.5f) spots.Add(Vector3.Lerp(street[k], street[k + 1], d / leg));
                }
                spots.Add(street[street.Count - 1]);
                int n = spots.Count;
                float[] ground = new float[n], target = new float[n];
                bool[] fixedAt = new bool[n];
                bool steep = false;
                for (int i = 0; i < n; i++)
                {
                    if (!ZoneSystem.instance.IsZoneLoaded(spots[i]) || !ZoneSystem.instance.GetGroundHeight(spots[i], out ground[i])) return false;
                    if (i > 0 && Mathf.Abs(ground[i] - ground[i - 1]) > 0.6f) steep = true;
                    float floor;
                    fixedAt[i] = PadFloor(s, spots[i], out floor);
                    target[i] = fixedAt[i] ? floor : ground[i];
                }
                if (!steep) continue;
                // The outer end meets the road bed the road keeper cuts: it stays where it is.
                fixedAt[n - 1] = true;
                float[] even = new float[n];
                for (int i = 0; i < n; i++)
                {
                    if (fixedAt[i])
                    {
                        even[i] = target[i];
                        continue;
                    }
                    float sum = 0f;
                    int count = 0;
                    for (int k = Mathf.Max(0, i - 3); k <= Mathf.Min(n - 1, i + 3); k++)
                    {
                        sum += target[k];
                        count++;
                    }
                    even[i] = sum / count;
                }
                for (int pass = 0; pass < 4; pass++)
                {
                    for (int i = 1; i < n; i++) if (!fixedAt[i]) even[i] = Mathf.Clamp(even[i], even[i - 1] - 0.45f, even[i - 1] + 0.45f);
                    for (int i = n - 2; i >= 0; i--) if (!fixedAt[i]) even[i] = Mathf.Clamp(even[i], even[i + 1] - 0.45f, even[i + 1] + 0.45f);
                    for (int i = 0; i < n; i++) if (!fixedAt[i]) even[i] = Mathf.Clamp(even[i], ground[i] - 1.8f, ground[i] + 1.8f);
                }
                bool fits = true;
                for (int i = 0; i < n && fits; i++) fits = fixedAt[i] || Mathf.Abs(ground[i] - even[i]) < 0.25f || Mathf.Abs(even[i] - NaturalHeight(spots[i], 0.65f)) <= MaxPadDrift + 1.3f;
                if (!fits) continue;
                for (int i = 0; i < n; i++)
                {
                    if (fixedAt[i] || Mathf.Abs(ground[i] - even[i]) < 0.25f) continue;
                    Vector3 p = spots[i];
                    if (GroundWorkNear(p, 1.2f)) continue;
                    QueueGrade(new Vector3(p.x, even[i], p.z), 1.6f, !OnAPad(s, p, 4.5f));
                    queued++;
                }
            }
            if (queued > 0) Plugin.Log.LogInfo(s.Name + ": " + queued + " spots of its streets cut into ramps");
            return true;
        }

        /// <summary>
        /// The yard things of a building (woodpiles, carts, hives: the blueprint marks them) where they
        /// stand in the world, by prefab: so one put up before a road came that way can be told apart
        /// from the building's own walls, which are never taken away.
        /// </summary>
        private static List<KeyValuePair<int, Vector4>> YardSpots(Settlement s, Building b)
        {
            List<KeyValuePair<int, Vector4>> spots = new List<KeyValuePair<int, Vector4>>();
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            if (faction == null || b.Kind == BuildingKind.Palisade || float.IsNaN(b.GroundY)) return spots;
            if (b.Kind == BuildingKind.Campfire && s.CampaignId != 0) return spots;
            BuildStyle style = Blueprints.StyleFor(faction, s.Tier);
            Vec2 offset = new Vec2(0f, 0f);
            float yaw = 0f;
            if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
            Vector3 origin = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (PieceSpec spec in Blueprints.Get(b.Kind, style, s.Biome, b.Id + s.Seed))
            {
                if (!spec.Yard) continue;
                Vector3 at = origin + rotation * spec.Pos;
                spots.Add(new KeyValuePair<int, Vector4>(spec.Prefab.GetStableHashCode(), new Vector4(at.x, at.y, at.z, spec.Reach)));
            }
            return spots;
        }

        private static bool OnAPad(Settlement s, Vector3 p, float reach)
        {
            foreach (Building other in s.Buildings)
            {
                float radius = Blueprints.Radius(other.Kind);
                if (radius <= 0f || other.State == BuildingState.Destroyed || float.IsNaN(other.GroundY)) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (other.Plot >= 0) Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                if (Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z)) < radius + PadMargin + reach) return true;
            }
            return false;
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
                Defs.PlotTransform(s, other.Plot, out offset, out yaw);
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

        /// <summary>Ground the mod shaped this session, and ground players worked, by 8 m cells (HealSeams).</summary>
        private static readonly HashSet<long> ModWorked = new HashSet<long>(), PlayerWorked = new HashSet<long>();
        private const float WorkCell = 8f;

        private static long CellOf(Vector3 p)
        {
            return ((long)Mathf.FloorToInt(p.x / WorkCell) << 32) ^ (uint)Mathf.FloorToInt(p.z / WorkCell);
        }

        private static void Mark(HashSet<long> cells, Vector3 pos, float radius)
        {
            int x0 = Mathf.FloorToInt((pos.x - radius) / WorkCell), x1 = Mathf.FloorToInt((pos.x + radius) / WorkCell);
            int z0 = Mathf.FloorToInt((pos.z - radius) / WorkCell), z1 = Mathf.FloorToInt((pos.z + radius) / WorkCell);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++) cells.Add(((long)x << 32) ^ (uint)z);
            }
        }

        /// <summary>A player raised, dug, levelled or smoothed the ground there (their hoe, pickaxe or cultivator).</summary>
        internal static void NotePlayerGroundWork(Vector3 pos, float radius)
        {
            Mark(PlayerWorked, pos, radius);
        }

        /// <summary>
        /// The game shapes each terrain tile from that tile's own heights, so smoothing that reaches
        /// across the border of two tiles leaves them disagreeing along it: a visible tear. Once the
        /// ground work around the player is finished, every vertex where two tiles differ is levelled
        /// to the mean of the two on both tiles at once, which closes the tear. Only where the mod
        /// shaped the ground itself, and never where a player worked it.
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
                        // Only a tear the mod's own ground work can have made, and none where a player has worked the
                        // ground: mending the game's own small seams under a player levelling a field with the hoe
                        // dug ditches and raised humps along every tile border they crossed.
                        long cell = CellOf(p);
                        if (!ModWorked.Contains(cell) || PlayerWorked.Contains(cell)) continue;
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
                            // Never at a player's own building: the ground there is theirs.
                            if (Roads.PlayerBuilt(p, 4f)) continue;
                            EnqueueTerrain(new TerrainJob { Pos = new Vector3(p.x, mean, p.z), Radius = 1.3f, LevelOnly = true, NoPaint = true, Seam = true });
                            queued++;
                            break;
                        }
                    }
                }
            }
            SeamsHealed += queued;
        }

        /// <summary>Evens out the ground at one spot of a road bed, without levelling it to a set height.</summary>
        /// <summary>The ground round pos drawn toward pos.y (see TerrainJob.Blend): a road bed's steps evened out.</summary>
        public static void QueueBlend(Vector3 pos, float radius)
        {
            Smooths.Add(new TerrainJob { Pos = pos, Radius = radius, Grade = true, Blend = true, Queued = Time.time });
        }

        public static void QueueSmooth(Vector3 pos, float radius)
        {
            Smooths.Add(new TerrainJob { Pos = pos, Radius = radius, Queued = Time.time });
        }

        /// <summary>
        /// Cuts one spot of a road bed or a ramp down (or builds it up) to the height given in pos.y.
        /// Smoothed round the spot unless it is by the border of two terrain tiles, which smoothing tears.
        /// </summary>
        public static void QueueGrade(Vector3 pos, float radius, bool smooth, float drift = 0f)
        {
            Smooths.Add(new TerrainJob { Pos = pos, Radius = radius, Grade = true, Smooth = smooth, Drift = drift, Queued = Time.time });
        }

        /// <summary>True when no pad or other ground work is waiting: the ground is what the pads made of it.</summary>
        private static bool PadsSettled()
        {
            return Terrain.Count + Flatten.Count == 0;
        }

        /// <summary>A ramp between two pads may lie this far off the land: it runs between their floors, which may both be cut into a hill.</summary>
        private const float RampDrift = 6f;

        /// <summary>True while ground work of a road bed or ramp is waiting near the point.</summary>
        public static bool GroundWorkNear(Vector3 p, float reach)
        {
            foreach (TerrainJob job in Smooths)
            {
                if (job.Grade && Utils.DistanceXZ(job.Pos, p) < reach) return true;
            }
            return false;
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
            // Ground already dug or raised (by the player, or by an old bug), or not fully loaded yet,
            // must not set the pad: the pad keeps to the land as the world made it.
            float natural = NaturalHeight(origin, radius);
            if (Mathf.Abs(height - natural) > MaxPadDrift)
            {
                Plugin.Log.LogWarning("Pad at " + origin.ToString("0") + ": the ground there is " + (height - natural).ToString("+0.0;-0.0") + " m off the land; levelling to the land instead");
                height = natural;
            }
            return true;
        }

        /// <summary>How far a pad may lie from the land as the world generator made it, before it is taken for a mistake.</summary>
        private const float MaxPadDrift = 2.5f;
        private const float WarCampRadius = 8.5f;

        /// <summary>The land's own height round a spot, averaged like a pad's, from the world generator: no loaded terrain needed, no digging counts.</summary>
        internal static float NaturalHeight(Vector3 origin, float radius)
        {
            WorldGenerator gen = WorldGenerator.instance;
            if (gen == null) return origin.y;
            float sum = gen.GetHeight(origin.x, origin.z) * 2f;
            float weight = 2f;
            for (int i = 0; i < 8 && radius > 0f; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 p = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.75f;
                sum += gen.GetHeight(p.x, p.z);
                weight += 1f;
            }
            return Mathf.Max(sum / weight, ZoneSystem.c_WaterLevel + 0.6f);
        }

        /// <summary>Testing: the next levelling job may dig as deep as it is told (to make a pit and see it mended).</summary>
        public static bool AllowDeepDig;

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
                if (other.Plot >= 0) Defs.PlotTransform(s, other.Plot, out offset, out yaw);
                Vector3 pos = new Vector3(s.Pos.X + offset.X, other.GroundY, s.Pos.Z + offset.Z);
                if (Utils.DistanceXZ(pos, origin) < Blueprints.Radius(added.Kind) + radius + PadMargin * 2f + 4f) EnqueueTerrain(new TerrainJob { Pos = pos, Radius = radius + PadMargin, LevelOnly = true });
            }
        }

        /// <summary>A piece put up for the settlement under a building id of its own (the envoy's tent): tagged as a building's are.</summary>
        internal static ZNetView PlaceTagged(Settlement s, int buildingId, string prefabName, Vector3 pos, Quaternion rot)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                if (MissingPrefabs.Add(prefabName)) Plugin.Log.LogWarning("Blueprint prefab not found: " + prefabName);
                return null;
            }
            GameObject go = Object.Instantiate(prefab, pos, rot);
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.GetZDO() != null)
            {
                view.GetZDO().Set(HashSettlement, s.Id);
                view.GetZDO().Set(HashBuilding, buildingId);
            }
            _placedTotal++;
            _lastPlaced = Time.unscaledTime;
            return view;
        }

        /// <summary>Takes one object of the world away, loaded or not.</summary>
        internal static void RemoveObject(ZDO zdo) { RemoveNpc(zdo); }

        private static void Place(Settlement s, Building b, string prefabName, Vector3 pos, Quaternion rot)
        {
            // A field's fence leaves a gap where a road runs through it, rather than barring the way.
            if (b.Kind == BuildingKind.Farm && prefabName == "wood_fence" && FenceInTheWay(s, pos, FencePaths(s))) return;
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
            if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
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
                // Some growth keeps its rock or pickable on a child object (the root is the spawner): look down the tree too.
                bool vegetation = root.GetComponentInChildren<TreeBase>() != null || root.GetComponentInChildren<TreeLog>() != null || root.GetComponentInChildren<Destructible>() != null
                    || root.GetComponentInChildren<MineRock>() != null || root.GetComponentInChildren<MineRock5>() != null || root.GetComponentInChildren<Pickable>() != null;
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
        /// <summary>Counts for `mat`: grades done, grades refused, the pad-queue drops (testing the ramps and streets).</summary>
        private static int _gradeLog;
        private static int _gradesDone, _gradesRefused, _ramps, _rampsSkipped, _gradeDrops, _pathsFlat, _pathsUnloaded, _pathsSteep;
        private static Vector3 _headPos;
        private static float _headRadius, _headSince;

        /// <summary>How far round a building's footprint (its radius) no grade of a ramp or road bed may reach.</summary>
        private const float FootprintKeep = 0.5f;

        /// <summary>Whether a grade of that radius at the spot would reach a building's floor (refused) or its pad with smoothing (smoothing off).</summary>
        private static void PadsNear(Vector3 p, float radius, ref bool smooth, ref bool onFloor)
        {
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (s.Destroyed || Utils.DistanceXZ(p, new Vector3(s.Pos.X, 0f, s.Pos.Z)) > 100f) continue;
                foreach (Building b in s.Buildings)
                {
                    float r = Blueprints.Radius(b.Kind);
                    if (r <= 0f || b.State == BuildingState.Destroyed || float.IsNaN(b.GroundY)) continue;
                    Vec2 offset = new Vec2(0f, 0f);
                    float yaw;
                    if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                    float d = Utils.DistanceXZ(p, new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z));
                    // A grade that reaches the building's own footprint (its radius and half a metre) would change its floor:
                    // refused. The margin round it is only the pad blending into the land, and a ramp may start there:
                    // kept out of it as well (as before 0.7.0's fitted pads), the first metre and a half off every pad stayed a
                    // bank no ramp reached. (Bounds reaching into the footprint, 0.8 r, let padcheck see the floor move.)
                    if (d < r + FootprintKeep + radius) onFloor = true;
                    if (d < r + PadMargin + radius + 3f) smooth = false;
                }
            }
        }

        /// <summary>
        /// Whether a tile's last ground change is not built into its heightmap yet. The game rebuilds the
        /// heightmap (and its collider) in the LateUpdate of the frame the change was made in (Heightmap.Poke
        /// with a delay of 1): from two frames on, with no rebuild pending, the next change starts from the
        /// ground as it is. This waited a fixed 1.5 s before, which made a settlement's pads and paths take
        /// minutes.
        /// </summary>
        private static bool Settling(TerrainComp comp)
        {
            float changed;
            // Testing (`settle <seconds>`): the old fixed wait, to compare the ground with.
            if (FixedSettle > 0f) return TerrainBusyAt.TryGetValue(comp, out changed) && Time.time < changed + FixedSettle;
            if (TerrainBusy.TryGetValue(comp, out changed) && Time.frameCount < changed + 2f) return true;
            Heightmap hm = TerrainHeightmap(comp);
            return hm != null && hm.m_doLateUpdate != 0;
        }

        /// <summary>Testing: when above 0, ground work waits this many seconds after each change instead (`settle`).</summary>
        public static float FixedSettle;
        private static readonly Dictionary<TerrainComp, float> TerrainBusyAt = new Dictionary<TerrainComp, float>();

        private static readonly AccessTools.FieldRef<TerrainComp, Heightmap> TerrainHeightmap = AccessTools.FieldRefAccess<TerrainComp, Heightmap>("m_hmap");

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
                    // The host has every object of the world in memory, so a saved compiler is known for certain;
                    // waiting for the whole area as well (ZNetScene.IsAreaReady, the tile's zone and the eight round it)
                    // held up for ever the ground work on a tile at the edge of the loaded land, whose outer neighbours
                    // are never brought in (a road's paint and bed 70 m off the player stood a whole test long).
                    if (SavedCompilerExists(hm) || (!Net.IsHost && !ZNetScene.instance.IsAreaReady(hm.transform.position))) return false;
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
            // the line, so the wait costs next to nothing however long the line is. While building pads wait, beds
            // give way to them, but not for ever: the oldest bed goes first once it has waited 45 s (the pads are
            // queued all the time in a settlement under construction, so the beds were starved for good before).
            bool padsWaiting = Terrain.Count + Flatten.Count > 0;
            for (int looked = 0; looked < 6 && Smooths.Count > 0 && smoothed < 1; looked++)
            {
                TerrainJob bed = Smooths[0];
                if (padsWaiting && Time.time - bed.Queued < 45f) break;
                Smooths.RemoveAt(0);
                if (!Compilers(bed.Pos, bed.Radius + (bed.Grade && bed.Smooth ? 4f : 1f), compilers))
                {
                    if (StillWaiting(ref bed)) Smooths.Add(bed);
                    else _gradeDrops++;
                    continue;
                }
                bool settling = false;
                foreach (TerrainComp comp in compilers)
                {
                    if (Settling(comp)) settling = true;
                }
                if (settling)
                {
                    Smooths.Add(bed);
                    continue;
                }
                float bedGround;
                Vector3 bedSpot = bed.Pos;
                // Ground not loaded yet waits its turn like any other job; it is not dropped silently.
                if (!ZoneSystem.instance.GetGroundHeight(bedSpot, out bedGround))
                {
                    if (StillWaiting(ref bed)) Smooths.Add(bed);
                    else _gradeDrops++;
                    continue;
                }
                if (bedGround < ZoneSystem.c_WaterLevel + 0.2f)
                {
                    _gradeDrops++;
                    continue;
                }
                TerrainOp.Settings even;
                if (bed.Grade)
                {
                    // The same guard against pits as for the pads: a bed far off the land is a mistake upstream.
                    float land = NaturalHeight(bed.Pos, bed.Radius * 0.5f);
                    if (Mathf.Abs(bed.Pos.y - land) > (bed.Drift > 0f ? bed.Drift : MaxPadDrift + 1.5f) || bed.Pos.y < ZoneSystem.c_WaterLevel + 0.3f)
                    {
                        _gradesRefused++;
                        Plugin.Log.LogWarning("Refused to grade ground at " + bed.Pos.ToString("0") + " to " + bed.Pos.y.ToString("0.0") + " m, " + (bed.Pos.y - land).ToString("+0.0;-0.0") + " m off the land");
                        continue;
                    }
                    // Never into a building's floor; no smoothing that reaches its pad.
                    bool smoothIt = bed.Smooth, onFloor = false;
                    PadsNear(bed.Pos, bed.Radius, ref smoothIt, ref onFloor);
                    if (onFloor)
                    {
                        _gradesRefused++;
                        Plugin.Log.LogWarning("Refused to grade ground at " + bed.Pos.ToString("0") + ": it would reach a building's floor");
                        continue;
                    }
                    if (bed.Blend)
                    {
                        // Drawn toward the bed, never onto a pad (a blend is all smoothing).
                        if (!smoothIt)
                        {
                            _gradesRefused++;
                            continue;
                        }
                        even = new TerrainOp.Settings
                        {
                            m_level = false,
                            m_smooth = true,
                            m_smoothRadius = bed.Radius,
                            m_smoothPower = 3f,
                            m_paintCleared = false
                        };
                    }
                    else even = new TerrainOp.Settings
                    {
                        m_level = true,
                        m_levelRadius = bed.Radius,
                        m_square = false,
                        m_smooth = smoothIt,
                        m_smoothRadius = bed.Radius + 3f,
                        m_smoothPower = 3f,
                        m_paintCleared = false
                    };
                }
                else
                {
                    bedSpot.y = bedGround;
                    even = new TerrainOp.Settings
                    {
                        m_level = false,
                        m_smooth = true,
                        m_smoothRadius = bed.Radius,
                        m_smoothPower = 3f,
                        m_paintCleared = false
                    };
                }
                foreach (TerrainComp comp in compilers)
                {
                    TerrainDoOperation.Invoke(comp, new object[] { bedSpot, Vector3.zero, even });
                    TerrainBusy[comp] = Time.frameCount;
                    TerrainBusyAt[comp] = Time.time;
                }
                Mark(ModWorked, bedSpot, Mathf.Max(even.m_level ? even.m_levelRadius : 0f, even.m_smooth ? even.m_smoothRadius : 0f));
                smoothed++;
                if (bed.Grade)
                {
                    _gradesDone++;
                    // Checked from the next frame on (the heights are rebuilt some frames later): see GradeCheck.
                    if (_gradeLog < 40) Plugin.Log.LogInfo("grade at " + bed.Pos.ToString("0.0") + " to " + bed.Pos.y.ToString("0.00") + " (was " + bedGround.ToString("0.00") + ", r " + bed.Radius.ToString("0.0") + ", smooth " + even.m_smooth + ")");
                    _gradeLog++;
                }
            }

            // Shaping (level + blend) always goes first; the final flattening of every pad comes after all of it,
            // because the blend around one pad reaches onto the pads next to it.
            Queue<TerrainJob> queue = Terrain.Count > 0 ? Terrain : Flatten;
            if (queue.Count > 0)
            {
                // A job at the head of the line that has not gone anywhere for two minutes is dropped, and said
                // so: one job that can never run must not hold up every pad behind it.
                TerrainJob head = queue.Peek();
                if (head.Pos != _headPos || head.Radius != _headRadius)
                {
                    _headPos = head.Pos;
                    _headRadius = head.Radius;
                    _headSince = Time.time;
                }
                else if (Time.time - _headSince > 120f)
                {
                    queue.Dequeue();
                    Plugin.Log.LogWarning("Gave up ground work at " + head.Pos.ToString("0") + " (radius " + head.Radius.ToString("0.0") + (head.LevelOnly ? ", levelling" : ", shaping") + "): it could not run for two minutes; "
                        + (Compilers(head.Pos, head.Radius + 5f, compilers) ? "its terrain stayed busy" : "its terrain was not ready or not ours"));
                    _headSince = Time.time;
                    _gradeDrops++;
                    return;
                }
            }
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
                    if (Settling(comp)) return;
                }
                queue.Dequeue();
                // The last guard against pits: a levelling far from the land as the world made it is a
                // mistake somewhere upstream, and is not carried out.
                float land = NaturalHeight(job.Pos, job.LevelOnly ? job.Radius * 0.5f : job.Radius);
                if (Mathf.Abs(job.Pos.y - land) > MaxPadDrift + 1.5f && !AllowDeepDig)
                {
                    Plugin.Log.LogWarning("Refused to level ground at " + job.Pos.ToString("0") + " to " + job.Pos.y.ToString("0.0") + " m, " + (job.Pos.y - land).ToString("+0.0;-0.0") + " m off the land");
                    continue;
                }
                AllowDeepDig = false;
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
                    TerrainBusy[comp] = Time.frameCount;
                    TerrainBusyAt[comp] = Time.time;
                }
                if (!job.Seam) Mark(ModWorked, job.Pos, settings.m_smooth ? settings.m_smoothRadius : settings.m_levelRadius);
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

            int haveChief = 0, haveGuards = 0, haveVillagers = 0, haveEnvoys = 0;
            Vector3 post;
            float postYaw;
            bool tent = EnvoyPost.Wanted(s) && desired > 0 && EnvoyPost.PostOf(s, out post, out postYaw);
            foreach (ZDO zdo in FindInhabitants(s, centre))
            {
                NpcRole role = (NpcRole)zdo.GetInt(FactionMember.HashRole);
                bool wrongFaction = zdo.GetInt(FactionMember.HashFaction) != s.FactionId;
                if (role == NpcRole.Envoy)
                {
                    if (wrongFaction || !tent || haveEnvoys >= 1) RemoveNpc(zdo);
                    else haveEnvoys++;
                    continue;
                }
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

            if (tent && haveEnvoys == 0 && EnvoyPost.PostOf(s, out post, out postYaw)) SpawnNpc(s, post, NpcRole.Envoy, true);
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

        private static bool SpawnNpc(Settlement s, Vector3 centre, NpcRole role, bool exactly = false)
        {
            Faction faction = SimHost.Sim.State.GetFaction(s.FactionId);
            GameObject prefab = Npcs.PrefabFor(faction);
            if (prefab == null) return false;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            // In a war camp the tents stand round the fire from 5 m out: people come out by the fire, not on a roof.
            float dist = exactly ? 0f : s.CampaignId != 0 ? Random.Range(2.2f, 3.8f) : role == NpcRole.Chief ? Random.Range(3f, 5f) : Random.Range(4f, 8f);
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
