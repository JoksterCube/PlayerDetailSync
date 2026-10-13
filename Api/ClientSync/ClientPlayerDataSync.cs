using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using JoksterCube.PlayerDetailSync.Common.Networking;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class ClientPlayerDataSync
{
    private const string RpcName = "JoksterCube.PlayerDetailSync.ClientPlayerData";
    private const string RefreshRpcName = "JoksterCube.PlayerDetailSync.RequestClientData";
    private const string AcceptedRpcName = "JoksterCube.PlayerDetailSync.ClientDataAccepted";
    private const int MaxPayloadLength = 262144;
    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly Func<ZRoutedRpc, long> GetServerPeerId =
        AccessTools.MethodDelegate<Func<ZRoutedRpc, long>>(
            AccessTools.Method(typeof(ZRoutedRpc), "GetServerPeerID"));
    private static readonly Func<ZRoutedRpc, long, ZNetPeer> GetRoutedPeer =
        AccessTools.MethodDelegate<Func<ZRoutedRpc, long, ZNetPeer>>(
            AccessTools.Method(typeof(ZRoutedRpc), "GetPeer"));
    private static readonly Dictionary<long, ReportedPlayerData> Reports = new();
    private static readonly ClientReportSchedule ReportSchedule = new();

    private static ZRoutedRpc? _registeredRpc;
    private static bool _loggedFirstSend;
    private static bool _loggedFirstReceive;
    private static bool _loggedFirstDataRpc;
    private static float _nextSendDiagnosticTime;
    private static Player? _lastLocalPlayer;
    private static bool _reportsChanged;
    private static readonly Dictionary<long, ClientRefreshRetry> RefreshRetries = new();
    private static DateTime _nextAcceptedReportDiagnosticUtc;
    private static DateTime _nextRejectedReportDiagnosticUtc;

    internal static long GetPlayerId(ZNetPeer peer) =>
        Reports.TryGetValue(peer.m_uid, out ReportedPlayerData? report) && report.Snapshot.CharacterPlayerId > 0
            ? report.Snapshot.CharacterPlayerId
            : peer.m_playerID != 0 ? peer.m_playerID : peer.m_uid;

    internal static bool ConsumeReportsChanged()
    {
        bool changed = _reportsChanged;
        _reportsChanged = false;
        return changed;
    }

    internal static void ResetClientSession()
    {
        ReportSchedule.Reset();
        _lastLocalPlayer = null;
        _loggedFirstSend = false;
        _nextSendDiagnosticTime = 0f;
    }

    internal static void Update(float refreshInterval)
    {
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (network == null || routedRpc == null) return;

        if (!ReferenceEquals(_registeredRpc, routedRpc))
        {
            ResetClientSession();
            if (network.IsServer())
            {
                routedRpc.Register<string>(RpcName, ReceiveClientData);
            }
            else
            {
                routedRpc.Register(RefreshRpcName, ReceiveRefreshRequest);
                routedRpc.Register(AcceptedRpcName, ReceiveReportAccepted);
            }
            _registeredRpc = routedRpc;
        }

        if (network.IsServer()) return;
        if (!VersionSync.ServerVersionMatched) return;

        Player? localPlayer = Player.m_localPlayer;
        if (!ReferenceEquals(localPlayer, _lastLocalPlayer))
        {
            ResetClientSession();
            _lastLocalPlayer = localPlayer;
        }
        float now = Time.realtimeSinceStartup;
        if (!ReportSchedule.IsDue(now)) return;

        if (localPlayer == null || localPlayer.GetPlayerID() <= 0 || network.GetServerPeer() == null) return;

        SendClientReport(routedRpc, localPlayer, now, refreshInterval);
    }

    private static void ReceiveReportAccepted(long sender)
    {
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (network == null || network.IsServer() || routedRpc == null ||
            !VersionSync.ServerVersionMatched || sender != GetServerPeerId(routedRpc)) return;

        ReportSchedule.Accept();
    }

    internal static bool RequestClientRefresh(long playerId)
    {
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (network == null || !network.IsServer() || routedRpc == null) return false;

        foreach (ZNetPeer peer in network.GetPeers())
        {
            if (GetPlayerId(peer) != playerId) continue;
            if (!VersionSync.IsPeerVerified(peer.m_rpc)) return false;

            float now = Time.realtimeSinceStartup;
            if (!RefreshRetries.TryGetValue(peer.m_uid, out ClientRefreshRetry? retry))
            {
                retry = new ClientRefreshRetry();
                RefreshRetries[peer.m_uid] = retry;
            }
            if (!retry.TrySchedule(now)) return false;

            routedRpc.InvokeRoutedRPC(peer.m_uid, RefreshRpcName);
            Plugin.ModLogger.LogDebug("Requested missing character data from a verified client.");
            return true;
        }

        return false;
    }

    private static void ReceiveRefreshRequest(long sender)
    {
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        Player? localPlayer = Player.m_localPlayer;
        if (network == null || network.IsServer() || routedRpc == null || localPlayer == null ||
            !VersionSync.ServerVersionMatched || sender != GetServerPeerId(routedRpc))
        {
            return;
        }

        Plugin.ModLogger.LogDebug("Client received an automatic character-data refresh request.");
        SendClientReport(routedRpc, localPlayer, Time.realtimeSinceStartup, 0f);
    }

    private static void SendClientReport(ZRoutedRpc routedRpc, Player localPlayer, float now, float refreshInterval)
    {
        if (!ReportSchedule.CanSend(now)) return;
        ReportSchedule.RecordSend(now, refreshInterval);
        PlayerDataSnapshot snapshot = PlayerDataSnapshot.Capture(localPlayer);
        string payload = Serialize(snapshot);
        if (payload.Length > MaxPayloadLength) return;

        routedRpc.InvokeRoutedRPC(GetServerPeerId(routedRpc), RpcName, payload);
        if (!_loggedFirstSend || now >= _nextSendDiagnosticTime)
        {
            Plugin.ModLogger.LogDebug("Sent client character-data report to the server.");
            _loggedFirstSend = true;
            _nextSendDiagnosticTime = now + 10f;
        }
    }

    internal static PlayerDataSnapshot? GetLatest(long playerId)
    {
        if (!Reports.TryGetValue(playerId, out ReportedPlayerData? report)) return null;

        float elapsedSeconds = (float)(DateTime.UtcNow - report.ReceivedAtUtc).TotalSeconds;
        return report.Snapshot.WithElapsedSeconds(Math.Max(0f, elapsedSeconds));
    }

    internal static void RemoveReport(long peerId)
    {
        Reports.Remove(peerId);
        RefreshRetries.Remove(peerId);
        _reportsChanged = true;
    }

    private static void ReceiveClientData(long sender, string payload)
    {
        ZNet? network = ZNet.instance;
        ZRoutedRpc? routedRpc = ZRoutedRpc.instance;
        if (network == null || !network.IsServer() || routedRpc == null ||
            string.IsNullOrEmpty(payload) || payload.Length > MaxPayloadLength)
        {
            return;
        }

        if (!_loggedFirstDataRpc)
        {
            Plugin.ModLogger.LogInfo("Received a client player-data RPC.");
            _loggedFirstDataRpc = true;
        }

        ZNetPeer? peer = GetRoutedPeer(routedRpc, sender);
        if (peer == null)
        {
            Plugin.ModLogger.LogWarning($"Could not map client player-data sender {sender} to a Valheim peer.");
            return;
        }
        if (!VersionSync.IsPeerVerified(peer.m_rpc))
        {
            Plugin.ModLogger.LogWarning("Ignored player-data report from a peer that has not passed version verification.");
            return;
        }
        long playerId = GetPlayerId(peer);
        if (playerId == 0) return;

        try
        {
            PlayerDataSnapshot? snapshot = Deserialize(payload);
            string? validationError = PlayerReportValidator.GetError(snapshot);
            if (validationError != null)
            {
                if (DateTime.UtcNow >= _nextRejectedReportDiagnosticUtc)
                {
                    Plugin.ModLogger.LogWarning($"Rejected client character-data report: {validationError}");
                    _nextRejectedReportDiagnosticUtc = DateTime.UtcNow.AddSeconds(10);
                }
                return;
            }

            Reports[peer.m_uid] = new ReportedPlayerData(snapshot!, DateTime.UtcNow);
            RefreshRetries.Remove(peer.m_uid);
            _reportsChanged = true;
            routedRpc.InvokeRoutedRPC(sender, AcceptedRpcName);
            if (!_loggedFirstReceive || DateTime.UtcNow >= _nextAcceptedReportDiagnosticUtc)
            {
                Plugin.ModLogger.LogDebug("Accepted client character-data report.");
                _loggedFirstReceive = true;
                _nextAcceptedReportDiagnosticUtc = DateTime.UtcNow.AddSeconds(10);
            }
        }
        catch (Exception exception)
        {
            if (DateTime.UtcNow >= _nextRejectedReportDiagnosticUtc)
            {
                Plugin.ModLogger.LogWarning($"Could not decode client player-data report: {exception.GetType().Name}");
                _nextRejectedReportDiagnosticUtc = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }

    private static string Serialize(PlayerDataSnapshot snapshot)
    {
        var serializer = new DataContractJsonSerializer(typeof(PlayerDataSnapshot));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, snapshot);
        return Utf8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static PlayerDataSnapshot? Deserialize(string payload)
    {
        var serializer = new DataContractJsonSerializer(typeof(PlayerDataSnapshot));
        using var stream = new MemoryStream(Utf8.GetBytes(payload));
        PlayerDataSnapshot? snapshot = serializer.ReadObject(stream) as PlayerDataSnapshot;
        PlayerReportValidator.FilterInvalidPrivateKeys(snapshot);
        return snapshot;
    }

}
