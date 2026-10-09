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
internal sealed class PlayerSummaryResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "steamUserId", Order = 6)]
    public string SteamUserId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "online", Order = 3)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 4)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "position", Order = 5)]
    public PlayerPosition Position { get; set; } = new();
}
