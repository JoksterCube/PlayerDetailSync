using BepInEx.Configuration;

namespace JoksterCube.PlayerDetailSync.Settings;

internal sealed class ConfigInfo<T>
{
    internal string Group { get; }
    internal string Name { get; }
    internal object Description { get; }
    internal T DefaultValue { get; }
    internal bool IsServerConfig { get; }

    internal ConfigInfo(string group, string name, string description, T defaultValue, bool isServerConfig)
        : this(group, name, new ConfigDescription(description), defaultValue, isServerConfig)
    {
    }

    internal ConfigInfo(string group, string name, ConfigDescription description, T defaultValue, bool isServerConfig)
    {
        Group = group;
        Name = name;
        Description = description;
        DefaultValue = defaultValue;
        IsServerConfig = isServerConfig;
    }
}