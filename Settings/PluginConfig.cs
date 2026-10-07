using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;
using ServerSync;
using static JoksterCube.PlayerDetailSync.Settings.Constants.Groups;

namespace JoksterCube.PlayerDetailSync.Settings;

internal static class PluginConfig
{
    internal static ConfigEntry<Toggle> LockConfiguration = null!;
    internal static ConfigEntry<Toggle> ApiEnabled = null!;
    internal static ConfigEntry<string> BindAddress = null!;
    internal static ConfigEntry<int> Port = null!;
    internal static ConfigEntry<string> Password = null!;
    internal static ConfigEntry<float> RefreshInterval = null!;

    internal static void Build(ConfigFile config, ConfigSync configSync)
    {
        ConfigOptions.Initialize(config, configSync);

        LockConfiguration = ConfigOptions.Config(HttpApi.LockConfiguration);
        configSync.AddLockingConfigEntry(LockConfiguration);

        ApiEnabled = ConfigOptions.Config(HttpApi.Enabled);
        BindAddress = ConfigOptions.Config(HttpApi.BindAddress);
        Port = ConfigOptions.Config(HttpApi.Port);
        Password = ConfigOptions.Config(HttpApi.Password);
        RefreshInterval = ConfigOptions.Config(HttpApi.RefreshInterval);
    }
}