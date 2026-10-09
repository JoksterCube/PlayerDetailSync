using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class PlayerDetailCapture
{
    internal static MountRecord? CaptureMount(Player player)
    {
        Sadle? saddle = player.GetDoodadController() as Sadle;
        Character? character = saddle?.GetCharacter();
        if (character == null || character.IsDead()) return null;

        ZDOID id = character.GetZDOID();
        Tameable? tameable = character.GetComponent<Tameable>();
        Vector3 position = character.transform.position;
        string name = character.GetHoverName();
        return new MountRecord
        {
            MountId = id == ZDOID.None ? "" : id.ToString(),
            Name = string.IsNullOrWhiteSpace(name) ? PlayerDataSnapshot.LocalizeName(character.m_name) : name,
            Species = PlayerDataSnapshot.LocalizeName(character.m_name),
            Position = new PlayerPosition { X = position.x, Y = position.y, Z = position.z },
            Level = character.GetLevel(),
            Tamed = character.IsTamed(),
            Hungry = tameable?.IsHungry(),
            Statuses = CharacterStatusCapture.Capture(character),
            Stats = new MountStats
            {
                Health = Math.Max(0f, character.GetHealth()),
                MaxHealth = Math.Max(0f, character.GetMaxHealth()),
                Stamina = Math.Max(0f, saddle!.GetStamina()),
                MaxStamina = Math.Max(0f, saddle.GetMaxStamina())
            }
        };
    }

    internal static ShipRecord? CaptureShip(Player player)
    {
        Ship? controlledShip = player.GetControlledShip();
        Ship? ship = controlledShip ?? player.GetStandingOnShip();
        if (ship == null) return null;

        ZNetView? netView = ship.GetComponent<ZNetView>();
        ZDO? zdo = netView != null && netView.IsValid() ? netView.GetZDO() : null;
        Vector3 position = ship.transform.position;
        string name = ship.gameObject.name;
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - cloneSuffix.Length).TrimEnd();

        return new ShipRecord
        {
            ShipId = zdo != null && zdo.m_uid != ZDOID.None ? zdo.m_uid.ToString() : "",
            Name = name,
            Position = new PlayerPosition { X = position.x, Y = position.y, Z = position.z },
            Speed = Math.Max(0f, ship.GetSpeed()),
            SpeedSetting = ship.GetSpeedSetting().ToString(),
            SailUp = ship.IsSailUp(),
            AtHelm = controlledShip != null
        };
    }

    internal static PlayerStatistic[] CaptureLifetimeStats()
    {
        PlayerProfile? profile = Game.instance?.GetPlayerProfile();
        if (profile == null) return [];

        IReadOnlyList<(PlayerStatisticName Name, PlayerStatType Type, PlayerStatisticUnit Unit)> definitions =
            LifetimeStatisticCatalog.All;
        var stats = new PlayerStatistic[definitions.Count];
        for (int index = 0; index < definitions.Count; index++)
        {
            (PlayerStatisticName name, PlayerStatType type, PlayerStatisticUnit unit) = definitions[index];
            stats[index] = new PlayerStatistic
            {
                Name = name,
                Value = Math.Max(0f, profile.GetStat(type)),
                Unit = unit
            };
        }
        return stats;
    }

    internal static string[] CapturePrivateKeys(Player player)
    {
        List<string> keys = player.GetUniqueKeys();
        keys.Sort(StringComparer.Ordinal);
        return [.. keys];
    }

}