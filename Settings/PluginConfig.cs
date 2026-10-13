using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;
using JoksterCube.PlayerDetailSync.Settings.Groups;
using ServerSync;

namespace JoksterCube.PlayerDetailSync.Settings;

internal static class PluginConfig
{
    internal static ConfigEntry<Toggle> LockConfiguration = null!;
    internal static ConfigEntry<Toggle> ApiEnabled = null!;
    internal static ConfigEntry<string> BindAddress = null!;
    internal static ConfigEntry<int> Port = null!;
    internal static ConfigEntry<string> Password = null!;
    internal static ConfigEntry<float> RefreshInterval = null!;
    internal static ConfigEntry<Toggle> ActivityStatusCollection = null!;
    internal static ConfigEntry<float> ActivityConfirmationPeriod = null!;
    internal static ConfigEntry<string> WebhookUrl = null!;
    internal static ConfigEntry<WebhookFormat> WebhookFormat = null!;
    internal static ConfigEntry<string> WebhookDiscordTemplate = null!;
    internal static ConfigEntry<string> WebhookBossKillTemplate = null!;
    internal static ConfigEntry<string> WebhookFirstBossKillTemplate = null!;
    internal static ConfigEntry<string> WebhookPvPKillTemplate = null!;
    internal static ConfigEntry<string> WebhookFirstMinibossKillTemplate = null!;
    internal static ConfigEntry<string> WebhookMinibossKillTemplate = null!;
    internal static ConfigEntry<string> WebhookRaidStartedTemplate = null!;
    internal static ConfigEntry<string> WebhookRaidCompletedTemplate = null!;
    internal static ConfigEntry<string> WebhookDeathTemplate = null!;
    internal static ConfigEntry<string> WebhookLoginTemplate = null!;
    internal static ConfigEntry<string> WebhookLogoffTemplate = null!;
    internal static ConfigEntry<Toggle> WebhookBossKill = null!;
    internal static ConfigEntry<Toggle> WebhookFirstBossKill = null!;
    internal static ConfigEntry<Toggle> WebhookPvPKill = null!;
    internal static ConfigEntry<Toggle> WebhookFirstMinibossKill = null!;
    internal static ConfigEntry<Toggle> WebhookMinibossKill = null!;
    internal static ConfigEntry<Toggle> WebhookRaidStarted = null!;
    internal static ConfigEntry<Toggle> WebhookRaidCompleted = null!;
    internal static ConfigEntry<Toggle> WebhookDeath = null!;
    internal static ConfigEntry<Toggle> WebhookLogin = null!;
    internal static ConfigEntry<Toggle> WebhookLogoff = null!;

    internal static void Build(ConfigFile config, ConfigSync configSync)
    {
        ConfigOptions.Initialize(config, configSync);

        LockConfiguration = ConfigOptions.Config(HttpApiConfig.LockConfiguration);
        configSync.AddLockingConfigEntry(LockConfiguration);

        ApiEnabled = ConfigOptions.Config(HttpApiConfig.Enabled);
        BindAddress = ConfigOptions.Config(HttpApiConfig.BindAddress);
        Port = ConfigOptions.Config(HttpApiConfig.Port);
        Password = ConfigOptions.Config(HttpApiConfig.Password);
        RefreshInterval = ConfigOptions.Config(HttpApiConfig.RefreshInterval);
        ActivityStatusCollection = ConfigOptions.Config(ActivityConfig.ActivityStatusCollection);
        ActivityConfirmationPeriod = ConfigOptions.Config(ActivityConfig.ActivityConfirmationPeriod);
        WebhookUrl = ConfigOptions.Config(WebhookConfig.Url);
        WebhookFormat = ConfigOptions.Config(WebhookConfig.Format);
        WebhookFormat.SettingChanged += (_, _) => UpdateDiscordMessageVisibility();
        UpdateDiscordMessageVisibility();
        WebhookDiscordTemplate = ConfigOptions.Config(DiscordWebhookConfig.SharedTemplate);
        WebhookBossKillTemplate = ConfigOptions.Config(DiscordWebhookConfig.BossKillTemplate);
        WebhookFirstBossKillTemplate = ConfigOptions.Config(DiscordWebhookConfig.FirstBossKillTemplate);
        WebhookPvPKillTemplate = ConfigOptions.Config(DiscordWebhookConfig.PvPKillTemplate);
        WebhookFirstMinibossKillTemplate = ConfigOptions.Config(DiscordWebhookConfig.FirstMinibossKillTemplate);
        WebhookMinibossKillTemplate = ConfigOptions.Config(DiscordWebhookConfig.MinibossKillTemplate);
        WebhookRaidStartedTemplate = ConfigOptions.Config(DiscordWebhookConfig.RaidStartedTemplate);
        WebhookRaidCompletedTemplate = ConfigOptions.Config(DiscordWebhookConfig.RaidCompletedTemplate);
        WebhookDeathTemplate = ConfigOptions.Config(DiscordWebhookConfig.DeathTemplate);
        WebhookLoginTemplate = ConfigOptions.Config(DiscordWebhookConfig.LoginTemplate);
        WebhookLogoffTemplate = ConfigOptions.Config(DiscordWebhookConfig.LogoffTemplate);
        WebhookBossKill = ConfigOptions.Config(WebhookConfig.BossKill);
        WebhookFirstBossKill = ConfigOptions.Config(WebhookConfig.FirstBossKill);
        WebhookPvPKill = ConfigOptions.Config(WebhookConfig.PvPKill);
        WebhookFirstMinibossKill = ConfigOptions.Config(WebhookConfig.FirstMinibossKill);
        WebhookMinibossKill = ConfigOptions.Config(WebhookConfig.MinibossKill);
        WebhookRaidStarted = ConfigOptions.Config(WebhookConfig.RaidStarted);
        WebhookRaidCompleted = ConfigOptions.Config(WebhookConfig.RaidCompleted);
        WebhookDeath = ConfigOptions.Config(WebhookConfig.Death);
        WebhookLogin = ConfigOptions.Config(WebhookConfig.Login);
        WebhookLogoff = ConfigOptions.Config(WebhookConfig.Logoff);
    }

    internal static void SetClientVisibility(bool isClient) => ConfigOptions.SetClientVisibility(isClient);

    private static void UpdateDiscordMessageVisibility() =>
        DiscordWebhookConfig.SetVisible(
            WebhookFormat.Value == JoksterCube.PlayerDetailSync.Settings.Groups.WebhookFormat.Discord);
}