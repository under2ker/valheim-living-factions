using System.Collections.Generic;
using System.Text;
using LivingFactions.Core;
using Splatform;
using HarmonyLib;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Measurements behind the in-game checks (dev tools only): how much stamina an axe really
    /// costs, how many arrows settlement folk have loosed, and a chest of goods for the trading post.
    /// </summary>
    internal static class DevProbes
    {
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>Arrows and bolts loosed by settlement folk since the last `shots reset`.</summary>
        public static int FolkShots;
        public static string LastShooter = "-";

        /// <summary>A chest the test placed, so `chestinfo` can show what the trading post did with it.</summary>
        public static Container TestChest;

        private static float _frames, _frameSum, _frameWorst, _windowStart, _lastAvg, _lastWorst;

        /// <summary>Called every frame: keeps the average and the worst frame time of the last five seconds.</summary>
        public static void Frame()
        {
            if (!ModConfig.DevTools.Value) return;
            float dt = Time.unscaledDeltaTime * 1000f;
            _frames++;
            _frameSum += dt;
            _frameWorst = Mathf.Max(_frameWorst, dt);
            if (Time.unscaledTime - _windowStart < 5f) return;
            _lastAvg = _frameSum / Mathf.Max(1f, _frames);
            _lastWorst = _frameWorst;
            _frames = _frameSum = _frameWorst = 0f;
            _windowStart = Time.unscaledTime;
        }

        public static string Perf()
        {
            return "last 5 s: average frame " + _lastAvg.ToString("0.0", Inv) + " ms (" + (_lastAvg > 0f ? 1000f / _lastAvg : 0f).ToString("0", Inv) + " fps), worst frame " + _lastWorst.ToString("0", Inv) + " ms; " + Materializer.Describe();
        }

        /// <summary>
        /// Looks for torn ground: along the shared edge of every pair of loaded terrain tiles the two
        /// must agree on the height. Reports the places where they do not.
        /// </summary>
        public static string Seams(float radius)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            List<Heightmap> near = new List<Heightmap>(), at = new List<Heightmap>();
            Heightmap.FindHeightmap(player.transform.position, radius, near);
            int edges = 0, torn = 0;
            float worst = 0f;
            Vector3 worstAt = Vector3.zero;
            StringBuilder sb = new StringBuilder();
            foreach (Heightmap hm in near)
            {
                int w = hm.m_width;
                float half = w * hm.m_scale * 0.5f;
                Vector3 origin = hm.transform.position;
                for (int side = 0; side < 2; side++)
                {
                    float edgeWorst = 0f;
                    Vector3 edgeAt = Vector3.zero;
                    bool shared = false;
                    for (int k = 0; k <= w; k++)
                    {
                        // The +X edge, then the +Z edge; the other two belong to the neighbours.
                        Vector3 p = side == 0 ? new Vector3(origin.x + half, 0f, origin.z - half + k * hm.m_scale) : new Vector3(origin.x - half + k * hm.m_scale, 0f, origin.z + half);
                        float mine = origin.y + hm.GetHeight(side == 0 ? w : k, side == 0 ? k : w);
                        at.Clear();
                        Heightmap.FindHeightmap(p, 0.2f, at);
                        foreach (Heightmap other in at)
                        {
                            if (other == hm || other.m_width != w) continue;
                            Vector3 o = other.transform.position;
                            int gx = Mathf.RoundToInt((p.x - o.x + half) / other.m_scale), gz = Mathf.RoundToInt((p.z - o.z + half) / other.m_scale);
                            if (gx < 0 || gz < 0 || gx > w || gz > w) continue;
                            shared = true;
                            float gap = Mathf.Abs(mine - (o.y + other.GetHeight(gx, gz)));
                            if (gap > edgeWorst)
                            {
                                edgeWorst = gap;
                                edgeAt = new Vector3(p.x, mine, p.z);
                            }
                        }
                    }
                    if (!shared) continue;
                    edges++;
                    if (edgeWorst > 0.05f)
                    {
                        torn++;
                        if (torn <= 8) sb.AppendLine("  TORN " + edgeWorst.ToString("0.00", Inv) + " m at " + edgeAt.x.ToString("0", Inv) + " " + edgeAt.z.ToString("0", Inv));
                    }
                    if (edgeWorst > worst)
                    {
                        worst = edgeWorst;
                        worstAt = edgeAt;
                    }
                }
            }
            return "seams: " + edges + " tile edges checked, torn " + torn + ", worst gap " + worst.ToString("0.00", Inv) + " m" + (torn > 0 ? " at " + worstAt.x.ToString("0", Inv) + " " + worstAt.z.ToString("0", Inv) + "\n" + sb.ToString().TrimEnd() : "");
        }

        private static string _track = "no track taken yet; start one with: track <seconds>";
        private static bool _tracking;

        /// <summary>
        /// Watches how every inhabitant moves, frame by frame, for a few seconds: top speed, sudden
        /// jumps in place, sudden turns and stop-start stutter. `track 15` starts, `track` reads.
        /// </summary>
        public static string Track(string[] a)
        {
            if (a.Length < 2) return _tracking ? "tracking..." : _track;
            if (_tracking) return "tracking...";
            float seconds = Mathf.Clamp(float.Parse(a[1], Inv), 2f, 120f);
            Plugin.Instance.StartCoroutine(TrackRun(seconds));
            return "tracking for " + seconds.ToString("0", Inv) + " s";
        }

        private class Trace
        {
            public string Name;
            public Vector3 Pos;
            public float Yaw, TopSpeed, Path, WorstJump, WorstTurn;
            public int Jumps, Turns, Stutters, Frames;
            public bool Moving;
        }

        private static System.Collections.IEnumerator TrackRun(float seconds)
        {
            _tracking = true;
            Dictionary<FactionMember, Trace> traces = new Dictionary<FactionMember, Trace>();
            float end = Time.unscaledTime + seconds;
            int frames = 0;
            while (Time.unscaledTime < end)
            {
                // Characters move on the physics clock; sampled per rendered frame they seem to stop between steps.
                float dt = Time.fixedDeltaTime;
                frames++;
                foreach (FactionMember m in FactionMember.All)
                {
                    if (m == null) continue;
                    Vector3 pos = m.transform.position;
                    float yaw = m.transform.eulerAngles.y;
                    Trace t;
                    if (!traces.TryGetValue(m, out t))
                    {
                        traces[m] = new Trace { Name = m.Describe(), Pos = pos, Yaw = yaw };
                        continue;
                    }
                    if (dt > 0f)
                    {
                        float step = Vector3.Distance(pos, t.Pos), speed = step / dt;
                        float turn = Mathf.Abs(Mathf.DeltaAngle(yaw, t.Yaw));
                        t.Path += step;
                        t.Frames++;
                        // Nobody on foot covers more than 12 m/s; more than half a metre in one frame at that rate is a jump.
                        if (speed > 12f && step > 0.5f)
                        {
                            t.Jumps++;
                            t.WorstJump = Mathf.Max(t.WorstJump, step);
                        }
                        else t.TopSpeed = Mathf.Max(t.TopSpeed, speed);
                        if (turn > 60f)
                        {
                            t.Turns++;
                            t.WorstTurn = Mathf.Max(t.WorstTurn, turn);
                        }
                        bool moving = speed > 0.4f;
                        if (moving != t.Moving) t.Stutters++;
                        t.Moving = moving;
                    }
                    t.Pos = pos;
                    t.Yaw = yaw;
                }
                yield return new WaitForFixedUpdate();
            }
            StringBuilder sb = new StringBuilder("tracked " + traces.Count + " inhabitants for " + seconds.ToString("0", Inv) + " s (" + frames + " physics steps)\n");
            int odd = 0;
            foreach (Trace t in traces.Values)
            {
                // Starting and stopping more than once a second, on average, is what reads as jerking.
                bool jerky = t.Jumps > 0 || t.Turns > 1 || t.Stutters > seconds * 1.5f;
                if (jerky) odd++;
                sb.AppendLine((jerky ? "  JERKY " : "  ok    ") + "jumps " + t.Jumps + " (worst " + t.WorstJump.ToString("0.0", Inv) + " m), sharp turns " + t.Turns + " (worst " + t.WorstTurn.ToString("0", Inv)
                    + " deg), starts/stops " + t.Stutters + ", walked " + t.Path.ToString("0", Inv) + " m, top speed " + t.TopSpeed.ToString("0.0", Inv) + " m/s | " + t.Name);
            }
            _track = "track done: " + odd + " of " + traces.Count + " moved in jerks\n" + sb.ToString().TrimEnd();
            _tracking = false;
        }

        /// <summary>
        /// What an item's model is drawn with: every renderer under the prefab, its materials, their
        /// shader, switched-on keywords and each shader property with its value. Written for working
        /// out how the game makes things glow. Also saved to materials.txt.
        /// </summary>
        public static string Materials(string[] a)
        {
            if (a.Length < 2) return "usage: lf mats <item or piece prefab> [...]";
            StringBuilder sb = new StringBuilder();
            for (int n = 1; n < a.Length; n++)
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(a[n]) : null;
                if (prefab == null && ZNetScene.instance != null) prefab = ZNetScene.instance.GetPrefab(a[n]);
                if (prefab == null)
                {
                    sb.AppendLine(a[n] + ": NOT FOUND");
                    continue;
                }
                sb.AppendLine(a[n] + ":");
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    sb.AppendLine("  renderer " + r.GetType().Name + " '" + r.name + "'" + (r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)"));
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        Shader shader = m.shader;
                        sb.AppendLine("    material '" + m.name + "' shader '" + shader.name + "' queue " + m.renderQueue + " keywords [" + string.Join(" ", m.shaderKeywords) + "]"
                            + " emissionFlags " + m.globalIlluminationFlags);
                        for (int i = 0; i < shader.GetPropertyCount(); i++)
                        {
                            string name = shader.GetPropertyName(i);
                            UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
                            string value;
                            switch (type)
                            {
                                case UnityEngine.Rendering.ShaderPropertyType.Color:
                                    Color c = m.GetColor(name);
                                    value = "(" + c.r.ToString("0.###", Inv) + ", " + c.g.ToString("0.###", Inv) + ", " + c.b.ToString("0.###", Inv) + ", " + c.a.ToString("0.###", Inv) + ")";
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                    Texture t = m.GetTexture(name);
                                    value = t != null ? "'" + t.name + "' " + t.width + "x" + t.height : "none";
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                    Vector4 v = m.GetVector(name);
                                    value = "(" + v.x.ToString("0.###", Inv) + ", " + v.y.ToString("0.###", Inv) + ", " + v.z.ToString("0.###", Inv) + ", " + v.w.ToString("0.###", Inv) + ")";
                                    break;
                                default:
                                    value = m.GetFloat(name).ToString("0.###", Inv);
                                    break;
                            }
                            sb.AppendLine("      " + type + " " + name + " = " + value);
                        }
                    }
                }
                foreach (Light light in prefab.GetComponentsInChildren<Light>(true))
                {
                    sb.AppendLine("  light '" + light.name + "' " + light.type + " colour (" + light.color.r.ToString("0.##", Inv) + ", " + light.color.g.ToString("0.##", Inv) + ", " + light.color.b.ToString("0.##", Inv)
                        + ") intensity " + light.intensity.ToString("0.##", Inv) + " range " + light.range.ToString("0.#", Inv));
                }
            }
            string text = sb.ToString().TrimEnd();
            System.IO.File.WriteAllText(System.IO.Path.Combine(SimHost.DataDir, "materials.txt"), text + "\n");
            return text;
        }

        /// <summary>How bunched up the inhabitants are: the most people standing within 3 m of any one of them.</summary>
        public static string Crowd()
        {
            List<FactionMember> all = new List<FactionMember>(FactionMember.All);
            int worst = 0;
            string where = "-";
            foreach (FactionMember m in all)
            {
                if (m == null) continue;
                int near = 0;
                foreach (FactionMember o in all)
                {
                    if (o != null && Vector3.Distance(o.transform.position, m.transform.position) < 3f) near++;
                }
                if (near > worst)
                {
                    worst = near;
                    where = m.Describe();
                }
            }
            return "crowd: at most " + worst + " together within 3 m (of " + all.Count + "), around " + where;
        }

        /// <summary>Switches the game's own language (kept in its settings) and the mod's with it.</summary>
        public static string Language(string[] a)
        {
            if (Localization.instance == null) return "no localization yet";
            if (a.Length > 1) Localization.instance.SetLanguage(a[1]);
            string choice = ModConfig.Language.Value.Trim().ToLowerInvariant();
            Texts.Lang = choice == "ru" ? 1 : choice == "en" ? 0 : Localization.instance.GetSelectedLanguage() == "Russian" ? 1 : 0;
            return "game language " + Localization.instance.GetSelectedLanguage() + ", mod language " + (Texts.Lang == 1 ? "ru" : "en");
        }

        /// <summary>Spends a known amount of stamina and reports what it actually cost, through the game's own UseStamina.</summary>
        public static string Stamina()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            float before = player.GetStamina();
            if (before < 3f) return "not enough stamina to measure (" + before.ToString("0", Inv) + ")";
            player.UseStamina(2f);
            float taken = before - player.GetStamina();
            // Give it back, so measuring twice in a row starts from the same place.
            player.AddStamina(taken);
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            string name = weapon != null && weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : "nothing";
            return "holding " + name + ": asked 2.00, took " + taken.ToString("0.00", Inv) + " stamina";
        }

        public static string Shots(string[] a)
        {
            if (a.Length > 1 && a[1] == "reset")
            {
                FolkShots = 0;
                LastShooter = "-";
            }
            return (FolkShots > 0 ? "arrows flying: " : "none yet: ") + "settlement folk fired " + FolkShots + " projectiles, last shooter " + LastShooter;
        }

        /// <summary>Places a wooden chest by a settlement, owned by the player, and fills it with wood and stone.</summary>
        public static string PlaceTradeChest(Settlement s)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) return "no player";
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            if (f == null) return "no faction";
            // Standing high enough for the settlement to trade at all.
            SimHost.Sim.PlayerRel(f, player.GetPlayerID()).Reputation = 45f;

            Vector3 spot = new Vector3(s.Pos.X + 12f, 0f, s.Pos.Z);
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(spot, out ground)) return "zone not loaded there";
            spot.y = ground;
            GameObject prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (prefab == null) return "no chest prefab";
            GameObject go = Object.Instantiate(prefab, spot, Quaternion.identity);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null) piece.SetCreator(player.GetPlayerID(), PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            TestChest = go.GetComponent<Container>();
            if (TestChest == null) return "chest prefab has no container";
            Inventory inv = TestChest.GetInventory();
            inv.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 20);
            inv.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 10);
            return "chest at " + spot + " for " + s.Name + ", reputation 45";
        }

        public static string ChestInfo()
        {
            if (TestChest == null) return "no test chest";
            StringBuilder sb = new StringBuilder();
            foreach (ItemDrop.ItemData item in TestChest.GetInventory().GetAllItems()) sb.Append(" " + item.m_dropPrefab.name + "x" + item.m_stack);
            return "chest contents:" + (sb.Length == 0 ? " empty" : sb.ToString());
        }
    }

    /// <summary>Counts the arrows the settlement folk loose, whether guards at home or raiders on the way.</summary>
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class FolkShotPatch
    {
        private static void Postfix(Character owner)
        {
            if (!ModConfig.DevTools.Value || owner == null) return;
            FactionMember member = FactionMember.Of(owner);
            if (member == null) return;
            DevProbes.FolkShots++;
            DevProbes.LastShooter = member.DisplayName + " (" + member.Role + ")";
        }
    }
}
