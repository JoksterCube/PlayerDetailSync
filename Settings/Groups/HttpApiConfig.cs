using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;

namespace JoksterCube.PlayerDetailSync.Settings.Groups;

internal static class HttpApiConfig
{
    internal const string Group = "1 - HTTP API";

    internal static readonly ConfigInfo<Toggle> LockConfiguration = new(
        Group,
        "Lock Configuration",
        "If on, only server admins can change synchronized configuration entries.",
        Toggle.On,
        true);

    internal static readonly ConfigInfo<Toggle> Enabled = new(
        Group,
        "Enabled",
        "Enable the server-hosted player data API.",
        Toggle.On,
        true);

    internal static readonly ConfigInfo<string> BindAddress = new(
        Group,
        "Bind Address",
        "Use 127.0.0.1 for local access or 0.0.0.0 to allow remote connections.",
        "127.0.0.1",
        false);

    internal static readonly ConfigInfo<int> Port = new(
        Group,
        "Port",
        new ConfigDescription(
            "HTTP port for the API.",
            new AcceptableValueRange<int>(1024, 65535)),
        8785,
        false);

    internal static readonly ConfigInfo<string> Password = new(
        Group,
        "Password",
        "Set a non-empty password to enable the API. Send it using the X-API-Password header.",
        string.Empty,
        false);

    internal static readonly ConfigInfo<float> RefreshInterval = new(
        Group,
        "Refresh Interval",
        new ConfigDescription(
            "Seconds between automatic player snapshots, cache saves, and client reports (1-60 seconds). Joins and missing data bypass this interval with bounded retries.",
            new AcceptableValueRange<float>(1f, 60f)),
        5f,
        true);
}