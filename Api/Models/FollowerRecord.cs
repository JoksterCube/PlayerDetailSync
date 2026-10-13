using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

[DataContract]
internal sealed class FollowerRecord
{
    [DataMember(Name = "followerId", Order = 1)]
    public string FollowerId { get; set; } = "";

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

    [DataMember(Name = "alerted", Order = 8)]
    public bool Alerted { get; set; }

    [DataMember(Name = "stats", Order = 9)]
    public FollowerStats Stats { get; set; } = new();

    [DataMember(Name = "statuses", Order = 10)]
    public TimedPlayerData[] Statuses { get; set; } = [];

    [DataMember(Name = "followingPlayerId", Order = 11)]
    public string FollowingPlayerId { get; set; } = "";

    [DataMember(Name = "observedAtUtc", Order = 12)]
    public string ObservedAtUtc { get; set; } = "";

    [DataMember(Name = "characterDataAgeSeconds", Order = 13)]
    public float CharacterDataAgeSeconds { get; set; }

    internal static bool IsFollowingPlayer(bool followTargetMatches, string savedFollowName, string playerName) =>
        string.IsNullOrEmpty(savedFollowName)
            ? followTargetMatches
            : string.Equals(savedFollowName, playerName, StringComparison.Ordinal);

    internal static FollowerRecord[] Capture(Player player, IReadOnlyList<Character> characters)
    {
        var followers = new List<FollowerRecord>();
        string playerName = player.GetPlayerName();
        foreach (Character character in characters)
        {
            if (character == null || character.IsDead()) continue;
            MonsterAI ai = character.GetComponent<MonsterAI>();
            if (ai == null) continue;
            ZDOID id = character.GetZDOID();
            if (id == ZDOID.None) continue;
            bool followTargetMatches = ai.GetFollowTarget() == player.gameObject;
            string savedFollowName = ZDOMan.instance?.GetZDO(id)?.GetString(ZDOVars.s_follow, "") ?? "";
            if (!IsFollowingPlayer(followTargetMatches, savedFollowName, playerName)) continue;
            Tameable tameable = character.GetComponent<Tameable>();
            Vector3 position = character.transform.position;
            string name = character.GetHoverName();
            followers.Add(new FollowerRecord
            {
                FollowerId = id.ToString(),
                Name = string.IsNullOrWhiteSpace(name) ? PlayerDataSnapshot.LocalizeName(character.m_name) : name,
                Species = PlayerDataSnapshot.LocalizeName(character.m_name),
                Position = new PlayerPosition { X = position.x, Y = position.y, Z = position.z },
                Level = character.GetLevel(),
                Tamed = character.IsTamed(),
                Hungry = tameable != null ? tameable.IsHungry() : null,
                Alerted = ai.IsAlerted(),
                Stats = new FollowerStats
                {
                    Health = Math.Max(0f, character.GetHealth()),
                    MaxHealth = Math.Max(0f, character.GetMaxHealth())
                },
                Statuses = CharacterStatusCapture.Capture(character)
            });
        }
        followers.Sort((left, right) => string.CompareOrdinal(left.FollowerId, right.FollowerId));
        return [.. followers];
    }

    internal static FollowerRecord[] ApplyElapsed(FollowerRecord[] followers, float elapsedSeconds)
    {
        var result = new FollowerRecord[followers.Length];
        for (int index = 0; index < followers.Length; index++)
        {
            FollowerRecord follower = followers[index];
            result[index] = new FollowerRecord
            {
                FollowerId = follower.FollowerId, Name = follower.Name, Species = follower.Species,
                Position = follower.Position, Level = follower.Level, Tamed = follower.Tamed,
                Hungry = follower.Hungry, Alerted = follower.Alerted, Stats = follower.Stats,
                FollowingPlayerId = follower.FollowingPlayerId, ObservedAtUtc = follower.ObservedAtUtc,
                CharacterDataAgeSeconds = follower.CharacterDataAgeSeconds + elapsedSeconds,
                Statuses = PlayerDataSnapshot.ApplyElapsed(follower.Statuses, elapsedSeconds)
            };
        }
        return result;
    }
}
