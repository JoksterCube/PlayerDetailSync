using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using JoksterCube.PlayerDetailSync.Common.Networking;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

// Boss deaths and key grants happen on clients; each credited client reports itself to the server.
internal static class KillCreditSync
{
    private const string BossKeyRpcName = "JoksterCube.PlayerDetailSync.BossKeyReceived";
    private const string MinibossRpcName = "JoksterCube.PlayerDetailSync.MinibossKilled";
    private const string PlayerDeathRpcName = "JoksterCube.PlayerDetailSync.PlayerDied";
    private const string TargetDeathRpcName = "JoksterCube.PlayerDetailSync.TargetDied";
    private const int MaxValueLength = 128;
    internal const string ProgressionGuid = "com.orianaventure.mod.WorldAdvancementProgression";

    internal static bool ProgressionInstalled => Chainloader.PluginInfos.ContainsKey(ProgressionGuid);

    private static readonly Func<ZRoutedRpc, long> GetServerPeerId =
        AccessTools.MethodDelegate<Func<ZRoutedRpc, long>>(
            AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID"));
    private static readonly Func<ZRoutedRpc, long, ZNetPeer> GetRoutedPeer =
        AccessTools.MethodDelegate<Func<ZRoutedRpc, long, ZNetPeer>>(
            AccessTools.Method(typeof(ZRoutedRpc), "GetPeer"));

    private static ZRoutedRpc? _registeredRpc;

    internal static void EnsureRegistered()
    {
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || ReferenceEquals(_registeredRpc, routedRpc)) return;
        routedRpc.Register<string, bool>(BossKeyRpcName, ReceiveBossKey);
        routedRpc.Register<string>(MinibossRpcName, ReceiveMinibossKill);
        routedRpc.Register<long, int, string, Vector3>(PlayerDeathRpcName, ReceivePlayerDeath);
        routedRpc.Register<string, Vector3>(TargetDeathRpcName, ReceiveTargetDeath);
        _registeredRpc = routedRpc;
    }

    internal static void ReportBossKey(string key, bool firstTime)
    {
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (!CanReport(routedRpc) || string.IsNullOrEmpty(key) || key.Length > MaxValueLength) return;
        routedRpc!.InvokeRoutedRPC(GetServerPeerId(routedRpc), BossKeyRpcName, key, firstTime);
    }

    internal static void ReportPlayerDeath(long killerPlayerId, HitData.HitType hitType, string killerToken,
        Vector3 position)
    {
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (!CanReport(routedRpc)) return;
        if (killerToken.Length > MaxValueLength) killerToken = "";
        routedRpc!.InvokeRoutedRPC(GetServerPeerId(routedRpc), PlayerDeathRpcName,
            killerPlayerId, (int)hitType, killerToken, position);
    }

    internal static void ReportTargetDeath(string targetId, Vector3 position)
    {
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (!CanReport(routedRpc) || string.IsNullOrEmpty(targetId) || targetId.Length > MaxValueLength) return;
        routedRpc!.InvokeRoutedRPC(GetServerPeerId(routedRpc), TargetDeathRpcName, targetId, position);
    }

    internal static void ReportMinibossKill(string enemyName)
    {
        if (WebhookNotifications.IsMinibossName(enemyName)) Report(MinibossRpcName, enemyName);
    }

    private static void Report(string rpcName, string value)
    {
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (!CanReport(routedRpc) || string.IsNullOrEmpty(value) || value.Length > MaxValueLength) return;
        routedRpc!.InvokeRoutedRPC(GetServerPeerId(routedRpc), rpcName, value);
    }

    private static bool CanReport(ZRoutedRpc? routedRpc)
    {
        ZNet? network = ZNet.instance;
        if (network == null || routedRpc == null || Player.m_localPlayer == null) return false;
        if (!network.IsServer() && !VersionSync.ServerVersionMatched) return false;
        EnsureRegistered();
        return true;
    }

    private static void ReceiveBossKey(long sender, string key, bool firstTime)
    {
        if (TryGetSender(sender, out string playerId, out string playerName, out Vector3? position))
            WebhookNotifications.BossKeyReceived(playerId, playerName, position, key, firstTime);
    }

    private static void ReceiveMinibossKill(long sender, string enemyName)
    {
        if (TryGetSender(sender, out string playerId, out string playerName, out Vector3? position))
            WebhookNotifications.MinibossKilled(playerId, playerName, position, enemyName);
    }

    private static void ReceiveTargetDeath(long sender, string targetId, Vector3 position)
    {
        if (TryGetSender(sender, out _, out _, out _))
            WebhookNotifications.TargetDied(targetId, position);
    }

    private static void ReceivePlayerDeath(long sender, long killerPlayerId, int hitType, string killerToken,
        Vector3 position)
    {
        if (!TryGetSender(sender, out string playerId, out string playerName, out _)) return;
        if (hitType is < 0 or > byte.MaxValue || !Enum.IsDefined(typeof(HitData.HitType), (byte)hitType))
            hitType = (int)HitData.HitType.Undefined;

        string? killerId = null;
        string? killerName = null;
        if (killerPlayerId != 0 && TryFindPlayer(killerPlayerId, out string foundName))
        {
            killerId = killerPlayerId.ToString();
            killerName = foundName;
        }
        else if (killerToken.Length <= MaxValueLength)
        {
            killerName = WebhookNotifications.LocalizeCreatureName(killerToken);
        }
        WebhookNotifications.PlayerDied(playerId, playerName, position, (HitData.HitType)hitType, killerId, killerName);
    }

    // Only credit killers who are actually connected, so clients cannot name arbitrary players.
    private static bool TryFindPlayer(long playerId, out string playerName)
    {
        playerName = "";
        foreach (ZNetPeer peer in ZNet.instance.GetPeers())
        {
            if (ClientPlayerDataSync.GetPlayerId(peer) != playerId) continue;
            playerName = peer.m_playerName;
            return true;
        }

        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null || localPlayer.GetPlayerID() != playerId) return false;
        playerName = localPlayer.GetPlayerName();
        return true;
    }

    private static bool TryGetSender(long sender, out string playerId, out string playerName, out Vector3? position)
    {
        playerId = "";
        playerName = "";
        position = null;
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (network == null || !network.IsServer() || routedRpc == null) return false;

        if (sender == GetServerPeerId(routedRpc))
        {
            Player? localPlayer = Player.m_localPlayer;
            if (localPlayer == null) return false;
            playerId = localPlayer.GetPlayerID().ToString();
            playerName = localPlayer.GetPlayerName();
            position = localPlayer.transform.position;
            return true;
        }

        ZNetPeer? peer = GetRoutedPeer(routedRpc, sender);
        if (peer == null || !VersionSync.IsPeerVerified(peer.m_rpc)) return false;
        playerId = ClientPlayerDataSync.GetPlayerId(peer).ToString();
        playerName = peer.m_playerName;
        position = WebhookNotifications.GetPeerPosition(peer);
        return true;
    }
}
