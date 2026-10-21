using LivingFactions.Core;

namespace LivingFactions
{
    /// <summary>
    /// A faction does not turn on the player in one step: as standing falls the player is told when it
    /// stops trusting them, when the alliance is about to end, and when it is about to come to war.
    /// </summary>
    internal static class Warnings
    {
        public const float AllianceWarnBelow = 50f;
        public const float WarWarnBelow = -60f;

        /// <summary>Called on the offender's machine after standing has been taken off.</summary>
        public static void AfterOffence(Faction f, PlayerRelation rel, float before, bool wasAllied)
        {
            if (MessageHud.instance == null) return;
            float now = rel.Reputation;
            string name = Texts.FactionName(f);
            if (wasAllied && rel.Allied && before >= AllianceWarnBelow && now < AllianceWarnBelow)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("warn_alliance", name));
            else if (!rel.AtWar && before > WarWarnBelow && now <= WarWarnBelow)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Texts.Get("warn_war", name));
            else if (before > Simulation.WaryBelow && now <= Simulation.WaryBelow)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, Texts.Get("warn_trust", name));
        }
    }
}
