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
            }
            else if (group == "greydwarf")
            {
                style.Id = "open";
                style.Door = null;
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
                case BuildingKind.Campfire: Campfire(b); break;
                case BuildingKind.Hut: Hut(b, variant); break;
                case BuildingKind.House: House(b, variant); break;
                case BuildingKind.Longhouse: Longhouse(b); break;
                case BuildingKind.Storehouse: Storehouse(b); break;
                case BuildingKind.Woodcutter: Woodcutter(b); break;
                case BuildingKind.Stonecutter: Stonecutter(b); break;
                case BuildingKind.Farm: Farm(b, biome); break;
                case BuildingKind.HunterLodge: HunterLodge(b); break;
                case BuildingKind.Smithy: Smithy(b); break;
                case BuildingKind.Watchtower: Watchtower(b); break;
                case BuildingKind.Market: Market(b); break;
                case BuildingKind.GreatHall: GreatHall(b); break;
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
                    Add("wood_wall_half", x, y2, -nz, 180f);
                    Add("wood_wall_half", x, y2, nz);
                }
                for (int j = 0; j < nz; j++)
                {
                    float z = -nz + 1 + 2 * j;
                    Add("wood_wall_half", -nx, y2, z, 90f);
                    Add("wood_wall_half", nx, y2, z, 90f);
                }
            }

            /// <summary>Gable roof with the ridge along X over an nx by nz bay rectangle (nz is 2 or 3).</summary>
            public void GableRoof(int nx, int nz, float wallTop, bool steep)
            {
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
                // The 45-degree piece has its origin at mid-height, the 26-degree one at its low edge.
                float slopeY = steep ? wallTop + 1f : wallTop;
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

        private static void Campfire(Builder b)
        {
            b.Add("fire_pit_haldor", 0f, 0f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 60f) * Mathf.Deg2Rad;
                // Benches sit on a circle, facing the fire.
                b.Add("piece_logbench01", Mathf.Sin(a) * 2.6f, 0.15f, Mathf.Cos(a) * 2.6f, i * 120f + 60f);
            }
            b.Add("wood_pole_log_4", 3.6f, 2f, -3.6f);
            b.Add(b.Style.Banner, 3.95f, 4.1f, -3.6f);
            b.Add("wood_stack", -3.8f, 0.55f, -3.2f, 30f);
        }

        private static void Hut(Builder b, int variant)
        {
            int door = variant & 1;
            b.Floor(2, 2);
            b.CornerPoles(2, 2, true);
            b.Walls(2, 2, true, door);
            b.GableRoof(2, 2, WallBase + 3f, b.Style.Steep);
            // Bed on the side away from the door.
            b.Add("bed", door == 0 ? 0.75f : -0.75f, WallBase, -0.05f);
            b.Add("piece_walltorch", door == 0 ? 1f : -1f, WallBase + 1.6f, 1.7f, 180f);
            // The household chest stands against the back wall on the door side, clear of the bed.
            b.Add("piece_chest_wood", door == 0 ? -1.2f : 1.2f, WallBase, -0.9f, door == 0 ? 90f : -90f);
        }

        private static void House(Builder b, int variant)
        {
            b.Floor(3, 2);
            b.CornerPoles(3, 2, true);
            b.Walls(3, 2, true, 1);
            b.GableRoof(3, 2, WallBase + 3f, b.Style.Steep);
            b.Add("bed", -1.75f, WallBase, -0.05f);
            b.Add("piece_table", 1.9f, WallBase, -1.1f);
            b.Add("piece_chair", 1.3f, WallBase, -0.1f);
            b.Add("piece_chair", 2.3f, WallBase, -0.1f);
            b.Add("piece_chest_wood", 2.1f, WallBase, 1.35f, 180f);
            b.Add("piece_walltorch", 0f, WallBase + 1.6f, -1.75f);
        }

        private static void Longhouse(Builder b)
        {
            b.Floor(4, 3);
            b.CornerPoles(4, 3, true);
            b.Walls(4, 3, true, 2);
            b.GableRoof(4, 3, WallBase + 3f, true);
            b.Add("fire_pit_haldor", 0f, WallBase, 0f);
            b.Add("bed", -2.85f, WallBase, -1f);
            b.Add("bed", -2.85f, WallBase, 0.9f, 180f);
            b.Add("bed", 2.85f, WallBase, -1f);
            b.Add("piece_table", 0f, WallBase, -2.1f);
            b.Add("piece_bench01", 0f, WallBase, -1.3f, 180f);
            b.Add("piece_chest_wood", 2.9f, WallBase, 2.3f, 180f);
            b.Add("piece_walltorch", -1f, WallBase + 1.6f, -2.75f);
            b.Add("piece_walltorch", 1f, WallBase + 1.6f, -2.75f);
            b.Add(b.Style.Banner, 2.3f, WallBase + 3.4f, 3.25f, 90f);
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
            b.Add("piece_chest_wood", -1.9f, WallBase, -1.3f);
            b.Add("piece_chest_wood", 0f, WallBase, -1.3f);
            b.Add("piece_chest_wood", 1.9f, WallBase, -1.3f);
            b.Add("wood_stack", -4.5f, 0.6f, 0.4f);
            b.Add("stone_pile", 4.3f, 0.2f, 0.3f);
        }

        private static void Woodcutter(Builder b)
        {
            b.LeanTo(2, 0f, -1.5f);
            b.Add("wood_stack", -1f, WallBase + 0.6f, -1.6f, 90f);
            b.Add("wood_core_stack", 1f, WallBase + 0.15f, -1.6f);
            b.Add("wood_stack", -3.4f, 0.6f, 1.6f, 15f);
            b.Add("wood_stack", 3.3f, 0.6f, 1.2f, 100f);
        }

        private static void Stonecutter(Builder b)
        {
            b.LeanTo(2, 0f, -1.5f);
            b.Add("piece_stonecutter", 0f, WallBase, -1.6f);
            b.Add("stone_pile", -3.3f, 0.2f, 1.2f);
            b.Add("stone_pile", 3.2f, 0.2f, 0.8f, 70f);
            b.Add("stone_pile", 1.2f, 0.2f, 2.4f, 140f);
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
                    b.Add(crops[row % 2], -3.5f + col * 1.4f, 0f, -2.4f + row * 1.5f);
                }
            }
            b.Add("piece_beehive", 3.6f, 0.15f, 3f, 200f);
        }

        private static void HunterLodge(Builder b)
        {
            b.Floor(2, 2);
            b.CornerPoles(2, 2, true);
            b.Walls(2, 2, true, 1);
            b.GableRoof(2, 2, WallBase + 3f, false);
            b.Add("rug_deer", -0.3f, WallBase + 0.02f, -0.3f);
            b.Add("piece_chest_wood", -1.2f, WallBase, -1.35f);
            b.Add("fire_pit_haldor", -3.6f, 0f, 2.6f);
            b.Add("piece_cookingstation", -3.6f, 0.2f, 2.6f, 45f);
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
            b.Add("piece_chest_wood", 2f, WallBase, -1.35f);
            b.Add("coal_pile", 1.6f, WallBase + 0.1f, 0.6f);
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
            b.Add(b.Style.Banner, 2.35f, deck + 3.2f, 2f);
        }

        private static void Market(Builder b)
        {
            b.LeanTo(2, -3f, -1.5f);
            b.LeanTo(1, 3.5f, -1.5f);
            b.Add("piece_table", -3f, WallBase, -1.3f);
            b.Add("piece_chest_wood", -4.3f, WallBase, -1.9f);
            b.Add("piece_table", 3.5f, WallBase, -1.2f, 90f);
            b.Add("wood_pole_log_4", 0.4f, 2f, 1.5f);
            b.Add(b.Style.Banner, 0.75f, 4.1f, 1.5f);
            b.Add("Cart", 1.2f, 0.3f, 3.2f, 120f);
        }

        private static void GreatHall(Builder b)
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
            b.Add("fire_pit_haldor", -1f, WallBase, 0f);
            b.Add("piece_throne01", -4.6f, WallBase, 0f, 90f);
            b.Add("piece_table", 2.5f, WallBase, -1.4f);
            b.Add("piece_bench01", 2.5f, WallBase, -2.3f);
            b.Add("piece_table", 2.5f, WallBase, 1.3f);
            b.Add("piece_bench01", 2.5f, WallBase, 2.2f, 180f);
            b.Add("bed", 4.6f, WallBase, -1.2f);
            b.Add("piece_chest_wood", 5.2f, WallBase, 2.3f, 180f);
            foreach (int x in new[] { -4, -1, 2, 5 }) b.Add("piece_walltorch", x, WallBase + 2f, -2.75f);
            b.Add(s.Banner, -0.5f, WallBase + 4.3f, 3.25f, 90f);
            b.Add(s.Banner, 2.5f, WallBase + 4.3f, 3.25f, 90f);
            b.Add("piece_groundtorch_wood", -1f, 0.7f, 4.6f);
            b.Add("piece_groundtorch_wood", 3f, 0.7f, 4.6f);
        }

        /// <summary>Ring wall; unlike other buildings every stake follows the ground at its own spot.</summary>
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
