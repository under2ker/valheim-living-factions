using System.Collections.Generic;
using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    internal enum Job { Woodcutter = 0, Stonecutter = 1, Farmer = 2 }

    /// <summary>
    /// The night watch of each settlement: guards challenge strangers who come close after dark,
    /// and sometimes decide to drive them off by force.
    /// </summary>
    internal static class NightWatch
    {
        private class Watch
        {
            public int Night = -1;
            public float WarnedAt = -1f;
            public bool Resolved;
            public float AngryUntil;
        }

        private static readonly Dictionary<int, Watch> Watches = new Dictionary<int, Watch>();

        public static void Reset() { Watches.Clear(); }

        public static bool Angry(int settlementId)
        {
            Watch w;
            return Watches.TryGetValue(settlementId, out w) && Time.time < w.AngryUntil;
        }

        /// <summary>Called by a guard on night duty. Returns the line the guard should say, or null.</summary>
        public static string Challenge(Settlement s, Faction f, Player player, float distanceToCentre)
        {
            Watch w;
            if (!Watches.TryGetValue(s.Id, out w))
            {
                w = new Watch();
                Watches[s.Id] = w;
            }
            int night = (int)(SimHost.WorldDay + 0.5);
            if (w.Night != night)
            {
                w.Night = night;
                w.WarnedAt = -1f;
                w.Resolved = false;
            }
            if (w.Resolved) return null;

            // Friends come and go as they please; enemies are attacked anyway.
            Standing standing = SimHost.Sim.GetStanding(f, player.GetPlayerID());
            if (standing != Standing.Neutral && standing != Standing.Wary) return null;
            float limit = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 16f;
            if (distanceToCentre > limit)
            {
                // Backing off after the warning settles it peacefully.
                if (w.WarnedAt > 0f && Time.time - w.WarnedAt > 4f) w.WarnedAt = -1f;
                return null;
            }
            if (w.WarnedAt < 0f)
            {
                w.WarnedAt = Time.time;
                return "night_halt";
            }
            if (Time.time - w.WarnedAt < 8f) return null;

            w.Resolved = true;
            if (Random.value < ModConfig.NightAttackChance.Value)
            {
                w.AngryUntil = Time.time + 90f;
                foreach (FactionMember member in FactionMember.All)
                {
                    if (member.SettlementId == s.Id && member.Role != NpcRole.Villager) member.Enrage();
                }
                return "night_attack";
            }
            return "night_pass";
        }
    }

    // The daily routine. By day villagers walk to a work spot and swing a tool there (an animation
    // only: nothing is hit, felled or dug), rest at home between shifts and help on building sites.
    // At night villagers and the chief go indoors to sleep while guards walk the perimeter.
    // Movement is steered through the AI's own patrol point, so noticing enemies, fighting and
    // fleeing keep working exactly as in the base game.
    internal partial class FactionMember
    {
        private static readonly int HashHome = "lf_home".GetStableHashCode();
        private static readonly int HashAsleep = "lf_asleep".GetStableHashCode();

        /// <summary>How many workers are busy at each work target (keyed by its position).</summary>
        private static readonly Dictionary<long, int> Claims = new Dictionary<long, int>();

        private Vector3 _workSpot;
        private Vector3 _workTarget;
        private bool _working;
        private bool _talking;
        private bool _hasClaim;
        private long _claim;
        private float _phaseUntil;
        private float _phaseStart;
        private float _nextSwing;
        private float _bedtime = -1f;
        private float _nextBark;
        private int _patrolLeg;
        private ItemDrop.ItemData _tool;
        private bool _hauling;
        private GameObject _cargo;
        private string _errand;
        private bool _onTower;
        private float _towerSince;
        private float _journeyAt;
        private bool _journeyIn;
        private float _journeyNext;

        private static readonly int HashJourneyAt = "lf_jat".GetStableHashCode();
        private static readonly int HashJourneyIn = "lf_jin".GetStableHashCode();

        public Job Job { get { return (Job)(Mathf.Abs(Seed) % 3); } }

        private bool Asleep { get { return _view.GetZDO().GetBool(HashAsleep); } }

        private Vector3 Home { get { return _view.GetZDO().GetVec3(HashHome, transform.position); } }

        private void RememberHome() { _view.GetZDO().Set(HashHome, transform.position); }

        private void SetPatrol(Vector3 point, float range)
        {
            ZDO zdo = _view.GetZDO();
            zdo.Set(ZDOVars.s_patrolPoint, point);
            zdo.Set(ZDOVars.s_patrol, true);
            if (_ai != null) _ai.m_randomMoveRange = range;
        }

        /// <summary>One line on what this inhabitant is doing right now (for the dev `npcs` command).</summary>
        public string Describe()
        {
            string state = Asleep ? "asleep" : _talking ? "talking" : _onTower ? "on-tower" : _hauling ? "hauling" : _working ? "working" : _errand != null ? _errand : _bedtime >= 0f ? "to-bed"
                : Role == NpcRole.Trader ? "journey " + (_journeyIn ? "in " : "out ") + _journeyAt.ToString("0") : "idle";
            bool busy = _ai != null && (_ai.IsAlerted() || _ai.GetTargetCreature() != null);
            return DisplayName + " [" + Role + (Role == NpcRole.Villager ? "/" + Job : "") + "] " + state + (_cargo != null ? " carrying" : "") + (busy ? " ALERT" : "") + (_ai != null && _ai.IsAggravated() ? " AGGRO" : "")
                + " toSpot " + (_working ? Utils.DistanceXZ(transform.position, _workSpot).ToString("0.0") : "-")
                + " tool " + (_tool != null && _tool.m_dropPrefab != null ? _tool.m_dropPrefab.name : "-")
                + " hp " + _humanoid.GetHealth().ToString("0") + " y " + transform.position.y.ToString("0.0");
        }

        public static int ClaimCount { get { return Claims.Count; } }

        public static int MaxClaim
        {
            get
            {
                int max = 0;
                foreach (int c in Claims.Values) max = Mathf.Max(max, c);
                return max;
            }
        }

        public static void ResetClaims() { Claims.Clear(); }

        public void Enrage()
        {
            if (_ai != null && _view != null && _view.IsValid() && _view.IsOwner()) _ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);
        }

        private void ReleaseClaim()
        {
            if (!_hasClaim) return;
            _hasClaim = false;
            int count;
            if (Claims.TryGetValue(_claim, out count))
            {
                if (count <= 1) Claims.Remove(_claim);
                else Claims[_claim] = count - 1;
            }
        }

        private static long ClaimKey(Vector3 target)
        {
            return ((long)Mathf.RoundToInt(target.x) << 32) ^ (uint)Mathf.RoundToInt(target.z);
        }

        private bool TryClaim(Vector3 target, int limit)
        {
            long key = ClaimKey(target);
            int count;
            Claims.TryGetValue(key, out count);
            if (count >= limit) return false;
            ReleaseClaim();
            Claims[key] = count + 1;
            _claim = key;
            _hasClaim = true;
            return true;
        }

        private bool _turning;
        private Quaternion _turnGoal;

        /// <summary>Starts a smooth turn towards a direction; <see cref="TurnStep"/> carries it out frame by frame.</summary>
        private void TurnTo(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            _turnGoal = Quaternion.LookRotation(dir);
            _turning = true;
            _humanoid.SetLookDir(dir.normalized, 0.4f);
        }

        private void TurnStep()
        {
            if (!_turning) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _turnGoal, 300f * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, _turnGoal) < 1f) _turning = false;
        }

        private void StopWork()
        {
            _working = false;
            _hauling = false;
            _errand = null;
            DropCargo();
            ReleaseClaim();
        }

        private void DropCargo()
        {
            if (_cargo != null) Destroy(_cargo);
            _cargo = null;
        }

        /// <summary>
        /// Puts what was just gathered on the worker's back: a visual copy of the item's mesh,
        /// carried to the storehouse. It is decoration only - the simulation books the goods.
        /// </summary>
        private void PickUpCargo()
        {
            DropCargo();
            string itemName = Job == Job.Woodcutter ? "RoundLog" : Job == Job.Stonecutter ? "Stone" : "Turnip";
            GameObject prefab = ObjectDB.instance.GetItemPrefab(itemName);
            MeshFilter source = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            MeshRenderer look = source != null ? source.GetComponent<MeshRenderer>() : null;
            Transform spine = Utils.FindChild(transform, "Spine2");
            if (source == null || look == null || spine == null) return;
            _cargo = new GameObject("lf_cargo");
            _cargo.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            _cargo.AddComponent<MeshRenderer>().sharedMaterials = look.sharedMaterials;
            _cargo.transform.SetParent(spine, false);
            float size = Job == Job.Woodcutter ? 0.6f : Job == Job.Stonecutter ? 1.4f : 0.8f;
            _cargo.transform.localScale = Vector3.one * size / Mathf.Max(0.01f, spine.lossyScale.x);
            _cargo.transform.position = spine.position + transform.up * 0.2f - transform.forward * 0.28f;
            _cargo.transform.rotation = transform.rotation;
        }

        /// <summary>Where gathered goods are taken: the storehouse if there is one, else the fire.</summary>
        private Vector3 DropOff(Settlement s, Vector3 centre)
        {
            foreach (Building b in s.Buildings)
            {
                if (b.Kind != BuildingKind.Storehouse || b.State != BuildingState.Complete || b.Plot < 0) continue;
                Vector3 site = PlotPosition(s, b.Plot);
                // Not all to one flagstone: each carrier has a place of their own along the front.
                Vector3 front = (centre - site).normalized;
                Vector3 along = new Vector3(-front.z, 0f, front.x);
                return site + front * 3.6f + along * (((Mathf.Abs(Seed) % 5) - 2) * 1.3f);
            }
            return centre + (transform.position - centre).normalized * 3f;
        }

        /// <summary>Called about once a second for inhabitants the local peer owns.</summary>
        private void ThinkRoutine()
        {
            if (_ai == null || Role == NpcRole.Raider) return;
            Settlement s = SimHost.Sim.State.GetSettlement(SettlementId);
            if (s == null) return;
            if (Npcs.IsTemporary(Role))
            {
                OnTheMove(s);
                return;
            }
            if (s.Destroyed) return;
            Vector3 centre = new Vector3(s.Pos.X, transform.position.y, s.Pos.Z);
            bool night = EnvMan.IsNight();
            bool busy = _ai.IsAlerted() || _ai.GetTargetCreature() != null || _ai.IsAggravated();

            if (busy || _talking)
            {
                // Trouble (or a visitor) comes first; sleepers come out of their houses.
                // An archer keeps the tower and shoots from it; anyone else climbs down to fight.
                if (_onTower && busy && !IsArcher) LeaveTower(s);
                if (!_onTower) StopWork();
                _phaseUntil = 0f;
                _bedtime = -1f;
                if (busy && Asleep) WakeUp(s);
                return;
            }

            if (Role == NpcRole.Guard)
            {
                GuardDuty(s, centre, night);
                return;
            }

            if (night && ModConfig.NightSleep.Value)
            {
                StopWork();
                GoToBed(s, centre);
                return;
            }
            _bedtime = -1f;
            if (Asleep)
            {
                WakeUp(s);
                _phaseUntil = 0f;
            }
            if (Role == NpcRole.Chief)
            {
                if (Time.time >= _phaseUntil)
                {
                    _phaseUntil = Time.time + 120f;
                    SetPatrol(Home, 4f);
                }
                return;
            }
            if (ModConfig.VisibleWork.Value) WorkDay(s, centre);
        }

        // ------------------------------------------------------------------ day

        private bool _staggered;

        private void WorkDay(Settlement s, Vector3 centre)
        {
            if (!_staggered)
            {
                // People do not all set off on the same heartbeat: the first shift starts at a time of each one's own.
                _staggered = true;
                _phaseUntil = Time.time + Random.Range(0f, 40f);
                SetPatrol(Home, 6f);
                return;
            }
            if (_hauling)
            {
                // Walk the load in; put it down at the storehouse (or give up after a while).
                if (Utils.DistanceXZ(transform.position, _workSpot) > 2.8f && Time.time < _phaseUntil) return;
                StopWork();
                _phaseUntil = Time.time + Random.Range(8f, 16f);
                SetPatrol(Home, 6f);
                return;
            }
            if (Time.time >= _phaseUntil)
            {
                bool wasWorking = _working;
                bool atSpot = wasWorking && Utils.DistanceXZ(transform.position, _workSpot) < 3f;
                StopWork();
                if (atSpot && _tool != null && _tool.m_dropPrefab != null && _tool.m_dropPrefab.name != "Hammer")
                {
                    _hauling = true;
                    _humanoid.UnequipItem(_tool, false);
                    PickUpCargo();
                    _workSpot = DropOff(s, centre);
                    _phaseUntil = Time.time + 50f;
                    SetPatrol(_workSpot, 1f);
                    return;
                }
                if (wasWorking || !FindWork(s, centre))
                {
                    // Between shifts: a call on a neighbour, a walk out along the road and back, or a breather at home.
                    if (!ModConfig.Errands.Value || !RunErrand(s, centre))
                    {
                        _phaseUntil = Time.time + Random.Range(15f, 30f);
                        SetPatrol(Home, 6f);
                    }
                }
                else
                {
                    _working = true;
                    _phaseStart = Time.time;
                    _phaseUntil = Time.time + Random.Range(70f, 120f);
                    SetPatrol(_workSpot, 1.2f);
                    // Feet that take the same way every day leave a trail to it.
                    Roads.WornTrail(s.Id, new Vector3(Home.x, 0f, Home.z), new Vector3(_workSpot.x, 0f, _workSpot.z));
                }
                return;
            }
            if (!_working) return;

            float distance = Utils.DistanceXZ(transform.position, _workSpot);
            // Once at the spot they stay on it instead of shuffling about between swings.
            if (distance <= 2.6f && _ai.m_randomMoveRange > 0.5f) SetPatrol(_workSpot, 0.35f);
            if (distance > 2.6f)
            {
                // Could not get there (fenced off, cliff, water): give the spot up instead of pacing.
                if (Time.time - _phaseStart > 45f) _phaseUntil = 0f;
                return;
            }
            if (Time.time < _nextSwing) return;
            _nextSwing = Time.time + Random.Range(2.4f, 3.8f);
            Swing();
        }

        private bool FindWork(Settlement s, Vector3 centre)
        {
            // Whoever is free helps on the building site first.
            Building project = Simulation.CurrentProject(s);
            if (project != null && project.Plot >= 0 && !float.IsNaN(project.GroundY) && Mathf.Abs(Seed) % 2 == 0)
            {
                Vector3 site = PlotPosition(s, project.Plot);
                float a = (Seed % 360) * Mathf.Deg2Rad;
                Vector3 stand = site + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (Blueprints.Radius(project.Kind) + 0.6f);
                // Two builders to a site; the rest keep to their own trade.
                if (TryClaim(site, 2) && SetSpot(site, stand, "Hammer")) return true;
            }

            switch (Job)
            {
                case Job.Woodcutter:
                    return WorkAtTree(centre) || WorkAtBuilding(s, BuildingKind.Woodcutter, centre, "AxeStone");
                case Job.Stonecutter:
                    return WorkAtBuilding(s, BuildingKind.Stonecutter, centre, "PickaxeAntler") || WorkAtTree(centre);
                default:
                    return WorkAtBuilding(s, BuildingKind.Farm, centre, "Cultivator") || WorkAtBuilding(s, BuildingKind.HunterLodge, centre, "AxeStone") || WorkAtTree(centre);
            }
        }

        /// <summary>A tree near the settlement that does not already have its share of woodcutters.</summary>
        private TreeBase FreeTree(Vector3 centre, float radius)
        {
            int limit = ModConfig.WorkersPerTree.Value;
            TreeBase best = null;
            float bestDist = float.MaxValue;
            HashSet<TreeBase> seen = new HashSet<TreeBase>();
            foreach (Collider collider in Physics.OverlapSphere(centre, radius))
            {
                TreeBase tree = collider.GetComponentInParent<TreeBase>();
                if (tree == null || !seen.Add(tree)) continue;
                int taken;
                Claims.TryGetValue(ClaimKey(tree.transform.position), out taken);
                if (taken >= limit) continue;
                // Prefer near trees, with some spread so the crew fans out.
                float d = Utils.DistanceXZ(tree.transform.position, transform.position) + Random.Range(0f, 12f);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = tree;
                }
            }
            return best != null && TryClaim(best.transform.position, limit) ? best : null;
        }

        private bool WorkAtBuilding(Settlement s, BuildingKind kind, Vector3 centre, string tool)
        {
            foreach (Building b in s.Buildings)
            {
                if (b.Kind != kind || b.State != BuildingState.Complete || b.Plot < 0 || float.IsNaN(b.GroundY)) continue;
                Vector3 site = PlotPosition(s, b.Plot);
                // A yard has room for two and a field for three, each at a place of their own a good
                // step apart. Whoever finds them all taken looks for other work instead of crowding in.
                Vector3 front = (centre - site).normalized;
                Vector3 along = new Vector3(-front.z, 0f, front.x);
                int places = kind == BuildingKind.Farm ? 3 : 2;
                for (int n = 0; n < places; n++)
                {
                    int place = (n + Mathf.Abs(Seed)) % places;
                    Vector3 stand = kind == BuildingKind.Farm
                        ? site + along * ((place - 1) * 2.2f) + front * (place == 1 ? -1.2f : 0.8f)
                        : site + front * 3.2f + along * (place == 0 ? -1.7f : 1.7f);
                    if (!TryClaim(stand, 1)) continue;
                    Vector3 look = kind == BuildingKind.Farm ? stand + new Vector3(Mathf.Cos(Seed), 0f, Mathf.Sin(Seed)) : site;
                    if (SetSpot(look, stand, tool)) return true;
                }
            }
            return false;
        }

        /// <summary>Any pair of hands can fell a tree: the work for whoever found their own trade full.</summary>
        private bool WorkAtTree(Vector3 centre)
        {
            TreeBase tree = FreeTree(centre, 55f);
            return tree != null && SetSpot(tree.transform.position, StandBeside(tree.transform.position, centre, 1.7f), "AxeStone");
        }

        /// <summary>What this inhabitant would talk about right now: the key of a line in Texts.</summary>
        public string BarkKey()
        {
            if (Role == NpcRole.Trader) return "bark_trader";
            if (Role == NpcRole.Escort) return "bark_escort";
            if (Role == NpcRole.Mercenary) return "bark_merc";
            if (Role == NpcRole.Chief) return "bark_chief";
            if (_onTower) return "bark_tower";
            if (Role == NpcRole.Guard) return "bark_guard";
            if (_hauling) return "bark_haul";
            if (_errand == "visiting") return "bark_visit";
            if (_errand != null) return "bark_road";
            if (_working)
            {
                if (_tool != null && _tool.m_dropPrefab != null && _tool.m_dropPrefab.name == "Hammer") return "bark_build";
                if (_tool != null && _tool.m_dropPrefab != null && _tool.m_dropPrefab.name == "AxeStone") return "bark_wood";
                return Job == Job.Stonecutter ? "bark_stone" : "bark_farm";
            }
            return "bark_idle";
        }

        private static Vector3 PlotPosition(Settlement s, int plot)
        {
            Vec2 offset;
            float yaw;
            Defs.PlotTransform(plot, s.Seed, out offset, out yaw);
            return new Vector3(s.Pos.X + offset.X, 0f, s.Pos.Z + offset.Z);
        }

        private static Vector3 StandBeside(Vector3 target, Vector3 from, float distance)
        {
            Vector3 dir = from - target;
            dir.y = 0f;
            return target + dir.normalized * distance;
        }

        private bool SetSpot(Vector3 target, Vector3 stand, string toolName)
        {
            float ground;
            if (!ZoneSystem.instance.IsZoneLoaded(stand) || !ZoneSystem.instance.GetGroundHeight(stand, out ground) || ground < ZoneSystem.c_WaterLevel + 0.3f)
            {
                ReleaseClaim();
                return false;
            }
            stand.y = ground;
            _workSpot = stand;
            _workTarget = new Vector3(target.x, ground + 1f, target.z);
            _tool = FindOrGiveTool(toolName);
            return true;
        }

        private ItemDrop.ItemData FindOrGiveTool(string prefabName)
        {
            Inventory inventory = _humanoid.GetInventory();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName) return item;
            }
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null || !inventory.AddItem(prefab, 1)) return null;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefabName) return item;
            }
            return null;
        }

        /// <summary>
        /// Plays the tool's swing without starting a real attack, so nothing is damaged: no felled
        /// trees, no hurt neighbours and, above all, no pickaxe digging into the ground.
        /// </summary>
        private void Swing()
        {
            Vector3 dir = _workTarget - transform.position;
            dir.y = 0f;
            // Turn to the work over a moment rather than in one frame, which read as a twitch.
            if (dir.sqrMagnitude > 0.01f) TurnTo(dir);
            _ai.StopMoving();
            if (_tool == null || _humanoid.InAttack()) return;
            if (_humanoid.GetCurrentWeapon() != _tool && !_humanoid.EquipItem(_tool, false)) return;
            Attack attack = _tool.m_shared.m_attack;
            if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) return;
            ZSyncAnimation anim = GetComponent<ZSyncAnimation>();
            if (anim != null) anim.SetTrigger(attack.m_attackChainLevels > 1 ? attack.m_attackAnimation + "0" : attack.m_attackAnimation);
        }

        // ------------------------------------------------------------------ night

        private static bool IsHousing(BuildingKind kind)
        {
            return kind == BuildingKind.Hut || kind == BuildingKind.House || kind == BuildingKind.Longhouse || kind == BuildingKind.GreatHall;
        }

        /// <summary>The house this inhabitant sleeps in: where to stand inside and where its door lets out.</summary>
        private bool FindBed(Settlement s, out Vector3 inside, out Vector3 door)
        {
            inside = door = Vector3.zero;
            List<Building> homes = new List<Building>();
            foreach (Building b in s.Buildings)
            {
                if (IsHousing(b.Kind) && b.State == BuildingState.Complete && b.Plot >= 0 && !float.IsNaN(b.GroundY) && b.SpawnedPieces > 0) homes.Add(b);
            }
            if (homes.Count == 0) return false;
            // The chief takes the grandest house.
            Building home = homes[Mathf.Abs(Seed) % homes.Count];
            if (Role == NpcRole.Chief)
            {
                foreach (Building b in homes)
                {
                    if (Defs.Buildings[b.Kind].Housing > Defs.Buildings[home.Kind].Housing) home = b;
                }
            }

            DoorPoints(s, home, ((Seed % 7) - 3) * 0.12f, out inside, out door);
            return true;
        }

        /// <summary>For a house: a standing spot just inside its door and one just outside it.</summary>
        public static void DoorPoints(Settlement s, Building home, float jitter, out Vector3 inside, out Vector3 door)
        {
            Vec2 offset;
            float yaw;
            Defs.PlotTransform(home.Plot, s.Seed, out offset, out yaw);
            Vector3 origin = new Vector3(s.Pos.X + offset.X, home.GroundY, s.Pos.Z + offset.Z);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            float doorX, halfDepth;
            switch (home.Kind)
            {
                case BuildingKind.Hut:
                    doorX = ((home.Id + s.Seed) & 1) == 0 ? -1f : 1f;
                    halfDepth = 2f;
                    break;
                case BuildingKind.House:
                    doorX = 0f;
                    halfDepth = 2f;
                    break;
                default:
                    doorX = 1f;
                    halfDepth = 3f;
                    break;
            }
            inside = origin + rot * new Vector3(doorX + jitter, 0.5f, halfDepth - 1.4f + jitter);
            door = origin + rot * new Vector3(doorX, 0.2f, halfDepth + 1.6f);
        }

        public static bool IsHome(BuildingKind kind) { return IsHousing(kind); }

        private void GoToBed(Settlement s, Vector3 centre)
        {
            if (Asleep) return;
            Vector3 inside, door;
            if (!FindBed(s, out inside, out door))
            {
                // No roof yet: the night is spent by the fire.
                if (Time.time >= _phaseUntil)
                {
                    _phaseUntil = Time.time + 60f;
                    float a = (Seed % 360) * Mathf.Deg2Rad;
                    SetPatrol(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.2f, 1.5f);
                }
                return;
            }
            if (_bedtime < 0f)
            {
                _bedtime = Time.time;
                SetPatrol(door, 0.8f);
            }
            // At the door they step inside; whoever is held up for a minute is let in anyway.
            if (Utils.DistanceXZ(transform.position, door) > 2.5f && Time.time - _bedtime < 60f) return;
            Relocate(inside);
            SetPatrol(inside, 0.3f);
            _view.GetZDO().Set(HashAsleep, true);
        }

        private void WakeUp(Settlement s)
        {
            _view.GetZDO().Set(HashAsleep, false);
            Vector3 inside, door;
            if (FindBed(s, out inside, out door)) Relocate(door);
            SetPatrol(Home, 6f);
        }

        private void Relocate(Vector3 position)
        {
            _ai.StopMoving();
            transform.position = position;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
            }
        }

        // ------------------------------------------------------------------ errands

        private bool RunErrand(Settlement s, Vector3 centre)
        {
            int roll = Random.Range(0, 10);
            if (roll < 4)
            {
                List<Building> places = new List<Building>();
                foreach (Building b in s.Buildings)
                {
                    if (b.State == BuildingState.Complete && b.Plot >= 0 && !float.IsNaN(b.GroundY) && b.Kind != BuildingKind.Farm) places.Add(b);
                }
                if (places.Count == 0) return false;
                Building place = places[Random.Range(0, places.Count)];
                Vector3 site = PlotPosition(s, place.Plot);
                Vector3 stand = site + (centre - site).normalized * (Blueprints.Radius(place.Kind) + 1.2f);
                stand.y = transform.position.y;
                _errand = "visiting";
                _phaseUntil = Time.time + Random.Range(35f, 55f);
                SetPatrol(stand, 1.5f);
                Roads.WornTrail(s.Id, new Vector3(Home.x, 0f, Home.z), new Vector3(stand.x, 0f, stand.z));
                return true;
            }
            if (roll < 6)
            {
                // Out of the settlement and back: far enough to be seen going somewhere.
                Vector3 far = Roads.Along(s, Random.Range(50f, 85f));
                float ground;
                if (!ZoneSystem.instance.IsZoneLoaded(far) || !ZoneSystem.instance.GetGroundHeight(far, out ground) || ground < ZoneSystem.c_WaterLevel + 0.3f) return false;
                _errand = "on-the-road";
                _phaseUntil = Time.time + Random.Range(70f, 100f);
                SetPatrol(far, 2f);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ caravans and hired fighters

        /// <summary>Sets a trader off: `at` metres out along the settlement's road, heading in or out.</summary>
        public void StartJourney(float at, bool arriving)
        {
            _journeyAt = at;
            _journeyIn = arriving;
            ZDO zdo = _view.GetZDO();
            zdo.Set(HashJourneyAt, at);
            zdo.Set(HashJourneyIn, arriving);
        }

        private void OnTheMove(Settlement s)
        {
            Player player = Player.m_localPlayer;
            if (Role == NpcRole.Mercenary)
            {
                if (player == null) return;
                if (_ai.GetFollowTarget() != player.gameObject) _ai.SetFollowTarget(player.gameObject);
                // Left behind by a portal or a long run: they catch up.
                if (!player.IsTeleporting() && Vector3.Distance(player.transform.position, transform.position) > 45f)
                {
                    Vector3 near = player.transform.position - player.transform.forward * 2.5f;
                    float ground;
                    if (ZoneSystem.instance.GetGroundHeight(near, out ground)) near.y = ground + 0.3f;
                    Relocate(near);
                }
                return;
            }
            if (Role == NpcRole.Escort)
            {
                // Stay with the trader they were sent out with.
                FactionMember trader = null;
                float best = 60f;
                foreach (FactionMember m in All)
                {
                    if (m.Role != NpcRole.Trader || m.SettlementId != SettlementId) continue;
                    float d = Vector3.Distance(m.transform.position, transform.position);
                    if (d < best)
                    {
                        best = d;
                        trader = m;
                    }
                }
                if (trader != null && _ai.GetFollowTarget() != trader.gameObject) _ai.SetFollowTarget(trader.gameObject);
                // Without a trader there is nothing to guard: home, and gone.
                if (trader == null && _ai.GetFollowTarget() == null) Expire(0);
                return;
            }

            // The trader walks the road a stretch at a time.
            if (_cargo == null) PickUpCargo();
            if (_journeyAt <= 0f)
            {
                ZDO zdo = _view.GetZDO();
                _journeyAt = zdo.GetFloat(HashJourneyAt, 12f);
                _journeyIn = zdo.GetBool(HashJourneyIn);
            }
            if (_ai.IsAlerted() || _ai.GetTargetCreature() != null || _talking) return;
            Vector3 target = Roads.Along(s, _journeyAt);
            if (Time.time >= _journeyNext || Utils.DistanceXZ(transform.position, target) < 5f)
            {
                _journeyNext = Time.time + 25f;
                if (Utils.DistanceXZ(transform.position, target) < 5f) _journeyAt += _journeyIn ? -14f : 14f;
                bool arrived = _journeyIn ? _journeyAt <= 10f : _journeyAt >= 190f;
                bool unseen = player == null || Vector3.Distance(player.transform.position, transform.position) > 140f;
                if (arrived || unseen)
                {
                    // Journey's end (or out of sight): the goods are delivered and the party breaks up.
                    foreach (FactionMember m in All)
                    {
                        if (m.Role == NpcRole.Escort && m.SettlementId == SettlementId) m.Expire(_journeyIn ? 12 : 0);
                    }
                    Expire(_journeyIn ? 12 : 0);
                    SetPatrol(new Vector3(s.Pos.X, transform.position.y, s.Pos.Z), 3f);
                    return;
                }
                _view.GetZDO().Set(HashJourneyAt, _journeyAt);
                SetPatrol(Roads.Along(s, _journeyAt), 1f);
            }
        }

        // ------------------------------------------------------------------ the tower

        private bool FindTower(Settlement s, out Vector3 deck, out Vector3 foot)
        {
            deck = foot = Vector3.zero;
            foreach (Building b in s.Buildings)
            {
                if (b.Kind != BuildingKind.Watchtower || b.State != BuildingState.Complete || b.Plot < 0 || float.IsNaN(b.GroundY) || b.SpawnedPieces == 0) continue;
                Vec2 offset;
                float yaw;
                Defs.PlotTransform(b.Plot, s.Seed, out offset, out yaw);
                Vector3 origin = new Vector3(s.Pos.X + offset.X, b.GroundY, s.Pos.Z + offset.Z);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                deck = origin + rot * new Vector3(0f, 4.6f, -0.4f);
                foot = origin + rot * new Vector3(0f, 0.3f, 6.5f);
                return true;
            }
            return false;
        }

        private void LeaveTower(Settlement s)
        {
            Vector3 deck, foot;
            if (FindTower(s, out deck, out foot)) Relocate(foot);
            _onTower = false;
            ReleaseClaim();
        }

        /// <summary>One guard keeps the tower, day and night, as long as another is left to walk the rounds.</summary>
        private bool KeepTower(Settlement s)
        {
            Vector3 deck, foot;
            if (!ModConfig.TowerGuards.Value || !FindTower(s, out deck, out foot))
            {
                _onTower = false;
                return false;
            }
            if (_onTower)
            {
                // Still up there? (Knocked off, or the tower came down.)
                if (Utils.DistanceXZ(transform.position, deck) > 3f || transform.position.y < deck.y - 1.8f)
                {
                    _onTower = false;
                    ReleaseClaim();
                    return false;
                }
                if (Time.time >= _phaseUntil)
                {
                    // Look out over a different quarter now and then.
                    _phaseUntil = Time.time + Random.Range(12f, 25f);
                    TurnTo(Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward);
                }
                return true;
            }
            int guards = 0;
            foreach (FactionMember m in All)
            {
                if (m.Role == NpcRole.Guard && m.SettlementId == SettlementId) guards++;
            }
            if (guards < 2) return false;
            long key = ClaimKey(deck);
            if (!_hasClaim || _claim != key)
            {
                if (!TryClaim(deck, 1))
                {
                    // The post is taken. Whoever is still up there from an earlier watch comes down to walk the rounds.
                    if (Utils.DistanceXZ(transform.position, deck) < 3.5f && transform.position.y > deck.y - 1.8f) Relocate(foot);
                    return false;
                }
                _towerSince = Time.time;
                SetPatrol(foot, 1f);
            }
            // At the ladder they go up; whoever is held up on the way is let up anyway.
            if (Utils.DistanceXZ(transform.position, foot) > 2.5f && Time.time - _towerSince < 45f) return true;
            Relocate(deck);
            SetPatrol(deck, 0.3f);
            _onTower = true;
            _patrolLeg = -1;
            return true;
        }

        private void GuardDuty(Settlement s, Vector3 centre, bool night)
        {
            if (KeepTower(s)) return;
            if (!night)
            {
                if (_patrolLeg != -1)
                {
                    _patrolLeg = -1;
                    SetPatrol(Home, 14f);
                }
                return;
            }

            // Walk the edge of the settlement, leg by leg.
            float ring = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 7f;
            if (_patrolLeg < 0 || Time.time >= _phaseUntil)
            {
                _patrolLeg = _patrolLeg < 0 ? Mathf.Abs(Seed) % 8 : (_patrolLeg + 1) % 8;
                _phaseUntil = Time.time + 35f;
                float a = _patrolLeg * Mathf.PI / 4f;
                SetPatrol(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring, 3f);
            }

            Player player = Player.m_localPlayer;
            if (player == null || Time.time < _nextBark || !ModConfig.NightWatchOn.Value) return;
            Faction faction = SimHost.Sim.State.GetFaction(FactionId);
            if (faction == null) return;
            // Only a guard who can actually see the stranger raises the challenge.
            if (Vector3.Distance(player.transform.position, transform.position) > 45f) return;
            string line = NightWatch.Challenge(s, faction, player, Utils.DistanceXZ(player.transform.position, centre));
            if (line == null) return;
            _nextBark = Time.time + 4f;
            Plugin.Log.LogInfo("[night watch] " + s.Name + ": " + DisplayName + " -> " + line);
            if (line == "night_halt")
            {
                // The guard leaves the round and walks up to the stranger.
                _phaseUntil = Time.time + 20f;
                SetPatrol(StandBeside(player.transform.position, transform.position, 3f), 1.5f);
            }
            if (Chat.instance != null) Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 30f, 6f, "", Texts.Get(line), false);
            if (line == "night_attack" && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("night_alarm", Texts.Name(s.Name)));
        }
    }

    /// <summary>Inhabitants never damage their own settlement, whatever they swing at.</summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class OwnPropertyPatch
    {
        private static bool Prefix(ZNetView ___m_nview, HitData hit)
        {
            if (hit == null || ___m_nview == null || !___m_nview.IsValid()) return true;
            FactionMember member = FactionMember.Of(hit.GetAttacker());
            return member == null || ___m_nview.GetZDO().GetInt(Materializer.HashSettlement) != member.SettlementId;
        }
    }
}
