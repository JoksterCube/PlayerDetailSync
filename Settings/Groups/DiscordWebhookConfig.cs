using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Settings;

namespace JoksterCube.PlayerDetailSync.Settings.Groups;

internal static class DiscordWebhookConfig
{
    private const string Group = "4 - Discord Webhook Messages";
    internal const string DefaultTemplate = "**{event}**\n{message}\n-# {time}";

    private static readonly ConfigurationManagerAttributes GroupVisibility = new()
    {
        Browsable = true
    };

    internal static readonly ConfigInfo<string> SharedTemplate = new(
        Group,
        "Discord Message Template",
        TemplateDescription("Shared Discord message template used when an event-specific template is blank."),
        DefaultTemplate,
        false);

    internal static readonly ConfigInfo<string> BossKillTemplate = EventTemplate("Boss Kill Template");
    internal static readonly ConfigInfo<string> FirstBossKillTemplate = EventTemplate("First Boss Kill Template");
    internal static readonly ConfigInfo<string> PvPKillTemplate = EventTemplate("PvP Kill Template");
    internal static readonly ConfigInfo<string> FirstMinibossKillTemplate = EventTemplate("First Miniboss Kill Template");
    internal static readonly ConfigInfo<string> MinibossKillTemplate = EventTemplate("Miniboss Kill Template");
    internal static readonly ConfigInfo<string> RaidStartedTemplate = EventTemplate("Raid Started Template");
    internal static readonly ConfigInfo<string> RaidCompletedTemplate = EventTemplate("Raid Completed Template");
    internal static readonly ConfigInfo<string> DeathTemplate = EventTemplate("Player Death Template");
    internal static readonly ConfigInfo<string> LoginTemplate = EventTemplate("Player Login Template");
    internal static readonly ConfigInfo<string> LogoffTemplate = EventTemplate("Player Logoff Template");

    internal static void SetVisible(bool visible) => GroupVisibility.Browsable = visible;

    private static ConfigInfo<string> EventTemplate(string name) => new(
        Group,
        name,
        TemplateDescription("Optional override. Leave blank to use Discord Message Template."),
        "",
        false);

    private static ConfigDescription TemplateDescription(string description) => new(
        $"{description} Tags: {{event}}, {{message}}, {{player name}}, {{player id}}, {{target}}, {{boss}}, {{killer}}, {{killer id}}, {{victim}}, {{cause}}, {{position}}, {{key}}, {{time}}. Unknown tags remain unchanged.",
        null,
        GroupVisibility);
}