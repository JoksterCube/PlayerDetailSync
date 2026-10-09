using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class MountRecord
{
    [DataMember(Name = "mountId", Order = 1)]
    public string MountId { get; set; } = "";

    [DataMember(Name = "name", Order = 2)]
    public string Name { get; set; } = "";

    [DataMember(Name = "species", Order = 3)]
    public string Species { get; set; } = "";

    [DataMember(Name = "position", Order = 4)]
    public PlayerPosition Position { get; set; } = new();

    [DataMember(Name = "level", Order = 5)]
    public int Level { get; set; }

    [DataMember(Name = "tamed", Order = 6)]
    public bool Tamed { get; set; }

    [DataMember(Name = "hungry", Order = 7)]
    public bool? Hungry { get; set; }

    [DataMember(Name = "stats", Order = 8)]
    public MountStats Stats { get; set; } = new();

    [DataMember(Name = "statuses", Order = 9)]
    public TimedPlayerData[] Statuses { get; set; } = [];

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context) => Statuses = [];

    internal MountRecord WithElapsedSeconds(float elapsedSeconds) => new()
    {
        MountId = MountId,
        Name = Name,
        Species = Species,
        Position = Position,
        Level = Level,
        Tamed = Tamed,
        Hungry = Hungry,
        Stats = Stats,
        Statuses = PlayerDataSnapshot.ApplyElapsed(Statuses, elapsedSeconds)
    };
}