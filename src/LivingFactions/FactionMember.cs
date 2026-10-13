using System.Collections.Generic;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>Marks a character as an inhabitant of a faction settlement and gives it its look, gear and manners.</summary>
    internal partial class FactionMember : MonoBehaviour, Interactable
    {
        public static readonly int HashFaction = "lf_fid".GetStableHashCode();
        public static readonly int HashRole = "lf_role".GetStableHashCode();
        private static readonly int HashSeed = "lf_seed".GetStableHashCode();
        private static readonly int HashInit = "lf_init".GetStableHashCode();
        private static readonly int HashUntil = "lf_until".GetStableHashCode();
        private const double RaidSeconds = 240;

        private static readonly Dictionary<Character, FactionMember> ByCharacter = new Dictionary<Character, FactionMember>();
        private static List<string> _hairs, _beards;

        public int FactionId;
        public int SettlementId;
        public int Seed;
        public NpcRole Role;
        public float LastHurt = -999f;
        public string DisplayName = "";

        private ZNetView _view;
        private Humanoid _humanoid;
        private MonsterAI _ai;
        private bool _applied;
        private float _nextThink;
        private float _nextGreet;

        public static FactionMember Of(Character character)
        {
            FactionMember member;
            return character != null && ByCharacter.TryGetValue(character, out member) ? member : null;
        }

        public static IEnumerable<FactionMember> All { get { return ByCharacter.Values; } }

        public Humanoid Humanoid { get { return _humanoid; } }

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _humanoid = GetComponent<Humanoid>();
            _ai = GetComponent<MonsterAI>();
            if (_view == null || _view.GetZDO() == null) return;
            ZDO zdo = _view.GetZDO();
            FactionId = zdo.GetInt(HashFaction);
            SettlementId = zdo.GetInt(Materializer.HashSettlement);
            Role = (NpcRole)zdo.GetInt(HashRole);
            Seed = zdo.GetInt(HashSeed);
            DisplayName = zdo.GetString(ZDOVars.s_overrideHoverName);
            if (_humanoid != null) ByCharacter[_humanoid] = this;
        }

        private void Start()
        {
            if (FactionId != 0) Apply();
        }

        private void OnDestroy()
        {
            if (_humanoid != null) ByCharacter.Remove(_humanoid);
            ReleaseClaim();
        }

        /// <summary>Gives a temporary character (caravan, hired fighter) the time it has in the world. Call before Setup.</summary>
        public void Expire(double seconds)
        {
            _view.GetZDO().Set(HashUntil, (long)(ZNet.instance.GetTimeSeconds() + seconds));
        }

        public bool IsArcher
        {
            get
            {
                foreach (ItemDrop.ItemData item in _humanoid.GetInventory().GetAllItems())
                {
                    if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow) return true;
                }
                return false;
            }
        }

        /// <summary>Called once by the materializer right after the character is created.</summary>
        public void Setup(int factionId, int settlementId, NpcRole role, int seed)
        {
            FactionId = factionId;
            SettlementId = settlementId;
            Role = role;
            Seed = seed;
            ZDO zdo = _view.GetZDO();
            zdo.Set(HashFaction, factionId);
            zdo.Set(Materializer.HashSettlement, settlementId);
            zdo.Set(HashRole, (int)role);
            zdo.Set(HashSeed, seed);
            Apply();
        }

        private string _latinName = "";

        /// <summary>Name and calling in the language the mod speaks now; called again when the player switches it.</summary>
        public void Rename()
        {
            if (_latinName.Length == 0 || _view == null || !_view.IsValid() || !_view.IsOwner()) return;
            string title = Role == NpcRole.Chief ? (Texts.Lang == 1 ? "вождь" : "chieftain") : Role == NpcRole.Guard ? (Texts.Lang == 1 ? "страж" : "guard")
                : Role == NpcRole.Raider ? (Texts.Lang == 1 ? "налётчик" : "raider") : Role == NpcRole.Mercenary ? (Texts.Lang == 1 ? "наёмник" : "hired sword")
                : Role == NpcRole.Trader ? (Texts.Lang == 1 ? "торговец" : "trader") : Role == NpcRole.Escort ? (Texts.Lang == 1 ? "охранник каравана" : "caravan guard") : "";
            string personal = Texts.Name(_latinName);
            DisplayName = title.Length > 0 ? personal + ", " + title : personal;
            _view.GetZDO().Set(ZDOVars.s_overrideHoverName, DisplayName);
        }

        private void Apply()
        {
            if (_applied || _view == null || !_view.IsValid() || !_view.IsOwner() || !SimHost.Active) return;
            Settlement settlement = SimHost.Sim.State.GetSettlement(SettlementId);
            Faction faction = SimHost.Sim.State.GetFaction(FactionId);
            if (faction == null) return;
            _applied = true;

            CultureDef culture = Defs.Culture(faction.CultureId);
            int tier = settlement != null ? (int)settlement.Tier : 0;
            System.Random rnd = new System.Random(Seed);
            ZDO zdo = _view.GetZDO();

            VisEquipment vis = GetComponent<VisEquipment>();
            bool human = culture.Group == "human" && vis != null && vis.m_isPlayer;
            bool female = human && culture.FemaleNames != null && rnd.Next(100) < 40;
            string[] names = female ? culture.FemaleNames : culture.PersonNames;
            string personal = names[rnd.Next(names.Length)];
            _latinName = personal;
            Rename();
            if (Role == NpcRole.Raider && zdo.GetLong(HashUntil) == 0L) zdo.Set(HashUntil, (long)(ZNet.instance.GetTimeSeconds() + RaidSeconds));

            if (human)
            {
                vis.SetModel(female ? 1 : 0);
                LoadHairLists();
                if (_hairs.Count > 0) _humanoid.SetHair(_hairs[rnd.Next(_hairs.Count)]);
                if (!female && _beards.Count > 0 && rnd.Next(100) < 75) _humanoid.SetBeard(_beards[rnd.Next(_beards.Count)]);
                Color[] hairColors =
                {
                    new Color(0.85f, 0.7f, 0.35f), new Color(0.35f, 0.2f, 0.1f), new Color(0.12f, 0.09f, 0.07f),
                    new Color(0.6f, 0.25f, 0.1f), new Color(0.65f, 0.65f, 0.62f)
                };
                vis.SetHairColor(Utils.ColorToVec3(hairColors[rnd.Next(hairColors.Length)]));
                float tone = 0.8f + (float)rnd.NextDouble() * 0.2f;
                vis.SetSkinColor(new Vector3(tone, tone, tone));
                GiveGear(rnd, tier);
            }

            bool fighter = Role == NpcRole.Guard || Role == NpcRole.Mercenary || Role == NpcRole.Escort;
            float health = Role == NpcRole.Chief ? 250f + 50f * tier : fighter ? 150f + 40f * tier : 80f + 10f * tier;
            if (human && !zdo.GetBool(HashInit))
            {
                _humanoid.SetMaxHealth(health);
                _humanoid.SetHealth(health);
            }
            if (!zdo.GetBool(HashInit))
            {
                zdo.Set(HashInit, true);
                // Home is where they first stood; idle wandering stays around it.
                if (_ai != null) _ai.SetPatrolPoint();
                RememberHome();
            }
            if (_ai != null && Role == NpcRole.Mercenary)
            {
                // A hired sword looks out for trouble around the one who pays, and goes to meet it.
                _ai.m_viewRange = Mathf.Max(_ai.m_viewRange, 45f);
                _ai.m_hearRange = Mathf.Max(_ai.m_hearRange, 45f);
                _ai.m_alertRange = Mathf.Max(_ai.m_alertRange, 40f);
            }
            if (_ai != null)
            {
                _ai.m_aggravatable = true;
                _ai.m_randomMoveRange = Role == NpcRole.Chief ? 4f : Role == NpcRole.Villager ? 9f : 14f;
            }
        }

        private static void LoadHairLists()
        {
            if (_hairs != null) return;
            _hairs = new List<string>();
            _beards = new List<string>();
            foreach (ItemDrop item in ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, "Hair"))
            {
                if (!item.name.EndsWith("None")) _hairs.Add(item.name);
            }
            foreach (ItemDrop item in ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, "Beard"))
            {
                if (!item.name.EndsWith("None")) _beards.Add(item.name);
            }
        }

        private void GiveGear(System.Random rnd, int tier)
        {
            List<string> gear = new List<string>();
            if (Role == NpcRole.Villager || Role == NpcRole.Trader)
            {
                gear.Add(Pick(rnd, tier >= 2 ? new[] { "AxeFlint", "AxeBronze", "KnifeFlint", "Club" } : new[] { "AxeStone", "Club", "AxeFlint" }));
                gear.Add(tier >= 2 ? "ArmorLeatherChest" : "ArmorRagsChest");
                gear.Add(tier >= 2 ? "ArmorLeatherLegs" : "ArmorRagsLegs");
                if (rnd.Next(100) < 35) gear.Add(Pick(rnd, new[] { "HelmetStrawHat", "HelmetHat1", "HelmetHat3", "HelmetHat5", "HelmetHat7" }));
                if (rnd.Next(100) < 30) gear.Add("CapeDeerHide");
            }
            else
            {
                int level = Mathf.Clamp(tier + (Role == NpcRole.Chief ? 1 : 0), 0, 4);
                // One fighter in three is an archer: a bow tuned for AI use (it keeps its distance) and a knife.
                if (ModConfig.Archers.Value && Role != NpcRole.Chief && Mathf.Abs(Seed) % 3 == 0 && ObjectDB.instance.GetItemPrefab("FW_BowDraugrFang") != null)
                {
                    gear.Add("FW_BowDraugrFang");
                    gear.Add(ObjectDB.instance.GetItemPrefab("FW_KnifeSilver") != null ? "FW_KnifeSilver" : "KnifeFlint");
                    gear.Add("ArmorLeatherChest");
                    gear.Add("ArmorLeatherLegs");
                    gear.Add(level >= 2 ? "CapeTrollHide" : "CapeDeerHide");
                }
                else if (level <= 1)
                {
                    gear.Add(Pick(rnd, new[] { "SpearFlint", "AxeFlint", "Club" }));
                    gear.Add("ShieldWood");
                    gear.Add("ArmorLeatherChest");
                    gear.Add("ArmorLeatherLegs");
                    gear.Add("HelmetLeather");
                }
                else if (level == 2)
                {
                    gear.Add(Pick(rnd, new[] { "SwordBronze", "AxeBronze", "MaceBronze", "SpearBronze" }));
                    gear.Add("ShieldBronzeBuckler");
                    gear.Add("ArmorBronzeChest");
                    gear.Add("ArmorBronzeLegs");
                    gear.Add("HelmetBronze");
                }
                else
                {
                    gear.Add(Pick(rnd, new[] { "SwordIron", "AxeIron" }));
                    gear.Add("ShieldBanded");
                    gear.Add("ArmorIronChest");
                    gear.Add("ArmorIronLegs");
                    gear.Add("HelmetIron");
                }
                gear.Add(Role == NpcRole.Chief ? (level >= 2 ? "CapeWolf" : "CapeTrollHide") : (level >= 3 ? "CapeTrollHide" : "CapeDeerHide"));
            }

            Inventory inventory = _humanoid.GetInventory();
            inventory.RemoveAll();
            foreach (string name in gear)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(name);
                if (prefab != null) inventory.AddItem(prefab, 1);
            }
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToArray())
            {
                if (!item.IsWeapon() && item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Tool) _humanoid.EquipItem(item, false);
            }
            _humanoid.EquipBestWeapon(null, null, null, null);
        }

        private static string Pick(System.Random rnd, string[] options)
        {
            return options[rnd.Next(options.Length)];
        }

        public bool IsHostileTo(Player player)
        {
            if (!SimHost.Active || player == null) return false;
            if (SimHost.Sim.IsHostileToPlayer(FactionId, player.GetPlayerID())) return true;
            return _ai != null && _ai.IsAggravated();
        }

        private void Update()
        {
            TurnStep();
            if (Time.time < _nextThink) return;
            _nextThink = Time.time + 1f;
            if (!_applied)
            {
                if (FactionId != 0) Apply();
                return;
            }
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || !SimHost.Active) return;
            if (Npcs.IsTemporary(Role) && ZNet.instance.GetTimeSeconds() > _view.GetZDO().GetLong(HashUntil))
            {
                // The raid, the journey or the contract is over: they are gone.
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            ThinkRoutine();
            Player player = Player.m_localPlayer;
            if (player == null) return;

            bool hostile = SimHost.Sim.IsHostileToPlayer(FactionId, player.GetPlayerID());
            // A brawl is forgiven once tempers cool, unless the whole faction is hostile.
            if (_ai != null && _ai.IsAggravated() && !hostile && Time.time - LastHurt > 40f && !NightWatch.Angry(SettlementId))
            {
                _ai.SetAggravated(false, BaseAI.AggravatedReason.Damage);
            }

            if (Time.time >= _nextGreet && Chat.instance != null && Vector3.Distance(player.transform.position, transform.position) < 6f)
            {
                _nextGreet = Time.time + 50f + (Mathf.Abs(Seed) % 40);
                Faction faction = SimHost.Sim.State.GetFaction(FactionId);
                if (faction != null && (_ai == null || !_ai.IsAlerted()))
                {
                    Standing standing = SimHost.Sim.GetStanding(faction, player.GetPlayerID());
                    // People on good terms talk about what they are doing as often as they greet; the rest only greet.
                    int salt = Mathf.Abs(Seed) + (int)(Time.time / 45f);
                    bool chat = standing >= Standing.Neutral && (salt & 1) == 0;
                    Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 15f, 6f, "", chat ? Texts.Vary(BarkKey(), salt / 2) : Texts.Vary("greet_" + standing, salt / 2), false);
                }
            }
        }

        /// <summary>While talking, the character stays with the player instead of wandering off.</summary>
        public void SetTalking(Player player)
        {
            _talking = player != null;
            if (_ai != null) _ai.SetFollowTarget(player != null ? player.gameObject : null);
        }

        public string HoverText()
        {
            if (Player.m_localPlayer != null && IsHostileTo(Player.m_localPlayer)) return "";
            return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] " + Texts.Get("ui_talk"));
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !SimHost.Active) return false;
            Player player = user as Player;
            if (player == null) return false;
            if (IsHostileTo(player))
            {
                if (Chat.instance != null) Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 15f, 5f, "", Texts.Get("greet_War"), false);
                return true;
            }
            DiplomacyUI.Open(this, player);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
    }
}
