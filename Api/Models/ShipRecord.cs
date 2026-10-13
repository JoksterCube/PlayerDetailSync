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

    [DataMember(Name = "passengerPlayerIds", Order = 8)]
    public string[] PassengerPlayerIds { get; set; } = [];

    [DataMember(Name = "currentPassengerPlayerIds", Order = 9)]
    public string[] CurrentPassengerPlayerIds { get; set; } = [];

    [DataMember(Name = "lastDriverPlayerId", Order = 10)]
    public string LastDriverPlayerId { get; set; } = "";

    [DataMember(Name = "observedAtUtc", Order = 11)]
    public string ObservedAtUtc { get; set; } = "";

    [DataMember(Name = "lastDrivenAtUtc", Order = 12)]
    public string LastDrivenAtUtc { get; set; } = "";

    [DataMember(Name = "characterDataAgeSeconds", Order = 13)]
    public float CharacterDataAgeSeconds { get; set; }

    internal ShipRecord Copy() => new()
    {
        ShipId = ShipId, Name = Name, Position = Position, Speed = Speed,
        SpeedSetting = SpeedSetting, SailUp = SailUp, AtHelm = AtHelm,
        PassengerPlayerIds = PassengerPlayerIds, CurrentPassengerPlayerIds = CurrentPassengerPlayerIds,
        LastDriverPlayerId = LastDriverPlayerId, ObservedAtUtc = ObservedAtUtc,
        LastDrivenAtUtc = LastDrivenAtUtc, CharacterDataAgeSeconds = CharacterDataAgeSeconds
    };
}