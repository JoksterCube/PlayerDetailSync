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
internal sealed class HealthResponse
{
    [DataMember(Name = "status", Order = 1)]
    public string Status { get; set; } = "";

    [DataMember(Name = "updatedAtUtc", Order = 2)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "connectedPlayerCount", Order = 3)]
    public int ConnectedPlayerCount { get; set; }

    [DataMember(Name = "trackedPlayerCount", Order = 4)]
    public int TrackedPlayerCount { get; set; }
}
