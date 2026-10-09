using System;
using System.Collections.Generic;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class LifetimeStatisticCatalog
{
    internal static IReadOnlyList<(PlayerStatisticName Name, PlayerStatType Type, PlayerStatisticUnit Unit)> All { get; } =
        Array.AsReadOnly(new (PlayerStatisticName Name, PlayerStatType Type, PlayerStatisticUnit Unit)[]
        {
            (PlayerStatisticName.Deaths, PlayerStatType.Deaths, PlayerStatisticUnit.Count),
            (PlayerStatisticName.EnemyKills, PlayerStatType.EnemyKills, PlayerStatisticUnit.Count),
            (PlayerStatisticName.PlayerKills, PlayerStatType.PlayerKills, PlayerStatisticUnit.Count),
            (PlayerStatisticName.BossKills, PlayerStatType.BossKills, PlayerStatisticUnit.Count),
            (PlayerStatisticName.Builds, PlayerStatType.Builds, PlayerStatisticUnit.Count),
            (PlayerStatisticName.BuiltPieces, PlayerStatType.BuiltPieces, PlayerStatisticUnit.Count),
            (PlayerStatisticName.BuildPiecesRemoved, PlayerStatType.BuildPiecesRemoved, PlayerStatisticUnit.Count),
            (PlayerStatisticName.Crafts, PlayerStatType.Crafts, PlayerStatisticUnit.Count),
            (PlayerStatisticName.ItemsPickedUp, PlayerStatType.ItemsPickedUp, PlayerStatisticUnit.Count),
            (PlayerStatisticName.PortalsUsed, PlayerStatType.PortalsUsed, PlayerStatisticUnit.Count),
            (PlayerStatisticName.DistanceWalk, PlayerStatType.DistanceWalk, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.DistanceRun, PlayerStatType.DistanceRun, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.DistanceSail, PlayerStatType.DistanceSail, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.DistanceSailHelm, PlayerStatType.DistanceSailHelm, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.DistanceTraveled, PlayerStatType.DistanceTraveled, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.DistanceAir, PlayerStatType.DistanceAir, PlayerStatisticUnit.Meters),
            (PlayerStatisticName.TimeInBase, PlayerStatType.TimeInBase, PlayerStatisticUnit.Seconds),
            (PlayerStatisticName.TimeOutOfBase, PlayerStatType.TimeOutOfBase, PlayerStatisticUnit.Seconds),
            (PlayerStatisticName.FishHooked, PlayerStatType.FishHooked, PlayerStatisticUnit.Count),
            (PlayerStatisticName.FishCaught, PlayerStatType.FishCaught, PlayerStatisticUnit.Count),
            (PlayerStatisticName.FishLost, PlayerStatType.FishLost, PlayerStatisticUnit.Count)
        });
}