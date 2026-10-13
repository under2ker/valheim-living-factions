using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Dev camera that frames one piece of the player's equipment (dev tools only). It follows the
    /// item itself, not a fixed world point, so the same command works for an axe in the hand, a
    /// shield on the arm, a helmet, a cape or a weapon slung on the back.
    ///
    ///   look hand                     the item in the right hand, in profile
    ///   look shield front             named angle: side / front / rear / face / left / right
    ///   look cape 150 10 1.3          bearing and elevation in degrees, zoom factor
    ///   look auto                     the slot of the item last given by `gear` or `equip`
    ///   look off
    ///
    /// Bearing is measured around the player: 0 is straight in front of them looking back at
    /// their face, 90 is from their right side, 180 from behind.
    /// </summary>
    internal static class GearView
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const float Fov = 40f;

        private class Slot
        {
            public string Name;
            /// <summary>VisEquipment fields holding the spawned item models; none = a region of the body.</summary>
            public string[] Fields;
            public Func<VisEquipment, Transform> Joint;
            /// <summary>Fallback region when the item has no model of its own: height above the feet and radius.</summary>
            public float Height, Radius;
            public float Elevation;
            /// <summary>Named angles; the first one is the default and the set is what `gearshots` takes.</summary>
            public string[] Labels;
            public float[] Bearings;
        }

        private static Slot S(string name, string[] fields, Func<VisEquipment, Transform> joint, float height, float radius, float elevation, string labels, params float[] bearings)
        {
            return new Slot { Name = name, Fields = fields, Joint = joint, Height = height, Radius = radius, Elevation = elevation, Labels = labels.Split(' '), Bearings = bearings };
        }

        private static readonly string[] None = new string[0];

        private static readonly Slot[] Slots =
        {
            S("hand", new[] { "m_rightItemInstance" }, v => v.m_rightHand, 1.0f, 0.55f, 5f, "side front rear", 90f, 35f, 150f),
            S("bow", new[] { "m_leftItemInstance" }, v => v.m_leftHand, 1.0f, 0.7f, 5f, "side front rear", -90f, -35f, -150f),
            S("shield", new[] { "m_leftItemInstance" }, v => v.m_leftHand, 1.0f, 0.55f, 5f, "face front side", -60f, 0f, -110f),
            S("helmet", new[] { "m_helmetItemInstance" }, v => v.m_helmet, 1.65f, 0.3f, 5f, "front side rear", 30f, 90f, 180f),
            S("chest", None, null, 1.25f, 0.5f, 5f, "front side rear", 20f, 90f, 180f),
            S("legs", None, null, 0.5f, 0.6f, 5f, "front side rear", 20f, 90f, 180f),
            S("cape", None, null, 1.0f, 0.85f, 10f, "rear side front", 180f, 115f, 20f),
            S("belt", None, null, 0.95f, 0.4f, 5f, "front side rear", 0f, 90f, 180f),
            S("back", new[] { "m_rightBackItemInstance", "m_leftBackItemInstance" }, null, 1.2f, 0.7f, 10f, "rear side left", 180f, 120f, -120f),
            S("full", None, null, 0.95f, 1.15f, 8f, "front side rear", 30f, 90f, 180f),
        };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "weapon", "hand" }, { "axe", "hand" }, { "sword", "hand" }, { "mace", "hand" }, { "club", "hand" }, { "knife", "hand" },
            { "spear", "hand" }, { "atgeir", "hand" }, { "tool", "hand" }, { "pickaxe", "hand" }, { "torch", "hand" }, { "right", "hand" },
            { "offhand", "bow" }, { "left", "bow" }, { "head", "helmet" }, { "helm", "helmet" }, { "armor", "chest" }, { "armour", "chest" },
            { "pants", "legs" }, { "shoulder", "cape" }, { "cloak", "cape" }, { "utility", "belt" }, { "sheathed", "back" }, { "body", "full" },
        };

        private static Slot _slot;
        private static float _bearing, _elevation, _zoom = 1f;

        /// <summary>The item last handed over by `gear` / `equip`; what `look auto` frames.</summary>
        public static ItemDrop.ItemData LastItem;

        public static bool Active { get { return _slot != null; } }

        public static void Off() { _slot = null; }

        private static Slot Find(string name)
        {
            string alias;
            if (Aliases.TryGetValue(name, out alias)) name = alias;
            return Slots.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Which view suits an item of this kind.</summary>
        public static string SlotFor(ItemDrop.ItemData item)
        {
            if (item == null) return "full";
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Shield: return "shield";
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return "bow";
                case ItemDrop.ItemData.ItemType.Helmet: return "helmet";
                case ItemDrop.ItemData.ItemType.Chest: return "chest";
                case ItemDrop.ItemData.ItemType.Legs: return "legs";
                case ItemDrop.ItemData.ItemType.Shoulder: return "cape";
                case ItemDrop.ItemData.ItemType.Utility: return "belt";
                case ItemDrop.ItemData.ItemType.Trinket: return "full";
                default: return "hand";
            }
        }

        /// <summary>True for things that go on the back when put away, so a sheathed shot makes sense.</summary>
        public static bool IsHandItem(ItemDrop.ItemData item)
        {
            string slot = SlotFor(item);
            return slot == "hand" || slot == "bow" || slot == "shield";
        }

        /// <summary>The named angles `gearshots` takes for the slot.</summary>
        public static string[] Angles(string slot)
        {
            Slot s = Find(slot);
            return s != null ? s.Labels : new string[0];
        }

        public static string Look(string[] a)
        {
            if (a.Length < 2 || a[1] == "off")
            {
                Off();
                return "gear camera off";
            }
            string name = a[1] == "auto" ? SlotFor(LastItem) : a[1];
            Slot slot = Find(name);
            if (slot == null) return "no such view; use one of: auto " + string.Join(" ", Slots.Select(s => s.Name)) + " (or " + string.Join(" ", Aliases.Keys) + ")";
            float bearing = slot.Bearings[0];
            if (a.Length > 2)
            {
                int named = Array.FindIndex(slot.Labels, l => l.Equals(a[2], StringComparison.OrdinalIgnoreCase));
                if (named >= 0) bearing = slot.Bearings[named];
                else if (a[2] == "front") bearing = 0f;
                else if (a[2] == "rear" || a[2] == "behind") bearing = 180f;
                else if (a[2] == "right") bearing = 90f;
                else if (a[2] == "left") bearing = -90f;
                else if (a[2] == "top") bearing = slot.Bearings[0];
                else if (!float.TryParse(a[2], NumberStyles.Float, Inv, out bearing)) return "angle is a number of degrees or one of: " + string.Join(" ", slot.Labels) + " front rear left right top";
            }
            _elevation = a.Length > 2 && a[2] == "top" ? 70f : slot.Elevation;
            if (a.Length > 3) _elevation = float.Parse(a[3], Inv);
            _zoom = a.Length > 4 ? Mathf.Clamp(float.Parse(a[4], Inv), 0.3f, 6f) : 1f;
            _bearing = bearing;
            _slot = slot;
            return "looking at " + slot.Name + " bearing " + bearing.ToString("0", Inv) + " elevation " + _elevation.ToString("0", Inv) + (Player.m_localPlayer != null && Models(Player.m_localPlayer, slot).Count == 0 && slot.Fields.Length > 0 ? " (nothing there yet)" : "");
        }

        /// <summary>Turns the player so the side the camera is on faces the sun: face lit [degrees off the camera axis] | face &lt;degrees&gt;.</summary>
        public static string Face(string[] a)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            float yaw;
            if (a.Length > 1 && a[1] == "lit")
            {
                if (EnvMan.instance == null) return "no world";
                Vector3 sun = -EnvMan.instance.GetSunDirection();
                // Well off the camera axis: lit from the side an item keeps its shading and polished metal does not glare.
                yaw = Mathf.Atan2(sun.x, sun.z) * Mathf.Rad2Deg - (Active ? _bearing : 0f) + (a.Length > 2 ? float.Parse(a[2], Inv) : 65f);
            }
            else if (a.Length > 1) yaw = float.Parse(a[1], Inv);
            else return "usage: lf face lit | <degrees>";
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            player.transform.rotation = rot;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null) body.rotation = rot;
            player.SetLookDir(rot * Vector3.forward);
            return "facing " + Mathf.Repeat(yaw, 360f).ToString("0", Inv);
        }

        private static List<GameObject> Models(Player player, Slot slot)
        {
            List<GameObject> models = new List<GameObject>();
            VisEquipment vis = player.GetComponent<VisEquipment>();
            if (vis == null) return models;
            foreach (string field in slot.Fields)
            {
                object value = AccessTools.Field(typeof(VisEquipment), field).GetValue(vis);
                GameObject one = value as GameObject;
                if (one != null) models.Add(one);
                List<GameObject> many = value as List<GameObject>;
                if (many != null) models.AddRange(many.Where(g => g != null));
            }
            return models;
        }

        private static bool ModelBounds(List<GameObject> models, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (GameObject model in models)
            {
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (any) bounds.Encapsulate(r.bounds);
                    else bounds = r.bounds;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>What the camera should centre on and how big it is.</summary>
        private static void Frame(Player player, Slot slot, out Vector3 centre, out float radius)
        {
            Bounds bounds;
            if (ModelBounds(Models(player, slot), out bounds))
            {
                centre = bounds.center;
                radius = Mathf.Clamp(bounds.extents.magnitude, 0.2f, 2.2f);
                return;
            }
            VisEquipment vis = player.GetComponent<VisEquipment>();
            Transform joint = slot.Joint != null && vis != null ? slot.Joint(vis) : null;
            centre = joint != null ? joint.position : player.transform.position + Vector3.up * slot.Height;
            radius = slot.Radius;
        }

        /// <summary>Called instead of the game's own camera update while a gear view is on.</summary>
        public static bool Place(GameCamera cam)
        {
            Player player = Player.m_localPlayer;
            if (_slot == null || player == null) return false;
            Vector3 centre;
            float radius;
            Frame(player, _slot, out centre, out radius);
            float dist = Mathf.Max(0.6f, radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) * 1.1f) * _zoom;
            Quaternion yaw = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            Vector3 dir = yaw * (Quaternion.Euler(-_elevation, _bearing, 0f) * Vector3.forward);
            Vector3 pos = centre + dir * dist;
            float ground;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out ground)) pos.y = Mathf.Max(pos.y, ground + 0.15f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(centre - pos);
            Camera main = cam.GetComponent<Camera>();
            if (main != null) main.fieldOfView = Fov;
            if (cam.m_skyCamera != null) cam.m_skyCamera.fieldOfView = Fov;
            return true;
        }

        /// <summary>Puts hand items on the back or takes them out again.</summary>
        public static string Sheathe(bool away)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            if (away) return player.HideHandItems(false, false) ? "put away" : "nothing in the hands";
            AccessTools.Method(typeof(Humanoid), "ShowHandItems").Invoke(player, new object[] { false, false });
            return "drawn";
        }

        public static string Unequip(string[] a)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            if (a.Length < 2 || a[1] == "all")
            {
                player.UnequipAllItems();
                return "everything taken off";
            }
            ItemDrop.ItemData item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == a[1] && player.IsItemEquiped(i));
            if (item == null) return a[1] + " is not worn";
            player.UnequipItem(item, false);
            return "took off " + a[1];
        }

        /// <summary>What is worn where and how big each visible model is: the numbers behind the pictures.</summary>
        public static string Describe()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "no player";
            Traverse t = Traverse.Create(player);
            StringBuilder sb = new StringBuilder();
            string[,] worn =
            {
                { "right", "m_rightItem", "hand" }, { "left", "m_leftItem", "bow" }, { "helmet", "m_helmetItem", "helmet" }, { "chest", "m_chestItem", "" },
                { "legs", "m_legItem", "" }, { "shoulder", "m_shoulderItem", "" }, { "utility", "m_utilityItem", "" }, { "trinket", "m_trinketItem", "" },
                { "hidden right", "m_hiddenRightItem", "back" }, { "hidden left", "m_hiddenLeftItem", "back" },
            };
            for (int i = 0; i < worn.GetLength(0); i++)
            {
                ItemDrop.ItemData item = t.Field(worn[i, 1]).GetValue<ItemDrop.ItemData>();
                if (item == null) continue;
                sb.Append(worn[i, 0] + ": " + (item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name) + " (" + item.m_shared.m_itemType + ")");
                Slot slot = worn[i, 2].Length > 0 ? Find(worn[i, 2]) : null;
                Bounds bounds;
                if (slot != null && ModelBounds(Models(player, slot), out bounds))
                {
                    Vector3 size = bounds.size;
                    sb.Append(" model " + Mathf.Max(size.x, Mathf.Max(size.y, size.z)).ToString("0.00", Inv) + " m");
                }
                sb.AppendLine();
            }
            if (sb.Length == 0) sb.AppendLine("nothing worn");
            sb.Append("view " + (_slot != null ? _slot.Name + " bearing " + _bearing.ToString("0", Inv) : "off"));
            return sb.ToString();
        }
    }
}
