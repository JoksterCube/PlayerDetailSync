using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class PlayerActivityTracker
{
    private readonly float[] _activeSeconds = new float[Enum.GetValues(typeof(PlayerActivityStatus)).Length];
    private readonly List<PlayerActivityStatus> _immediateStates = new(8);
    private readonly List<PlayerActivityStatus> _confirmedStates = new(16);
    private readonly List<PlayerActivityStatus> _activeStates = new(48);
    private readonly IReadOnlyList<ActivityDetector> _activityDetectors;
    private float _lastCapturedAt = -1f;
    private float _lastNonIdleAt;

    internal long PlayerId { get; }

    internal PlayerActivityTracker(long playerId, IReadOnlyList<ActivityDetector> activityDetectors)
    {
        PlayerId = playerId;
        _activityDetectors = activityDetectors;
        _lastNonIdleAt = Time.realtimeSinceStartup;
        for (int index = 0; index < _activeSeconds.Length; index++)
            _activeSeconds[index] = -1f;
    }

    internal PlayerActivity Capture(Player player, float confirmationPeriodSeconds)
    {
        float now = Time.realtimeSinceStartup;
        float elapsed = _lastCapturedAt < 0f ? 0f : Mathf.Max(0f, now - _lastCapturedAt);
        _lastCapturedAt = now;

        _immediateStates.Clear();
        _confirmedStates.Clear();
        PlayerActivityStatus mostEstablishedState = PlayerActivityStatus.Idling;
        float greatestActiveSeconds = -1f;
        float movementSqr = _hasPosition ? (player.transform.position - _lastPosition).sqrMagnitude : 0f;
        bool moved = movementSqr > 0.0025f;
        bool traveled = movementSqr > 0.25f;

        foreach (ActivityDetector detector in _activityDetectors)
        {
            bool active = detector.IsActive(player);
            if (!detector.IsImmediate)
            {
                int stateIndex = (int)detector.Status;
                float previousSeconds = _activeSeconds[stateIndex];
                bool wasActive = previousSeconds >= 0f;
                float activeSeconds = active ? (wasActive ? previousSeconds + elapsed : 0f) : -1f;
                _activeSeconds[stateIndex] = activeSeconds;

                if (!active || activeSeconds < confirmationPeriodSeconds) continue;
                _confirmedStates.Add(detector.Status);
                if (activeSeconds > greatestActiveSeconds)
                {
                    greatestActiveSeconds = activeSeconds;
                    mostEstablishedState = detector.Status;
                }
                continue;
            }

            if (active) _immediateStates.Add(detector.Status);
        }

        bool hasActiveState = _immediateStates.Count > 0 || _confirmedStates.Count > 0;
        if (moved || hasActiveState)
            _lastNonIdleAt = now;
        float idleSeconds = Mathf.Max(0f, now - _lastNonIdleAt);

        _activeStates.Clear();
        _activeStates.AddRange(_immediateStates);
        _activeStates.AddRange(_confirmedStates);

        PlayerActivityStatus primaryState;
        if (_immediateStates.Count > 0)
        {
            primaryState = _immediateStates[0];
        }
        else if (_confirmedStates.Count > 0)
        {
            primaryState = mostEstablishedState;
        }
        else
        {
            if (traveled && IsOverlandOutsideBase(player))
            {
                primaryState = PlayerActivityStatus.Adventuring;
            }
            else if (idleSeconds >= Math.Max(90f, confirmationPeriodSeconds))
            {
                primaryState = player.GetBaseValue() >= 3
                    ? player.GetComfortLevel() >= 5 ? PlayerActivityStatus.IdlingAtBase : PlayerActivityStatus.IdlingAtOutpost
                    : player.GetBaseValue() > 0 ? PlayerActivityStatus.IdlingAtOutpost : PlayerActivityStatus.IdlingInDanger;
            }
            else
            {
                primaryState = PlayerActivityStatus.Idling;
            }
            _activeStates.Add(primaryState);
        }

        _lastPosition = player.transform.position;
        _hasPosition = true;

        return new PlayerActivity
        {
            State = primaryState,
            ActiveStates = _activeStates.ToArray(),
            ObservedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static bool IsOverlandOutsideBase(Player player) =>
        player.GetCurrentBiome() != Heightmap.Biome.Ocean &&
        player.transform.position.y <= 3000f &&
        player.GetBaseValue() == 0 &&
        !player.IsSwimming() &&
        player.GetControlledShip() == null &&
        player.GetDoodadController() is not Sadle;

    private Vector3 _lastPosition;
    private bool _hasPosition;
}