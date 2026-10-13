using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Where equipment sits on a character, in numbers (dev tools only).
    ///
    /// The game spawns an item's `attach` child under a joint of the character with position and
    /// rotation reset, so the model's own zero point IS the joint: for a weapon, the middle of the
    /// palm. A model whose handle does not pass through its zero point floats beside the hand.
    /// Everything here is measured in "joint space": metres, origin and axes of the joint.
    ///
    ///   points                       the character's joints and everything worn right now
    ///   points Item [Item...]        one item, in the hand and on the back, without wearing it
    ///   points all                   a table of every wearable item in the game (points_items.tsv)
    ///   marks on|off                 small coloured balls on the character's joints
    /// </summary>
    internal static class AttachPoints
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] AxisName = { "X", "Y", "Z" };

        /// <summary>A handle further than this from the palm is visibly not in the hand.</summary>
        public const float FloatingGap = 0.05f;

        private class Joint
        {
            public string Name, Holds;
            public Func<VisEquipment, Transform> Get;
            public Color Color;
        }

        private static readonly Joint[] Joints =
        {
            new Joint { Name = "rightHand", Get = v => v.m_rightHand, Color = Color.red, Holds = "one- and two-handed weapons, tools, torch" },
            new Joint { Name = "leftHand", Get = v => v.m_leftHand, Color = Color.blue, Holds = "shield, bow, left-hand weapons" },
            new Joint { Name = "helmet", Get = v => v.m_helmet, Color = Color.yellow, Holds = "helmet" },
            new Joint { Name = "backMelee", Get = v => v.m_backMelee, Color = Color.green, Holds = "sheathed one-handed weapon, torch" },
            new Joint { Name = "backTwohandedMelee", Get = v => v.m_backTwohandedMelee, Color = Color.cyan, Holds = "sheathed two-handed weapon" },
            new Joint { Name = "backShield", Get = v => v.m_backShield, Color = Color.magenta, Holds = "shield on the back" },
            new Joint { Name = "backBow", Get = v => v.m_backBow, Color = new Color(1f, 0.5f, 0f), Holds = "bow on the back" },
            new Joint { Name = "backTool", Get = v => v.m_backTool, Color = Color.white, Holds = "tool on the back, left-hand torch" },
            new Joint { Name = "backAtgeir", Get = v => v.m_backAtgeir, Color = Color.black, Holds = "atgeir on the back" },
        };

        private static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        // ------------------------------------------------------------------ geometry

        /// <summary>One mesh of a model and the transform that takes its vertices into joint space.</summary>
        private struct Part
        {
            public Mesh Mesh;
            public Matrix4x4 ToJoint;
        }

        private class Shape
        {
            public Bounds Box;
            public bool Exact;          // measured on triangles, not on bounding boxes
            public int Triangles;
            public float Gap;           // distance from the joint to the nearest surface
            public Vector3 Nearest;
            public int Long;            // index of the longest axis
            public bool Slice;          // the plane across the long axis at the joint cuts the model
            public Vector2 SliceMin, SliceMax;
        }

        private static readonly Dictionary<int, Mesh> Readable = new Dictionary<int, Mesh>();

        /// <summary>The game's meshes are not readable from scripts; this copies one back from the graphics card.</summary>
        private static Mesh ReadableCopy(Mesh mesh)
        {
            if (mesh == null || mesh.isReadable) return mesh;
            Mesh copy;
            if (Readable.TryGetValue(mesh.GetInstanceID(), out copy)) return copy;
            try
            {
                copy = new Mesh { indexFormat = mesh.indexFormat };
                copy.SetVertexBufferParams(mesh.vertexCount, mesh.GetVertexAttributes());
                for (int i = 0; i < mesh.vertexBufferCount; i++)
                {
                    using (GraphicsBuffer vb = mesh.GetVertexBuffer(i))
                    {
                        byte[] data = new byte[vb.stride * vb.count];
                        vb.GetData(data);
                        copy.SetVertexBufferData(data, 0, 0, data.Length, i);
                    }
                }
                using (GraphicsBuffer ib = mesh.GetIndexBuffer())
                {
                    byte[] data = new byte[ib.stride * ib.count];
                    ib.GetData(data);
                    copy.SetIndexBufferParams(ib.count, mesh.indexFormat);
                    copy.SetIndexBufferData(data, 0, 0, data.Length);
                }
                copy.subMeshCount = mesh.subMeshCount;
                for (int i = 0; i < mesh.subMeshCount; i++) copy.SetSubMesh(i, mesh.GetSubMesh(i));
                if (copy.vertices.Length == 0) copy = null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[points] could not read mesh " + mesh.name + ": " + e.Message);
                copy = null;
            }
            Readable[mesh.GetInstanceID()] = copy;
            return copy;
        }

        private static Vector3 ClosestOnTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            // Closest point of triangle abc to the origin (Ericson, Real-Time Collision Detection).
            Vector3 ab = b - a, ac = c - a, ap = -a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = -b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = -c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        private static Shape Measure(List<Part> parts)
        {
            if (parts.Count == 0) return null;
            Shape shape = new Shape { Exact = true, Gap = float.MaxValue };
            bool first = true;
            List<Vector3[]> soupVerts = new List<Vector3[]>();
            List<int[]> soupTris = new List<int[]>();
            foreach (Part part in parts)
            {
                Mesh mesh = ReadableCopy(part.Mesh);
                if (mesh != null)
                {
                    Vector3[] v = mesh.vertices;
                    for (int i = 0; i < v.Length; i++)
                    {
                        v[i] = part.ToJoint.MultiplyPoint3x4(v[i]);
                        if (first) shape.Box = new Bounds(v[i], Vector3.zero);
                        else shape.Box.Encapsulate(v[i]);
                        first = false;
                    }
                    int[] t = mesh.triangles;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 p = ClosestOnTriangle(v[t[i]], v[t[i + 1]], v[t[i + 2]]);
                        if (p.magnitude < shape.Gap)
                        {
                            shape.Gap = p.magnitude;
                            shape.Nearest = p;
                        }
                    }
                    shape.Triangles += t.Length / 3;
                    soupVerts.Add(v);
                    soupTris.Add(t);
                    continue;
                }
                // No triangles to be had: fall back to the mesh's box, which only bounds the answer.
                shape.Exact = false;
                Bounds b = part.Mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = part.ToJoint.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (first) shape.Box = new Bounds(corner, Vector3.zero);
                    else shape.Box.Encapsulate(corner);
                    first = false;
                }
            }
            if (!shape.Exact)
            {
                Vector3 p = shape.Box.ClosestPoint(Vector3.zero);
                shape.Gap = p.magnitude;
                shape.Nearest = p;
            }
            Vector3 size = shape.Box.size;
            shape.Long = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

            // Cut the model with the plane through the joint, across its length: that is the piece of
            // handle (or rim, or grip) the hand is supposed to be closed around.
            int u = (shape.Long + 1) % 3, w = (shape.Long + 2) % 3;
            for (int s = 0; s < soupVerts.Count; s++)
            {
                Vector3[] v = soupVerts[s];
                int[] t = soupTris[s];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        Vector3 a = v[t[i + e]], b = v[t[i + (e + 1) % 3]];
                        float da = a[shape.Long], db = b[shape.Long];
                        if ((da > 0f) == (db > 0f)) continue;
                        Vector3 hit = a + (b - a) * (da / (da - db));
                        Vector2 q = new Vector2(hit[u], hit[w]);
                        // Only the cut nearest the hand: further out the same plane may catch a blade or a guard.
                        if (q.magnitude > 0.3f) continue;
                        if (!shape.Slice)
                        {
                            shape.SliceMin = shape.SliceMax = q;
                            shape.Slice = true;
                        }
                        else
                        {
                            shape.SliceMin = Vector2.Min(shape.SliceMin, q);
                            shape.SliceMax = Vector2.Max(shape.SliceMax, q);
                        }
                    }
                }
            }
            return shape;
        }

        private static string F(float v) { return v.ToString("+0.00;-0.00", Inv); }

        private static string V(Vector3 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")"; }

        private static string Describe(Shape s, bool handHeld, string indent)
        {
            if (s == null) return indent + "no visible model\n";
            StringBuilder sb = new StringBuilder();
            Vector3 min = s.Box.min, max = s.Box.max;
            sb.AppendLine(indent + "box   x " + F(min.x) + " .. " + F(max.x) + "   y " + F(min.y) + " .. " + F(max.y) + "   z " + F(min.z) + " .. " + F(max.z) + (s.Exact ? "   (" + s.Triangles + " triangles)" : "   (boxes only, mesh unreadable)"));
            int l = s.Long, u = (l + 1) % 3, w = (l + 2) % 3;
            float length = s.Box.size[l];
            float along = length > 0f ? -min[l] / length : 0f;
            sb.AppendLine(indent + "long axis " + AxisName[l] + ", " + length.ToString("0.00", Inv) + " m; the joint is " + (along * 100f).ToString("0", Inv) + "% of the way from the -" + AxisName[l] + " end"
                + "; the bulk sits " + F(s.Box.center[u]) + " along " + AxisName[u] + " and " + F(s.Box.center[w]) + " along " + AxisName[w]);
            sb.Append(indent + "nearest surface " + s.Gap.ToString("0.00", Inv) + " m from the joint, at " + V(s.Nearest));
            if (handHeld) sb.Append(s.Gap > FloatingGap ? "   <-- NOT IN THE HAND" : "   ok");
            sb.AppendLine();
            if (s.Slice)
            {
                Vector2 c = (s.SliceMin + s.SliceMax) * 0.5f, size = s.SliceMax - s.SliceMin;
                sb.Append(indent + "what the hand closes around (cut across " + AxisName[l] + " at the joint): centre " + AxisName[u] + " " + F(c.x) + ", " + AxisName[w] + " " + F(c.y) + ", " + size.x.ToString("0.00", Inv) + " x " + size.y.ToString("0.00", Inv) + " m");
                if (handHeld && c.magnitude > 0.02f) sb.Append("   -> move the model " + AxisName[u] + " " + F(-c.x) + ", " + AxisName[w] + " " + F(-c.y));
                sb.AppendLine();
            }
            else if (s.Exact) sb.AppendLine(indent + "nothing of the model crosses the joint along " + AxisName[l] + ": the joint is off the end of it or beside it");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ items as the game would attach them

        private static bool IsWearable(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsHandHeld(ItemDrop.ItemData.ItemType type)
        {
            return type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                || type == ItemDrop.ItemData.ItemType.Bow || type == ItemDrop.ItemData.ItemType.Torch || type == ItemDrop.ItemData.ItemType.Tool || type == ItemDrop.ItemData.ItemType.Attach_Atgeir;
        }

        /// <summary>The joint an item of this type is worn on, and the one it is put away on (as VisEquipment decides).</summary>
        private static void JointsFor(ItemDrop.ItemData.SharedData shared, out string worn, out string back)
        {
            ItemDrop.ItemData.ItemType type = shared.m_itemType;
            ItemDrop.ItemData.ItemType backType = shared.m_attachOverride != ItemDrop.ItemData.ItemType.None ? shared.m_attachOverride : type;
            worn = type == ItemDrop.ItemData.ItemType.Helmet ? "helmet"
                : type == ItemDrop.ItemData.ItemType.Shield || type == ItemDrop.ItemData.ItemType.Bow || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ? "leftHand"
                : IsHandHeld(type) ? "rightHand" : null;
            switch (backType)
            {
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: back = "backMelee"; break;
                case ItemDrop.ItemData.ItemType.Bow: back = "backBow"; break;
                case ItemDrop.ItemData.ItemType.Tool: back = "backTool"; break;
                case ItemDrop.ItemData.ItemType.Attach_Atgeir: back = "backAtgeir"; break;
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: back = "backTwohandedMelee"; break;
                case ItemDrop.ItemData.ItemType.Shield: back = "backShield"; break;
                default: back = null; break;
            }
        }

        private static VisEquipment ReferenceBody()
        {
            if (Player.m_localPlayer != null) return Player.m_localPlayer.GetComponent<VisEquipment>();
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
            return prefab != null ? prefab.GetComponent<VisEquipment>() : null;
        }

        private static float JointScale(string joint)
        {
            VisEquipment vis = ReferenceBody();
            Joint j = Joints.FirstOrDefault(x => x.Name == joint);
            Transform t = vis != null && j != null ? j.Get(vis) : null;
            return t != null ? t.lossyScale.x : 1f;
        }

        /// <summary>The model the game would spawn for the item, as parts in the space of the joint it goes on.</summary>
        private static List<Part> PrefabParts(GameObject prefab, bool onBack, string joint, out string node)
        {
            List<Part> parts = new List<Part>();
            node = null;
            Transform attach = null;
            // Same search, in the same order, as VisEquipment.AttachItem.
            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                Transform child = prefab.transform.GetChild(i);
                if (onBack && child.name == "attach_back") { attach = child; break; }
                if (child.name == "attach" || (!onBack && child.name == "attach_skin")) { attach = child; break; }
            }
            if (attach == null) return parts;
            node = attach.name;
            if (attach.name == "attach_skin") return parts;
            Transform offset = prefab.transform.Find("equipoffset");
            // SetParent keeps the model's world scale, so the joint's own scale (0.95 on the hands, 95 on the back)
            // never reaches the model; it only stretches the equipoffset, which is given in the joint's units.
            Matrix4x4 place = Matrix4x4.TRS(offset != null ? offset.position * JointScale(joint) : Vector3.zero, offset != null ? offset.rotation : Quaternion.identity, attach.localScale)
                * attach.worldToLocalMatrix;
            if (offset != null) node += " + equipoffset";
            foreach (MeshFilter mf in attach.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled || !Shown(mf.transform, attach)) continue;
                parts.Add(new Part { Mesh = mf.sharedMesh, ToJoint = place * mf.transform.localToWorldMatrix });
            }
            return parts;
        }

        private static bool Shown(Transform t, Transform root)
        {
            // The attach node itself is switched on by the game; anything switched off below it stays off.
            for (; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
            }
            return true;
        }

        public static string Item(string name)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return name + ": no such item";
            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            string worn, back, node;
            JointsFor(shared, out worn, out back);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(name + " (" + shared.m_itemType + (shared.m_attachOverride != ItemDrop.ItemData.ItemType.None ? ", put away as " + shared.m_attachOverride : "") + ")");
            sb.AppendLine("  children of the prefab: " + string.Join(", ", Enumerable.Range(0, prefab.transform.childCount).Select(i => prefab.transform.GetChild(i).name)));
            List<Part> parts = PrefabParts(prefab, false, worn, out node);
            if (node == "attach_skin")
            {
                Transform skin = prefab.transform.Find("attach_skin");
                SkinnedMeshRenderer[] skinned = skin.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                sb.AppendLine("  worn: skinned onto the body's own bones (" + skinned.Length + " skinned mesh(es), " + skinned.Sum(r => r.bones.Length) + " bone slots) - no attach point, it follows the skeleton");
            }
            else if (node == null) sb.AppendLine("  worn: no `attach` child - nothing is shown (the body's material or texture may change instead)");
            else
            {
                sb.AppendLine("  worn on " + (worn ?? "the body") + " (`" + node + "`):");
                sb.Append(Describe(Measure(parts), IsHandHeld(shared.m_itemType), "    "));
            }
            if (back != null)
            {
                parts = PrefabParts(prefab, true, back, out node);
                if (node != null && node != "attach_skin")
                {
                    sb.AppendLine("  put away on " + back + " (`" + node + "`):");
                    sb.Append(Describe(Measure(parts), false, "    "));
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Every wearable item in the game as one table, the vanilla ones being the reference for new models.</summary>
        public static string All()
        {
            if (ObjectDB.instance == null) return "no item database yet";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("item\ttype\tjoint\tnode\texact\tlong axis\tlength\tjoint at % of length\tgap\tmin x\tmax x\tmin y\tmax y\tmin z\tmax z\tcut centre u\tcut centre w\tcut size u\tcut size w");
            int count = 0, floating = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.OrderBy(p => p.name))
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || !IsWearable(drop.m_itemData.m_shared.m_itemType)) continue;
                string worn, back, node;
                JointsFor(drop.m_itemData.m_shared, out worn, out back);
                List<Part> parts = PrefabParts(prefab, false, worn, out node);
                Shape s = Measure(parts);
                sb.Append(prefab.name + "\t" + drop.m_itemData.m_shared.m_itemType + "\t" + (worn ?? "body") + "\t" + (node ?? "-"));
                if (s != null)
                {
                    Vector3 min = s.Box.min, max = s.Box.max;
                    float length = s.Box.size[s.Long];
                    sb.Append("\t" + (s.Exact ? "yes" : "no") + "\t" + AxisName[s.Long] + "\t" + length.ToString("0.00", Inv) + "\t" + (length > 0f ? -min[s.Long] / length * 100f : 0f).ToString("0", Inv) + "\t" + s.Gap.ToString("0.000", Inv)
                        + "\t" + F(min.x) + "\t" + F(max.x) + "\t" + F(min.y) + "\t" + F(max.y) + "\t" + F(min.z) + "\t" + F(max.z));
                    if (s.Slice)
                    {
                        Vector2 c = (s.SliceMin + s.SliceMax) * 0.5f, size = s.SliceMax - s.SliceMin;
                        sb.Append("\t" + F(c.x) + "\t" + F(c.y) + "\t" + size.x.ToString("0.00", Inv) + "\t" + size.y.ToString("0.00", Inv));
                    }
                    if (IsHandHeld(drop.m_itemData.m_shared.m_itemType) && s.Gap > FloatingGap) floating++;
                }
                sb.AppendLine();
                count++;
            }
            string path = Path.Combine(SimHost.DataDir, "points_items.tsv");
            File.WriteAllText(path, sb.ToString());
            return count + " wearable items, " + floating + " hand-held ones further than " + FloatingGap.ToString("0.00", Inv) + " m from the palm -> " + path;
        }

        // ------------------------------------------------------------------ the character and what it wears now

        private static Matrix4x4 Metric(Transform joint)
        {
            // Joint space in metres: the joint's place and axes without its scale.
            return Matrix4x4.TRS(joint.position, joint.rotation, Vector3.one).inverse;
        }

        private static List<Part> LiveParts(GameObject model, Transform joint)
        {
            List<Part> parts = new List<Part>();
            Matrix4x4 toJoint = Metric(joint);
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                parts.Add(new Part { Mesh = mf.sharedMesh, ToJoint = toJoint * mf.transform.localToWorldMatrix });
            }
            return parts;
        }

        private static string BonePath(Transform t, Transform root)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < 3 && t != null && t != root; i++, t = t.parent) names.Insert(0, t.name);
            return string.Join("/", names);
        }

        public static string Character()
        {
            Player player = Player.m_localPlayer;
            VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
            if (vis == null) return "no player";
            StringBuilder sb = new StringBuilder();
            Transform root = player.transform;
            sb.AppendLine("Joints equipment hangs on. Position is in the character's own space in the current pose: x to their right, y up from the feet, z forward. Metres.");
            foreach (Joint j in Joints)
            {
                Transform t = j.Get(vis);
                if (t == null)
                {
                    sb.AppendLine("  " + j.Name.PadRight(20) + "missing");
                    continue;
                }
                sb.AppendLine("  " + j.Name.PadRight(20) + V(root.InverseTransformPoint(t.position)) + "  scale " + t.lossyScale.x.ToString("0.###", Inv) + "  " + BonePath(t, root) + "  - " + j.Holds);
            }
            Animator animator = player.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                sb.AppendLine("Skeleton (skinned armour, capes and belts follow these):");
                foreach (HumanBodyBones bone in Bones)
                {
                    Transform t = animator.GetBoneTransform(bone);
                    if (t != null) sb.AppendLine("  " + bone.ToString().PadRight(20) + V(root.InverseTransformPoint(t.position)) + "  " + t.name);
                }
            }
            sb.Append(Worn());
            return sb.ToString().TrimEnd();
        }

        /// <summary>Measures the models the character is holding and carrying right now.</summary>
        public static string Worn()
        {
            Player player = Player.m_localPlayer;
            VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
            if (vis == null) return "no player";
            Traverse humanoid = Traverse.Create(player);
            string[,] slots =
            {
                { "m_rightItemInstance", "rightHand", "m_rightItem" }, { "m_leftItemInstance", "leftHand", "m_leftItem" }, { "m_helmetItemInstance", "helmet", "m_helmetItem" },
                { "m_rightBackItemInstance", "", "m_hiddenRightItem" }, { "m_leftBackItemInstance", "", "m_hiddenLeftItem" },
            };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < slots.GetLength(0); i++)
            {
                GameObject model = AccessTools.Field(typeof(VisEquipment), slots[i, 0]).GetValue(vis) as GameObject;
                if (model == null) continue;
                Transform joint = model.transform.parent;
                Joint known = Joints.FirstOrDefault(j => j.Get(vis) == joint);
                ItemDrop.ItemData item = humanoid.Field(slots[i, 2]).GetValue<ItemDrop.ItemData>();
                string name = item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : model.name;
                bool handHeld = slots[i, 1].EndsWith("Hand") && item != null && IsHandHeld(item.m_shared.m_itemType);
                sb.AppendLine("worn now: " + name + " on " + (known != null ? known.Name : joint != null ? joint.name : "?"));
                sb.Append(Describe(Measure(LiveParts(model, joint)), handHeld, "    "));
            }
            return sb.Length == 0 ? "nothing held or carried\n" : sb.ToString();
        }

        /// <summary>The gap between the palm and the model in it right now; negative when nothing is held.</summary>
        public static float HandGap(out string item)
        {
            item = null;
            Player player = Player.m_localPlayer;
            VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
            if (vis == null) return -1f;
            float worst = -1f;
            Traverse humanoid = Traverse.Create(player);
            foreach (string[] slot in new[] { new[] { "m_rightItemInstance", "m_rightItem" }, new[] { "m_leftItemInstance", "m_leftItem" } })
            {
                GameObject model = AccessTools.Field(typeof(VisEquipment), slot[0]).GetValue(vis) as GameObject;
                ItemDrop.ItemData held = humanoid.Field(slot[1]).GetValue<ItemDrop.ItemData>();
                if (model == null || held == null || !IsHandHeld(held.m_shared.m_itemType)) continue;
                Shape s = Measure(LiveParts(model, model.transform.parent));
                if (s != null && s.Gap > worst)
                {
                    worst = s.Gap;
                    item = held.m_dropPrefab != null ? held.m_dropPrefab.name : held.m_shared.m_name;
                }
            }
            return worst;
        }

        // ------------------------------------------------------------------ markers

        private static readonly List<GameObject> Markers = new List<GameObject>();

        public static string Marks(bool on)
        {
            foreach (GameObject marker in Markers)
            {
                if (marker != null) UnityEngine.Object.Destroy(marker);
            }
            Markers.Clear();
            if (!on) return "markers off";
            Player player = Player.m_localPlayer;
            VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
            if (vis == null) return "no player";
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            foreach (Joint j in Joints)
            {
                Transform t = j.Get(vis);
                if (t == null) continue;
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "lf_mark_" + j.Name;
                UnityEngine.Object.Destroy(ball.GetComponent<Collider>());
                ball.transform.SetParent(t, false);
                ball.transform.localPosition = Vector3.zero;
                ball.transform.localScale = Vector3.one * (0.04f / Mathf.Max(0.0001f, t.lossyScale.x));
                ball.layer = t.gameObject.layer;
                if (shader != null)
                {
                    Material mat = new Material(shader);
                    mat.color = j.Color;
                    ball.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Markers.Add(ball);
            }
            return Markers.Count + " markers: " + string.Join(", ", Joints.Select(j => j.Name + " " + ColorName(j.Color)));
        }

        private static string ColorName(Color c)
        {
            if (c == Color.red) return "red";
            if (c == Color.blue) return "blue";
            if (c == Color.yellow) return "yellow";
            if (c == Color.green) return "green";
            if (c == Color.cyan) return "cyan";
            if (c == Color.magenta) return "magenta";
            if (c == Color.white) return "white";
            if (c == Color.black) return "black";
            return "orange";
        }

        public static string Command(string[] a)
        {
            if (a.Length < 2)
            {
                string text = Character();
                File.WriteAllText(Path.Combine(SimHost.DataDir, "points_character.txt"), text + "\n");
                return text;
            }
            if (a[1] == "all") return All();
            return string.Join("\n", a.Skip(1).Select(Item));
        }
    }
}
