using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class HttpApiServer : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly HttpListener _listener = new();
    private readonly string _password;
    private readonly Thread _worker;
    private volatile PlayerSnapshot _snapshot = new(DateTime.UtcNow.ToString("O"), Array.Empty<PlayerRecord>());

    internal HttpApiServer(string bindAddress, int port, string password)
    {
        _password = password;
        string host = bindAddress == "0.0.0.0" ? "*" : bindAddress;
        _listener.Prefixes.Add($"http://{host}:{port}/");
        _worker = new Thread(Listen) { IsBackground = true, Name = "PlayerDetailSync HTTP API" };
    }

    internal bool Start()
    {
        try
        {
            _listener.Start();
            _worker.Start();
            return true;
        }
        catch (Exception exception)
        {
            Plugin.ModLogger.LogError($"Could not start the player data API: {exception.Message}");
            _listener.Close();
            return false;
        }
    }

    internal void Publish(PlayerSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    private void Listen()
    {
        while (_listener.IsListening)
        {
            try
            {
                HandleRequest(_listener.GetContext());
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogError($"Player data API request failed: {exception.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        if (!IsAuthorized(context.Request.Headers["X-API-Password"]))
        {
            WriteResponse(context.Response, 401, new ApiErrorResponse
            {
                Error = "unauthorized",
                Message = "Provide the X-API-Password header."
            });
            return;
        }

        if (context.Request.HttpMethod != "GET")
        {
            WriteResponse(context.Response, 405, new ApiErrorResponse
            {
                Error = "method_not_allowed"
            });
            return;
        }

        string? path = context.Request.Url?.AbsolutePath;
        if (path == "/api/v1/health")
        {
            PlayerSnapshot snapshot = _snapshot;
            WriteResponse(context.Response, 200, new HealthResponse
            {
                Status = "ok",
                UpdatedAtUtc = snapshot.UpdatedAtUtc,
                ConnectedPlayerCount = CountOnlinePlayers(snapshot),
                TrackedPlayerCount = snapshot.Players.Length
            });
            return;
        }

        if (path == "/api/v1/players")
        {
            PlayerSnapshot snapshot = _snapshot;
            var players = new List<PlayerSummaryResponse>(snapshot.Players.Length);
            foreach (PlayerRecord player in snapshot.Players)
            {
                players.Add(new PlayerSummaryResponse
                {
                    PlayerId = player.PlayerId,
                    SteamUserId = player.SteamUserId,
                    Name = player.Name,
                    Online = player.Online,
                    LastSeenUtc = player.LastSeenUtc,
                    Position = new PlayerPosition
                    {
                        X = player.X,
                        Y = player.Y,
                        Z = player.Z
                    }
                });
            }

            WriteResponse(context.Response, 200, new PlayersResponse
            {
                UpdatedAtUtc = snapshot.UpdatedAtUtc,
                Players = players.ToArray()
            });
            return;
        }

        const string playerDetailsPrefix = "/api/v1/players/";
        if (path != null && path.StartsWith(playerDetailsPrefix, StringComparison.Ordinal))
        {
            string[] segments = path.Substring(playerDetailsPrefix.Length).Split('/');
            string playerIdText = segments[0];
            if (!long.TryParse(playerIdText, NumberStyles.None, CultureInfo.InvariantCulture, out long playerId) || playerId <= 0)
            {
                WriteResponse(context.Response, 400, new ApiErrorResponse { Error = "invalid_player_id" });
                return;
            }

            PlayerRecord? player = FindPlayer(_snapshot, playerId.ToString(CultureInfo.InvariantCulture));
            if (player == null)
            {
                WriteResponse(context.Response, 404, new ApiErrorResponse { Error = "player_not_found" });
                return;
            }

            if (segments.Length == 1)
            {
                WriteResponse(context.Response, 200, ToPlayerResponse(player));
                return;
            }
            if (segments[1] != "followers" || segments.Length > 3)
            {
                WriteResponse(context.Response, 404, new ApiErrorResponse { Error = "not_found" });
                return;
            }
            if (segments.Length == 2)
            {
                WriteResponse(context.Response, 200, new FollowersResponse
                {
                    PlayerId = player.PlayerId,
                    Online = player.Online,
                    LastSeenUtc = player.LastSeenUtc,
                    CharacterDataAvailable = player.CharacterDataAvailable,
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Followers = ToFollowerSummaries(player.Followers)
                });
                return;
            }
            string followerId = Uri.UnescapeDataString(segments[2]);
            foreach (FollowerRecord follower in player.Followers)
            {
                if (!string.Equals(follower.FollowerId, followerId, StringComparison.Ordinal)) continue;
                WriteResponse(context.Response, 200, new FollowerResponse
                {
                    PlayerId = player.PlayerId,
                    Online = player.Online,
                    LastSeenUtc = player.LastSeenUtc,
                    CharacterDataAvailable = player.CharacterDataAvailable,
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Follower = follower
                });
                return;
            }
            WriteResponse(context.Response, 404, new ApiErrorResponse { Error = "follower_not_found" });
            return;
        }

        WriteResponse(context.Response, 404, new ApiErrorResponse
        {
            Error = "not_found"
        });
    }

    private static PlayerRecord? FindPlayer(PlayerSnapshot snapshot, string playerId)
    {
        foreach (PlayerRecord player in snapshot.Players)
        {
            if (string.Equals(player.PlayerId, playerId, StringComparison.Ordinal)) return player;
        }
        return null;
    }

    private static int CountOnlinePlayers(PlayerSnapshot snapshot)
    {
        int count = 0;
        foreach (PlayerRecord player in snapshot.Players)
            if (player.Online) count++;
        return count;
    }

    private static PlayerResponse ToPlayerResponse(PlayerRecord player)
    {
        return new PlayerResponse
        {
            PlayerId = player.PlayerId,
            SteamUserId = player.SteamUserId,
            Name = player.Name,
            Online = player.Online,
            LastSeenUtc = player.LastSeenUtc,
            CharacterDataAvailable = player.CharacterDataAvailable,
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Position = new PlayerPosition
            {
                X = player.X,
                Y = player.Y,
                Z = player.Z
            },
            Statuses = ToTimedPlayerDataResponses(player.Statuses),
            Foods = ToTimedPlayerDataResponses(player.Foods),
            Stats = ToPlayerStatsResponse(player.Stats),
            Equipment = ToEquippedGearResponses(player.Equipment),
            Followers = ToFollowerSummaries(player.Followers)
        };
    }

    private static FollowerSummaryResponse[] ToFollowerSummaries(IReadOnlyList<FollowerRecord> followers)
    {
        var result = new FollowerSummaryResponse[followers.Count];
        for (int index = 0; index < followers.Count; index++)
        {
            FollowerRecord follower = followers[index];
            result[index] = new FollowerSummaryResponse
            {
                FollowerId = follower.FollowerId,
                Name = follower.Name,
                Species = follower.Species,
                Position = follower.Position,
                Level = follower.Level
            };
        }
        return result;
    }

    private static TimedPlayerDataResponse[] ToTimedPlayerDataResponses(IReadOnlyList<TimedPlayerData> data)
    {
        var result = new TimedPlayerDataResponse[data.Count];
        for (int index = 0; index < data.Count; index++)
        {
            result[index] = new TimedPlayerDataResponse
            {
                Name = data[index].Name,
                RemainingSeconds = data[index].RemainingSeconds,
                IconKey = data[index].IconKey,
                HasDuration = data[index].HasDuration
            };
        }
        return result;
    }

    private static PlayerStatsResponse ToPlayerStatsResponse(PlayerStats stats) => new()
    {
        Health = stats.Health,
        MaxHealth = stats.MaxHealth,
        Stamina = stats.Stamina,
        MaxStamina = stats.MaxStamina,
        Eitr = stats.Eitr,
        MaxEitr = stats.MaxEitr
    };

    private static EquippedGearResponse[] ToEquippedGearResponses(IReadOnlyList<EquippedGear> equipment)
    {
        var result = new EquippedGearResponse[equipment.Count];
        for (int index = 0; index < equipment.Count; index++)
        {
            result[index] = new EquippedGearResponse
            {
                Name = equipment[index].Name,
                Slot = equipment[index].Slot,
                Quality = equipment[index].Quality,
                IconKey = equipment[index].IconKey
            };
        }
        return result;
    }

    private bool IsAuthorized(string? providedPassword)
    {
        if (string.IsNullOrEmpty(providedPassword)) return false;

        byte[] provided = Utf8.GetBytes(providedPassword);
        byte[] expected = Utf8.GetBytes(_password);
        int difference = provided.Length ^ expected.Length;
        for (int index = 0; index < Math.Min(provided.Length, expected.Length); index++)
            difference |= provided[index] ^ expected[index];
        return difference == 0;
    }

    private static void WriteResponse(HttpListenerResponse response, int statusCode, object value)
    {
        var serializer = new DataContractJsonSerializer(value.GetType());
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        string json = Utf8.GetString(stream.ToArray());
        byte[] body = Utf8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Utf8;
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
    }
}

[Serializable]
[DataContract]
internal sealed class HealthResponse
{
    [DataMember(Name = "status", Order = 1)]
    public string Status { get; set; } = "";

    [DataMember(Name = "updatedAtUtc", Order = 2)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "connectedPlayerCount", Order = 3)]
    public int ConnectedPlayerCount { get; set; }

    [DataMember(Name = "trackedPlayerCount", Order = 4)]
    public int TrackedPlayerCount { get; set; }
}

[Serializable]
[DataContract]
internal sealed class ApiErrorResponse
{
    [DataMember(Name = "error", Order = 1)]
    public string Error { get; set; } = "";

    [DataMember(Name = "message", Order = 2, EmitDefaultValue = false)]
    public string Message { get; set; } = "";
}

[Serializable]
[DataContract]
internal sealed class PlayersResponse
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "players", Order = 2)]
    public PlayerSummaryResponse[] Players { get; set; } = [];
}

[Serializable]
[DataContract]
internal sealed class PlayerSummaryResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "steamUserId", Order = 6)]
    public string SteamUserId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "online", Order = 3)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 4)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "position", Order = 5)]
    public PlayerPosition Position { get; set; } = new();
}

[Serializable]
[DataContract]
internal sealed class PlayerResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "steamUserId", Order = 12)]
    public string SteamUserId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "online", Order = 3)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 4)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "characterDataAvailable", Order = 5)]
    public bool CharacterDataAvailable { get; set; }

    [DataMember(Name = "characterDataAgeSeconds", Order = 6)]
    public float CharacterDataAgeSeconds { get; set; }

    [DataMember(Name = "position", Order = 7)]
    public PlayerPosition Position { get; set; } = new();

    [DataMember(Name = "statuses", Order = 8)]
    public TimedPlayerDataResponse[] Statuses { get; set; } = [];

    [DataMember(Name = "foods", Order = 9)]
    public TimedPlayerDataResponse[] Foods { get; set; } = [];

    [DataMember(Name = "stats", Order = 10)]
    public PlayerStatsResponse Stats { get; set; } = new();

    [DataMember(Name = "equipment", Order = 11)]
    public EquippedGearResponse[] Equipment { get; set; } = [];

    [DataMember(Name = "followers", Order = 13)]
    public FollowerSummaryResponse[] Followers { get; set; } = [];
}

[DataContract]
internal sealed class FollowerSummaryResponse
{
    [DataMember(Name = "followerId", Order = 1)]
    public string FollowerId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "species", Order = 3)]
    public string Species { get; set; } = "";

    [DataMember(Name = "position", Order = 4)]
    public PlayerPosition Position { get; set; } = new();

    [DataMember(Name = "level", Order = 5)]
    public int Level { get; set; }
}

[DataContract]
internal sealed class FollowersResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "online", Order = 2)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 3)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "characterDataAvailable", Order = 4)]
    public bool CharacterDataAvailable { get; set; }

    [DataMember(Name = "characterDataAgeSeconds", Order = 5)]
    public float CharacterDataAgeSeconds { get; set; }

    [DataMember(Name = "followers", Order = 6)]
    public FollowerSummaryResponse[] Followers { get; set; } = [];
}

[DataContract]
internal sealed class FollowerResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "online", Order = 2)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 3)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "characterDataAvailable", Order = 4)]
    public bool CharacterDataAvailable { get; set; }

    [DataMember(Name = "characterDataAgeSeconds", Order = 5)]
    public float CharacterDataAgeSeconds { get; set; }

    [DataMember(Name = "follower", Order = 6)]
    public FollowerRecord Follower { get; set; } = new();
}

[Serializable]
[DataContract]
internal sealed class TimedPlayerDataResponse
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "remainingSeconds", Order = 2)]
    public float RemainingSeconds { get; set; }

    [DataMember(Name = "iconKey", Order = 3)]
    public string IconKey { get; set; } = "";

    [DataMember(Name = "hasDuration", Order = 4)]
    public bool HasDuration { get; set; }
}

[Serializable]
[DataContract]
internal sealed class PlayerStatsResponse
{
    [DataMember(Name = "health", Order = 1)]
    public float Health { get; set; }

    [DataMember(Name = "maxHealth", Order = 2)]
    public float MaxHealth { get; set; }

    [DataMember(Name = "stamina", Order = 3)]
    public float Stamina { get; set; }

    [DataMember(Name = "maxStamina", Order = 4)]
    public float MaxStamina { get; set; }

    [DataMember(Name = "eitr", Order = 5)]
    public float Eitr { get; set; }

    [DataMember(Name = "maxEitr", Order = 6)]
    public float MaxEitr { get; set; }
}

[Serializable]
[DataContract]
internal sealed class EquippedGearResponse
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "slot", Order = 2)]
    public string Slot { get; set; } = "";

    [DataMember(Name = "quality", Order = 3)]
    public int Quality { get; set; }

    [DataMember(Name = "iconKey", Order = 4)]
    public string IconKey { get; set; } = "";
}

[Serializable]
[DataContract]
internal sealed class PlayerPosition
{
    [DataMember(Name = "x", Order = 1)]
    public float X { get; set; }

    [DataMember(Name = "y", Order = 2)]
    public float Y { get; set; }

    [DataMember(Name = "z", Order = 3)]
    public float Z { get; set; }
}