using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class DiscordWebhookPayload
{
    [DataMember(Name = "content", Order = 1)]
    public string Content { get; set; } = "";

    [DataMember(Name = "username", Order = 2)]
    public string Username { get; set; } = "";
}