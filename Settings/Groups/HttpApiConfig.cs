using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;

namespace JoksterCube.PlayerDetailSync.Settings.Groups;

internal static class HttpApiConfig
{
    internal const string Group = "1 - HTTP API";
    private const string DefaultBindAddress = "";
    private const int DefaultPort = 8785;
    private const float DefaultRefreshIntervalSeconds = 5f;

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
        "Leave empty to allow remote connections, use local for local-only access, or enter an IP address.",
        DefaultBindAddress,
        false);

    internal static readonly ConfigInfo<int> Port = new(
        Group,
        "Port",
        new ConfigDescription(
            "HTTP port for the API.",
            new AcceptableValueRange<int>(1024, 65535)),
        DefaultPort,
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
        DefaultRefreshIntervalSeconds,
        true);
}