using System;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class ReportedPlayerData
{
    internal PlayerDataSnapshot Snapshot { get; }
    internal DateTime ReceivedAtUtc { get; }

    internal ReportedPlayerData(PlayerDataSnapshot snapshot, DateTime receivedAtUtc)
    {
        Snapshot = snapshot;
        ReceivedAtUtc = receivedAtUtc;
    }
}