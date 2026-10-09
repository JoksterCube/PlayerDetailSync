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
using static JoksterCube.PlayerDetailSync.Settings.PluginConstants;

namespace JoksterCube.PlayerDetailSync;

[BepInPlugin(ModGUID, ModName, ModVersion)]
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
    private float _nextConnectionCheckTime;
    private bool _snapshotDirty;
    private float _nextSnapshotTime;

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
            StopApiServer();
            if (!_waitingForZNetLogged)
            {
                ModLogger.LogInfo("Waiting for the Valheim network instance before starting the HTTP API.");
                _waitingForZNetLogged = true;
            }
            return;
        }
        _waitingForZNetLogged = false;

        VersionSync.Update();
        if (!InitialConfigSyncDone) return;

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
        _snapshotDirty |= ClientPlayerDataSync.ConsumeReportsChanged();
        if (now >= _nextConnectionCheckTime)
        {
            _nextConnectionCheckTime = now + 1f;
            var currentPeerIds = new HashSet<long>();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (string.IsNullOrWhiteSpace(peer.m_playerName)) continue;
                currentPeerIds.Add(peer.m_uid);
                bool joined = !_connectedPeerIds.Contains(peer.m_uid);
                bool missing = ClientPlayerDataSync.GetLatest(peer.m_uid) == null;
                if (joined || missing)
                {
                    bool requested = ClientPlayerDataSync.RequestClientRefresh(ClientPlayerDataSync.GetPlayerId(peer));
                    _snapshotDirty |= joined || requested;
                }
            }
            if (!_connectedPeerIds.SetEquals(currentPeerIds)) _snapshotDirty = true;
            _connectedPeerIds.Clear();
            _connectedPeerIds.UnionWith(currentPeerIds);
        }
        if (!_snapshotDirty && now < _nextSnapshotTime) return;
        _snapshotDirty = false;
        _nextSnapshotTime = now + UnityEngine.Mathf.Clamp(PluginConfig.RefreshInterval.Value, 1f, 60f);
        _apiServer.Publish(PlayerSnapshotStore.MergeAndSave(PlayerSnapshot.Capture()));
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
        _configWatcher?.Dispose();
        StopApiServer();
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
}