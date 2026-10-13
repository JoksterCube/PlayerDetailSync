using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
internal static class CraftingActivityPatch
{
    private static void Postfix()
    {
        Player player = Player.m_localPlayer;
        if (player != null)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.Crafting);
    }
}