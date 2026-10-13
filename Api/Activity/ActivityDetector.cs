using System;
using System.Collections.Generic;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class ActivityDetector
{
    internal PlayerActivityStatus Status { get; }
    internal int Priority { get; }
    internal bool IsImmediate { get; }
    internal Func<Player, bool> IsActive { get; }
    internal Func<Player, IReadOnlyList<Character>, bool>? IsActiveWithCharacters { get; }

    internal ActivityDetector(
        PlayerActivityStatus status,
        int priority,
        bool isImmediate,
        Func<Player, bool> isActive,
        Func<Player, IReadOnlyList<Character>, bool>? isActiveWithCharacters = null)
    {
        Status = status;
        Priority = priority;
        IsImmediate = isImmediate;
        IsActive = isActive;
        IsActiveWithCharacters = isActiveWithCharacters;
    }
}