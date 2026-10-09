using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Common.Networking;

[HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
internal static class VersionSyncOnConnection
{
    private static void Prefix(ZNet __instance, ZNetPeer peer) => VersionSync.OnNewConnection(__instance, peer);
}