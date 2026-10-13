using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;
using JoksterCube.PlayerDetailSync.Common;
using JoksterCube.PlayerDetailSync.Settings;

namespace JoksterCube.PlayerDetailSync.Api;

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

    [DataMember(Name = "mount", Order = 7)]
    public MountRecord? Mount { get; set; }

    [DataMember(Name = "lifetimeStats", Order = 8)]
    public PlayerStatistic[] LifetimeStats { get; set; } = [];

    [DataMember(Name = "privateKeys", Order = 9)]
    public string[] PrivateKeys { get; set; } = [];

    [DataMember(Name = "ship", Order = 10)]
    public ShipRecord? Ship { get; set; }

    [DataMember(Name = "activity", Order = 11)]
    public PlayerActivity? Activity { get; set; }

    [DataMember(Name = "steamName", Order = 12)]
    public string SteamName { get; set; } = "";

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        Followers = [];
        LifetimeStats = [];
        PrivateKeys = [];
        SteamName = "";
        Activity = new PlayerActivity { ActiveStates = [] };
    }

    [IgnoreDataMember]
    internal float AgeSeconds { get; private set; }

    internal static PlayerDataSnapshot Capture(Player player)
    {
        IReadOnlyList<Character> characters = Character.GetAllCharacters();
        var statuses = new List<TimedPlayerData>();
        SEMan? statusManager = player.GetSEMan();
        IReadOnlyList<StatusEffect>? statusEffects = statusManager?.GetStatusEffects();
        if (statusEffects != null)
        {
            foreach (StatusEffect status in statusEffects)
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
            SteamName = ReferenceEquals(player, Player.m_localPlayer) ? CaptureSteamName() : "",
            Statuses = [.. statuses],
            Foods = [.. foods],
            Stats = new PlayerStats
            {
                Health = Math.Max(0f, player.GetHealth()),
                MaxHealth = Math.Max(0f, player.GetMaxHealth()),
                Stamina = Math.Max(0f, player.GetStamina()),
                MaxStamina = Math.Max(0f, player.GetMaxStamina()),
                Eitr = Math.Max(0f, player.GetEitr()),
                MaxEitr = Math.Max(0f, player.GetMaxEitr())
            },
            Equipment = [.. equipment],
            Followers = FollowerRecord.Capture(player, characters),
            Mount = PlayerDetailCapture.CaptureMount(player),
            LifetimeStats = PlayerDetailCapture.CaptureLifetimeStats(),
            PrivateKeys = PlayerDetailCapture.CapturePrivateKeys(player),
            Ship = PlayerDetailCapture.CaptureShip(player),
            Activity = ReferenceEquals(player, Player.m_localPlayer)
                ? PlayerActivityCapture.Capture(
                    player,
                    PluginConfig.ActivityStatusCollection.Value.IsOn(),
                    PluginConfig.ActivityConfirmationPeriod.Value,
                    statusEffects,
                    characters)
                : null
        };
    }

    private static string CaptureSteamName()
    {
        Splatform.ILocalUser? user = Splatform.PlatformManager.DistributionPlatform?.LocalUser;
        return user != null && user.PlatformUserID.m_platform == "Steam" ? user.DisplayName ?? "" : "";
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
            Mount = Mount?.WithElapsedSeconds(elapsedSeconds),
            LifetimeStats = LifetimeStats,
            PrivateKeys = PrivateKeys,
            Ship = Ship,
            Activity = Activity,
            SteamName = SteamName,
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
