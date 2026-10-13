using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;

namespace JoksterCube.PlayerDetailSync.Settings.Groups;

internal static class WebhookConfig
{
    private const string Group = "3 - Webhooks";

    internal static readonly ConfigInfo<string> Url = new(
        Group,
        "URL",
        "HTTP or HTTPS webhook destination. Leave empty to disable webhook delivery.",
        "",
        false);

    internal static readonly ConfigInfo<WebhookFormat> Format = new(
        Group,
        "Format",
        "Discord sends a Discord-compatible message; JSON sends the structured event object directly.",
        WebhookFormat.Discord,
        false);

    internal static readonly ConfigInfo<Toggle> BossKill = Event("Boss Kill");
    internal static readonly ConfigInfo<Toggle> FirstBossKill = Event("First Boss Kill");
    internal static readonly ConfigInfo<Toggle> PvPKill = Event("PvP Kill");
    internal static readonly ConfigInfo<Toggle> FirstMinibossKill = Event("First Miniboss Kill");
    internal static readonly ConfigInfo<Toggle> MinibossKill = Event("Miniboss Kill");
    internal static readonly ConfigInfo<Toggle> RaidStarted = Event("Raid Started");
    internal static readonly ConfigInfo<Toggle> RaidCompleted = Event("Raid Completed");
    internal static readonly ConfigInfo<Toggle> Death = Event("Player Death");
    internal static readonly ConfigInfo<Toggle> Login = Event("Player Login");
    internal static readonly ConfigInfo<Toggle> Logoff = Event("Player Logoff");

    private static ConfigInfo<Toggle> Event(string name) => new(
        Group,
        name,
        $"Send a webhook when {name.ToLowerInvariant()} occurs.",
        Toggle.On,
        false);

}