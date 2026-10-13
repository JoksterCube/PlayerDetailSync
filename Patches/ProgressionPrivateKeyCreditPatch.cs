using System.Reflection;
using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Api;

// World Advancement Progression sends private boss keys only to players near the boss.
[HarmonyPatch]
internal static class ProgressionPrivateKeyCreditPatch
{
    private static MethodInfo? _hasPrivateKey;

    private static MethodBase? TargetMethod()
    {
        System.Type? keyManager = AccessTools.TypeByName("VentureValheim.Progression.KeyManager");
        if (keyManager == null) return null;
        _hasPrivateKey = AccessTools.Method(keyManager, "HasPrivateKey", [typeof(string)]);
        return AccessTools.Method(keyManager, "RPC_SetPrivateKey");
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(object __instance, string key, out bool __state) =>
        __state = _hasPrivateKey != null && (bool)_hasPrivateKey.Invoke(__instance, [key.ToLowerInvariant()]);

    private static void Postfix(string key, bool __state) => KillCreditSync.ReportBossKey(key, !__state);
}
