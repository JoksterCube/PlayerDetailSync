using System;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class ActivityDetector
{
    internal PlayerActivityStatus Status { get; }
    internal int Priority { get; }
    internal bool IsImmediate { get; }
    internal Func<Player, bool> IsActive { get; }

    internal ActivityDetector(PlayerActivityStatus status, int priority, bool isImmediate, Func<Player, bool> isActive)
    {
        Status = status;
        Priority = priority;
        IsImmediate = isImmediate;
        IsActive = isActive;
    }
}