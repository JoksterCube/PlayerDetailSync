using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class ShipRecord
{
    [DataMember(Name = "shipId", Order = 1)]
    public string ShipId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "position", Order = 3)]
    public PlayerPosition Position { get; set; } = new();

    [DataMember(Name = "speed", Order = 4)]
    public float Speed { get; set; }

    [DataMember(Name = "speedSetting", Order = 5)]
    public string SpeedSetting { get; set; } = "";

    [DataMember(Name = "sailUp", Order = 6)]
    public bool SailUp { get; set; }

    [DataMember(Name = "atHelm", Order = 7)]
    public bool AtHelm { get; set; }
}