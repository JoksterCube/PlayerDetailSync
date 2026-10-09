using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class TimedPlayerData
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "remainingSeconds", Order = 2)]
    public float RemainingSeconds { get; set; }

    [DataMember(Name = "iconKey", Order = 3)]
    public string IconKey { get; set; } = "";

    [DataMember(Name = "hasDuration", Order = 4)]
    public bool HasDuration { get; set; } = true;

    [DataMember(Name = "shieldRemainingAbsorption", Order = 5, EmitDefaultValue = false)]
    public float? ShieldRemainingAbsorption { get; set; }

    [DataMember(Name = "shieldMaxAbsorption", Order = 6, EmitDefaultValue = false)]
    public float? ShieldMaxAbsorption { get; set; }

    [IgnoreDataMember]
    public Sprite? Icon { get; private set; }

    public TimedPlayerData() { }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => HasDuration = true;

    internal static TimedPlayerData FromStatus(string name, float remainingSeconds, float ttl, string iconKey, Sprite? icon) =>
        new(name, ttl > 0f ? Math.Max(0f, remainingSeconds) : 0f, iconKey, icon, ttl > 0f);

    internal TimedPlayerData(string name, float remainingSeconds, string iconKey = "", Sprite? icon = null, bool hasDuration = true)
    {
        Name = name;
        RemainingSeconds = remainingSeconds;
        IconKey = iconKey;
        Icon = icon;
        HasDuration = hasDuration;
    }
}
