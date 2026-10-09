using System;
using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[Serializable]
[DataContract]
internal sealed class PlayerPosition
{
    [DataMember(Name = "x", Order = 1)]
    public float X { get; set; }

    [DataMember(Name = "y", Order = 2)]
    public float Y { get; set; }

    [DataMember(Name = "z", Order = 3)]
    public float Z { get; set; }
}