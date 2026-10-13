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
        public const string Version = "0.3.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ModConfig.Bind(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            PrefabManager.OnVanillaPrefabsAvailable += Npcs.CreatePrefabs;
            ItemManager.OnVanillaItemsAvailable += CustomItems.CreatePrefabs;
            CommandManager.Instance.AddConsoleCommand(new LfConsoleCommand());
            Log.LogInfo(ModName + " " + Version + " loaded");
        }

        private void Update()
        {
            if (ModConfig.DevTools.Value) DevTools.Update();
            SimHost.Update();
            Materializer.Update();
            Discovery.Update();
            Chests.Update();
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
