using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class HttpApiRequestHandler
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _password;

    internal HttpApiRequestHandler(string password)
    {
        _password = password;
    }

    internal void HandleRequest(HttpListenerContext context, PlayerSnapshot snapshot)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        if (!IsAuthorized(context.Request.Headers["X-API-Password"]))
        {
            WriteResponse(context.Response, HttpStatusCode.Unauthorized, new ApiErrorResponse
            {
                Error = "unauthorized",
                Message = "Provide the X-API-Password header."
            });
            return;
        }

        if (context.Request.HttpMethod != WebRequestMethods.Http.Get)
        {
            WriteResponse(context.Response, HttpStatusCode.MethodNotAllowed, new ApiErrorResponse
            {
                Error = "method_not_allowed"
            });
            return;
        }

        string? path = context.Request.Url?.AbsolutePath;
        if (path == "/api/v1/health")
        {
            WriteResponse(context.Response, HttpStatusCode.OK, new HealthResponse
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
            var players = new List<PlayerSummaryResponse>(snapshot.Players.Length);
            foreach (PlayerRecord player in snapshot.Players)
            {
                players.Add(new PlayerSummaryResponse
                {
                    PlayerId = player.PlayerId,
                    SteamUserId = player.SteamUserId,
                    SteamName = player.SteamName,
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

            WriteResponse(context.Response, HttpStatusCode.OK, new PlayersResponse
            {
                UpdatedAtUtc = snapshot.UpdatedAtUtc,
                Players = players.ToArray()
            });
            return;
        }

        if (HandleGlobalEntityRequest(context.Response, path, snapshot)) return;

        const string playerDetailsPrefix = "/api/v1/players/";
        if (path != null && path.StartsWith(playerDetailsPrefix, StringComparison.Ordinal))
        {
            string[] segments = path.Substring(playerDetailsPrefix.Length).Split('/');
            string playerIdText = segments[0];
            if (!long.TryParse(playerIdText, NumberStyles.None, CultureInfo.InvariantCulture, out long playerId) || playerId <= 0)
            {
                WriteResponse(context.Response, HttpStatusCode.BadRequest, new ApiErrorResponse { Error = "invalid_player_id" });
                return;
            }

            PlayerRecord? player = FindPlayer(snapshot, playerId.ToString(CultureInfo.InvariantCulture));
            if (player == null)
            {
                WriteResponse(context.Response, HttpStatusCode.NotFound, new ApiErrorResponse { Error = "player_not_found" });
                return;
            }

            if (segments.Length == 1)
            {
                WriteResponse(context.Response, HttpStatusCode.OK, ToPlayerResponse(player));
                return;
            }
            HandlePlayerCategoryRequest(context.Response, player, segments);
            return;
        }

        WriteResponse(context.Response, HttpStatusCode.NotFound, new ApiErrorResponse
        {
            Error = "not_found"
        });
    }

    private static bool HandleGlobalEntityRequest(HttpListenerResponse response, string? path, PlayerSnapshot snapshot)
    {
        const string prefix = "/api/v1/";
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string[] segments = path.Substring(prefix.Length).Split('/');
        switch (segments[0])
        {
            case "mounts":
                var mounts = new SortedDictionary<string, MountRecord>(StringComparer.Ordinal);
                foreach (PlayerRecord player in snapshot.Players)
                    if (player.Mount != null) mounts[player.Mount.MountId] = player.Mount;
                WriteEntityResponse(response, segments, mounts, new MountsResponse
                {
                    UpdatedAtUtc = snapshot.UpdatedAtUtc,
                    Mounts = new List<MountRecord>(mounts.Values).ToArray()
                });
                return true;
            case "ships":
                var ships = new SortedDictionary<string, ShipRecord>(StringComparer.Ordinal);
                foreach (PlayerRecord player in snapshot.Players)
                    if (player.Ship != null) ships[player.Ship.ShipId] = player.Ship;
                WriteEntityResponse(response, segments, ships, new ShipsResponse
                {
                    UpdatedAtUtc = snapshot.UpdatedAtUtc,
                    Ships = new List<ShipRecord>(ships.Values).ToArray()
                });
                return true;
            case "followers":
                var followers = new SortedDictionary<string, FollowerRecord>(StringComparer.Ordinal);
                foreach (PlayerRecord player in snapshot.Players)
                    foreach (FollowerRecord follower in player.Followers) followers[follower.FollowerId] = follower;
                WriteEntityResponse(response, segments, followers, new GlobalFollowersResponse
                {
                    UpdatedAtUtc = snapshot.UpdatedAtUtc,
                    Followers = new List<FollowerRecord>(followers.Values).ToArray()
                });
                return true;
            default:
                return false;
        }
    }

    private static void WriteEntityResponse<T>(HttpListenerResponse response, string[] segments, IDictionary<string, T> entities, object listResponse)
        where T : class
    {
        if (segments.Length == 1)
        {
            WriteResponse(response, HttpStatusCode.OK, listResponse);
            return;
        }
        if (segments.Length == 2 && entities.TryGetValue(Uri.UnescapeDataString(segments[1]), out T? entity))
        {
            WriteResponse(response, HttpStatusCode.OK, entity);
            return;
        }
        WriteResponse(response, HttpStatusCode.NotFound, new ApiErrorResponse { Error = "not_found" });
    }

    private static void HandlePlayerCategoryRequest(HttpListenerResponse response, PlayerRecord player, string[] segments)
    {
        if (segments.Length < 2 || segments.Length > 3)
        {
            WriteResponse(response, HttpStatusCode.NotFound, new ApiErrorResponse { Error = "not_found" });
            return;
        }

        switch (segments[1])
        {
            case "character":
                if (segments.Length != 2) break;
                WriteResponse(response, HttpStatusCode.OK, new PlayerCharacterResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Vitals = ToPlayerStatsResponse(player.Stats),
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
                WriteResponse(response, HttpStatusCode.OK, new PlayerStatsCategoryResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    LifetimeStats = player.LifetimeStats
                });
                return;
            case "keys":
                if (segments.Length != 2) break;
                WriteResponse(response, HttpStatusCode.OK, new PlayerKeysResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    PrivateKeys = player.PrivateKeys
                });
                return;
            case "activity":
                if (segments.Length != 2) break;
                WriteResponse(response, HttpStatusCode.OK, new PlayerActivityResponse
                {
                    CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
                    Activity = player.Activity,
                    LastActivity = player.LastActivity
                });
                return;
        }

        WriteResponse(response, HttpStatusCode.NotFound, new ApiErrorResponse { Error = "not_found" });
    }

    private static void HandleFollowerRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, HttpStatusCode.OK, new FollowersResponse
        {
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Followers = player.Followers
        });
    }

    private static void HandleMountRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, HttpStatusCode.OK, new MountResponse
        {
            CharacterDataAgeSeconds = player.CharacterDataAgeSeconds,
            Riding = player.Riding,
            Mount = player.Mount
        });
    }

    private static void HandleShipRequest(HttpListenerResponse response, PlayerRecord player)
    {
        WriteResponse(response, HttpStatusCode.OK, new ShipResponse
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
            SteamName = player.SteamName,
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

    private static void WriteResponse(HttpListenerResponse response, HttpStatusCode statusCode, object value)
    {
        var serializer = new DataContractJsonSerializer(value.GetType());
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        string json = Utf8.GetString(stream.ToArray());
        byte[] body = Utf8.GetBytes(json);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Utf8;
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }
}
