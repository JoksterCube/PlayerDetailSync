using System;
using System.Collections.Generic;
using System.Globalization;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class EntityAssociations
{
    internal static void Reconcile(IReadOnlyList<PlayerRecord> players, IReadOnlyList<PlayerRecord> previous, string updatedAtUtc)
    {
        var oldShips = new Dictionary<string, ShipRecord>(StringComparer.Ordinal);
        foreach (PlayerRecord player in previous)
            if (player.Ship != null) oldShips[player.Ship.ShipId] = player.Ship;

        var mountOwners = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        var followerOwners = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        var ships = new Dictionary<string, ShipRecord>(StringComparer.Ordinal);
        foreach (PlayerRecord player in players)
        {
            string sampledAtUtc = SampleTime(updatedAtUtc, player.CharacterDataAgeSeconds);
            if (player.Mount != null)
            {
                player.Mount = player.Mount.WithElapsedSeconds(0f);
                MountRecord mount = player.Mount;
                if (string.IsNullOrEmpty(mount.ObservedAtUtc))
                {
                    mount.ObservedAtUtc = sampledAtUtc;
                    mount.CharacterDataAgeSeconds = player.CharacterDataAgeSeconds;
                }
                mount.RiderPlayerId = player.PlayerId;
                mount.CharacterDataAgeSeconds = Age(updatedAtUtc, mount.ObservedAtUtc);
                mount.Riding = player.Online && player.Riding;
                if (!mountOwners.TryGetValue(mount.MountId, out PlayerRecord? owner) ||
                    Prefer(mount.ObservedAtUtc, owner.Mount!.ObservedAtUtc, mount.Riding, owner.Mount.Riding, player.PlayerId, owner.PlayerId))
                    mountOwners[mount.MountId] = player;
            }

            player.Followers = FollowerRecord.ApplyElapsed(player.Followers, 0f);
            foreach (FollowerRecord follower in player.Followers)
            {
                if (string.IsNullOrEmpty(follower.ObservedAtUtc))
                {
                    follower.ObservedAtUtc = sampledAtUtc;
                    follower.CharacterDataAgeSeconds = player.CharacterDataAgeSeconds;
                }
                follower.FollowingPlayerId = player.PlayerId;
                follower.CharacterDataAgeSeconds = Age(updatedAtUtc, follower.ObservedAtUtc);
                if (!followerOwners.TryGetValue(follower.FollowerId, out PlayerRecord? owner) ||
                    Prefer(sampledAtUtc, SampleTime(updatedAtUtc, owner.CharacterDataAgeSeconds), player.Online, owner.Online, player.PlayerId, owner.PlayerId))
                    followerOwners[follower.FollowerId] = player;
            }

            if (player.Ship == null) continue;
            player.Ship = player.Ship.Copy();
            ShipRecord ship = player.Ship;
            if (string.IsNullOrEmpty(ship.ObservedAtUtc))
            {
                ship.ObservedAtUtc = sampledAtUtc;
                ship.CharacterDataAgeSeconds = player.CharacterDataAgeSeconds;
                ship.LastDriverPlayerId = player.Aboard && ship.AtHelm ? player.PlayerId : "";
                ship.LastDrivenAtUtc = string.IsNullOrEmpty(ship.LastDriverPlayerId) ? "" : sampledAtUtc;
            }
            if (oldShips.TryGetValue(ship.ShipId, out ShipRecord? oldShip))
                RetainDriver(ship, oldShip);
            if (!ships.TryGetValue(ship.ShipId, out ShipRecord? current))
                ships[ship.ShipId] = ship;
            else if (string.CompareOrdinal(ship.ObservedAtUtc, current.ObservedAtUtc) > 0)
            {
                RetainDriver(ship, current);
                ships[ship.ShipId] = ship;
            }
            else
                RetainDriver(current, ship);
        }

        foreach (PlayerRecord player in players)
        {
            if (player.Mount != null && mountOwners[player.Mount.MountId] != player)
            {
                player.Mount = null;
                player.Riding = false;
            }
            player.Followers = Array.FindAll(player.Followers, follower => followerOwners[follower.FollowerId] == player);
        }

        foreach (ShipRecord ship in ships.Values)
        {
            var passengers = new List<string>();
            var currentPassengers = new List<string>();
            bool atHelm = false;
            foreach (PlayerRecord player in players)
            {
                if (player.Ship?.ShipId != ship.ShipId) continue;
                passengers.Add(player.PlayerId);
                if (player.Online && player.Aboard)
                {
                    currentPassengers.Add(player.PlayerId);
                    atHelm |= player.PlayerId == ship.LastDriverPlayerId && player.Ship.AtHelm;
                }
                player.Ship = ship;
            }
            passengers.Sort(StringComparer.Ordinal);
            currentPassengers.Sort(StringComparer.Ordinal);
            ship.PassengerPlayerIds = passengers.ToArray();
            ship.CurrentPassengerPlayerIds = currentPassengers.ToArray();
            ship.AtHelm = atHelm;
            ship.CharacterDataAgeSeconds = Age(updatedAtUtc, ship.ObservedAtUtc);
        }
    }

    private static void RetainDriver(ShipRecord ship, ShipRecord previous)
    {
        if (string.IsNullOrEmpty(previous.LastDriverPlayerId) ||
            string.CompareOrdinal(previous.LastDrivenAtUtc, ship.LastDrivenAtUtc) <= 0) return;
        ship.LastDriverPlayerId = previous.LastDriverPlayerId;
        ship.LastDrivenAtUtc = previous.LastDrivenAtUtc;
    }

    private static bool Prefer(string observed, string previous, bool active, bool previousActive, string playerId, string previousId)
    {
        int order = string.CompareOrdinal(observed, previous);
        if (order != 0) return order > 0;
        if (active != previousActive) return active;
        return string.CompareOrdinal(playerId, previousId) < 0;
    }

    private static float Age(string timestamp, string observedAtUtc) => Math.Max(0f, (float)(
        DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) -
        DateTime.Parse(observedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds);

    private static string SampleTime(string timestamp, float ageSeconds) =>
        DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .AddSeconds(-Math.Max(0f, ageSeconds)).ToString("O", CultureInfo.InvariantCulture);
}