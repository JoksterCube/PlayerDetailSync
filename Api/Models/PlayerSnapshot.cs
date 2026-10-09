using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class PlayerSnapshot
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "players", Order = 2)]
    public PlayerRecord[] Players { get; set; } = [];

    [DataMember(Name = "identityVersion", Order = 3)]
    public int IdentityVersion { get; set; } = 1;

    public PlayerSnapshot() { }

    internal PlayerSnapshot(string updatedAtUtc, IReadOnlyList<PlayerRecord> players)
    {
        UpdatedAtUtc = updatedAtUtc;
        Players = CopyPlayers(players);
    }

    private static PlayerRecord[] CopyPlayers(IReadOnlyList<PlayerRecord> players)
    {
        var result = new PlayerRecord[players.Count];
        for (int index = 0; index < players.Count; index++) result[index] = players[index];
        return result;
    }

    internal static PlayerSnapshot Capture()
    {
        var players = new List<PlayerRecord>();
        var playerIds = new HashSet<string>(StringComparer.Ordinal);
        string capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        ZNet? network = ZNet.instance;
        if (network != null && network.IsServer())
        {
            foreach (ZNetPeer peer in network.GetPeers())
            {
                if (string.IsNullOrWhiteSpace(peer.m_playerName)) continue;

                Vector3 position = peer.GetRefPos();
                Player? character = null;
                ZNetScene? scene = ZNetScene.instance;
                if (scene != null)
                {
                    GameObject characterObject = scene.FindInstance(peer.m_characterID);
                    if (characterObject != null) character = characterObject.GetComponent<Player>();
                }

                PlayerDataSnapshot? clientData = ClientPlayerDataSync.GetLatest(peer.m_uid);
                long numericPlayerId = character != null ? character.GetPlayerID() : clientData?.CharacterPlayerId ?? 0;
                if (numericPlayerId <= 0) continue;
                string playerId = numericPlayerId.ToString(CultureInfo.InvariantCulture);
                if (!playerIds.Add(playerId)) continue;
                players.Add(new PlayerRecord(
                    playerId,
                    peer.m_playerName,
                    position.x,
                    position.y,
                    position.z,
                    character,
                    clientData,
                    capturedAtUtc)
                {
                    SessionId = peer.m_uid,
                    SteamUserId = GetSteamUserId(peer.m_socket.GetHostName())
                });
            }
        }

        foreach (Player player in Player.GetAllPlayers())
        {
            long numericPlayerId = player.GetPlayerID();
            if (numericPlayerId <= 0) continue;
            string playerId = numericPlayerId.ToString(CultureInfo.InvariantCulture);
            if (!playerIds.Add(playerId)) continue;

            Vector3 position = player.transform.position;
            players.Add(new PlayerRecord(
                playerId,
                player.GetPlayerName(),
                position.x,
                position.y,
                position.z,
                player,
                null,
                capturedAtUtc));
        }

        return new PlayerSnapshot(capturedAtUtc, players);
    }

    internal static string GetSteamUserId(string hostName)
    {
        if (ulong.TryParse(hostName, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId) && steamId != 0)
            return steamId.ToString(CultureInfo.InvariantCulture);

        if (Splatform.PlatformUserID.TryParse(hostName, out Splatform.PlatformUserID platformId) &&
            platformId.m_platform == "Steam" &&
            ulong.TryParse(platformId.m_userID, NumberStyles.None, CultureInfo.InvariantCulture, out steamId) && steamId != 0)
            return steamId.ToString(CultureInfo.InvariantCulture);

        return "";
    }
}
