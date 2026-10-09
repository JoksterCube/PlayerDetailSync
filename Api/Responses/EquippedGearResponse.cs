using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace JoksterCube.PlayerDetailSync.Api;

[Serializable]
[DataContract]
internal sealed class EquippedGearResponse
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "slot", Order = 2)]
    public string Slot { get; set; } = "";

    [DataMember(Name = "quality", Order = 3)]
    public int Quality { get; set; }

    [DataMember(Name = "iconKey", Order = 4)]
    public string IconKey { get; set; } = "";
}
