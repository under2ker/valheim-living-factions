using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LivingFactions;
using LivingFactions.Core;

namespace LivingFactions.BlueprintCheck
{
    /// <summary>
    /// Lays out every blueprint (each kind, culture, variant, war camp and wall) without the game and checks
    /// the pieces against each other with their real sizes, read from the game's own measurements (the
    /// dump.txt that `lf dump` writes): furniture and yard things must not stand in each other, in walls or
    /// posts or in doorways, banners must hang free and clear of the ground, every home needs as many beds
    /// as it houses people. Runs in a second; exit code 1 on any failure.
    /// Usage: dotnet run --project tools/BlueprintCheck -c Release [-- path/to/dump.txt] [-v]
    /// </summary>
    internal static class Program
    {
        private struct Box
        {
            public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        private class Placed
        {
            public string Prefab;
            public float X, Y, Z, Yaw;
            public Box Local;
            /// <summary>What stands solid, against walls and posts (a bed's frame without the hide over it).</summary>
            public Box Frame;
            public int Index;
        }

        private static readonly Dictionary<string, Box> Sizes = new Dictionary<string, Box>();
        private static readonly HashSet<string> Unknown = new HashSet<string>();
        private static readonly List<string> Failures = new List<string>();
        private static int _checks;
        private static bool _verbose;

        private static int Main(string[] args)
        {
            _verbose = args.Contains("-v");
            string dump = args.FirstOrDefault(a => !a.StartsWith("-"));
            if (dump == null)
            {
                string game = Environment.GetEnvironmentVariable("VALHEIM_INSTALL");
                if (string.IsNullOrEmpty(game)) game = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
                dump = Path.Combine(game, @"BepInEx\config\LivingFactions\dump.txt");
            }
            if (!File.Exists(dump))
            {
                System.Console.WriteLine("no dump.txt at " + dump + " (run tools/scenarios/probe-decor.txt once)");
                return 2;
            }
            ReadSizes(dump);
            Adjust();
            SeedWallThickness();

            BuildStyle[] styles = Styles();
            BuildingKind[] kinds = (BuildingKind[])Enum.GetValues(typeof(BuildingKind));
            foreach (BuildStyle style in styles)
            {
                foreach (BuildingKind kind in kinds)
                {
                    if (kind == BuildingKind.Palisade) continue;
                    for (int variant = 0; variant < 4; variant++)
                    {
                        List<PieceSpec> pieces = Blueprints.Get(kind, style, Biome.Meadows, variant);
                        CheckBuilding(kind + "/" + style.Id + "/v" + variant, kind, style, variant, pieces);
                    }
                }
                for (int seed = 0; seed < 8; seed++) CheckBuilding("WarCamp/" + style.Id + "/s" + seed, BuildingKind.Campfire, style, seed, Blueprints.WarCamp(style, seed));
                CheckBuilding("EnvoyTent/" + style.Id, BuildingKind.Campfire, style, 0, Blueprints.EnvoyTent(style));
                foreach (Tier tier in new[] { Tier.Town, Tier.City })
                {
                    string signature;
                    List<PieceSpec> wall = Blueprints.Wall(style, tier, new List<float> { 10f, 130f, 250f }, out signature);
                    CheckBuilding("Wall/" + style.Id + "/" + tier, BuildingKind.Palisade, style, 0, wall);
                }
            }

            foreach (string u in Unknown.OrderBy(n => n)) System.Console.WriteLine("  info  no size measured for " + u + " (not checked)");
            foreach (string f in Failures) System.Console.WriteLine("  FAIL  " + f);
            System.Console.WriteLine(_checks + " checks, " + Failures.Count + " failed");
            System.Console.WriteLine(Failures.Count == 0 ? "ALL BLUEPRINT CHECKS PASSED" : "BLUEPRINT CHECKS FAILED");
            return Failures.Count == 0 ? 0 : 1;
        }

        private static BuildStyle[] Styles()
        {
            BuildStyle wood = new BuildStyle { Id = "wood" };
            BuildStyle stone = new BuildStyle { Id = "stone", WallKind = 1, Door = null, Floor = "stone_floor_2x2", FloorDrop = -0.4f, Pole = "stone_pillar" };
            BuildStyle goblin = new BuildStyle { Id = "goblin", WallKind = 2, Door = null, Pole = "goblin_pole", PoleY = 0.4f, Roof45 = "goblin_roof_45d" };
            BuildStyle root = new BuildStyle { Id = "root", Door = null };
            BuildStyle dark = new BuildStyle { Id = "dark", Roof26 = "darkwood_roof", Roof45 = "darkwood_roof_45", RoofTop26 = "darkwood_roof_top", RoofTop45 = "darkwood_roof_top_45" };
            BuildStyle raven = new BuildStyle { Id = "raven", Roof26 = "darkwood_roof", Roof45 = "darkwood_roof_45", RoofTop26 = "darkwood_roof_top", RoofTop45 = "darkwood_roof_top_45", Pole = "darkwood_pole", Ornament = "darkwood_raven" };
            BuildStyle wolf = new BuildStyle { Id = "wolf", WallKind = 3, HipRoofs = true, Ornament = "darkwood_wolf" };
            BuildStyle stag = new BuildStyle { Id = "stag", Porch = true };
            return new[] { wood, stone, goblin, root, dark, raven, wolf, stag };
        }

        // ------------------------------------------------------------------ sizes

        private static readonly Regex Line = new Regex(@"^(\S+): mesh min\(([^)]*)\) max\(([^)]*)\)(.*)$");
        private static readonly Regex BoxRx = new Regex(@"\(([^)]*)\)\.\.\(([^)]*)\)");

        private static void ReadSizes(string path)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                Match m = Line.Match(raw.Trim());
                if (!m.Success) continue;
                float[] lo = Vec(m.Groups[2].Value), hi = Vec(m.Groups[3].Value);
                Box box = new Box { MinX = lo[0], MinY = lo[1], MinZ = lo[2], MaxX = hi[0], MaxY = hi[1], MaxZ = hi[2] };
                // Solid boxes, when the dump has them, are the truer footprint: meshes take in drapes and overhangs.
                int at = m.Groups[4].Value.IndexOf("boxes[", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string list = m.Groups[4].Value.Substring(at + 6);
                    list = list.Substring(0, list.IndexOf(']'));
                    bool any = false;
                    Box solid = new Box();
                    foreach (Match b in BoxRx.Matches(list))
                    {
                        float[] a = Vec(b.Groups[1].Value), c = Vec(b.Groups[2].Value);
                        Box one = new Box
                        {
                            MinX = Math.Min(a[0], c[0]), MinY = Math.Min(a[1], c[1]), MinZ = Math.Min(a[2], c[2]),
                            MaxX = Math.Max(a[0], c[0]), MaxY = Math.Max(a[1], c[1]), MaxZ = Math.Max(a[2], c[2])
                        };
                        solid = any ? Union(solid, one) : one;
                        any = true;
                    }
                    if (any && UsesSolid(m.Groups[1].Value)) box = solid;
                }
                Sizes[m.Groups[1].Value] = box;
            }
        }

        /// <summary>Furniture is measured by what stands solid; structure by its mesh, which is what one sees.</summary>
        private static bool UsesSolid(string prefab)
        {
            return prefab.StartsWith("rug_") || prefab == "piece_table" || prefab == "piece_bench01" || prefab == "piece_logbench01" || prefab == "piece_chair";
        }

        private static readonly Dictionary<string, Box> Frames = new Dictionary<string, Box>();

        /// <summary>Sizes the dump cannot tell well.</summary>
        private static void Adjust()
        {
            // A bed: the frame stands solid (1.2 x 2.8 m); the hide over it hangs 0.5 m down either side.
            foreach (string bed in new[] { "bed", "goblin_bed" })
            {
                Sizes[bed] = new Box { MinX = -1.1f, MinY = -0.05f, MinZ = -1.45f, MaxX = 1.1f, MaxY = 0.7f, MaxZ = 1.45f };
                Frames[bed] = new Box { MinX = -0.6f, MinY = -0.05f, MinZ = -1.4f, MaxX = 0.6f, MaxY = 0.65f, MaxZ = 1.4f };
            }
            // Haldor's everlasting fire is not in the dump; it is the size of a fire pit.
            Box pit;
            if (!Sizes.ContainsKey("fire_pit_haldor") && Sizes.TryGetValue("fire_pit", out pit)) Sizes["fire_pit_haldor"] = pit;
        }

        private static Box Union(Box a, Box b)
        {
            return new Box
            {
                MinX = Math.Min(a.MinX, b.MinX), MinY = Math.Min(a.MinY, b.MinY), MinZ = Math.Min(a.MinZ, b.MinZ),
                MaxX = Math.Max(a.MaxX, b.MaxX), MaxY = Math.Max(a.MaxY, b.MaxY), MaxZ = Math.Max(a.MaxZ, b.MaxZ)
            };
        }

        private static float[] Vec(string s)
        {
            return s.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        }

        /// <summary>The game measures wall thickness for wall torches; give the blueprints the same numbers.</summary>
        private static void SeedWallThickness()
        {
            Dictionary<string, float> thickness = (Dictionary<string, float>)typeof(Blueprints).GetField("Thickness", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (string wall in new[] { "woodwall", "stone_wall_2x1", "goblin_woodwall_2m", "wood_wall_half" })
            {
                Box b;
                thickness[wall] = Sizes.TryGetValue(wall, out b) ? (b.MaxZ - b.MinZ) * 0.5f : 0.15f;
            }
        }

        // ------------------------------------------------------------------ what a piece is

        private static bool IsRoof(string p)
        {
            return p.Contains("roof") && !p.Contains("wall_roof") || p == "goblin_roof_cap";
        }

        private static bool IsStructure(string p)
        {
            if (p == "piece_walltorch" || p == "goblin_totempole") return false;
            return p.Contains("wall") || p.Contains("floor") || p.Contains("pole") || p.Contains("pillar") || p.Contains("roof")
                || p.Contains("door") || p.Contains("fence") || p.Contains("arch") || p.Contains("stepladder") || p == "Greydwarf_Root"
                || p.StartsWith("root");
        }

        private static bool IsBanner(string p) { return p.StartsWith("piece_banner"); }

        private static bool IsRug(string p) { return p.StartsWith("rug_"); }

        /// <summary>Pairs meant to share room: chairs and benches drawn up to a table, a spit over a fire.</summary>
        private static bool Together(string a, string b)
        {
            Func<string, bool> seat = p => p == "piece_chair" || p == "piece_bench01" || p.StartsWith("piece_chair");
            Func<string, bool> table = p => p == "piece_table" || p.StartsWith("piece_table");
            if (seat(a) && table(b) || seat(b) && table(a)) return true;
            Func<string, bool> fire = p => p.StartsWith("fire_pit");
            if (a == "piece_cookingstation" && fire(b) || b == "piece_cookingstation" && fire(a)) return true;
            return IsRug(a) || IsRug(b) || a == "goblin_strawpile" || b == "goblin_strawpile";
        }

        // ------------------------------------------------------------------ checks

        private static void CheckBuilding(string name, BuildingKind kind, BuildStyle style, int variant, List<PieceSpec> specs)
        {
            // Sharpened stakes point out: their points lean along their own -Z, which must face away from the centre.
            foreach (PieceSpec spec in specs)
            {
                if (spec.Prefab != "piece_sharpstakes") continue;
                _checks++;
                float yaw = spec.Yaw * (float)Math.PI / 180f;
                float px = -(float)Math.Sin(yaw), pz = -(float)Math.Cos(yaw);
                if (px * spec.Pos.x + pz * spec.Pos.z <= 0f) Failures.Add(name + ": sharpened stakes at (" + spec.Pos.x.ToString("0.0") + ", " + spec.Pos.z.ToString("0.0") + ") point in, not out");
            }
            List<Placed> pieces = new List<Placed>();
            for (int i = 0; i < specs.Count; i++)
            {
                PieceSpec s = specs[i];
                Box local;
                if (!Sizes.TryGetValue(s.Prefab, out local))
                {
                    Unknown.Add(s.Prefab);
                    continue;
                }
                Box frame;
                if (!Frames.TryGetValue(s.Prefab, out frame)) frame = local;
                pieces.Add(new Placed { Prefab = s.Prefab, X = s.Pos.x, Y = s.Pos.y, Z = s.Pos.z, Yaw = s.Yaw, Local = local, Frame = frame, Index = i });
            }
            List<Placed> fixtures = pieces.Where(p => !IsStructure(p.Prefab)).ToList();
            // Roots grow every which way: their bounds say nothing about where they are solid.
            List<Placed> solid = pieces.Where(p => IsStructure(p.Prefab) && !IsRoof(p.Prefab) && !p.Prefab.Contains("floor") && p.Prefab != "Greydwarf_Root" && !p.Prefab.StartsWith("root")).ToList();

            // Things in each other.
            for (int i = 0; i < fixtures.Count; i++)
            {
                for (int j = i + 1; j < fixtures.Count; j++)
                {
                    if (Together(fixtures[i].Prefab, fixtures[j].Prefab)) continue;
                    Expect(!Overlap(fixtures[i], fixtures[j], 0.03f), name + ": " + Label(fixtures[i]) + " stands in " + Label(fixtures[j]));
                }
                Placed f = fixtures[i];
                Placed frame = new Placed { Prefab = f.Prefab, X = f.X, Y = f.Y, Z = f.Z, Yaw = f.Yaw, Local = f.Frame, Index = f.Index };
                foreach (Placed s in solid)
                {
                    // A heap of straw is a flat bed on the floor; it may run under the foot of a wall.
                    if (f.Prefab == "goblin_strawpile") continue;
                    // Stone walls are 1.3 m thick: what sinks a little into them is still hidden.
                    float give = IsBanner(f.Prefab) ? 0.01f : s.Prefab.StartsWith("stone_") ? 0.2f : 0.05f;
                    Expect(!Overlap(frame, s, give), name + ": " + Label(f) + " stands in " + Label(s));
                }
            }

            // Banners hang clear of the ground.
            foreach (Placed b in fixtures.Where(p => IsBanner(p.Prefab)))
            {
                float bottom = b.Y + b.Local.MinY;
                Expect(bottom >= 0.6f, name + ": " + Label(b) + " trails on the ground (foot " + bottom.ToString("0.00", CultureInfo.InvariantCulture) + " m)");
            }

            // Doorways stay open.
            Door door;
            if (DoorOf(kind, style, variant, out door))
            {
                foreach (Placed f in fixtures)
                {
                    // Rugs and straw are walked over; whatever hangs higher than a head (banners, ridge carvings) is above the way in.
                    if (IsRug(f.Prefab) || f.Prefab == "goblin_strawpile" || f.Y + f.Local.MinY > 2.3f) continue;
                    Expect(!InZone(f, door), name + ": " + Label(f) + " is in the doorway");
                }
            }

            // As many beds as the home houses people.
            if (kind == BuildingKind.Hut || kind == BuildingKind.House || kind == BuildingKind.Longhouse || kind == BuildingKind.GreatHall)
            {
                int beds = specs.Count(s => s.Prefab == "bed" || s.Prefab == "goblin_bed" || s.Prefab == "goblin_strawpile") * Defs.SleepersPerBed;
                int housing = Defs.Buildings[kind].Housing;
                Expect(beds == housing, name + ": " + beds / Defs.SleepersPerBed + " beds sleep " + beds + ", it houses " + housing);
            }

            // Things outside stay on the levelled pad.
            float radius = Blueprints.Radius(kind == BuildingKind.Campfire && name.StartsWith("WarCamp") ? BuildingKind.Campfire : kind);
            if (radius > 0f && !name.StartsWith("WarCamp"))
            {
                foreach (Placed f in fixtures)
                {
                    // A carving on a ridge reaches out high above the ground: no pad under it is needed.
                    if (f.Y + f.Local.MinY > 2.3f) continue;
                    float reach = Reach(f);
                    // The pad is levelled PadMargin (1.5 m) past the radius.
                    Expect(reach <= radius + 1.45f, name + ": " + Label(f) + " reaches " + reach.ToString("0.0", CultureInfo.InvariantCulture) + " m out, off the pad (" + radius + " m)");
                }
            }
            if (_verbose) System.Console.WriteLine("  ok    " + name + " (" + specs.Count + " pieces)");
        }

        private static void Expect(bool ok, string failure)
        {
            _checks++;
            if (!ok && !Failures.Contains(failure)) Failures.Add(failure);
        }

        private static string Label(Placed p)
        {
            return p.Prefab + "#" + p.Index + " (" + p.X.ToString("0.0#", CultureInfo.InvariantCulture) + ", " + p.Z.ToString("0.0#", CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>Corners of a piece's footprint in building space (Unity yaw: local +X goes to (cos, -sin)).</summary>
        private static float[][] Corners(Placed p, float shrink)
        {
            float t = p.Yaw * (float)Math.PI / 180f, c = (float)Math.Cos(t), s = (float)Math.Sin(t);
            float x0 = p.Local.MinX + shrink, x1 = p.Local.MaxX - shrink, z0 = p.Local.MinZ + shrink, z1 = p.Local.MaxZ - shrink;
            if (x1 < x0) x0 = x1 = (p.Local.MinX + p.Local.MaxX) * 0.5f;
            if (z1 < z0) z0 = z1 = (p.Local.MinZ + p.Local.MaxZ) * 0.5f;
            float[][] local = { new[] { x0, z0 }, new[] { x1, z0 }, new[] { x1, z1 }, new[] { x0, z1 } };
            return local.Select(v => new[] { p.X + v[0] * c + v[1] * s, p.Z - v[0] * s + v[1] * c }).ToArray();
        }

        private static float Reach(Placed p)
        {
            return Corners(p, 0f).Max(v => (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1]));
        }

        private static bool Overlap(Placed a, Placed b, float shrink)
        {
            if (a.Y + a.Local.MaxY - shrink <= b.Y + b.Local.MinY + shrink || b.Y + b.Local.MaxY - shrink <= a.Y + a.Local.MinY + shrink) return false;
            if (IsLog(b.Prefab)) return CircleHits(a, b, shrink);
            if (IsLog(a.Prefab)) return CircleHits(b, a, shrink);
            float[][] p = Corners(a, shrink), q = Corners(b, shrink);
            return !Separated(p, q) && !Separated(q, p);
        }

        /// <summary>Round posts: their square bounds would reach 40% further out across the corners.</summary>
        private static bool IsLog(string p)
        {
            return p == "wood_pole_log" || p == "wood_pole_log_4" || p == "wood_pole" || p == "wood_pole2" || p == "goblin_pole_small";
        }

        private static bool CircleHits(Placed box, Placed log, float shrink)
        {
            float r = Math.Max(log.Local.MaxX - log.Local.MinX, log.Local.MaxZ - log.Local.MinZ) * 0.5f - shrink;
            // The post's centre in the box's own space.
            float t = box.Yaw * (float)Math.PI / 180f, c = (float)Math.Cos(t), s = (float)Math.Sin(t);
            float dx = log.X - box.X, dz = log.Z - box.Z;
            float lx = dx * c - dz * s, lz = dx * s + dz * c;
            float nx = Math.Max(box.Local.MinX + shrink, Math.Min(lx, box.Local.MaxX - shrink));
            float nz = Math.Max(box.Local.MinZ + shrink, Math.Min(lz, box.Local.MaxZ - shrink));
            return (lx - nx) * (lx - nx) + (lz - nz) * (lz - nz) < r * r;
        }

        /// <summary>Separating axis test on the edges of the first rectangle.</summary>
        private static bool Separated(float[][] p, float[][] q)
        {
            for (int i = 0; i < 4; i++)
            {
                float[] a = p[i], b = p[(i + 1) % 4];
                float nx = -(b[1] - a[1]), nz = b[0] - a[0];
                float pMin = float.MaxValue, pMax = float.MinValue, qMin = float.MaxValue, qMax = float.MinValue;
                foreach (float[] v in p) { float d = v[0] * nx + v[1] * nz; pMin = Math.Min(pMin, d); pMax = Math.Max(pMax, d); }
                foreach (float[] v in q) { float d = v[0] * nx + v[1] * nz; qMin = Math.Min(qMin, d); qMax = Math.Max(qMax, d); }
                if (pMax <= qMin || qMax <= pMin) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ doorways

        private struct Door
        {
            public float X, Z, HalfWidth, Before, After;
        }

        /// <summary>The way in (as FactionWork.DoorPoints and the blueprints put it): a lane through the door's bay.</summary>
        private static bool DoorOf(BuildingKind kind, BuildStyle style, int variant, out Door door)
        {
            // A lane 1.2 m wide (room for one to pass), from 1.3 m inside the door to 1.6 m out.
            door = new Door { HalfWidth = 0.6f, Before = 1.3f, After = 1.6f };
            bool round = (style.Id == "goblin" || style.Id == "root") && Blueprints.RoundRadius(kind) > 0f;
            if (round)
            {
                door.X = 0f;
                door.Z = Blueprints.RoundRadius(kind);
                return true;
            }
            switch (kind)
            {
                case BuildingKind.Hut: door.X = (variant & 1) == 0 ? -1f : 1f; door.Z = 2f; return true;
                case BuildingKind.House: door.X = 0f; door.Z = 2f; return true;
                case BuildingKind.HunterLodge: door.X = 1f; door.Z = 2f; return true;
                case BuildingKind.Longhouse:
                case BuildingKind.GreatHall: door.X = 1f; door.Z = 3f; return true;
            }
            return false;
        }

        private static bool InZone(Placed f, Door d)
        {
            float[][] c = Corners(f, 0.03f);
            float minX = c.Min(v => v[0]), maxX = c.Max(v => v[0]), minZ = c.Min(v => v[1]), maxZ = c.Max(v => v[1]);
            return maxX > d.X - d.HalfWidth && minX < d.X + d.HalfWidth && maxZ > d.Z - d.Before && minZ < d.Z + d.After;
        }
    }
}
