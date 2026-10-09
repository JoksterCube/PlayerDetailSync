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
internal abstract class PlayerDataCategoryResponse
{
    [DataMember(Name = "characterDataAgeSeconds", Order = 1)]
    public float CharacterDataAgeSeconds { get; set; }
}
