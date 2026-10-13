using System;
using JoksterCube.PlayerDetailSync.Settings.Groups;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class PendingWebhook
{
    internal Uri Endpoint { get; }
    internal WebhookFormat Format { get; }
    internal string DiscordTemplate { get; }
    internal WebhookEvent Event { get; }

    internal PendingWebhook(Uri endpoint, WebhookFormat format, string discordTemplate, WebhookEvent webhookEvent)
    {
        Endpoint = endpoint;
        Format = format;
        DiscordTemplate = discordTemplate;
        Event = webhookEvent;
    }
}