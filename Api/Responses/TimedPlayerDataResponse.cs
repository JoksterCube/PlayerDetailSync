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
internal sealed class TimedPlayerDataResponse
{
    [DataMember(Name = "name", Order = 1)]
    public string Name { get; set; } = "";

    [DataMember(Name = "remainingSeconds", Order = 2)]
    public float RemainingSeconds { get; set; }

    [DataMember(Name = "iconKey", Order = 3)]
    public string IconKey { get; set; } = "";

    [DataMember(Name = "hasDuration", Order = 4)]
    public bool HasDuration { get; set; }
}
