using BepInEx.Configuration;

namespace JoksterCube.PlayerDetailSync.Common;

internal static class ToggleExtensions
{
    internal static Toggle Not(this Toggle toggle) => toggle == Toggle.On ? Toggle.Off : Toggle.On;

    internal static bool IsOn(this Toggle toggle) => toggle == Toggle.On;

    internal static bool IsOn(this ConfigEntry<Toggle> configEntry) => configEntry.Value.IsOn();

    internal static Toggle ToToggle(this bool value) => value ? Toggle.On : Toggle.Off;
}