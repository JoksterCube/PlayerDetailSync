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

[DataContract]
internal sealed class PlayerStatsCategoryResponse : PlayerDataCategoryResponse
{
    [DataMember(Name = "vitals", Order = 6)]
    public PlayerStatsResponse Vitals { get; set; } = new();

    [DataMember(Name = "lifetime", Order = 7)]
    public PlayerStatistic[] LifetimeStats { get; set; } = [];
}
