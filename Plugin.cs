using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JoksterCube.PlayerDetailSync.Api;
using JoksterCube.PlayerDetailSync.Common;
using JoksterCube.PlayerDetailSync.Common.Networking;
using JoksterCube.PlayerDetailSync.Settings;
using ServerSync;
using static JoksterCube.PlayerDetailSync.Settings.Constants.Plugin;

namespace JoksterCube.PlayerDetailSync;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInDependency(KillCreditSync.ProgressionGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

    private static readonly ConfigSync ConfigSync = new(ModGUID)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };

    private readonly Harmony _harmony = new(ModGUID);

    private readonly string _configFileFullPath = Path.Combine(Paths.ConfigPath, ConfigFileName);

    private HttpApiServer? _apiServer;
    private FileSystemWatcher? _configWatcher;
    private bool _apiStartAttempted;
    private bool _missingPasswordWarningLogged;
    private bool _waitingForZNetLogged;
    private bool _snapshotCacheLoaded;
    private readonly HashSet<long> _connectedPeerIds = new();
    private readonly Dictionary<long, string> _connectedPeerNames = new();
    private readonly Dictionary<long, string> _connectedPlayerIds = new();
    private readonly Dictionary<long, UnityEngine.Vector3> _connectedPeerPositions = new();
    private readonly Dictionary<long, float> _pendingLogins = new();
    private const float LoginPositionTimeoutSeconds = 60f;
    private float _nextConnectionCheckTime;
    private bool _snapshotDirty;
    private float _snapshotDirtySince = -1f;
    private float _nextSnapshotTime;
    private bool? _configIsClient;

    internal static bool InitialConfigSyncDone => ConfigSync.InitialSyncDone;

    private void Awake()
    {
        PluginConfig.Build(Config, ConfigSync);
        _harmony.PatchAll();
        SetupWatcher();
    }

    private void Update()
    {
        if (ZNet.instance == null)
        {
            UpdateConfigVisibility(false);
            StopApiServer();
            if (!_waitingForZNetLogged)
            {
                ModLogger.LogInfo("Waiting for the Valheim network instance before starting the HTTP API.");
                _waitingForZNetLogged = true;
            }
            return;
        }
        _waitingForZNetLogged = false;
        UpdateConfigVisibility(!ZNet.instance.IsServer());

        VersionSync.Update();
        KillCreditSync.EnsureRegistered();
        if (!InitialConfigSyncDone) return;

        if (ZNet.instance.IsServer())
        {
            UpdateConnectedPeers();
            WebhookNotifications.UpdateRaidState();
            WebhookNotifications.EnsureBossNames();
        }

        if (!PluginConfig.ApiEnabled.Value.IsOn())
        {
            StopApiServer();
            return;
        }

        ClientPlayerDataSync.Update(PluginConfig.RefreshInterval.Value);

        if (!ZNet.instance.IsServer())
        {
            StopApiServer();
            return;
        }

        if (!_snapshotCacheLoaded)
        {
            PlayerSnapshotStore.Load(Paths.ConfigPath,
                message => ModLogger.LogInfo(message), message => ModLogger.LogWarning(message));
            _snapshotCacheLoaded = true;
        }

        if (string.IsNullOrWhiteSpace(PluginConfig.Password.Value))
        {
            StopApiServer();
            if (!_missingPasswordWarningLogged)
            {
                ModLogger.LogWarning("The HTTP API is disabled until a non-empty Password is set in the config.");
                _missingPasswordWarningLogged = true;
            }
            return;
        }
        _missingPasswordWarningLogged = false;

        if (!_apiStartAttempted)
        {
            _apiStartAttempted = true;
            _apiServer = new HttpApiServer(
                PluginConfig.BindAddress.Value,
                PluginConfig.Port.Value,
                PluginConfig.Password.Value);
            if (!_apiServer.Start())
            {
                _apiServer = null;
                return;
            }

            _apiServer.Publish(PlayerSnapshotStore.Current);
            ModLogger.LogInfo($"Player data API listening on {PluginConfig.BindAddress.Value}:{PluginConfig.Port.Value}.");
        }

        if (_apiServer == null) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (ClientPlayerDataSync.ConsumeReportsChanged() && !_snapshotDirty)
        {
            _snapshotDirty = true;
            _snapshotDirtySince = now;
        }
        if (_snapshotDirty && _snapshotDirtySince >= 0f && now - _snapshotDirtySince < 0.25f) return;
        if (!_snapshotDirty && now < _nextSnapshotTime) return;
        _snapshotDirty = false;
        _snapshotDirtySince = -1f;
        _nextSnapshotTime = now + UnityEngine.Mathf.Clamp(PluginConfig.RefreshInterval.Value, 1f, 60f);
        _apiServer.Publish(PlayerSnapshotStore.MergeAndSave(PlayerSnapshot.Capture()));
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
        _configWatcher?.Dispose();
        StopApiServer();
    }

    private void UpdateConfigVisibility(bool isClient)
    {
        if (_configIsClient == isClient) return;
        _configIsClient = isClient;
        PluginConfig.SetClientVisibility(isClient);
    }

    private void SetupWatcher()
    {
        var watcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName)
        {
            IncludeSubdirectories = false,
            SynchronizingObject = ThreadingHelper.SynchronizingObject
        };
        watcher.Changed += ReadConfigValues;
        watcher.Created += ReadConfigValues;
        watcher.Renamed += ReadConfigValues;
        _configWatcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        if (!File.Exists(_configFileFullPath)) return;

        string previousBindAddress = PluginConfig.BindAddress.Value;
        int previousPort = PluginConfig.Port.Value;
        string previousPassword = PluginConfig.Password.Value;

        try
        {
            ModLogger.LogDebug($"Reloading configuration after {e.ChangeType}.");
            Config.Reload();

            if (previousBindAddress != PluginConfig.BindAddress.Value ||
                previousPort != PluginConfig.Port.Value ||
                previousPassword != PluginConfig.Password.Value)
            {
                StopApiServer();
            }
        }
        catch (Exception exception)
        {
            ModLogger.LogError($"Could not reload {ConfigFileName}: {exception.Message}");
        }
    }

    private void StopApiServer()
    {
        _apiServer?.Dispose();
        _apiServer = null;
        _apiStartAttempted = false;
        _nextSnapshotTime = 0f;
    }

    private void UpdateConnectedPeers()
    {
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextConnectionCheckTime) return;
        _nextConnectionCheckTime = now + 1f;

        var currentPeerIds = new HashSet<long>();
        var currentPeerNames = new Dictionary<long, string>();
        var currentPlayerIds = new Dictionary<long, string>();
        foreach (ZNetPeer peer in ZNet.instance!.GetPeers())
        {
            if (string.IsNullOrWhiteSpace(peer.m_playerName)) continue;
            currentPeerIds.Add(peer.m_uid);
            currentPeerNames[peer.m_uid] = peer.m_playerName;
            string playerId = ClientPlayerDataSync.GetPlayerId(peer).ToString();
            currentPlayerIds[peer.m_uid] = playerId;
            bool joined = !_connectedPeerIds.Contains(peer.m_uid);
            if (joined) _pendingLogins[peer.m_uid] = now;
            UnityEngine.Vector3? position = WebhookNotifications.GetPeerPosition(peer);
            if (position != null) _connectedPeerPositions[peer.m_uid] = position.Value;
            // Wait for the character to spawn so the login reports where the player entered the world.
            if (_pendingLogins.TryGetValue(peer.m_uid, out float joinedAt) &&
                (position != null || now - joinedAt >= LoginPositionTimeoutSeconds))
            {
                WebhookNotifications.PlayerConnected(playerId, peer.m_playerName, position);
                _pendingLogins.Remove(peer.m_uid);
            }

            if (!PluginConfig.ApiEnabled.Value.IsOn()) continue;
            bool missing = ClientPlayerDataSync.GetLatest(peer.m_uid) == null;
            if (joined || missing)
            {
                bool requested = ClientPlayerDataSync.RequestClientRefresh(ClientPlayerDataSync.GetPlayerId(peer));
                _snapshotDirty |= joined || requested;
            }
        }

        foreach (long peerId in _connectedPeerIds)
        {
            if (currentPeerIds.Contains(peerId)) continue;
            string previousName = _connectedPeerNames.TryGetValue(peerId, out string? name) ? name : "";
            string previousPlayerId = _connectedPlayerIds.TryGetValue(peerId, out string? playerId)
                ? playerId
                : peerId.ToString();
            if (_pendingLogins.Remove(peerId))
                WebhookNotifications.PlayerConnected(previousPlayerId, previousName, null);
            WebhookNotifications.PlayerDisconnected(previousPlayerId, previousName,
                _connectedPeerPositions.TryGetValue(peerId, out UnityEngine.Vector3 lastPosition) ? lastPosition : null);
            _connectedPeerPositions.Remove(peerId);
        }

        if (!_connectedPeerIds.SetEquals(currentPeerIds)) _snapshotDirty = true;
        _connectedPeerIds.Clear();
        _connectedPeerIds.UnionWith(currentPeerIds);
        _connectedPeerNames.Clear();
        foreach (KeyValuePair<long, string> peer in currentPeerNames)
            _connectedPeerNames[peer.Key] = peer.Value;
        _connectedPlayerIds.Clear();
        foreach (KeyValuePair<long, string> peer in currentPlayerIds)
            _connectedPlayerIds[peer.Key] = peer.Value;
    }
}