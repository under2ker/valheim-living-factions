using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using LivingFactions.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// One axe the mod adds: a clone of a vanilla axe whose mesh and textures are built at run time from the
    /// lowpoly-kit export embedded in the plugin (Assets/&lt;Asset&gt;.json, its PNGs and &lt;Asset&gt;-icon.png).
    /// The trade offer that sells it is in trade.cfg (Wares.cs).
    /// </summary>
    internal class AxeSpec
    {
        public string Asset;
        public string Prefab;
        public string Source;
        public string NameEn, NameRu;
        public string DescEn, DescRu;
    }

    /// <summary>Items the mod adds to the game. Each axe is listed here once.</summary>
    internal static class CustomItems
    {
        public const string WolfFangAxe = "LF_WolfFangAxe";
        public const string StagHornAxe = "LF_StagHornAxe";

        private static readonly AxeSpec[] Axes =
        {
            new AxeSpec
            {
                Asset = "wolf-fang-axe", Prefab = WolfFangAxe, Source = "AxeIron",
                NameEn = "Wolf's Fang", NameRu = "Клык Волка",
                DescEn = "The relic axe of the Wolf Clan. Iron blade, leather-wrapped haft, a wolf's tail at the pommel.",
                DescRu = "Реликвийный топор Клана Волка. Лезвие выковано из железа, рукоять обвита кожей, на обухе - волчий хвост.",
            },
            new AxeSpec
            {
                Asset = "stag-horn-axe", Prefab = StagHornAxe, Source = "AxeIron",
                NameEn = "Stag's Horn", NameRu = "Рог Оленя",
                DescEn = "A merchant's axe of the Stag Clan. Antler haft, bronze fittings, coins on a chain.",
                DescRu = "Торговый топор Клана Оленя. Рукоять из оленьего рога, бронзовая оковка, монеты на цепи.",
            },
            // The five that are not sold: how each one is come by is in Rewards.cs.
            new AxeSpec
            {
                Asset = "raven-wing-axe", Prefab = "LF_RavenWingAxe", Source = "AxeIron",
                NameEn = "Raven's Wing", NameRu = "Воронье Крыло",
                DescEn = "The long bearded axe of the Raven Clan: blued steel, a raven's beak on the back, three feathers. A jarl gives it to whoever cuts down raiders attacking a Raven Clan settlement.",
                DescRu = "Длинная секира Клана Ворона: синёная сталь, вороний клюв на обухе, три пера. Ярл дарит её тому, кто сразит налётчиков у поселения клана.",
            },
            new AxeSpec
            {
                Asset = "fuling-blackgnaw", Prefab = "LF_BlackgnawAxe", Source = "AxeIron",
                NameEn = "Blackgnaw", NameRu = "Черногрыз",
                DescEn = "A Fuling cleaver of black iron with a saw for an edge and trophy teeth. Nobody is given it: it is taken from a Fuling chieftain.",
                DescRu = "Тесак фулингов из чёрного железа с пилой вместо лезвия и зубами-трофеями. Его не дарят: его берут с вождя фулингов.",
            },
            new AxeSpec
            {
                Asset = "dvergr-rune-axe", Prefab = "LF_RuneAxe", Source = "AxeIron",
                NameEn = "Rune Cleaver", NameRu = "Рунная Двусечка",
                DescEn = "A double-bitted Dvergr axe of dark alloy and copper, with runes along the blades. A thane of the Dvergr Hold gives it to an ally of long standing.",
                DescRu = "Двусторонний топор двергов из тёмного сплава и меди, с рунами по лезвиям. Тан Чертога дарит его давнему союзнику.",
            },
            new AxeSpec
            {
                Asset = "greydwarf-root-axe", Prefab = "LF_RootAxe", Source = "AxeIron",
                NameEn = "Rootbiter", NameRu = "Корнеруб",
                DescEn = "Knapped flint on a living root, wound with vine, an amber eye of the brood. The chief of a Greydwarf brood carries it.",
                DescRu = "Колотый кремень на живом корне, обвитый лианой, с янтарным глазом выводка. Его носит вождь выводка грейдворфов.",
            },
            new AxeSpec
            {
                Asset = "draugr-barrow-moon", Prefab = "LF_BarrowMoonAxe", Source = "AxeIron",
                NameEn = "Barrow Moon", NameRu = "Курганная Луна",
                DescEn = "A crescent axe of rust and patina with a skull at the socket and burial rags. It lies with the chief of the Draugr Court.",
                DescRu = "Секира-полумесяц в ржавчине и патине, с черепом у втулки и погребальными лохмотьями. Она у вождя Двора драугров.",
            },
        };

        private static bool _created;

        /// <summary>The vanilla AxeIron in joint space (docs/reference/attach-points-items.tsv): handle on Z, -0.16..+0.72.</summary>
        private const float HandleMin = -0.16f;
        private const float HandleLength = 0.88f;
        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        public static void CreatePrefabs()
        {
            if (_created) return;
            _created = true;
            ItemManager.OnVanillaItemsAvailable -= CreatePrefabs;
            foreach (AxeSpec spec in Axes)
            {
                try
                {
                    CreateAxe(spec);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not create " + spec.Prefab + ": " + e);
                }
            }
        }

        private static void CreateAxe(AxeSpec spec)
        {
            GameObject go = PrefabManager.Instance.CreateClonedPrefab(spec.Prefab, spec.Source);
            if (go == null)
            {
                Plugin.Log.LogError("Could not clone " + spec.Source + " for " + spec.Prefab);
                return;
            }

            ItemDrop.ItemData.SharedData shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
            bool ru = Texts.Lang == 1;
            // Other languages keep the item's name and tale in their own tables, under the prefab's name.
            shared.m_name = Texts.Translated(Texts.Lang, "item_" + spec.Prefab) && Texts.Lang >= 2 ? Texts.Get("item_" + spec.Prefab) : ru ? spec.NameRu : spec.NameEn;
            shared.m_description = Texts.Translated(Texts.Lang, "itemd_" + spec.Prefab) && Texts.Lang >= 2 ? Texts.Get("itemd_" + spec.Prefab) : ru ? spec.DescRu : spec.DescEn;
            Sprite icon = LoadIcon(spec.Asset);
            if (icon != null) shared.m_icons = new[] { icon };

            ApplyModel(go, spec.Asset, spec.Prefab);
            ItemManager.Instance.AddItem(new CustomItem(go, false));
            Plugin.Log.LogInfo("Registered " + spec.Prefab);
        }

        /// <summary>Replaces the clone's visible mesh with the kit's axe, keeping the clone's overall size.</summary>
        private static void ApplyModel(GameObject go, string asset, string prefab)
        {
            MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter target = filters.OrderByDescending(f => f.sharedMesh != null ? f.sharedMesh.bounds.size.magnitude : 0f).FirstOrDefault();
            if (target == null || target.sharedMesh == null)
            {
                Plugin.Log.LogWarning(prefab + ": the iron axe has no mesh to replace");
                return;
            }
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Plugin.Log.LogWarning(prefab + ": the mesh has no MeshRenderer");
                return;
            }

            // The whole vanilla axe (all its parts, in prefab space) is the reference for size and place.
            Bounds vanilla = PrefabBounds(go, filters);
            float oldSize = Mathf.Max(vanilla.size.x, vanilla.size.y, vanilla.size.z);
            JObject kit = JObject.Parse(ReadText(asset + ".json"));

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int[]> subs = new List<int[]>();
            List<Material> mats = new List<Material>();
            Material baseMat = renderer.sharedMaterial;

            // The grip's centre, in joint space before fitting: the handle must pass through the joint's zero.
            float gripX = 0f, gripY = 0f;
            int gripN = 0;
            foreach (JObject part in (JArray)kit["meshes"])
            {
                float[] p = ((JArray)part["positions"]).Select(t => (float)t).ToArray();
                float[] uv = ((JArray)part["uvs"]).Select(t => (float)t).ToArray();
                List<int> tris = new List<int>();
                // The kit gives no winding and renders two-sided: every triangle is added twice, once per
                // side, each with its own normal, so the axe reads correctly from either side.
                for (int t = 0; t + 8 < p.Length; t += 9)
                {
                    // Kit (x, y, z) -> joint space (-x, z, y): the handle runs along Y in the kit and along Z in
                    // the hand, the blade goes from -X to +X, the thickness from Z to Y. This is a turn, not a
                    // mirror, so the triangles keep their winding. Normals are computed after the map.
                    Vector3 a = new Vector3(-p[t], p[t + 2], p[t + 1]);
                    Vector3 b = new Vector3(-p[t + 3], p[t + 5], p[t + 4]);
                    Vector3 c = new Vector3(-p[t + 6], p[t + 8], p[t + 7]);
                    if ((string)part["name"] == "grip")
                    {
                        gripX += a.x + b.x + c.x; gripY += a.y + b.y + c.y; gripN += 3;
                    }
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                    int i = t / 3; // index of vertex a; uvs hold two floats per vertex
                    AddVertex(verts, normals, uvs, a, n, uv, i);
                    AddVertex(verts, normals, uvs, b, n, uv, i + 1);
                    AddVertex(verts, normals, uvs, c, n, uv, i + 2);
                    int k = verts.Count - 3;
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                    // The other side of the same triangle, reversed.
                    AddVertex(verts, normals, uvs, a, -n, uv, i);
                    AddVertex(verts, normals, uvs, c, -n, uv, i + 2);
                    AddVertex(verts, normals, uvs, b, -n, uv, i + 1);
                    k = verts.Count - 3;
                    tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                }
                subs.Add(tris.ToArray());
                mats.Add(MaterialFor((string)part["texture"], baseMat));
            }

            Mesh mesh = new Mesh { name = prefab };
            if (verts.Count == 0) return;
            Bounds kitBounds = new Bounds(verts[0], Vector3.zero);
            foreach (Vector3 v in verts) kitBounds.Encapsulate(v);
            // The game puts the item's `attach` node in the joint with position and rotation reset, so that
            // node's space is the hand. Vanilla AxeIron there (tools/reference attach-points-items.tsv):
            // handle along Z through (0,0), Z from -0.16 to +0.72, head toward +X.
            Transform attach = go.transform.Find("attach") ?? go.transform;
            float length = kitBounds.size.z;
            float scale = length > 0f ? HandleLength / length : 1f;
            Vector3 centre = gripN > 0 ? new Vector3(gripX / gripN, gripY / gripN, 0f) : Vector3.zero;
            Vector3 offset = new Vector3(-centre.x * scale, -centre.y * scale, HandleMin - kitBounds.min.z * scale);
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i] * scale + offset;

            // Joint space -> the mesh's own space. The vertices are then moved into the mesh's local space, and the
            // normals are recomputed from the final triangles: a non-uniform transform would skew the old ones.
            Matrix4x4 jointToMesh = (attach.worldToLocalMatrix * target.transform.localToWorldMatrix).inverse;
            for (int i = 0; i < verts.Count; i++) verts[i] = jointToMesh.MultiplyPoint3x4(verts[i]);
            for (int t = 0; t + 2 < verts.Count; t += 3)
            {
                Vector3 n = Vector3.Cross(verts[t + 1] - verts[t], verts[t + 2] - verts[t]).normalized;
                normals[t] = n; normals[t + 1] = n; normals[t + 2] = n;
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();

            target.sharedMesh = mesh;
            renderer.sharedMaterials = mats.ToArray();
            // Anything else the vanilla axe drew would sit on top of the new model.
            foreach (MeshRenderer other in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (other != renderer) other.enabled = false;
            }
            Plugin.Log.LogInfo(prefab + ": " + subs.Count + " parts, scale " + scale.ToString("0.000") + ", handle " + HandleMin.ToString("0.00") + ".." + (HandleMin + HandleLength).ToString("0.00") + " m on Z, grip centre " + centre.ToString("0.000") + ", attach " + attach.name + ", mesh node scale " + target.transform.lossyScale.ToString("0.000"));
        }

        /// <summary>Bounds of every mesh under the prefab, measured in the prefab's own space.</summary>
        private static Bounds PrefabBounds(GameObject go, MeshFilter[] filters)
        {
            Bounds all = new Bounds();
            bool first = true;
            foreach (MeshFilter f in filters)
            {
                if (f.sharedMesh == null) continue;
                Bounds lb = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    Vector3 local = go.transform.InverseTransformPoint(f.transform.TransformPoint(lb.center + Vector3.Scale(lb.extents, sign)));
                    if (first) { all = new Bounds(local, Vector3.zero); first = false; }
                    else all.Encapsulate(local);
                }
            }
            return all;
        }

        private static void AddVertex(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, Vector3 pos, Vector3 normal, float[] uv, int index)
        {
            verts.Add(pos);
            normals.Add(normal);
            uvs.Add(new Vector2(uv[index * 2], uv[index * 2 + 1]));
        }

        private static Material MaterialFor(string texture, Material baseMat)
        {
            Material mat = new Material(baseMat);
            Texture2D tex = LoadTexture(texture + ".png");
            if (tex != null) mat.mainTexture = tex;
            return mat;
        }

        private static Texture2D LoadTexture(string file)
        {
            Texture2D tex;
            if (Textures.TryGetValue(file, out tex)) return tex;
            byte[] png = ReadBytes(file);
            if (png == null) return null;
            tex = new Texture2D(2, 2) { filterMode = FilterMode.Point, name = file };
            // Reflection: the Unity build's ReadOnlySpan overload does not bind against net472.
            typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })
                .Invoke(null, new object[] { tex, png, false });
            Textures[file] = tex;
            return tex;
        }

        private static Sprite LoadIcon(string asset)
        {
            Texture2D tex = LoadTexture(asset + "-icon.png");
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private static byte[] ReadBytes(string file)
        {
            using (Stream s = typeof(CustomItems).Assembly.GetManifestResourceStream("lf_" + file))
            {
                if (s == null)
                {
                    Plugin.Log.LogError("Missing embedded asset " + file);
                    return null;
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private static string ReadText(string file)
        {
            byte[] bytes = ReadBytes(file);
            return bytes == null ? "{}" : Encoding.UTF8.GetString(bytes);
        }
    }
}
