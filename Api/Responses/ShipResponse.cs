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
internal sealed class ShipResponse : PlayerDataCategoryResponse
{
    [DataMember(Name = "aboard", Order = 6)]
    public bool Aboard { get; set; }

    [DataMember(Name = "ship", Order = 7)]
    public ShipRecord? Ship { get; set; }
}
