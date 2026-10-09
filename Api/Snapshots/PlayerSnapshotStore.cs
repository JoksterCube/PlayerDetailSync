using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class PlayerSnapshotStore
{
    private static readonly BlockingCollection<PendingWrite> PendingWrites = new(new ConcurrentQueue<PendingWrite>(), 1);
    private static readonly object WriteLock = new();
    private static string _cachePath = "";
    private static Action<string> _logInfo = _ => { };
    private static Action<string> _logWarning = _ => { };
    private static PlayerSnapshot _current = new(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Array.Empty<PlayerRecord>());

    static PlayerSnapshotStore()
    {
        var writer = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = "PlayerDetailSync snapshot cache writer"
        };
        writer.Start();
    }

    internal static PlayerSnapshot Current => _current;

    internal static void Load(string configDirectory, Action<string> logInfo, Action<string> logWarning)
    {
        _logInfo = logInfo;
        _logWarning = logWarning;
        _cachePath = Path.Combine(configDirectory, "JoksterCube.PlayerDetailSync.players.json");
        _current = new PlayerSnapshot(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Array.Empty<PlayerRecord>());
        if (!File.Exists(_cachePath)) return;

        try
        {
            var serializer = new DataContractJsonSerializer(typeof(PlayerSnapshot));
            using var stream = File.OpenRead(_cachePath);
            if (serializer.ReadObject(stream) is not PlayerSnapshot saved) return;

            if (saved.IdentityVersion != 1)
            {
                _logInfo("Cleared legacy player cache containing unverified character identities.");
                QueueSave(_current);
                return;
            }

            float elapsedSeconds = GetElapsedSeconds(saved.UpdatedAtUtc, DateTime.UtcNow);
            var offlineById = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
            foreach (PlayerRecord player in saved.Players)
            {
                if (!offlineById.TryGetValue(player.PlayerId, out PlayerRecord? previous) ||
                    string.CompareOrdinal(player.LastSeenUtc, previous.LastSeenUtc) > 0)
                    offlineById[player.PlayerId] = player.WithOfflineAge(elapsedSeconds);
            }

            _current = new PlayerSnapshot(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), new List<PlayerRecord>(offlineById.Values));
            _logInfo($"Loaded {_current.Players.Length} cached player record(s).");
        }
        catch (Exception exception)
        {
            _current = new PlayerSnapshot(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Array.Empty<PlayerRecord>());
            _logWarning($"Could not load the player snapshot cache: {exception.Message}");
        }
    }

    internal static PlayerSnapshot MergeAndSave(PlayerSnapshot liveSnapshot)
    {
        DateTime now = DateTime.UtcNow;
        string updatedAtUtc = now.ToString("O", CultureInfo.InvariantCulture);
        float elapsedSeconds = GetElapsedSeconds(_current.UpdatedAtUtc, now);
        var previousById = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        var previousBySession = new Dictionary<long, PlayerRecord>();
        foreach (PlayerRecord player in _current.Players)
        {
            previousById[player.PlayerId] = player;
            if (player.SessionId != 0) previousBySession[player.SessionId] = player;
        }

        var merged = new List<PlayerRecord>(liveSnapshot.Players.Length + _current.Players.Length);
        var liveIds = new HashSet<string>(StringComparer.Ordinal);
        var retiredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (PlayerRecord livePlayer in liveSnapshot.Players)
        {
            if (!liveIds.Add(livePlayer.PlayerId)) continue;
            if (livePlayer.SessionId != 0 &&
                previousBySession.TryGetValue(livePlayer.SessionId, out PlayerRecord? sessionRecord) &&
                sessionRecord.PlayerId != livePlayer.PlayerId)
                retiredIds.Add(sessionRecord.PlayerId);
            if (previousById.TryGetValue(livePlayer.PlayerId, out PlayerRecord? previous) &&
                !livePlayer.CharacterDataAvailable && previous.CharacterDataAvailable)
            {
                merged.Add(previous.WithLiveMetadata(livePlayer, elapsedSeconds, updatedAtUtc));
            }
            else
            {
                if (previousById.TryGetValue(livePlayer.PlayerId, out PlayerRecord? previousVehicleData))
                {
                    livePlayer.Mount ??= previousVehicleData.Mount;
                    livePlayer.Ship ??= previousVehicleData.Ship;
                }
                livePlayer.Online = true;
                livePlayer.LastSeenUtc = updatedAtUtc;
                merged.Add(livePlayer);
            }
        }

        foreach (PlayerRecord previous in _current.Players)
        {
            if (!retiredIds.Contains(previous.PlayerId) && liveIds.Add(previous.PlayerId))
                merged.Add(previous.WithOfflineAge(elapsedSeconds));
        }

        _current = new PlayerSnapshot(updatedAtUtc, merged.ToArray());
        QueueSave(_current);
        return _current;
    }

    private static float GetElapsedSeconds(string timestamp, DateTime now)
    {
        if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            return 0f;
        return Math.Max(0f, (float)(now - parsed.ToUniversalTime()).TotalSeconds);
    }

    private static void QueueSave(PlayerSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(_cachePath)) return;

        try
        {
            var pendingWrite = new PendingWrite(_cachePath, snapshot);

            lock (WriteLock)
            {
                if (!PendingWrites.TryAdd(pendingWrite))
                {
                    _ = PendingWrites.TryTake(out _);
                    _ = PendingWrites.TryAdd(pendingWrite);
                }
            }
        }
        catch (Exception exception)
        {
            _logWarning($"Could not queue the player snapshot cache: {exception.Message}");
        }
    }

    private static void WriteLoop()
    {
        foreach (PendingWrite write in PendingWrites.GetConsumingEnumerable())
        {
            try
            {
                string? directory = Path.GetDirectoryName(write.Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string temporaryPath = write.Path + ".tmp";
                var serializer = new DataContractJsonSerializer(typeof(PlayerSnapshot));
                using (var stream = File.Create(temporaryPath))
                    serializer.WriteObject(stream, write.Snapshot);

                if (File.Exists(write.Path))
                {
                    try
                    {
                        File.Replace(temporaryPath, write.Path, null);
                    }
                    catch
                    {
                        File.Delete(write.Path);
                        File.Move(temporaryPath, write.Path);
                    }
                }
                else
                {
                    File.Move(temporaryPath, write.Path);
                }
            }
            catch (Exception exception)
            {
                _logWarning($"Could not save the player snapshot cache: {exception.Message}");
            }
        }
    }

}
