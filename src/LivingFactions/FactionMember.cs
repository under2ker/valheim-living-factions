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
        private static readonly int HashSquad = "lf_squad".GetStableHashCode();
        /// <summary>The band on the move this one walks with (SquadPresence), 0 for none.</summary>
        public int SquadId;
        private static readonly int HashSeed = "lf_seed".GetStableHashCode();
        private static readonly int HashInit = "lf_init".GetStableHashCode();
        private static readonly int HashUntil = "lf_until".GetStableHashCode();
        private static readonly int HashEmployer = "lf_employer".GetStableHashCode();
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

        private static readonly Dictionary<BaseAI, FactionMember> Minds = new Dictionary<BaseAI, FactionMember>();

        /// <summary>The member an AI belongs to, or null (cheap: asked every frame for every creature).</summary>
        public static FactionMember MemberOf(BaseAI ai)
        {
            FactionMember m;
            return ai != null && Minds.TryGetValue(ai, out m) ? m : null;
        }

        public Humanoid Humanoid { get { return _humanoid; } }

        public long EmployerId { get { return _view != null && _view.IsValid() ? _view.GetZDO().GetLong(HashEmployer) : 0L; } }

        public void SetEmployer(Player player)
        {
            if (player != null && _view != null && _view.IsValid() && _view.IsOwner())
                _view.GetZDO().Set(HashEmployer, player.GetPlayerID());
        }

        public Player Employer()
        {
            long id = EmployerId;
            if (id == 0L) return null;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && player.GetPlayerID() == id) return player;
            return null;
        }

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
            SquadId = zdo.GetInt(HashSquad);
            DisplayName = zdo.GetString(ZDOVars.s_overrideHoverName);
            if (_humanoid != null) ByCharacter[_humanoid] = this;
            if (_ai != null) Minds[_ai] = this;
        }

        private void Start()
        {
            if (FactionId != 0) Apply();
        }

        private void OnDestroy()
        {
            if (_humanoid != null) ByCharacter.Remove(_humanoid);
            if (_ai != null) Minds.Remove(_ai);
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
        public void JoinSquad(int squadId)
        {
            SquadId = squadId;
            _view.GetZDO().Set(HashSquad, squadId);
        }

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
            string key = "title_" + Role;
            Squad band = SquadId != 0 && SimHost.Active ? SquadPresence.Find(SquadId) : null;
            if (band != null) key = "title_squad_" + band.Kind;
            string title = Texts.Has(key) ? Texts.Get(key) : "";
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
                GiveGear(rnd, tier, faction.CultureId, settlement);
            }

            bool fighter = Role == NpcRole.Guard || Role == NpcRole.Mercenary || Role == NpcRole.Escort || Role == NpcRole.Squad;
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

        /// <summary>Each clan's own sword and shield (CustomItems), carried by its chiefs and better fighters.</summary>
        private static readonly Dictionary<string, string[]> ClanGear = new Dictionary<string, string[]>
        {
            { "clan_wolf", new[] { "LF_GreyBlade", "LF_WolfHideShield" } },
            { "clan_stag", new[] { "LF_AntlerBlade", "LF_StagTrailShield" } },
            { "clan_raven", new[] { "LF_BlackBeak", "LF_RavenShield" } },
        };

        private void GiveGear(System.Random rnd, int tier, string cultureId, LivingFactions.Core.Settlement home)
        {
            List<string> gear = new List<string>();
            if (Role == NpcRole.Envoy)
            {
                gear.Add(tier >= 2 ? "ArmorLeatherChest" : "ArmorRagsChest");
                gear.Add(tier >= 2 ? "ArmorLeatherLegs" : "ArmorRagsLegs");
                gear.Add(ObjectDB.instance.GetItemPrefab("CapeLinen") != null ? "CapeLinen" : "CapeDeerHide");
                gear.Add("HelmetHat5");
            }
            else if (Role == NpcRole.Villager || Role == NpcRole.Trader)
            {
                gear.Add(Pick(rnd, tier >= 2 ? new[] { "AxeFlint", "AxeBronze", "KnifeFlint", "Club" } : new[] { "AxeStone", "Club", "AxeFlint" }));
                gear.Add(tier >= 2 ? "ArmorLeatherChest" : "ArmorRagsChest");
                gear.Add(tier >= 2 ? "ArmorLeatherLegs" : "ArmorRagsLegs");
                if (rnd.Next(100) < 35) gear.Add(Pick(rnd, new[] { "HelmetStrawHat", "HelmetHat1", "HelmetHat3", "HelmetHat5", "HelmetHat7" }));
                if (rnd.Next(100) < 30) gear.Add("CapeDeerHide");
            }
            else
            {
                int level = GearLevel(tier, home);
                // One fighter in three is an archer: a bow tuned for AI use (it keeps its distance) and a knife.
                if (ModConfig.Archers.Value && Role != NpcRole.Chief && Mathf.Abs(Seed) % 3 == 0 && ObjectDB.instance.GetItemPrefab("FW_BowDraugrFang") != null)
                {
                    gear.Add("FW_BowDraugrFang");
                    gear.Add(ObjectDB.instance.GetItemPrefab("FW_KnifeSilver") != null ? "FW_KnifeSilver" : "KnifeFlint");
                    gear.Add(Kit(level, 2));
                    gear.Add(Kit(level, 3));
                    if (level >= 3) gear.Add(Kit(level, 4));
                }
                else
                {
                    gear.Add(Pick(rnd, KitWeapons(level)));
                    for (int slot = 1; slot <= 4; slot++) gear.Add(Kit(level, slot));
                }
                gear.Add(Role == NpcRole.Chief ? (level >= 2 ? "CapeWolf" : "CapeTrollHide") : (level >= 4 ? "CapeWolf" : level >= 3 ? "CapeTrollHide" : "CapeDeerHide"));

                // From a village up the clan shows its colours: its shield on every shield arm, its sword in
                // the chief's hand and in one fighter's hand in three (the seed, not rnd: looks stay as they were).
                // Past iron the clan's own gear would be a step down: only the chief still carries it.
                string[] own;
                if (level >= 2 && (level <= 3 || Role == NpcRole.Chief) && ClanGear.TryGetValue(cultureId, out own))
                {
                    int shield = gear.FindIndex(g => g.StartsWith("Shield"));
                    if (shield >= 0) gear[shield] = own[1];
                    int sword = gear.FindIndex(g => g.StartsWith("Sword") || g.StartsWith("Axe") || g.StartsWith("Mace") || g.StartsWith("Spear"));
                    if (sword >= 0 && (Role == NpcRole.Chief || Mathf.Abs(Seed / 7) % 3 == 0)) gear[sword] = own[0];
                }
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

        /// <summary>
        /// A fighter's gear level: the settlement's size (the chief one better), at least what its biome calls for
        /// (World/GearByBiome), and better with the world's age (World/GearAgeDays). 0-1 flint and leather,
        /// 2 bronze, 3 iron, 4 silver, 5 black metal, 6 carapace.
        /// </summary>
        private int GearLevel(int tier, LivingFactions.Core.Settlement home)
        {
            int level = tier + (Role == NpcRole.Chief ? 1 : 0);
            if (ModConfig.GearByBiome.Value)
            {
                // The home settlement's biome; one without a home (a raider band) goes by where it stands.
                int floor = home != null ? BiomeGearFloor(home.Biome)
                    : WorldGenerator.instance != null ? BiomeGearFloor(WorldGenerator.instance.GetBiome(transform.position)) : 0;
                level = Mathf.Max(level, floor + (Role == NpcRole.Chief ? 1 : 0));
            }
            int every = ModConfig.GearAgeDays.Value;
            if (every > 0) level += Mathf.Clamp((int)(SimHost.WorldDay / every), 0, 2);
            return Mathf.Clamp(level, 0, KitLevels.Length - 1);
        }

        internal static int BiomeGearFloor(LivingFactions.Core.Biome biome)
        {
            switch (biome)
            {
                case LivingFactions.Core.Biome.BlackForest: return 2;
                case LivingFactions.Core.Biome.Swamp: return 3;
                case LivingFactions.Core.Biome.Mountain: return 4;
                case LivingFactions.Core.Biome.Plains: return 5;
                case LivingFactions.Core.Biome.Mistlands:
                case LivingFactions.Core.Biome.Ashlands:
                case LivingFactions.Core.Biome.DeepNorth: return 6;
                default: return 0;
            }
        }

        internal static int BiomeGearFloor(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.BlackForest: return 2;
                case Heightmap.Biome.Swamp: return 3;
                case Heightmap.Biome.Mountain: return 4;
                case Heightmap.Biome.Plains: return 5;
                case Heightmap.Biome.Mistlands:
                case Heightmap.Biome.AshLands:
                case Heightmap.Biome.DeepNorth: return 6;
                default: return 0;
            }
        }

        /// <summary>Per gear level: weapons, shield, chest, legs, helmet.</summary>
        private static readonly string[][] KitLevels =
        {
            new[] { "SpearFlint|AxeFlint|Club", "ShieldWood", "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather" },
            new[] { "SpearFlint|AxeFlint|Club", "ShieldWood", "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather" },
            new[] { "SwordBronze|AxeBronze|MaceBronze|SpearBronze", "ShieldBronzeBuckler", "ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze" },
            new[] { "SwordIron|AxeIron|MaceIron", "ShieldBanded", "ArmorIronChest", "ArmorIronLegs", "HelmetIron" },
            new[] { "SwordSilver|MaceSilver|SpearWolfFang", "ShieldSilver", "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake" },
            new[] { "SwordBlackmetal|AxeBlackMetal|AtgeirBlackmetal", "ShieldBlackmetal", "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded" },
            new[] { "SwordMistwalker|MaceNeedle|AxeJotunBane", "ShieldCarapace", "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace" },
        };

        /// <summary>The item for a slot at a level, or at the best level below it whose item the game has (another mod may remove some).</summary>
        private static string Kit(int level, int slot)
        {
            for (int l = Mathf.Clamp(level, 0, KitLevels.Length - 1); l >= 0; l--)
            {
                string name = KitLevels[l][slot];
                if (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(name) != null) return name;
            }
            return KitLevels[0][slot];
        }

        private static string[] KitWeapons(int level)
        {
            for (int l = Mathf.Clamp(level, 0, KitLevels.Length - 1); l >= 0; l--)
            {
                string[] found = System.Array.FindAll(KitLevels[l][0].Split('|'), n => ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(n) != null);
                if (found.Length > 0) return found;
            }
            return KitLevels[0][0].Split('|');
        }

        private static string Pick(System.Random rnd, string[] options)
        {
            return options[rnd.Next(options.Length)];
        }

        /// <summary>True when a player is near enough to see this one vanish.</summary>
        public bool Watched(float range)
        {
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p != null && Utils.DistanceXZ(p.transform.position, transform.position) < range) return true;
            }
            return false;
        }

        public bool IsHostileTo(Player player)
        {
            if (!SimHost.Active || player == null) return false;
            if (Role == NpcRole.Envoy) return false;
            // Under the envoy's shelter the faction does not set on anyone, at war or not, unless they strike first.
            if (EnvoyPost.Shelters(FactionId, player.transform.position) && !Grudges.AngryAt(this, player)) return false;
            if (SimHost.Sim.IsHostileToPlayer(FactionId, player.GetPlayerID())) return true;
            if (Role != NpcRole.Villager && NightWatch.AngryAt(SettlementId, player)) return true;
            return Grudges.AngryAt(this, player);
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
            // A hired sword is run by its employer's game: after a portal or a zone change another player's
            // game may have taken it over, and from there it could neither find nor follow the one who pays.
            if (Role == NpcRole.Mercenary && _view != null && _view.IsValid() && !_view.IsOwner() && Player.m_localPlayer != null
                && EmployerId != 0L && EmployerId == Player.m_localPlayer.GetPlayerID())
                _view.ClaimOwnership();
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || !SimHost.Active) return;
            long until = _view.GetZDO().GetLong(HashUntil);
            double now = ZNet.instance.GetTimeSeconds();
            // The raid, the journey or the contract is over: they are gone, though not before the eyes of
            // someone walking with them. A while later they go whoever looks.
            if (Npcs.IsTemporary(Role) && now > until && (now > until + 150.0 || !Watched(55f)))
            {
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            ThinkRoutine();
            Player player = Player.m_localPlayer;
            if (player == null) return;

            // Anger is Grudges' business now; a flag the game set before (an older save) is cleared.
            if (_ai != null && _ai.IsAggravated()) _ai.SetAggravated(false, BaseAI.AggravatedReason.Damage);

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
                    string line = chat ? Texts.Vary(BarkKey(), salt / 2) : Texts.Vary("greet_" + standing, salt / 2);
                    // The chief does not send the player to the chief.
                    if (Role == NpcRole.Chief && line == Texts.Get("greet_Neutral2")) line = Texts.Get("greet_Neutral");
                    Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 15f, 6f, "", line, false);
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
            if (Role != NpcRole.Envoy && Player.m_localPlayer != null && IsHostileTo(Player.m_localPlayer)) return "";
            return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] " + Texts.Get("ui_talk"));
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !SimHost.Active) return false;
            Player player = user as Player;
            if (player == null) return false;
            if (Role != NpcRole.Envoy && IsHostileTo(player))
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
