using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using JoksterCube.PlayerDetailSync.Common.Networking;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class PlayerReportValidator
{
    internal static string? GetError(PlayerDataSnapshot? snapshot)
    {
        if (snapshot == null) return "snapshot is missing";
        string? error = GetTimedError(snapshot.Statuses, "statuses") ?? GetTimedError(snapshot.Foods, "foods");
        if (error != null) return error;
        error = GetFollowersError(snapshot.Followers);
        if (error != null) return error;
        if (snapshot.Equipment == null) return "equipment is missing";
        if (snapshot.Equipment.Length > 32) return "equipment exceeds 32 entries";
        for (int index = 0; index < snapshot.Equipment.Length; index++)
        {
            EquippedGear? item = snapshot.Equipment[index];
            string path = $"equipment[{index}]";
            if (item == null) return path + " is missing";
            error = GetTextError(item.Name, path + ".name", true) ??
                GetTextError(item.Slot, path + ".slot", true) ??
                GetTextError(item.IconKey, path + ".iconKey", false);
            if (error != null) return error;
            if (item.Quality < 0) return path + ".quality must be nonnegative";
        }
        error = GetMountError(snapshot.Mount);
        if (error != null) return error;
        error = GetShipError(snapshot.Ship);
        if (error != null) return error;
        error = GetLifetimeStatsError(snapshot.LifetimeStats);
        if (error != null) return error;
        error = GetPrivateKeysError(snapshot.PrivateKeys);
        if (error != null) return error;
        error = GetActivityError(snapshot.Activity);
        if (error != null) return error;
        PlayerStats? stats = snapshot.Stats;
        if (stats == null) return "stats is missing";
        return GetNumberError(stats.Health, "stats.health", 100000f) ??
            GetNumberError(stats.MaxHealth, "stats.maxHealth", 100000f) ??
            GetNumberError(stats.Stamina, "stats.stamina", 100000f) ??
            GetNumberError(stats.MaxStamina, "stats.maxStamina", 100000f) ??
            GetNumberError(stats.Eitr, "stats.eitr", 100000f) ??
            GetNumberError(stats.MaxEitr, "stats.maxEitr", 100000f);
    }

    private static string? GetShipError(ShipRecord? ship)
    {
        if (ship == null) return null;
        string? error = GetTextError(ship.ShipId, "ship.shipId", false) ??
            GetTextError(ship.Name, "ship.name", true) ??
            GetTextError(ship.SpeedSetting, "ship.speedSetting", true);
        if (error != null) return error;
        error = GetPositionError(ship.Position, "ship.position");
        if (error != null) return error;
        return GetNumberError(ship.Speed, "ship.speed", 1000f);
    }

    private static string? GetLifetimeStatsError(PlayerStatistic[]? stats)
    {
        if (stats == null) return "lifetimeStats is missing";
        if (stats.Length > 64) return "lifetimeStats exceeds 64 entries";
        var names = new HashSet<PlayerStatisticName>();
        for (int index = 0; index < stats.Length; index++)
        {
            PlayerStatistic? stat = stats[index];
            string path = $"lifetimeStats[{index}]";
            if (stat == null) return path + " is missing";
            if (!stat.HasName) return path + ".name is missing";
            if (!Enum.IsDefined(typeof(PlayerStatisticName), stat.Name)) return path + ".name is invalid";
            if (!stat.HasUnit) return path + ".unit is missing";
            if (!Enum.IsDefined(typeof(PlayerStatisticUnit), stat.Unit)) return path + ".unit is invalid";
            string? error = GetNumberError(stat.Value, path + ".value", 1000000000000f);
            if (error != null) return error;
            if (!names.Add(stat.Name)) return path + ".name is duplicated";
        }
        return null;
    }

    private static string? GetPrivateKeysError(string[]? keys)
    {
        if (keys == null) return "privateKeys is missing";
        if (keys.Length > 256) return "privateKeys exceeds 256 entries";
        for (int index = 0; index < keys.Length; index++)
        {
            string? error = GetTextError(keys[index], $"privateKeys[{index}]", true);
            if (error != null) return error;
        }
        return null;
    }

    private static string? GetActivityError(PlayerActivity? activity)
    {
        if (activity == null) return null;
        if (!activity.HasState) return "activity.state is missing";
        if (!Enum.IsDefined(typeof(PlayerActivityStatus), activity.State)) return "activity.state is invalid";
        string? error = GetTextError(activity.ObservedAtUtc, "activity.observedAtUtc", true);
        if (error != null) return error;
        if (!activity.HasActiveStates || activity.ActiveStates == null) return "activity.activeStates is missing";
        if (activity.ActiveStates.Length > 64) return "activity.activeStates exceeds 64 entries";
        for (int index = 0; index < activity.ActiveStates.Length; index++)
        {
            if (!Enum.IsDefined(typeof(PlayerActivityStatus), activity.ActiveStates[index]))
                return $"activity.activeStates[{index}] is invalid";
        }
        return null;
    }

    private static string? GetMountError(MountRecord? mount)
    {
        if (mount == null) return null;
        string? error = GetTextError(mount.MountId, "mount.mountId", false) ??
            GetTextError(mount.Name, "mount.name", true) ??
            GetTextError(mount.Species, "mount.species", true) ??
            GetTimedError(mount.Statuses, "mount.statuses");
        if (error != null) return error;
        if (mount.Level < 1) return "mount.level must be positive";
        error = GetPositionError(mount.Position, "mount.position");
        if (error != null) return error;
        MountStats? stats = mount.Stats;
        if (stats == null) return "mount.stats is missing";
        return GetNumberError(stats.Health, "mount.stats.health", 100000f) ??
            GetNumberError(stats.MaxHealth, "mount.stats.maxHealth", 100000f) ??
            GetNumberError(stats.Stamina, "mount.stats.stamina", 100000f) ??
            GetNumberError(stats.MaxStamina, "mount.stats.maxStamina", 100000f);
    }

    private static string? GetFollowersError(FollowerRecord[]? followers)
    {
        if (followers == null) return "followers is missing";
        if (followers.Length > 64) return "followers exceeds 64 entries";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < followers.Length; index++)
        {
            FollowerRecord? follower = followers[index];
            string path = $"followers[{index}]";
            if (follower == null) return path + " is missing";
            string? error = GetTextError(follower.FollowerId, path + ".followerId", true) ??
                GetTextError(follower.Name, path + ".name", true) ??
                GetTextError(follower.Species, path + ".species", true) ??
                GetTimedError(follower.Statuses, path + ".statuses");
            if (error != null) return error;
            if (!ids.Add(follower.FollowerId)) return path + ".followerId is duplicated";
            if (follower.Level < 1) return path + ".level must be positive";
            error = GetPositionError(follower.Position, path + ".position");
            if (error != null) return error;
            FollowerStats? stats = follower.Stats;
            if (stats == null) return path + ".stats is missing";
            error = GetNumberError(stats.Health, path + ".stats.health", 100000f) ??
                GetNumberError(stats.MaxHealth, path + ".stats.maxHealth", 100000f);
            if (error != null) return error;
        }
        return null;
    }

    private static string? GetOptionalNumberError(float? value, string path) =>
        value.HasValue ? GetNumberError(value.Value, path, 100000f) : null;

    private static string? GetPositionError(PlayerPosition? position, string path)
    {
        if (position == null) return path + " is missing";
        foreach (float coordinate in new[] { position.X, position.Y, position.Z })
            if (float.IsNaN(coordinate) || float.IsInfinity(coordinate)) return path + " must be finite";
        return null;
    }

    private static string? GetTimedError(TimedPlayerData[]? entries, string path)
    {
        if (entries == null) return path + " is missing";
        if (entries.Length > 64) return path + " exceeds 64 entries";
        for (int index = 0; index < entries.Length; index++)
        {
            TimedPlayerData? entry = entries[index];
            string itemPath = $"{path}[{index}]";
            if (entry == null) return itemPath + " is missing";
            string? error = GetTextError(entry.Name, itemPath + ".name", true) ??
                GetTextError(entry.IconKey, itemPath + ".iconKey", false) ??
                GetNumberError(entry.RemainingSeconds, itemPath + ".remainingSeconds", 604800f) ??
                GetOptionalNumberError(entry.ShieldRemainingAbsorption, itemPath + ".shieldRemainingAbsorption") ??
                GetOptionalNumberError(entry.ShieldMaxAbsorption, itemPath + ".shieldMaxAbsorption");
            if (error != null) return error;
        }
        return null;
    }

    private static string? GetTextError(string? value, string path, bool required)
    {
        if (value == null || (required && string.IsNullOrWhiteSpace(value))) return path + " is missing";
        return value.Length > 128 ? path + " exceeds 128 characters" : null;
    }

    private static string? GetNumberError(float value, string path, float maximum)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > maximum)
            return path + "=" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " must be finite and between 0 and " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }
}
