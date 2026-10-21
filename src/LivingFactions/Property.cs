using HarmonyLib;
using LivingFactions.Core;
using UnityEngine;

namespace LivingFactions
{
    /// <summary>
    /// What a settlement puts in the world is its own, not a free store for the player: its fields
    /// cannot be harvested nor its hives emptied, its pieces cannot be taken down with the hammer, and a
    /// piece broken in a fight or a raid leaves no building materials behind. (Its chests can still be
    /// robbed, at a cost in standing: Chests.cs.)
    /// </summary>
    internal static class Property
    {
        public static Settlement OwnerOf(Component c)
        {
            if (!SimHost.Active || c == null) return null;
            ZNetView view = c.GetComponentInParent<ZNetView>();
            if (view == null || !view.IsValid()) return null;
            int sid = view.GetZDO().GetInt(Materializer.HashSettlement);
            return sid != 0 ? SimHost.Sim.State.GetSettlement(sid) : null;
        }

        public static void Refuse(Humanoid who, Settlement s)
        {
            Player player = who as Player;
            if (player == null || player != Player.m_localPlayer) return;
            Faction f = SimHost.Sim.State.GetFaction(s.FactionId);
            player.Message(MessageHud.MessageType.Center, Texts.Get("msg_not_yours", Texts.FactionName(f)));
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class FieldPatch
    {
        private static bool Prefix(Pickable __instance, Humanoid character, ref bool __result)
        {
            Settlement s = Property.OwnerOf(__instance);
            if (s == null || s.Destroyed) return true;
            Property.Refuse(character, s);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
    internal static class HivePatch
    {
        private static bool Prefix(Beehive __instance, Humanoid character, ref bool __result)
        {
            Settlement s = Property.OwnerOf(__instance);
            if (s == null || s.Destroyed) return true;
            Property.Refuse(character, s);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "CheckCanRemovePiece")]
    internal static class DeconstructPatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            Settlement s = Property.OwnerOf(piece);
            if (s == null) return true;
            Property.Refuse(__instance, s);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    internal static class NoMaterialsPatch
    {
        private static bool Prefix(Piece __instance)
        {
            return Property.OwnerOf(__instance) == null;
        }
    }

    [HarmonyPatch(typeof(DropOnDestroyed), "OnDestroyed")]
    internal static class NoDropsPatch
    {
        private static bool Prefix(DropOnDestroyed __instance)
        {
            return Property.OwnerOf(__instance) == null;
        }
    }
}
