param([string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$gamePath = $env:VALHEIM_GAME_PATH
if ([string]::IsNullOrWhiteSpace($gamePath)) {
    $localPropsPath = Join-Path $root 'environment.local.props'
    if (Test-Path $localPropsPath) {
        [xml]$localProps = Get-Content $localPropsPath -Raw
        $gamePath = [string]$localProps.Project.PropertyGroup.ValheimGamePath
    }
}
if ([string]::IsNullOrWhiteSpace($gamePath)) { throw 'Set VALHEIM_GAME_PATH to the installed Valheim directory.' }
$managed = Join-Path $gamePath 'valheim_Data\Managed'
foreach ($name in @('UnityEngine.CoreModule.dll', 'UnityEngine.dll', 'assembly_utils.dll', 'assembly_valheim.dll', 'Splatform.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $managed $name)) | Out-Null
}
$bepInExCore = Join-Path $gamePath 'BepInEx\core'
foreach ($name in @('BepInEx.dll', '0Harmony.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $bepInExCore $name)) | Out-Null
}
$assemblyPath = Join-Path $root "bin\$Configuration\PlayerDetailSync.dll"
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))
$recordType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerRecord', $true)
$snapshotType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerSnapshot', $true)
$statsType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerStats', $true)
$timedType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.TimedPlayerData', $true)
$dataType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerDataSnapshot', $true)
$gearType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.EquippedGear', $true)
$followerType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.FollowerRecord', $true)
$characterStatusCaptureType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.CharacterStatusCapture', $true)
$followerStatsType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.FollowerStats', $true)
$mountType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.MountRecord', $true)
$mountStatsType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.MountStats', $true)
$shipType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.ShipRecord', $true)
$statisticType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerStatistic', $true)
$gameAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'assembly_valheim.dll'))
$gameStatType = $gameAssembly.GetType('PlayerStatType', $true)
$activityType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivity', $true)
$activityStatusType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivityStatus', $true)
$validatorType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerReportValidator', $true)
$retryType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.ClientRefreshRetry', $true)
$storeType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerSnapshotStore', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$mergeMethod = $storeType.GetMethod('MergeAndSave', $flags)
$loadMethod = $storeType.GetMethod('Load', $flags)
$currentProperty = $storeType.GetProperty('Current', $flags)
$isFollowingPlayer = $followerType.GetMethod('IsFollowingPlayer', $flags)
Add-Type -TypeDefinition @'
public static class CacheRegressionLogging
{
    public static void Ignore(string message) { System.Console.WriteLine(message); }
}
'@
$logger = [Action[string]][CacheRegressionLogging]::Ignore
$directory = Join-Path ([IO.Path]::GetTempPath()) ('PlayerDetailSync-cache-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$watcher = [IO.FileSystemWatcher]::new($directory, 'JoksterCube.PlayerDetailSync.players.json')
$watcher.EnableRaisingEvents = $true

function Assert-Cache([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-CacheMethod([Reflection.MethodInfo]$Method, [object[]]$Arguments) {
    $unwrapped = [object[]]::new($Arguments.Count)
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $unwrapped[$index] = $Arguments[$index].PSObject.BaseObject
    }
    return $Method.Invoke($null, $unwrapped)
}

function New-Snapshot([object[]]$Records) {
    $snapshot = [Activator]::CreateInstance($snapshotType)
    $snapshot.UpdatedAtUtc = [DateTime]::UtcNow.ToString('O')
    $array = [Array]::CreateInstance($recordType, $Records.Count)
    for ($index = 0; $index -lt $Records.Count; $index++) { $array.SetValue($Records[$index], $index) }
    $snapshot.Players = $array
    return $snapshot
}

function Resolve-Entities([object[]]$Records, [object[]]$Previous = @()) {
    $snapshot = New-Snapshot $Records
    $prior = New-Snapshot $Previous
    $resolver = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.EntityAssociations', $true).GetMethod('Reconcile', $flags)
    Invoke-CacheMethod $resolver @($snapshot.Players, $prior.Players, $snapshot.UpdatedAtUtc) | Out-Null
    return $snapshot
}

function New-ActivityRecord([string]$State, [bool]$HasReport = $true) {
    $player = [Activator]::CreateInstance($recordType)
    $player.PlayerId = '800'
    $player.Name = 'Activity history player'
    $player.Online = $true
    $player.CharacterDataAvailable = $HasReport
    if ($State) {
        $player.Activity = [Activator]::CreateInstance($activityType)
        $player.Activity.State = [Enum]::Parse($activityStatusType, $State)
        $player.Activity.ActiveStates = [Array]::CreateInstance($activityStatusType, 1)
        $player.Activity.ActiveStates.SetValue($player.Activity.State, 0)
        $player.Activity.ObservedAtUtc = [DateTime]::UtcNow.ToString('O')
    }
    return $player
}

try {
    $trackerType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivityTracker', $true)
    $fallbackMethod = $trackerType.GetMethod('SelectFallback', $flags)
    foreach ($case in @(
        @{ Joining = $false; Travel = $false; Idle = 0; Base = 0; Comfort = 0; Expected = 'Unclassified' },
        @{ Joining = $true; Travel = $false; Idle = 30; Base = 0; Comfort = 0; Expected = 'Joining' },
        @{ Joining = $false; Travel = $false; Idle = 89; Base = 3; Comfort = 5; Expected = 'Unclassified' },
        @{ Joining = $false; Travel = $false; Idle = 90; Base = 3; Comfort = 5; Expected = 'IdlingAtBase' },
        @{ Joining = $false; Travel = $false; Idle = 90; Base = 1; Comfort = 2; Expected = 'IdlingAtOutpost' },
        @{ Joining = $false; Travel = $false; Idle = 90; Base = 0; Comfort = 0; Expected = 'IdlingInDanger' },
        @{ Joining = $true; Travel = $true; Idle = 0; Base = 0; Comfort = 0; Expected = 'Adventuring' }
    )) {
        $fallback = Invoke-CacheMethod $fallbackMethod @([bool]$case.Joining, [bool]$case.Travel, [float]$case.Idle, [float]15, [int]$case.Base, [int]$case.Comfort)
        Assert-Cache ($fallback.ToString() -ceq $case.Expected) "Activity fallback expected $($case.Expected), got $fallback."
    }
    Write-Output 'PASS: pending activity, joining, travel, and contextual idle fallbacks.'

    $scheduleType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.ClientReportSchedule', $true)
    $schedule = [Activator]::CreateInstance($scheduleType, $true)
    $scheduleFlags = [Reflection.BindingFlags]'Instance,NonPublic'
    $resetSchedule = $scheduleType.GetMethod('Reset', $scheduleFlags)
    $isReportDue = $scheduleType.GetMethod('IsDue', $scheduleFlags)
    $canSendReport = $scheduleType.GetMethod('CanSend', $scheduleFlags)
    $recordReport = $scheduleType.GetMethod('RecordSend', $scheduleFlags)
    $acceptReport = $scheduleType.GetMethod('Accept', $scheduleFlags)
    foreach ($connection in 1..2) {
        $resetSchedule.Invoke($schedule, @()) | Out-Null
        Assert-Cache ($isReportDue.Invoke($schedule, [object[]]@([float]0))) "Connection $connection did not allow an immediate join report."
        $recordReport.Invoke($schedule, [object[]]@([float]0, [float]60)) | Out-Null
        Assert-Cache (-not $canSendReport.Invoke($schedule, [object[]]@([float]0.5))) 'Join reporting bypassed the one-second capture throttle.'
        Assert-Cache ($isReportDue.Invoke($schedule, [object[]]@([float]1))) 'Unacknowledged join reports must retry every second, not at the configured interval.'
        $recordReport.Invoke($schedule, [object[]]@([float]1, [float]60)) | Out-Null
        Assert-Cache ($isReportDue.Invoke($schedule, [object[]]@([float]2))) 'A dropped or rejected join report stopped retrying.'
        $acceptReport.Invoke($schedule, @()) | Out-Null
        $recordReport.Invoke($schedule, [object[]]@([float]2, [float]60)) | Out-Null
        Assert-Cache (-not $isReportDue.Invoke($schedule, [object[]]@([float]3))) 'Accepted reports continued using the join retry interval.'
        Assert-Cache ($isReportDue.Invoke($schedule, [object[]]@([float]62))) 'Accepted reports did not resume the configured interval.'
    }
    Write-Output 'PASS: join/rejoin resets stale client send state and retries until server acceptance.'

    $networkPlayerIdMethod = $snapshotType.GetMethod('GetNetworkPlayerId', $flags)
    Assert-Cache ((Invoke-CacheMethod $networkPlayerIdMethod @($null)) -eq 0) 'Missing character network data must not invent a player ID.'
    $networkCharacter = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([ZDO])
    Assert-Cache ((Invoke-CacheMethod $networkPlayerIdMethod @($networkCharacter)) -eq 0) 'Unidentified character network data must not invent a player ID.'
    $playerIdKey = [ZDOVars].GetField('s_playerID', [Reflection.BindingFlags]'Static,Public,NonPublic').GetValue($null)
    $networkId = [ZDO].GetField('m_uid', [Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($networkCharacter)
    $setNetworkLong = [ZDOExtraData].GetMethod('Set', [Reflection.BindingFlags]'Static,Public,NonPublic', $null, [type[]]@([ZDOID], [int], [long]), $null)
    $networkRecords = foreach ($characterId in @([long]123456, [long]654321)) {
        Invoke-CacheMethod $setNetworkLong @($networkId, [int]$playerIdKey, $characterId) | Out-Null
        $resolvedId = Invoke-CacheMethod $networkPlayerIdMethod @($networkCharacter)
        Assert-Cache ($resolvedId -eq $characterId) 'Character ID was not resolved without a loaded Player object or client report.'
        $networkRecord = [Activator]::CreateInstance($recordType)
        $networkRecord.PlayerId = $resolvedId.ToString([Globalization.CultureInfo]::InvariantCulture)
        $networkRecord.Name = "Network player $characterId"
        $networkRecord.Online = $true
        $networkRecord
    }

    $steamIdentityMethod = $snapshotType.GetMethod('GetSteamUserId', $flags)
    Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @('76561198000000000')) -ceq '76561198000000000') 'Native Steam socket identity was not resolved.'
    $crossplaySteamIdentity = [Splatform.PlatformUserID]::new('Steam', '76561198000000000').ToString()
    Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @($crossplaySteamIdentity)) -ceq '76561198000000000') 'Crossplay Steam socket identity was not resolved.'
    foreach ($hostName in @('', '0', 'Steam_not-an-id', 'PlayFab_76561198000000000', [Splatform.PlatformUserID]::new('Xbox', '76561198000000000').ToString())) {
        Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @($hostName)) -ceq '') 'Non-Steam or invalid identity was exposed as a Steam account.'
    }

    $webhookEventType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.WebhookEvent', $true)
    $webhookEventEnumType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.WebhookEventType', $true)
    $webhookFormatType = $assembly.GetType('JoksterCube.PlayerDetailSync.Settings.Groups.WebhookFormat', $true)
    $webhookInstanceFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $webhookEventProperty = $webhookEventType.GetProperty('Event', $webhookInstanceFlags)
    $webhookSerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($webhookEventType)
    $webhookWireNames = [ordered]@{
        BossKill = 'boss_kill'
        PvpKill = 'pvp_kill'
        FirstMinibossKill = 'first_miniboss_kill'
        MinibossKill = 'miniboss_kill'
        RaidStarted = 'raid_started'
        RaidCompleted = 'raid_completed'
        PlayerDeath = 'player_death'
        PlayerLogin = 'player_login'
        PlayerLogoff = 'player_logoff'
        FirstBossKill = 'first_boss_kill'
    }
    foreach ($eventName in $webhookWireNames.Keys) {
        $webhookEvent = [Activator]::CreateInstance($webhookEventType, $true)
        $webhookEventProperty.SetValue($webhookEvent, [Enum]::Parse($webhookEventEnumType, $eventName))
        $eventStream = [IO.MemoryStream]::new()
        try {
            $webhookSerializer.WriteObject($eventStream, $webhookEvent)
            $wireEvent = [Text.Encoding]::UTF8.GetString($eventStream.ToArray()) | ConvertFrom-Json
            Assert-Cache ($wireEvent.event -ceq $webhookWireNames[$eventName]) "Webhook event '$eventName' serialized to '$($wireEvent.event)'."
            $eventStream.Position = 0
            $eventRoundTrip = $webhookSerializer.ReadObject($eventStream)
            Assert-Cache ($webhookEventProperty.GetValue($eventRoundTrip).ToString() -ceq $eventName) "Webhook event '$eventName' did not deserialize from its wire name."
        }
        finally { $eventStream.Dispose() }
    }

    $bossEvent = [Activator]::CreateInstance($webhookEventType, $true)
    $webhookEventProperty.SetValue($bossEvent, [Enum]::Parse($webhookEventEnumType, 'BossKill'))
    $bossEvent.OccurredAtUtc = '2026-10-09T12:00:00Z'
    $bossEvent.PlayerId = '42'
    $bossEvent.PlayerName = 'Alice'
    $bossEvent.Target = 'Eikthyr'
    $bossEvent.Key = 'defeated_eikthyr'
    $formatterType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.WebhookDiscordTemplateFormatter', $true)
    $formatTemplateMethod = $formatterType.GetMethod('Format', [Reflection.BindingFlags]'Static,NonPublic')
    $allTagsTemplate = '{PLAYER NAME}|{player_name}|{playername}|{player id}|{player_id}|{playerid}|{target}|{boss}|{killer}|{victim}|{key}|{event}|{message}|{time}|{timestamp}|{unknown}|{999}'
    $formattedTags = Invoke-CacheMethod $formatTemplateMethod @($allTagsTemplate, $bossEvent)
    $expectedTags = 'Alice|Alice|Alice|42|42|42|Eikthyr|Eikthyr|Alice|Eikthyr|defeated_eikthyr|Boss defeated|**Alice** defeated **Eikthyr**.|2026-10-09 12:00:00 UTC|2026-10-09T12:00:00Z|{unknown}|{999}'
    Assert-Cache ($formattedTags -ceq $expectedTags) 'Webhook tags, aliases, case-insensitivity, or unknown-tag handling changed.'
    $defaultMessage = Invoke-CacheMethod $formatTemplateMethod @('', $bossEvent)
    Assert-Cache ($defaultMessage -ceq "**Boss defeated**`n**Alice** defeated **Eikthyr**.`n-# 2026-10-09 12:00:00 UTC") 'Blank webhook template did not use the default Discord template.'
    $bossEvent.PlayerName = 'Alice {boss}'
    $singlePassMessage = Invoke-CacheMethod $formatTemplateMethod @('{player name}', $bossEvent)
    Assert-Cache ($singlePassMessage -ceq 'Alice {boss}') 'Webhook replacement values were parsed as template tags a second time.'
    $bossEvent.PlayerName = 'A' * 2100
    $limitedMessage = Invoke-CacheMethod $formatTemplateMethod @('{player name}', $bossEvent)
    Assert-Cache ($limitedMessage.Length -eq 2000 -and $limitedMessage.EndsWith('...')) 'Discord message length was not capped at 2,000 characters.'
    $bossEvent.PlayerName = 'Alice'

    $pendingType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PendingWebhook', $true)
    $pendingConstructor = $pendingType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
    $notificationsType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.WebhookNotifications', $true)
    $serializeWebhookMethod = $notificationsType.GetMethod('Serialize', [Reflection.BindingFlags]'Static,NonPublic')
    $discordFormat = [Enum]::Parse($webhookFormatType, 'Discord')
    $jsonFormat = [Enum]::Parse($webhookFormatType, 'Json')
    $discordPending = $pendingConstructor.Invoke([object[]]@([Uri]'https://example.test/webhook', $discordFormat, '{player name} defeated {boss}!', $bossEvent))
    $discordBody = [byte[]]$serializeWebhookMethod.Invoke($null, [object[]]@($discordPending))
    $discordPayload = [Text.Encoding]::UTF8.GetString($discordBody) | ConvertFrom-Json
    Assert-Cache ($discordPayload.username -ceq 'PlayerDetailSync' -and $discordPayload.content -ceq 'Alice defeated Eikthyr!') 'Discord webhook payload did not use the configured template.'
    $jsonPending = $pendingConstructor.Invoke([object[]]@([Uri]'https://example.test/webhook', $jsonFormat, 'ignored template', $bossEvent))
    $jsonBody = [byte[]]$serializeWebhookMethod.Invoke($null, [object[]]@($jsonPending))
    $jsonText = [Text.Encoding]::UTF8.GetString($jsonBody)
    $expectedJsonText = '{"event":"boss_kill","occurredAtUtc":"2026-10-09T12:00:00Z","playerId":"42","playerName":"Alice","target":"Eikthyr","key":"defeated_eikthyr"}'
    Assert-Cache ($jsonText -ceq $expectedJsonText) 'JSON webhook payload changed schema, lost event data, or used Discord formatting.'

    $positionType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerPosition', $true)
    $deathEvent = [Activator]::CreateInstance($webhookEventType, $true)
    $webhookEventProperty.SetValue($deathEvent, [Enum]::Parse($webhookEventEnumType, 'PvpKill'))
    $deathEvent.OccurredAtUtc = '2026-10-09T12:00:00Z'
    $deathEvent.PlayerId = '42'
    $deathEvent.PlayerName = 'Alice'
    $deathEvent.Cause = 'player'
    $deathEvent.KillerId = '7'
    $deathEvent.KillerName = 'Bob'
    $deathEvent.Position = [Activator]::CreateInstance($positionType)
    $deathEvent.Position.X = 10.4
    $deathEvent.Position.Y = 30
    $deathEvent.Position.Z = -5.6
    $deathTags = Invoke-CacheMethod $formatTemplateMethod @('{killer}|{killer id}|{victim}|{cause}|{position}|{message}', $deathEvent)
    Assert-Cache ($deathTags -ceq 'Bob|7|Alice|player|10, 30, -6|**Bob** defeated **Alice**.') 'PvP webhook killer, victim, cause, or position tags changed.'
    $deathPending = $pendingConstructor.Invoke([object[]]@([Uri]'https://example.test/webhook', $jsonFormat, '', $deathEvent))
    $deathJson = [Text.Encoding]::UTF8.GetString([byte[]]$serializeWebhookMethod.Invoke($null, [object[]]@($deathPending)))
    Assert-Cache ($deathJson -ceq '{"event":"pvp_kill","occurredAtUtc":"2026-10-09T12:00:00Z","playerId":"42","playerName":"Alice","cause":"player","killerId":"7","killerName":"Bob","position":{"x":10.4,"y":30,"z":-5.6}}') "PvP webhook JSON changed: $deathJson"
    $deathEvent.KillerId = $null
    $deathEvent.KillerName = $null
    $deathEvent.Cause = 'fall'
    $webhookEventProperty.SetValue($deathEvent, [Enum]::Parse($webhookEventEnumType, 'PlayerDeath'))
    Assert-Cache ((Invoke-CacheMethod $formatTemplateMethod @('{message}', $deathEvent)) -ceq '**Alice** died (fall).') 'Natural death message changed.'
    Write-Output 'PASS: webhook event JSON names, round-trips, Discord/JSON payloads, tags, aliases, fallback, and length limits.'

    Invoke-CacheMethod $loadMethod @($directory, $logger, $logger) | Out-Null
    $record = [Activator]::CreateInstance($recordType)
    $record.PlayerId = '123456'
    $record.SteamUserId = '76561198000000000'
    $record.SteamName = 'Steam account name'
    $record.Name = 'Cache test player'
    $record.Online = $true
    $record.LastSeenUtc = [DateTime]::UtcNow.ToString('O')
    $record.CharacterDataAvailable = $true
    $record.Stats = [Activator]::CreateInstance($statsType)
    $record.Stats.Health = 75
    $record.Stats.MaxHealth = 100
    $food = [Activator]::CreateInstance($timedType)
    $food.Name = 'Test food'
    $food.RemainingSeconds = 300
    $food.IconKey = 'test_food'
    $foods = [Array]::CreateInstance($timedType, 1)
    $foods.SetValue($food, 0)
    $record.Foods = $foods
    $mount = [Activator]::CreateInstance($mountType)
    $mount.MountId = '123:789'
    $mount.Name = 'Rex'
    $mount.Species = 'Lox'
    $mount.Position.X = 12
    $mount.Level = 3
    $mount.Tamed = $true
    $mount.Hungry = $false
    $mount.Stats = [Activator]::CreateInstance($mountStatsType)
    $mount.Stats.Health = 650
    $mount.Stats.MaxHealth = 1000
    $mount.Stats.Stamina = 80
    $mount.Stats.MaxStamina = 100
    $record.Mount = $mount
    $record.Riding = $true
    $ship = [Activator]::CreateInstance($shipType)
    $ship.ShipId = '456:789'
    $ship.Name = 'VikingShip'
    $ship.Position.Z = -22
    $ship.Speed = 3.5
    $ship.SpeedSetting = 'Full'
    $ship.SailUp = $true
    $ship.AtHelm = $true
    $record.Ship = $ship
    $record.Aboard = $true
    $deathCount = [Activator]::CreateInstance($statisticType)
    $deathCount.Name = 'Deaths'
    $deathCount.Value = 2
    $lifetimeStats = [Array]::CreateInstance($statisticType, 1)
    $lifetimeStats.SetValue($deathCount, 0)
    $record.LifetimeStats = $lifetimeStats
    $record.PrivateKeys = [string[]]@('defeated_eikthyr', 'found_haldor')
    $activity = [Activator]::CreateInstance($activityType)
    $activity.State = [Enum]::Parse($activityStatusType, 'Sailing')
    $activity.ActiveStates = [Array]::CreateInstance($activityStatusType, 1)
    $activity.ActiveStates.SetValue([Enum]::Parse($activityStatusType, 'Sailing'), 0)
    $activity.ObservedAtUtc = [DateTime]::UtcNow.ToString('O')
    $record.Activity = $activity
    $follower = [Activator]::CreateInstance($followerType)
    $follower.FollowerId = '123:456'
    $follower.Name = 'Thora'
    $follower.Species = 'Skelett'
    $follower.Level = 2
    $follower.Tamed = $true
    $follower.Hungry = $false
    $follower.Position.X = -42
    $follower.Stats = [Activator]::CreateInstance($followerStatsType)
    $follower.Stats.Health = 120
    $follower.Stats.MaxHealth = 160
    $bubble = [Activator]::CreateInstance($timedType)
    $bubble.Name = 'Protection'
    $bubble.RemainingSeconds = 30
    $shieldType = [SE_Shield]
    $shield = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($shieldType)
    $shieldFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $shieldType.GetField('m_totalAbsorbDamage', $shieldFlags).SetValue($shield, [float]200)
    $shieldType.GetField('m_damage', $shieldFlags).SetValue($shield, [float]50)
    $captureShield = $characterStatusCaptureType.GetMethod('CaptureShield', $flags)
    Invoke-CacheMethod $captureShield @($bubble, $shield) | Out-Null
    Assert-Cache ($bubble.ShieldRemainingAbsorption -eq 150 -and $bubble.ShieldMaxAbsorption -eq 200) 'Original game assembly shield fields were not captured.'
    $shieldType.GetField('m_damage', $shieldFlags).SetValue($shield, [float]250)
    Invoke-CacheMethod $captureShield @($bubble, $shield) | Out-Null
    Assert-Cache ($bubble.ShieldRemainingAbsorption -eq 0) 'Depleted shield absorption was not clamped.'
    $bubble.ShieldRemainingAbsorption = 150
    $mountStatuses = [Array]::CreateInstance($timedType, 1)
    $mountStatuses.SetValue($bubble, 0)
    $mount.Statuses = $mountStatuses
    $mountAgingFlags = [Reflection.BindingFlags]'Instance,NonPublic'
    $ageMount = $mountType.GetMethod('WithElapsedSeconds', $mountAgingFlags)
    $agedMount = $ageMount.Invoke($mount, [object[]]@([float]10))
    Assert-Cache ($agedMount.Statuses[0].RemainingSeconds -eq 20 -and $agedMount.Statuses[0].ShieldRemainingAbsorption -eq 150) 'Mount status aging changed its timer or shield absorption.'
    $followerStatuses = [Array]::CreateInstance($timedType, 1)
    $followerStatuses.SetValue($bubble, 0)
    $follower.Statuses = $followerStatuses
    $followers = [Array]::CreateInstance($followerType, 1)
    $followers.SetValue($follower, 0)
    $record.Followers = $followers
    $httpType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.HttpApiServer', $true)
    $requestHandlerType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.HttpApiRequestHandler', $true)
    $response = Invoke-CacheMethod ($requestHandlerType.GetMethod('ToPlayerResponse', $flags)) @($record)
    $responseSerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($response.GetType())
    $responseStream = [IO.MemoryStream]::new()
    try {
        $responseSerializer.WriteObject($responseStream, $response)
        $responseJson = [Text.Encoding]::UTF8.GetString($responseStream.ToArray()) | ConvertFrom-Json
        Assert-Cache ($responseJson.steamUserId -ceq $record.SteamUserId) 'Detailed API response lost Steam identity.'
        Assert-Cache ($responseJson.steamName -ceq $record.SteamName) 'Detailed API response lost the Steam display name.'
        Assert-Cache (($responseJson.PSObject.Properties.Name | Select-Object -First 4) -join ',' -ceq 'playerId,name,steamName,steamUserId') 'Steam identity fields were not ordered immediately after the character name.'
        Assert-Cache ($null -eq $responseJson.currentMount -and $null -eq $responseJson.followers -and $null -eq $responseJson.stats) 'Player identity response still embeds category data.'
    }
    finally { $responseStream.Dispose() }
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $port = $portProbe.LocalEndpoint.Port
    $portProbe.Stop()
    $httpConstructor = $httpType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([string], [int], [string]), $null)
    $server = $httpConstructor.Invoke([object[]]@('127.0.0.1', $port, 'cache-regression'))
    try {
        $httpType.GetMethod('Publish', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($server, [object[]]@((Resolve-Entities @($record)).PSObject.BaseObject))
        Assert-Cache ($httpType.GetMethod('Start', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($server, $null)) 'Local test API did not start.'
        $baseUri = "http://127.0.0.1:$port/api/v1/players/123456"
        $headers = @{ 'X-API-Password' = 'cache-regression' }
        $playerDetail = Invoke-RestMethod $baseUri -Headers $headers
        Assert-Cache ($playerDetail.name -ceq $record.Name -and $null -eq $playerDetail.statuses) 'Player identity route included expanded character data.'
        $followerProperties = @($playerDetail.PSObject.Properties.Name | Where-Object { $_ -match 'follower' })
        Assert-Cache ($followerProperties.Count -eq 0) 'Player details exposed follower information.'
        $characterData = Invoke-RestMethod "$baseUri/character" -Headers $headers
        Assert-Cache ($characterData.foods[0].name -ceq 'Test food' -and $characterData.equipment.Count -eq 0) 'Character data route lost foods or gear.'
        $categoryResponses = @($characterData)
        $list = Invoke-RestMethod "$baseUri/followers" -Headers $headers
        Assert-Cache ($list.followers.Count -eq 1 -and $list.characterDataAgeSeconds -ge 0) 'Follower list lost detail or age metadata.'
        $categoryResponses += $list
        $followerDetails = $list.followers[0]
        Assert-Cache ($followerDetails.name -ceq 'Thora' -and $followerDetails.species -ceq 'Skelett') 'Follower list confused custom name with species.'
        Assert-Cache ($followerDetails.stats.health -eq 120 -and $followerDetails.position.x -eq -42) 'Follower list lost health or position.'
        Assert-Cache ($followerDetails.statuses[0].shieldRemainingAbsorption -eq 150 -and $followerDetails.statuses[0].remainingSeconds -eq 30) 'Follower list lost status details or shield absorption.'
        $mountState = Invoke-RestMethod "$baseUri/mount" -Headers $headers
        Assert-Cache ($mountState.riding -and $mountState.mount.name -ceq 'Rex' -and $mountState.mount.stats.stamina -eq 80) 'Mount endpoint lost riding state or mount details.'
        Assert-Cache ($mountState.mount.statuses[0].shieldRemainingAbsorption -eq 150) 'Mount endpoint lost shield absorption.'
        $categoryResponses += $mountState
        $shipState = Invoke-RestMethod "$baseUri/ship" -Headers $headers
        Assert-Cache ($shipState.aboard -and $shipState.ship.name -ceq 'VikingShip' -and $shipState.ship.speed -eq 3.5) 'Ship endpoint lost aboard state or ship details.'
        $categoryResponses += $shipState
        $globalUri = "http://127.0.0.1:$port/api/v1"
        foreach ($entry in @(
            @{ Route = 'mounts'; Id = $mount.MountId; Expected = $mountState.mount; Relationship = 'riderPlayerId' },
            @{ Route = 'ships'; Id = $ship.ShipId; Expected = $shipState.ship; Relationship = 'lastDriverPlayerId' },
            @{ Route = 'followers'; Id = $follower.FollowerId; Expected = $followerDetails; Relationship = 'followingPlayerId' }
        )) {
            $globalList = Invoke-RestMethod "$globalUri/$($entry.Route)" -Headers $headers
            Assert-Cache ($globalList.($entry.Route).Count -eq 1) "Global $($entry.Route) did not return one unique entity."
            $entity = Invoke-RestMethod "$globalUri/$($entry.Route)/$([Uri]::EscapeDataString($entry.Id))" -Headers $headers
            Assert-Cache (($entity | ConvertTo-Json -Depth 20 -Compress) -ceq ($entry.Expected | ConvertTo-Json -Depth 20 -Compress)) 'Global detail and player entity DTOs differ.'
            Assert-Cache ($entity.($entry.Relationship) -ceq $record.PlayerId) "Global entity lost $($entry.Relationship)."
            $missingEntity = Invoke-WebRequest "$globalUri/$($entry.Route)/missing" -Headers $headers -SkipHttpErrorCheck
            Assert-Cache ($missingEntity.StatusCode -eq 404) 'Missing global entity did not return 404.'
            $unauthorizedEntity = Invoke-WebRequest "$globalUri/$($entry.Route)" -SkipHttpErrorCheck
            Assert-Cache ($unauthorizedEntity.StatusCode -eq 401) 'Global entity list did not require authentication.'
            $nestedEntity = Invoke-WebRequest "$globalUri/$($entry.Route)/$($entry.Id)/extra" -Headers $headers -SkipHttpErrorCheck
            Assert-Cache ($nestedEntity.StatusCode -eq 404) 'Unexpected nested global entity route was accepted.'
        }
        $pluralMountRoute = Invoke-WebRequest "$baseUri/mounts" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($pluralMountRoute.StatusCode -eq 404) 'Plural mount route is still available.'
        $mountDetailRoute = Invoke-WebRequest "$baseUri/mount/current" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($mountDetailRoute.StatusCode -eq 404) 'Nested mount detail route is still available.'
        $pluralShipRoute = Invoke-WebRequest "$baseUri/ships" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($pluralShipRoute.StatusCode -eq 404) 'Plural ship route is still available.'
        $shipDetailRoute = Invoke-WebRequest "$baseUri/ship/current" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($shipDetailRoute.StatusCode -eq 404) 'Nested ship detail route is still available.'
        $playerStats = Invoke-RestMethod "$baseUri/stats" -Headers $headers
        Assert-Cache ($characterData.vitals.health -eq 75 -and $null -eq $playerStats.vitals -and $playerStats.lifetime[0].name -ceq 'Deaths' -and $playerStats.lifetime[0].value -eq 2) 'Vitals were not moved to character data or lifetime statistics were lost.'
        $categoryResponses += $playerStats
        $privateKeys = Invoke-RestMethod "$baseUri/keys" -Headers $headers
        Assert-Cache ($privateKeys.privateKeys.Count -eq 2 -and $privateKeys.privateKeys[0] -ceq 'defeated_eikthyr') 'Private key endpoint lost per-player game keys.'
        $categoryResponses += $privateKeys
        $currentActivity = Invoke-RestMethod "$baseUri/activity" -Headers $headers
        Assert-Cache ($currentActivity.activity.state -ceq 'sailing') 'Activity endpoint lost the current state.'
        $record.Activity.State = [Enum]::Parse($activityStatusType, 'Unclassified')
        $record.Activity.ActiveStates = [Array]::CreateInstance($activityStatusType, 0)
        $pendingActivity = Invoke-RestMethod "$baseUri/activity" -Headers $headers
        Assert-Cache ($pendingActivity.activity.state -ceq 'unclassified' -and $pendingActivity.activity.activeStates.Count -eq 0) 'Pending activity became null instead of an explicit state.'
        $record.Activity.State = [Enum]::Parse($activityStatusType, 'Sailing')
        $record.Activity.ActiveStates = [Array]::CreateInstance($activityStatusType, 1)
        $record.Activity.ActiveStates.SetValue([Enum]::Parse($activityStatusType, 'Sailing'), 0)
        $categoryResponses += $currentActivity
        $activityResponseType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivityResponse', $true)
        $emptyActivityResponse = [Activator]::CreateInstance($activityResponseType)
        $emptyActivityResponse.CharacterDataAgeSeconds = 24
        $activitySerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($activityResponseType)
        $activityStream = [IO.MemoryStream]::new()
        try {
            $activitySerializer.WriteObject($activityStream, $emptyActivityResponse)
            $emptyActivityJson = [Text.Encoding]::UTF8.GetString($activityStream.ToArray()) | ConvertFrom-Json
            Assert-Cache ($emptyActivityJson.PSObject.Properties.Name -contains 'activity' -and $null -eq $emptyActivityJson.activity) 'Unavailable activity was omitted instead of returned as null.'
        }
        finally { $activityStream.Dispose() }
        $repeatedPlayerMetadata = @('playerId', 'online', 'lastSeenUtc', 'characterDataAvailable')
        foreach ($categoryResponse in $categoryResponses) {
            Assert-Cache ($categoryResponse.characterDataAgeSeconds -ge 0) 'Category response lost character data age.'
            foreach ($propertyName in $repeatedPlayerMetadata) {
                Assert-Cache ($propertyName -notin $categoryResponse.PSObject.Properties.Name) "Category response repeated '$propertyName'."
            }
        }
        foreach ($key in @('stamina', 'maxStamina', 'eitr', 'maxEitr')) {
            Assert-Cache ($key -notin $followerDetails.stats.PSObject.Properties.Name) "Follower stats still expose '$key'."
            Assert-Cache ($key -in $characterData.vitals.PSObject.Properties.Name) "Player vitals lost '$key'."
        }
        $removedFollowerDetailRoute = Invoke-WebRequest "$baseUri/followers/missing" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($removedFollowerDetailRoute.StatusCode -eq 404 -and ($removedFollowerDetailRoute.Content | ConvertFrom-Json).error -eq 'not_found') 'Per-follower detail route was not removed.'
        $missingPlayer = Invoke-WebRequest "http://127.0.0.1:$port/api/v1/players/999/followers" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($missingPlayer.StatusCode -eq 404) 'Missing follower owner did not return 404.'
        $invalid = Invoke-WebRequest "http://127.0.0.1:$port/api/v1/players/invalid/followers" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($invalid.StatusCode -eq 400) 'Invalid player ID did not return 400.'
        $unauthorized = Invoke-WebRequest "$baseUri/followers" -SkipHttpErrorCheck
        Assert-Cache ($unauthorized.StatusCode -eq 401) 'Follower API did not require authentication.'
        $wrongMethod = Invoke-WebRequest "$baseUri/followers" -Headers $headers -Method Post -SkipHttpErrorCheck
        Assert-Cache ($wrongMethod.StatusCode -eq 405) 'Follower API accepted a non-GET method.'
    }
    finally { $server.Dispose() }
    $firstRider = [Activator]::CreateInstance($recordType)
    $firstRider.PlayerId = '11'
    $firstRider.Online = $true
    $firstRider.CharacterDataAgeSeconds = 20
    $firstRider.Mount = [Activator]::CreateInstance($mountType)
    $firstRider.Mount.MountId = 'shared-mount'
    $firstRider.Ship = [Activator]::CreateInstance($shipType)
    $firstRider.Ship.ShipId = 'shared-ship'
    $firstRider.Ship.AtHelm = $true
    $firstRider.Aboard = $true
    $firstRider.Riding = $true
    $firstRider.Followers = [Array]::CreateInstance($followerType, 1)
    $firstRider.Followers.SetValue([Activator]::CreateInstance($followerType), 0)
    $firstRider.Followers[0].FollowerId = 'shared-follower'
    $secondRider = [Activator]::CreateInstance($recordType)
    $secondRider.PlayerId = '22'
    $secondRider.Online = $true
    $secondRider.Mount = [Activator]::CreateInstance($mountType)
    $secondRider.Mount.MountId = 'shared-mount'
    $secondRider.Riding = $true
    $secondRider.Ship = [Activator]::CreateInstance($shipType)
    $secondRider.Ship.ShipId = 'shared-ship'
    $secondRider.Aboard = $true
    $secondRider.Followers = [Array]::CreateInstance($followerType, 1)
    $secondRider.Followers.SetValue([Activator]::CreateInstance($followerType), 0)
    $secondRider.Followers[0].FollowerId = 'shared-follower'
    $shared = Resolve-Entities @($firstRider, $secondRider)
    Assert-Cache ($null -eq $firstRider.Mount -and -not $firstRider.Riding -and $secondRider.Mount.RiderPlayerId -ceq '22') 'A transferred mount retained multiple riders.'
    Assert-Cache ($firstRider.Followers.Length -eq 0 -and $secondRider.Followers[0].FollowingPlayerId -ceq '22') 'A transferred follower retained multiple following players.'
    Assert-Cache ($firstRider.Ship.PassengerPlayerIds.Length -eq 2 -and $firstRider.Ship.CurrentPassengerPlayerIds.Length -eq 2 -and $firstRider.Ship.LastDriverPlayerId -ceq '11') 'Ship passengers or last driver were not reconciled.'
    $oldShip = $firstRider.Ship
    $firstRider.Ship = [Activator]::CreateInstance($shipType)
    $firstRider.Ship.ShipId = 'new-ship'
    $firstRider.Ship.AtHelm = $true
    $firstRider.CharacterDataAgeSeconds = 0
    $secondRider.Aboard = $false
    $changed = Resolve-Entities @($firstRider, $secondRider) $shared.Players
    Assert-Cache ($secondRider.Ship.PassengerPlayerIds.Length -eq 1 -and $secondRider.Ship.CurrentPassengerPlayerIds.Length -eq 0 -and $secondRider.Ship.LastDriverPlayerId -ceq '11') 'A remaining passenger lost the ship or its historical driver.'
    Assert-Cache ($oldShip.PassengerPlayerIds.Length -eq 2) 'Reconciliation mutated a previously published ship DTO.'
    $secondRider.Ship = [Activator]::CreateInstance($shipType)
    $secondRider.Ship.ShipId = 'new-ship'
    $removed = Resolve-Entities @($firstRider, $secondRider) $changed.Players
    Assert-Cache (@($removed.Players | Where-Object { $_.Ship.ShipId -eq 'shared-ship' }).Count -eq 0) 'An unreferenced ship remained retained.'
    Write-Output 'PASS: unique mount/follower transfers, ship passengers, historical drivers, and ship retirement.'

    $online = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($record)))
    Assert-Cache ($online.Players.Length -eq 1 -and $online.Players[0].Online) 'Online player was not cached.'
    Assert-Cache ($online.Players[0].Mount.Name -ceq 'Rex' -and $online.Players[0].Riding) 'Online cache did not record the mount and riding state.'
    Assert-Cache ($online.Players[0].Ship.Name -ceq 'VikingShip' -and $online.Players[0].Aboard -and $online.Players[0].PrivateKeys.Length -eq 2) 'Online cache did not record the ship, aboard state, or private keys.'

    $cacheFile = Join-Path $directory 'JoksterCube.PlayerDetailSync.players.json'
    if (-not (Test-Path $cacheFile)) {
        $change = $watcher.WaitForChanged([IO.WatcherChangeTypes]'Created,Renamed', 5000)
        Assert-Cache (-not $change.TimedOut -or (Test-Path $cacheFile)) 'Background save did not complete.'
    }
    $saved = Get-Content $cacheFile -Raw | ConvertFrom-Json
    Assert-Cache ($saved.players[0].stats.health -eq 75) 'Serialized cache lost player vitals.'
    Assert-Cache ($saved.players[0].steamUserId -eq $record.SteamUserId) 'Serialized cache lost Steam identity.'
    Assert-Cache ($saved.players[0].steamName -ceq $record.SteamName) 'Serialized cache lost Steam display name.'
    Assert-Cache ($saved.players[0].followers[0].statuses[0].shieldRemainingAbsorption -eq 150) 'Cache lost pet protection bubble.'
    Assert-Cache ($saved.players[0].mount.name -ceq 'Rex' -and $saved.players[0].riding) 'Serialized cache lost mount details or riding state.'
    Assert-Cache ($saved.players[0].mount.statuses[0].shieldRemainingAbsorption -eq 150) 'Cache lost mount shield absorption.'
    Assert-Cache ($saved.players[0].ship.name -ceq 'VikingShip' -and $saved.players[0].aboard -and $saved.players[0].lifetimeStats[0].value -eq 2) 'Serialized cache lost ship state or lifetime stats.'
    Assert-Cache ($saved.players[0].privateKeys.Count -eq 2 -and $saved.players[0].activity.state -ceq 'sailing') 'Serialized cache lost private keys or activity.'
    Assert-Cache ($saved.players[0].lifetimeStats[0].name -ceq 'Deaths' -and $null -eq $saved.players[0].lifetimeStats[0].unit) 'Serialized game statistic names or unit-free JSON changed.'

    Invoke-CacheMethod $loadMethod @($directory, $logger, $logger) | Out-Null
    $restored = $currentProperty.GetValue($null)
    Assert-Cache ($restored.Players.Length -eq 1 -and -not $restored.Players[0].Online) 'Reload must restore players as offline.'
    Assert-Cache ($restored.Players[0].Stats.Health -eq 75) 'Reload lost last-known health.'
    Assert-Cache ($restored.Players[0].SteamUserId -eq $record.SteamUserId) 'Reload lost Steam identity.'
    Assert-Cache ($restored.Players[0].SteamName -ceq $record.SteamName) 'Reload lost Steam display name.'
    Assert-Cache ($restored.Players[0].Mount.Name -ceq 'Rex' -and -not $restored.Players[0].Riding) 'Offline cache did not retain the mount with riding false.'
    Assert-Cache ($restored.Players[0].Ship.Name -ceq 'VikingShip' -and -not $restored.Players[0].Aboard) 'Offline cache did not retain the ship with aboard false.'
    Assert-Cache ($restored.Players[0].Foods[0].RemainingSeconds -eq 300) 'Reload changed last-known offline food timer.'
    Assert-Cache ($restored.Players[0].Followers[0].Statuses[0].RemainingSeconds -eq 30) 'Reload changed last-known offline follower timer.'

    $rejoin = [Activator]::CreateInstance($recordType)
    $rejoin.PlayerId = '123456'
    $rejoin.Name = 'Cache test player'
    $rejoin.Online = $true
    $rejoin.X = 42
    $joined = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($rejoin)))
    Assert-Cache ($joined.Players.Length -eq 1 -and $joined.Players[0].Online) 'Rejoin duplicated the cached character.'
    Assert-Cache ($joined.Players[0].SteamUserId -eq $record.SteamUserId) 'Rejoin lost last-known Steam identity.'
    Assert-Cache ($joined.Players[0].SteamName -ceq $record.SteamName) 'Rejoin lost last-known Steam display name.'
    Assert-Cache ($joined.Players[0].Stats.Health -eq 75 -and $joined.Players[0].X -eq 42) 'Rejoin did not retain details and update live position.'
    Assert-Cache ($joined.Players[0].Followers.Length -eq 1) 'Missing rejoin report discarded cached followers.'
    Assert-Cache ($joined.Players[0].Mount.Name -ceq 'Rex' -and -not $joined.Players[0].Riding) 'Missing client data lost the retained mount or marked the player as riding.'

    $offline = Invoke-CacheMethod $mergeMethod @((New-Snapshot @()))
    Assert-Cache ($offline.Players.Length -eq 1 -and -not $offline.Players[0].Online) 'Disconnect removed last-known data.'
    Assert-Cache ($offline.Players[0].SteamUserId -eq $record.SteamUserId) 'Disconnect lost Steam identity.'
    Assert-Cache ($offline.Players[0].Stats.Health -eq 75) 'Offline record lost its cached vitals.'

    $dismount = [Activator]::CreateInstance($recordType)
    $dismount.PlayerId = '123456'
    $dismount.Name = 'Cache test player'
    $dismount.Online = $true
    $dismount.CharacterDataAvailable = $true
    $dismounted = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($dismount)))
    Assert-Cache ($dismounted.Players[0].Mount.Name -ceq 'Rex' -and -not $dismounted.Players[0].Riding) 'Dismount did not retain one mount with riding false.'
    Assert-Cache ($dismounted.Players[0].Ship.Name -ceq 'VikingShip' -and -not $dismounted.Players[0].Aboard) 'Leaving a ship did not retain one ship with aboard false.'

    $duplicateLive = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($rejoin, $rejoin)))
    Assert-Cache ($duplicateLive.Players.Length -eq 1) 'Duplicate live character IDs were not collapsed.'
    $otherCharacter = [Activator]::CreateInstance($recordType)
    $otherCharacter.PlayerId = '654321'
    $otherCharacter.Name = $record.Name
    $otherCharacter.SteamUserId = $record.SteamUserId
    $distinct = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($rejoin, $otherCharacter)))
    Assert-Cache ($distinct.Players.Length -eq 2) 'Different characters with the same name or Steam ID were combined.'
    $distinctOffline = Invoke-CacheMethod $mergeMethod @((New-Snapshot @()))
    Assert-Cache ($distinctOffline.Players.Length -eq 2 -and @($distinctOffline.Players | Where-Object Online).Count -eq 0) 'Offline characters were duplicated or combined.'
    $storeType.GetField('_current', $flags).SetValue($null, (New-Snapshot @()).PSObject.BaseObject)
    $networkPlayers = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($networkRecords)))
    Assert-Cache ($networkPlayers.Players.Length -eq 2 -and @($networkPlayers.Players | Where-Object Online).Count -eq 2) 'Two network-only players were omitted or combined.'
    Assert-Cache (@($networkPlayers.Players | Where-Object CharacterDataAvailable).Count -eq 0) 'Network identity alone must not imply character details are available.'
    Write-Output 'PASS: two network-only players retain distinct character IDs and online records without client reports.'
    $storeType.GetField('_current', $flags).SetValue($null, (New-Snapshot @($record)).PSObject.BaseObject)

    $sessionProperty = $recordType.GetProperty('SessionId', [Reflection.BindingFlags]'Instance,NonPublic')
    $temporary = [Activator]::CreateInstance($recordType)
    $temporary.PlayerId = '999'
    $temporary.Name = 'Temporary identity'
    $sessionProperty.SetValue($temporary, [long]999)
    Invoke-CacheMethod $mergeMethod @((New-Snapshot @($temporary))) | Out-Null
    $stable = [Activator]::CreateInstance($recordType)
    $stable.PlayerId = '123456'
    $stable.Name = 'Cache test player'
    $stable.CharacterDataAvailable = $true
    $sessionProperty.SetValue($stable, [long]999)
    $migrated = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($stable)))
    Assert-Cache ($migrated.Players.Length -eq 1 -and $migrated.Players[0].PlayerId -eq '123456') 'Temporary session record survived stable character identification.'

    $validate = $validatorType.GetMethod('GetError', $flags)
    Assert-Cache (Invoke-CacheMethod $isFollowingPlayer @($false, 'Test player', 'Test player')) 'Saved follow target did not include a pet after its AI target changed.'
    Assert-Cache (-not (Invoke-CacheMethod $isFollowingPlayer @($true, 'Different player', 'Test player'))) 'A stale AI target overrode the saved new owner.'
    Assert-Cache (Invoke-CacheMethod $isFollowingPlayer @($true, '', 'Test player')) 'Current AI follow target was not accepted when no saved target was available.'
    Assert-Cache (-not (Invoke-CacheMethod $isFollowingPlayer @($false, '', 'Test player'))) 'A pet told to stay was still included after its AI target cleared.'
    foreach ($statusName in @(
        'Joining', 'Dead', 'RecoveringCorpse', 'Drowning', 'Freezing', 'Burning', 'FightingBoss',
        'SiegingFortress', 'HuntingSerpent', 'DefendingBase', 'InPvP', 'InCombat',
        'DungeonDelving', 'LostAtSea', 'Portaling', 'CartHauling', 'Terraforming',
        'Mining', 'Logging', 'Farming', 'TamingRanching', 'Cooking', 'Brewing', 'Smelting',
        'Crafting', 'SortingChests', 'Trading', 'StalkingSneaking', 'Hunting', 'Gathering', 'Encumbered',
        'Resting', 'IdlingAtBase', 'IdlingAtOutpost', 'IdlingInDanger', 'Exploring', 'Adventuring'
    )) {
        Assert-Cache ([Enum]::IsDefined($activityStatusType, $statusName)) "Activity status '$statusName' is missing."
    }
    $enumWireType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.EnumWireValue', $true)
    $toWireValue = $enumWireType.GetMethod('ToWireValue', $flags).MakeGenericMethod($activityStatusType)
    Assert-Cache (($toWireValue.Invoke($null, [object[]]@([Enum]::Parse($activityStatusType, 'Joining')))) -ceq 'joining') 'Joining activity did not use the expected wire value.'
    Assert-Cache (-not [Enum]::IsDefined($activityStatusType, 'Combat')) 'Legacy Combat activity value is still accepted.'
    Assert-Cache (-not [Enum]::IsDefined($activityStatusType, 'Idling')) 'Generic Idling activity value is still accepted.'
    $report = [Activator]::CreateInstance($dataType)
    $report.SteamName = 'Reported Steam display name'
    $report.Mount = $mount
    $report.Ship = $ship
    $report.LifetimeStats = $lifetimeStats
    $report.PrivateKeys = $record.PrivateKeys
    $report.Activity = $activity
    $statusFactory = $timedType.GetMethod('FromStatus', $flags)
    $effect = $statusFactory.Invoke($null, [object[]]@('Untimed status', [float]-10, [float]0, 'test_status', $null))
    Assert-Cache (-not $effect.HasDuration -and $effect.RemainingSeconds -eq 0) 'Untimed Valheim status was not normalized.'
    $expired = $statusFactory.Invoke($null, [object[]]@('Expired status', [float]-1, [float]10, '', $null))
    Assert-Cache ($expired.HasDuration -and $expired.RemainingSeconds -eq 0) 'Expired timed status was not clamped.'
    $effects = [Array]::CreateInstance($timedType, 1)
    $effects.SetValue($effect, 0)
    $report.Statuses = $effects
    $gear = [Activator]::CreateInstance($gearType)
    $gear.Name = 'Modded helmet'
    $gear.Slot = 'Helmet'
    $gear.IconKey = 'test_helmet'
    $gear.Quality = 20
    $equipment = [Array]::CreateInstance($gearType, 1)
    $equipment.SetValue($gear, 0)
    $report.Equipment = $equipment
    $report.Followers = $followers
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($null -eq $validationError) 'Valid untimed status or modded quality gear was rejected.'
    $report.SteamName = 'x' * 129
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -ceq 'steamName exceeds 128 characters') 'An oversized Steam name was accepted.'
    $report.SteamName = 'Reported Steam display name'
    $gameStatNames = [Enum]::GetNames($gameStatType) | Where-Object { $_ -notin @('Count', 'None') } | Select-Object -First 65
    $expandedStats = [Array]::CreateInstance($statisticType, $gameStatNames.Count)
    for ($index = 0; $index -lt $gameStatNames.Count; $index++) {
        $gameStat = [Activator]::CreateInstance($statisticType)
        $gameStat.Name = $gameStatNames[$index]
        $gameStat.Value = 1
        $expandedStats.SetValue($gameStat, $index)
    }
    $report.LifetimeStats = $expandedStats
    Assert-Cache ($null -eq (Invoke-CacheMethod $validate @($report))) 'Valid game stat names beyond the old 64-entry limit were rejected.'
    $expandedStats[0].Value = 0
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -match 'value must be greater than 0') 'A zero-valued lifetime stat was accepted.'
    $expandedStats[0].Value = 1
    $expandedStats[0].Name = 'NotAPlayerStatType'
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -match 'name is invalid') 'An unknown lifetime stat name was accepted.'
    $expandedStats[0].Name = $gameStatNames[0]
    $report.Activity.ActiveStates = [Array]::CreateInstance($activityStatusType, [Enum]::GetValues($activityStatusType).Length)
    [Enum]::GetValues($activityStatusType).CopyTo($report.Activity.ActiveStates, 0)
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($null -eq $validationError) 'Expanded activity state list was rejected.'
    $report.Activity = $null
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($null -eq $validationError) 'Disabled activity collection report was rejected.'
    $report.Activity = $activity
    $serializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($dataType)
    $filterPrivateKeys = $validatorType.GetMethod('FilterInvalidPrivateKeys', $flags)
    $report.PrivateKeys = [string[]]@($null, '', 'defeated_eikthyr')
    $stream = [IO.MemoryStream]::new()
    try {
        $serializer.WriteObject($stream, $report.PSObject.BaseObject)
        $stream.Position = 0
        $roundTrip = $serializer.ReadObject($stream)
        Invoke-CacheMethod $filterPrivateKeys @($roundTrip) | Out-Null
        Assert-Cache (-not $roundTrip.Statuses[0].HasDuration -and $roundTrip.Equipment[0].Quality -eq 20) 'Report JSON lost untimed status or gear.'
        Assert-Cache ($roundTrip.Followers[0].FollowerId -ceq '123:456') 'Client report JSON lost follower ID.'
        Assert-Cache ($roundTrip.Mount.Name -ceq 'Rex' -and $roundTrip.Mount.Stats.Stamina -eq 80) 'Client report JSON lost mount details.'
        Assert-Cache ($roundTrip.Ship.Name -ceq 'VikingShip') 'Client report JSON lost ship details.'
        Assert-Cache ($roundTrip.SteamName -ceq $report.SteamName) 'Client report JSON lost Steam display name.'
        Assert-Cache ($roundTrip.PrivateKeys.Length -eq 1 -and $roundTrip.PrivateKeys[0] -ceq 'defeated_eikthyr') 'Blank client private keys were not filtered from the report.'
        Assert-Cache ($null -eq (Invoke-CacheMethod $validate @($roundTrip))) 'Blank client private keys caused the full report to be rejected.'
        $agingMethod = $dataType.GetMethod('WithElapsedSeconds', [Reflection.BindingFlags]'Instance,NonPublic')
        $aged = $agingMethod.Invoke($roundTrip, [object[]]@([float]60))
        Assert-Cache ($aged.SteamName -ceq $report.SteamName) 'Report aging lost Steam display name.'
        Assert-Cache (-not $aged.Statuses[0].HasDuration -and $aged.Statuses[0].RemainingSeconds -eq 0) 'Timer aging lost untimed status semantics.'
        Assert-Cache ($aged.Followers[0].Statuses[0].RemainingSeconds -eq 0 -and $roundTrip.Followers[0].Statuses[0].RemainingSeconds -eq 30) 'Follower aging failed to clamp or mutated its original sample.'
        Assert-Cache ($aged.Followers[0].Statuses[0].ShieldRemainingAbsorption -eq 150) 'Timer aging lost shield absorption.'
        $roundTrip.Mount.RiderPlayerId = 'forged'
        $roundTrip.Mount.ObservedAtUtc = '9999-01-01T00:00:00.0000000Z'
        $roundTrip.Ship.LastDriverPlayerId = 'forged'
        $roundTrip.Ship.LastDrivenAtUtc = '9999-01-01T00:00:00.0000000Z'
        $roundTrip.Ship.ObservedAtUtc = '9999-01-01T00:00:00.0000000Z'
        $roundTrip.Followers[0].FollowingPlayerId = 'forged'
        $roundTrip.Followers[0].ObservedAtUtc = '9999-01-01T00:00:00.0000000Z'
        $recordConstructor = $recordType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object { $_.GetParameters().Count -eq 8 }
        $reportedPlayer = $recordConstructor.Invoke([object[]]@('77', 'Reported character', [float]0, [float]0, [float]0, $null, $roundTrip, [DateTime]::UtcNow.ToString('O')))
        $resolvedReport = Resolve-Entities @($reportedPlayer)
        Assert-Cache ($reportedPlayer.SteamName -ceq $report.SteamName -and $reportedPlayer.Mount.RiderPlayerId -ceq '77' -and $reportedPlayer.Ship.LastDriverPlayerId -ceq '77' -and $reportedPlayer.Followers[0].FollowingPlayerId -ceq '77') 'Client-supplied relationship IDs overrode the reporting character.'
        Assert-Cache ($reportedPlayer.Mount.ObservedAtUtc -notlike '9999*' -and $reportedPlayer.Ship.LastDrivenAtUtc -notlike '9999*') 'Client-supplied future observations overrode server timestamps.'
        Assert-Cache ($roundTrip.Mount.RiderPlayerId -ceq 'forged') 'Server normalization mutated the original client report.'
    }
    finally { $stream.Dispose() }
    $report.PrivateKeys = $record.PrivateKeys
    $follower.Position.X = [float]::NaN
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -eq 'followers[0].position must be finite') 'Nonfinite follower position was accepted.'
    $follower.Position.X = -42
    $bubble.ShieldRemainingAbsorption = -1
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -like 'followers[[]0].statuses[[]0].shieldRemainingAbsorption=*') 'Invalid shield absorption was accepted.'
    $bubble.ShieldRemainingAbsorption = 150
    $duplicates = [Array]::CreateInstance($followerType, 2)
    $duplicates.SetValue($follower, 0)
    $duplicates.SetValue($follower, 1)
    $report.Followers = $duplicates
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -eq 'followers[1].followerId is duplicated') 'Duplicate follower IDs were accepted.'
    $report.Followers = [Array]::CreateInstance($followerType, 65)
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -eq 'followers exceeds 64 entries') 'Unbounded followers were accepted.'
    $report.Followers = $followers
    $mount.Stats.Stamina = [float]::NaN
    Assert-Cache ((Invoke-CacheMethod $validate @($report)) -like 'mount.stats.stamina=*') 'Invalid mount stamina was accepted.'
    $mount.Stats.Stamina = 80
    $report.Followers = $followers
    $legacy = ($saved | ConvertTo-Json -Depth 20) | ConvertFrom-Json
    $legacy.players[0].PSObject.Properties.Remove('steamUserId')
    $legacy.players[0].PSObject.Properties.Remove('followers')
    $legacy.players[0].foods[0].PSObject.Properties.Remove('hasDuration')
    $legacyMount = $legacy.players[0].mount
    $legacyShip = $legacy.players[0].ship
    $legacy.players[0].PSObject.Properties.Remove('mount')
    $legacy.players[0].PSObject.Properties.Remove('riding')
    $legacy.players[0].PSObject.Properties.Remove('ship')
    $legacy.players[0].PSObject.Properties.Remove('aboard')
    $legacy.players[0] | Add-Member -NotePropertyName currentMount -NotePropertyValue $null
    $legacy.players[0] | Add-Member -NotePropertyName lastMount -NotePropertyValue $legacyMount
    $legacy.players[0] | Add-Member -NotePropertyName currentShip -NotePropertyValue $null
    $legacy.players[0] | Add-Member -NotePropertyName lastShip -NotePropertyValue $legacyShip
    $legacyStream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes(($legacy | ConvertTo-Json -Depth 20)))
    try {
        $legacySerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($snapshotType)
        $legacySnapshot = $legacySerializer.ReadObject($legacyStream)
        Assert-Cache ($legacySnapshot.Players[0].Foods[0].HasDuration) 'Old cache food timer lost its duration semantics.'
        Assert-Cache ($legacySnapshot.Players[0].Mount.Name -ceq 'Rex' -and -not $legacySnapshot.Players[0].Riding) 'Old current/last mount cache fields did not migrate.'
        Assert-Cache ($legacySnapshot.Players[0].Ship.Name -ceq 'VikingShip' -and -not $legacySnapshot.Players[0].Aboard) 'Old current/last ship cache fields did not migrate.'
        $offlineMethod = $recordType.GetMethod('WithOfflineAge', [Reflection.BindingFlags]'Instance,NonPublic')
        $legacyOffline = $offlineMethod.Invoke($legacySnapshot.Players[0], [object[]]@([float]0))
        Assert-Cache ($legacyOffline.SteamUserId -eq '') 'Old cache did not default missing Steam identity to an empty string.'
        Assert-Cache ($legacyOffline.Followers.Length -eq 0) 'Old cache did not default followers to an empty array.'
    }
    finally { $legacyStream.Dispose() }
    $effect.RemainingSeconds = -10
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($validationError -like 'statuses[[]0].remainingSeconds=*') 'Invalid timer did not identify the failing field.'
    $effect.RemainingSeconds = 0
    $report.Stats.Stamina = [float]::NaN
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($validationError -like 'stats.stamina=*') 'Nonfinite stat did not identify the failing field.'
    $report.Stats.Stamina = 0
    $gearType.GetProperty('IconKey').SetValue($gear.PSObject.BaseObject, $null)
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($validationError -eq 'equipment[0].iconKey is missing') 'Missing icon key was not rejected safely.'
    $retry = [Activator]::CreateInstance($retryType)
    $schedule = $retryType.GetMethod('TrySchedule', [Reflection.BindingFlags]'Instance,NonPublic')
    Assert-Cache ($schedule.Invoke($retry, [object[]]@([float]0))) 'First missing-report refresh was delayed.'
    foreach ($time in @(1, 3, 7, 15, 31, 61, 91)) {
        Assert-Cache (-not $schedule.Invoke($retry, [object[]]@([float]($time - 0.5)))) 'Missing-report retry ignored backoff.'
        Assert-Cache ($schedule.Invoke($retry, [object[]]@([float]$time))) 'Missing-report retry missed its boundary.'
    }
    $retry = [Activator]::CreateInstance($retryType)
    Assert-Cache ($schedule.Invoke($retry, [object[]]@([float]0))) 'New retry state did not reset the delay.'
    $legacyDirectory = Join-Path $directory 'legacy'
    [IO.Directory]::CreateDirectory($legacyDirectory) | Out-Null
    $legacyCache = ($saved | ConvertTo-Json -Depth 20) | ConvertFrom-Json
    $legacyCache.PSObject.Properties.Remove('identityVersion')
    [IO.File]::WriteAllText((Join-Path $legacyDirectory 'JoksterCube.PlayerDetailSync.players.json'), ($legacyCache | ConvertTo-Json -Depth 20))
    Invoke-CacheMethod $loadMethod @($legacyDirectory, $logger, $logger) | Out-Null
    $cleaned = $currentProperty.GetValue($null)
    Assert-Cache ($cleaned.Players.Length -eq 0 -and $cleaned.IdentityVersion -eq 1) 'Legacy identities were not cleared on startup.'

    $duplicateDirectory = Join-Path $directory 'duplicates'
    [IO.Directory]::CreateDirectory($duplicateDirectory) | Out-Null
    $record.LastSeenUtc = '2026-10-06T12:00:00.0000000Z'
    $rejoin.LastSeenUtc = '2026-10-07T12:00:00.0000000Z'
    $duplicateSnapshot = New-Snapshot @($record, $rejoin)
    $duplicateSerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($snapshotType)
    $duplicateStream = [IO.File]::Create((Join-Path $duplicateDirectory 'JoksterCube.PlayerDetailSync.players.json'))
    try { $duplicateSerializer.WriteObject($duplicateStream, $duplicateSnapshot.PSObject.BaseObject) }
    finally { $duplicateStream.Dispose() }
    Invoke-CacheMethod $loadMethod @($duplicateDirectory, $logger, $logger) | Out-Null
    $deduplicated = $currentProperty.GetValue($null)
    Assert-Cache ($deduplicated.Players.Length -eq 1 -and $deduplicated.Players[0].X -eq 42 -and -not $deduplicated.Players[0].Online) 'Startup did not retain only the latest offline record per character.'
    $record.Followers = [Array]::CreateInstance($followerType, 0)
    $cleared = Invoke-CacheMethod $mergeMethod @((New-Snapshot @($record)))
    Assert-Cache ($cleared.Players[0].Followers.Length -eq 0) 'A fresh empty follower report retained old pets.'

    $historyDirectory = Join-Path $directory 'activity-history'
    [IO.Directory]::CreateDirectory($historyDirectory) | Out-Null
    Invoke-CacheMethod $loadMethod @($historyDirectory, $logger, $logger) | Out-Null
    $building = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Building'))))
    Assert-Cache ($null -eq $building.Players[0].LastActivity) 'The first activity was incorrectly also recorded as last activity.'
    $building = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Building'))))
    Assert-Cache ($null -eq $building.Players[0].LastActivity) 'A repeated building report advanced activity history.'
    $pending = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Unclassified'))))
    Assert-Cache ($pending.Players[0].LastActivity.State.ToString() -ceq 'Building') 'Pending activity erased the last classified activity.'
    $logging = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Logging'))))
    Assert-Cache ($logging.Players[0].Activity.State.ToString() -ceq 'Logging' -and $logging.Players[0].LastActivity.State.ToString() -ceq 'Building') 'Building to logging lost the previous activity.'
    Assert-Cache ($logging.Players[0].LastActivity.ObservedAtUtc -ceq $building.Players[0].Activity.ObservedAtUtc) 'Last activity lost its original observation time.'
    $repeatedLogging = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Logging'))))
    Assert-Cache ($repeatedLogging.Players[0].LastActivity.State.ToString() -ceq 'Building') 'Repeated logging replaced building in activity history.'
    $offlineActivity = Invoke-CacheMethod $mergeMethod @((New-Snapshot @()))
    Assert-Cache ($offlineActivity.Players[0].Activity.State.ToString() -ceq 'Offline' -and $offlineActivity.Players[0].LastActivity.State.ToString() -ceq 'Logging') 'Disconnect did not retain logging behind offline activity.'
    $repeatedOffline = Invoke-CacheMethod $mergeMethod @((New-Snapshot @()))
    Assert-Cache ($repeatedOffline.Players[0].Activity.ObservedAtUtc -ceq $offlineActivity.Players[0].Activity.ObservedAtUtc -and $repeatedOffline.Players[0].LastActivity.State.ToString() -ceq 'Logging') 'Repeated offline snapshots changed the transition time or history.'
    $missingReport = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord '' $false))))
    Assert-Cache ($null -eq $missingReport.Players[0].Activity -and $missingReport.Players[0].LastActivity.State.ToString() -ceq 'Logging') 'A reconnect without a report kept offline as current activity or lost history.'
    $joiningHistory = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Joining'))))
    $resumed = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord 'Building'))))
    Assert-Cache ($resumed.Players[0].LastActivity.State.ToString() -ceq 'Logging') 'Rejoining replaced the last classified activity with joining or offline.'
    $disabledActivity = Invoke-CacheMethod $mergeMethod @((New-Snapshot @((New-ActivityRecord ''))))
    Assert-Cache ($null -eq $disabledActivity.Players[0].Activity -and $disabledActivity.Players[0].LastActivity.State.ToString() -ceq 'Building') 'Disabling activity erased history or invented a current state.'

    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $historyPort = $portProbe.LocalEndpoint.Port
    $portProbe.Stop()
    $historyServer = $httpConstructor.Invoke([object[]]@('127.0.0.1', $historyPort, 'cache-regression'))
    $publishHistory = $httpType.GetMethod('Publish', [Reflection.BindingFlags]'Instance,NonPublic')
    try {
        $publishHistory.Invoke($historyServer, [object[]]@($logging.PSObject.BaseObject)) | Out-Null
        Assert-Cache ($httpType.GetMethod('Start', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($historyServer, $null)) 'Activity history test API did not start.'
        $historyUri = "http://127.0.0.1:$historyPort/api/v1/players/800/activity"
        $historyResponse = Invoke-RestMethod $historyUri -Headers $headers
        Assert-Cache ($historyResponse.activity.state -ceq 'logging' -and $historyResponse.lastActivity.state -ceq 'building') 'Activity API did not expose the previous building state.'
        $publishHistory.Invoke($historyServer, [object[]]@($offlineActivity.PSObject.BaseObject)) | Out-Null
        $historyResponse = Invoke-RestMethod $historyUri -Headers $headers
        Assert-Cache ($historyResponse.activity.state -ceq 'offline' -and $historyResponse.lastActivity.state -ceq 'logging') 'Activity API did not expose offline with the last logging state.'
    }
    finally { $historyServer.Dispose() }

    $restoreDirectory = Join-Path $directory 'activity-history-restore'
    [IO.Directory]::CreateDirectory($restoreDirectory) | Out-Null
    $historyCacheStream = [IO.File]::Create((Join-Path $restoreDirectory 'JoksterCube.PlayerDetailSync.players.json'))
    try { $duplicateSerializer.WriteObject($historyCacheStream, $logging.PSObject.BaseObject) }
    finally { $historyCacheStream.Dispose() }
    Invoke-CacheMethod $loadMethod @($restoreDirectory, $logger, $logger) | Out-Null
    $restoredHistory = $currentProperty.GetValue($null)
    Assert-Cache ($restoredHistory.Players[0].Activity.State.ToString() -ceq 'Offline' -and $restoredHistory.Players[0].LastActivity.State.ToString() -ceq 'Logging') 'Reload did not turn the saved current activity into the last offline activity.'
    Assert-Cache ($restoredHistory.Players[0].LastActivity.ObservedAtUtc -ceq $logging.Players[0].Activity.ObservedAtUtc) 'Reload changed the last activity observation time.'
    Write-Output 'PASS: distinct activity history, transient states, offline transitions, reconnects, HTTP schema, and cache restoration.'

    Write-Output 'PASS: full follower lists, authentication, resources, shield timers, cache retention, validation, and legacy defaults.'
    Write-Output 'PASS: single mount/ship records, riding/aboard state, singular endpoints, cache, dismount, and offline handling.'
    Write-Output 'PASS: refresh retries start immediately, back off to 30 seconds, and reset.'
    Write-Output 'PASS: untimed status and modded gear accepted; invalid fields identified precisely.'
    Write-Output 'PASS: background save, reload, offline retention, and reconnect merge.'
    Write-Output 'PASS: Steam identity in API details and cache; legacy cache compatibility.'
    Write-Output 'PASS: one record per character; same-name and same-account characters remain distinct.'
    Write-Output 'PASS: startup clears legacy identities and collapses duplicate character records.'
    Write-Output 'PASS: native and crossplay Steam IDs resolved; invalid and non-Steam IDs excluded.'
}
finally {
    $watcher.Dispose()
}