using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Interact))]
internal static class FermenterActivityPatch
{
    private static void Postfix(Humanoid __0, bool __result)
    {
        if (__result && __0 is Player player)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.Brewing);
    }
}