using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using JoksterCube.PlayerDetailSync.Common.Networking;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class ClientRefreshRetry
{
    private float _nextTime;
    private float _delay = 1f;

    internal bool TrySchedule(float now)
    {
        if (now < _nextTime) return false;
        _nextTime = now + _delay;
        _delay = Math.Min(30f, _delay * 2f);
        return true;
    }
}
