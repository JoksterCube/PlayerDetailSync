using System;
using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class WebhookEvent
{
    [IgnoreDataMember]
    internal WebhookEventType Event { get; set; }

    [DataMember(Name = "event", Order = 1)]
    private string SerializedEvent
    {
        get => ToSnakeCase(Event.ToString());
        set
        {
            string enumName = value.Replace("_", "");
            if (!Enum.TryParse(enumName, true, out WebhookEventType parsedEvent) ||
                !Enum.IsDefined(typeof(WebhookEventType), parsedEvent))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Event = parsedEvent;
        }
    }

    internal static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0 && char.IsLower(value[index - 1]))
                result.Append('_');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    [DataMember(Name = "occurredAtUtc", Order = 2)]
    public string OccurredAtUtc { get; set; } = "";

    [DataMember(Name = "playerId", Order = 3, EmitDefaultValue = false)]
    public string? PlayerId { get; set; }

    [DataMember(Name = "playerName", Order = 4, EmitDefaultValue = false)]
    public string? PlayerName { get; set; }

    [DataMember(Name = "target", Order = 5, EmitDefaultValue = false)]
    public string? Target { get; set; }

    [DataMember(Name = "key", Order = 6, EmitDefaultValue = false)]
    public string? Key { get; set; }

    [DataMember(Name = "cause", Order = 7, EmitDefaultValue = false)]
    public string? Cause { get; set; }

    [DataMember(Name = "killerId", Order = 8, EmitDefaultValue = false)]
    public string? KillerId { get; set; }

    [DataMember(Name = "killerName", Order = 9, EmitDefaultValue = false)]
    public string? KillerName { get; set; }

    [DataMember(Name = "position", Order = 10, EmitDefaultValue = false)]
    public PlayerPosition? Position { get; set; }

    internal WebhookEvent As(WebhookEventType eventType)
    {
        var copy = (WebhookEvent)MemberwiseClone();
        copy.Event = eventType;
        return copy;
    }
}