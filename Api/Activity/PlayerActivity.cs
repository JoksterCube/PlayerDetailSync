using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class PlayerActivity
{
    private PlayerActivityStatus _state = PlayerActivityStatus.IdlingAtBase;
    private PlayerActivityStatus[]? _activeStates = [];
    private bool _hasState;
    private bool _hasActiveStates;

    [IgnoreDataMember]
    public PlayerActivityStatus State
    {
        get => _state;
        set
        {
            _state = value;
            _hasState = true;
        }
    }

    [IgnoreDataMember]
    public PlayerActivityStatus[]? ActiveStates
    {
        get => _activeStates;
        set
        {
            _activeStates = value;
            _hasActiveStates = true;
        }
    }

    [IgnoreDataMember]
    internal bool HasState => _hasState;

    [IgnoreDataMember]
    internal bool HasActiveStates => _hasActiveStates;

    [DataMember(Name = "state", Order = 1, EmitDefaultValue = false)]
    private string? SerializedState
    {
        get => _hasState ? EnumWireValue.ToWireValue(State) : null;
        set => State = EnumWireValue.Parse<PlayerActivityStatus>(value);
    }

    [DataMember(Name = "activeStates", Order = 2)]
    private string[]? SerializedActiveStates
    {
        get => ActiveStates == null ? null : EnumWireValue.ToWireValues(ActiveStates);
        set => ActiveStates = value == null ? null : EnumWireValue.ParseValues<PlayerActivityStatus>(value);
    }

    [DataMember(Name = "observedAtUtc", Order = 3)]
    public string ObservedAtUtc { get; set; } = "";
}