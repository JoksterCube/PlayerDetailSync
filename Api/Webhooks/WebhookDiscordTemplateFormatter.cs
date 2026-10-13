using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using JoksterCube.PlayerDetailSync.Settings.Groups;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class WebhookDiscordTemplateFormatter
{
    private const int DiscordMessageLimit = 2000;

    internal static string Format(string template, WebhookEvent webhookEvent)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = DiscordWebhookConfig.DefaultTemplate;

        string playerName = SafeValue(webhookEvent.PlayerName);
        string target = SafeValue(webhookEvent.Target);
        bool death = webhookEvent.Event is WebhookEventType.PlayerDeath or WebhookEventType.PvpKill;
        string eventName = GetEventName(webhookEvent.Event);
        string message = GetMessage(webhookEvent, playerName, target);
        var tags = new Dictionary<WebhookTemplateTag, string>
        {
            [WebhookTemplateTag.Event] = eventName,
            [WebhookTemplateTag.Message] = message,
            [WebhookTemplateTag.PlayerName] = playerName,
            [WebhookTemplateTag.PlayerId] = SafeValue(webhookEvent.PlayerId),
            [WebhookTemplateTag.Target] = target,
            [WebhookTemplateTag.Killer] = death ? SafeValue(webhookEvent.KillerName) : playerName,
            [WebhookTemplateTag.KillerId] = SafeValue(webhookEvent.KillerId),
            [WebhookTemplateTag.Victim] = death ? playerName : target,
            [WebhookTemplateTag.Cause] = SafeValue(webhookEvent.Cause),
            [WebhookTemplateTag.Position] = FormatPosition(webhookEvent.Position),
            [WebhookTemplateTag.Key] = SafeValue(webhookEvent.Key),
            [WebhookTemplateTag.Time] = FormatTime(webhookEvent.OccurredAtUtc),
            [WebhookTemplateTag.Timestamp] = webhookEvent.OccurredAtUtc
        };

        return LimitLength(ApplyTags(template, tags));
    }

    private static string GetEventName(WebhookEventType eventType) => eventType switch
    {
        WebhookEventType.BossKill => "Boss defeated",
        WebhookEventType.FirstBossKill => "Boss defeated for the first time",
        WebhookEventType.PvpKill => "PvP victory",
        WebhookEventType.FirstMinibossKill => "First miniboss defeated",
        WebhookEventType.MinibossKill => "Miniboss defeated",
        WebhookEventType.RaidStarted => "Raid started",
        WebhookEventType.RaidCompleted => "Raid completed",
        WebhookEventType.PlayerDeath => "Player death",
        WebhookEventType.PlayerLogin => "Player joined",
        WebhookEventType.PlayerLogoff => "Player left",
        _ => eventType.ToString().Replace('_', ' ')
    };

    private static string GetMessage(WebhookEvent webhookEvent, string playerName, string target) => webhookEvent.Event switch
    {
        WebhookEventType.BossKill => $"{Bold(playerName, "A player")} defeated {Bold(target, "a boss")}.",
        WebhookEventType.FirstBossKill => $"{Bold(playerName, "A player")} defeated {Bold(target, "a boss")} for the first time.",
        WebhookEventType.PvpKill => $"{Bold(SafeValue(webhookEvent.KillerName), "A player")} defeated {Bold(playerName, "another player")}.",
        WebhookEventType.FirstMinibossKill => $"{Bold(playerName, "A player")} defeated their first miniboss: {Bold(target, "unknown miniboss")}.",
        WebhookEventType.MinibossKill => $"{Bold(playerName, "A player")} defeated {Bold(target, "a miniboss")}.",
        WebhookEventType.RaidStarted => "A raid is underway. Prepare to defend the base!",
        WebhookEventType.RaidCompleted => "The raid has ended.",
        WebhookEventType.PlayerDeath => string.IsNullOrWhiteSpace(webhookEvent.KillerName)
            ? $"{Bold(playerName, "A player")} died ({(webhookEvent.Cause ?? "natural_causes").Replace('_', ' ')})."
            : $"{Bold(playerName, "A player")} was killed by {Bold(SafeValue(webhookEvent.KillerName), "")}.",
        WebhookEventType.PlayerLogin => $"{Bold(playerName, "A player")} joined the server.",
        WebhookEventType.PlayerLogoff => $"{Bold(playerName, "A player")} left the server.",
        _ => webhookEvent.Event.ToString().Replace('_', ' ')
    };

    private static string Bold(string value, string fallback) =>
        $"**{(string.IsNullOrWhiteSpace(value) ? fallback : value)}**";

    private static string SafeValue(string? value) => (value ?? "").Replace("@", "@\u200B");

    private static string FormatPosition(PlayerPosition? position) => position == null
        ? ""
        : string.Format(CultureInfo.InvariantCulture, "{0:F0}, {1:F0}, {2:F0}", position.X, position.Y, position.Z);

    private static string FormatTime(string timestamp) =>
        DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time)
            ? time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : timestamp;

    private static string ApplyTags(string template, IReadOnlyDictionary<WebhookTemplateTag, string> tags)
    {
        var result = new StringBuilder(template.Length);
        for (int index = 0; index < template.Length;)
        {
            if (template[index] == '{')
            {
                int closingBrace = template.IndexOf('}', index + 1);
                if (closingBrace >= 0)
                {
                    string tag = template.Substring(index + 1, closingBrace - index - 1);
                    if (TryParseTag(tag, out WebhookTemplateTag parsedTag) &&
                        tags.TryGetValue(parsedTag, out string? replacement))
                    {
                        result.Append(replacement);
                        index = closingBrace + 1;
                        continue;
                    }
                }
            }

            result.Append(template[index]);
            index++;
        }
        return result.ToString();
    }

    private static bool TryParseTag(string tag, out WebhookTemplateTag parsedTag)
    {
        string normalizedTag = tag.Replace(" ", "").Replace("_", "");
        return Enum.TryParse(normalizedTag, true, out parsedTag) &&
            Enum.IsDefined(typeof(WebhookTemplateTag), parsedTag);
    }

    private static string LimitLength(string value)
    {
        if (value.Length <= DiscordMessageLimit) return value;
        int length = DiscordMessageLimit - 3;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value.Substring(0, length) + "...";
    }
}