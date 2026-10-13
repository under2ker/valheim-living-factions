using System;
using System.Collections.Generic;
using System.Reflection;
using Jotunn.Entities;
using Jotunn.Managers;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    internal enum NpcRole { Villager = 0, Guard = 1, Chief = 2, Raider = 3, Mercenary = 4, Trader = 5, Escort = 6 }

    /// <summary>Creates the character prefabs the factions are populated with.</summary>
    internal static class Npcs
    {
        public const string Viking = "LF_Viking";

        private static bool _created;
        private static readonly HashSet<int> NpcHashes = new HashSet<int>();

        public static bool IsNpcPrefab(int prefabHash) { return NpcHashes.Contains(prefabHash); }

        /// <summary>Raiders, hired fighters and caravans: in the world for a while, never counted as inhabitants.</summary>
        public static bool IsTemporary(NpcRole role) { return role >= NpcRole.Raider; }

        public static void CreatePrefabs()
        {
            if (_created) return;
            _created = true;
            PrefabManager.OnVanillaPrefabsAvailable -= CreatePrefabs;
            foreach (string name in new[] { Viking, "LF_Fuling", "LF_Dvergr", "LF_Greydwarf", "LF_Draugr" }) NpcHashes.Add(name.GetStableHashCode());
            try
            {
                CreateViking();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not create the human NPC prefab: " + e);
            }
            CreateRace("LF_Fuling", "Goblin", "GoblinBrute");
            CreateRace("LF_Dvergr", "Dverger");
            CreateRace("LF_Greydwarf", "Greydwarf");
            CreateRace("LF_Draugr", "Draugr");
        }

        public static GameObject PrefabFor(Faction faction)
        {
            string name;
            switch (Defs.Culture(faction.CultureId).Group)
            {
                case "fuling": name = "LF_Fuling"; break;
                case "dvergr": name = "LF_Dvergr"; break;
                case "greydwarf": name = "LF_Greydwarf"; break;
                case "draugr": name = "LF_Draugr"; break;
                default: name = Viking; break;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(name);
            // A missing race prefab (renamed by a game update) falls back to humans rather than nothing.
            return prefab != null ? prefab : ZNetScene.instance.GetPrefab(Viking);
        }

        /// <summary>
        /// A human NPC built from the player prefab: the Player component is swapped for a plain
        /// Humanoid driven by MonsterAI, keeping the body, animator and equipment visuals.
        /// </summary>
        private static void CreateViking()
        {
            GameObject go = PrefabManager.Instance.CreateClonedPrefab(Viking, "Player");
            if (go == null)
            {
                Plugin.Log.LogError("Player prefab not found; human factions will have no people");
                return;
            }

            Player player = go.GetComponent<Player>();
            Humanoid humanoid = go.AddComponent<Humanoid>();
            CopyFields(player, humanoid, typeof(Humanoid));
            DestroyComponent<PlayerController>(go);
            DestroyComponent<Talker>(go);
            DestroyComponent<Skills>(go);
            UnityEngine.Object.DestroyImmediate(player);

            humanoid.m_name = "Viking";
            humanoid.m_group = "";
            humanoid.m_faction = Character.Faction.Players;
            humanoid.m_boss = false;
            humanoid.m_health = 100f;
            humanoid.m_defaultItems = new GameObject[0];
            humanoid.m_randomWeapon = new GameObject[0];
            humanoid.m_randomArmor = new GameObject[0];
            humanoid.m_randomShield = new GameObject[0];
            humanoid.m_randomSets = new Humanoid.ItemSet[0];
            humanoid.m_randomItems = new Humanoid.RandomItem[0];

            ZNetView view = go.GetComponent<ZNetView>();
            view.m_persistent = true;
            view.m_distant = false;
            view.m_type = ZDO.ObjectType.Default;

            MonsterAI ai = go.AddComponent<MonsterAI>();
            ConfigureAI(ai);
            ai.m_consumeItems = new List<ItemDrop>();

            go.AddComponent<FactionMember>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, false));
            Plugin.Log.LogInfo("Created NPC prefab " + Viking);
        }

        private static void CreateRace(string name, params string[] bases)
        {
            try
            {
                foreach (string candidate in bases)
                {
                    if (PrefabManager.Instance.GetPrefab(candidate) == null) continue;
                    GameObject go = PrefabManager.Instance.CreateClonedPrefab(name, candidate);
                    MonsterAI ai = go.GetComponent<MonsterAI>();
                    if (ai != null)
                    {
                        ai.m_aggravatable = true;
                        ai.m_enableHuntPlayer = false;
                        ai.m_attackPlayerObjects = false;
                        ai.m_sleeping = false;
                        ai.m_randomMoveRange = 9f;
                        ai.m_maxChaseDistance = 45f;
                    }
                    NpcTalk talk = go.GetComponent<NpcTalk>();
                    if (talk != null) UnityEngine.Object.DestroyImmediate(talk);
                    go.AddComponent<FactionMember>();
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(go, false));
                    Plugin.Log.LogInfo("Created NPC prefab " + name + " from " + candidate);
                    return;
                }
                Plugin.Log.LogWarning("No base creature found for " + name);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not create " + name + ": " + e);
            }
        }

        private static void ConfigureAI(MonsterAI ai)
        {
            ai.m_viewRange = 30f;
            ai.m_viewAngle = 120f;
            ai.m_hearRange = 30f;
            ai.m_pathAgentType = Pathfinding.AgentType.Humanoid;
            ai.m_moveMinAngle = 60f;
            ai.m_smoothMovement = true;
            ai.m_randomMoveInterval = 14f;
            ai.m_randomMoveRange = 9f;
            ai.m_avoidFire = false;
            ai.m_afraidOfFire = false;
            ai.m_avoidWater = true;
            ai.m_aggravatable = true;
            ai.m_alertRange = 20f;
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_enableHuntPlayer = false;
            ai.m_attackPlayerObjects = false;
            ai.m_maxChaseDistance = 45f;
            ai.m_circleTargetInterval = 0f;
            ai.m_sleeping = false;
        }

        private static void DestroyComponent<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            if (component != null) UnityEngine.Object.DestroyImmediate(component);
        }

        /// <summary>Copies every field declared from <paramref name="top"/> down to MonoBehaviour.</summary>
        private static void CopyFields(object source, object target, Type top)
        {
            for (Type t = top; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral) continue;
                    field.SetValue(target, field.GetValue(source));
                }
            }
        }
    }
}
