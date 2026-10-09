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
internal sealed class PlayersResponse
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "players", Order = 2)]
    public PlayerSummaryResponse[] Players { get; set; } = [];
}
