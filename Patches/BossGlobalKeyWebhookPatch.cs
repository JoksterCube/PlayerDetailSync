using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

// World Advancement Progression skips the original when it blocks the global key.
[HarmonyPatch(typeof(ZoneSystem), "RPC_SetGlobalKey")]
internal static class BossGlobalKeyWebhookPatch
{
    private static void Prefix(string name, out bool __state) =>
        __state = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(name);

    private static void Postfix(string name, bool __runOriginal, bool __state)
    {
        if (__runOriginal) WebhookNotifications.BossGlobalKeySet(name, !__state);
    }
}
