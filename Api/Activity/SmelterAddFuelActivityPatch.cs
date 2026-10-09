using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
internal static class SmelterAddFuelActivityPatch
{
    private static void Postfix(Humanoid __1, bool __result)
    {
        if (__result && __1 is Player player)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.Smelting);
    }
}