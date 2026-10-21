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
                case "rewind": return SimHost.Rewind(a);
                case "audit":
                    {
                        Settlement s = SimHost.Active && a.Length > 1 ? FindSettlement(a[1]) : null;
                        if (s == null) return "audit <settlement>";
                        string report = Materializer.Reconcile(s, new Vector3(s.Pos.X, 0f, s.Pos.Z), a.Length > 2 && a[2] == "fix");
                        return s.Name + ": " + (report.Length == 0 ? "world and record agree" : report);
                    }
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
                case "shieldfit": return DevProbes.ShieldFit(a);
                case "shieldbox": return DevProbes.ShieldBox();
                case "seams": return DevProbes.Seams(a.Length > 1 ? float.Parse(a[1], Inv) : 150f);
                case "hoe": return DevProbes.Hoe(a);
                case "hoecheck": return DevProbes.HoeCheck();
                case "emptybag":
                    // A test character fills up over many runs; a full bag makes every purchase fail.
                    if (Player.m_localPlayer == null) return "no player";
                    Player.m_localPlayer.UnequipAllItems();
                    int had = Player.m_localPlayer.GetInventory().NrOfItems();
                    Player.m_localPlayer.GetInventory().RemoveAll();
                    return "bag emptied, " + had + " stacks gone";
                case "server": return Net.DevOnHost(string.Join(" ", a, 1, a.Length - 1));
                case "net": return Net.Describe();
                case "forest": return Forestry.Describe();
                case "ally":
                {
                    // ally <faction#>: proposes an alliance, as the button in the dialog does.
                    Faction f = a.Length > 1 && SimHost.Active ? SimHost.Sim.State.GetFaction(int.Parse(a[1])) : null;
                    if (f == null || Player.m_localPlayer == null) return "usage: ally <faction#>";
                    return Net.PlayerProposeAlliance(f, Player.m_localPlayer.GetPlayerID()).Reason;
                }
                case "strike":
                {
                    // strike <prefab>: the player kills the nearest creature of that kind (bosses included), as a real kill.
                    Player me = Player.m_localPlayer;
                    if (me == null || a.Length < 2) return "usage: strike <prefab>";
                    Character target = Character.GetAllCharacters().Where(c => !c.IsDead() && Utils.GetPrefabName(c.gameObject) == a[1]).OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).FirstOrDefault();
                    if (target == null) return "no " + a[1] + " nearby";
                    HitData hit = new HitData();
                    hit.m_damage.m_slash = 1000000f;
                    hit.m_point = target.transform.position;
                    hit.SetAttacker(me);
                    target.Damage(hit);
                    return "struck " + a[1];
                }
                case "slay":
                {
                    // slay <Role> [n]: the player strikes down the n nearest of that role, as a real kill (for testing rewards).
                    Player me = Player.m_localPlayer;
                    if (me == null || a.Length < 2) return "usage: slay <Chief|Raider|Guard|Villager> [n]";
                    int n = a.Length > 2 ? int.Parse(a[2]) : 1, done = 0;
                    foreach (FactionMember m in FactionMember.All.Where(m => m.Role.ToString() == a[1] && m.Humanoid != null && !m.Humanoid.IsDead()).OrderBy(m => Vector3.Distance(m.transform.position, me.transform.position)).Take(n).ToList())
                    {
                        HitData hit = new HitData();
                        hit.m_damage.m_slash = 100000f;
                        hit.m_point = m.transform.position;
                        hit.SetAttacker(me);
                        m.Humanoid.Damage(hit);
                        done++;
                    }
                    return "struck down " + done + " " + a[1];
                }
                case "mapinfo": return MapZones.Info() + "\n" + MinimapNpcs.Info();
                case "map":
                    // map large [zoom] [all]: opens the big map around the player; `all` shows every settlement.
                    if (Minimap.instance == null) return "no map";
                    if (a.Length > 1 && a[1] == "off") { PinnedZoom = 0f; Minimap.instance.SetMapMode(Minimap.MapMode.Small); return "map closed"; }
                    ModConfig.MapShowAll.Value = System.Array.IndexOf(a, "all") > 0;
                    Minimap.instance.SetMapMode(Minimap.MapMode.Large);
                    // Held where it is set (DevZoomPin): a connected gamepad can zoom the map on its own.
                    if (a.Length > 2 && a[2] != "all") Minimap.instance.LargeZoom = PinnedZoom = float.Parse(a[2], Inv);
                    return "map open, show all " + ModConfig.MapShowAll.Value + "; " + MapZones.Describe();
                case "stamina": return DevProbes.Stamina();
                case "damage": return DevProbes.Damage(a);
                case "duel": return Duel.Command(a);
                case "orbit":
                case "fly":
                case "dolly":
                case "follow":
                case "timelapse":
                case "daycard":
                case "still": return Cinema.Command(a);
                // The wall clock, to find this moment in a recording of the window.
                case "mark": return "mark " + (a.Length > 1 ? a[1] : "") + " at " + DateTime.Now.ToString("HH:mm:ss.fff", Inv);
                case "swing": return Duel.Swing(a);
                case "shots": return DevProbes.Shots(a);
                case "tradechest":
                    Settlement tradeTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return tradeTown == null ? "usage: tradechest <settlement>" : DevProbes.PlaceTradeChest(tradeTown);
                case "chestinfo": return DevProbes.ChestInfo();
                case "sweep":
                {
                    // lf sweep [settlement] [now]: clears loose items at once; "now" also takes those dropped in the last minute.
                    Settlement swept = a.Length > 1 && a[1] != "now" ? FindSettlement(a[1]) : null;
                    if (a.Length > 1 && a[1] != "now" && swept == null) return "usage: sweep [settlement] [now]";
                    return LootSweep.Command(swept, Array.IndexOf(a, "now") > 0);
                }
                case "watch": return Watch(a);
                case "watchgear":
                {
                    // watchgear <text from npcs> <front|left|right|back> [metres] [height]: the camera close on one
                    // inhabitant from that side, at chest height, to see what they hold and how (a shield on the left arm).
                    if (a.Length < 3) return "watchgear <text> <front|left|right|back> [metres] [height]";
                    FactionMember m = FactionMember.All.FirstOrDefault(x => x.Describe().IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (m == null) return "nobody matches " + a[1];
                    float dist = a.Length > 3 ? float.Parse(a[3], Inv) : 2f, high = a.Length > 4 ? float.Parse(a[4], Inv) : 1.3f;
                    Transform t = m.transform;
                    Vector3 side = a[2] == "left" ? -t.right : a[2] == "right" ? t.right : a[2] == "back" ? -t.forward : t.forward;
                    GearView.Off();
                    CamTarget = t.position + Vector3.up * 1.15f;
                    CamPos = t.position + side * dist + Vector3.up * high;
                    ItemDrop.ItemData shield = m.Humanoid != null ? m.Humanoid.GetInventory().GetAllItems().FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) : null;
                    return "watching " + m.Describe() + " from the " + a[2] + "; shield " + (shield != null && shield.m_dropPrefab != null ? shield.m_dropPrefab.name : "-");
                }
                case "resetgifts":
                {
                    // The character forgets every relic it was given and every hunt, so the tests can earn them again.
                    if (Player.m_localPlayer == null) return "no player";
                    List<string> keys = Player.m_localPlayer.m_customData.Keys.Where(k => k.StartsWith("lf_given_") || k.StartsWith("lf_hunt")).ToList();
                    foreach (string k in keys) Player.m_localPlayer.m_customData.Remove(k);
                    return "forgot " + keys.Count + " gifts and hunts";
                }
                case "grudge":
                {
                    // grudge [faction#]: the heat of the moment, and this player's tally with one faction.
                    if (!SimHost.Active || Player.m_localPlayer == null) return "no world";
                    string all = Grudges.Describe();
                    if (a.Length < 2) return all;
                    Faction f = SimHost.Sim.State.GetFaction(int.Parse(a[1]));
                    if (f == null) return "no such faction";
                    PlayerRelation rel = SimHost.Sim.PlayerRel(f, Player.m_localPlayer.GetPlayerID());
                    return all + "; " + f.Name + ": " + Grudges.DescribeFor(f.Id, Player.m_localPlayer) + ", patience " + Grudges.Patience(f, Player.m_localPlayer)
                        + ", standing " + rel.Reputation.ToString("0.0", Inv) + (rel.Allied ? " allied" : "") + (rel.AtWar ? " at war" : "")
                        + ", grievance " + rel.Grievance.ToString("0.0", Inv) + ", suspicion " + rel.Suspicion.ToString("0.0", Inv);
                }
                case "hit":
                {
                    // hit <Role> [n]: the player lands n light blows on the nearest member of that role (testing anger).
                    Player me = Player.m_localPlayer;
                    if (me == null || a.Length < 2) return "usage: hit <Chief|Guard|Villager> [n]";
                    FactionMember m = FactionMember.All.Where(x => x.Role.ToString() == a[1] && x.Humanoid != null && !x.Humanoid.IsDead()).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault();
                    if (m == null) return "no " + a[1] + " nearby";
                    int n = a.Length > 2 ? int.Parse(a[2]) : 1;
                    for (int i = 0; i < n; i++)
                    {
                        HitData hit = new HitData();
                        hit.m_damage.m_blunt = 1f;
                        hit.m_point = m.transform.position + Vector3.up;
                        hit.SetAttacker(me);
                        m.Humanoid.Damage(hit);
                    }
                    return "hit " + m.DisplayName + " " + n + " times";
                }
                case "squads":
                    return SimHost.Active && Player.m_localPlayer != null ? SimHost.Sim.DescribeSquads(new Vec2(Player.m_localPlayer.transform.position.x, Player.m_localPlayer.transform.position.z)) : "no world";
                case "expedition":
                {
                    Settlement s = SimHost.Active && a.Length > 1 ? FindSettlement(a[1]) : null;
                    Res res = Res.Wood;
                    bool named = a.Length > 2 && System.Enum.TryParse(a[2], true, out res);
                    return s == null ? "expedition <settlement> [wood|stone|food|metal]" : SimHost.Sim.DebugExpedition(s.Id, named ? (Res?)res : null);
                }
                case "campaign":
                    return SimHost.Active && a.Length > 2 ? SimHost.Sim.DebugCampaign(int.Parse(a[1]), int.Parse(a[2]), a.Length > 3 && a[3] == "camp") : "campaign <faction#> <settlement id> [camp]";
                case "steal":
                {
                    // steal <faction#>: as if the player had just been caught taking from its chests.
                    Faction f = SimHost.Active && a.Length > 1 ? SimHost.Sim.State.GetFaction(int.Parse(a[1])) : null;
                    if (f == null || Player.m_localPlayer == null) return "steal <faction#> [off]";
                    if (a.Length > 2 && a[2] == "off")
                    {
                        SimHost.Sim.PlayerRel(f, Player.m_localPlayer.GetPlayerID()).LastStoleDay = -100;
                        return "theft forgotten";
                    }
                    return "noticed " + Net.PlayerStole(f, Player.m_localPlayer.GetPlayerID(), 10f);
                }
                case "pickfield":
                {
                    // pickfield: the player tries to harvest the nearest crop of a settlement field.
                    Player me = Player.m_localPlayer;
                    if (me == null) return "no player";
                    Pickable crop = UnityEngine.Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
                        .Where(x => Property.OwnerOf(x) != null).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault();
                    if (crop == null) return "no settlement crop loaded";
                    int before = me.GetInventory().NrOfItems();
                    bool took = crop.Interact(me, false, false);
                    return (took ? "picked" : "refused") + ", items " + before + " -> " + me.GetInventory().NrOfItems();
                }
                case "dig":
                {
                    // dig <settlement> <building id> <metres> [force]: sets a building's pad that much lower,
                    // as the bug that dug pits did; with force the ground really is dug, to see it mended.
                    Settlement s = SimHost.Active && a.Length > 3 ? FindSettlement(a[1]) : null;
                    Building b = s != null ? s.Buildings.FirstOrDefault(x => x.Id == int.Parse(a[2])) : null;
                    if (b == null || float.IsNaN(b.GroundY)) return "dig <settlement> <building id> <metres> [force]";
                    b.GroundY -= float.Parse(a[3], Inv);
                    b.TerrainRev = 0;
                    Materializer.AllowDeepDig = a.Length > 4 && a[4] == "force";
                    return "pad of " + b.Kind + " #" + b.Id + " set to " + b.GroundY.ToString("0.0", Inv) + (Materializer.AllowDeepDig ? ", digging" : "");
                }
                case "npcgear":
                {
                    // npcgear <text from npcs>: what that inhabitant carries and holds.
                    FactionMember m = FactionMember.All.FirstOrDefault(x => a.Length > 1 && x.Describe().IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (m == null || m.Humanoid == null) return "nobody matches";
                    ItemDrop.ItemData held = m.Humanoid.GetCurrentWeapon();
                    return m.Describe() + ": holds " + (held != null && held.m_dropPrefab != null ? held.m_dropPrefab.name : "-") + "; carries " + string.Join(", ", m.Humanoid.GetInventory().GetAllItems().Select(i => (i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name) + (i.m_equipped ? "*" : "")));
                }
                case "roads": return Roads.Describe();
                case "roadcheck":
                {
                    // roadcheck <settlement>: its roads walked from 100 m out to the square; gates, lamps, fences.
                    Settlement checkedTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return checkedTown == null ? "usage: roadcheck <settlement>" : Roads.Check(checkedTown);
                }
                case "roadslope":
                {
                    // roadslope <settlement> [metres out | steep]: the ground of its road now, against the bed it should have.
                    Settlement slopeTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (slopeTown == null) return "usage: roadslope <settlement> [metres|steep]";
                    float around = a.Length > 2 && a[2] != "steep" ? float.Parse(a[2], Inv) : Roads.SteepestOut(slopeTown);
                    return Roads.Slope(slopeTown, around);
                }
                case "pathcheck":
                {
                    // pathcheck <settlement>: the steepest step along each building's path, as the ground is now.
                    Settlement pathTown = a.Length > 1 ? FindSettlement(a[1]) : null;
                    return pathTown == null ? "usage: pathcheck <settlement>" : Materializer.PathCheck(pathTown);
                }
                case "roadtp":
                case "roadview":
                {
                    // roadtp <settlement> <metres out>: stand on the road there. roadview: look along it from above.
                    Settlement town = a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (town == null || Player.m_localPlayer == null) return "usage: " + a[0] + " <settlement> <metres>";
                    if (!Roads.HasRoad(town)) return town.Name + " has no road (no way over land to a neighbour)";
                    if (a[0] == "roadview" && a.Length > 2 && a[2] == "gate")
                    {
                        // roadview 4 gate [n] [height]: from outside the wall, along its n-th street, at the gate and the square behind it.
                        float street = Roads.StreetBearing(town, a.Length > 3 ? int.Parse(a[3]) : 0);
                        float wallOut = (town.Tier >= Tier.City ? Defs.CityWallRadius : Defs.PalisadeRadius) + 22f;
                        return View(new[] { "view", town.Id.ToString(Inv), wallOut.ToString("0", Inv), a.Length > 4 ? a[4] : "7", street.ToString("0.0", Inv) }) + ", along the street at " + street.ToString("0.0", Inv) + " deg";
                    }
                    // `roadview 4 dry` looks along the road from where the last `roadtp 4 dry` put the player.
                    // `roadtp 4 steep`: where the land along its road is steepest.
                    if (a.Length > 2 && a[2] == "steep") a[2] = Roads.SteepestOut(town).ToString("0", Inv);
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
                    return hireTown == null || Player.m_localPlayer == null ? "usage: hire <settlement>" : Company.HireReply(hireTown, Player.m_localPlayer, true) + " - hired now " + Company.HiredCount();
                case "marks": return AttachPoints.Marks(a.Length < 2 || a[1] != "off");
                case "hidehud":
                    if (Hud.instance == null) return "no hud";
                    Hud.instance.m_userHidden = a.Length < 2 || a[1] != "off";
                    return Hud.instance.m_userHidden ? "hud hidden" : "hud shown";
                case "hideme":
                    // The player out of the picture while the camera films (the body stays, so the world keeps loading around it).
                    if (Player.m_localPlayer == null) return "no player";
                    GameObject visual = Traverse.Create(Player.m_localPlayer).Field("m_visual").GetValue<GameObject>();
                    visual.SetActive(a.Length > 1 && a[1] == "off");
                    return visual.activeSelf ? "player shown" : "player hidden";
                case "env": return Env(a);
                case "god": return God(a);
                case "dump": return Dump(a);
                case "spawn": return Spawn(a);
                case "specimen":
                {
                    // specimen <prefab> <yaw> [prefab2 dx dy dz yaw2 [lift]]: puts a piece up in the air 5 m ahead (and a
                    // second beside it), lift metres up (3; higher, for the sky behind it), to see how a prefab sits.
                    Player me = Player.m_localPlayer;
                    if (me == null || a.Length < 3) return "specimen <prefab> <yaw> [prefab2 dx dy dz yaw2 [lift]]";
                    Vector3 at = me.transform.position + Vector3.forward * 5f + Vector3.up * (a.Length > 8 ? float.Parse(a[8], Inv) : 3f);
                    GameObject first = ZNetScene.instance.GetPrefab(a[1]);
                    if (first == null) return "no prefab " + a[1];
                    UnityEngine.Object.Instantiate(first, at, Quaternion.Euler(0f, float.Parse(a[2], Inv), 0f));
                    if (a.Length > 7 && ZNetScene.instance.GetPrefab(a[3]) != null)
                        UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(a[3]), at + new Vector3(float.Parse(a[4], Inv), float.Parse(a[5], Inv), float.Parse(a[6], Inv)), Quaternion.Euler(0f, float.Parse(a[7], Inv), 0f));
                    Specimen = at;
                    return "specimen at " + at;
                }
                case "campsite":
                {
                    // campsite <home> <target>: why the ground on the way does or does not take a war camp.
                    Settlement h = a.Length > 2 ? FindSettlement(a[1]) : null, t = a.Length > 2 ? FindSettlement(a[2]) : null;
                    if (h == null || t == null) return "campsite <home> <target>";
                    GameWorld world = new GameWorld();
                    Vec2 way = t.Pos - h.Pos;
                    Vec2 back = way * (-1f / Mathf.Max(1f, way.Length()));
                    Dictionary<string, int> tally = new Dictionary<string, int>();
                    for (int k = 0; k < 60; k++)
                    {
                        float off = Mathf.Max(200f, way.Length() * UnityEngine.Random.Range(0.3f, 0.45f));
                        Vec2 p = t.Pos + back * off + Vec2.FromAngle(UnityEngine.Random.Range(0f, 6.28f), UnityEngine.Random.Range(0f, 100f));
                        Biome biome;
                        string why = world.ProbeReason(p, k % 2 == 0 ? 14f : 9f, out biome) + (k % 2 == 0 ? "/14" : "/9");
                        int n0;
                        tally.TryGetValue(why, out n0);
                        tally[why] = n0 + 1;
                    }
                    return string.Join(", ", tally.Select(x => x.Key + "=" + x.Value).ToArray());
                }
                case "wreck":
                {
                    // wreck <settlement> <building id> [share|state]: breaks that share of a building's pieces in the
                    // world (as an axe would), or tells the building's state on record.
                    Settlement s = SimHost.Active && a.Length > 2 ? FindSettlement(a[1]) : null;
                    Building b = s != null ? s.Buildings.FirstOrDefault(x => x.Id == int.Parse(a[2])) : null;
                    if (b == null) return "wreck <settlement> <building id> [share|state]";
                    if (a.Length > 3 && a[3] == "state") return b.Kind + " #" + b.Id + " " + b.State + ", pieces on record " + b.SpawnedPieces;
                    float share = a.Length > 3 ? float.Parse(a[3], Inv) : 0.6f;
                    List<ZDO> found = new List<ZDO>();
                    ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(new Vector3(s.Pos.X, 0f, s.Pos.Z)), SimulationDistance.OriginalDistance, found);
                    List<ZDO> mine = found.Where(z => z.GetInt(Materializer.HashSettlement) == s.Id && z.GetInt(Materializer.HashBuilding) == b.Id).ToList();
                    int broken = 0;
                    foreach (ZDO z in mine.Take(Mathf.CeilToInt(mine.Count * share)))
                    {
                        ZNetView v = ZNetScene.instance.FindInstance(z);
                        if (v == null) continue;
                        v.ClaimOwnership();
                        ZNetScene.instance.Destroy(v.gameObject);
                        broken++;
                    }
                    return "broke " + broken + " of " + mine.Count + " pieces of " + b.Kind + " #" + b.Id;
                }
                case "openchest":
                {
                    // openchest <settlement> <building id>: the player steps up to that building's chest and opens it.
                    Settlement s = SimHost.Active && a.Length > 2 ? FindSettlement(a[1]) : null;
                    Player me = Player.m_localPlayer;
                    if (s == null || me == null) return "openchest <settlement> <building id|kind>";
                    int bid;
                    if (!int.TryParse(a[2], out bid))
                    {
                        Building byKind = s.Buildings.FirstOrDefault(x => x.Kind.ToString() == a[2] && x.State == BuildingState.Complete);
                        if (byKind == null) return "no " + a[2] + " in " + s.Name;
                        bid = byKind.Id;
                    }
                    // [nth]: a storehouse has three, in the order Chests gives them their goods (wood and stone, food, metal and coins).
                    int nth = a.Length > 3 ? int.Parse(a[3]) : 0;
                    Container chest = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c =>
                    {
                        ZNetView v = c.GetComponent<ZNetView>();
                        return v != null && v.IsValid() && v.GetZDO().GetInt(Materializer.HashSettlement) == s.Id && v.GetZDO().GetInt(Materializer.HashBuilding) == bid;
                    }).OrderBy(c => c.transform.position.x * 1.37f + c.transform.position.z).Skip(nth).FirstOrDefault();
                    if (chest == null) return "no chest of building " + bid + " loaded";
                    Vector3 toward = new Vector3(s.Pos.X, chest.transform.position.y, s.Pos.Z) - chest.transform.position;
                    me.transform.position = chest.transform.position + Vector3.ClampMagnitude(toward, 0.9f) + Vector3.up * 0.2f;
                    bool opened = chest.Interact(me, false, false);
                    _openedChest = chest;
                    return (opened ? "opened" : "could not open") + " the chest of building " + bid + "; " + (NightThief.Counting ? "the count is on" : "no count");
                }
                case "chestmove":
                {
                    // chestmove <put|take> <prefab> <n>: items into or out of the chest open on screen, as the player would move them.
                    // The chest on screen, or the one `openchest` just opened (the screen opens a frame or more after the request).
                    Container open = InventoryGui.instance != null ? Traverse.Create(InventoryGui.instance).Field("m_currentContainer").GetValue<Container>() : null;
                    if (open == null) open = _openedChest;
                    Player me = Player.m_localPlayer;
                    GameObject prefab = a.Length > 3 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(a[2]) : null;
                    if (open == null || me == null || prefab == null) return "chestmove <put|take> <prefab> <n>, with a chest open";
                    int n = int.Parse(a[3]);
                    string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                    Inventory chestInv = open.GetInventory();
                    if (a[1] == "put") chestInv.AddItem(prefab, n);
                    else
                    {
                        n = Mathf.Min(n, chestInv.CountItems(name));
                        chestInv.RemoveItem(name, n);
                        me.GetInventory().AddItem(prefab, n);
                    }
                    return a[1] + " " + n + " " + a[2] + ", the chest has " + chestInv.CountItems(name);
                }
                case "envoy":
                {
                    // envoy <settlement> [go|view]: its envoy's tent and envoy; go: the player stands at the tent's mouth;
                    // view: the camera on the tent from in front and to one side.
                    Settlement s = SimHost.Active && a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (s == null) return "envoy <settlement> [go|view]";
                    Vector3 post;
                    float yaw;
                    if (a.Length > 2 && a[2] == "view" && EnvoyPost.PostOf(s, out post, out yaw))
                    {
                        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
                        CamTarget = post + Vector3.up * 1.2f - facing * Vector3.forward * 2.8f;
                        CamPos = post + facing * new Vector3(4f, 2.5f, 6f);
                    }
                    if (a.Length > 2 && a[2] == "go" && Player.m_localPlayer != null && EnvoyPost.PostOf(s, out post, out yaw))
                    {
                        Vector3 stand = post + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 2.5f);
                        float ground;
                        if (ZoneSystem.instance.GetGroundHeight(stand, out ground)) stand.y = ground + 0.3f;
                        Player.m_localPlayer.TeleportTo(stand, Player.m_localPlayer.transform.rotation, false);
                    }
                    return EnvoyPost.Describe(s);
                }
                case "thief":
                {
                    // thief <settlement>: whether a night thief's count is on and whether the settlement was raised.
                    Settlement s = SimHost.Active && a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (s == null) return "thief <settlement>";
                    return (NightThief.Counting ? "count on" : "no count") + ", " + (NightWatch.Angry(s.Id) ? "the settlement is raised" : "the settlement sleeps on");
                }
                case "closechest":
                    if (InventoryGui.instance != null) InventoryGui.instance.Hide();
                    if (_openedChest != null && _openedChest.IsInUse() && _openedChest.GetComponent<ZNetView>().IsOwner()) _openedChest.SetInUse(false);
                    _openedChest = null;
                    return "chest closed";
                case "specam":
                    // specam <dx> <dy> <dz>: the camera that far from the last specimen, looking at it.
                    CamPos = Specimen + new Vector3(float.Parse(a[1], Inv), float.Parse(a[2], Inv), float.Parse(a[3], Inv));
                    CamTarget = Specimen;
                    return "camera on the specimen";
                case "find": return Find(a);
                case "mat": return Materializer.Describe();
                case "settle":
                    // settle <seconds>: ground work waits that long after each change, as before 0.7.0 (1.5); 0 = until the heightmap is rebuilt.
                    if (a.Length > 1) Materializer.FixedSettle = float.Parse(a[1], Inv);
                    return Materializer.FixedSettle > 0f ? "ground work waits " + Materializer.FixedSettle.ToString("0.0", Inv) + " s after each change" : "ground work waits for the heightmap's rebuild";
                case "destroy":
                    return SimHost.Active && a.Length > 1 && SimHost.Sim.DebugDestroy(int.Parse(a[1])) ? "destroyed" : "no such live settlement";
                case "owner":
                {
                    // owner <settlement id> <faction#>: puts a place in another faction's hands, as a won
                    // assault would (testing). Only the owner changes: no battle and no losses.
                    if (!SimHost.Active || a.Length < 3) return "owner <settlement id> <faction#>";
                    Settlement taken = SimHost.Sim.State.GetSettlement(int.Parse(a[1]));
                    Faction owner = SimHost.Sim.State.GetFaction(int.Parse(a[2]));
                    if (taken == null || taken.Destroyed || owner == null) return "no such settlement or faction";
                    taken.FactionId = owner.Id;
                    return taken.Name + " is now held by " + Texts.FactionName(owner);
                }
                case "scoutloss":
                {
                    // scoutloss <faction#> [reported]: the scouts of that faction's campaign fall, after
                    // seeing the place (reported) or before it. Testing.
                    if (!SimHost.Active || a.Length < 2) return "scoutloss <faction#> [reported]";
                    Campaign running = SimHost.Sim.CampaignOf(int.Parse(a[1]));
                    if (running == null || running.Stage != CampaignStage.Scouting) return "no campaign is scouting for that faction";
                    if (a.Length > 2 && a[2] == "reported") running.EnemyEstimate = 10f;
                    Squad band = SimHost.Sim.State.Squads.FirstOrDefault(q => q.Id == running.SquadId);
                    if (band == null) return "no scout band for campaign " + running.Id;
                    band.Men = 0f;
                    band.Soldiers = 0f;
                    return "the scouts of campaign " + running.Id + (running.EnemyEstimate >= 0f ? " are lost after their report" : " are lost before their report");
                }
                case "capture":
                    // capture <settlement id> <faction#>: a place changes hands as after a won assault (testing).
                    return SimHost.Active && a.Length > 2 ? SimHost.Sim.DebugCapture(int.Parse(a[1]), int.Parse(a[2])) : "capture <settlement id> <faction#>";
                case "campinfo":
                {
                    // campinfo <settlement>: the ties a place has to a campaign, an outpost or a road post.
                    Settlement info = SimHost.Active && a.Length > 1 ? FindSettlement(a[1]) : null;
                    if (info == null) return "campinfo <settlement>";
                    Faction holder = SimHost.Sim.State.GetFaction(info.FactionId);
                    return info.Name + " #" + info.Id + ": owner " + (holder != null ? Texts.FactionName(holder) : "?")
                        + ", campaign " + info.CampaignId + ", parent " + info.ParentId + ", goal " + info.OutpostGoal
                        + ", road post " + info.RoadPost + ", leaderless until " + info.LeaderlessUntil.ToString("0.0", Inv)
                        + ", destroyed " + info.Destroyed
                        + ", buildings: " + string.Join("; ", info.Buildings.Select(b => b.Kind + " rev " + b.LayoutRev + " pieces " + b.SpawnedPieces));
                }
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
                case "hudinfo": return HudWidget.Describe();
                case "hud": return SimHost.Active && Player.m_localPlayer != null ? HudWidget.Compose() : "no world";
                case "npcs": return NpcReport();
                case "beds": return Beds(a);
                case "chests": return Chests.Describe();
                case "inside": return Inside(a);
                case "outside": return Outside(a);
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
        private static Container _openedChest;

        private static string Help()
        {
            return "lf status | factions | chronicle [n] | advance <days> | rewind [day] | tp <settlement> | rep <faction#> <value> | save";
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
            // The factions move on by this many days; there is no going back but a world backup.
            return "advanced " + days.ToString("0.##", Inv) + " days, now day " + SimHost.Sim.State.Day.ToString("0.0", Inv) + " (lf advance moves the factions ahead by that many days; lf rewind takes them back)";
        }

        internal static Settlement FindSettlement(string key)
        {
            if (!SimHost.Active) return null;
            // "camp": the first war camp standing (testing campaigns, whose camps get new ids).
            if (key == "camp") return SimHost.Sim.State.Settlements.FirstOrDefault(s => s.CampaignId != 0 && !s.Destroyed);
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
            Vector3 p;
            if (a.Length > 4 && a[2] == "at")
            {
                // here <faction> at <x> <z>: a spot on the map, wherever the player is.
                p = new Vector3(float.Parse(a[3], Inv), 0f, float.Parse(a[4], Inv));
            }
            else
            {
                float ahead = a.Length > 2 ? float.Parse(a[2], Inv) : 40f;
                p = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * ahead;
            }
            Biome biome = GameWorld.Map(WorldGenerator.instance.GetBiome(p.x, p.z));
            if (WorldGenerator.instance.GetHeight(p.x, p.z) < ZoneSystem.c_WaterLevel + 1.5f) return "that spot is under water";
            // The same test a faction puts a new site to, so a settlement founded by hand has room to grow.
            Biome probed;
            string reason = new GameWorld().ProbeReason(new Vec2(p.x, p.z), 22f, out probed);
            if (reason != "ok" && Array.IndexOf(a, "force") < 0) return "no room to build there (" + reason + "); add 'force' to found it anyway";
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
        /// <summary>
        /// outside &lt;settlement&gt; &lt;Kind&gt; [nth] [dist] [height] [bearing]: the camera on a building from outside,
        /// bearing in degrees round it from its front (0 = in front, 90 = its right side, its local +X).
        /// </summary>
        private static string Outside(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            string kind = a.Length > 2 ? a[2] : "Hut";
            int nth = a.Length > 3 ? int.Parse(a[3], Inv) : 0;
            float dist = a.Length > 4 ? float.Parse(a[4], Inv) : 12f, height = a.Length > 5 ? float.Parse(a[5], Inv) : 4f;
            float bearing = (a.Length > 6 ? float.Parse(a[6], Inv) : 0f) * Mathf.Deg2Rad;
            foreach (Building b in s.Buildings)
            {
                if (!b.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase) || b.State != BuildingState.Complete || float.IsNaN(b.GroundY)) continue;
                if (nth-- > 0) continue;
                Vec2 offset = new Vec2(0f, 0f);
                float yaw = 0f;
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                GearView.Off();
                CamPos = origin + rot * new Vector3(Mathf.Sin(bearing) * dist, height, Mathf.Cos(bearing) * dist);
                CamTarget = origin + Vector3.up * 2.5f;
                return "looking at " + b.Kind + " #" + b.Id + " at " + origin;
            }
            return "no complete " + kind;
        }

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
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
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
                List<string> loose = new List<string>();
                foreach (RaycastHit hit in Physics.CapsuleCastAll(feet, head, r, dir.normalized, dir.magnitude))
                {
                    if (hit.collider.isTrigger) continue;
                    ZNetView view = hit.collider.GetComponentInParent<ZNetView>();
                    string name = view != null ? Utils.GetPrefabName(view.gameObject) : hit.collider.name;
                    if (name.Contains("door") || hit.collider.GetComponentInParent<Character>() != null || hit.collider.GetComponentInParent<Heightmap>() != null) continue;
                    // Something dropped on the threshold is pushed aside by whoever walks in (and swept away
                    // in time, LootSweep): told apart from what the house or its yard puts in the way.
                    if (hit.collider.GetComponentInParent<ItemDrop>() != null) loose.Add(name + " " + Utils.DistanceXZ(hit.point, door).ToString("0.0", Inv) + " m in");
                    else blockers.Add(name);
                }
                sb.AppendLine(b.Kind + " #" + b.Id + " (capsule r " + r.ToString("0.00", Inv) + " h " + h.ToString("0.00", Inv) + "): " + (blockers.Count == 0 ? "clear" : "BLOCKED by " + string.Join(",", blockers))
                    + (loose.Count > 0 ? ", LOOSE ITEMS " + string.Join(",", loose) : ""));
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
                if (b.Plot >= 0) Defs.PlotTransform(s, b.Plot, out offset, out yaw);
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
            if (a.Length < 2) return "usage: lf watch <text from the npcs list>[+more text]";
            // Several pieces joined by + must all be on the line: `watch Farmer]+carrying`.
            string[] wanted = string.Join(" ", a.Skip(1)).Split('+');
            foreach (FactionMember m in FactionMember.All)
            {
                string line = m.Describe();
                if (!wanted.All(w => line.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                GearView.Off();
                Transform t = m.transform;
                CamTarget = t.position + Vector3.up * 1.2f;
                CamPos = t.position - t.forward * 3.2f + t.right * 2.2f + Vector3.up * 2f;
                return "watching " + line;
            }
            return "nobody matches " + string.Join("+", wanted);
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

        /// <summary>beds &lt;settlement&gt;: where each of its people sleeps; "on the floor" for anyone without a bed.</summary>
        private static string Beds(string[] a)
        {
            Settlement s = a.Length > 1 ? FindSettlement(a[1]) : null;
            if (s == null) return "no such settlement";
            StringBuilder sb = new StringBuilder();
            int floor = 0, sleepers = 0;
            foreach (FactionMember m in FactionMember.All)
            {
                if (m.SettlementId != s.Id || (m.Role != NpcRole.Villager && m.Role != NpcRole.Chief)) continue;
                string place = m.SleepPlace(s);
                sleepers++;
                if (!place.Contains(" bed ")) floor++;
                sb.AppendLine("  " + m.DisplayName + " [" + m.Role + "] " + place);
            }
            return sleepers + " sleepers, " + (floor == 0 ? "everyone in a bed" : floor + " on the floor") + "\n" + sb.ToString().TrimEnd();
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

        private static Vector3 Specimen;

        /// <summary>The big map's zoom held by `lf map large <zoom>` (testing); 0 when not held.</summary>
        public static float PinnedZoom;

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

        private static string God(string[] a)
        {
            if (Player.m_localPlayer == null) return "no player";
            // "god off": stamina and health count again (measuring what a swing costs).
            bool on = a.Length < 2 || a[1] != "off";
            Player.m_localPlayer.SetGodMode(on);
            Player.m_localPlayer.SetGhostMode(on);
            Player.m_debugMode = true;
            return on ? "god + ghost on" : "god + ghost off";
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
                // Solid boxes in the prefab's own space: the mesh bounds also take in drapes and overhangs.
                List<string> boxes = new List<string>();
                foreach (BoxCollider box in prefab.GetComponentsInChildren<BoxCollider>(true))
                {
                    if (box.isTrigger) continue;
                    Matrix4x4 m = toRoot * box.transform.localToWorldMatrix;
                    boxes.Add(V(m.MultiplyPoint3x4(box.center - box.size * 0.5f)) + ".." + V(m.MultiplyPoint3x4(box.center + box.size * 0.5f)));
                }
                if (boxes.Count > 0) sb.Append(" boxes[" + string.Join(" ", boxes) + "]");
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

    [HarmonyLib.HarmonyPatch(typeof(Minimap), "Update")]
    internal static class DevZoomPin
    {
        private static void Postfix(Minimap __instance)
        {
            if (Commands.PinnedZoom > 0f && __instance.m_mode == Minimap.MapMode.Large) __instance.LargeZoom = Commands.PinnedZoom;
        }
    }
}
