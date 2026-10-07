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

[DataContract]
internal sealed class PlayerRecord
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "x", Order = 3)]
    public float X { get; set; }

    [DataMember(Name = "y", Order = 4)]
    public float Y { get; set; }

    [DataMember(Name = "z", Order = 5)]
    public float Z { get; set; }

    [DataMember(Name = "online", Order = 6)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 7)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "characterDataAvailable", Order = 8)]
    public bool CharacterDataAvailable { get; set; }

    [DataMember(Name = "characterDataAgeSeconds", Order = 9)]
    public float CharacterDataAgeSeconds { get; set; }

    [DataMember(Name = "statuses", Order = 10)]
    public TimedPlayerData[] Statuses { get; set; } = [];

    [DataMember(Name = "foods", Order = 11)]
    public TimedPlayerData[] Foods { get; set; } = [];

    [DataMember(Name = "stats", Order = 12)]
    public PlayerStats Stats { get; set; } = new();

    [DataMember(Name = "equipment", Order = 13)]
    public EquippedGear[] Equipment { get; set; } = [];

    [DataMember(Name = "steamUserId", Order = 14)]
    public string SteamUserId { get; set; } = "";

    [DataMember(Name = "followers", Order = 15)]
    public FollowerRecord[] Followers { get; set; } = [];

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => Followers = [];

    [IgnoreDataMember]
    internal long SessionId { get; set; }

    public PlayerRecord() { }

    internal PlayerRecord(
        string playerId,
        string name,
        float x,
        float y,
        float z,
        Player? character,
        PlayerDataSnapshot? clientData,
        string capturedAtUtc)
    {
        PlayerId = playerId;
        Name = name;
        X = x;
        Y = y;
        Z = z;
        Online = true;
        LastSeenUtc = capturedAtUtc;
        PlayerDataSnapshot? data = clientData ?? (character != null ? PlayerDataSnapshot.Capture(character) : null);
        CharacterDataAvailable = data != null;
        CharacterDataAgeSeconds = data?.AgeSeconds ?? -1f;
        Statuses = data?.Statuses ?? [];
        Foods = data?.Foods ?? [];
        Stats = data?.Stats ?? new PlayerStats();
        Equipment = data?.Equipment ?? [];
        Followers = data?.Followers ?? [];
    }

    internal PlayerRecord WithOfflineAge(float elapsedSeconds)
    {
        return new PlayerRecord
        {
            PlayerId = PlayerId,
            SteamUserId = SteamUserId ?? "",
            Name = Name,
            X = X,
            Y = Y,
            Z = Z,
            Online = false,
            LastSeenUtc = LastSeenUtc,
            CharacterDataAvailable = CharacterDataAvailable,
            CharacterDataAgeSeconds = CharacterDataAvailable
                ? CharacterDataAgeSeconds + elapsedSeconds
                : -1f,
            Statuses = Statuses,
            Foods = Foods,
            Stats = Stats,
            Equipment = Equipment,
            Followers = Followers,
            SessionId = SessionId
        };
    }

    internal PlayerRecord WithLiveMetadata(PlayerRecord live, float elapsedSeconds, string capturedAtUtc)
    {
        return new PlayerRecord
        {
            PlayerId = live.PlayerId,
            SteamUserId = string.IsNullOrEmpty(live.SteamUserId) ? SteamUserId ?? "" : live.SteamUserId,
            Name = live.Name,
            X = live.X,
            Y = live.Y,
            Z = live.Z,
            Online = true,
            LastSeenUtc = capturedAtUtc,
            CharacterDataAvailable = CharacterDataAvailable,
            CharacterDataAgeSeconds = CharacterDataAgeSeconds + elapsedSeconds,
            Statuses = PlayerDataSnapshot.ApplyElapsed(Statuses, elapsedSeconds),
            Foods = PlayerDataSnapshot.ApplyElapsed(Foods, elapsedSeconds),
            Stats = Stats,
            Equipment = Equipment,
            Followers = FollowerRecord.ApplyElapsed(Followers, elapsedSeconds),
            SessionId = live.SessionId
        };
    }
}

[DataContract]
internal sealed class PlayerDataSnapshot
{
    [DataMember(Name = "statuses", Order = 1)]
    public TimedPlayerData[] Statuses { get; set; } = [];

    [DataMember(Name = "foods", Order = 2)]
    public TimedPlayerData[] Foods { get; set; } = [];

    [DataMember(Name = "stats", Order = 3)]
    public PlayerStats Stats { get; set; } = new();

    [DataMember(Name = "equipment", Order = 4)]
    public EquippedGear[] Equipment { get; set; } = [];

    [DataMember(Name = "characterPlayerId", Order = 5)]
    public long CharacterPlayerId { get; set; }

    [DataMember(Name = "followers", Order = 6)]
    public FollowerRecord[] Followers { get; set; } = [];

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => Followers = [];

    [IgnoreDataMember]
    internal float AgeSeconds { get; private set; }

    internal static PlayerDataSnapshot Capture(Player player)
    {
        var statuses = new List<TimedPlayerData>();
        SEMan statusManager = player.GetSEMan();
        if (statusManager != null)
        {
            foreach (StatusEffect status in statusManager.GetStatusEffects())
            {
                if (status == null || string.IsNullOrWhiteSpace(status.m_name)) continue;
                string iconKey = PlayerDetailSyncApi.RegisterIcon(status.m_icon, status.m_name);
                statuses.Add(TimedPlayerData.FromStatus(
                    LocalizeName(status.m_name),
                    status.GetRemaningTime(),
                    status.m_ttl,
                    iconKey,
                    status.m_icon));
            }
        }

        var foods = new List<TimedPlayerData>();
        foreach (Player.Food food in player.GetFoods())
        {
            if (food == null || string.IsNullOrWhiteSpace(food.m_name)) continue;
            string foodName = food.m_item?.m_shared?.m_name ?? food.m_name;
            if (string.IsNullOrWhiteSpace(foodName)) continue;
            Sprite? icon = food.m_item?.GetIcon();
            string iconKey = PlayerDetailSyncApi.RegisterIcon(icon, foodName);
            foods.Add(new TimedPlayerData(
                LocalizeName(foodName),
                Math.Max(0f, food.m_time),
                iconKey,
                icon));
        }

        var equipment = new List<EquippedGear>();
        Inventory inventory = player.GetInventory();
        if (inventory != null)
        {
            foreach (ItemDrop.ItemData item in inventory.GetEquippedItems())
            {
                if (item?.m_shared == null || string.IsNullOrWhiteSpace(item.m_shared.m_name)) continue;
                Sprite? icon = item.GetIcon();
                string iconKey = PlayerDetailSyncApi.RegisterIcon(icon, item.m_shared.m_name);
                equipment.Add(new EquippedGear(
                    LocalizeName(item.m_shared.m_name),
                    item.m_shared.m_itemType.ToString(),
                    item.m_quality,
                    iconKey,
                    icon));
            }
        }

        return new PlayerDataSnapshot
        {
            CharacterPlayerId = player.GetPlayerID(),
            Statuses = statuses.ToArray(),
            Foods = foods.ToArray(),
            Stats = new PlayerStats
            {
                Health = Math.Max(0f, player.GetHealth()),
                MaxHealth = Math.Max(0f, player.GetMaxHealth()),
                Stamina = Math.Max(0f, player.GetStamina()),
                MaxStamina = Math.Max(0f, player.GetMaxStamina()),
                Eitr = Math.Max(0f, player.GetEitr()),
                MaxEitr = Math.Max(0f, player.GetMaxEitr())
            },
            Equipment = equipment.ToArray(),
            Followers = FollowerRecord.Capture(player)
        };
    }

    internal PlayerDataSnapshot WithElapsedSeconds(float elapsedSeconds)
    {
        return new PlayerDataSnapshot
        {
            Statuses = ApplyElapsed(Statuses, elapsedSeconds),
            Foods = ApplyElapsed(Foods, elapsedSeconds),
            Stats = Stats,
            Equipment = Equipment,
            Followers = FollowerRecord.ApplyElapsed(Followers, elapsedSeconds),
            CharacterPlayerId = CharacterPlayerId,
            AgeSeconds = elapsedSeconds
        };
    }

    internal static string LocalizeName(string name)
    {
        Localization? localization = Localization.instance;
        if (localization == null) return name;

        string localizedName = localization.Localize(name);
        return string.IsNullOrWhiteSpace(localizedName) ? name : localizedName;
    }

    internal static TimedPlayerData[] ApplyElapsed(TimedPlayerData[] data, float elapsedSeconds)
    {
        var adjusted = new TimedPlayerData[data.Length];
        for (int index = 0; index < data.Length; index++)
        {
            adjusted[index] = new TimedPlayerData(
                data[index].Name,
                data[index].HasDuration ? Math.Max(0f, data[index].RemainingSeconds - elapsedSeconds) : 0f,
                data[index].IconKey,
                null,
                data[index].HasDuration)
            {
                ShieldRemainingAbsorption = data[index].ShieldRemainingAbsorption,
                ShieldMaxAbsorption = data[index].ShieldMaxAbsorption
            };
        }
        return adjusted;
    }
}

[DataContract]
internal sealed class FollowerRecord
{
    private static readonly FieldInfo ShieldCapacityField = typeof(SE_Shield).GetField(
        "m_totalAbsorbDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ShieldDamageField = typeof(SE_Shield).GetField(
        "m_damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

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

    [DataMember(Name = "tamed", Order = 6)]
    public bool Tamed { get; set; }

    [DataMember(Name = "hungry", Order = 7)]
    public bool? Hungry { get; set; }

    [DataMember(Name = "alerted", Order = 8)]
    public bool Alerted { get; set; }

    [DataMember(Name = "stats", Order = 9)]
    public FollowerStats Stats { get; set; } = new();

    [DataMember(Name = "statuses", Order = 10)]
    public TimedPlayerData[] Statuses { get; set; } = [];

    internal static FollowerRecord[] Capture(Player player)
    {
        var followers = new List<FollowerRecord>();
        foreach (Character character in Character.GetAllCharacters())
        {
            if (character == null || character.IsDead()) continue;
            MonsterAI ai = character.GetComponent<MonsterAI>();
            if (ai == null || ai.GetFollowTarget() != player.gameObject) continue;
            ZDOID id = character.GetZDOID();
            if (id == ZDOID.None) continue;
            Tameable tameable = character.GetComponent<Tameable>();
            Vector3 position = character.transform.position;
            var statuses = new List<TimedPlayerData>();
            foreach (StatusEffect status in character.GetSEMan().GetStatusEffects())
            {
                if (status == null || string.IsNullOrWhiteSpace(status.m_name)) continue;
                TimedPlayerData effect = TimedPlayerData.FromStatus(
                    PlayerDataSnapshot.LocalizeName(status.m_name), status.GetRemaningTime(), status.m_ttl,
                    PlayerDetailSyncApi.RegisterIcon(status.m_icon, status.m_name), status.m_icon);
                if (status is SE_Shield shield)
                {
                    CaptureShield(effect, shield);
                }
                statuses.Add(effect);
            }
            string name = character.GetHoverName();
            followers.Add(new FollowerRecord
            {
                FollowerId = id.ToString(),
                Name = string.IsNullOrWhiteSpace(name) ? PlayerDataSnapshot.LocalizeName(character.m_name) : name,
                Species = PlayerDataSnapshot.LocalizeName(character.m_name),
                Position = new PlayerPosition { X = position.x, Y = position.y, Z = position.z },
                Level = character.GetLevel(),
                Tamed = character.IsTamed(),
                Hungry = tameable != null ? tameable.IsHungry() : null,
                Alerted = ai.IsAlerted(),
                Stats = new FollowerStats
                {
                    Health = Math.Max(0f, character.GetHealth()),
                    MaxHealth = Math.Max(0f, character.GetMaxHealth())
                },
                Statuses = statuses.ToArray()
            });
        }
        followers.Sort((left, right) => string.CompareOrdinal(left.FollowerId, right.FollowerId));
        return followers.ToArray();
    }

    internal static void CaptureShield(TimedPlayerData effect, SE_Shield shield)
    {
        float capacity = (float)ShieldCapacityField.GetValue(shield);
        float damage = (float)ShieldDamageField.GetValue(shield);
        effect.ShieldMaxAbsorption = Math.Max(0f, capacity);
        effect.ShieldRemainingAbsorption = Math.Max(0f, capacity - damage);
    }

    internal static FollowerRecord[] ApplyElapsed(FollowerRecord[] followers, float elapsedSeconds)
    {
        var result = new FollowerRecord[followers.Length];
        for (int index = 0; index < followers.Length; index++)
        {
            FollowerRecord follower = followers[index];
            result[index] = new FollowerRecord
            {
                FollowerId = follower.FollowerId, Name = follower.Name, Species = follower.Species,
                Position = follower.Position, Level = follower.Level, Tamed = follower.Tamed,
                Hungry = follower.Hungry, Alerted = follower.Alerted, Stats = follower.Stats,
                Statuses = PlayerDataSnapshot.ApplyElapsed(follower.Statuses, elapsedSeconds)
            };
        }
        return result;
    }
}

[DataContract]
internal sealed class FollowerStats
{
    [DataMember(Name = "health", Order = 1)]
    public float Health { get; set; }

    [DataMember(Name = "maxHealth", Order = 2)]
    public float MaxHealth { get; set; }
}

[DataContract]
internal sealed class TimedPlayerData
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "remainingSeconds", Order = 2)]
    public float RemainingSeconds { get; set; }

    [DataMember(Name = "iconKey", Order = 3)]
    public string IconKey { get; set; } = "";

    [DataMember(Name = "hasDuration", Order = 4)]
    public bool HasDuration { get; set; } = true;

    [DataMember(Name = "shieldRemainingAbsorption", Order = 5, EmitDefaultValue = false)]
    public float? ShieldRemainingAbsorption { get; set; }

    [DataMember(Name = "shieldMaxAbsorption", Order = 6, EmitDefaultValue = false)]
    public float? ShieldMaxAbsorption { get; set; }

    [IgnoreDataMember]
    public Sprite? Icon { get; private set; }

    public TimedPlayerData() { }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => HasDuration = true;

    internal static TimedPlayerData FromStatus(string name, float remainingSeconds, float ttl, string iconKey, Sprite? icon) =>
        new(name, ttl > 0f ? Math.Max(0f, remainingSeconds) : 0f, iconKey, icon, ttl > 0f);

    internal TimedPlayerData(string name, float remainingSeconds, string iconKey = "", Sprite? icon = null, bool hasDuration = true)
    {
        Name = name;
        RemainingSeconds = remainingSeconds;
        IconKey = iconKey;
        Icon = icon;
        HasDuration = hasDuration;
    }
}

[DataContract]
internal sealed class PlayerStats
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

[DataContract]
internal sealed class EquippedGear
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "slot", Order = 2)]
    public string Slot { get; set; } = "";

    [DataMember(Name = "quality", Order = 3)]
    public int Quality { get; set; }

    [DataMember(Name = "iconKey", Order = 4)]
    public string IconKey { get; set; } = "";

    [IgnoreDataMember]
    public Sprite? Icon { get; private set; }

    public EquippedGear() { }

    internal EquippedGear(string name, string slot, int quality, string iconKey, Sprite? icon = null)
    {
        Name = name;
        Slot = slot;
        Quality = quality;
        IconKey = iconKey;
        Icon = icon;
    }
}