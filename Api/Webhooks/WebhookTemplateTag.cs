namespace JoksterCube.PlayerDetailSync.Api;

internal enum WebhookTemplateTag
{
    Event,
    Message,
    PlayerName,
    PlayerId,
    Target,
    Killer,
    KillerId,
    Victim,
    Cause,
    Position,
    Key,
    Time,
    Timestamp,
    Boss = Target
}