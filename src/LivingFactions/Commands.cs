using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Jotunn.Entities;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>The <c>lf</c> console command. Type <c>lf help</c> in the game console (F5).</summary>
    internal class LfConsoleCommand : ConsoleCommand
    {
        public override string Name { get { return "lf"; } }

        public override string Help { get { return "Living Factions: lf help"; } }

        public override void Run(string[] args)
        {
            string result = Commands.Run(string.Join(" ", args));
            foreach (string line in result.Split('\n')) Console.instance.Print(line);
        }
    }

    internal static class Commands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Camera placement forced by the dev <c>cam</c> command; null = normal camera.</summary>
        public static Vector3? CamPos;
        public static Vector3 CamTarget;

        public static string Run(string line)
        {
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return Help();
            switch (a[0].ToLowerInvariant())
            {
                case "help": return Help();
                case "status": return Status();
                case "factions": return Factions();
                case "chronicle": return Chronicle(a.Length > 1 ? int.Parse(a[1]) : 15);
                case "advance": return Advance(a.Length > 1 ? double.Parse(a[1], Inv) : 1);
                case "tp": return Teleport(a);
                case "rep": return Rep(a);
                case "save": SimHost.Save(); return "saved";
            }
            if (!ModConfig.DevTools.Value) return "Unknown command. Try: lf help";
            switch (a[0].ToLowerInvariant())
            {
                case "here": return Here(a);
                case "time": return "worldDay " + (SimHost.Active ? SimHost.WorldDay.ToString("0.00", Inv) : "?") + " simDay " + (SimHost.Active ? SimHost.Sim.State.Day.ToString("0.00", Inv) : "?");
                case "pos": return Pos();
                case "shot": return Shot(a);
                case "cam": return Cam(a);
                case "look": return GearView.Look(a);
                case "gear":
                    string worn = Equip(a);
                    return worn.StartsWith("equipped") ? worn + "; " + GearView.Look(new[] { "look", "auto" }) : worn;
                case "unequip": return GearView.Unequip(a);
                case "sheathe": return GearView.Sheathe(true);
                case "draw": return GearView.Sheathe(false);
                case "face": return GearView.Face(a);
                case "gearinfo": return GearView.Describe();
                case "points": return AttachPoints.Command(a);
                case "mats": return DevProbes.Materials(a);
                case "crowd": return DevProbes.Crowd();
                case "lang": return DevProbes.Language(a);
                case "perf": return DevProbes.Perf();
                case "track": return DevProbes.Track(a);
                case "seams": return DevProbes.Seams(a.Length > 1 ? float.Parse(a[1], Inv) : 150f);
                case "emptybag":
                    // A test character fills up over many runs; a full bag makes every purchase fail.
                    if (Player.m_localPlayer == null) return "no player";
                    Player.m_localPlayer.UnequipAllItems();
                    int had = Player.m_localPlayer.GetInventory().NrOfItems();
                    Player.m_localPlayer.GetInventory().RemoveAll();
                    return "bag emptied, " + had + " stacks gone";
                case "stamina": return DevProbes.Stamina();
                case "damage": return DevProbes.Damage(a);
                case "shots": return DevProbes.Shots(a);
                case "tradechest":
                    Settlement tradeTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return tradeTown == null ? "usage: tradechest <settlement>" : DevProbes.PlaceTradeChest(tradeTown);
                case "chestinfo": return DevProbes.ChestInfo();
                case "watch": return Watch(a);
                case "roads": return Roads.Describe();
                case "roadtp":
                case "roadview":
                {
                    // roadtp <settlement> <metres out>: stand on the road there. roadview: look along it from above.
                    Settlement town = a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (town == null || Player.m_localPlayer == null) return "usage: " + a[0] + " <settlement> <metres>";
                    if (!Roads.HasRoad(town)) return town.Name + " has no road (no way over land to a neighbour)";
                    // `roadview 4 dry` looks along the road from where the last `roadtp 4 dry` put the player.
                    float out1 = a.Length > 2 && a[2] != "dry" ? float.Parse(a[2], Inv) : 120f;
                    if (a[0] == "roadview" && a.Length > 2 && a[2] == "dry")
                    {
                        Vector3 from0 = Roads.Along(town, _lastDryRoad - 20f), to0 = Roads.Along(town, _lastDryRoad + 10f);
                        GearView.Off();
                        CamPos = from0 + (from0 - to0).normalized * 14f + Vector3.up * 10f;
                        CamTarget = to0 + Vector3.up;
                        return "looking along the road at " + _lastDryRoad.ToString("0", Inv) + " m out";
                    }
                    if (a.Length > 2 && a[2] == "dry")
                    {
                        // roadtp 4 dry [from]: the first stretch of 60 m of road, at or beyond `from`, that is all on dry land.
                        float from = a.Length > 3 ? float.Parse(a[3], Inv) : 150f;
                        out1 = -1f;
                        for (float d = from; d < 3000f && out1 < 0f; d += 10f)
                        {
                            bool dry = true;
                            for (float k = 0f; k <= 60f && dry; k += 10f)
                            {
                                Vector3 q = Roads.Along(town, d + k);
                                dry = WorldGenerator.instance.GetHeight(q.x, q.z) > ZoneSystem.c_WaterLevel + 1f;
                            }
                            if (dry) out1 = d + 20f;
                        }
                        if (out1 < 0f) return "no dry stretch of road beyond " + from.ToString("0", Inv) + " m";
                        _lastDryRoad = out1;
                    }
                    Vector3 here = Roads.Along(town, out1), there = Roads.Along(town, out1 + 30f);
                    if (a[0] == "roadtp")
                    {
                        here.y = WorldGenerator.instance.GetHeight(here.x, here.z) + 1f;
                        Player.m_localPlayer.TeleportTo(here, Quaternion.LookRotation(new Vector3(there.x - here.x, 0f, there.z - here.z)), true);
                        return "on the road at " + here + ", " + out1.ToString("0", Inv) + " m out";
                    }
                    GearView.Off();
                    Vector3 back = (here - there).normalized;
                    CamPos = here + back * 14f + Vector3.up * (a.Length > 3 ? float.Parse(a[3], Inv) : 9f);
                    CamTarget = there + Vector3.up * 1f;
                    return "looking along the road from " + here;
                }
                case "modmenu": return ModMenu.DevCommand(a);
                case "lampview":
                {
                    // Looks at the road lamp nearest to the player from a few steps away.
                    if (Player.m_localPlayer == null) return "no player";
                    Vector3 me = Player.m_localPlayer.transform.position;
                    ZNetView lamp = null;
                    float nearest = float.MaxValue;
                    foreach (Collider collider in Physics.OverlapSphere(me, 90f))
                    {
                        ZNetView view = collider.GetComponentInParent<ZNetView>();
                        if (view == null || !view.IsValid() || view.GetZDO().GetInt(Roads.HashRoad) == 0 || view.GetComponent<Fireplace>() == null) continue;
                        float d = Vector3.Distance(me, view.transform.position);
                        if (d < nearest)
                        {
                            nearest = d;
                            lamp = view;
                        }
                    }
                    if (lamp == null) return "no road lamp within 90 m";
                    GearView.Off();
                    CamTarget = lamp.transform.position;
                    CamPos = lamp.transform.position + lamp.transform.forward * 5f + lamp.transform.right * 3f + Vector3.up * 1.5f;
                    return "lamp " + Utils.GetPrefabName(lamp.gameObject) + " at " + lamp.transform.position + ", burning " + lamp.GetComponent<Fireplace>().IsBurning();
                }
                case "inv":
                    if (InventoryGui.instance == null) return "no inventory screen";
                    if (a.Length > 1 && a[1] == "off") InventoryGui.instance.Hide();
                    else InventoryGui.instance.Show(null);
                    return "inventory " + (InventoryGui.IsVisible() ? "open" : "closed");
                case "caravan":
                    Settlement caravanTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return caravanTown == null ? "usage: caravan <settlement> [in]" : Company.Send(caravanTown, a.Length > 2 && a[2] == "in");
                case "archer":
                    Settlement archerTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return archerTown == null || Player.m_localPlayer == null ? "usage: archer <settlement>" : Company.DevArcher(archerTown, Player.m_localPlayer);
                case "hire":
                    Settlement hireTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return hireTown == null || Player.m_localPlayer == null ? "usage: hire <settlement>" : Texts.Get(Company.Hire(hireTown, Player.m_localPlayer, true)) + " - hired now " + Company.HiredCount();
                case "marks": return AttachPoints.Marks(a.Length < 2 || a[1] != "off");
                case "hidehud":
                    if (Hud.instance == null) return "no hud";
                    Hud.instance.m_userHidden = a.Length < 2 || a[1] != "off";
                    return Hud.instance.m_userHidden ? "hud hidden" : "hud shown";
                case "env": return Env(a);
                case "god": return God();
                case "dump": return Dump(a);
                case "spawn": return Spawn(a);
                case "find": return Find(a);
                case "mat": return Materializer.Describe();
                case "destroy":
                    return SimHost.Active && a.Length > 1 && SimHost.Sim.DebugDestroy(int.Parse(a[1])) ? "destroyed" : "no such live settlement";
                case "speed":
                    // The game writes its own time scale every frame, so that is the value to change.
                    float scale = Mathf.Clamp(a.Length > 1 ? float.Parse(a[1], Inv) : 1f, 0.1f, 5f);
                    AccessTools.Field(typeof(Game), "m_timeScale").SetValue(null, scale);
                    Time.timeScale = scale;
                    return "time scale " + scale.ToString(Inv);
                case "equip": return Equip(a);
                case "count":
                    return "inhabitants " + FactionMember.All.Count() + ", " + Materializer.Describe();
                case "doorcheck": return DoorCheck(a);
                case "padcheck": return PadCheck(a);
                case "wares":
                    Wares.Reload();
                    return "trade.cfg reloaded";
                case "coins":
                    if (Player.m_localPlayer == null) return "no player";
                    Player.m_localPlayer.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Coins"), a.Length > 1 ? int.Parse(a[1]) : 500);
                    return "coins added";
                case "items":
                    if (Player.m_localPlayer == null) return "no player";
                    return string.Join(", ", Player.m_localPlayer.GetInventory().GetAllItems().Select(i => i.m_dropPrefab.name + "x" + i.m_stack + (Wares.AffixOf(i) != null ? "[" + Wares.AffixOf(i) + "]" : i.m_customData.ContainsKey(Wares.MysteryKey) ? "[?]" : "") + " dmg " + i.GetDamage().GetTotalDamage().ToString("0")));
                case "hud": return SimHost.Active && Player.m_localPlayer != null ? HudWidget.Compose() : "no world";
                case "npcs": return NpcReport();
                case "chests": return Chests.Describe();
                case "inside": return Inside(a);
                case "night":
                    NightWatch.Reset();
                    if (a.Length > 1) ModConfig.NightAttackChance.Value = float.Parse(a[1], Inv);
                    return "night watch reset, attack chance " + ModConfig.NightAttackChance.Value.ToString(Inv);
                case "probe": return Probe(a);
                case "view": return View(a);
                case "quit": return Quit();
                case "war": return War(a);
                case "raid": return Raid(a);
                case "dumpchar": return DumpChar(a);
                case "ui": return DiplomacyUI.DevCommand(a);
                default: return "Unknown command: " + a[0];
            }
        }

        private static float _lastDryRoad = 150f;

        private static string Help()
        {
            return "lf status | factions | chronicle [n] | advance <days> | tp <settlement> | rep <faction#> <value> | save";
        }

        private static string NoSim { get { return "The faction simulation is not running here (only the host of the world runs it)."; } }

        private static string Status()
        {
            if (!SimHost.Active) return NoSim;
            Simulation sim = SimHost.Sim;
            StringBuilder sb = new StringBuilder();
            Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            sb.AppendLine(Texts.Get("ui_day", sim.State.Day.ToString("0.0", Inv)));
            foreach (Faction f in sim.State.Factions)
            {
                sb.AppendLine("#" + f.Id + " " + Texts.FactionName(f) + (f.Eliminated ? " [gone]" : ""));
                foreach (Settlement s in sim.State.Settlements)
                {
                    if (s.FactionId != f.Id) continue;
                    float dist = Utils.DistanceXZ(me, new Vector3(s.Pos.X, 0, s.Pos.Z));
                    sb.AppendLine("   " + s.Id + " " + s.Name + (s.Destroyed ? " [ruin]" : "") + " " + Texts.Get("tier_" + s.Tier) + " " + s.Biome + " pop " + s.Population.ToString("0") + " sol " + s.Soldiers.ToString("0")
                        + " W" + s.Stock[0].ToString("0") + " S" + s.Stock[1].ToString("0") + " F" + s.Stock[2].ToString("0") + " M" + s.Stock[3].ToString("0") + " C" + s.Stock[4].ToString("0")
                        + " bld " + s.Buildings.Count + " @" + s.Pos + " " + dist.ToString("0") + "m");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string Factions()
        {
            if (!SimHost.Active) return NoSim;
            Simulation sim = SimHost.Sim;
            long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;
            StringBuilder sb = new StringBuilder();
            foreach (Faction f in sim.State.Factions)
            {
                if (f.Eliminated) continue;
                PlayerRelation rel = sim.PlayerRel(f, me);
                sb.AppendLine("#" + f.Id + " " + Texts.FactionName(f) + ": " + Texts.Get("st_" + sim.GetStanding(f, me)) + " (" + rel.Reputation.ToString("0") + "), " + Texts.Get("ui_strength") + " " + sim.Strength(f).ToString("0") + ", " + sim.CountSettlements(f.Id) + " " + Texts.Get("ui_settlements"));
                foreach (Faction other in sim.State.Factions)
                {
                    if (other.Eliminated || other.Id <= f.Id) continue;
                    Relation r = sim.Rel(f, other);
                    if (r.Treaty != Treaty.Peace) sb.AppendLine("     " + Texts.Get("tr_" + r.Treaty) + ": " + Texts.FactionName(other));
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string Chronicle(int count)
        {
            if (!SimHost.Active) return NoSim;
            List<SimEvent> log = SimHost.Sim.State.Chronicle;
            StringBuilder sb = new StringBuilder();
            for (int i = Math.Max(0, log.Count - count); i < log.Count; i++)
            {
                sb.AppendLine(Texts.Get("ui_day", ((int)log[i].Day).ToString()) + ": " + Texts.Describe(log[i], SimHost.Sim.State));
            }
            return sb.Length == 0 ? Texts.Get("ui_no_news") : sb.ToString().TrimEnd();
        }

        private static string Advance(double days)
        {
            if (!SimHost.Active) return NoSim;
            days = Math.Max(0, Math.Min(days, 400));
            SimHost.FastForward(days);
            return "advanced " + days.ToString("0.##", Inv) + " days, now day " + SimHost.Sim.State.Day.ToString("0.0", Inv);
        }

        private static Settlement FindSettlement(string key)
        {
            if (!SimHost.Active) return null;
            int id;
            foreach (Settlement s in SimHost.Sim.State.Settlements)
            {
                if (int.TryParse(key, out id) ? s.Id == id : s.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }

        private static string Teleport(string[] a)
        {
            if (Player.m_localPlayer == null) return "no player";
            Vector3 target;
            if (a.Length >= 3)
            {
                float x = float.Parse(a[1], Inv), z = float.Parse(a[2], Inv);
                target = new Vector3(x, 0, z);
            }
            else
            {
                Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
                if (s == null) return "no such settlement";
                // Arrive a little outside the first ring, facing the centre.
                target = new Vector3(s.Pos.X + 20f, 0, s.Pos.Z);
            }
            target.y = WorldGenerator.instance.GetHeight(target.x, target.z) + 2f;
            Player.m_localPlayer.TeleportTo(target, Player.m_localPlayer.transform.rotation, true);
            return "teleporting to " + target;
        }

        private static string Rep(string[] a)
        {
            if (!SimHost.Active) return NoSim;
            if (a.Length < 3 || Player.m_localPlayer == null) return "usage: lf rep <faction#> <value>";
            Faction f = SimHost.Sim.State.GetFaction(int.Parse(a[1]));
            if (f == null) return "no such faction";
            PlayerRelation rel = SimHost.Sim.PlayerRel(f, Player.m_localPlayer.GetPlayerID());
            rel.Reputation = Util.Clamp(float.Parse(a[2], Inv), -100, 100);
            return Texts.FactionName(f) + ": " + rel.Reputation.ToString("0") + " (" + Texts.Get("st_" + SimHost.Sim.GetStanding(f, Player.m_localPlayer.GetPlayerID())) + ")";
        }

        // ------------------------------------------------------------------ developer commands

        private static string Here(string[] a)
        {
            if (!SimHost.Active) return NoSim;
            if (Player.m_localPlayer == null) return "no player";
            int factionId = a.Length > 1 ? int.Parse(a[1]) : SimHost.Sim.State.Factions[0].Id;
            float ahead = a.Length > 2 ? float.Parse(a[2], Inv) : 40f;
            Vector3 p = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * ahead;
            Biome biome = GameWorld.Map(WorldGenerator.instance.GetBiome(p.x, p.z));
            if (WorldGenerator.instance.GetHeight(p.x, p.z) < ZoneSystem.c_WaterLevel + 1.5f) return "that spot is under water";
            foreach (Settlement other in SimHost.Sim.State.Settlements)
            {
                if (!other.Destroyed && Utils.DistanceXZ(p, new Vector3(other.Pos.X, 0f, other.Pos.Z)) < 160f) return "too close to " + other.Name;
            }
            Settlement s = SimHost.Sim.DebugFound(factionId, new Vec2(p.x, p.z), biome);
            return s == null ? "no such faction" : "founded " + s.Name + " (id " + s.Id + ") at " + s.Pos;
        }

        /// <summary>Why are sites being rejected? Samples random spots in a distance band and tallies the reasons.</summary>
        private static string Probe(string[] a)
        {
            float min = a.Length > 1 ? float.Parse(a[1], Inv) : 220f, max = a.Length > 2 ? float.Parse(a[2], Inv) : 900f;
            int n = a.Length > 3 ? int.Parse(a[3]) : 400;
            GameWorld world = new GameWorld();
            Dictionary<string, int> tally = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f), dist = UnityEngine.Random.Range(min, max);
                Vec2 p = new Vec2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
                Biome biome;
                string reason = world.ProbeReason(p, 30f, out biome);
                if (reason == "ok" && world.IsReserved(p, 70f)) reason = "reserved";
                string key = reason + "/" + biome;
                int count;
                tally.TryGetValue(key, out count);
                tally[key] = count + 1;
            }
            return string.Join(", ", tally.OrderByDescending(k => k.Value).Select(k => k.Key + "=" + k.Value));
        }

        private static string DumpChar(string[] a)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < a.Length; i++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(a[i]);
                if (prefab == null)
                {
                    sb.AppendLine(a[i] + ": NOT FOUND");
                    continue;
                }
                sb.Append(a[i] + ": scale" + V(prefab.transform.localScale) + " layer " + prefab.layer);
                sb.Append(" comps[" + string.Join(",", prefab.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name)) + "]");
                Animator animator = prefab.GetComponentInChildren<Animator>(true);
                if (animator != null) sb.Append(" animator=" + (animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null") + " avatar=" + (animator.avatar != null ? animator.avatar.name : "null"));
                Humanoid h = prefab.GetComponent<Humanoid>();
                if (h != null)
                {
                    sb.Append(" name=" + h.m_name + " faction=" + h.m_faction + " group=" + h.m_group + " hp=" + h.m_health + " walk=" + h.m_walkSpeed + " run=" + h.m_runSpeed);
                    sb.Append(" default[" + Names(h.m_defaultItems) + "] weapon[" + Names(h.m_randomWeapon) + "] armor[" + Names(h.m_randomArmor) + "] shield[" + Names(h.m_randomShield) + "]");
                    if (h.m_randomSets != null) sb.Append(" sets[" + string.Join(" | ", h.m_randomSets.Select(set => set.m_name + ":" + Names(set.m_items))) + "]");
                }
                VisEquipment vis = prefab.GetComponent<VisEquipment>();
                if (vis != null)
                {
                    HarmonyLib.Traverse t = HarmonyLib.Traverse.Create(vis);
                    sb.Append(" vis{isPlayer=" + vis.m_isPlayer + " models=" + vis.m_models.Length + " randomModel=" + vis.m_randomModelIndex
                        + " hairPrefix=" + t.Field("m_hairPrefabPrefix").GetValue() + " hairCount=" + t.Field("m_hairPrefabCount").GetValue()
                        + " beardPrefix=" + t.Field("m_beardPrefabPrefix").GetValue() + " beardCount=" + t.Field("m_beardPrefabCount").GetValue() + "}");
                }
                MonsterAI ai = prefab.GetComponent<MonsterAI>();
                if (ai != null) sb.Append(" ai{view=" + ai.m_viewRange + " hear=" + ai.m_hearRange + " aggravatable=" + ai.m_aggravatable + " hunt=" + ai.m_enableHuntPlayer + " moveRange=" + ai.m_randomMoveRange + " chase=" + ai.m_maxChaseDistance + " agent=" + ai.m_pathAgentType + "}");
                sb.AppendLine();
            }
            string text = sb.ToString().TrimEnd();
            File.AppendAllText(Path.Combine(SimHost.DataDir, "dump.txt"), text + "\n");
            return text;
        }

        private static string Names(GameObject[] items)
        {
            return items == null ? "" : string.Join(",", items.Select(g => g != null ? g.name : "null"));
        }

        /// <summary>Frames a settlement from outside: view &lt;settlement&gt; [distance] [height] [bearing degrees].</summary>
        private static string View(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            GearView.Off();
            float dist = a.Length > 2 ? float.Parse(a[2], Inv) : 30f, height = a.Length > 3 ? float.Parse(a[3], Inv) : 14f;
            float bearing = (a.Length > 4 ? float.Parse(a[4], Inv) : 0f) * Mathf.Deg2Rad;
            float ground = GameWorld.GroundHeight(s.Pos.X, s.Pos.Z);
            CamTarget = new Vector3(s.Pos.X, ground + 1.5f, s.Pos.Z);
            CamPos = new Vector3(s.Pos.X + Mathf.Sin(bearing) * dist, ground + height, s.Pos.Z + Mathf.Cos(bearing) * dist);
            return "viewing " + s.Name + " ground " + ground.ToString("0.0", Inv);
        }

        private static string War(string[] a)
        {
            if (!SimHost.Active || a.Length < 3) return "usage: lf war <faction#> <faction#> [peace]";
            Faction x = SimHost.Sim.State.GetFaction(int.Parse(a[1])), y = SimHost.Sim.State.GetFaction(int.Parse(a[2]));
            if (x == null || y == null || x == y) return "no such factions";
            Relation r = SimHost.Sim.Rel(x, y);
            bool peace = a.Length > 3 && a[3] == "peace";
            r.Treaty = peace ? Treaty.Peace : Treaty.War;
            r.Value = peace ? -20f : -80f;
            r.SinceDay = SimHost.Sim.State.Day;
            return Texts.FactionName(x) + " / " + Texts.FactionName(y) + ": " + Texts.Get("tr_" + r.Treaty);
        }

        private static string Raid(string[] a)
        {
            if (!SimHost.Active || a.Length < 3) return "usage: lf raid <fromSettlement> <targetSettlement>";
            Settlement from = FindSettlement(a[1]), target = FindSettlement(a[2]);
            if (from == null || target == null) return "no such settlements";
            Raids.Stage(from, target, a.Length > 3 ? float.Parse(a[3], Inv) : 4f);
            return "raid staged: " + from.Name + " -> " + target.Name;
        }

        private static string Quit()
        {
            if (ZNet.instance != null) ZNet.instance.Save(true);
            Application.Quit();
            return "saved, quitting";
        }

        /// <summary>Puts the camera at the front of a building looking in: inside &lt;settlement&gt; [kind] [back] [height].</summary>
        private static string Inside(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            string kind = a.Length > 2 ? a[2] : "Hut";
            float back = a.Length > 3 ? float.Parse(a[3], Inv) : 1.4f, height = a.Length > 4 ? float.Parse(a[4], Inv) : 1.9f;
            foreach (Building b in s.Buildings)
            {
                if (!b.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase) || b.State != BuildingState.Complete || float.IsNaN(b.GroundY)) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw = 0f;
                if (b.Plot >= 0) Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                GearView.Off();
                CamPos = origin + rot * new Vector3(0f, height, back);
                CamTarget = origin + rot * new Vector3(0f, 0.6f, -1.6f);
                return "looking into " + b.Kind + " #" + b.Id + " at " + origin;
            }
            return "no complete " + kind;
        }

        /// <summary>Sweeps a player-sized capsule through every house door and reports anything in the way.</summary>
        private static string DoorCheck(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            StringBuilder sb = new StringBuilder();
            foreach (Building b in s.Buildings)
            {
                if (!FactionMember.IsHome(b.Kind) || b.State != BuildingState.Complete || float.IsNaN(b.GroundY) || b.SpawnedPieces == 0) continue;
                Vector3 inside, door;
                FactionMember.DoorPoints(s, b, 0f, out inside, out door);
                // The player's own capsule, standing on the house floor, walked from outside the door to the spot inside.
                CapsuleCollider body = Player.m_localPlayer != null ? Player.m_localPlayer.GetCollider() : null;
                float r = body != null ? body.radius : 0.4f, h = body != null ? body.height : 1.85f;
                float floor = b.GroundY + 0.42f;
                Vector3 feet = new Vector3(door.x, floor + r, door.z), head = new Vector3(door.x, floor + h - r, door.z);
                Vector3 dir = inside - door;
                dir.y = 0f;
                HashSet<string> blockers = new HashSet<string>();
                foreach (RaycastHit hit in Physics.CapsuleCastAll(feet, head, r, dir.normalized, dir.magnitude))
                {
                    if (hit.collider.isTrigger) continue;
                    ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
                    string name = view != null ? Utils.GetPrefabName(view.gameObject) : hit.collider.name;
                    if (name.Contains("door") || hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Heightmap>() != null) continue;
                    blockers.Add(name);
                }
                sb.AppendLine(b.Kind + " #" + b.Id + " (capsule r " + r.ToString("0.00", Inv) + " h " + h.ToString("0.00", Inv) + "): " + (blockers.Count == 0 ? "clear" : "BLOCKED by " + string.Join(",", blockers)));
            }
            return sb.Length == 0 ? "no finished houses" : sb.ToString().TrimEnd();
        }

        /// <summary>How far the real ground under each building is from the height the building stands at.</summary>
        private static string PadCheck(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            StringBuilder sb = new StringBuilder();
            float worst = 0f;
            foreach (Building b in s.Buildings)
            {
                float radius = Blueprints.Radius(b.Kind);
                if (float.IsNaN(b.GroundY) || radius <= 0f) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw;
                if (b.Plot >= 0) Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
                float min = 0f, max = 0f;
                for (int i = 0; i < 9; i++)
                {
                    float ang = i * Mathf.PI / 4f, r = i == 8 ? 0f : radius * 0.8f, h;
                    if (!ZoneSystem.instance.GetGroundHeight(origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r, out h)) continue;
                    min = Mathf.Min(min, h - b.GroundY);
                    max = Mathf.Max(max, h - b.GroundY);
                }
                worst = Mathf.Max(worst, Mathf.Max(-min, max));
                sb.AppendLine(b.Kind + " #" + b.Id + ": ground " + min.ToString("+0.00;-0.00", Inv) + " .. " + max.ToString("+0.00;-0.00", Inv));
            }
            return "worst deviation " + worst.ToString("0.00", Inv) + " m\n" + sb.ToString().TrimEnd();
        }

        /// <summary>Puts the camera beside the first inhabitant whose `npcs` line contains the text: watch carrying.</summary>
        private static string Watch(string[] a)
        {
            if (a.Length < 2) return "usage: lf watch <text from the npcs list>";
            string wanted = string.Join(" ", a.Skip(1));
            foreach (FactionMember m in FactionMember.All)
            {
                string line = m.Describe();
                if (line.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0) continue;
                GearView.Off();
                Transform t = m.transform;
                CamTarget = t.position + Vector3.up * 1.2f;
                CamPos = t.position - t.forward * 3.2f + t.right * 2.2f + Vector3.up * 2f;
                return "watching " + line;
            }
            return "nobody matches " + wanted;
        }

        /// <summary>Gives the player an item if they lack it and puts it in their hands.</summary>
        private static string Equip(string[] a)
        {
            Player player = Player.m_localPlayer;
            if (player == null || a.Length < 2) return "usage: lf equip <item prefab>";
            Inventory inv = player.GetInventory();
            // A relic copy (one with an affix) first: its properties are what a check is usually after.
            ItemDrop.ItemData item = inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == a[1]).OrderByDescending(i => Wares.AffixOf(i) != null).FirstOrDefault();
            if (item == null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(a[1]);
                if (prefab == null) return "no item " + a[1];
                inv.AddItem(prefab, 1);
                item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == a[1]);
            }
            if (item == null) return "could not equip " + a[1];
            GearView.LastItem = item;
            return player.IsItemEquiped(item) || player.EquipItem(item, false) ? "equipped " + a[1] : "could not equip " + a[1];
        }

        private static string NpcReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("night " + EnvMan.IsNight() + " claims " + FactionMember.ClaimCount + " maxPerTarget " + FactionMember.MaxClaim);
            foreach (FactionMember m in FactionMember.All) sb.AppendLine("  s" + m.SettlementId + " " + m.Describe());
            return sb.ToString().TrimEnd();
        }

        private static string Pos()
        {
            if (Player.m_localPlayer == null) return "no player";
            Vector3 p = Player.m_localPlayer.transform.position;
            return p.x.ToString("0.0", Inv) + " " + p.y.ToString("0.0", Inv) + " " + p.z.ToString("0.0", Inv) + " biome " + WorldGenerator.instance.GetBiome(p.x, p.z) + " zoneLoaded " + ZoneSystem.instance.IsZoneLoaded(p);
        }

        private static string Shot(string[] a)
        {
            string name = a.Length > 1 ? a[1] : "shot";
            string dir = Path.Combine(SimHost.DataDir, "shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }

        private static string Cam(string[] a)
        {
            GearView.Off();
            if (a.Length < 7)
            {
                CamPos = null;
                return "camera released";
            }
            CamPos = new Vector3(float.Parse(a[1], Inv), float.Parse(a[2], Inv), float.Parse(a[3], Inv));
            CamTarget = new Vector3(float.Parse(a[4], Inv), float.Parse(a[5], Inv), float.Parse(a[6], Inv));
            return "camera at " + CamPos.Value + " looking at " + CamTarget;
        }

        private static string Env(string[] a)
        {
            if (EnvMan.instance == null) return "no world";
            EnvMan.instance.m_debugTimeOfDay = a.Length > 1 && a[1] != "off";
            if (a.Length > 1 && a[1] != "off") EnvMan.instance.m_debugTime = float.Parse(a[1], Inv);
            EnvMan.instance.m_debugEnv = a.Length > 2 ? a[2] : "";
            return "time " + (EnvMan.instance.m_debugTimeOfDay ? EnvMan.instance.m_debugTime.ToString(Inv) : "live") + " env '" + EnvMan.instance.m_debugEnv + "'";
        }

        private static string God()
        {
            if (Player.m_localPlayer == null) return "no player";
            Player.m_localPlayer.SetGodMode(true);
            Player.m_localPlayer.SetGhostMode(true);
            Player.m_debugMode = true;
            return "god + ghost on";
        }

        private static string Spawn(string[] a)
        {
            if (a.Length < 2 || Player.m_localPlayer == null) return "usage: lf spawn <prefab> [dx dz yaw]";
            GameObject prefab = ZNetScene.instance.GetPrefab(a[1]);
            if (prefab == null) return "no prefab " + a[1];
            Transform t = Player.m_localPlayer.transform;
            float dx = a.Length > 2 ? float.Parse(a[2], Inv) : 0f, dz = a.Length > 3 ? float.Parse(a[3], Inv) : 3f;
            float yaw = a.Length > 4 ? float.Parse(a[4], Inv) : 0f;
            Vector3 p = t.position + new Vector3(dx, 0, dz);
            float h;
            if (ZoneSystem.instance.GetGroundHeight(p, out h)) p.y = h;
            UnityEngine.Object.Instantiate(prefab, p, Quaternion.Euler(0, yaw, 0));
            return "spawned " + a[1] + " at " + p;
        }

        private static string Find(string[] a)
        {
            if (a.Length < 2) return "usage: lf find <part of prefab name>";
            List<string> names = ZNetScene.instance.GetPrefabNames().Where(n => n.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(n => n).ToList();
            return names.Count + ": " + string.Join(" ", names.Take(150));
        }

        /// <summary>Writes the real geometry of prefabs (bounds, snap points, colliders) for blueprint design.</summary>
        private static string Dump(string[] a)
        {
            if (a.Length < 2) return "usage: lf dump <prefab> [prefab...]";
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i < a.Length; i++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(a[i]);
                if (prefab == null)
                {
                    sb.AppendLine(a[i] + ": NOT FOUND");
                    continue;
                }
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
                Bounds? bounds = null;
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        Vector3 p = m.MultiplyPoint3x4(corner);
                        if (bounds.HasValue)
                        {
                            Bounds b = bounds.Value;
                            b.Encapsulate(p);
                            bounds = b;
                        }
                        else bounds = new Bounds(p, Vector3.zero);
                    }
                }
                sb.Append(a[i] + ":");
                if (bounds.HasValue) sb.Append(" mesh min" + V(bounds.Value.min) + " max" + V(bounds.Value.max));
                List<string> snaps = new List<string>();
                foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (child.CompareTag("snappoint")) snaps.Add(V(toRoot.MultiplyPoint3x4(child.position)));
                }
                sb.Append(" snaps[" + string.Join(" ", snaps) + "]");
                sb.Append(" comps[" + string.Join(",", prefab.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name)) + "]");
                Piece piece = prefab.GetComponent<Piece>();
                if (piece != null) sb.Append(" piece{ground=" + piece.m_groundPiece + " name=" + piece.m_name + "}");
                WearNTear wnt = prefab.GetComponent<WearNTear>();
                if (wnt != null) sb.Append(" wnt{mat=" + wnt.m_materialType + " hp=" + wnt.m_health + "}");
                sb.AppendLine();
                if (a.Length == 2)
                {
                    foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
                    {
                        if (child == prefab.transform) continue;
                        sb.AppendLine("   " + ChildPath(child, prefab.transform) + " [" + string.Join(",", child.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c == null ? "null" : c.GetType().Name)) + "]" + (child.gameObject.activeSelf ? "" : " (off)"));
                    }
                }
            }
            string text = sb.ToString().TrimEnd();
            File.AppendAllText(Path.Combine(SimHost.DataDir, "dump.txt"), text + "\n");
            return text;
        }

        private static string ChildPath(Transform t, Transform root)
        {
            string path = t.name;
            while (t.parent != null && t.parent != root)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static string V(Vector3 v)
        {
            return "(" + v.x.ToString("0.##", Inv) + "," + v.y.ToString("0.##", Inv) + "," + v.z.ToString("0.##", Inv) + ")";
        }
    }

    /// <summary>While dev tools are on the game never pauses (it would when its window loses focus), so tests keep running.</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.Pause))]
    internal static class DevNoPausePatch
    {
        private static bool Prefix() { return !ModConfig.DevTools.Value; }
    }

    /// <summary>The game hides the player when the camera is within 2 m of them; gear close-ups need them drawn.</summary>
    [HarmonyPatch(typeof(Character), "SetVisible")]
    internal static class DevKeepPlayerVisiblePatch
    {
        private static void Prefix(Character __instance, ref bool visible)
        {
            if (GearView.Active && __instance == Player.m_localPlayer) visible = true;
        }
    }

    /// <summary>Dev camera override so screenshots can be framed from anywhere.</summary>
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class DevCameraPatch
    {
        private static bool Prefix(GameCamera __instance)
        {
            if (GearView.Active && GearView.Place(__instance)) return false;
            if (!Commands.CamPos.HasValue) return true;
            __instance.transform.position = Commands.CamPos.Value;
            __instance.transform.rotation = Quaternion.LookRotation(Commands.CamTarget - Commands.CamPos.Value);
            return false;
        }
    }
}
