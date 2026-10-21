using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>One piece of a building in the building's own space: +Z is the front (faces the settlement centre).</summary>
    internal struct PieceSpec
    {
        public string Prefab;
        public Vector3 Pos;
        public float Yaw;
        /// <summary>A yard thing outside the walls (woodpile, cart, bench): left out where it would block a road or a path.</summary>
        public bool Yard;
        /// <summary>For a yard thing: how far it reaches from its origin across the ground.</summary>
        public float Reach;
    }

    /// <summary>Which vanilla pieces a culture builds with. All use the same 2 m module.</summary>
    internal class BuildStyle
    {
        public string Roof26 = "wood_roof", Roof45 = "wood_roof_45";
        public string RoofTop26 = "wood_roof_top", RoofTop45 = "wood_roof_top_45";
        public string Banner = "piece_banner01";
        public string Id = "wood";
        /// <summary>0 = timber wall, 1 = stone blocks (two courses), 2 = Fuling palisade wall.</summary>
        public int WallKind;
        /// <summary>Door piece, or null for an open doorway.</summary>
        public string Door = "wood_door";
        public string Floor = "wood_floor";
        public float FloorDrop;
        public string Pole = "wood_pole_log";
        public float PoleY = 1f;
        public bool Steep = true;
        /// <summary>A carving on the ridge of a home (darkwood_raven, darkwood_wolf), or null.</summary>
        public string Ornament;
        /// <summary>Huts and houses under a roof hipped on all four sides instead of a gable.</summary>
        public bool HipRoofs;
        /// <summary>A roof on two posts over the door of a home.</summary>
        public bool Porch;
    }

    /// <summary>
    /// Procedural buildings from vanilla build pieces. Dimensions come from the pieces' real snap
    /// points (dumped from the game, see MODLOG): walls and floors are 2 m modules, wall origins
    /// are at their centre, a 26-degree roof rises 1 m over 2 m and a 45-degree roof 2 m over 2 m,
    /// roofs slope down toward local +Z and gable pieces rise toward local +X.
    /// Pieces are listed in construction order: the first N of them are a believable building site.
    /// </summary>
    internal static class Blueprints
    {
        private const float FloorY = 0.25f;
        private const float WallBase = FloorY + 0.12f;

        private static readonly Dictionary<string, List<PieceSpec>> Cache = new Dictionary<string, List<PieceSpec>>();

        public static BuildStyle StyleFor(Faction faction, Tier tier)
        {
            BuildStyle style = new BuildStyle();
            int banner = 1 + (faction.ColorIndex % 11);
            style.Banner = "piece_banner" + banner.ToString("00");
            string group = Defs.Culture(faction.CultureId).Group;
            if (group == "draugr")
            {
                // The dead build in stone: block walls, flagstone floors, pillars and open doorways.
                style.Id = "stone";
                style.WallKind = 1;
                style.Door = null;
                style.Floor = "stone_floor_2x2";
                style.FloorDrop = -0.4f;
                style.Pole = "stone_pillar";
            }
            else if (group == "fuling")
            {
                // Fulings lash together rough stake walls and never bother with doors.
                style.Id = "goblin";
                style.WallKind = 2;
                style.Door = null;
                style.Pole = "goblin_pole";
                style.PoleY = 0.4f;
                // Their roofs are hides on pole frames, as in their camps: they come only at 45 degrees (GableRoof).
                style.Roof45 = ClothRoof;
            }
            else if (group == "greydwarf")
            {
                // Homes are dens among roots (GreydwarfDen); the rest stays open-fronted.
                style.Id = "root";
                style.Door = null;
            }
            if (group == "human")
            {
                switch (Defs.Culture(faction.CultureId).Id)
                {
                    case "clan_raven":
                        // The Raven Clan builds dark: shingled roofs, dark posts, a raven carved on each end of a ridge.
                        style.Id = "raven";
                        style.Roof26 = "darkwood_roof";
                        style.Roof45 = "darkwood_roof_45";
                        style.RoofTop26 = "darkwood_roof_top";
                        style.RoofTop45 = "darkwood_roof_top_45";
                        style.Pole = "darkwood_pole";
                        style.Ornament = "darkwood_raven";
                        break;
                    case "clan_wolf":
                        // The Wolf Clan lays log on log and hips its thatch on all sides; a wolf looks out from the top.
                        style.Id = "wolf";
                        style.WallKind = 3;
                        style.HipRoofs = true;
                        style.Ornament = "darkwood_wolf";
                        break;
                    case "clan_stag":
                        // The Stag Clan keeps the thatched gable and gives every door a porch.
                        style.Id = "stag";
                        style.Porch = true;
                        break;
                }
            }
            if (group == "dvergr")
            {
                // Dvergr roof their halls in dark shingles.
                style.Id = "dark";
                style.Roof26 = "darkwood_roof";
                style.Roof45 = "darkwood_roof_45";
                style.RoofTop26 = "darkwood_roof_top";
                style.RoofTop45 = "darkwood_roof_top_45";
            }
            return style;
        }

        /// <summary>Footprint radius used for levelling the ground and clearing vegetation.</summary>
        public static float Radius(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Campfire: return 5.5f;
                case BuildingKind.Hut: return 4f;
                case BuildingKind.House: return 5f;
                case BuildingKind.Longhouse: return 6.5f;
                case BuildingKind.GreatHall: return 8f;
                case BuildingKind.Farm: return 6.5f;
                case BuildingKind.Watchtower: return 4f;
                case BuildingKind.Palisade: return 0f;
                default: return 5f;
            }
        }

        public static List<PieceSpec> Get(BuildingKind kind, BuildStyle style, Biome biome, int variant)
        {
            string key = kind + "|" + style.Id + "|" + style.Banner + "|" + biome + "|" + (variant & 3);
            List<PieceSpec> list;
            if (Cache.TryGetValue(key, out list)) return list;
            Builder b = new Builder(style);
            switch (kind)
            {
                case BuildingKind.Campfire: Campfire(b, variant); break;
                case BuildingKind.Hut:
                case BuildingKind.House:
                case BuildingKind.Longhouse:
                    if (style.Id == "goblin") FulingHut(b, kind, variant);
                    else if (style.Id == "root") GreydwarfDen(b, kind, variant);
                    else if (kind == BuildingKind.Hut) Hut(b, variant);
                    else if (kind == BuildingKind.House) House(b, variant);
                    else Longhouse(b, variant);
                    break;
                case BuildingKind.Storehouse: Storehouse(b); break;
                case BuildingKind.Woodcutter: Woodcutter(b); break;
                case BuildingKind.Stonecutter: Stonecutter(b); break;
                case BuildingKind.Farm: Farm(b, biome); break;
                case BuildingKind.HunterLodge: HunterLodge(b); break;
                case BuildingKind.Smithy: Smithy(b); break;
                case BuildingKind.Watchtower: Watchtower(b); break;
                case BuildingKind.Market: Market(b); break;
                case BuildingKind.GreatHall: GreatHall(b, variant); break;
                case BuildingKind.Palisade: Palisade(b); break;
            }
            Cache[key] = b.Pieces;
            return b.Pieces;
        }

        private class Builder
        {
            public readonly List<PieceSpec> Pieces = new List<PieceSpec>();
            public readonly BuildStyle Style;

            public Builder(BuildStyle style) { Style = style; }

            public void Add(string prefab, float x, float y, float z, float yaw = 0f)
            {
                Pieces.Add(new PieceSpec { Prefab = prefab, Pos = new Vector3(x, y, z), Yaw = yaw });
            }

            /// <summary>A yard thing: put up only where it does not stand on a road or a path.</summary>
            public void Yard(string prefab, float x, float y, float z, float yaw, float reach)
            {
                Pieces.Add(new PieceSpec { Prefab = prefab, Pos = new Vector3(x, y, z), Yaw = yaw, Yard = true, Reach = reach });
            }

            /// <summary>
            /// A wall torch on the inside of the wall that runs through (x, z), facing the way (nx, nz) into
            /// the room: its iron plate against the wall's face, the torch out toward the room.
            /// </summary>
            public void Sconce(float x, float y, float z, float nx, float nz)
            {
                string wall = Style.WallKind == 1 ? "stone_wall_2x1" : Style.WallKind == 2 ? "goblin_woodwall_2m" : "woodwall";
                float half = HalfThickness(wall);
                // A torch that reaches past the top of the lower wall (2 m) also clears the half wall above it.
                if (y + SconceTop > WallBase + 2f) half = Mathf.Max(half, HalfThickness("wood_wall_half"));
                float off = half + SconceBack;
                Add("piece_walltorch", x + nx * off, y, z + nz * off, Mathf.Atan2(-nz, nx) * Mathf.Rad2Deg);
            }

            /// <summary>
            /// The faction's banner hung on the face of a post or wall: (x, z) is the post's axis (or a point on
            /// the wall's middle plane), reach how far its surface stands out from there, (nx, nz) the way the
            /// cloth faces, top the height of the top edge of the cloth.
            /// </summary>
            public void Banner(float x, float z, float reach, float top, float nx, float nz)
            {
                float len = Mathf.Sqrt(nx * nx + nz * nz);
                nx /= len;
                nz /= len;
                // The cloth hangs flat against the face with a finger's gap, its faces along its own +-X.
                float off = reach + BannerHalfThickness + 0.02f;
                Add(Style.Banner, x + nx * off, top - BannerAbove, z + nz * off, Mathf.Atan2(-nz, nx) * Mathf.Rad2Deg);
            }

            /// <summary>Floor of nx by nz tiles centred on the origin.</summary>
            public void Floor(int nx, int nz)
            {
                for (int j = 0; j < nz; j++)
                {
                    for (int i = 0; i < nx; i++) Add(Style.Floor, -nx + 1 + 2 * i, FloorY + Style.FloorDrop, -nz + 1 + 2 * j);
                }
            }

            public void CornerPoles(int nx, int nz, bool tall)
            {
                foreach (int sx in new[] { -1, 1 })
                {
                    foreach (int sz in new[] { -1, 1 })
                    {
                        Add(Style.Pole, sx * nx, WallBase + Style.PoleY, sz * nz);
                        if (tall) Add("wood_pole", sx * nx, WallBase + 2.5f, sz * nz);
                    }
                }
            }

            /// <summary>One 2 m wall bay in the culture's material, or its doorway.</summary>
            private void Wall(float x, float z, float yaw, bool door)
            {
                if (door)
                {
                    if (Style.Door != null) Add(Style.Door, x, WallBase + 1f, z, yaw);
                    else if (Style.WallKind == 1) Add("stone_arch", x, WallBase + 1.5f, z, yaw);
                    return;
                }
                if (Style.WallKind == 1)
                {
                    Add("stone_wall_2x1", x, WallBase + 0.5f, z, yaw);
                    Add("stone_wall_2x1", x, WallBase + 1.5f, z, yaw);
                }
                else if (Style.WallKind == 2) Add("goblin_woodwall_2m", x, WallBase, z, yaw);
                else if (Style.WallKind == 3) Logs(x, z, yaw, 0, 4);
                else Add("woodwall", x, WallBase + 1f, z, yaw);
            }

            /// <summary>Walls around an nx by nz bay rectangle. doorBay is the front bay holding the door, or -1 for an open front.</summary>
            public void Walls(int nx, int nz, bool tall, int doorBay, bool openFront = false)
            {
                float y = WallBase + 1f;
                for (int i = 0; i < nx; i++)
                {
                    float x = -nx + 1 + 2 * i;
                    Wall(x, -nz, 180f, false);
                    if (!openFront) Wall(x, nz, 0f, i == doorBay);
                }
                for (int j = 0; j < nz; j++)
                {
                    float z = -nz + 1 + 2 * j;
                    Wall(-nx, z, 90f, false);
                    Wall(nx, z, 90f, false);
                }
                if (!tall) return;
                float y2 = WallBase + 2.5f;
                for (int i = 0; i < nx; i++)
                {
                    float x = -nx + 1 + 2 * i;
                    if (Style.WallKind == 3)
                    {
                        Logs(x, -nz, 180f, 4, 6);
                        Logs(x, nz, 0f, 4, 6);
                        continue;
                    }
                    Add("wood_wall_half", x, y2, -nz, 180f);
                    Add("wood_wall_half", x, y2, nz);
                }
                for (int j = 0; j < nz; j++)
                {
                    float z = -nz + 1 + 2 * j;
                    if (Style.WallKind == 3)
                    {
                        Logs(-nx, z, 90f, 4, 6);
                        Logs(nx, z, 90f, 4, 6);
                        continue;
                    }
                    Add("wood_wall_half", -nx, y2, z, 90f);
                    Add("wood_wall_half", nx, y2, z, 90f);
                }
            }

            /// <summary>Courses from..to (0.5 m each, from the floor up) of a 2 m bay of log wall.</summary>
            private void Logs(float x, float z, float yaw, int from, int to)
            {
                for (int k = from; k < to; k++) Add("wood_wall_log", x, WallBase + 0.25f + 0.5f * k, z, yaw);
            }

            /// <summary>
            /// A roof hipped on all four sides over an nx by 2 bay rectangle: a 45-degree outer corner piece over each
            /// corner bay (its high corner toward the middle), plain 45-degree slopes along the long sides between them.
            /// No gable ends: the walls stop at wallTop all round.
            /// </summary>
            public void HipRoof(int nx, float wallTop)
            {
                string corner = Style.Roof45.StartsWith("darkwood") ? "darkwood_roof_ocorner_45" : "wood_roof_ocorner_45";
                foreach (int sx in new[] { -1, 1 })
                {
                    foreach (int sz in new[] { -1, 1 })
                    {
                        // The piece's high corner is its own (-1, -1): turned so it points at the middle of the roof.
                        float yaw = sx > 0 ? (sz > 0 ? 0f : 90f) : (sz > 0 ? 270f : 180f);
                        Add(corner, sx * (nx - 1), wallTop, sz, yaw);
                    }
                }
                for (int i = 1; i < nx - 1; i++)
                {
                    float x = -nx + 1 + 2 * i;
                    Add(Style.Roof45, x, wallTop + 1f, 1f, 0f);
                    Add(Style.Roof45, x, wallTop + 1f, -1f, 180f);
                }
            }

            /// <summary>The style's carving on each end of a ridge along X, nx bays from the middle, at the height given.</summary>
            public void RidgeOrnaments(float nx, float ridgeY)
            {
                if (Style.Ornament == null) return;
                Add(Style.Ornament, nx, ridgeY, 0f, 90f);
                Add(Style.Ornament, -nx, ridgeY, 0f, 270f);
            }

            /// <summary>A porch over a door in the front wall (at z): a 26-degree roof from the top of the wall, on two posts.</summary>
            public void Porch(float doorX, float z)
            {
                foreach (int side in new[] { -1, 1 }) Add("wood_pole", doorX + side * 0.95f, WallBase + 1f, z + 1.8f);
                Add("wood_roof", doorX, WallBase + 2f, z + 1f);
            }

            /// <summary>Gable roof with the ridge along X over an nx by nz bay rectangle (nz is 2 or 3).</summary>
            public void GableRoof(int nx, int nz, float wallTop, bool steep)
            {
                // The Fulings' hide roof has no 26-degree piece: their roofs are all steep.
                if (Style.Roof45 == ClothRoof) steep = true;
                string gable = steep ? "wood_wall_roof_45" : "wood_wall_roof";
                float rise = steep ? 2f : 1f;
                // Gable ends first, so the roof has something to sit on.
                foreach (int sx in new[] { -1, 1 })
                {
                    float x = sx * nx;
                    Add(gable, x, wallTop, nz - 1, 90f);
                    Add(gable, x, wallTop, -nz + 1, -90f);
                    if (nz == 3)
                    {
                        if (steep) Add("woodwall", x, wallTop + 1f, 0f, 90f);
                        else Add("wood_wall_half", x, wallTop + 0.5f, 0f, 90f);
                        Add(steep ? "wood_wall_roof_top_45" : "wood_wall_roof_top", x, wallTop + rise, 0f, 90f);
                    }
                }
                // The 45-degree piece has its origin at mid-height, the 26-degree one at its low edge, and so has the
                // Fulings' hide roof: the same 2 by 2 m slope falling toward +Z, from 0 to 2 m over its origin (its
                // solid box), where the wooden one goes from -1 to 1.
                float slopeY = steep && Style.Roof45 != ClothRoof ? wallTop + 1f : wallTop;
                for (int i = 0; i < nx; i++)
                {
                    float x = -nx + 1 + 2 * i;
                    Add(steep ? Style.Roof45 : Style.Roof26, x, slopeY, nz - 1, 0f);
                    Add(steep ? Style.Roof45 : Style.Roof26, x, slopeY, -nz + 1, 180f);
                    if (nz == 3) Add(steep ? Style.RoofTop45 : Style.RoofTop26, x, wallTop + rise, 0f);
                }
            }

            /// <summary>Open lean-to shed, nx bays wide and one bay deep, roof falling toward the front.</summary>
            public void LeanTo(int nx, float cx, float cz)
            {
                float y = WallBase + 1f;
                for (int i = 0; i < nx; i++)
                {
                    float x = cx - nx + 1 + 2 * i;
                    Add("wood_floor", x, FloorY, cz);
                }
                foreach (int sx in new[] { -1, 1 })
                {
                    Add("wood_pole_log", cx + sx * nx, y, cz + 1f);
                    Add("wood_pole", cx + sx * nx, WallBase + 2.5f, cz + 1f);
                }
                for (int i = 0; i < nx; i++)
                {
                    float x = cx - nx + 1 + 2 * i;
                    Add("woodwall", x, y, cz - 1f, 180f);
                }
                foreach (int sx in new[] { -1, 1 })
                {
                    Add("woodwall", cx + sx * nx, y, cz, 90f);
                    Add("wood_wall_roof", cx + sx * nx, WallBase + 2f, cz, -90f);
                }
                for (int i = 0; i < nx; i++) Add(Style.Roof26, cx - nx + 1 + 2 * i, WallBase + 2f, cz, 180f);
            }
        }

        // ------------------------------------------------------------------ buildings

        /// <summary>
        /// A war camp: tents round a fire, each turned its own way, banners of the faction on poles,
        /// sharpened stakes toward the outside, firewood, a cooking spit and torches. No two camps alike.
        /// </summary>
        public static List<PieceSpec> WarCamp(BuildStyle style, int seed)
        {
            string key = "warcamp|" + style.Id + "|" + style.Banner + "|" + (seed & 7);
            List<PieceSpec> list;
            if (Cache.TryGetValue(key, out list)) return list;
            Builder b = new Builder(style);
            System.Random rnd = new System.Random(seed & 7);
            b.Add("fire_pit", 0f, 0f, 0f);
            b.Add("piece_cookingstation", 0f, 0.05f, 0f, rnd.Next(360));
            b.Add("piece_logbench01", 0f, 0.15f, -2.3f);
            float turn = rnd.Next(360);
            int tents = 3 + rnd.Next(2);
            for (int k = 0; k < tents; k++)
            {
                float deg = turn + k * 360f / tents + (float)(rnd.NextDouble() - 0.5) * 40f;
                float r = 5.6f + (float)rnd.NextDouble() * 1.0f;
                float a = deg * Mathf.Deg2Rad;
                // The open end of the tent half toward the fire.
                Tent(b, Mathf.Sin(a) * r, Mathf.Cos(a) * r, deg + 90f + (float)(rnd.NextDouble() - 0.5) * 30f);
            }
            for (int k = 0; k < 2; k++)
            {
                // Midway between two tents (the first and second, the second and third), clear of both.
                float a = (turn + (2 * k + 1) * 180f / tents + (float)(rnd.NextDouble() - 0.5) * 30f) * Mathf.Deg2Rad;
                float x = Mathf.Sin(a) * 7.8f, z = Mathf.Cos(a) * 7.8f;
                b.Add("wood_pole_log_4", x, 2f, z);
                // On the side toward the fire, the top of the cloth just under the top of the pole (4.22 m).
                b.Banner(x, z, LogPoleRadius, 4.12f, -x, -z);
            }
            // Outside the gap between the last tent and the first, away from the banners.
            float stakes = turn + (2 * tents - 1) * 180f / tents + (float)(rnd.NextDouble() - 0.5) * 20f;
            for (int k = -1; k <= 1; k++)
            {
                float a = (stakes + k * 22f) * Mathf.Deg2Rad;
                b.Add("piece_sharpstakes", Mathf.Sin(a) * 9.5f, 0f, Mathf.Cos(a) * 9.5f, stakes + k * 22f + SharpStakesOut);
            }
            b.Yard("wood_stack", -3.2f + (float)rnd.NextDouble(), 0.55f, 2.6f, rnd.Next(360), WoodPile.HalfX);
            b.Add("piece_groundtorch_wood", 2.4f, 0.65f, 2.2f);
            b.Add("piece_groundtorch_wood", -2.6f, 0.65f, -1.4f);
            Cache[key] = b.Pieces;
            return b.Pieces;
        }

        /// <summary>
        /// The envoy's tent outside a settlement (EnvoyPost): an A-frame tent open toward local +Z, where the envoy
        /// stands, the faction's banner on a pole beside it, a bench for a visitor and two torches by the mouth.
        /// </summary>
        public static List<PieceSpec> EnvoyTent(BuildStyle style)
        {
            string key = "envoy|" + style.Id + "|" + style.Banner;
            List<PieceSpec> list;
            if (Cache.TryGetValue(key, out list)) return list;
            Builder b = new Builder(style);
            // Tent's own X is its ridge and its open ends: turned a quarter, they face +Z and -Z.
            Tent(b, 0f, 0f, -90f);
            b.Add("wood_pole_log_4", 3.0f, 2f, 1.4f);
            b.Banner(3.0f, 1.4f, LogPoleRadius, 4.12f, 0f, 1f);
            b.Add("piece_logbench01", -2.9f, 0.15f, 2.2f, 90f);
            b.Add("piece_groundtorch_wood", 1.5f, 0.65f, 2.6f);
            b.Add("piece_groundtorch_wood", -1.5f, 0.65f, 2.6f);
            Cache[key] = b.Pieces;
            return b.Pieces;
        }

        /// <summary>A 4 x 4 m A-frame tent of two pitched sides, a hide on the ground inside. Ridge along local X.</summary>
        private static void Tent(Builder b, float cx, float cz, float yawDeg)
        {
            float t = yawDeg * Mathf.Deg2Rad;
            System.Func<float, float, Vector2> at = (x, z) => new Vector2(cx + x * Mathf.Cos(t) + z * Mathf.Sin(t), cz - x * Mathf.Sin(t) + z * Mathf.Cos(t));
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector2 right = at(sx, 1f), left = at(sx, -1f);
                b.Add("wood_roof_45", right.x, 1.15f, right.y, yawDeg);
                b.Add("wood_roof_45", left.x, 1.15f, left.y, yawDeg + 180f);
            }
            Vector2 mid = at(0f, 0f);
            b.Add("rug_deer", mid.x, 0.05f, mid.y, yawDeg);
        }

        /// <summary>One yard thing: its prefab, how high its origin sits over the ground and its half size (local X, Z).</summary>
        private struct Thing
        {
            public string Prefab;
            public float Lift, HalfX, HalfZ;

            public Thing(string prefab, float lift, float halfX, float halfZ)
            {
                Prefab = prefab;
                Lift = lift;
                HalfX = halfX;
                HalfZ = halfZ;
            }
        }

        // Yard things (sizes from lf dump, the larger side of each). Only pieces that hold nothing and cost
        // little: no chests or pots (the settlement would fill them), no ingots (a hammer would take them down
        // for the metal), no sharpened stakes (they wound whoever brushes them).
        private static readonly Thing WoodPile = new Thing("wood_stack", 0.55f, 1.32f, 1.16f);
        private static readonly Thing ChoppingBlock = new Thing("piece_workbench_ext1", 0f, 0.71f, 0.36f);
        private static readonly Thing TanningRack = new Thing("piece_workbench_ext2", 0.04f, 1.21f, 0.96f);
        private static readonly Thing LogBench = new Thing("piece_logbench01", 0.15f, 1.36f, 0.32f);
        private static readonly Thing StonePile = new Thing("stone_pile", 0.2f, 0.89f, 1.33f);
        private static readonly Thing BonePile = new Thing("bone_stack", 0.06f, 0.57f, 0.54f);
        private static readonly Thing CoreWood = new Thing("wood_core_stack", 0.05f, 1.12f, 1.11f);
        private static readonly Thing Torch = new Thing("piece_groundtorch_wood", 0.65f, 0.06f, 0.06f);
        private static readonly Thing Root = new Thing("root11", 0f, 1.21f, 1.61f);
        private static readonly Thing RootCurl = new Thing("root07", 0.2f, 1.01f, 0.33f);

        /// <summary>What lies about the yards of a culture, the commonest first.</summary>
        private static Thing[] YardThings(string styleId)
        {
            switch (styleId)
            {
                case "goblin": return new[] { BonePile, WoodPile, StonePile, Torch };
                case "root": return new[] { RootCurl, Root, StonePile, BonePile };
                case "stone": return new[] { BonePile, StonePile, LogBench, Torch };
                default: return new[] { ChoppingBlock, LogBench, TanningRack, StonePile, CoreWood, Torch };
            }
        }

        /// <summary>How far from the building's origin the farthest corner of a thing reaches.</summary>
        private static float Reach(Thing t, float x, float z, float yawDeg)
        {
            float a = yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a), far = 0f;
            foreach (float u in new[] { -t.HalfX, t.HalfX })
            {
                foreach (float v in new[] { -t.HalfZ, t.HalfZ })
                {
                    float wx = x + u * c + v * s, wz = z - u * s + v * c;
                    far = Mathf.Max(far, Mathf.Sqrt(wx * wx + wz * wz));
                }
            }
            return far;
        }

        /// <summary>
        /// Puts the first of the things, from the given one on, that stays on the levelled pad (it is levelled
        /// 1.5 m past the building's radius); place gives a thing's spot as (x, turn, z). False when none fits.
        /// </summary>
        private static bool YardFit(Builder b, Thing[] things, int from, float pad, System.Func<Thing, Vector3> place, string not = null)
        {
            for (int i = 0; i < things.Length; i++)
            {
                Thing t = things[(from + i) % things.Length];
                if (t.Prefab == not) continue;
                Vector3 at = place(t);
                if (Reach(t, at.x, at.z, at.y) > pad) continue;
                b.Yard(t.Prefab, at.x, t.Lift, at.z, at.y, Mathf.Max(t.HalfX, t.HalfZ));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The yard of a square home halfWidth by halfDepth (to the middle of its walls): a woodpile along one
        /// side and a thing or two of its culture by the other side and at the back, picked by the house's own
        /// number and kept on its levelled pad. Nothing goes in front, where the door and the path to it are;
        /// things that would stand on a road or a path are left out when the house goes up (Materializer).
        /// </summary>
        private static void Yard(Builder b, BuildingKind kind, int variant, float halfWidth, float halfDepth)
        {
            Thing[] things = YardThings(b.Style.Id);
            float pad = Radius(kind) + 1.3f;
            float side = (variant & 1) == 0 ? 1f : -1f;
            // Out from the wall's face (0.28 m, 0.67 m for stone), with a hand's breadth to spare.
            float face = (b.Style.WallKind == 1 ? 0.67f : 0.28f) + 0.1f;
            // Along the side walls, turned so their long side lies along the wall, from the back corner forward.
            System.Func<float, float, System.Func<Thing, Vector3>> along = (sx, shift) => t =>
                new Vector3(sx * (halfWidth + face + t.HalfZ), 90f, -halfDepth + t.HalfX + shift);
            Thing[] piles = b.Style.Id == "root" ? things : new[] { WoodPile, CoreWood, ChoppingBlock };
            YardFit(b, piles, 0, pad, along(side, 0.1f + (variant & 2) * 0.2f));
            YardFit(b, things, variant >> 1, pad, along(-side, 0.3f));
            // At the back, facing away from the wall: off the middle on a house, at both corners of a bigger
            // home. Several spots, as some fall on a path or a road and are left out.
            System.Func<float, System.Func<Thing, Vector3>> back = sx => t =>
                new Vector3(sx * Mathf.Max(0f, halfWidth - t.HalfX - 0.6f), 180f, -(halfDepth + face + t.HalfZ));
            string beside = things[(variant >> 1) % things.Length].Prefab;
            if (halfWidth >= 3f) YardFit(b, things, variant + 2, pad, back(side), beside);
            if (halfWidth >= 4f) YardFit(b, things, variant + 3, pad, back(-side), beside);
            if (halfWidth < 3f) YardFit(b, new[] { ChoppingBlock, BonePile, Torch }, variant, pad, back(side));
        }

        /// <summary>The yard of a round Fuling or Greydwarf home of ring radius r: two things behind it, either side.</summary>
        private static void RoundYard(Builder b, BuildingKind kind, int variant, float r)
        {
            Thing[] things = YardThings(b.Style.Id);
            float pad = Radius(kind) + 1.3f;
            float side = (variant & 1) == 0 ? 1f : -1f;
            for (int k = 0; k < 2; k++)
            {
                // 35 and 60 degrees off straight behind (a little more in some), just outside the ring, turned
                // with its own Z outward so it stands out from the ring by its half depth.
                float deg = (k == 0 ? 35f : -60f) * side + (variant & 2) * 6f;
                float a = (270f + deg) * Mathf.Deg2Rad;
                YardFit(b, things, variant + k * 2, pad, t => new Vector3(Mathf.Cos(a) * (r + 0.4f + t.HalfZ), 90f - a * Mathf.Rad2Deg, Mathf.Sin(a) * (r + 0.4f + t.HalfZ)));
            }
        }

        /// <summary>
        /// The heart of the settlement: the fire that never goes out, benches round it, the faction's banner
        /// on its pole, the common woodpile with a chopping block, and a few things of the culture about the
        /// square, placed by the settlement's own number.
        /// </summary>
        private static void Campfire(Builder b, int variant)
        {
            b.Add("fire_pit_haldor", 0f, 0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 60f) * Mathf.Deg2Rad;
                // Benches sit on a circle, facing the fire.
                b.Add("piece_logbench01", Mathf.Sin(a) * 2.6f, 0.15f, Mathf.Cos(a) * 2.6f, i * 120f + 60f);
            }
            b.Add("wood_pole_log_4", 3.6f, 2f, -3.6f);
            b.Banner(3.6f, -3.6f, LogPoleRadius, 4.12f, -1f, 1f);
            b.Yard("wood_stack", -3.8f, 0.55f, -3.2f, 30f, WoodPile.HalfX);
            if (b.Style.Id != "root") b.Yard("piece_workbench_ext1", -1.75f, 0f, -4.6f, 15f, ChoppingBlock.HalfX);
            Thing[] things = YardThings(b.Style.Id);
            // Round the square's edge, between the benches' backs and where the paths come in.
            // Bearings clear of the benches' backs, the woodpile, the banner and the torches.
            float[] bearings = { 15f, 95f, 270f, 165f };
            for (int k = 0; k < 2; k++)
            {
                Thing t = things[(variant + k * 3) % things.Length];
                float deg = bearings[(variant + k * 2) % bearings.Length] + (variant & 2) * 4f;
                float a = deg * Mathf.Deg2Rad, reach = 4.6f + Mathf.Max(t.HalfX, t.HalfZ) * 0.5f;
                b.Yard(t.Prefab, Mathf.Sin(a) * reach, t.Lift, Mathf.Cos(a) * reach, deg + 90f, Mathf.Max(t.HalfX, t.HalfZ));
            }
            // Two torches light the square at night.
            b.Yard("piece_groundtorch_wood", -3.3f, 0.65f, 2.9f, 0f, 0.1f);
            b.Yard("piece_groundtorch_wood", 3.4f, 0.65f, 2.7f, 0f, 0.1f);
        }

        /// <summary>
        /// Fuling homes are round: a ring of their own stake walls with a gap for a door toward local +Z,
        /// under the great cone roof of their camps. No floor, a straw bed for every two it houses (one in
        /// a hut, two in a house, three and a heap of straw in the headman's hut), a chest, a standing torch. The radius of the
        /// ring (RoundRadius) is what the villagers walk to as the door.
        /// </summary>
        private static void FulingHut(Builder b, BuildingKind kind, int variant)
        {
            int n = Sides(kind);
            float r = RoundRadius(kind);
            for (int k = 1; k < n; k++)
            {
                float a = (90f + k * 360f / n) * Mathf.Deg2Rad;
                float yaw = Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a)) * Mathf.Rad2Deg;
                b.Add("goblin_woodwall_2m", Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r, yaw);
            }
            // Two posts frame the doorway.
            float gap = (90f + 180f / n) * Mathf.Deg2Rad;
            b.Add("goblin_pole_small", Mathf.Cos(gap) * r, -0.3f, Mathf.Sin(gap) * r);
            b.Add("goblin_pole_small", -Mathf.Cos(gap) * r, -0.3f, Mathf.Sin(gap) * r);
            b.Add("goblin_roof_cap", 0.05f, 2.95f, 0.4f);
            // The straw beds have the wooden bed's shape: 1.2 x 2.8 m, the hide over them 2.2 m wide.
            if (kind == BuildingKind.Hut)
            {
                b.Add("goblin_bed", 0f, 0.05f, -1.2f);
                b.Add("piece_chest_wood", 1.7f, 0.05f, 0.5f, -90f);
                b.Add("piece_groundtorch_wood", -1.6f, 0.65f, 1.0f);
            }
            else
            {
                // Two along the back, heads to the wall; in the headman's hut a third across the front and a
                // heap of straw by the left wall for two more (it lies flat, under the wall's foot).
                b.Add("goblin_bed", -1.15f, 0.05f, -1.55f);
                b.Add("goblin_bed", 1.15f, 0.05f, -1.55f);
                if (kind == BuildingKind.Longhouse)
                {
                    b.Add("goblin_bed", 0f, 0.05f, 1.05f, 90f);
                    b.Add("goblin_strawpile", -2.0f, 0f, 0.7f, 30f);
                    b.Add("piece_chest_wood", 2.4f, 0.05f, 0.8f, -90f);
                    b.Add("piece_groundtorch_wood", -2.6f, 0.65f, 1.9f);
                }
                else
                {
                    b.Add("piece_chest_wood", -2.4f, 0.05f, 0.8f, 90f);
                    b.Add("piece_groundtorch_wood", 2.3f, 0.65f, 0.6f);
                }
            }
            if (kind == BuildingKind.Longhouse)
            {
                // The headman's hut: a fire before it, a totem and a banner either side of the way in.
                b.Add("goblin_totempole", 2.3f, 0f, r + 1.4f, 180f);
                b.Add("goblin_banner", -2.5f, 0f, r + 1.2f, 180f);
            }
            RoundYard(b, kind, variant, r);
        }

        /// <summary>
        /// Greydwarfs den in the roots: a ring of twisted roots grown up round a hollow, roofed with the
        /// same great cone as the Fulings' huts but with no walls between the roots, a heap of straw to
        /// sleep on for every two it houses, and root tangles at the way in.
        /// </summary>
        private static void GreydwarfDen(Builder b, BuildingKind kind, int variant)
        {
            int n = Sides(kind) - 2;
            float r = RoundRadius(kind) - 0.3f;
            for (int k = 1; k < n; k++)
            {
                float a = (90f + k * 360f / n) * Mathf.Deg2Rad;
                // Each root turned its own way, so the ring does not look made.
                b.Add("Greydwarf_Root", Mathf.Cos(a) * r, -0.3f, Mathf.Sin(a) * r, k * 137f);
            }
            b.Add("goblin_roof_cap", 0.05f, 3.2f, 0.4f);
            // The heaps are flat (0.4 m) and spread into each other like one bed of straw over the floor.
            b.Add("goblin_strawpile", -0.3f, 0f, -0.9f);
            if (kind != BuildingKind.Hut) b.Add("goblin_strawpile", 1.1f, 0f, -0.6f, 40f);
            if (kind == BuildingKind.Longhouse)
            {
                b.Add("goblin_strawpile", -1.2f, 0f, 0.4f, 75f);
                b.Add("goblin_strawpile", 1.0f, 0f, 1.0f, 110f);
            }
            b.Add("piece_chest_wood", 1.7f, 0.05f, 0.9f, -90f);
            b.Add("root07", -2.2f, 0.2f, r + 1.2f, 40f);
            b.Add("root11", 2.0f, 0f, r + 0.9f, 200f);
            RoundYard(b, kind, variant, r + 0.4f);
        }

        private static int Sides(BuildingKind kind) { return kind == BuildingKind.Hut ? 10 : 12; }

        /// <summary>Radius of a round home's wall ring (a 2 m wall per side), 0 for square buildings.</summary>
        public static float RoundRadius(BuildingKind kind)
        {
            if (kind != BuildingKind.Hut && kind != BuildingKind.House && kind != BuildingKind.Longhouse) return 0f;
            return 1f / Mathf.Sin(Mathf.PI / Sides(kind));
        }

        /// <summary>
        /// The wall torch's iron plate lies 0.2 m behind its origin, along its own -X (measured in the game:
        /// "lf specimen"); the torch stands out along +X.
        /// </summary>
        private const float SconceBack = 0.21f;
        /// <summary>The top of the wall torch over its origin.</summary>
        private const float SconceTop = 0.62f;

        // piece_banner01..11 (lf dump): the cloth is 0.2 m thick along its own X, 1.42 m wide along Z, its top
        // edge 0.1 m above the origin and its bottom 2.93 m below it.
        private const float BannerHalfThickness = 0.1f;
        private const float BannerAbove = 0.1f;
        private const float BannerDrop = 2.93f;

        // Surfaces round the banner supports, from the meshes (lf dump).
        private const float LogPoleRadius = 0.28f;
        private const float PillarHalf = 0.66f;
        /// <summary>The thickest wall piece of a gable end (wood_wall_half), from its middle plane.</summary>
        private const float GableHalf = 0.29f;

        private static readonly Dictionary<string, float> Thickness = new Dictionary<string, float>();

        /// <summary>Half the thickness of a wall piece, from its mesh (0.15 m when the prefab is not to be had).</summary>
        public static float HalfThickness(string prefab)
        {
            float half;
            if (Thickness.TryGetValue(prefab, out half)) return half;
            half = 0.15f;
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (go != null)
            {
                Matrix4x4 toRoot = go.transform.worldToLocalMatrix;
                float min = float.MaxValue, max = float.MinValue;
                foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        float z = m.MultiplyPoint3x4(corner).z;
                        min = Mathf.Min(min, z);
                        max = Mathf.Max(max, z);
                    }
                }
                if (max > min) half = (max - min) * 0.5f;
            }
            Thickness[prefab] = half;
            return half;
        }

        // The wooden bed (lf dump): a solid frame 1.2 x 2.8 m along its own Z, with a hide over it that hangs
        // 0.5 m down either side (2.2 m wide). Frames are set flush against the walls; the hide may sink into
        // the wall. Two sleep in each bed (Defs.SleepersPerBed).
        private const float BedHalfWidth = 0.6f;
        private const float BedHalfLength = 1.4f;
        private const float BedHide = 1.1f;

        /// <summary>How far a wall's inner face stands from its middle line (its mesh): the room ends there.</summary>
        private static float WallFace(BuildStyle style)
        {
            return style.WallKind == 1 ? 0.67f : style.WallKind == 2 ? 0.19f : style.WallKind == 3 ? 0.3f : 0.28f;
        }

        /// <summary>
        /// A home's roof in its clan's way: hipped on all sides over a hut or a house (two bays deep) where the style
        /// says so, the carving then on the front wall beside the door (doorX), out of the logs under the eaves; else a
        /// gable, a carving at each end of its ridge, set against the gable's face near its peak (2 m over the walls on
        /// two bays, 3.15 m on three: the top piece). A carving lies level: on the top of a hipped roof it hung in the
        /// air over the slope, and 0.45 m out from a gable it stood clear of the house.
        /// </summary>
        private static void HomeRoof(Builder b, int nx, int nz, bool steep, float doorX)
        {
            float top = WallBase + 3f;
            if (b.Style.HipRoofs && nz == 2)
            {
                b.HipRoof(nx, top);
                if (b.Style.Ornament != null)
                {
                    // Beside the door on the side with more wall, its back against the logs' face, its ears under the eaves.
                    float side = doorX > 0f ? -1f : 1f;
                    b.Add(b.Style.Ornament, doorX + side * 1.5f, WallBase + 1.1f, nz + WallFace(b.Style) + CarvingBack(b.Style.Ornament));
                }
                return;
            }
            b.GableRoof(nx, nz, top, steep);
            // Its foot against the gable's outer face: the 45-degree gable is 0.24 m thick each way, the top piece of a
            // three-bay gable 0.30 m.
            if (steep && b.Style.Ornament != null) b.RidgeOrnaments(nx + (nz == 3 ? 0.30f : 0.24f) + CarvingBack(b.Style.Ornament), top + (nz == 3 ? 3.15f : 2f));
        }

        /// <summary>How far a carving's mesh reaches behind the point it is fixed by (lf dump): set that far off a face, it touches it.</summary>
        private static float CarvingBack(string prefab)
        {
            return prefab == "darkwood_wolf" ? 0.17f : prefab == "darkwood_raven" ? 0.07f : 0f;
        }

        /// <summary>
        /// A hut, 4 x 4 m: one bed across the back wall, the household chest by the front wall on the side
        /// away from the door, a wall torch over it. Draugr walls are so thick that a barrel stands in for the chest.
        /// </summary>
        private static void Hut(Builder b, int variant)
        {
            int door = variant & 1;
            float doorX = door == 0 ? -1f : 1f;
            b.Floor(2, 2);
            b.CornerPoles(2, 2, true);
            b.Walls(2, 2, true, door);
            HomeRoof(b, 2, 2, b.Style.Steep, doorX);
            if (b.Style.Porch) b.Porch(doorX, 2f);
            float inner = 2f - WallFace(b.Style);
            b.Add("bed", 0f, WallBase, -inner + BedHalfWidth + 0.02f, 90f);
            float hide = -inner + BedHalfWidth + 0.02f + BedHide;
            b.Sconce(-doorX, WallBase + 1.6f, 2f, 0f, -1f);
            // The chest (1.64 x 1.15 m) opens toward the room.
            if (inner - hide >= 1.17f) b.Add("piece_chest_wood", -doorX * 0.85f, WallBase, inner - 0.77f, 180f);
            else b.Add("piece_chest_barrel", -doorX * 0.85f, WallBase, inner - 0.57f, 180f);
            Yard(b, BuildingKind.Hut, variant, 2f, 2f);
        }

        /// <summary>A house, 6 x 4 m: a bed along each side wall, heads to the back, the chest between them, a torch over it.</summary>
        private static void House(Builder b, int variant)
        {
            b.Floor(3, 2);
            b.CornerPoles(3, 2, true);
            b.Walls(3, 2, true, 1);
            HomeRoof(b, 3, 2, b.Style.Steep, 0f);
            if (b.Style.Porch) b.Porch(0f, 2f);
            float w = WallFace(b.Style), innerX = 3f - w, innerZ = 2f - w;
            float bedZ = -innerZ + BedHalfLength + 0.02f;
            b.Add("bed", -innerX + BedHalfWidth + 0.02f, WallBase, bedZ);
            b.Add("bed", innerX - BedHalfWidth - 0.02f, WallBase, bedZ);
            // Turned on its side between the beds, opening toward the right-hand one.
            b.Add("piece_chest_wood", 0.18f, WallBase, -innerZ + 0.84f, 90f);
            b.Sconce(0f, WallBase + 1.6f, -2f, 0f, 1f);
            Yard(b, BuildingKind.House, variant, 3f, 2f);
        }

        /// <summary>
        /// A longhouse, 8 x 6 m: four beds, two along each side wall, heads to it; down the middle a table
        /// under the wall torches and the chest by the front wall left of the door. Between thick stone walls
        /// the middle is too narrow for the table, and a barrel stands in for the chest.
        /// </summary>
        private static void Longhouse(Builder b, int variant)
        {
            b.Floor(4, 3);
            b.CornerPoles(4, 3, true);
            b.Walls(4, 3, true, 2);
            HomeRoof(b, 4, 3, true, 1f);
            if (b.Style.Porch) b.Porch(1f, 3f);
            float w = WallFace(b.Style), innerX = 4f - w, innerZ = 3f - w;
            float bedX = innerX - BedHalfLength - 0.02f, bedZ = -innerZ + BedHalfWidth + 0.02f;
            foreach (int sx in new[] { -1, 1 })
            {
                b.Add("bed", sx * bedX, WallBase, bedZ, 90f);
                b.Add("bed", sx * bedX, WallBase, bedZ + 2f * BedHide, 90f);
            }
            // Between the feet of the beds.
            float aisle = bedX - BedHalfLength;
            if (aisle >= 0.7f)
            {
                b.Add("piece_table", 0f, WallBase, -innerZ + 1.25f, 90f);
                b.Add("piece_chest_wood", -0.05f, WallBase, innerZ - 0.84f, 90f);
            }
            else b.Add("piece_chest_barrel", 0f, WallBase, innerZ - 0.58f, 180f);
            b.Sconce(-1f, WallBase + 1.6f, -3f, 0f, 1f);
            b.Sconce(1f, WallBase + 1.6f, -3f, 0f, 1f);
            GableBanners(b, 4);
            Yard(b, BuildingKind.Longhouse, variant, 4f, 3f);
        }

        /// <summary>
        /// A banner on each gable end of a steep-roofed hall nx bays long (3 bays deep), in the middle where the
        /// gable runs highest: the top of the cloth under the ridge piece (5.66 m up at the cloth's edges) and
        /// past the eaves (they reach 0.15 m past the gable), its foot above the lower walls (stone ones stand
        /// 0.67 m proud of the gable and end 2.49 m up). The front wall has no room: its eaves come down to 2.7 m.
        /// </summary>
        private static void GableBanners(Builder b, int nx)
        {
            float top = WallBase + 5.2f;
            foreach (int sx in new[] { -1, 1 }) b.Banner(sx * nx, 0f, GableHalf, top, sx, 0f);
        }

        private static void Storehouse(Builder b)
        {
            b.Floor(3, 2);
            float y = WallBase + 1f;
            foreach (int x in new[] { -3, 0, 3 })
            {
                b.Add("wood_pole_log", x, y, 2f);
                b.Add("wood_pole", x, WallBase + 2.5f, 2f);
                b.Add("wood_pole_log", x, y, -2f);
            }
            b.Walls(3, 2, true, -1, true);
            b.GableRoof(3, 2, WallBase + 3f, false);
            // Against the back wall: a chest reaches 0.76 m behind its origin.
            float back = -2f + WallFace(b.Style) + 0.77f;
            b.Add("piece_chest_wood", -1.75f, WallBase, back);
            b.Add("piece_chest_wood", 0f, WallBase, back);
            b.Add("piece_chest_wood", 1.75f, WallBase, back);
            b.Yard("wood_stack", -4.5f, 0.6f, 0.4f, 0f, WoodPile.HalfX);
            b.Yard("stone_pile", 4.3f, 0.2f, 0.3f, 0f, StonePile.HalfZ);
        }

        private static void Woodcutter(Builder b)
        {
            b.LeanTo(2, 0f, -1.5f);
            // Under the roof a woodpile against the back wall (2.5 x 2.3 m, its front out in the open) and the
            // chopping block beside it.
            b.Add("wood_stack", -0.4f, WallBase + 0.6f, -1.06f);
            b.Add("piece_workbench_ext1", 1.35f, WallBase, -1.5f, 90f);
            b.Yard("wood_stack", -3.4f, 0.6f, 1.6f, 15f, WoodPile.HalfX);
            b.Yard("wood_stack", 3.3f, 0.6f, 1.2f, 100f, WoodPile.HalfX);
        }

        private static void Stonecutter(Builder b)
        {
            b.LeanTo(2, 0f, -1.5f);
            b.Add("piece_stonecutter", 0f, WallBase, -1.34f);
            b.Yard("stone_pile", -3.3f, 0.2f, 1.2f, 0f, StonePile.HalfZ);
            b.Yard("stone_pile", 3.2f, 0.2f, 0.8f, 70f, StonePile.HalfZ);
            b.Yard("stone_pile", 1.2f, 0.2f, 2.4f, 140f, StonePile.HalfZ);
        }

        private static void Farm(Builder b, Biome biome)
        {
            // Fenced field, 10 m by 8 m, with a gap in the front fence.
            for (int i = 0; i < 5; i++)
            {
                float x = -4f + 2f * i;
                b.Add("wood_fence", x + 0.1f, 0f, -4f);
                if (i != 2) b.Add("wood_fence", x + 0.1f, 0f, 4f);
            }
            for (int j = 0; j < 4; j++)
            {
                float z = -3f + 2f * j;
                b.Add("wood_fence", -5f, 0f, z - 0.1f, 90f);
                b.Add("wood_fence", 5f, 0f, z - 0.1f, 90f);
            }
            string[] crops = biome == Biome.Plains ? new[] { "Pickable_Barley", "Pickable_Flax" } : new[] { "Pickable_Carrot", "Pickable_Turnip" };
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    // The beehive takes the last plant's place in the front corner.
                    if (row == 3 && col == 5) continue;
                    b.Add(crops[row % 2], -3.5f + col * 1.4f, 0f, -2.4f + row * 1.5f);
                }
            }
            b.Add("piece_beehive", 3.6f, 0.15f, 2.85f, 180f);
        }

        private static void HunterLodge(Builder b)
        {
            b.Floor(2, 2);
            b.CornerPoles(2, 2, true);
            b.Walls(2, 2, true, 1);
            b.GableRoof(2, 2, WallBase + 3f, false);
            b.Add("rug_wolf", 0f, WallBase + 0.02f, 0f);
            b.Add("piece_chest_wood", -0.9f, WallBase, -2f + WallFace(b.Style) + 0.77f);
            b.Yard("fire_pit_haldor", -3.6f, 0f, 2.6f, 0f, 0.9f);
            b.Yard("piece_cookingstation", -3.6f, 0.2f, 2.6f, 45f, 0.9f);
        }

        private static void Smithy(Builder b)
        {
            b.Floor(3, 2);
            float y = WallBase + 1f;
            foreach (int x in new[] { -3, -1, 1, 3 })
            {
                b.Add("wood_pole_log", x, y, 2f);
                b.Add("wood_pole", x, WallBase + 2.5f, 2f);
            }
            b.Walls(3, 2, true, -1, true);
            b.GableRoof(3, 2, WallBase + 3f, false);
            b.Add("forge", -1f, WallBase, -1.1f);
            // The chest turned to face the room against the right-hand wall, the coal heaped between the posts.
            float w = WallFace(b.Style);
            b.Add("piece_chest_wood", 3f - w - 0.77f, WallBase, -2f + w + 0.84f, -90f);
            b.Add("coal_pile", 0f, WallBase + 0.1f, 0.75f);
            b.Add("bar_copper_stack", -2.3f, WallBase, 0.8f);
        }

        private static void Watchtower(Builder b)
        {
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 }) b.Add("wood_pole_log_4", sx * 2f, 2f, sz * 2f);
            }
            float deck = 4.15f;
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 }) b.Add("wood_floor", sx, deck, sz);
            }
            // Two stepladders reach the deck from the front.
            b.Add("wood_stepladder", 0f, 0.1f, 5f);
            b.Add("wood_stepladder", 0f, 2.1f, 3f);
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sz in new[] { -1, 1 }) b.Add("wood_pole2", sx * 2f, deck + 1.1f, sz * 2f);
            }
            float rail = deck + 0.15f;
            b.Add("wood_fence", -0.9f, rail, -2f);
            b.Add("wood_fence", 1.1f, rail, -2f);
            b.Add("wood_fence", -2f, rail, -1.1f, 90f);
            b.Add("wood_fence", -2f, rail, 0.9f, 90f);
            b.Add("wood_fence", 2f, rail, -1.1f, 90f);
            b.Add("wood_fence", 2f, rail, 0.9f, 90f);
            float top = deck + 2.1f;
            foreach (int x in new[] { -1, 1 })
            {
                b.Add(b.Style.Roof26, x, top, 1f);
                b.Add(b.Style.Roof26, x, top, -1f, 180f);
            }
            // On the front of the front right corner post, the cloth under the eaves (their edge is 6.25 m up at
            // 2 m out and falls further) and clear of the deck's edge and the side railing.
            b.Banner(2f, 2f, LogPoleRadius + 0.05f, deck + 1.6f, 0f, 1f);
        }

        private static void Market(Builder b)
        {
            b.LeanTo(2, -3f, -1.5f);
            b.LeanTo(1, 3.5f, -1.5f);
            // A counter in the wide stall, the trader's chest turned on its side in the narrow one.
            b.Add("piece_table", -3f, WallBase, -1.3f);
            b.Add("piece_chest_wood", 3.68f, WallBase, -1.4f, 90f);
            b.Add("wood_pole_log_4", 0.4f, 2f, 1.5f);
            b.Banner(0.4f, 1.5f, LogPoleRadius, 4.12f, 0f, 1f);
            // The cart stands before the wide stall, across, off the way in and clear of the banner.
            b.Yard("Cart", -2.8f, 0.3f, 1.6f, 90f, 1.7f);
        }

        private static void GreatHall(Builder b, int variant)
        {
            BuildStyle s = b.Style;
            string r45 = s.Roof45, rt45 = s.RoofTop45;
            s.Roof45 = "darkwood_roof_45";
            s.RoofTop45 = "darkwood_roof_top_45";
            b.Floor(6, 3);
            b.CornerPoles(6, 3, true);
            b.Walls(6, 3, true, 3);
            b.GableRoof(6, 3, WallBase + 3f, true);
            s.Roof45 = r45;
            s.RoofTop45 = rt45;
            // The throne at the west end facing down the hall, the hearth before it; the table along the
            // back wall with its bench behind it (the door is in the front wall, at x 0..2); the chief's
            // household sleeps at the east end; the chest in the west front corner.
            float w = WallFace(s), innerX = 6f - w, innerZ = 3f - w;
            b.Add("piece_throne01", -4.6f, WallBase, 0f, 90f);
            b.Add("fire_pit_haldor", -1.6f, WallBase, 1.1f);
            float benchZ = -innerZ + 0.4f, tableZ = benchZ + 0.95f;
            b.Add("piece_table", -2.2f, WallBase, tableZ);
            b.Add("piece_bench01", -2.2f, WallBase, benchZ);
            // Two beds heads to the east gable, a third beside them heads to the back wall.
            float bedX = innerX - BedHalfLength - 0.02f, bedZ = -innerZ + BedHalfWidth + 0.02f;
            b.Add("bed", bedX, WallBase, bedZ, 90f);
            b.Add("bed", bedX, WallBase, bedZ + 2f * BedHide, 90f);
            b.Add("bed", bedX - BedHalfLength - BedHide - 0.02f, WallBase, -innerZ + BedHalfLength + 0.02f);
            b.Add("piece_chest_wood", -innerX + 0.86f, WallBase, innerZ - 0.78f, 180f);
            foreach (int x in new[] { -4, -1, 2, 5 }) b.Sconce(x, WallBase + 2f, -3f, 0f, 1f);
            GableBanners(b, 6);
            b.Add("piece_groundtorch_wood", -1f, 0.7f, 4.6f);
            b.Add("piece_groundtorch_wood", 3f, 0.7f, 4.6f);
            Yard(b, BuildingKind.GreatHall, variant, 6f, 3f);
        }

        /// <summary>Ring wall; unlike other buildings every stake follows the ground at its own spot.</summary>
        /// <summary>
        /// The wall round a town or a city, with a gate wherever a road comes through it (or north and
        /// south when there are no roads). A town keeps a palisade of stakes at 60.5 m with two tall
        /// posts at each gate; a city raises a stone wall two courses high at 78 m, outside its outer
        /// ring of houses, with stone towers flying its banner either side of each gate. The Fulings and
        /// the Greydwarfs keep stakes even round a city. The signature tells when it has to be rebuilt.
        /// </summary>
        public static List<PieceSpec> Wall(BuildStyle style, Tier tier, List<float> gates, out string signature)
        {
            bool city = tier >= Tier.City;
            bool stone = city && style.Id != "goblin" && style.Id != "root";
            float radius = city ? Defs.CityWallRadius : Defs.PalisadeRadius;
            List<float> at = new List<float>();
            foreach (float g in gates)
            {
                // Roads that cross within 6.5 m of each other share a gate (the first one's); further apart
                // each has its own, or the second would run into the wall. Exactly where the road crosses, to
                // the tenth of a degree: at 78 m a whole degree is more than a metre off the road.
                float q = Mathf.Round(Mathf.Repeat(g, 360f) * 10f) / 10f;
                bool near = false;
                foreach (float other in at) if (Mathf.Abs(Mathf.DeltaAngle(other, q)) * Mathf.Deg2Rad * radius < 6.5f) near = true;
                if (!near) at.Add(q);
            }
            if (at.Count == 0) { at.Add(0f); at.Add(180f); }
            at.Sort();
            // The 2 marks the taller gate posts that the banners hang from since 0.7.0.
            signature = (stone ? "stone" : "stake") + (city ? "C" : "T") + "2:" + string.Join(",", at.ConvertAll(g => g.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).ToArray());

            Builder b = new Builder(style);
            float metresPerDeg = radius * Mathf.Deg2Rad;
            // A 6 m opening; the post (or tower) stands right at its edge, and the wall runs from the post
            // to the post of the next gate, cut into equal stretches so it closes up against both.
            float halfGap = 3f / metresPerDeg;
            float post = (stone ? 0.6f : 0.3f) / metresPerDeg;
            float wallFrom = (3f + (stone ? 1.2f : 0.5f)) / metresPerDeg;
            float step = stone ? 4f : 2f;
            for (int i = 0; i < at.Count; i++)
            {
                float start = at[i] + wallFrom;
                float end = (i + 1 < at.Count ? at[i + 1] : at[0] + 360f) - wallFrom;
                float arc = end - start;
                if (arc <= 0f) continue;
                int pieces = Mathf.CeilToInt(arc * metresPerDeg / step);
                for (int k = 0; k < pieces; k++)
                {
                    float deg = start + arc * (k + 0.5f) / pieces;
                    float rad = deg * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
                    if (stone)
                    {
                        // Two courses of 4 x 2 m blocks, 4 m of wall.
                        b.Add("stone_wall_4x2", p.x, 1.15f, p.z, deg);
                        b.Add("stone_wall_4x2", p.x, 3.15f, p.z, deg);
                    }
                    else b.Add("stake_wall", p.x, 0f, p.z, deg);
                }
            }
            foreach (float g in at)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float a = (g + side * (halfGap + post)) * Mathf.Deg2Rad;
                    float x = Mathf.Sin(a) * radius, z = Mathf.Cos(a) * radius;
                    // The banner hangs on the outer face of the post, toward the road coming in. The cloth is
                    // wider than the post, so it hangs above the wall beside it rather than through it.
                    if (stone)
                    {
                        // A tower of four pillars (8.27 m) over the 4.3 m wall.
                        for (int k = 0; k < 4; k++) b.Add("stone_pillar", x, 1.25f + 2f * k, z, g);
                        b.Banner(x, z, PillarHalf, 8.17f, Mathf.Sin(a), Mathf.Cos(a));
                    }
                    else
                    {
                        // A 4 m and a 2 m log (6.64 m) over the 3.4 m stakes.
                        b.Add("wood_pole_log_4", x, 2.2f, z);
                        b.Add("wood_pole_log", x, 5.53f, z);
                        b.Banner(x, z, LogPoleRadius, 6.54f, Mathf.Sin(a), Mathf.Cos(a));
                    }
                }
                if (style.Id == "goblin")
                {
                    // Fulings plant sharpened stakes before their gates.
                    float a = g * Mathf.Deg2Rad;
                    float r = radius + 3f;
                    float sx = Mathf.Cos(a), sz = -Mathf.Sin(a);
                    b.Add("piece_sharpstakes", Mathf.Sin(a) * r + sx * 6f, 0f, Mathf.Cos(a) * r + sz * 6f, g + SharpStakesOut);
                    b.Add("piece_sharpstakes", Mathf.Sin(a) * r - sx * 6f, 0f, Mathf.Cos(a) * r - sz * 6f, g + SharpStakesOut);
                }
            }
            return b.Pieces;
        }

        /// <summary>
        /// The sharpened stakes lean their points along their own -Z (the mesh reaches 1.8 m that way, 1 m the
        /// other): turned this much more than the way out, they point out at whoever comes.
        /// </summary>
        private const float SharpStakesOut = 180f;

        /// <summary>The hide roof of the Fuling camps, a 45-degree slope on a frame of poles.</summary>
        private const string ClothRoof = "goblin_roof_45d";

        private static void Palisade(Builder b)
        {
            float radius = Defs.PalisadeRadius;
            int count = Mathf.RoundToInt(2f * Mathf.PI * radius / 2f);
            for (int i = 0; i < count; i++)
            {
                float deg = i * 360f / count;
                // Two gateways, north and south.
                float off = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(deg, 0f)), Mathf.Abs(Mathf.DeltaAngle(deg, 180f)));
                if (off < 2.2f) continue;
                float a = deg * Mathf.Deg2Rad;
                b.Add("stake_wall", Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius, deg);
            }
        }
    }
}
