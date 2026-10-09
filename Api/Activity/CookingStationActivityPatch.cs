using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
internal static class CookingStationActivityPatch
{
    private static void Postfix(Humanoid __0, bool __result)
    {
        if (__result && __0 is Player player)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.Cooking);
    }
}