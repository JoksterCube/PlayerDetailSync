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
internal sealed class ApiErrorResponse
{
    [DataMember(Name = "error", Order = 1)]
    public string Error { get; set; } = "";

    [DataMember(Name = "message", Order = 2, EmitDefaultValue = false)]
    public string Message { get; set; } = "";
}
