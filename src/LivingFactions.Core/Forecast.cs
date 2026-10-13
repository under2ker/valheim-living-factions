using System;
using System.Collections.Generic;

namespace LivingFactions.Core
{
    /// <summary>
    /// Reads the simulation's state back as plain sentences: what a settlement is busy with, what
    /// troubles it and what is coming next. Nothing here changes the world; it only describes it,
    /// so the same code feeds the HUD, the dialog and the tests.
    /// </summary>
    public static class Forecast
    {
        public enum Mood { Plain, Good, Trouble }

        public struct Line
        {
            public string Text;
            public Mood Mood;

            public Line(string text, Mood mood) { Text = text; Mood = mood; }
        }

        /// <summary>One of several wordings of the same thing, changing from day to day and place to place.</summary>
        private static string Vary(string key, int count, double day, int salt, params object[] args)
        {
            int pick = (int)(((long)Math.Floor(day) * 7 + salt * 13) % count);
            if (pick < 0) pick += count;
            return Texts.Get(key + pick, args);
        }

        private static Faction Enemy(Simulation sim, Faction f)
        {
            foreach (Faction other in sim.State.Factions)
            {
                if (other != f && !other.Eliminated && sim.AreAtWar(f.Id, other.Id)) return other;
            }
            return null;
        }

        /// <summary>Who lives there, what they are doing, what is wrong and what comes next.</summary>
        public static List<Line> ForSettlement(Simulation sim, Settlement s)
        {
            List<Line> lines = new List<Line>();
            double day = sim.State.Day;
            Faction f = sim.State.GetFaction(s.FactionId);
            if (s.Destroyed)
            {
                lines.Add(new Line(Texts.Get("fc_ruin", Texts.Name(s.Name)), Mood.Trouble));
                return lines;
            }
            lines.Add(new Line(Texts.Get("fc_head", Texts.Name(s.Name), Texts.Get("tier_" + s.Tier), Math.Round(s.Population), Math.Round(s.Soldiers)), Mood.Plain));

            // What the place is busy with.
            Building project = Simulation.CurrentProject(s);
            if (project != null)
            {
                float work = Defs.Buildings[project.Kind].Work;
                int percent = work > 0f ? (int)Math.Round(100f * project.Work / work) : 100;
                lines.Add(new Line(Vary("fc_build", 3, day, s.Id, Texts.Get("b_" + project.Kind), Math.Max(0, Math.Min(99, percent))), Mood.Plain));
            }
            else if (s.Stock[(int)Res.Wood] < 30f || s.Stock[(int)Res.Stone] < 15f) lines.Add(new Line(Vary("fc_gather", 2, day, s.Id), Mood.Plain));
            else lines.Add(new Line(Vary("fc_quiet", 4, day, s.Id), Mood.Plain));

            // What is wrong: the worst two troubles.
            List<Line> troubles = new List<Line>();
            float foodDays = s.Stock[(int)Res.Food] / Math.Max(0.1f, s.Population);
            if (s.HungerDays > 0.5f) troubles.Add(new Line(Texts.Get("fc_starving"), Mood.Trouble));
            else if (foodDays < 3f) troubles.Add(new Line(Texts.Get("fc_food_low"), Mood.Trouble));
            if (day - s.LastAttackedDay < 3) troubles.Add(new Line(Vary("fc_raided", 2, day, s.Id), Mood.Trouble));
            Faction enemy = f != null ? Enemy(sim, f) : null;
            if (enemy != null) troubles.Add(new Line(Texts.Get("fc_war", Texts.FactionName(enemy)), Mood.Trouble));
            float housing = Math.Min(Simulation.Housing(s), Defs.PopCapByTier[(int)s.Tier]);
            if (s.Population >= housing - 0.5f) troubles.Add(new Line(Texts.Get("fc_crowded"), Mood.Trouble));
            if (s.Soldiers < 1f && s.Population >= 4f) troubles.Add(new Line(Texts.Get("fc_unguarded"), Mood.Trouble));
            if (project != null && s.Stock[(int)Res.Wood] < 8f) troubles.Add(new Line(Texts.Get("fc_no_wood"), Mood.Trouble));
            for (int i = 0; i < troubles.Count && i < 2; i++) lines.Add(troubles[i]);

            // What comes next.
            if (s.Tier < Tier.City && Defs.TierRequirementsMet(s, s.Tier + 1)) lines.Add(new Line(Texts.Get("fc_grows", Texts.Get("tier_" + (s.Tier + 1))), Mood.Good));
            else if (troubles.Count == 0 && s.Stock[(int)Res.Coins] > 300f) lines.Add(new Line(Texts.Get("fc_rich"), Mood.Good));
            return lines;
        }

        /// <summary>A faction in one line: its size and the one thing that matters most about it today.</summary>
        public static Line ForFaction(Simulation sim, Faction f)
        {
            double day = sim.State.Day;
            int settlements = 0;
            float people = 0f;
            Settlement hungry = null, raided = null, building = null, growing = null;
            foreach (Settlement s in sim.State.Settlements)
            {
                if (s.FactionId != f.Id || s.Destroyed) continue;
                settlements++;
                people += s.Population;
                if (s.HungerDays > 0.5f) hungry = s;
                if (day - s.LastAttackedDay < 3) raided = s;
                if (Simulation.CurrentProject(s) != null) building = s;
                if (s.Tier < Tier.City && Defs.TierRequirementsMet(s, s.Tier + 1)) growing = s;
            }
            string head = Texts.Get("fc_faction", Texts.FactionName(f), settlements, Math.Round(people));
            Faction enemy = Enemy(sim, f);
            if (settlements == 0) return new Line(Texts.Get("fc_homeless", Texts.FactionName(f)), Mood.Trouble);
            if (enemy != null) return new Line(head + Texts.Get("fc_f_war", Texts.FactionName(enemy)), Mood.Trouble);
            if (hungry != null) return new Line(head + Texts.Get("fc_f_hunger", Texts.Name(hungry.Name)), Mood.Trouble);
            if (raided != null) return new Line(head + Texts.Get("fc_f_raided", Texts.Name(raided.Name)), Mood.Trouble);
            if (growing != null) return new Line(head + Texts.Get("fc_f_grows", Texts.Name(growing.Name), Texts.Get("tier_" + (growing.Tier + 1))), Mood.Good);
            if (building != null) return new Line(head + Texts.Get("fc_f_builds", Texts.Name(building.Name), Texts.Get("b_" + Simulation.CurrentProject(building).Kind)), Mood.Plain);
            return new Line(head + Vary("fc_f_quiet", 3, day, f.Id), Mood.Plain);
        }
    }
}
