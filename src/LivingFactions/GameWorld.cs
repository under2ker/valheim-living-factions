using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>Answers the simulation's questions about the Valheim world, including areas that are not loaded.</summary>
    internal class GameWorld : IWorldQuery
    {
        public float WorldRadius { get { return 10000f; } }

        public static Biome Map(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return Biome.Meadows;
                case Heightmap.Biome.BlackForest: return Biome.BlackForest;
                case Heightmap.Biome.Swamp: return Biome.Swamp;
                case Heightmap.Biome.Mountain: return Biome.Mountain;
                case Heightmap.Biome.Plains: return Biome.Plains;
                case Heightmap.Biome.Mistlands: return Biome.Mistlands;
                case Heightmap.Biome.AshLands: return Biome.Ashlands;
                case Heightmap.Biome.DeepNorth: return Biome.DeepNorth;
                case Heightmap.Biome.Ocean: return Biome.Ocean;
                default: return Biome.Other;
            }
        }

        public bool ProbeSite(Vec2 pos, float radius, out Biome biome)
        {
            return ProbeReason(pos, radius, out biome) == "ok";
        }

        /// <summary>Like ProbeSite, but says why a site was rejected.</summary>
        public string ProbeReason(Vec2 pos, float radius, out Biome biome)
        {
            biome = Biome.Other;
            WorldGenerator gen = WorldGenerator.instance;
            if (gen == null) return "noworld";
            Heightmap.Biome centre = gen.GetBiome(pos.X, pos.Z);
            biome = Map(centre);
            if (biome == Biome.Ocean || biome == Biome.Other) return "ocean";

            float minHeight = ZoneSystem.c_WaterLevel + 1.5f;
            float h0 = gen.GetHeight(pos.X, pos.Z);
            if (h0 < minHeight) return "low";

            // Sample two rings: the site must be dry, in one biome and not too steep.
            float maxDelta = 2f + radius * 0.25f;
            for (int ring = 1; ring <= 2; ring++)
            {
                float r = radius * ring * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f + ring * 0.4f;
                    float x = pos.X + Mathf.Cos(a) * r;
                    float z = pos.Z + Mathf.Sin(a) * r;
                    float h = gen.GetHeight(x, z);
                    if (h < minHeight) return "shore";
                    if (Mathf.Abs(h - h0) > maxDelta) return "steep";
                    if (radius >= 20f && gen.GetBiome(x, z) != centre) return "mixed";
                }
            }
            // A single building plot must also stay off the grounds of vanilla locations.
            if (radius < 10f && NearLocation(new Vector3(pos.X, 0f, pos.Z), 5f, 1)) return "location";
            return "ok";
        }

        /// <summary>True when a vanilla location's grounds come within <paramref name="margin"/> metres of the point.</summary>
        private static bool NearLocation(Vector3 p, float margin, int reach)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return false;
            Vector2s zone = ZoneSystem.GetZone(p);
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    ZoneSystem.LocationInstance loc;
                    if (!zs.m_locationInstances.TryGetValue(new Vector2s(zone.x + dx, zone.y + dy), out loc)) continue;
                    float grounds = loc.m_location != null ? loc.m_location.m_exteriorRadius : 10f;
                    if (Utils.DistanceXZ(loc.m_position, p) < margin + grounds) return true;
                }
            }
            return false;
        }

        public bool IsReserved(Vec2 pos, float radius)
        {
            if (ZoneSystem.instance == null) return true;
            Vector3 p = new Vector3(pos.X, 0f, pos.Z);

            // The world is dense with small vanilla locations, so only the heart of a new settlement
            // has to be clear of them; later building plots check their own spot.
            if (NearLocation(p, Mathf.Min(radius, 28f), 2)) return true;

            // Stay clear of where players currently are, so nothing is founded on top of a base.
            foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList())
            {
                if (Utils.DistanceXZ(info.m_position, p) < radius + 60f) return true;
            }
            return false;
        }

        /// <summary>Best available ground height: the real terrain when loaded, the generator's otherwise.</summary>
        public static float GroundHeight(float x, float z)
        {
            Vector3 p = new Vector3(x, 0f, z);
            float height;
            if (ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(p) && ZoneSystem.instance.GetGroundHeight(p, out height)) return height;
            return WorldGenerator.instance.GetHeight(x, z);
        }
    }
}
