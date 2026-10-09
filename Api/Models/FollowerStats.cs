using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class FollowerStats
{
    [DataMember(Name = "health", Order = 1)]
    public float Health { get; set; }

    [DataMember(Name = "maxHealth", Order = 2)]
    public float MaxHealth { get; set; }
}