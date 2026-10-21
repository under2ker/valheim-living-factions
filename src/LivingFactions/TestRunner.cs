using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// Runs a whole test scenario inside the game in one go (dev tools only). A scenario is a text
    /// file of `lf` commands plus a few verbs of its own; the runner waits on what the game is
    /// actually doing instead of on fixed sleeps, and writes a report with PASS/FAIL lines.
    ///
    ///   goto 4                        travel to settlement 4 and wait until the player is there
    ///                                 and it has finished building and populating
    ///   wait 5                        game seconds (follows `speed`)
    ///   waitready [timeout]           until the player has arrived, the area is loaded and the
    ///                                 settlement around them has finished building and populating
    ///   shot name                     screenshot, a few frames after the last camera move
    ///   gearshots Item [name]         wear the item and photograph it from every angle its slot has,
    ///                                 plus slung on the back for weapons, shields and bows, plus once
    ///                                 with the joint markers on; FAIL when a hand-held item is not in
    ///                                 the hand (its surface is further than 5 cm from the palm)
    ///   expect TEXT :: command        PASS when the command's output contains TEXT
    ///   reject TEXT :: command        PASS when it does not
    ///   anything else                 an `lf` command; its output goes into the report
    /// </summary>
    internal static class TestRunner
    {
        private static bool _running;

        private static string ScenarioFile { get { return Path.Combine(SimHost.DataDir, "scenario.txt"); } }
        public static string ReportFile { get { return Path.Combine(SimHost.DataDir, "report.txt"); } }

        public static void Update()
        {
            if (_running || !SimHost.Active || Player.m_localPlayer == null || !File.Exists(ScenarioFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(ScenarioFile);
                File.Delete(ScenarioFile);
            }
            catch (IOException)
            {
                return;
            }
            _running = true;
            Plugin.Instance.StartCoroutine(Run(lines));
        }

        /// <summary>True when nothing around the player is still being built, levelled or populated.</summary>
        private static bool Settled(ref int lastCount, ref float stableSince)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting() || !ZNetScene.instance.IsAreaReady(player.transform.position)) return false;
            if (!Materializer.Idle) return false;
            int count = FactionMember.All.Count();
            if (count != lastCount)
            {
                lastCount = count;
                stableSince = Time.unscaledTime;
            }
            return Time.unscaledTime - stableSince > 2.5f;
        }

        private static IEnumerator Run(string[] lines)
        {
            File.WriteAllText(ReportFile, "=== scenario started " + DateTime.Now.ToString("HH:mm:ss") + " ===\n");
            int pass = 0, fail = 0;
            float started = Time.unscaledTime;
            // Right after login the player is still being placed; commands such as a teleport are ignored until then.
            while (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting() || !ZNetScene.instance.IsAreaReady(Player.m_localPlayer.transform.position)) yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(2f);
            bool quitAtEnd = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "quit")
                {
                    // Quitting ends the process, so the report is closed first and the quit runs last.
                    quitAtEnd = true;
                    continue;
                }
                string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string verb = a[0].ToLowerInvariant();
                string result;

                if (verb == "wait")
                {
                    // Real time, so a paused or unfocused game cannot stall the scenario.
                    float seconds = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                    yield return new WaitForSecondsRealtime(seconds / Mathf.Max(0.1f, Time.timeScale));
                    continue;
                }
                if (verb == "waitready" || verb == "goto")
                {
                    float timeout = verb == "waitready" && a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 240f;
                    float begin = Time.unscaledTime, stableSince = Time.unscaledTime;
                    int lastCount = -1;
                    if (verb == "goto")
                    {
                        string travel = Safe("tp " + (a.Length > 1 ? a[1] : ""));
                        LivingFactions.Core.Settlement target = Commands.FindSettlement(a.Length > 1 ? a[1] : "?");
                        if (target == null)
                        {
                            fail++;
                            File.AppendAllText(ReportFile, "> " + line + "\nFAIL  " + travel + "\n");
                            continue;
                        }
                        Vector3 there = new Vector3(target.Pos.X, 0f, target.Pos.Z);
                        float asked = Time.unscaledTime;
                        while (Utils.DistanceXZ(Player.m_localPlayer.transform.position, there) > 60f && Time.unscaledTime - begin < timeout)
                        {
                            yield return new WaitForSecondsRealtime(0.25f);
                            // The game can refuse a teleport (still loading, mid-animation); ask again.
                            if (Time.unscaledTime - asked > 6f && !Player.m_localPlayer.IsTeleporting())
                            {
                                asked = Time.unscaledTime;
                                Safe("tp " + a[1]);
                            }
                        }
                    }
                    // A teleport only starts on the next frames; give it a moment before testing for arrival.
                    yield return new WaitForSecondsRealtime(1f);
                    while (!Settled(ref lastCount, ref stableSince) && Time.unscaledTime - begin < timeout) yield return new WaitForSecondsRealtime(0.25f);
                    bool ok = Time.unscaledTime - begin < timeout;
                    result = (ok ? "ready" : "TIMEOUT") + " after " + (Time.unscaledTime - begin).ToString("0.0") + " s, " + Materializer.Describe();
                    if (!ok) fail++;
                }
                else if (verb == "gearshots" && a.Length > 1)
                {
                    string name = a.Length > 2 ? a[2] : a[1];
                    result = Safe("gear " + a[1]);
                    if (result.StartsWith("equipped"))
                    {
                        Safe("hidehud on");
                        // Let the equip settle: the model spawns a frame later and capes keep swinging for a moment.
                        yield return new WaitForSecondsRealtime(1.2f);
                        string info = Safe("gearinfo");
                        string slot = GearView.SlotFor(GearView.LastItem);
                        foreach (string angle in GearView.Angles(slot))
                        {
                            Safe("look " + slot + " " + angle);
                            // Turn the side being photographed to the sun, then wait for the turn to show.
                            Safe("face lit");
                            yield return new WaitForSecondsRealtime(0.5f);
                            yield return new WaitForFixedUpdate();
                            for (int i = 0; i < 4; i++) yield return null;
                            result += "\n" + Commands.Run("shot gear_" + name + "_" + angle);
                            yield return null;
                        }
                        if (GearView.IsHandItem(GearView.LastItem))
                        {
                            Safe("sheathe");
                            Safe("look back");
                            Safe("face lit");
                            yield return new WaitForSecondsRealtime(1f);
                            result += "\n" + Commands.Run("shot gear_" + name + "_sheathed");
                            yield return null;
                            Safe("draw");
                        }
                        // The same side view with a ball on every joint: a ball you can see beside a handle is a hand holding air.
                        Safe("marks on");
                        Safe("look " + slot);
                        Safe("face lit");
                        yield return new WaitForSecondsRealtime(0.8f);
                        yield return new WaitForFixedUpdate();
                        for (int i = 0; i < 4; i++) yield return null;
                        result += "\n" + Commands.Run("shot gear_" + name + "_points");
                        yield return null;
                        Safe("marks off");
                        Safe("hidehud off");
                        result += "\n" + info + "\n" + Safe("points " + a[1]);
                        string held;
                        float gap = AttachPoints.HandGap(out held);
                        if (gap >= 0f && GearView.IsHandItem(GearView.LastItem) && held == a[1])
                        {
                            bool inHand = gap <= AttachPoints.FloatingGap;
                            if (inHand) pass++;
                            else fail++;
                            result += "\n" + (inHand ? "PASS" : "FAIL") + "  " + a[1] + " in the hand: surface " + gap.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m from the palm";
                        }
                    }
                    else fail++;
                }
                else if (verb == "until" && line.Contains("::"))
                {
                    // Polls the command in real time until its output has TEXT, for up to 90 s: waiting on what the game shows.
                    int split = line.IndexOf("::", System.StringComparison.Ordinal);
                    string wanted = line.Substring(verb.Length, split - verb.Length).Trim();
                    string command = line.Substring(split + 2).Trim();
                    float begin = Time.unscaledTime;
                    string output = Safe(command);
                    while (output.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) < 0 && Time.unscaledTime - begin < 90f)
                    {
                        yield return new WaitForSecondsRealtime(1f);
                        output = Safe(command);
                    }
                    bool ok = output.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (ok) pass++;
                    else fail++;
                    result = (ok ? "PASS" : "FAIL") + "  until '" + wanted + "' in `" + command + "` after " + (Time.unscaledTime - begin).ToString("0") + " s\n" + Clip(output, ok ? 3000 : 900);
                }
                else if (verb == "shot")
                {
                    for (int i = 0; i < 4; i++) yield return null;
                    result = Commands.Run(line);
                    yield return null;
                }
                else if ((verb == "expect" || verb == "reject") && line.Contains("::"))
                {
                    int split = line.IndexOf("::", StringComparison.Ordinal);
                    string wanted = line.Substring(verb.Length, split - verb.Length).Trim();
                    string command = line.Substring(split + 2).Trim();
                    string output = Safe(command);
                    bool found = output.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool ok = verb == "expect" ? found : !found;
                    if (ok) pass++;
                    else fail++;
                    result = (ok ? "PASS" : "FAIL") + "  " + verb + " '" + wanted + "' in `" + command + "`" + (ok ? "" : "\n" + Clip(verb == "reject" ? string.Join("\n", output.Split('\n').Where(l => l.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0).ToArray()) : output, 900));
                }
                else
                {
                    result = Clip(Safe(line), 6000);
                }
                File.AppendAllText(ReportFile, "> " + line + "\n" + result + "\n");
                // Commands that change the world get a frame to take effect before the next line.
                yield return null;
            }
            int errors = 0;
            try
            {
                string log = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
                using (FileStream fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs))
                {
                    string text = reader.ReadToEnd();
                    errors = Count(text, "Exception") + Count(text, "[Error");
                }
            }
            catch (IOException)
            {
                errors = -1;
            }
            File.AppendAllText(ReportFile, "=== DONE pass " + pass + " fail " + fail + " log-errors " + errors + " in " + (Time.unscaledTime - started).ToString("0") + " s ===\n");
            _running = false;
            if (quitAtEnd) Commands.Run("quit");
        }

        private static int Count(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                n++;
                i += needle.Length;
            }
            return n;
        }

        private static string Safe(string command)
        {
            try
            {
                return Commands.Run(command);
            }
            catch (Exception e)
            {
                return "ERROR " + e;
            }
        }

        private static string Clip(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + " ...";
        }
    }
}
