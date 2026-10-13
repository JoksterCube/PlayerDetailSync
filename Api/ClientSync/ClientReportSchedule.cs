using System;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class ClientReportSchedule
{
    private float _nextSendTime;
    private float _lastSendTime = -1f;
    private bool _joinReportPending = true;

    internal void Reset()
    {
        _nextSendTime = 0f;
        _lastSendTime = -1f;
        _joinReportPending = true;
    }

    internal bool CanSend(float now) => _lastSendTime < 0f || now - _lastSendTime >= 1f;

    internal bool IsDue(float now) => now >= _nextSendTime && CanSend(now);

    internal void RecordSend(float now, float refreshInterval)
    {
        _lastSendTime = now;
        _nextSendTime = now + (_joinReportPending ? 1f : Math.Max(1f, Math.Min(60f, refreshInterval)));
    }

    internal void Accept() => _joinReportPending = false;
}