using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LivingFactions
{
    /// <summary>
    /// Development helpers, off unless Dev/DevTools is enabled in the config: a command file the
    /// mod polls (so tests can drive the game without touching the keyboard) and an autostart that
    /// loads a lab world straight from the main menu.
    /// </summary>
    internal static class DevTools
    {
        private static float _nextPoll;
        private static float _menuSeenAt = -1f;
        private static bool _autostartDone;

        private static string CmdFile { get { return Path.Combine(SimHost.DataDir, "cmd.txt"); } }
        private static string OutFile { get { return Path.Combine(SimHost.DataDir, "out.txt"); } }
        private static string AutostartFile { get { return Path.Combine(SimHost.DataDir, "autostart.txt"); } }

        public static void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;
            TryAutostart();
            TestRunner.Update();
            if (!File.Exists(CmdFile)) return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(CmdFile);
                File.Delete(CmdFile);
            }
            catch (IOException)
            {
                return;
            }
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string result;
                try
                {
                    result = Commands.Run(line);
                }
                catch (Exception e)
                {
                    result = "ERROR " + e;
                }
                File.AppendAllText(OutFile, "> " + line + "\n" + result + "\n");
                Plugin.Log.LogInfo("[dev] " + line + " => " + result);
            }
        }

        private static void TryAutostart()
        {
            if (_autostartDone || FejdStartup.instance == null || !File.Exists(AutostartFile)) return;
            if (_menuSeenAt < 0f) _menuSeenAt = Time.unscaledTime;
            if (Time.unscaledTime - _menuSeenAt < 1.5f) return;
            _autostartDone = true;
            string world = File.ReadAllText(AutostartFile).Trim();
            if (world.Length == 0) world = "LFLab";
            Plugin.Instance.StartCoroutine(Autostart(world));
        }

        private static IEnumerator Autostart(string worldName)
        {
            const string profileName = "lftest";
            FejdStartup menu = FejdStartup.instance;
            Traverse t = Traverse.Create(menu);
            Plugin.Log.LogInfo("[dev] autostart: world " + worldName + ", character " + profileName);

            if (!PlayerProfile.HaveProfile(profileName))
            {
                if (t.Field("m_playerInstance").GetValue<GameObject>() == null)
                {
                    t.Method("SetupCharacterPreview", new[] { typeof(PlayerProfile) }, new object[] { null }).GetValue();
                }
                yield return null;
                GameObject preview = t.Field("m_playerInstance").GetValue<GameObject>();
                Player player = preview.GetComponent<Player>();
                PlayerProfile profile = new PlayerProfile(profileName, FileHelpers.FileSource.Local);
                player.GiveDefaultItems();
                profile.SetName("Lftest");
                profile.SavePlayerData(player);
                profile.Save();
                SaveSystem.InvalidateCache(SaveDataType.Character);
                Plugin.Log.LogInfo("[dev] created lab character");
            }

            List<PlayerProfile> profiles = SaveSystem.GetAllPlayerProfiles();
            int index = profiles.FindIndex(p => p.GetFilename() == profileName);
            if (index < 0)
            {
                Plugin.Log.LogError("[dev] autostart: lab character not found");
                yield break;
            }
            t.Field("m_profiles").SetValue(profiles);
            t.Field("m_profileIndex").SetValue(index);
            menu.OnCharacterStart();
            yield return new WaitForSecondsRealtime(0.2f);

            World world = World.GetCreateWorld(worldName, FileHelpers.FileSource.Local);
            t.Field("m_world").SetValue(world);
            foreach (string toggle in new[] { "m_publicServerToggle", "m_openServerToggle", "m_crossplayServerToggle" })
            {
                Toggle ui = t.Field(toggle).GetValue<Toggle>();
                if (ui != null) ui.isOn = false;
            }
            menu.OnWorldStart();
        }
    }
}
