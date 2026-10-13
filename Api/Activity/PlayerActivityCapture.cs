using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class PlayerActivityCapture
{
    private static readonly List<ActivityDetector> ActivityDetectors =
    [
        new(PlayerActivityStatus.Dead, 10, true, player => player.IsDead()),
        new(PlayerActivityStatus.RecoveringCorpse, 20, true, player => HasStatus(player, "CorpseRun")),
        new(PlayerActivityStatus.Drowning, 30, true, player => HasStatus(player, "Drowning") || HasRecentAction(player, PlayerActivityStatus.Drowning)),
        new(PlayerActivityStatus.Freezing, 40, true, player => HasStatus(player, "Freezing")),
        new(PlayerActivityStatus.Burning, 50, true, player => HasStatus(player, "Burning")),
        new(PlayerActivityStatus.FightingBoss, 60, true,
            player => HasRecentAction(player, PlayerActivityStatus.FightingBoss), IsFightingBoss),
        new(PlayerActivityStatus.HuntingSerpent, 70, true, player => HasRecentAction(player, PlayerActivityStatus.HuntingSerpent)),
        new(PlayerActivityStatus.DefendingBase, 80, true, IsDefendingBase),
        new(PlayerActivityStatus.InPvP, 90, true, player => HasRecentAction(player, PlayerActivityStatus.InPvP)),
        new(PlayerActivityStatus.SiegingFortress, 95, false, _ => false, IsSiegingFortress),
        new(PlayerActivityStatus.DungeonDelving, 96, false, player => player.transform.position.y > 3000f),
        new(PlayerActivityStatus.InCombat, 100, true, player => player.InAttack() || player.IsBlocking() || player.IsTargeted() || HasRecentAction(player, PlayerActivityStatus.InCombat)),
        new(PlayerActivityStatus.LostAtSea, 115, false, IsLostAtSea),
        new(PlayerActivityStatus.Sailing, 120, false, IsSailing),
        new(PlayerActivityStatus.Portaling, 140, true, player => player.IsTeleporting()),
        new(PlayerActivityStatus.CartHauling, 150, false, IsCartHauling),
        new(PlayerActivityStatus.Building, 160, false, IsBuilding),
        new(PlayerActivityStatus.Crafting, 165, false, IsCrafting),
        new(PlayerActivityStatus.Cooking, 166, false, player => HasRecentAction(player, PlayerActivityStatus.Cooking)),
        new(PlayerActivityStatus.Brewing, 167, false, player => HasRecentAction(player, PlayerActivityStatus.Brewing)),
        new(PlayerActivityStatus.Smelting, 168, false, player => HasRecentAction(player, PlayerActivityStatus.Smelting)),
        new(PlayerActivityStatus.SortingChests, 169, false, player => HasRecentAction(player, PlayerActivityStatus.SortingChests)),
        new(PlayerActivityStatus.Fishing, 170, false, IsFishing),
        new(PlayerActivityStatus.Terraforming, 171, false, player => HasRecentAction(player, PlayerActivityStatus.Terraforming)),
        new(PlayerActivityStatus.Mining, 172, false, player => HasRecentAction(player, PlayerActivityStatus.Mining)),
        new(PlayerActivityStatus.Logging, 173, false, player => HasRecentAction(player, PlayerActivityStatus.Logging)),
        new(PlayerActivityStatus.Farming, 174, false, player => HasRecentAction(player, PlayerActivityStatus.Farming)),
        new(PlayerActivityStatus.TamingRanching, 175, false, player => HasRecentAction(player, PlayerActivityStatus.TamingRanching)),
        new(PlayerActivityStatus.Trading, 176, false, IsTrading),
        new(PlayerActivityStatus.Hunting, 177, false, player => HasRecentAction(player, PlayerActivityStatus.Hunting)),
        new(PlayerActivityStatus.Gathering, 178, false, player => HasRecentAction(player, PlayerActivityStatus.Gathering)),
        new(PlayerActivityStatus.StalkingSneaking, 210, false, player => player.IsCrouching()),
        new(PlayerActivityStatus.Encumbered, 220, false, player => player.IsEncumbered()),
        new(PlayerActivityStatus.Sleeping, 230, false, player => player.IsSleeping()),
        new(PlayerActivityStatus.Resting, 240, false, player => HasStatus(player, "Resting")),
        new(PlayerActivityStatus.Riding, 250, false, player => player.GetDoodadController() is Sadle),
        new(PlayerActivityStatus.Swimming, 260, false, player => player.IsSwimming()),
        new(PlayerActivityStatus.Running, 270, false, player => player.IsRunning()),
        new(PlayerActivityStatus.Sitting, 280, false, player => player.IsSitting()),
        new(PlayerActivityStatus.Exploring, 290, false, player => HasRecentAction(player, PlayerActivityStatus.Exploring))
    ];

    private static readonly Dictionary<PlayerActivityStatus, float> RecentActions = [];
    private static readonly Dictionary<int, float> RecentContainers = [];
    private static readonly List<int> ExpiredContainerIds = [];
    static PlayerActivityCapture() =>
        ActivityDetectors.Sort((left, right) => left.Priority.CompareTo(right.Priority));

    private static PlayerActivityTracker? _tracker;
    private static Player? _lastPlayer;
    private static bool _joining;
    private static bool _enabled;
    private static float _recentActionWindowSeconds = 20f;
    private static float _nextFishingScanTime;
    private static bool _hasOwnedFishingFloat;
    private static bool _hasCorpseRun;
    private static bool _hasDrowningEffect;
    private static bool _hasFreezing;
    private static bool _hasBurning;
    private static bool _hasResting;

    internal static PlayerActivity? Capture(
        Player player,
        bool enabled,
        float confirmationPeriodSeconds,
        IReadOnlyList<StatusEffect>? statusEffects,
        IReadOnlyList<Character> characters)
    {
        if (!ReferenceEquals(player, Player.m_localPlayer)) return null;

        if (!enabled)
        {
            _enabled = false;
            RecentActions.Clear();
            RecentContainers.Clear();
            ExpiredContainerIds.Clear();
            _tracker = null;
            return null;
        }

        _enabled = true;
        if (!ReferenceEquals(player, _lastPlayer))
        {
            _lastPlayer = player;
            _joining = true;
            _tracker = null;
        }
        _recentActionWindowSeconds = Mathf.Clamp(confirmationPeriodSeconds + 5f, 20f, 65f);
        UpdateActivityEffects(statusEffects);
        long playerId = player.GetPlayerID();
        if (_tracker == null || _tracker.PlayerId != playerId)
        {
            _tracker = new PlayerActivityTracker(playerId, ActivityDetectors, _joining);
            _joining = false;
        }

        return _tracker.Capture(player, Mathf.Clamp(confirmationPeriodSeconds, 0f, 60f), characters);
    }

    internal static void OnPlayerSpawned(Player player)
    {
        if (!ReferenceEquals(player, Player.m_localPlayer)) return;

        _joining = true;
        _tracker = null;
        RecentActions.Clear();
        RecentContainers.Clear();
        ExpiredContainerIds.Clear();
        _nextFishingScanTime = 0f;
        _hasOwnedFishingFloat = false;
    }

    internal static void RecordAction(Player player, PlayerActivityStatus status)
    {
        if (!_enabled || !ReferenceEquals(player, Player.m_localPlayer)) return;
        RecentActions[status] = Time.realtimeSinceStartup;
    }

    internal static void RecordDamage(Player target, HitData hit)
    {
        if (!_enabled) return;

        Player? localPlayer = Player.m_localPlayer;
        if (target == localPlayer)
        {
            RecordAction(target, PlayerActivityStatus.InCombat);
            float maxStamina = target.GetMaxStamina();
            if (target.IsSwimming() && maxStamina > 0f && target.GetStamina() <= maxStamina * 0.05f)
                RecordAction(target, PlayerActivityStatus.Drowning);
        }

        Player? attacker = hit.GetAttacker() as Player;
        if (attacker != localPlayer || localPlayer == null) return;
        RecordAction(localPlayer, PlayerActivityStatus.InCombat);
        if (target.IsPVPEnabled() && localPlayer.IsPVPEnabled())
        {
            RecordAction(localPlayer, PlayerActivityStatus.InPvP);
            if (target == localPlayer)
                RecordAction(localPlayer, PlayerActivityStatus.InPvP);
        }
    }

    internal static void RecordHuntingAction(Player player, Character target)
    {
        if (!ReferenceEquals(player, Player.m_localPlayer)) return;
        string targetName = target.m_name ?? target.name;
        if (targetName.IndexOf("serpent", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.HuntingSerpent);
        else if (targetName.IndexOf("deer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 targetName.IndexOf("hare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 targetName.IndexOf("bird", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.Hunting);
    }

    internal static void RecordUsedTool(Player player, ItemDrop.ItemData item)
    {
        if (!ReferenceEquals(player, Player.m_localPlayer)) return;
        string itemName = item.m_shared?.m_name ?? "";
        if (itemName.IndexOf("hoe", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.Terraforming);
        else if (itemName.IndexOf("cultivator", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.Farming);
        else if (itemName.IndexOf("pickaxe", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.Mining);
        else if (itemName.IndexOf("axe", StringComparison.OrdinalIgnoreCase) >= 0)
            RecordAction(player, PlayerActivityStatus.Logging);
    }

    internal static void RecordContainerOpened(Player player, Container container)
    {
        if (!_enabled || !ReferenceEquals(player, Player.m_localPlayer)) return;

        float now = Time.realtimeSinceStartup;
        ExpiredContainerIds.Clear();
        foreach (KeyValuePair<int, float> entry in RecentContainers)
            if (now - entry.Value > 60f) ExpiredContainerIds.Add(entry.Key);
        foreach (int instanceId in ExpiredContainerIds) RecentContainers.Remove(instanceId);

        RecentContainers[container.GetInstanceID()] = now;
        if (RecentContainers.Count >= 2) RecordAction(player, PlayerActivityStatus.SortingChests);
    }

    internal static void RecordStationInteraction(Player player, GameObject target)
    {
        if (!_enabled || !ReferenceEquals(player, Player.m_localPlayer)) return;

        if (target.GetComponentInParent<Pickable>() != null)
            RecordAction(player, PlayerActivityStatus.Gathering);

        string targetName = target.name;
        if (target.GetComponentInParent<CookingStation>() != null ||
            targetName.IndexOf("cauldron", StringComparison.OrdinalIgnoreCase) >= 0 ||
            targetName.IndexOf("bakingoven", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            RecordAction(player, PlayerActivityStatus.Cooking);
        }

        if (target.GetComponentInParent<Smelter>() != null ||
            targetName.IndexOf("blastfurnace", StringComparison.OrdinalIgnoreCase) >= 0 ||
            targetName.IndexOf("charcoalkiln", StringComparison.OrdinalIgnoreCase) >= 0 ||
            targetName.IndexOf("eitrrefinery", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            RecordAction(player, PlayerActivityStatus.Smelting);
        }
    }

    private static bool HasStatus(Player player, string nameFragment)
    {
        return nameFragment switch
        {
            "CorpseRun" => _hasCorpseRun,
            "Drowning" => _hasDrowningEffect,
            "Freezing" => _hasFreezing,
            "Burning" => _hasBurning,
            "Resting" => _hasResting,
            _ => false
        };
    }

    private static void UpdateActivityEffects(IReadOnlyList<StatusEffect>? statusEffects)
    {
        _hasCorpseRun = false;
        _hasDrowningEffect = false;
        _hasFreezing = false;
        _hasBurning = false;
        _hasResting = false;
        if (statusEffects == null) return;

        foreach (StatusEffect status in statusEffects)
        {
            if (status == null) continue;
            string typeName = status.GetType().Name;
            string effectName = status.m_name ?? "";
            _hasCorpseRun |= Contains(typeName, effectName, "CorpseRun");
            _hasDrowningEffect |= Contains(typeName, effectName, "Drowning");
            _hasFreezing |= Contains(typeName, effectName, "Freezing");
            _hasBurning |= Contains(typeName, effectName, "Burning");
            _hasResting |= Contains(typeName, effectName, "Resting");
        }
    }

    private static bool Contains(string typeName, string effectName, string nameFragment) =>
        typeName.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0 ||
        effectName.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsFishing(Player player)
    {
        float now = Time.realtimeSinceStartup;
        if (now < _nextFishingScanTime) return _hasOwnedFishingFloat;

        _nextFishingScanTime = now + 2f;
        _hasOwnedFishingFloat = false;
        foreach (FishingFloat fishingFloat in UnityEngine.Object.FindObjectsByType<FishingFloat>(FindObjectsSortMode.None))
        {
            if (fishingFloat == null || fishingFloat.GetOwner() != player) continue;
            _hasOwnedFishingFloat = true;
            break;
        }
        return _hasOwnedFishingFloat;
    }

    private static bool IsBuilding(Player player) =>
        player.GetBuildTool() != null && player.GetPlacementStatus() == Player.PlacementStatus.Valid;

    private static bool IsCrafting(Player player) => player.GetCurrentCraftingStation() != null;

    private static bool IsSailing(Player player) =>
        player.GetControlledShip() != null || player.IsAttachedToShip();

    private static bool IsLostAtSea(Player player)
    {
        if (player.GetCurrentBiome() != Heightmap.Biome.Ocean) return false;

        Vector3 position = player.transform.position;
        const float landSearchDistance = 250f;
        for (int sample = 0; sample < 8; sample++)
        {
            float angle = sample * (Mathf.PI / 4f);
            var samplePosition = new Vector3(
                position.x + Mathf.Cos(angle) * landSearchDistance,
                position.y,
                position.z + Mathf.Sin(angle) * landSearchDistance);
            if (Heightmap.FindBiome(samplePosition) != Heightmap.Biome.Ocean) return false;
        }

        return true;
    }

    private static bool IsSiegingFortress(Player player, IReadOnlyList<Character> characters)
    {
        if (player.GetCurrentBiome() != Heightmap.Biome.AshLands) return false;

        Vector3 position = player.transform.position;
        ZoneSystem? zoneSystem = ZoneSystem.instance;
        if (zoneSystem == null || !zoneSystem.FindClosestLocation("Ashlands_Fortress", position, out var fortress) ||
            (fortress.m_position - position).sqrMagnitude > 3600f)
        {
            return false;
        }

        foreach (Character character in characters)
        {
            if (character == null || character.IsDead() ||
                (character.transform.position - position).sqrMagnitude > 3600f)
            {
                continue;
            }

            BaseAI? ai = character.GetComponent<BaseAI>();
            if (ai != null && ai.IsEnemy(player)) return true;
        }

        return false;
    }

    private static bool IsTrading(Player player) => StoreGui.IsVisible();

    private static bool IsCartHauling(Player player)
    {
        Transform attachPoint = player.GetAttachPoint();
        return attachPoint != null && attachPoint.GetComponentInParent<Vagon>() != null;
    }

    private static bool IsFightingBoss(Player player, IReadOnlyList<Character> characters)
    {
        if (!player.IsTargeted()) return false;

        foreach (Character character in characters)
        {
            if (character == null || character == player || character.IsDead() || !character.IsBoss() ||
                (character.transform.position - player.transform.position).sqrMagnitude > 2500f)
            {
                continue;
            }

            BaseAI? ai = character.GetComponent<BaseAI>();
            if (ai != null && ai.GetTargetCreature() == player) return true;
        }
        return false;
    }

    private static bool IsDefendingBase(Player player)
    {
        RandEventSystem? eventSystem = RandEventSystem.instance;
        RandomEvent? currentEvent = eventSystem?.GetCurrentRandomEvent();
        return currentEvent != null && eventSystem!.IsInsideRandomEventArea(currentEvent, player.transform.position);
    }

    private static bool HasRecentAction(Player player, PlayerActivityStatus status) =>
        RecentActions.TryGetValue(status, out float recordedAt) &&
        IsActionRecent(status, recordedAt);

    private static bool IsActionRecent(PlayerActivityStatus status, float recordedAt)
    {
        if (Time.realtimeSinceStartup - recordedAt <= _recentActionWindowSeconds) return true;
        RecentActions.Remove(status);
        return false;
    }

}