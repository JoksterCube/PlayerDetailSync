namespace JoksterCube.PlayerDetailSync.Settings;

internal sealed class ConfigurationManagerAttributes
{
    private static bool _isClient;
    private bool? _browsable;

    public bool? Browsable
    {
        get => _isClient ? false : _browsable;
        set => _browsable = value;
    }

    internal static void SetClientVisibility(bool isClient) => _isClient = isClient;
}