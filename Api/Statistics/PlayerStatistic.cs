using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class PlayerStatistic
{
    [DataMember(Name = "name", Order = 1)]
    public string? Name { get; set; }

    [DataMember(Name = "value", Order = 2)]
    public float Value { get; set; }
}