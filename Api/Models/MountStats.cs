using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class MountStats
{
    [DataMember(Name = "health", Order = 1)]
    public float Health { get; set; }

    [DataMember(Name = "maxHealth", Order = 2)]
    public float MaxHealth { get; set; }

    [DataMember(Name = "stamina", Order = 3)]
    public float Stamina { get; set; }

    [DataMember(Name = "maxStamina", Order = 4)]
    public float MaxStamina { get; set; }
}