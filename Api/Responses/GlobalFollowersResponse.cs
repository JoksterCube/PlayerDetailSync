using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class GlobalFollowersResponse
{
    [DataMember(Name = "updatedAtUtc", Order = 1)]
    public string UpdatedAtUtc { get; set; } = "";

    [DataMember(Name = "followers", Order = 2)]
    public FollowerRecord[] Followers { get; set; } = [];
}