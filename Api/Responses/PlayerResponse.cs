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
internal sealed class PlayerResponse
{
    [DataMember(Name = "playerId", Order = 1)]
    public string PlayerId { get; set; } = "";

    [DataMember(Name = "steamUserId", Order = 4)]
    public string SteamUserId { get; set; } = "";

    [DataMember(Name = "steamName", Order = 3)]
    public string SteamName { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "online", Order = 5)]
    public bool Online { get; set; }

    [DataMember(Name = "lastSeenUtc", Order = 6)]
    public string LastSeenUtc { get; set; } = "";

    [DataMember(Name = "characterDataAvailable", Order = 7)]
    public bool CharacterDataAvailable { get; set; }

    [DataMember(Name = "characterDataAgeSeconds", Order = 8)]
    public float CharacterDataAgeSeconds { get; set; }

    [DataMember(Name = "position", Order = 9)]
    public PlayerPosition Position { get; set; } = new();
}
