using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class ShipsResponse
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "ships", Order = 2)]
    public ShipRecord[] Ships { get; set; } = [];
}