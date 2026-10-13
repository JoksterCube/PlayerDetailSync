using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

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

    [DataMember(Name = "mount", Order = 16)]
    public MountRecord? Mount { get; set; }

    [DataMember(Name = "riding", Order = 17)]
    public bool Riding { get; set; }

    [DataMember(Name = "lifetimeStats", Order = 18)]
    public PlayerStatistic[] LifetimeStats { get; set; } = [];

    [DataMember(Name = "privateKeys", Order = 19)]
    public string[] PrivateKeys { get; set; } = [];

    [DataMember(Name = "ship", Order = 20)]
    public ShipRecord? Ship { get; set; }

    [DataMember(Name = "aboard", Order = 21)]
    public bool Aboard { get; set; }

    [DataMember(Name = "activity", Order = 22)]
    public PlayerActivity? Activity { get; set; }

    [DataMember(Name = "steamName", Order = 23)]
    public string SteamName { get; set; } = "";

    [DataMember(Name = "lastActivity", Order = 24)]
    public PlayerActivity? LastActivity { get; set; }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        Followers = [];
        LifetimeStats = [];
        PrivateKeys = [];
        SteamName = "";
    }

    [DataMember(Name = "currentMount", Order = 90, EmitDefaultValue = false)]
    private MountRecord? LegacyCurrentMount { get; set; }

    [DataMember(Name = "lastMount", Order = 91, EmitDefaultValue = false)]
    private MountRecord? LegacyLastMount { get; set; }

    [DataMember(Name = "currentShip", Order = 92, EmitDefaultValue = false)]
    private ShipRecord? LegacyCurrentShip { get; set; }

    [DataMember(Name = "lastShip", Order = 93, EmitDefaultValue = false)]
    private ShipRecord? LegacyLastShip { get; set; }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        if (Mount == null)
        {
            Mount = LegacyLastMount ?? LegacyCurrentMount;
            Riding = LegacyCurrentMount != null;
        }
        if (Ship == null)
        {
            Ship = LegacyLastShip ?? LegacyCurrentShip;
            Aboard = LegacyCurrentShip != null;
        }
        LegacyCurrentMount = null;
        LegacyLastMount = null;
        LegacyCurrentShip = null;
        LegacyLastShip = null;
    }

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
        Followers = FollowerRecord.ApplyElapsed(data?.Followers ?? [], 0f);
        foreach (FollowerRecord follower in Followers)
        {
            follower.FollowingPlayerId = "";
            follower.ObservedAtUtc = "";
        }
        Mount = data?.Mount?.WithElapsedSeconds(0f);
        if (Mount != null)
        {
            Mount.RiderPlayerId = "";
            Mount.ObservedAtUtc = "";
        }
        Riding = Mount != null;
        LifetimeStats = data?.LifetimeStats ?? [];
        PrivateKeys = data?.PrivateKeys ?? [];
        Ship = data?.Ship?.Copy();
        if (Ship != null)
        {
            Ship.ObservedAtUtc = "";
            Ship.LastDriverPlayerId = "";
            Ship.LastDrivenAtUtc = "";
            Ship.PassengerPlayerIds = [];
            Ship.CurrentPassengerPlayerIds = [];
        }
        Aboard = Ship != null;
        Activity = data?.Activity;
        SteamName = data?.SteamName ?? "";
    }

    internal PlayerRecord WithOfflineAge(float elapsedSeconds)
    {
        return new PlayerRecord
        {
            PlayerId = PlayerId,
            SteamUserId = SteamUserId ?? "",
            SteamName = SteamName ?? "",
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
            Mount = Mount?.WithElapsedSeconds(elapsedSeconds),
            Riding = false,
            LifetimeStats = LifetimeStats,
            PrivateKeys = PrivateKeys,
            Ship = Ship,
            Aboard = false,
            Activity = Activity?.State == PlayerActivityStatus.Offline ? Activity : new PlayerActivity
            {
                State = PlayerActivityStatus.Offline,
                ActiveStates = [PlayerActivityStatus.Offline],
                ObservedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            },
            LastActivity = GetLastClassifiedActivity(Activity, LastActivity),
            SessionId = SessionId
        };
    }

    internal PlayerRecord WithLiveMetadata(PlayerRecord live, float elapsedSeconds, string capturedAtUtc)
    {
        return new PlayerRecord
        {
            PlayerId = live.PlayerId,
            SteamUserId = string.IsNullOrEmpty(live.SteamUserId) ? SteamUserId ?? "" : live.SteamUserId,
            SteamName = string.IsNullOrEmpty(live.SteamName) ? SteamName ?? "" : live.SteamName,
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
            Mount = Mount?.WithElapsedSeconds(elapsedSeconds),
            Riding = false,
            LifetimeStats = LifetimeStats,
            PrivateKeys = PrivateKeys,
            Ship = Ship,
            Aboard = false,
            Activity = Online ? Activity : null,
            LastActivity = LastActivity,
            SessionId = live.SessionId
        };
    }

    internal void RetainActivityHistory(PlayerRecord previous)
    {
        LastActivity = Activity?.State == previous.Activity?.State
            ? previous.LastActivity
            : GetLastClassifiedActivity(previous.Activity, previous.LastActivity);
    }

    private static PlayerActivity? GetLastClassifiedActivity(PlayerActivity? activity, PlayerActivity? lastActivity) =>
        activity != null && activity.State != PlayerActivityStatus.Offline &&
        activity.State != PlayerActivityStatus.Unclassified && activity.State != PlayerActivityStatus.Joining
            ? activity : lastActivity;
}
