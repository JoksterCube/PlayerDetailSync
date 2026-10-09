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
internal sealed class PlayerActivityResponse : PlayerDataCategoryResponse
{
    [DataMember(Name = "activity", Order = 6, EmitDefaultValue = false)]
    public PlayerActivity? Activity { get; set; }
}
