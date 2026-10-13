using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using JoksterCube.PlayerDetailSync.Common;
using JoksterCube.PlayerDetailSync.Settings;
using JoksterCube.PlayerDetailSync.Settings.Groups;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class WebhookNotifications
{
    private const int QueueCapacity = 128;
    private const int RequestTimeoutMilliseconds = 5000;
    private static readonly BlockingCollection<PendingWebhook> Queue = new(new ConcurrentQueue<PendingWebhook>(), QueueCapacity);
    private static readonly Dictionary<string, DateTime> RecentKillCredits = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (Vector3 Position, DateTime Time)> RecentTargetDeaths =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TargetDeathWindow = TimeSpan.FromSeconds(30);
    // Long enough to merge duplicate key sources for one kill, short enough not to swallow a quick re-kill.
    private static readonly TimeSpan KillCreditWindow = TimeSpan.FromSeconds(5);
    // Hildir's quest minibosses: Brenna, Geirrhafa, Zil, Thungr.
    private static readonly HashSet<string> HildirMinibossNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$enemy_skeletonfire",
        "$enemy_fenringcultist_hildir",
        "$enemy_goblin_hildir",
        "$enemy_goblinbrute_hildir"
    };
    private static readonly Dictionary<string, string> BossNamesByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> MinibossNamesByToken = new(StringComparer.OrdinalIgnoreCase);
    private static ZNetScene? _bossNamesScene;
    private static int _bossNamesPrefabCount;
    private static readonly Thread Worker = StartWorker();
    private static bool _raidActive;
    private static Vector3 _raidPosition;
    private static bool _raidStateInitialized;
    private static bool _queueFullWarningLogged;

    internal static void UpdateRaidState()
    {
        RandomEvent? raid = RandEventSystem.instance?.GetCurrentRandomEvent();
        bool raidActive = raid != null;
        if (raid != null) _raidPosition = raid.m_pos;
        if (!_raidStateInitialized)
        {
            _raidActive = raidActive;
            _raidStateInitialized = true;
            return;
        }

        if (raidActive == _raidActive) return;
        _raidActive = raidActive;
        Send(new WebhookEvent
        {
            Event = raidActive ? WebhookEventType.RaidStarted : WebhookEventType.RaidCompleted,
            Position = ToPosition(_raidPosition)
        });
    }

    internal static void PlayerDied(string playerId, string playerName, Vector3 position, HitData.HitType hitType,
        string? killerId, string? killerName)
    {
        if (!IsServer) return;
        bool pvp = killerId != null && killerId != playerId;
        var death = new WebhookEvent
        {
            Event = WebhookEventType.PlayerDeath,
            PlayerId = playerId,
            PlayerName = playerName,
            Cause = GetDeathCause(hitType, pvp),
            KillerId = pvp ? killerId : null,
            KillerName = killerName,
            Position = ToPosition(position)
        };
        Send(death);
        if (pvp) Send(death.As(WebhookEventType.PvpKill));
    }

    internal static string? LocalizeCreatureName(string token) =>
        string.IsNullOrWhiteSpace(token) ? null : Localize(token, token.TrimStart('$'));

    private static string GetDeathCause(HitData.HitType hitType, bool pvp) => pvp ? "player" : hitType switch
    {
        HitData.HitType.Undefined => "natural_causes",
        HitData.HitType.EnemyHit => "creature",
        _ => WebhookEvent.ToSnakeCase(hitType.ToString())
    };

    // Boss and miniboss positions come from the client that owned the creature when it died.
    internal static void TargetDied(string targetId, Vector3 position)
    {
        if (!IsServer || (GetBossName(targetId) == null && !TryGetMinibossName(targetId, out _))) return;
        RecentTargetDeaths[targetId] = (position, DateTime.UtcNow);
    }

    private static Vector3? GetTargetPosition(string targetId, Vector3? fallback) =>
        RecentTargetDeaths.TryGetValue(targetId, out (Vector3 Position, DateTime Time) death) &&
        DateTime.UtcNow - death.Time < TargetDeathWindow
            ? death.Position
            : fallback;

    // Until the character spawns a peer has no character and its reference position is the world origin.
    internal static Vector3? GetPeerPosition(ZNetPeer peer)
    {
        if (peer.m_characterID == ZDOID.None) return null;
        ZDO? character = ZDOMan.instance?.GetZDO(peer.m_characterID);
        return character != null ? character.GetPosition() : peer.GetRefPos();
    }

    // A world key reaches every online player, unless player-based events make progression per player.
    internal static void BossGlobalKeySet(string key, bool firstForWorld)
    {
        if (!IsServer || GetBossName(key) == null) return;
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PlayerEvents)) return;

        foreach (ZNetPeer peer in ZNet.instance.GetPeers())
        {
            if (string.IsNullOrWhiteSpace(peer.m_playerName)) continue;
            BossKeyReceived(ClientPlayerDataSync.GetPlayerId(peer).ToString(CultureInfo.InvariantCulture),
                peer.m_playerName, GetPeerPosition(peer), key, firstForWorld);
        }
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer != null)
            BossKeyReceived(localPlayer.GetPlayerID().ToString(CultureInfo.InvariantCulture),
                GetPlayerName(localPlayer), localPlayer.transform.position, key, firstForWorld);
    }

    internal static void BossKeyReceived(string playerId, string playerName, Vector3? playerPosition, string key,
        bool firstTime)
    {
        if (!IsServer) return;
        string? bossName = GetBossName(key);
        if (bossName == null) return;
        var kill = new WebhookEvent
        {
            Event = WebhookEventType.BossKill,
            PlayerId = playerId,
            PlayerName = playerName,
            Target = bossName,
            Key = key,
            Position = ToPosition(GetTargetPosition(key, playerPosition))
        };
        if (TryCreditKill(playerId, key)) Send(kill);
        // Another key source for the same kill may be the one that knows this was the player's first.
        if (firstTime && WebhookHistory.TryAddFirstBossKill(playerId, key))
            Send(kill.As(WebhookEventType.FirstBossKill));
    }

    internal static void MinibossKilled(string playerId, string playerName, Vector3? playerPosition, string enemyName)
    {
        if (!IsServer || !TryGetMinibossName(enemyName, out string targetName) || !TryCreditKill(playerId, enemyName)) return;
        var kill = new WebhookEvent
        {
            Event = WebhookEventType.MinibossKill,
            PlayerId = playerId,
            PlayerName = playerName,
            Target = targetName,
            Position = ToPosition(GetTargetPosition(enemyName, playerPosition))
        };
        if (WebhookHistory.TryAddFirstMinibossKill(playerId))
            Send(kill.As(WebhookEventType.FirstMinibossKill));
        Send(kill);
    }

    internal static bool IsMinibossName(string enemyName) => TryGetMinibossName(enemyName, out _);

    private static bool TryGetMinibossName(string enemyName, out string name)
    {
        EnsureBossNames();
        name = "";
        return !string.IsNullOrEmpty(enemyName) && MinibossNamesByToken.TryGetValue(enemyName, out name);
    }

    internal static void PlayerConnected(string playerId, string playerName, Vector3? position) =>
        SendPlayerEvent(WebhookEventType.PlayerLogin, playerId, playerName, position);

    internal static void PlayerDisconnected(string playerId, string playerName, Vector3? lastPosition) =>
        SendPlayerEvent(WebhookEventType.PlayerLogoff, playerId, playerName, lastPosition);

    private static void SendPlayerEvent(WebhookEventType eventType, string playerId, string playerName, Vector3? position) =>
        Send(new WebhookEvent
        {
            Event = eventType,
            PlayerId = playerId,
            PlayerName = playerName,
            Position = ToPosition(position)
        });

    private static PlayerPosition? ToPosition(Vector3? position) => position is { } value
        ? new PlayerPosition { X = value.x, Y = value.y, Z = value.z }
        : null;

    private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

    // One kill can reach the server from several key sources; credit each player once per kill.
    private static bool TryCreditKill(string playerId, string target)
    {
        DateTime now = DateTime.UtcNow;
        var expired = new List<string>();
        foreach (KeyValuePair<string, DateTime> credit in RecentKillCredits)
            if (now - credit.Value >= KillCreditWindow) expired.Add(credit.Key);
        foreach (string key in expired) RecentKillCredits.Remove(key);

        string creditKey = playerId + "|" + target.ToLowerInvariant();
        if (RecentKillCredits.ContainsKey(creditKey)) return false;
        RecentKillCredits[creditKey] = now;
        return true;
    }

    // Any creature whose defeat sets a key is a boss, including the Deep North boss and modded bosses.
    private static string? GetBossName(string key)
    {
        EnsureBossNames();
        return !string.IsNullOrEmpty(key) && BossNamesByKey.TryGetValue(key, out string name) ? name : null;
    }

    internal static void EnsureBossNames()
    {
        ZNetScene? scene = ZNetScene.instance;
        if (scene == null || (ReferenceEquals(scene, _bossNamesScene) && scene.m_prefabs.Count == _bossNamesPrefabCount))
            return;

        BossNamesByKey.Clear();
        MinibossNamesByToken.Clear();
        var bossTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ignored = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UnityEngine.GameObject prefab in scene.m_prefabs)
        {
            if (prefab == null) continue;
            Character character = prefab.GetComponent<Character>();
            if (character == null || string.IsNullOrEmpty(character.m_name)) continue;

            // Many ordinary creatures set defeat keys too; only campaign bosses have a boss order.
            if (character.m_bossOrder > 0)
            {
                bossTokens.Add(character.m_name);
                if (!string.IsNullOrEmpty(character.m_defeatSetGlobalKey) &&
                    !BossNamesByKey.ContainsKey(character.m_defeatSetGlobalKey))
                    BossNamesByKey[character.m_defeatSetGlobalKey] = Localize(character.m_name, prefab.name);
                continue;
            }
            if (!string.IsNullOrEmpty(character.m_defeatSetGlobalKey)) ignored.Add(character.m_defeatSetGlobalKey);

            // Minibosses show a boss health bar without a campaign boss order.
            if ((character.m_boss || HildirMinibossNames.Contains(character.m_name)) &&
                !MinibossNamesByToken.ContainsKey(character.m_name))
                MinibossNamesByToken[character.m_name] = Localize(character.m_name, prefab.name);
        }
        // Earlier phases of a campaign boss share its name but have no boss order.
        var bossNames = new HashSet<string>(BossNamesByKey.Values, StringComparer.OrdinalIgnoreCase);
        var earlierPhases = new List<string>();
        foreach (KeyValuePair<string, string> miniboss in MinibossNamesByToken)
            if (bossTokens.Contains(miniboss.Key) || bossNames.Contains(miniboss.Value)) earlierPhases.Add(miniboss.Key);
        foreach (string token in earlierPhases) MinibossNamesByToken.Remove(token);
        _bossNamesScene = scene;
        _bossNamesPrefabCount = scene.m_prefabs.Count;

        var detected = new List<string>();
        foreach (KeyValuePair<string, string> boss in BossNamesByKey) detected.Add(boss.Key + " = " + boss.Value);
        Plugin.ModLogger.LogInfo("Boss defeat keys: " + string.Join(", ", detected.ToArray()));
        foreach (string key in BossNamesByKey.Keys) ignored.Remove(key);
        Plugin.ModLogger.LogInfo("Ignored non-boss defeat keys: " + string.Join(", ", new List<string>(ignored).ToArray()));
        var minibosses = new List<string>();
        foreach (KeyValuePair<string, string> miniboss in MinibossNamesByToken) minibosses.Add(miniboss.Key + " = " + miniboss.Value);
        Plugin.ModLogger.LogInfo("Minibosses: " + string.Join(", ", minibosses.ToArray()));
        foreach (string token in HildirMinibossNames)
            if (!MinibossNamesByToken.ContainsKey(token))
                Plugin.ModLogger.LogWarning("Hildir miniboss " + token + " was not found among creature prefabs.");
    }

    private static string Localize(string token, string fallback)
    {
        string text = Regex.Replace(Localization.instance.Localize(token), "<[^>]*>", "").Trim();
        // Missing translations come back as the token or as [token].
        return text.Length == 0 || text.StartsWith("$", StringComparison.Ordinal) ||
            text.StartsWith("[", StringComparison.Ordinal) ? fallback : text;
    }

    private static string GetPlayerName(Player player)
    {
        string name = player.GetPlayerName();
        return string.IsNullOrWhiteSpace(name) ? player.GetHoverName() : name;
    }

    private static void Send(WebhookEvent payload)
    {
        WebhookEventType eventType = payload.Event;
        if (string.IsNullOrWhiteSpace(PluginConfig.WebhookUrl.Value) || !IsEnabled(eventType)) return;
        if (!Uri.TryCreate(PluginConfig.WebhookUrl.Value, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            Plugin.ModLogger.LogWarning("Webhook URL must be an absolute HTTP or HTTPS URL.");
            return;
        }

        payload.OccurredAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        try
        {
            if (!Queue.TryAdd(new PendingWebhook(
                endpoint,
                PluginConfig.WebhookFormat.Value,
                GetDiscordTemplate(eventType),
                payload)))
            {
                if (!_queueFullWarningLogged)
                {
                    Plugin.ModLogger.LogWarning("Webhook queue is full; new webhook events are being dropped.");
                    _queueFullWarningLogged = true;
                }
            }
            else
            {
                _queueFullWarningLogged = false;
            }
        }
        catch (InvalidOperationException)
        {
            Plugin.ModLogger.LogWarning("Webhook queue is unavailable; event was not delivered.");
        }
    }

    private static string GetDiscordTemplate(WebhookEventType eventType)
    {
        string eventTemplate = eventType switch
        {
            WebhookEventType.BossKill => PluginConfig.WebhookBossKillTemplate.Value,
            WebhookEventType.FirstBossKill => PluginConfig.WebhookFirstBossKillTemplate.Value,
            WebhookEventType.PvpKill => PluginConfig.WebhookPvPKillTemplate.Value,
            WebhookEventType.FirstMinibossKill => PluginConfig.WebhookFirstMinibossKillTemplate.Value,
            WebhookEventType.MinibossKill => PluginConfig.WebhookMinibossKillTemplate.Value,
            WebhookEventType.RaidStarted => PluginConfig.WebhookRaidStartedTemplate.Value,
            WebhookEventType.RaidCompleted => PluginConfig.WebhookRaidCompletedTemplate.Value,
            WebhookEventType.PlayerDeath => PluginConfig.WebhookDeathTemplate.Value,
            WebhookEventType.PlayerLogin => PluginConfig.WebhookLoginTemplate.Value,
            WebhookEventType.PlayerLogoff => PluginConfig.WebhookLogoffTemplate.Value,
            _ => ""
        };

        return string.IsNullOrWhiteSpace(eventTemplate)
            ? PluginConfig.WebhookDiscordTemplate.Value
            : eventTemplate;
    }

    private static bool IsEnabled(WebhookEventType eventType) => eventType switch
    {
        WebhookEventType.BossKill => PluginConfig.WebhookBossKill.Value.IsOn(),
        WebhookEventType.FirstBossKill => PluginConfig.WebhookFirstBossKill.Value.IsOn(),
        WebhookEventType.PvpKill => PluginConfig.WebhookPvPKill.Value.IsOn(),
        WebhookEventType.FirstMinibossKill => PluginConfig.WebhookFirstMinibossKill.Value.IsOn(),
        WebhookEventType.MinibossKill => PluginConfig.WebhookMinibossKill.Value.IsOn(),
        WebhookEventType.RaidStarted => PluginConfig.WebhookRaidStarted.Value.IsOn(),
        WebhookEventType.RaidCompleted => PluginConfig.WebhookRaidCompleted.Value.IsOn(),
        WebhookEventType.PlayerDeath => PluginConfig.WebhookDeath.Value.IsOn(),
        WebhookEventType.PlayerLogin => PluginConfig.WebhookLogin.Value.IsOn(),
        WebhookEventType.PlayerLogoff => PluginConfig.WebhookLogoff.Value.IsOn(),
        _ => false
    };

    private static Thread StartWorker()
    {
        var worker = new Thread(DispatchLoop) { IsBackground = true, Name = "PlayerDetailSync webhook sender" };
        worker.Start();
        return worker;
    }

    private static void DispatchLoop()
    {
        foreach (PendingWebhook item in Queue.GetConsumingEnumerable())
        {
            try
            {
                byte[] body = Serialize(item);
                var request = (HttpWebRequest)WebRequest.Create(item.Endpoint);
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "application/json";
                request.Timeout = RequestTimeoutMilliseconds;
                request.ReadWriteTimeout = RequestTimeoutMilliseconds;
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                using var response = (HttpWebResponse)request.GetResponse();
                if ((int)response.StatusCode >= 400)
                    Plugin.ModLogger.LogWarning($"Webhook endpoint returned HTTP {(int)response.StatusCode}.");
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Webhook delivery failed ({exception.GetType().Name}).");
            }
        }
    }

    private static byte[] Serialize(PendingWebhook item)
    {
        using var stream = new MemoryStream();
        var serializer = new DataContractJsonSerializer(
            item.Format == WebhookFormat.Discord ? typeof(DiscordWebhookPayload) : typeof(WebhookEvent));
        object payload = item.Format == WebhookFormat.Discord
            ? new DiscordWebhookPayload
            {
                Content = WebhookDiscordTemplateFormatter.Format(item.DiscordTemplate, item.Event),
                Username = "PlayerDetailSync"
            }
            : item.Event;
        serializer.WriteObject(stream, payload);
        return stream.ToArray();
    }

}