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
internal sealed class PlayerCharacterResponse : PlayerDataCategoryResponse
{
    [DataMember(Name = "statuses", Order = 6)]
    public TimedPlayerDataResponse[] Statuses { get; set; } = [];

    [DataMember(Name = "foods", Order = 7)]
    public TimedPlayerDataResponse[] Foods { get; set; } = [];

    [DataMember(Name = "equipment", Order = 8)]
    public EquippedGearResponse[] Equipment { get; set; } = [];
}
