using BepInEx.Configuration;
using ServerSync;

namespace JoksterCube.PlayerDetailSync.Settings;

internal static class ConfigOptions
{
    private static ConfigFile _config = null!;
    private static ConfigSync _configSync = null!;

    internal static void Initialize(ConfigFile config, ConfigSync configSync)
    {
        _config = config;
        _configSync = configSync;
    }

    internal static ConfigEntry<T> Config<T>(ConfigInfo<T> configInfo)
    {
        ConfigDescription description = configInfo.Description as ConfigDescription
            ?? new ConfigDescription(configInfo.Description.ToString());
        ConfigEntry<T> entry = _config.Bind(configInfo.Group, configInfo.Name, configInfo.DefaultValue, description);
        SyncedConfigEntry<T> syncedEntry = _configSync.AddConfigEntry(entry);
        syncedEntry.SynchronizedConfig = configInfo.IsServerConfig;
        return entry;
    }
}