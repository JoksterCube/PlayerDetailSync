using HarmonyLib;

namespace JoksterCube.PlayerDetailSync.Common.Networking;

[HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
internal static class VersionSyncOnDisconnect
{
    private static void Prefix(ZNet __instance, ZNetPeer peer) => VersionSync.OnDisconnect(__instance, peer);
}