using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class PlayerToolActivityPatch
{
    private static void Postfix(Humanoid __instance, ItemDrop.ItemData __1)
    {
        if (__instance is Player player && __1 != null)
            PlayerActivityCapture.RecordUsedTool(player, __1);
    }
}