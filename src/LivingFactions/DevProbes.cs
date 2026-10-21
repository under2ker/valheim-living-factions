using System.Collections.Generic;
using System.Linq;
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
        private static Vector3 _hoeCentre;
        private static float _hoeHeight, _hoeHalf;
        private static int _hoeSeams, _hoeLeft;

        /// <summary>
        /// `hoe <half size> <height> [border]`: a square of ground levelled as a player does it with the hoe ("Level
        /// ground", one stroke every 0.3 s, 2 m apart), at the ground's height in its middle plus height; with border,
        /// across the nearest border of two terrain tiles. Read the result with `hoecheck`.
        /// </summary>
        public static string Hoe(string[] a)
        {
            Player me = Player.m_localPlayer;
            if (me == null || a.Length < 3) return "hoe <half size> <height> [border]";
            GameObject level = HoeLevelPiece();
            float half = float.Parse(a[1], Inv), up = float.Parse(a[2], Inv);
            Vector3 centre = me.transform.position;
            if (a.Length > 3 && a[3] == "border")
            {
                // Tiles are the 64 m zones: their borders run half a zone off each zone's middle.
                Vector3 zone = ZoneSystem.GetZonePos(ZoneSystem.GetZone(centre));
                centre.x = zone.x + (centre.x >= zone.x ? 32f : -32f);
                // Along that border, the nearest square clear of every road: the mod grades its road beds, and a
                // square on one is levelled back to the bed.
                for (int step = 0; step <= 20; step++)
                {
                    float dz = (step + 1) / 2 * 4f * (step % 2 == 0 ? 1f : -1f);
                    Vector3 at = new Vector3(centre.x, 0f, centre.z + dz);
                    if (Roads.OnRoad(at, half * 1.5f + 6f)) continue;
                    centre = at;
                    break;
                }
            }
            float ground;
            if (!ZoneSystem.instance.GetGroundHeight(centre, out ground)) return "no ground there";
            _hoeCentre = new Vector3(centre.x, ground + up, centre.z);
            _hoeHeight = ground + up;
            _hoeHalf = half;
            _hoeSeams = Materializer.SeamsHealed;
            Plugin.Instance.StartCoroutine(HoeRun(level));
            return "levelling a " + (half * 2f).ToString("0", Inv) + " m square at " + _hoeCentre.ToString("0.0") + " to " + _hoeHeight.ToString("0.00", Inv) + " m with "
                + (level != null ? level.name : "a levelling stroke of its own (level r 2, smooth r 3), the hoe's " + HoePieces());
        }

        /// <summary>
        /// The hoe's "Level ground", found in the hoe's own pieces (a terrain operation that levels and raises
        /// nothing) rather than by a prefab name, which was guessed wrong once.
        /// </summary>
        private static GameObject HoeLevelPiece()
        {
            GameObject hoe = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hoe") : null;
            ItemDrop drop = hoe != null ? hoe.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return null;
            foreach (GameObject piece in table.m_pieces)
            {
                TerrainOp op = piece != null ? piece.GetComponentInChildren<TerrainOp>(true) : null;
                if (op != null && op.m_settings.m_level && !op.m_settings.m_raise) return piece;
            }
            return null;
        }

        /// <summary>What the hoe's pieces are, for the reply when none levels.</summary>
        private static string HoePieces()
        {
            GameObject hoe = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hoe") : null;
            ItemDrop drop = hoe != null ? hoe.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null) return "piece table is missing";
            StringBuilder sb = new StringBuilder("pieces:");
            foreach (GameObject piece in table.m_pieces)
            {
                if (piece == null) continue;
                TerrainOp op = piece.GetComponentInChildren<TerrainOp>(true);
                sb.Append(" " + piece.name + (op == null ? "(no op)" : "(level " + op.m_settings.m_level + ", raise " + op.m_settings.m_raise + ", smooth " + op.m_settings.m_smooth + ")"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// One stroke of levelling as a player's tool makes it: a TerrainOp that, waking up, sends its operation to
        /// each tile's owner (TerrainComp.ApplyOperation, the way every player's ground work goes), then goes.
        /// </summary>
        private static void LevelStroke(Vector3 at)
        {
            GameObject stroke = new GameObject("lf_hoe_stroke");
            stroke.SetActive(false);
            stroke.transform.position = at;
            TerrainOp op = stroke.AddComponent<TerrainOp>();
            op.m_settings.m_level = true;
            op.m_settings.m_levelRadius = 2f;
            op.m_settings.m_square = false;
            op.m_settings.m_smooth = true;
            op.m_settings.m_smoothRadius = 3f;
            op.m_settings.m_smoothPower = 3f;
            op.m_settings.m_paintCleared = true;
            op.m_settings.m_paintType = TerrainModifier.PaintType.Dirt;
            op.m_settings.m_paintRadius = 2f;
            stroke.SetActive(true);
        }

        private static System.Collections.IEnumerator HoeRun(GameObject level)
        {
            List<Vector3> spots = new List<Vector3>();
            for (float x = -_hoeHalf; x <= _hoeHalf + 0.01f; x += 2f)
            {
                for (float z = -_hoeHalf; z <= _hoeHalf + 0.01f; z += 2f) spots.Add(_hoeCentre + new Vector3(x, 0f, z));
            }
            _hoeLeft = spots.Count;
            foreach (Vector3 spot in spots)
            {
                if (level != null) Object.Instantiate(level, spot, Quaternion.identity);
                else LevelStroke(spot);
                _hoeLeft--;
                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        /// <summary>`hoecheck`: how far the hoed square is off its level inside and on the tile border, and what the mod mended there.</summary>
        public static string HoeCheck()
        {
            if (_hoeHalf <= 0f) return "no hoe run yet";
            if (_hoeLeft > 0) return "still levelling, " + _hoeLeft + " strokes left";
            float inner = _hoeHalf - 2.5f, worstIn = 0f, worstBorder = 0f;
            for (float x = -inner; x <= inner + 0.01f; x += 0.5f)
            {
                for (float z = -inner; z <= inner + 0.01f; z += 0.5f)
                {
                    float h;
                    if (!ZoneSystem.instance.GetGroundHeight(_hoeCentre + new Vector3(x, 0f, z), out h)) continue;
                    float off = Mathf.Abs(h - _hoeHeight);
                    worstIn = Mathf.Max(worstIn, off);
                    if (Mathf.Abs(x) < 1.01f) worstBorder = Mathf.Max(worstBorder, off);
                }
            }
            return "hoed square: worst off its level " + worstIn.ToString("0.00", Inv) + " m, within 1 m of the tile border " + worstBorder.ToString("0.00", Inv)
                + " m; seams the mod mended since " + (Materializer.SeamsHealed - _hoeSeams);
        }

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
        private static bool _fitting;
        private static string _fit = "no shieldfit run yet";

        /// <summary>
        /// `shieldfit [seconds]`: for everyone near who carries a shield on the arm (the player too), the forearm
        /// (wrist to elbow) is sampled every physics step in the space of the hand the shield hangs from, and the
        /// least room between it and the back of the board is kept, where the arm is behind the board. Under
        /// ArmRadius the arm comes through the board (CLIPS). Read the result with `shieldfit` again, or wait
        /// for it with `until shieldfit done :: shieldfit`.
        /// </summary>
        public static string ShieldFit(string[] a)
        {
            if (a.Length < 2 || _fitting) return _fitting ? "measuring..." : _fit;
            float seconds = Mathf.Clamp(float.Parse(a[1], Inv), 1f, 60f);
            Plugin.Instance.StartCoroutine(ShieldFitRun(seconds));
            return "measuring for " + seconds.ToString("0", Inv) + " s";
        }

        /// <summary>`shieldbox`: the player's shield board in the hand joint's space, by its submesh bounds and by its own vertices.</summary>
        public static string ShieldBox()
        {
            Player h = Player.m_localPlayer;
            VisEquipment vis = h != null ? h.GetComponent<VisEquipment>() : null;
            GameObject item = vis != null ? Traverse.Create(vis).Field("m_leftItemInstance").GetValue<GameObject>() : null;
            if (item == null) return "no shield on the arm";
            StringBuilder sb = new StringBuilder("item under " + (item.transform.parent != null ? item.transform.parent.name : "-") + ", joint " + vis.m_leftHand.name + ", item local rot " + item.transform.localRotation.eulerAngles.ToString("0") + ", pos " + item.transform.localPosition.ToString("0.000") + "\n");
            foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                int part;
                CustomItems.BoardPart.TryGetValue(mesh.name, out part);
                Bounds byVerts = new Bounds();
                bool first = true;
                if (mesh.isReadable && part < mesh.subMeshCount)
                {
                    Vector3[] verts = mesh.vertices;
                    foreach (int i in mesh.GetTriangles(part))
                    {
                        Vector3 p = vis.m_leftHand.InverseTransformPoint(mf.transform.TransformPoint(verts[i]));
                        if (first) { byVerts = new Bounds(p, Vector3.zero); first = false; }
                        else byVerts.Encapsulate(p);
                    }
                }
                sb.Append(mf.name + " mesh " + mesh.name + " part " + part + " of " + mesh.subMeshCount + ", node rot " + mf.transform.localRotation.eulerAngles.ToString("0") + ", submesh bounds (local) " + mesh.GetSubMesh(Mathf.Min(part, mesh.subMeshCount - 1)).bounds.ToString("0.00")
                    + ", board by its vertices in the joint: " + (first ? "unreadable" : "min " + byVerts.min.ToString("0.00") + " max " + byVerts.max.ToString("0.00")) + "\n");
            }
            return sb.ToString().TrimEnd();
        }

        private static readonly Dictionary<Mesh, Vector3[]> BoardCache = new Dictionary<Mesh, Vector3[]>();

        /// <summary>The vertices of one part of a mesh (all of it for part -1), in the mesh's space; none when it cannot be read.</summary>
        private static Vector3[] BoardVertices(Mesh mesh, int part)
        {
            Vector3[] found;
            if (BoardCache.TryGetValue(mesh, out found)) return found;
            if (!mesh.isReadable) found = new Vector3[0];
            else if (part < 0 || part >= mesh.subMeshCount) found = mesh.vertices;
            else
            {
                Vector3[] all = mesh.vertices;
                found = mesh.GetTriangles(part).Select(i => all[i]).ToArray();
            }
            BoardCache[mesh] = found;
            return found;
        }

        /// <summary>Half the thickness of a forearm, sleeve or bracer included.</summary>
        private const float ArmRadius = 0.05f;

        private static readonly Dictionary<Humanoid, int[]> Places = new Dictionary<Humanoid, int[]>();
        private static readonly Dictionary<Humanoid, float> Thick = new Dictionary<Humanoid, float>();
        private static readonly Dictionary<Humanoid, string> Line = new Dictionary<Humanoid, string>();
        private static readonly Dictionary<Humanoid, float> Tilt = new Dictionary<Humanoid, float>();

        /// <summary>Least squares y = ax + bz + c through the points; back and front: how far they reach behind and before it.</summary>
        private static void FitPlane(List<Vector3> pts, out float a, out float b, out float c, out float back, out float front)
        {
            double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sy = 0, sxy = 0, szy = 0, n = pts.Count;
            foreach (Vector3 p in pts)
            {
                sxx += p.x * p.x; sxz += p.x * p.z; szz += p.z * p.z; sx += p.x; sz += p.z; sy += p.y; sxy += p.x * p.y; szy += p.z * p.y;
            }
            // Normal equations of the three unknowns, solved by Cramer's rule.
            double[,] m = { { sxx, sxz, sx }, { sxz, szz, sz }, { sx, sz, n } };
            double[] r = { sxy, szy, sy };
            double det = Det(m);
            double[] sol = new double[3];
            for (int k = 0; k < 3; k++)
            {
                double[,] mk = (double[,])m.Clone();
                for (int row = 0; row < 3; row++) mk[row, k] = r[row];
                sol[k] = System.Math.Abs(det) > 1e-12 ? Det(mk) / det : 0;
            }
            a = (float)sol[0]; b = (float)sol[1]; c = (float)sol[2];
            back = float.MinValue; front = float.MaxValue;
            foreach (Vector3 p in pts)
            {
                float d = p.y - (a * p.x + b * p.z + c);
                back = Mathf.Max(back, d);
                front = Mathf.Min(front, d);
            }
        }

        private static double Det(double[,] m)
        {
            return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        }

        private static void Tally(Humanoid h, int place)
        {
            int[] counts;
            if (!Places.TryGetValue(h, out counts)) Places[h] = counts = new int[3];
            counts[place]++;
        }

        private static System.Collections.IEnumerator ShieldFitRun(float seconds)
        {
            _fitting = true;
            Places.Clear();
            Thick.Clear();
            Line.Clear();
            Tilt.Clear();
            Dictionary<Humanoid, float> least = new Dictionary<Humanoid, float>();
            Dictionary<Humanoid, string> names = new Dictionary<Humanoid, string>();
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end)
            {
                List<Humanoid> carriers = new List<Humanoid>();
                foreach (FactionMember m in FactionMember.All) if (m != null && m.Humanoid != null) carriers.Add(m.Humanoid);
                if (Player.m_localPlayer != null) carriers.Add(Player.m_localPlayer);
                foreach (Humanoid h in carriers)
                {
                    VisEquipment vis = h.GetComponent<VisEquipment>();
                    GameObject item = vis != null ? Traverse.Create(vis).Field("m_leftItemInstance").GetValue<GameObject>() : null;
                    if (item == null || vis.m_leftHand == null) continue;
                    ItemDrop.ItemData shield = h.GetInventory().GetAllItems().FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield);
                    if (shield == null) continue;
                    Transform joint = vis.m_leftHand, wrist = Utils.FindChild(h.transform, "LeftHand"), elbow = Utils.FindChild(h.transform, "LeftForeArm");
                    if (wrist == null || elbow == null) continue;
                    // The board, as a box in the joint's space: the model's own board when it is one of the mod's
                    // (its rim, boss, fur or feathers left out), else everything the item draws.
                    Bounds board = new Bounds();
                    List<Vector3> pts = new List<Vector3>();
                    bool any = false;
                    int boardPart = -1;
                    bool own = shield.m_dropPrefab != null && CustomItems.BoardPart.TryGetValue(shield.m_dropPrefab.name, out boardPart);
                    if (!own) boardPart = -1;
                    foreach (MeshFilter mf in item.GetComponentsInChildren<MeshFilter>())
                    {
                        MeshRenderer r = mf.GetComponent<MeshRenderer>();
                        if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                        if (own && (boardPart >= mf.sharedMesh.subMeshCount || mf.sharedMesh.name != shield.m_dropPrefab.name)) continue;
                        // By the board's own vertices: the mesh node is turned, and the corners of its box, turned with
                        // it, made a board half a metre thick.
                        foreach (Vector3 v in BoardVertices(mf.sharedMesh, boardPart))
                        {
                            Vector3 corner = joint.InverseTransformPoint(mf.transform.TransformPoint(v));
                            pts.Add(corner);
                            if (!any) { board = new Bounds(corner, Vector3.zero); any = true; }
                            else board.Encapsulate(corner);
                        }
                    }
                    if (!any || pts.Count < 3) continue;
                    // The board's plane (it may lie tilted): y = ax + bz + c fitted to its vertices, its back where the
                    // vertices reach furthest behind that plane.
                    float pa, pb, pc, back, front;
                    FitPlane(pts, out pa, out pb, out pc, out back, out front);
                    float room = float.MaxValue;
                    for (int k = 0; k <= 8; k++)
                    {
                        Vector3 p = joint.InverseTransformPoint(Vector3.Lerp(wrist.position, elbow.position, k / 8f));
                        // Behind the board (within its outline), the arm's side toward the board against the board's back.
                        if (p.x < board.min.x || p.x > board.max.x || p.z < board.min.z || p.z > board.max.z) continue;
                        float plane = pa * p.x + pb * p.z + pc;
                        room = Mathf.Min(room, p.y - (plane + back));
                        Tally(h, p.y > plane + back + ArmRadius ? 0 : p.y < plane + front - ArmRadius ? 2 : 1);
                    }
                    Thick[h] = back - front;
                    Tilt[h] = Mathf.Atan(pa) * Mathf.Rad2Deg;
                    if (room == float.MaxValue) continue;
                    float was;
                    if (!least.TryGetValue(h, out was) || room < was)
                    {
                        least[h] = room;
                        // Where the arm ran when it came closest: the wrist and the elbow in the joint's space.
                        Line[h] = "wrist " + joint.InverseTransformPoint(wrist.position).ToString("0.00") + " elbow " + joint.InverseTransformPoint(elbow.position).ToString("0.00");
                    }
                    names[h] = (h == Player.m_localPlayer ? "player" : FactionMember.Of(h) != null ? FactionMember.Of(h).Describe() : h.name) + " with " + (shield.m_dropPrefab != null ? shield.m_dropPrefab.name : shield.m_shared.m_name);
                }
                yield return new WaitForFixedUpdate();
            }
            StringBuilder sb = new StringBuilder();
            int clips = 0;
            foreach (KeyValuePair<Humanoid, float> pair in least.OrderBy(x => x.Value))
            {
                bool clip = pair.Value < ArmRadius;
                if (clip) clips++;
                int[] where;
                Places.TryGetValue(pair.Key, out where);
                where = where ?? new int[3];
                float thick;
                Thick.TryGetValue(pair.Key, out thick);
                sb.AppendLine((clip ? "  CLIPS " : "  ok    ") + "least room " + pair.Value.ToString("0.000", Inv) + " m, forearm samples behind the board " + where[0] + ", through it " + where[1]
                    + ", in front of it " + where[2] + ", board " + thick.ToString("0.00", Inv) + " m thick, tilted " + (Tilt.ContainsKey(pair.Key) ? Tilt[pair.Key] : 0f).ToString("0", Inv) + " deg, closest with " + Line[pair.Key] + " | " + names[pair.Key]);
            }
            _fit = "shieldfit done: " + clips + " of " + least.Count + " shield carriers have the forearm through the board (room under " + ArmRadius.ToString("0.00", Inv) + " m)\n" + sb.ToString().TrimEnd();
            _fitting = false;
        }

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
            // `lang mod pl` sets the mod's own choice and leaves the game alone; `lang mod auto` follows the game again.
            if (a.Length > 2 && a[1] == "mod") ModConfig.Language.Value = a[2];
            else if (a.Length > 1) Localization.instance.SetLanguage(a[1]);
            Texts.Lang = Texts.Resolve(ModConfig.Language.Value, Localization.instance.GetSelectedLanguage());
            CustomItems.Rename();
            foreach (FactionMember member in FactionMember.All) member.Rename();
            return "game language " + Localization.instance.GetSelectedLanguage() + ", mod language " + Texts.Code;
        }

        /// <summary>Spends a known amount of stamina and reports what it actually cost, through the game's own UseStamina.</summary>
        public static string Stamina()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            // A full bar first: a player who has just run or swum would measure nothing.
            player.AddStamina(player.GetMaxStamina());
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

        /// <summary>
        /// What the weapon in hand deals, and what it would deal without its relic mark. `damage keen`
        /// puts that mark on it first, `damage none` takes the mark off.
        /// </summary>
        public static string Damage(string[] a)
        {
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData weapon = player != null ? player.GetCurrentWeapon() : null;
            if (weapon == null || weapon.m_dropPrefab == null) return "nothing in hand";
            if (a.Length > 1)
            {
                if (a[1] == "none") weapon.m_customData.Remove(Wares.RelicKey);
                else weapon.m_customData[Wares.RelicKey] = a[1];
            }
            string affix = Wares.AffixOf(weapon);
            float with = weapon.GetDamage().m_slash;
            weapon.m_customData.Remove(Wares.RelicKey);
            float plain = weapon.GetDamage().m_slash;
            if (affix != null) weapon.m_customData[Wares.RelicKey] = affix;
            return "holding " + weapon.m_dropPrefab.name + " [" + (affix ?? "no mark") + "]: slash " + with.ToString("0.0", Inv) + ", without the mark " + plain.ToString("0.0", Inv)
                + ", x" + (plain > 0f ? with / plain : 0f).ToString("0.00", Inv);
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
