using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
internal static class OfferingBowlActivityPatch
{
    private static void Postfix(Humanoid __0, bool __result)
    {
        if (__result && __0 is Player player)
            PlayerActivityCapture.RecordAction(player, PlayerActivityStatus.FightingBoss);
    }
}