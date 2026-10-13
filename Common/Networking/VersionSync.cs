using System;
using System.Collections.Generic;
using JoksterCube.PlayerDetailSync.Api;
using static JoksterCube.PlayerDetailSync.Settings.Constants.Plugin;

namespace JoksterCube.PlayerDetailSync.Common.Networking;

internal static class VersionSync
{
    private const string VersionRpc = "PlayerDetailSync_VersionCheck";
    private const float HandshakeTimeoutSeconds = 15f;

    private static readonly HashSet<ZRpc> VerifiedPeers = [];
    private static readonly Dictionary<ZRpc, float> PendingPeers = [];
    private static bool _waitingForServerVersion;
    private static float _serverHandshakeStartedAt;

    internal static bool ServerVersionMatched { get; private set; }

    internal static bool IsPeerVerified(ZRpc rpc) => VerifiedPeers.Contains(rpc);

    internal static void OnNewConnection(ZNet network, ZNetPeer peer)
    {
        if (network.IsServer())
        {
            PendingPeers[peer.m_rpc] = UnityEngine.Time.realtimeSinceStartup;
        }
        else
        {
            ClientPlayerDataSync.ResetClientSession();
            _waitingForServerVersion = true;
            ServerVersionMatched = false;
            _serverHandshakeStartedAt = UnityEngine.Time.realtimeSinceStartup;
        }

        peer.m_rpc.Register(VersionRpc, new Action<ZRpc, ZPackage>(ReceiveVersion));
        var package = new ZPackage();
        package.Write(ModVersion);
        peer.m_rpc.Invoke(VersionRpc, package);
    }

    internal static void ReceiveVersion(ZRpc rpc, ZPackage package)
    {
        string remoteVersion = package.ReadString();
        ZNet? network = ZNet.instance;
        if (network == null) return;

        bool versionsMatch = string.Equals(remoteVersion, ModVersion, StringComparison.Ordinal);
        Plugin.ModLogger.LogInfo($"Version check, local: {ModVersion}, remote: {remoteVersion}.");

        if (!network.IsServer())
        {
            _waitingForServerVersion = false;
            ServerVersionMatched = versionsMatch;
            if (!versionsMatch)
                Plugin.ModLogger.LogError($"Server requires PlayerDetailSync {remoteVersion}; this client is {ModVersion}.");
            return;
        }

        PendingPeers.Remove(rpc);
        if (versionsMatch)
        {
            VerifiedPeers.Add(rpc);
            Plugin.ModLogger.LogInfo("Peer passed the exact PlayerDetailSync version check.");
            return;
        }

        Plugin.ModLogger.LogWarning($"Disconnecting peer with PlayerDetailSync {remoteVersion}; required version is {ModVersion}.");
        ZNetPeer? peer = network.GetPeer(rpc);
        if (peer != null) network.Disconnect(peer);
    }

    internal static void Update()
    {
        ZNet? network = ZNet.instance;
        if (network == null) return;

        float now = UnityEngine.Time.realtimeSinceStartup;
        if (network.IsServer())
        {
            var peersToDisconnect = new List<ZNetPeer>();
            foreach (ZNetPeer peer in network.GetPeers())
            {
                if (VerifiedPeers.Contains(peer.m_rpc)) continue;
                if (!PendingPeers.TryGetValue(peer.m_rpc, out float startedAt))
                {
                    PendingPeers[peer.m_rpc] = now;
                    continue;
                }

                if (now - startedAt < HandshakeTimeoutSeconds) continue;

                PendingPeers.Remove(peer.m_rpc);
                Plugin.ModLogger.LogWarning("Disconnecting peer that did not complete the PlayerDetailSync version check.");
                peersToDisconnect.Add(peer);
            }

            foreach (ZNetPeer peer in peersToDisconnect)
                network.Disconnect(peer);
            return;
        }

        if (!_waitingForServerVersion || now - _serverHandshakeStartedAt < HandshakeTimeoutSeconds) return;
        _waitingForServerVersion = false;
        Plugin.ModLogger.LogError("The server did not confirm the PlayerDetailSync version; disconnecting.");
        ZNetPeer? serverPeer = network.GetServerPeer();
        if (serverPeer != null) network.Disconnect(serverPeer);
    }

    internal static void OnDisconnect(ZNet network, ZNetPeer peer)
    {
        if (network.IsServer())
        {
            PendingPeers.Remove(peer.m_rpc);
            _ = VerifiedPeers.Remove(peer.m_rpc);
            ClientPlayerDataSync.RemoveReport(peer.m_uid);
            return;
        }

        _waitingForServerVersion = false;
        ServerVersionMatched = false;
        ClientPlayerDataSync.ResetClientSession();
    }
}
