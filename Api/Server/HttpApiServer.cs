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
            HandlePlayerCategoryRequest(context.Response, player, segments);
            return;
        }

        WriteResponse(context.Response, 404, new ApiErrorResponse
        {
            Error = "not_found"
        });
    }

    private static void HandlePlayerCategoryRequest(HttpListenerResponse response, PlayerRecord player, string[] segments)
    {
        if (segments.Length < 2 || segments.Length > 3)
        {
            WriteResponse(response, 404, new ApiErrorResponse { Error = "not_found" });
            return;
        }

        switch (segments[1])
        {
            case "character":
                if (segments.Length != 2) break;
                WriteResponse(response, 200, new PlayerCharacterResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Statuses = ToTimedPlayerDataResponses(player.Statuses),
                    Foods = ToTimedPlayerDataResponses(player.Foods),
                    Equipment = ToEquippedGearResponses(player.Equipment)
                });
                return;
            case "followers":
                if (segments.Length != 2) break;
                HandleFollowerRequest(response, player);
                return;
            case "mount":
                if (segments.Length != 2) break;
                HandleMountRequest(response, player);
                return;
            case "ship":
                if (segments.Length != 2) break;
                HandleShipRequest(response, player);
                return;
            case "stats":
                if (segments.Length != 2) break;
                WriteResponse(response, 200, new PlayerStatsCategoryResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Vitals = ToPlayerStatsResponse(player.Stats),
                    LifetimeStats = player.LifetimeStats
                });
                return;
            case "keys":
                if (segments.Length != 2) break;
                WriteResponse(response, 200, new PlayerKeysResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    PrivateKeys = player.PrivateKeys
                });
                return;
            case "activity":
                if (segments.Length != 2) break;
                WriteResponse(response, 200, new PlayerActivityResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Activity = player.Online ? player.Activity : null
                });
                return;
        }

        WriteResponse(response, 404, new ApiErrorResponse { Error = "not_found" });
    }

    private static void HandleFollowerRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, 200, new FollowersResponse
        {
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Followers = player.Followers
        });
    }

    private static void HandleMountRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, 200, new MountResponse
        {
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Riding = player.Riding,
            Mount = player.Mount
        });
    }

    private static void HandleShipRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, 200, new ShipResponse
        {
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Aboard = player.Aboard,
            Ship = player.Ship
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
            }
        };
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
