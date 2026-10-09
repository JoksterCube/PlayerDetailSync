using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Minimap), nameof(Minimap.Explore), new[] { typeof(int), typeof(int) })]
internal static class MinimapExploreActivityPatch
{
    private static void Postfix(bool __result)
    {
        Player player = Player.m_localPlayer;
        if (__result && player != null)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.Exploring);
    }
}