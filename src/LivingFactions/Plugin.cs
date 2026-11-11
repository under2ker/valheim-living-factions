using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace LivingFactions
{
    [BepInPlugin(Guid, ModName, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "alexandr.livingfactions";
        public const string ModName = "Living Factions";
        public const string Version = "0.8.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            // The game reads -savedir only on a dedicated server; a client given it keeps its saves there too
            // (test copies of the game run side by side, each with its own worlds: tools/lfinst.sh).
            string[] args = System.Environment.GetCommandLineArgs();
            // -lfmute: a test copy of the game makes no sound (several play at once).
            if (System.Array.IndexOf(args, "-lfmute") >= 0) _mute = true;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-savedir") continue;
                Utils.SetSaveDataPath(args[i + 1]);
                Log.LogInfo("Saves kept in " + args[i + 1]);
                break;
            }
            Net.Init();
            ModConfig.Bind(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            PrefabManager.OnVanillaPrefabsAvailable += Npcs.CreatePrefabs;
            ItemManager.OnVanillaItemsAvailable += CustomItems.CreatePrefabs;
            CommandManager.Instance.AddConsoleCommand(new LfConsoleCommand());
            Log.LogInfo(ModName + " " + Version + " loaded");
        }

        private static bool _mute;

        private void Update()
        {
            // Kept at zero: the game sets the listener's volume from its own settings when they load.
            if (_mute && UnityEngine.AudioListener.volume != 0f) UnityEngine.AudioListener.volume = 0f;
            if (ModConfig.DevTools.Value)
            {
                DevTools.Update();
                Cinema.Update();
            }
            SimHost.Update();
            if (Net.IsDedicated) return;
            Materializer.Update();
            Discovery.Update();
            MinimapNpcs.Update();
            Chests.Update();
            LootSweep.Update();
            NightThief.Update();
            NightWatch.Update();
            Rewards.Update();
            if (SimHost.Active && ZoneSystem.instance != null) Forestry.Update();
            SquadPresence.Update();
            EnvoyPost.Update();
            HudWidget.Update();
            DiplomacyUI.Update();
        }

        private void OnDestroy()
        {
            SimHost.Save();
            if (_harmony != null) _harmony.UnpatchSelf();
        }
    }
}
