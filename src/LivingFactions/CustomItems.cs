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
    /// One weapon the mod adds: a clone of a vanilla one whose mesh and textures are built at run time from the
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
        /// <summary>Length the model is scaled to, as the vanilla item it is cloned from (docs/reference/attach-points-items.tsv).</summary>
        public float Length = 0.88f;
        /// <summary>The part the hand closes on.</summary>
        public string Grip = "grip";
        /// <summary>Axes stand on the vanilla axe's butt (-0.16 m); swords are held by the middle of their grip.</summary>
        public bool HeldByGrip;
        /// <summary>A shield: <see cref="Grip"/> is its board, <see cref="Length"/> the board's width.</summary>
        public bool Shield;
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
            // The swords: six sold by their faction's traders (trade.cfg), the Greydwarf club taken from their guards (Rewards.cs).
            new AxeSpec
            {
                Asset = "wolf-sword", Prefab = "LF_GreyBlade", Source = "SwordIron", Length = 1.27f, HeldByGrip = true,
                NameEn = "Grey Blade", NameRu = "Серый Клинок",
                DescEn = "The sword of the Wolf Clan: a straight blade with wolf tracks on the steel, a studded grip and a wolf's head at the pommel.",
                DescRu = "Меч Клана Волка: прямой клинок с волчьими следами на стали, рукоять в заклёпках, волчья голова на навершии.",
            },
            new AxeSpec
            {
                Asset = "stag-sword", Prefab = "LF_AntlerBlade", Source = "SwordBronze", Length = 0.91f, HeldByGrip = true, Grip = "antler_grip",
                NameEn = "Antlered Blade", NameRu = "Рогатый Клинок",
                DescEn = "A broad Stag Clan blade with a hunt engraved on it, a guard of branching antler and a grip of antler and bronze.",
                DescRu = "Широкий клинок Клана Оленя с гравировкой охоты, гарда из ветвистого рога, рукоять из рога и бронзы.",
            },
            new AxeSpec
            {
                Asset = "raven-sword", Prefab = "LF_BlackBeak", Source = "SwordIron", Length = 1.27f, HeldByGrip = true,
                NameEn = "Black Beak", NameRu = "Чёрный Клюв",
                DescEn = "A blued Raven Clan falchion with a hooked beak of a tip, wings at the guard and feathers tied below them.",
                DescRu = "Синёный фальшион Клана Ворона с острием-клювом, крыльями на гарде и перьями под ними.",
            },
            new AxeSpec
            {
                Asset = "fuling-sword", Prefab = "LF_ObsidianSaber", Source = "SwordIron", Length = 1.27f, HeldByGrip = true,
                NameEn = "Obsidian Saber", NameRu = "Обсидиановая Сабля",
                DescEn = "A curved Fuling saber with a back of chipped teeth, a bone guard and trophy teeth on cords.",
                DescRu = "Изогнутая сабля фулингов: обух из колотых зубьев, костяная гарда, зубы-трофеи на шнурах.",
            },
            new AxeSpec
            {
                Asset = "dvergr-sword", Prefab = "LF_RuneSword", Source = "SwordIron", Length = 1.27f, HeldByGrip = true,
                NameEn = "Rune Sword", NameRu = "Рунный Меч",
                DescEn = "A Dvergr sword of dark alloy with glowing runes on both faces, a copper guard and a crystal in the pommel.",
                DescRu = "Меч двергов из тёмного сплава со светящимися рунами по обеим сторонам, медной гардой и кристаллом в навершии.",
            },
            new AxeSpec
            {
                Asset = "greydwarf-sword", Prefab = "LF_RootClub", Source = "MaceBronze", Length = 1.0f, HeldByGrip = true, Grip = "vine_glow",
                NameEn = "Root Club", NameRu = "Корневая Дубина",
                DescEn = "A knotted root split at the head round ten flint chips, with an amber eye at the butt. Greydwarf guards carry it.",
                DescRu = "Узловатый корень, расщеплённый на конце вокруг десяти кремней, с янтарным глазом у рукояти. Её носят стражи грейдворфов.",
            },
            // The shields: sold as stout relics by their traders, the Greydwarf one taken from their guards (Rewards.cs).
            new AxeSpec
            {
                Asset = "wolf-shield", Prefab = "LF_WolfHideShield", Source = "ShieldWood", Length = 0.79f, Shield = true, Grip = "wood_disc",
                NameEn = "Wolf-Hide Shield", NameRu = "Щит Волчьей Шкуры",
                DescEn = "A round board under a whole grey wolf pelt, its tail hanging below the rim, an iron band and boss. The Wolf Clan's shield.",
                DescRu = "Круглый щит под целой серой волчьей шкурой, хвост свисает с края, железный обод и умбон. Щит Клана Волка.",
            },
            new AxeSpec
            {
                Asset = "stag-shield", Prefab = "LF_StagTrailShield", Source = "ShieldBanded", Length = 0.93f, Shield = true, Grip = "wood_disc",
                NameEn = "Stag-Trail Shield", NameRu = "Щит Оленьей Тропы",
                DescEn = "Tan deer-hide on a round board, a bronze trim and boss, two antler tines and bronze coins round the rim. The Stag Clan's shield.",
                DescRu = "Оленья кожа на круглом щите, бронзовая оковка и умбон, два отростка рога и бронзовые монеты по краю. Щит Клана Оленя.",
            },
            new AxeSpec
            {
                Asset = "raven-shield", Prefab = "LF_RavenShield", Source = "ShieldBanded", Length = 0.52f, Shield = true, Grip = "kite_wood",
                NameEn = "Raven Kite", NameRu = "Вороний Щит",
                DescEn = "A tall kite of black-stained wood faced with black leather, a dark steel boss and raven feathers along the top. The Raven Clan's shield.",
                DescRu = "Высокий каплевидный щит из чернёного дерева в чёрной коже, тёмный стальной умбон и вороньи перья по верху. Щит Клана Ворона.",
            },
            new AxeSpec
            {
                Asset = "fuling-shield", Prefab = "LF_BoneShield", Source = "ShieldBanded", Length = 0.93f, Shield = true, Grip = "bone_disc",
                NameEn = "Bone Shield", NameRu = "Костяной Щит",
                DescEn = "One great piece of bone with a bead rim, a bone boss and two tusks crossed above. The Fuling Horde's shield.",
                DescRu = "Цельная огромная кость с бортиком, костяным умбоном и двумя скрещёнными бивнями над краем. Щит Орды фулингов.",
            },
            new AxeSpec
            {
                Asset = "dvergr-shield", Prefab = "LF_CopperShield", Source = "ShieldBanded", Length = 0.93f, Shield = true, Grip = "copper_face",
                NameEn = "Copper Shield", NameRu = "Медный Щит",
                DescEn = "Hammered copper with a bead rim, rune bands across the face and a gem in the boss. The Dvergr Hold's shield.",
                DescRu = "Кованая медь с бортиком, полосами рун по полю и самоцветом в умбоне. Щит Чертога двергов.",
            },
            new AxeSpec
            {
                Asset = "greydwarf-shield", Prefab = "LF_BarkShield", Source = "ShieldWood", Length = 0.79f, Shield = true, Grip = "bark_slabs",
                NameEn = "Bark Shield", NameRu = "Корковый Щит",
                DescEn = "Five slabs of bark bound with vine, amber resin at the heart and roots past the rim. Greydwarf guards carry it.",
                DescRu = "Пять пластов коры, стянутых лианой, янтарная смола в центре и корни за краем. Его носят стражи грейдворфов.",
            },
            new AxeSpec
            {
                Asset = "draugr-shield", Prefab = "LF_BonePlateShield", Source = "ShieldBanded", Length = 0.93f, Shield = true, Grip = "backing",
                NameEn = "Bone-Plate Shield", NameRu = "Щит Костяных Пластин",
                DescEn = "Pale bone scales over a dark backing, an iron rim, a crescent boss and bone charms on cords. The Draugr Court's shield.",
                DescRu = "Бледные костяные чешуи на тёмной основе, железный обод, умбон-полумесяц и костяные обереги на шнурах. Щит Двора драугров.",
            },
            new AxeSpec
            {
                Asset = "draugr-sword", Prefab = "LF_RustedBlade", Source = "SwordIron", Length = 1.27f, HeldByGrip = true,
                NameEn = "Rusted Blade", NameRu = "Ржавый Клинок",
                DescEn = "A long pitted blade of the Draugr Court, a skull at the guard and another at the pommel, grave cloth on the grip.",
                DescRu = "Длинный изъеденный клинок Двора драугров: череп на гарде, череп на навершии, погребальная ткань на рукояти.",
            },
        };

        private static bool _created;

        /// <summary>Which part of a model (its submesh) is the board or blade it is held by, by prefab: for the dev probes.</summary>
        internal static readonly Dictionary<string, int> BoardPart = new Dictionary<string, int>();

        /// <summary>The vanilla AxeIron in joint space (docs/reference/attach-points-items.tsv): handle on Z, -0.16..+0.72.</summary>
        private const float HandleMin = -0.16f;
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
            Name(shared, spec);
            Sprite icon = LoadIcon(spec.Asset);
            if (icon != null) shared.m_icons = new[] { icon };

            ApplyModel(go, spec);
            ItemManager.Instance.AddItem(new CustomItem(go, false));
            Plugin.Log.LogInfo("Registered " + spec.Prefab);
        }

        private static void Name(ItemDrop.ItemData.SharedData shared, AxeSpec spec)
        {
            bool ru = Texts.Lang == 1;
            // Other languages keep the item's name and tale in their own tables, under the prefab's name.
            shared.m_name = Texts.Translated(Texts.Lang, "item_" + spec.Prefab) && Texts.Lang >= 2 ? Texts.Get("item_" + spec.Prefab) : ru ? spec.NameRu : spec.NameEn;
            shared.m_description = Texts.Translated(Texts.Lang, "itemd_" + spec.Prefab) && Texts.Lang >= 2 ? Texts.Get("itemd_" + spec.Prefab) : ru ? spec.DescRu : spec.DescEn;
        }

        /// <summary>
        /// The items are made before the mod knows its language (that waits for the world and the game's
        /// own language), so they are named again once it does, and when the player picks another. Every
        /// copy in a bag shares these names with the prefab.
        /// </summary>
        public static void Rename()
        {
            if (ObjectDB.instance == null) return;
            foreach (AxeSpec spec in Axes)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(spec.Prefab);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null) Name(drop.m_itemData.m_shared, spec);
            }
        }

        /// <summary>
        /// Kit (x, y, z) to joint space. Weapons: (-x, z, y), the handle along Y in the kit runs along Z in
        /// the hand, the blade from -X to +X, the thickness from Z to Y. Shields: (x, -z, y), the board
        /// stands in the hand's XZ with its top (kit +Y) along +Z, up the forearm, and its face (kit +Z) out
        /// to -Y, as the vanilla shields have it (their tower shields are long on Z and hang mostly below the
        /// hand, docs/reference/attach-points-items.tsv). The top along +X, as before, is forward: a kite
        /// stuck out in front of the hand and lay across the back, the round boards had their tops forward.
        /// Both are turns, not mirrors, so the triangles keep their winding.
        /// </summary>
        private static Vector3 Map(AxeSpec spec, float[] p, int i)
        {
            return spec.Shield ? new Vector3(p[i], -p[i + 2], p[i + 1]) : new Vector3(-p[i], p[i + 2], p[i + 1]);
        }

        /// <summary>
        /// How far the back of a shield's board stands off the hand's joint at the wrist, out toward its face.
        /// The forearm runs across the back of the board, from the wrist (x -0.10, y -0.03 in the joint's space)
        /// to the elbow (x -0.36, y -0.06 to -0.12 with the poses of the idle animations; measured with
        /// `shieldfit`), some 5 cm thick each way. At 2 cm off the joint and lying flat, as before, the elbow end
        /// of the arm came through the board on most of the people carrying one.
        /// </summary>
        private const float ShieldStandoff = 0.115f;

        /// <summary>
        /// The board turns with the forearm, its elbow side further out (about the joint's Z, the shield's up), as
        /// the game's own shields lie (they reach from y -0.24 in front to +0.11 behind over a metre of board).
        /// </summary>
        private const float ShieldTilt = 17f, WristX = -0.10f;

        /// <summary>The top of a tall shield's board above the hand, as the vanilla kite and tower shields hang (+0.38 to +0.69).</summary>
        private const float TallShieldTop = 0.45f;

        /// <summary>Replaces the clone's visible mesh with the kit's model, keeping the clone's overall size.</summary>
        private static void ApplyModel(GameObject go, AxeSpec spec)
        {
            string asset = spec.Asset, prefab = spec.Prefab;
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
            float gripX = 0f, gripY = 0f, gripZ = 0f;
            Bounds grip = new Bounds();
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
                    // Into joint space (see Map); normals are computed after the map.
                    Vector3 a = Map(spec, p, t), b = Map(spec, p, t + 3), c = Map(spec, p, t + 6);
                    if ((string)part["name"] == spec.Grip)
                    {
                        gripX += a.x + b.x + c.x; gripY += a.y + b.y + c.y; gripZ += a.z + b.z + c.z; gripN += 3;
                        if (gripN == 3) grip = new Bounds(a, Vector3.zero);
                        grip.Encapsulate(a); grip.Encapsulate(b); grip.Encapsulate(c);
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
                if ((string)part["name"] == spec.Grip) BoardPart[prefab] = subs.Count;
                subs.Add(tris.ToArray());
                mats.Add(MaterialFor((string)part["texture"], baseMat, spec.Shield));
            }

            Mesh mesh = new Mesh { name = prefab };
            if (verts.Count == 0) return;
            Bounds kitBounds = new Bounds(verts[0], Vector3.zero);
            foreach (Vector3 v in verts) kitBounds.Encapsulate(v);
            // The game puts the item's `attach` node in the joint with position and rotation reset, so that
            // node's space is the hand. Vanilla AxeIron there (tools/reference attach-points-items.tsv):
            // handle along Z through (0,0), Z from -0.16 to +0.72, head toward +X.
            Transform attach = go.transform.Find("attach") ?? go.transform;
            // A shield is sized by the width of its board, the rest is sized by its length.
            float length = spec.Shield ? grip.size.x : kitBounds.size.z;
            float scale = length > 0f ? spec.Length / length : 1f;
            Vector3 centre = gripN > 0 ? new Vector3(gripX / gripN, gripY / gripN, gripZ / gripN) : Vector3.zero;
            if (gripN == 0 && spec.HeldByGrip) Plugin.Log.LogWarning(prefab + ": no part named " + spec.Grip + " to hold it by");
            // A shield's board centred where the vanilla one is (x +0.045, z +0.025), its back against the arm;
            // a tall one hangs below the hand, its top no higher than the vanilla kites'.
            float boardZ = 0.025f - grip.center.z * scale;
            if (spec.Shield && grip.size.z > grip.size.x * 1.3f) boardZ = Mathf.Min(boardZ, TallShieldTop - grip.max.z * scale);
            Vector3 offset = spec.Shield
                ? new Vector3(0.045f - grip.center.x * scale, -ShieldStandoff - grip.max.y * scale, boardZ)
                : spec.HeldByGrip
                ? -centre * scale
                : new Vector3(-centre.x * scale, -centre.y * scale, HandleMin - kitBounds.min.z * scale);
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i] * scale + offset;
            if (spec.Shield)
            {
                // Turned about the shield's up through the back of the board at the wrist: the elbow side out, the far side in.
                Quaternion tilt = Quaternion.AngleAxis(ShieldTilt, Vector3.forward);
                Vector3 pivot = new Vector3(WristX, -ShieldStandoff, 0f);
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 v = verts[i] - pivot;
                    verts[i] = pivot + tilt * new Vector3(v.x, v.y, 0f) + new Vector3(0f, 0f, v.z);
                }
            }

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
            Plugin.Log.LogInfo(prefab + ": " + subs.Count + " parts, scale " + scale.ToString("0.000") + ", " + (kitBounds.min.z * scale + offset.z).ToString("0.00") + ".." + (kitBounds.max.z * scale + offset.z).ToString("0.00") + " m on Z, grip centre " + centre.ToString("0.000") + ", attach " + attach.name + ", mesh node scale " + target.transform.lossyScale.ToString("0.000"));
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

        private static Material MaterialFor(string texture, Material baseMat, bool matte)
        {
            Material mat = new Material(baseMat);
            if (matte)
            {
                // The vanilla shields' material shines and glows its own pattern: the kit's paint carries the look.
                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", null);
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
                if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", null);
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.82f, 0.82f, 0.82f));
            }
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
