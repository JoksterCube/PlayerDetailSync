using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

[HarmonyPatch(typeof(Player), "OnSpawned")]
internal static class PlayerSpawnWebhookPatch
{
    private static void Postfix(Player __instance) => PlayerActivityCapture.OnPlayerSpawned(__instance);
}