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
    }
}