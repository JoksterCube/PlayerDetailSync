using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

// Valheim registers the kill on every player who damaged the creature, including the owner.
[HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
internal static class MinibossKillCreditPatch
{
    private static void Postfix(string enemyName) => KillCreditSync.ReportMinibossKill(enemyName);
}
