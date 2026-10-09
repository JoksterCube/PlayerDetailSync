using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class PlayerStatistic
{
    private PlayerStatisticName _name;
    private PlayerStatisticUnit _unit;
    private bool _hasName;
    private bool _hasUnit;

    [DataMember(Name = "name", Order = 1)]
    private string? SerializedName
    {
        get => EnumWireValue.ToWireValue(Name);
        set => Name = EnumWireValue.Parse<PlayerStatisticName>(value);
    }

    [DataMember(Name = "unit", Order = 3)]
    private string? SerializedUnit
    {
        get => EnumWireValue.ToWireValue(Unit);
        set => Unit = EnumWireValue.Parse<PlayerStatisticUnit>(value);
    }

    [IgnoreDataMember]
    public PlayerStatisticName Name
    {
        get => _name;
        set
        {
            _name = value;
            _hasName = true;
        }
    }

    [IgnoreDataMember]
    public PlayerStatisticUnit Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            _hasUnit = true;
        }
    }

    [IgnoreDataMember]
    internal bool HasName => _hasName;

    [IgnoreDataMember]
    internal bool HasUnit => _hasUnit;

    [DataMember(Name = "value", Order = 2)]
    public float Value { get; set; }

}