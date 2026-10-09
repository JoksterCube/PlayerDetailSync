using BepInEx.Configuration;
using JoksterCube.PlayerDetailSync.Common;

namespace JoksterCube.PlayerDetailSync.Settings.Groups;

internal static class ActivityConfig
{
    private const string Group = "2 - Activity";

    internal static readonly ConfigInfo<Toggle> ActivityStatusCollection = new(
        Group,
        "Activity Status Collection",
        "If off, activity states such as building and combat are not captured. Character status effects remain available.",
        Toggle.On,
        true);

    internal static readonly ConfigInfo<float> ActivityConfirmationPeriod = new(
        Group,
        "Activity Confirmation Period",
        new ConfigDescription(
            "Seconds a routine activity must remain active before it is reported. Critical states bypass this delay (0-60 seconds).",
            new AcceptableValueRange<float>(0f, 60f)),
        15f,
        true);
}