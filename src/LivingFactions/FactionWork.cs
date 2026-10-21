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
            public Vector3 AlarmAt;
        }

        private static readonly Dictionary<int, Watch> Watches = new Dictionary<int, Watch>();

        public static void Reset() { Watches.Clear(); }

        public static bool Angry(int settlementId)
        {
            Watch w;
            return Watches.TryGetValue(settlementId, out w) && Time.time < w.AngryUntil;
        }

        /// <summary>A thief was caught: the watch is out for the rest of the night's trouble, no more warnings.</summary>
        public static void Raise(int settlementId, Vector3 where)
        {
            Watch w;
            if (!Watches.TryGetValue(settlementId, out w))
            {
                w = new Watch();
                Watches[settlementId] = w;
            }
            w.Night = (int)(SimHost.WorldDay + 0.5);
            w.Resolved = true;
            w.AngryUntil = Time.time + 180f;
            w.AlarmAt = where;
        }

        /// <summary>The settlement is up after a thief, and where the thief was seen.</summary>
        public static bool Alarmed(int settlementId, out Vector3 where)
        {
            Watch w;
            where = Vector3.zero;
            if (!Watches.TryGetValue(settlementId, out w) || Time.time >= w.AngryUntil || w.AlarmAt == Vector3.zero) return false;
            where = w.AlarmAt;
            return true;
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
            if (Time.time - w.WarnedAt < 15f) return null;

            w.Resolved = true;
            if (Random.value < ModConfig.NightAttackChance.Value)
            {
                // Its guards are hostile to strangers for a while (FactionMember.IsHostileTo).
                w.AngryUntil = Time.time + 90f;
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
        /// <summary>The tree being felled, and the swings it has taken so far.</summary>
        private TreeBase _tree;
        private int _treeSwings;
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
        private Vector3 _errandAt;
        /// <summary>Where the night watch is heading on its round.</summary>
        private Vector3 _legAt;
        private bool _onTower;
        private float _towerSince;
        private float _journeyAt;
        private bool _journeyIn;
        private float _journeyNext;

        private static readonly int HashJourneyAt = "lf_jat".GetStableHashCode();
        private static readonly int HashJourneyIn = "lf_jin".GetStableHashCode();

        /// <summary>
        /// The trade this one keeps to. Not one in three each regardless: the settlement puts more hands
        /// where it is short (wood, stone or food) and where it has the workshop for it, and people change
        /// trade as its needs change. Each keeps to a place of their own in the line-up, so only a few switch at once.
        /// </summary>
        public Job Job
        {
            get
            {
                Settlement s = SimHost.Active ? SimHost.Sim.State.GetSettlement(SettlementId) : null;
                if (s == null) return (Job)(Mathf.Abs(Seed) % 3);
                Res need = SimHost.Sim.NeededResource(s);
                int wood = 3 + (need == Res.Wood ? 3 : 0) + (Has(s, BuildingKind.Woodcutter) ? 1 : 0);
                int stone = 1 + (need == Res.Stone ? 3 : 0) + (Has(s, BuildingKind.Stonecutter) ? 2 : 0);
                int food = 3 + (need == Res.Food ? 3 : 0) + (Has(s, BuildingKind.Farm) ? 2 : 0) + (Has(s, BuildingKind.HunterLodge) ? 1 : 0);
                int pick = Mathf.Abs(Seed / 3) % (wood + stone + food);
                return pick < wood ? Job.Woodcutter : pick < wood + stone ? Job.Stonecutter : Job.Farmer;
            }
        }

        private static bool Has(Settlement s, BuildingKind kind)
        {
            foreach (Building b in s.Buildings) if (b.Kind == kind && b.State == BuildingState.Complete) return true;
            return false;
        }

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
            return DisplayName + " [" + Role + (Role == NpcRole.Villager ? "/" + Job : "") + "] " + state + (_cargo != null ? " carrying" : "") + (busy ? " ALERT" + (_ai.GetTargetCreature() != null ? "(" + Utils.GetPrefabName(_ai.GetTargetCreature().gameObject) + " " + Utils.DistanceXZ(_ai.GetTargetCreature().transform.position, transform.position).ToString("0") + "m)" : "") : "") + (Player.m_localPlayer != null && Grudges.AngryAt(this, Player.m_localPlayer) ? " ANGRY" : "")
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

        /// <summary>
        /// This one has just turned on an attacker: the faction's people near, and allies nearer, drop
        /// what they are doing and go for the same enemy. Villagers too, with whatever they hold.
        /// </summary>
        /// <summary>
        /// Walk the way the band goes, a leg at a time; mill about at the far end. Gone when the band is
        /// over, when it has moved on out of sight in the simulation, or when no player is near.
        /// </summary>
        private void MarchWithBand(Player player)
        {
            Squad band = SquadPresence.Find(SquadId);
            Vector3 dest = transform.position;
            bool arrived = false;
            Vector3 bandAt = band != null ? new Vector3(band.Pos.X, transform.position.y, band.Pos.Z) : transform.position;
            bool over = band == null || band.State == SquadState.Done || !SquadPresence.Destination(band, out dest, out arrived);
            // Left behind by the band in the simulation, or the band is over: gone, but only where nobody
            // walking with them sees it. Followed, they keep on along the way.
            if ((over || Utils.DistanceXZ(transform.position, bandAt) > 180f) && !Watched(55f)
                || player == null || Utils.DistanceXZ(player.transform.position, transform.position) > SquadPresence.KeepDistance)
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            if (over)
            {
                SetPatrol(transform.position, 6f);
                return;
            }
            dest.y = transform.position.y;
            Vector3 way = dest - transform.position;
            if (arrived || way.magnitude < 20f) SetPatrol(dest, 12f);
            else SetPatrol(transform.position + way.normalized * 25f, 2f);
        }

        public void Alarm(Character enemy)
        {
            if (enemy == null) return;
            foreach (FactionMember m in All)
            {
                if (m._ai == null || m._view == null || !m._view.IsValid() || !m._view.IsOwner() || m.Humanoid == null || m.Humanoid.IsDead()) continue;
                if (!BaseAI.IsEnemy(m.Humanoid, enemy)) continue;
                float range = m.FactionId == FactionId ? ModConfig.PursuitRange.Value : ModConfig.HelpRange.Value;
                if (Vector3.Distance(m.transform.position, transform.position) > range) continue;
                m._ai.Alert();
                if (m._ai.GetTargetCreature() == null) Traverse.Create(m._ai).Field("m_targetCreature").SetValue(enemy);
            }
            if (Chat.instance != null) Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 20f, 4f, "", Texts.Vary("bark_to_arms", Seed), false);
        }

        private float _lastProtest = -99f;

        /// <summary>A blow the faction still forgives: the one struck says so.</summary>
        public void Protest()
        {
            if (Time.time - _lastProtest < 2.5f || Chat.instance == null) return;
            _lastProtest = Time.time;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 15f, 3.5f, "", Texts.Vary("bark_hit", Seed + (int)Time.time), false);
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
            CloseLid();
            _deliverTo = null;
            _tree = null;
            _treeSwings = 0;
            _working = false;
            _hauling = false;
            _errand = null;
            DropCargo();
            ReleaseClaim();
        }

        private Container _deliverTo, _lidOpen;
        private float _unloadUntil;
        private string _loadPrefab;
        private Res _loadRes;
        private int _loadCount, _workedAt;

        /// <summary>
        /// What the load on the back is, from the work it came from: logs from a tree or the woodcutter's
        /// yard, stone from the quarry, the field's crop, meat from the hunters' lodge.
        /// </summary>
        private void ChooseLoad(Settlement s)
        {
            string tool = _tool != null && _tool.m_dropPrefab != null ? _tool.m_dropPrefab.name : "";
            Building at = _workedAt != 0 ? s.Buildings.Find(x => x.Id == _workedAt) : null;
            _loadPrefab = null;
            if (tool == "PickaxeAntler")
            {
                _loadPrefab = "Stone";
                _loadRes = Res.Stone;
                _loadCount = 2 + Mathf.Abs(Seed) % 3;
            }
            else if (tool == "Cultivator")
            {
                _loadPrefab = s.Biome == Biome.Plains ? "Barley" : ((Mathf.Abs(Seed) + (at != null ? at.Id : 0)) & 1) == 0 ? "Carrot" : "Turnip";
                _loadRes = Res.Food;
                _loadCount = 3 + Mathf.Abs(Seed) % 3;
            }
            else if (at != null && at.Kind == BuildingKind.HunterLodge)
            {
                _loadPrefab = (Mathf.Abs(Seed) & 1) == 0 ? "RawMeat" : "DeerMeat";
                _loadRes = Res.Food;
                _loadCount = 1 + Mathf.Abs(Seed) % 2;
            }
            else if (tool == "AxeStone")
            {
                _loadPrefab = "Wood";
                _loadRes = Res.Wood;
                _loadCount = 3 + Mathf.Abs(Seed) % 4;
            }
        }

        /// <summary>
        /// At the chest: the load goes into it for real (Chests.Deliver), with the lid up for a moment as
        /// when a player opens it. False when nothing went in (a player has it open, or it shows all it may):
        /// the load is then put down as before, booked by the simulation all the same.
        /// </summary>
        private bool Unload(Settlement s)
        {
            if (_deliverTo == null) return false;
            Vector3 toChest = _deliverTo.transform.position - transform.position;
            toChest.y = 0f;
            int put = Chests.Deliver(s, _deliverTo, _loadRes, _loadPrefab, _loadCount);
            if (put <= 0)
            {
                if (_unloads++ < 12) Plugin.Log.LogInfo(s.Name + ": a " + Job + " could not put " + _loadCount + " " + _loadPrefab + " into the chest (" + (_deliverTo.IsInUse() ? "in use" : "full, or showing all the stock it may") + ")");
                return false;
            }
            _ai.StopMoving();
            SetPatrol(transform.position, 0f);
            if (toChest.sqrMagnitude > 0.01f) TurnTo(toChest);
            DropCargo();
            _deliverTo.SetInUse(true);
            _lidOpen = _deliverTo;
            _unloadUntil = Time.time + 1.4f;
            if (_unloads++ < 12) Plugin.Log.LogInfo(s.Name + ": a " + Job + " put " + put + " " + _loadPrefab + " into the chest of building " + _lidOpen.GetComponent<ZNetView>().GetZDO().GetInt(Materializer.HashBuilding));
            _delivered++;
            return true;
        }

        private static int _unloads, _delivered;

        /// <summary>For `wares`/`mat`-style dev output: loads put into chests this session.</summary>
        internal static int Delivered { get { return _delivered; } }

        private void CloseLid()
        {
            if (_lidOpen != null)
            {
                ZNetView view = _lidOpen.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.IsOwner()) _lidOpen.SetInUse(false);
            }
            _lidOpen = null;
            _unloadUntil = 0f;
        }

        private void DropCargo()
        {
            if (_cargo != null) Destroy(_cargo);
            _cargo = null;
        }

        /// <summary>
        /// Puts what was just gathered on the worker's back: a visual copy of a load, carried to the
        /// storehouse. It is decoration only - the simulation books the goods. A woodcutter shoulders a log
        /// across the back, a stonecutter a stone, a farmer (and a trader) a small crate of the harvest: the
        /// item Turnip is a whole plant with its leaves, which on the back looked like a bush in the air.
        /// The load hangs from the joint a shield is slung on (VisEquipment, the middle of the back, so it
        /// follows the body as it moves), flat against the back: its thinnest side front to back, its
        /// longest across the shoulders.
        /// </summary>
        private void PickUpCargo()
        {
            DropCargo();
            bool log = _loadPrefab == "Wood" || (_loadPrefab == null && Job == Job.Woodcutter), stone = _loadPrefab == "Stone" || (_loadPrefab == null && Job == Job.Stonecutter);
            string itemName = log ? "RoundLog" : stone ? "Stone" : "CargoCrate";
            // The load's width across the back, in metres.
            float width = log ? 1.3f : stone ? 0.6f : 0.5f;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(itemName) : null;
            if (prefab == null && ObjectDB.instance != null) prefab = ObjectDB.instance.GetItemPrefab(itemName);
            VisEquipment vis = GetComponent<VisEquipment>();
            Transform joint = vis != null && vis.m_backShield != null ? vis.m_backShield : Utils.FindChild(transform, "Spine2");
            if (prefab == null || joint == null) return;

            // What the item looks like up close: the first level of detail, or every mesh when it has none.
            List<MeshFilter> parts = new List<MeshFilter>();
            LODGroup lods = prefab.GetComponentInChildren<LODGroup>(true);
            if (lods != null && lods.GetLODs().Length > 0)
            {
                foreach (Renderer r in lods.GetLODs()[0].renderers)
                {
                    MeshFilter mf = r != null ? r.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null && r is MeshRenderer) parts.Add(mf);
                }
            }
            if (parts.Count == 0)
            {
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null) parts.Add(mf);
                }
            }
            if (parts.Count == 0) return;

            _cargo = new GameObject("lf_cargo");
            Transform model = new GameObject("model").transform;
            model.SetParent(_cargo.transform, false);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            Bounds? all = null;
            foreach (MeshFilter mf in parts)
            {
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                GameObject piece = new GameObject("part");
                piece.transform.SetParent(model, false);
                piece.transform.localPosition = m.MultiplyPoint3x4(Vector3.zero);
                piece.transform.localRotation = m.rotation;
                piece.transform.localScale = m.lossyScale;
                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
                Bounds mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (all.HasValue) { Bounds b = all.Value; b.Encapsulate(corner); all = b; }
                    else all = new Bounds(corner, Vector3.zero);
                }
            }
            Bounds box = all.Value;
            Vector3 e = box.extents;
            // The model's own axes sorted: longest across the back, thinnest from back to front.
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] size = { e.x, e.y, e.z };
            int longest = 0, thinnest = 0;
            for (int i = 1; i < 3; i++)
            {
                if (size[i] > size[longest]) longest = i;
                if (size[i] < size[thinnest]) thinnest = i;
            }
            if (longest == thinnest) thinnest = (longest + 1) % 3;
            int middle = 3 - longest - thinnest;
            float scale = width / Mathf.Max(0.01f, 2f * size[longest]);
            // Centred on the model's middle, scaled, then turned so that its thinnest axis points forward and
            // its middle one up.
            model.localPosition = -box.center;
            _cargo.transform.SetParent(joint, false);
            _cargo.transform.localScale = Vector3.one * scale / Mathf.Max(0.0001f, joint.lossyScale.x);
            Quaternion turn = transform.rotation * Quaternion.Inverse(Quaternion.LookRotation(axes[thinnest], axes[middle]));
            _cargo.transform.rotation = turn;
            // Its front against the back: the joint is 0.31 m behind the middle of the body, the back itself
            // about 0.2 m. A log rides higher, across the shoulder blades.
            float depth = size[thinnest] * scale;
            Vector3 at = joint.position + transform.forward * (0.11f - depth) + transform.up * (log ? 0.12f : -0.05f);
            _cargo.transform.position = at;
        }

        private Character _chased;
        private Vector3 _chaseFrom;

        /// <summary>
        /// Nobody wades out after a Neck, and people on the road (caravan guards, traders, a band on the
        /// march) give up a chase that takes them far off their way, instead of being drawn on and on
        /// from one creature to the next.
        /// </summary>
        private void Leash()
        {
            Character target = _ai.GetTargetCreature();
            if (target == null)
            {
                _chased = null;
                return;
            }
            if (target != _chased)
            {
                // The first chase sets the place; one that follows straight on from it does not.
                if (_chased == null) _chaseFrom = transform.position;
                _chased = target;
            }
            bool onTheRoad = Role == NpcRole.Escort || Role == NpcRole.Trader || Role == NpcRole.Squad;
            bool inWater = !target.IsPlayer() && (target.IsSwimming() || target.transform.position.y < ZoneSystem.c_WaterLevel - 0.5f);
            bool tooFar = onTheRoad && Utils.DistanceXZ(transform.position, _chaseFrom) > 30f;
            if (!inWater && !tooFar) return;
            Traverse.Create(_ai).Field("m_targetCreature").SetValue(null);
            Traverse.Create(_ai).Method("SetAlerted", false).GetValue();
            _chased = null;
            if (onTheRoad) SetPatrol(_chaseFrom, 3f);
        }

        /// <summary>
        /// Where gathered goods are taken: in front of the chest they go into (the storehouse's, or the
        /// workshop's where there is none), else the storehouse, else the fire.
        /// </summary>
        private Vector3 DropOff(Settlement s, Vector3 centre)
        {
            _deliverTo = _loadPrefab != null ? Chests.DeliveryChest(s, _workedAt, _loadRes) : null;
            if (_deliverTo != null)
            {
                // On the side of the chest toward the middle of its building: chests stand against a wall.
                Vector3 at = _deliverTo.transform.position;
                ZNetView view = _deliverTo.GetComponent<ZNetView>();
                Building owner = view != null && view.IsValid() ? s.Buildings.Find(x => x.Id == view.GetZDO().GetInt(Materializer.HashBuilding)) : null;
                Vector3 inward = owner != null && owner.Plot >= 0 ? PlotPosition(s, owner.Plot) - at : _deliverTo.transform.forward;
                inward.y = 0f;
                if (inward.sqrMagnitude < 0.01f) inward = _deliverTo.transform.forward;
                return at + inward.normalized * 1.1f;
            }
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
            Leash();
            if (Npcs.IsTemporary(Role))
            {
                OnTheMove(s);
                return;
            }
            if (s.Destroyed) return;
            if (Role == NpcRole.Envoy)
            {
                AtThePost(s);
                return;
            }
            Vector3 centre = new Vector3(s.Pos.X, transform.position.y, s.Pos.Z);
            bool night = EnvMan.IsNight();
            bool busy = _ai.IsAlerted() || _ai.GetTargetCreature() != null;

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

            Vector3 alarm;
            if (night && NightWatch.Alarmed(SettlementId, out alarm))
            {
                // Nobody goes back to bed while the thief may still be about: they search round where it happened.
                StopWork();
                if (Asleep) WakeUp(s);
                _bedtime = -1f;
                if (Time.time >= _phaseUntil)
                {
                    _phaseUntil = Time.time + 15f;
                    SetPatrol(alarm, 8f);
                }
                return;
            }
            if (night && ModConfig.NightSleep.Value)
            {
                StopWork();
                if (Asleep)
                {
                    // Loaded asleep (or nudged): back into bed.
                    if (_bedAt == Vector3.zero)
                    {
                        Vector3 inside, door;
                        FindBed(s, out inside, out door, out _bedAt, out _bedFacing);
                    }
                    if (Utils.DistanceXZ(transform.position, _bedAt) > 0.3f || _anim == null) LieDown();
                    return;
                }
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
                // Walk the load in and put it into the chest (or down at the storehouse, or give up after a while).
                if (_lidOpen != null)
                {
                    if (Time.time < _unloadUntil) return;
                }
                else
                {
                    float left = Utils.DistanceXZ(transform.position, _workSpot);
                    if (left > (_deliverTo != null ? 1.4f : 2.8f) && Time.time < _phaseUntil) return;
                    if (_deliverTo != null && left <= 2.4f && Unload(s)) return;
                }
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
                    ChooseLoad(s);
                    _humanoid.UnequipItem(_tool, false);
                    PickUpCargo();
                    _workSpot = DropOff(s, centre);
                    if (_unloads < 12) Plugin.Log.LogInfo(s.Name + ": a " + Job + " carries " + (_loadPrefab ?? "nothing named") + (_deliverTo != null ? " to a chest " + Utils.DistanceXZ(transform.position, _workSpot).ToString("0") + " m away" : " to the storehouse front (no chest loaded)"));
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
            if (_errand != null && _ai.m_randomMoveRange > 0.5f && Utils.DistanceXZ(transform.position, _errandAt) < 2.5f)
            {
                // Arrived: they stand and talk instead of wandering round the doorstep in little steps.
                SetPatrol(_errandAt, 0.35f);
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
            if (_tree != null && ++_treeSwings >= Forestry.SwingsToFell)
            {
                // The tree comes down; the woodcutter carries the wood in.
                if (Forestry.Fell(_tree, s)) _phaseUntil = Time.time + 2.5f;
                _tree = null;
            }
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
            Settlement home = SimHost.Sim.State.GetSettlement(SettlementId);
            foreach (Collider collider in Physics.OverlapSphere(centre, radius))
            {
                TreeBase tree = collider.GetComponentInParent<TreeBase>();
                if (tree == null || !seen.Add(tree)) continue;
                int taken;
                Claims.TryGetValue(ClaimKey(tree.transform.position), out taken);
                if (taken >= limit) continue;
                // Prefer near trees, with some spread so the crew fans out.
                float d = Utils.DistanceXZ(tree.transform.position, transform.position) + Random.Range(0f, 12f);
                // Trees that may come down are worth the longer walk: the village keeps its shade, the wood its edge.
                if (home != null && !Forestry.MayFell(tree, home)) d += 40f;
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
                    if (SetSpot(look, stand, tool))
                    {
                        _workedAt = b.Id;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Any pair of hands can fell a tree: the work for whoever found their own trade full.</summary>
        private bool WorkAtTree(Vector3 centre)
        {
            _workedAt = 0;
            TreeBase tree = FreeTree(centre, 55f);
            if (tree == null || !SetSpot(tree.transform.position, StandBeside(tree.transform.position, centre, 1.7f), "AxeStone")) return false;
            Settlement s = SimHost.Sim.State.GetSettlement(SettlementId);
            _tree = s != null && Forestry.MayFell(tree, s) ? tree : null;
            _treeSwings = 0;
            return true;
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
            Defs.PlotTransform(s, plot, out offset, out yaw);
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
            Vector3 lie;
            Quaternion facing;
            return FindBed(s, out inside, out door, out lie, out facing);
        }

        /// <summary>
        /// Where this one sleeps. The chief has the grandest house; the others are shared out over the
        /// homes as many to each as it houses (not seven to one hut), each in a bed of that house while
        /// there are beds, on the floor beside them after that.
        /// </summary>
        private bool FindBed(Settlement s, out Vector3 inside, out Vector3 door, out Vector3 lie, out Quaternion facing)
        {
            inside = door = lie = Vector3.zero;
            facing = Quaternion.identity;
            List<Building> homes = new List<Building>();
            foreach (Building b in s.Buildings)
            {
                if (IsHousing(b.Kind) && b.State == BuildingState.Complete && b.Plot >= 0 && !float.IsNaN(b.GroundY) && b.SpawnedPieces > 0) homes.Add(b);
            }
            if (homes.Count == 0) return false;
            Building grand = homes[0];
            foreach (Building b in homes)
            {
                // The great hall is the chief's, however many it houses.
                if (grand.Kind != BuildingKind.GreatHall && (b.Kind == BuildingKind.GreatHall || Defs.Buildings[b.Kind].Housing > Defs.Buildings[grand.Kind].Housing)) grand = b;
            }
            Building home = grand;
            int slot = 0;
            if (Role != NpcRole.Chief)
            {
                // Everyone of the settlement who sleeps at home, in a fixed order.
                List<FactionMember> sleepers = new List<FactionMember>();
                foreach (FactionMember m in All)
                {
                    if (m.SettlementId == SettlementId && m.Role == NpcRole.Villager) sleepers.Add(m);
                }
                sleepers.Sort((x, y) => x.Seed.CompareTo(y.Seed));
                int index = Mathf.Max(0, sleepers.IndexOf(this));
                home = null;
                foreach (Building b in homes)
                {
                    // The chief's place in the grand house is taken.
                    int room = Mathf.RoundToInt(Defs.Buildings[b.Kind].Housing) - (b == grand ? 1 : 0);
                    if (index < room)
                    {
                        home = b;
                        slot = index + (b == grand ? 1 : 0);
                        break;
                    }
                    index -= room;
                }
                if (home == null)
                {
                    // More people than room: they squeeze in where they can.
                    home = homes[index % homes.Count];
                    slot = 4 + index / homes.Count;
                }
            }
            DoorPoints(s, home, ((Seed % 7) - 3) * 0.12f, out inside, out door);

            Vec2 offset;
            float yaw;
            Defs.PlotTransform(s, home.Plot, out offset, out yaw);
            Vector3 origin = new Vector3(s.Pos.X + offset.X, home.GroundY, s.Pos.Z + offset.Z);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            List<Transform> beds = BedsOf(s, home, origin);
            // A bed sleeps two (Defs: a home houses two for each of its beds), side by side across it.
            int bed = slot / Defs.SleepersPerBed;
            _sleepsIn = home.Kind + " #" + home.Id + " " + (bed < beds.Count ? "bed " + (bed + 1) + " of " + beds.Count : "floor (" + beds.Count + " beds)");
            _bedPiece = null;
            if (bed < beds.Count && BedPose(beds[bed], out lie, out facing))
            {
                lie += beds[bed].right * ((slot % Defs.SleepersPerBed) - 0.5f) * BedShare;
                _bedPiece = beds[bed];
                return true;
            }
            // On the floor, side by side across the house, between its middle and the door.
            int floor = slot - beds.Count * Defs.SleepersPerBed;
            float across = ((floor % 4) - 1.5f) * 0.85f;
            float along = 0.6f - (floor / 4) * 0.9f;
            Vector3 spot = origin + rot * new Vector3(across, 0f, along);
            RaycastHit hit;
            spot.y = Physics.Raycast(spot + Vector3.up * 2.5f, Vector3.down, out hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.point.y : inside.y - 0.3f;
            lie = spot;
            facing = rot * Quaternion.Euler(0f, 90f, 0f);
            return true;
        }

        /// <summary>How far apart two lie in one bed: its solid frame is 1.2 m wide.</summary>
        private const float BedShare = 0.55f;

        private string _sleepsIn = "";

        /// <summary>For the dev check: where this one sleeps tonight ("House #7 bed 2 of 2", "Hut #3 floor (1 beds)").</summary>
        internal string SleepPlace(Settlement s)
        {
            Vector3 inside, door;
            _sleepsIn = "nowhere";
            FindBed(s, out inside, out door);
            return _sleepsIn;
        }

        private static readonly List<Transform> BedScratch = new List<Transform>();
        private static readonly List<ZDO> BedZdos = new List<ZDO>();
        private static readonly Dictionary<long, KeyValuePair<float, List<Transform>>> BedCache = new Dictionary<long, KeyValuePair<float, List<Transform>>>();
        private static readonly int WoodBed = "bed".GetStableHashCode();
        private static readonly int StrawBed = "goblin_bed".GetStableHashCode();
        private static readonly int StrawPile = "goblin_strawpile".GetStableHashCode();

        /// <summary>
        /// The beds the settlement put in a house, in a fixed order: real beds, and the straw beds of the Fulings
        /// and the straw piles of the Greydwarfs, which are no Bed to the game.
        /// </summary>
        private static List<Transform> BedsOf(Settlement s, Building home, Vector3 origin)
        {
            // Everyone going to bed asks every moment; the beds of a house change only when it is rebuilt.
            long key = ((long)s.Id << 32) | (uint)home.Id;
            KeyValuePair<float, List<Transform>> known;
            if (BedCache.TryGetValue(key, out known) && Time.time - known.Key < 5f && known.Value.TrueForAll(t => t != null)) return known.Value;
            BedScratch.Clear();
            // Found by their tags in the object database, not by physics: a heap of straw has no collider.
            BedZdos.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(origin), SimulationDistance.OriginalDistance, BedZdos);
            foreach (ZDO zdo in BedZdos)
            {
                int prefab = zdo.GetPrefab();
                if (prefab != WoodBed && prefab != StrawBed && prefab != StrawPile) continue;
                if (zdo.GetInt(Materializer.HashSettlement) != s.Id || zdo.GetInt(Materializer.HashBuilding) != home.Id) continue;
                ZNetView view = ZNetScene.instance.FindInstance(zdo);
                if (view == null || !view.IsValid() || BedScratch.Contains(view.transform)) continue;
                BedScratch.Add(view.transform);
            }
            BedScratch.Sort((x, y) => (x.position.x * 1.37f + x.position.z).CompareTo(y.position.x * 1.37f + y.position.z));
            List<Transform> beds = new List<Transform>(BedScratch);
            BedCache[key] = new KeyValuePair<float, List<Transform>>(Time.time, beds);
            return beds;
        }

        /// <summary>
        /// Where one lies in a bed. A straw bed is the wooden bed's own shape, so one lies in it just as in the
        /// wooden bed (its spawn point, carried over); on a straw pile, on top of the straw along the pile.
        /// </summary>
        private static bool BedPose(Transform bed, out Vector3 lie, out Quaternion facing)
        {
            Bed real = bed.GetComponent<Bed>();
            Transform point = real != null ? real.m_spawnPoint : null;
            if (point == null)
            {
                GameObject wooden = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("bed") : null;
                Bed model = wooden != null ? wooden.GetComponent<Bed>() : null;
                if (model != null && model.m_spawnPoint != null && Utils.GetPrefabName(bed.gameObject) == "goblin_bed")
                {
                    Matrix4x4 local = wooden.transform.worldToLocalMatrix * model.m_spawnPoint.localToWorldMatrix;
                    lie = bed.TransformPoint(local.MultiplyPoint3x4(Vector3.zero));
                    facing = bed.rotation * local.rotation;
                    return true;
                }
                lie = bed.position + Vector3.up * 0.3f;
                facing = bed.rotation;
                return true;
            }
            lie = point.position;
            facing = point.rotation;
            return true;
        }

        /// <summary>For a house: a standing spot just inside its door and one just outside it.</summary>
        public static void DoorPoints(Settlement s, Building home, float jitter, out Vector3 inside, out Vector3 door)
        {
            Vec2 offset;
            float yaw;
            Defs.PlotTransform(s, home.Plot, out offset, out yaw);
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
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            string group = f != null ? Defs.Culture(f.CultureId).Group : "";
            if ((group == "fuling" || group == "greydwarf") && Blueprints.RoundRadius(home.Kind) > 0f)
            {
                // A round Fuling hut or Greydwarf den has its way in in the middle of the front.
                doorX = 0f;
                halfDepth = Blueprints.RoundRadius(home.Kind);
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
            Vector3 lie;
            Quaternion facing;
            FindBed(s, out inside, out door, out lie, out facing);
            _bedAt = lie;
            _bedFacing = facing;
            _view.GetZDO().Set(HashAsleep, true);
            LieDown();
        }

        private Vector3 _bedAt;
        private Quaternion _bedFacing;
        private ZSyncAnimation _anim;
        private Transform _bedPiece;
        private bool _held;
        private readonly List<KeyValuePair<Collider, Collider>> _ignored = new List<KeyValuePair<Collider, Collider>>();

        /// <summary>
        /// A sleeper lies still in bed, as the game keeps a player there: no gravity, no speed, its body
        /// passing through the bed and whoever shares it, and held to its place every frame. Before, the
        /// body stood in the bed's frame and its neighbour's, was pushed out and put back once a second,
        /// and twitched and slid about with its legs walking.
        /// </summary>
        private void HoldInBed()
        {
            bool asleep = _view != null && _view.IsValid() && _view.IsOwner() && _bedAt != Vector3.zero && Asleep;
            Rigidbody body = _body != null ? _body : (_body = GetComponent<Rigidbody>());
            if (!asleep)
            {
                if (_held) LetGo(body);
                return;
            }
            if (!_held)
            {
                _held = true;
                Collider mine = GetComponent<CapsuleCollider>();
                if (mine != null)
                {
                    if (_bedPiece != null)
                    {
                        foreach (Collider c in _bedPiece.GetComponentsInChildren<Collider>()) Ignore(mine, c);
                    }
                    foreach (FactionMember m in All)
                    {
                        if (m == this || !m.IsAsleep || Vector3.Distance(m.transform.position, _bedAt) > 1.5f) continue;
                        Collider theirs = m.GetComponent<CapsuleCollider>();
                        if (theirs != null) Ignore(mine, theirs);
                    }
                }
            }
            transform.position = _bedAt;
            transform.rotation = _bedFacing;
            if (body != null)
            {
                body.useGravity = false;
                body.position = _bedAt;
                body.rotation = _bedFacing;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void Ignore(Collider a, Collider b)
        {
            if (a == null || b == null || a == b) return;
            Physics.IgnoreCollision(a, b, true);
            _ignored.Add(new KeyValuePair<Collider, Collider>(a, b));
        }

        private void LetGo(Rigidbody body)
        {
            _held = false;
            if (body != null) body.useGravity = true;
            foreach (KeyValuePair<Collider, Collider> pair in _ignored)
            {
                if (pair.Key != null && pair.Value != null) Physics.IgnoreCollision(pair.Key, pair.Value, false);
            }
            _ignored.Clear();
        }

        private Rigidbody _body;

        private void FixedUpdate() { HoldInBed(); }

        private void LateUpdate() { HoldInBed(); }

        /// <summary>Lying in bed (or on the floor), arms empty, still: the game's own lying-in-bed pose.</summary>
        private void LieDown()
        {
            if (_bedAt == Vector3.zero) return;
            Relocate(_bedAt);
            transform.rotation = _bedFacing;
            SetPatrol(_bedAt, 0f);
            if (_anim == null) _anim = GetComponent<ZSyncAnimation>();
            if (_anim != null) _anim.SetBool("attach_bed", true);
            _humanoid.HideHandItems(false, false);
        }

        private void GetUp()
        {
            if (_anim == null) _anim = GetComponent<ZSyncAnimation>();
            if (_anim != null) _anim.SetBool("attach_bed", false);
            Traverse.Create(_humanoid).Method("ShowHandItems", false, false).GetValue();
        }

        private void WakeUp(Settlement s)
        {
            _view.GetZDO().Set(HashAsleep, false);
            GetUp();
            _bedAt = Vector3.zero;
            Vector3 inside, door;
            if (FindBed(s, out inside, out door)) Relocate(door);
            SetPatrol(Home, 6f);
        }

        /// <summary>Asleep and still: the AI does not walk a sleeper about (SteadyWalkPatch).</summary>
        public bool IsAsleep { get { return _view != null && _view.IsValid() && Asleep; } }

        /// <summary>The settlement is raised (a thief in the night): up and out, whoever owns this one's mind.</summary>
        public void Rouse()
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || !Asleep) return;
            Settlement s = SimHost.Sim.State.GetSettlement(SettlementId);
            if (s != null) WakeUp(s);
            _phaseUntil = 0f;
            _bedtime = -1f;
        }

        /// <summary>
        /// The player who hired this one goes through a portal: the hired sword goes with them, set down
        /// beside the far end. Its place in the world database moves too, so where the near end is left
        /// behind and unloaded, it comes up again at the far end with the player.
        /// </summary>
        public void FollowThroughPortal(Vector3 to, int index)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;
            float a = index * 2.1f;
            Vector3 at = to + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f + Vector3.up * 0.3f;
            Relocate(at);
            _view.GetZDO().SetPosition(at);
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
                _errandAt = stand;
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
                _errandAt = far;
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

        /// <summary>The envoy keeps to the mouth of the tent (EnvoyPost), facing the way in, and follows it if it moves.</summary>
        private void AtThePost(Settlement s)
        {
            if (_talking || Time.time < _phaseUntil) return;
            _phaseUntil = Time.time + 20f;
            Vector3 post;
            float yaw;
            if (!EnvoyPost.PostOf(s, out post, out yaw)) return;
            if (Utils.DistanceXZ(transform.position, post) > 25f && !Watched(40f))
            {
                float ground;
                if (ZoneSystem.instance.GetGroundHeight(post, out ground)) post.y = ground + 0.3f;
                Relocate(post);
            }
            SetPatrol(post, 0.8f);
            if (Utils.DistanceXZ(transform.position, post) < 1.5f) TurnTo(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
        }

        private Vector3 _journeyLast;
        private bool _journeyLooked;
        private int _stuck;

        private void OnTheMove(Settlement s)
        {
            Player player = Player.m_localPlayer;
            if (Role == NpcRole.Squad)
            {
                MarchWithBand(player);
                return;
            }
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
                // Held up behind (a bank, a rock): they catch up with the trader where nobody sees it.
                if (trader != null && best > 30f && !Watched(45f)) Relocate(trader.transform.position - trader.transform.forward * 3f + Vector3.up * 0.3f);
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
                bool there = Utils.DistanceXZ(transform.position, target) < 5f;
                // Not a step nearer since the last look: something is in the way (a rock, a bank, a wall). The next
                // stretch of the road is tried, and one held up for good is set down further along where nobody sees it.
                if (!there && _journeyLooked) _stuck = Utils.DistanceXZ(transform.position, _journeyLast) < 2f ? _stuck + 1 : 0;
                _journeyLooked = true;
                _journeyLast = transform.position;
                _journeyNext = Time.time + (_stuck > 0 ? 10f : 25f);
                if (there || _stuck > 0) _journeyAt += _journeyIn ? -14f : 14f;
                if (_stuck >= 3 && !Watched(45f))
                {
                    Vector3 ahead = Roads.Along(s, _journeyAt);
                    float ground;
                    if (ZoneSystem.instance.GetGroundHeight(ahead, out ground)) ahead.y = ground + 0.3f;
                    Relocate(ahead);
                    Plugin.Log.LogInfo(s.Name + ": a caravan held up on its road at " + _journeyLast.ToString("0") + " goes on from " + ahead.ToString("0"));
                    _stuck = 0;
                }
                bool arrived = _journeyIn ? _journeyAt <= 10f : _journeyAt >= 190f;
                // Followed out, the caravan goes on along the road until it is left behind.
                if (arrived && !_journeyIn && _journeyAt < 600f && Watched(60f)) arrived = false;
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
                Defs.PlotTransform(s, b.Plot, out offset, out yaw);
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

        /// <summary>
        /// By day half the guards walk a round: about the edge of the settlement and a stretch out along
        /// its road and back, a stop at each turn to look about. The other half keep near the fire.
        /// </summary>
        private void DayWatch(Settlement s, Vector3 centre)
        {
            if ((Mathf.Abs(Seed) / 5) % 2 == 1)
            {
                if (_patrolLeg != -1)
                {
                    _patrolLeg = -1;
                    SetPatrol(Home, 10f);
                }
                return;
            }
            if (_patrolLeg >= 0 && Time.time < _phaseUntil)
            {
                if (_ai.m_randomMoveRange > 0.5f && Utils.DistanceXZ(transform.position, _legAt) < 3.5f) SetPatrol(_legAt, 0.35f);
                return;
            }
            float ring = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 4f;
            _patrolLeg = _patrolLeg < 0 ? Mathf.Abs(Seed) % 6 : (_patrolLeg + 1) % 6;
            _phaseUntil = Time.time + 45f;
            if (_patrolLeg == 5 && Roads.HasRoad(s))
            {
                // Out along the road to see who comes.
                _legAt = Roads.Along(s, ring + 30f);
                _phaseUntil = Time.time + 70f;
            }
            else
            {
                float a = (_patrolLeg * 72f + (Seed % 40)) * Mathf.Deg2Rad;
                _legAt = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
            }
            SetPatrol(_legAt, 3f);
        }

        private void GuardDuty(Settlement s, Vector3 centre, bool night)
        {
            if (KeepTower(s)) return;
            if (!night)
            {
                DayWatch(s, centre);
                return;
            }

            // Walk the edge of the settlement, leg by leg.
            float ring = Defs.RingRadius[Defs.RingsByTier[(int)s.Tier] - 1] + 7f;
            if (_patrolLeg < 0 || Time.time >= _phaseUntil)
            {
                _patrolLeg = _patrolLeg < 0 ? Mathf.Abs(Seed) % 8 : (_patrolLeg + 1) % 8;
                _phaseUntil = Time.time + 35f;
                float a = _patrolLeg * Mathf.PI / 4f;
                _legAt = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
                SetPatrol(_legAt, 3f);
            }
            // At the end of a leg the guard stands and watches, rather than stepping about on the spot.
            else if (_ai.m_randomMoveRange > 0.5f && Utils.DistanceXZ(transform.position, _legAt) < 3.5f) SetPatrol(_legAt, 0.35f);

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
                _legAt = StandBeside(player.transform.position, transform.position, 3f);
                SetPatrol(_legAt, 1.5f);
            }
            if (Chat.instance != null) Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 30f, 6f, "", Texts.Get(line), false);
            if (line == "night_attack" && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("night_alarm", Texts.Name(s.Name)));
            // The challenge is plain to see, not only a line over a guard's head: an attack that follows must not look out of nowhere.
            if (line == "night_halt" && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("night_halt"));
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

    /// <summary>
    /// The game walks an idle creature that is far from its patrol point there in short legs, each
    /// turned a random way off the straight line, and the creature stops to turn at the start of every
    /// leg: two or three steps, a halt, two or three steps. People of the factions going somewhere walk
    /// there in one go; near the spot they mill about as before.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), "RandomMovement")]
    internal static class SteadyWalkPatch
    {
        private static readonly AccessTools.FieldRef<BaseAI, float> Timer = AccessTools.FieldRefAccess<BaseAI, float>("m_randomMoveUpdateTimer");
        private static readonly AccessTools.FieldRef<BaseAI, bool> Reached = AccessTools.FieldRefAccess<BaseAI, bool>("m_reachedRandomMoveTarget");
        private static readonly System.Func<BaseAI, float, Vector3, float, bool, bool> MoveTo =
            AccessTools.MethodDelegate<System.Func<BaseAI, float, Vector3, float, bool, bool>>(AccessTools.Method(typeof(BaseAI), "MoveTo"));

        private static bool Prefix(BaseAI __instance, float dt, Vector3 centerPoint)
        {
            FactionMember member = FactionMember.MemberOf(__instance);
            if (member == null) return true;
            if (member.IsAsleep)
            {
                __instance.StopMoving();
                return false;
            }
            float near = Mathf.Max(__instance.m_randomMoveRange * 2f, 3f);
            if (Utils.DistanceXZ(centerPoint, __instance.transform.position) <= near) return true;
            if (MoveTo(__instance, dt, centerPoint, near * 0.5f, __instance.IsAlerted()))
            {
                // There: a spot of its own round the point next time.
                Reached(__instance) = true;
                Timer(__instance) = 0f;
            }
            return false;
        }
    }

    /// <summary>Hired fighters near the player go through a portal with them (Company: hired swords).</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class HiredThroughPortalPatch
    {
        private static void Postfix(Player __instance, Vector3 pos, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || !SimHost.Active) return;
            int n = 0;
            foreach (FactionMember m in new List<FactionMember>(FactionMember.All))
            {
                if (m.Role != NpcRole.Mercenary || m.Humanoid == null || m.Humanoid.IsDead()) continue;
                if (Vector3.Distance(m.transform.position, __instance.transform.position) > 40f) continue;
                m.FollowThroughPortal(pos, n++);
            }
            if (n > 0) Plugin.Log.LogInfo(n + " hired fighters go through the portal with the player");
        }
    }
}
