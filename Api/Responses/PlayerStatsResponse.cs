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
internal sealed class PlayerStatsResponse
{
    [DataMember(Name = "health", Order = 1)]
    public float Health { get; set; }

    [DataMember(Name = "maxHealth", Order = 2)]
    public float MaxHealth { get; set; }

    [DataMember(Name = "stamina", Order = 3)]
    public float Stamina { get; set; }

    [DataMember(Name = "maxStamina", Order = 4)]
    public float MaxStamina { get; set; }

    [DataMember(Name = "eitr", Order = 5)]
    public float Eitr { get; set; }

    [DataMember(Name = "maxEitr", Order = 6)]
    public float MaxEitr { get; set; }
}
