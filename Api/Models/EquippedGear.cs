using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class EquippedGear
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "slot", Order = 2)]
    public string Slot { get; set; } = "";

    [DataMember(Name = "quality", Order = 3)]
    public int Quality { get; set; }

    [DataMember(Name = "iconKey", Order = 4)]
    public string IconKey { get; set; } = "";

    [IgnoreDataMember]
    public Sprite? Icon { get; private set; }

    public EquippedGear() { }

    internal EquippedGear(string name, string slot, int quality, string iconKey, Sprite? icon = null)
    {
        Name = name;
        Slot = slot;
        Quality = quality;
        IconKey = iconKey;
        Icon = icon;
    }
}
