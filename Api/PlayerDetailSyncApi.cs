using System;
using System.Collections.Generic;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

public static class PlayerDetailSyncApi
{
    private static readonly Dictionary<string, Sprite> Icons = new(StringComparer.Ordinal);

    public static bool TryGetIcon(string iconKey, out Sprite? sprite)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            sprite = null;
            return false;
        }

        return Icons.TryGetValue(iconKey, out sprite);
    }

    internal static string RegisterIcon(Sprite? sprite, string fallbackKey)
    {
        if (sprite == null || string.IsNullOrWhiteSpace(sprite.name)) return fallbackKey;
        Icons[sprite.name] = sprite;
        return sprite.name;
    }
}
