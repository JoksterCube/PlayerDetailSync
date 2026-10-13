using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class MountsResponse
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "mounts", Order = 2)]
    public MountRecord[] Mounts { get; set; } = [];
}