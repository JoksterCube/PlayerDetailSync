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
$statisticNameType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerStatisticName', $true)
$statisticUnitType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerStatisticUnit', $true)
$activityType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivity', $true)
$activityStatusType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerActivityStatus', $true)
$validatorType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerReportValidator', $true)
$retryType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.ClientRefreshRetry', $true)
$storeType = $assembly.GetType('JoksterCube.PlayerDetailSync.Api.PlayerSnapshotStore', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$mergeMethod = $storeType.GetMethod('MergeAndSave', $flags)
$loadMethod = $storeType.GetMethod('Load', $flags)
$currentProperty = $storeType.GetProperty('Current', $flags)
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

try {
    $steamIdentityMethod = $snapshotType.GetMethod('GetSteamUserId', $flags)
    Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @('76561198000000000')) -ceq '76561198000000000') 'Native Steam socket identity was not resolved.'
    $crossplaySteamIdentity = [Splatform.PlatformUserID]::new('Steam', '76561198000000000').ToString()
    Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @($crossplaySteamIdentity)) -ceq '76561198000000000') 'Crossplay Steam socket identity was not resolved.'
    foreach ($hostName in @('', '0', 'Steam_not-an-id', 'PlayFab_76561198000000000', [Splatform.PlatformUserID]::new('Xbox', '76561198000000000').ToString())) {
        Assert-Cache ((Invoke-CacheMethod $steamIdentityMethod @($hostName)) -ceq '') 'Non-Steam or invalid identity was exposed as a Steam account.'
    }
    Invoke-CacheMethod $loadMethod @($directory, $logger, $logger) | Out-Null
    $record = [Activator]::CreateInstance($recordType)
    $record.PlayerId = '123456'
    $record.SteamUserId = '76561198000000000'
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
    $deathCount.Name = [Enum]::Parse($statisticNameType, 'Deaths')
    $deathCount.Value = 2
    $deathCount.Unit = [Enum]::Parse($statisticUnitType, 'Count')
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
    $response = Invoke-CacheMethod ($httpType.GetMethod('ToPlayerResponse', $flags)) @($record)
    $responseSerializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($response.GetType())
    $responseStream = [IO.MemoryStream]::new()
    try {
        $responseSerializer.WriteObject($responseStream, $response)
        $responseJson = [Text.Encoding]::UTF8.GetString($responseStream.ToArray()) | ConvertFrom-Json
        Assert-Cache ($responseJson.steamUserId -ceq $record.SteamUserId) 'Detailed API response lost Steam identity.'
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
        $httpType.GetMethod('Publish', [Reflection.BindingFlags]'Instance,NonPublic').Invoke($server, [object[]]@((New-Snapshot @($record)).PSObject.BaseObject))
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
        $pluralMountRoute = Invoke-WebRequest "$baseUri/mounts" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($pluralMountRoute.StatusCode -eq 404) 'Plural mount route is still available.'
        $mountDetailRoute = Invoke-WebRequest "$baseUri/mount/current" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($mountDetailRoute.StatusCode -eq 404) 'Nested mount detail route is still available.'
        $pluralShipRoute = Invoke-WebRequest "$baseUri/ships" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($pluralShipRoute.StatusCode -eq 404) 'Plural ship route is still available.'
        $shipDetailRoute = Invoke-WebRequest "$baseUri/ship/current" -Headers $headers -SkipHttpErrorCheck
        Assert-Cache ($shipDetailRoute.StatusCode -eq 404) 'Nested ship detail route is still available.'
        $playerStats = Invoke-RestMethod "$baseUri/stats" -Headers $headers
        Assert-Cache ($playerStats.vitals.health -eq 75 -and $playerStats.lifetime[0].name -ceq 'deaths' -and $playerStats.lifetime[0].value -eq 2) 'Stats endpoint lost vitals or lifetime statistics.'
        $categoryResponses += $playerStats
        $privateKeys = Invoke-RestMethod "$baseUri/keys" -Headers $headers
        Assert-Cache ($privateKeys.privateKeys.Count -eq 2 -and $privateKeys.privateKeys[0] -ceq 'defeated_eikthyr') 'Private key endpoint lost per-player game keys.'
        $categoryResponses += $privateKeys
        $currentActivity = Invoke-RestMethod "$baseUri/activity" -Headers $headers
        Assert-Cache ($currentActivity.activity.state -ceq 'sailing') 'Activity endpoint lost the current state.'
        $categoryResponses += $currentActivity
        $repeatedPlayerMetadata = @('playerId', 'online', 'lastSeenUtc', 'characterDataAvailable')
        foreach ($categoryResponse in $categoryResponses) {
            Assert-Cache ($categoryResponse.characterDataAgeSeconds -ge 0) 'Category response lost character data age.'
            foreach ($propertyName in $repeatedPlayerMetadata) {
                Assert-Cache ($propertyName -notin $categoryResponse.PSObject.Properties.Name) "Category response repeated '$propertyName'."
            }
        }
        foreach ($key in @('stamina', 'maxStamina', 'eitr', 'maxEitr')) {
            Assert-Cache ($key -notin $followerDetails.stats.PSObject.Properties.Name) "Follower stats still expose '$key'."
            Assert-Cache ($key -in $playerStats.vitals.PSObject.Properties.Name) "Player stats lost '$key'."
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
    Assert-Cache ($saved.players[0].followers[0].statuses[0].shieldRemainingAbsorption -eq 150) 'Cache lost pet protection bubble.'
    Assert-Cache ($saved.players[0].mount.name -ceq 'Rex' -and $saved.players[0].riding) 'Serialized cache lost mount details or riding state.'
    Assert-Cache ($saved.players[0].mount.statuses[0].shieldRemainingAbsorption -eq 150) 'Cache lost mount shield absorption.'
    Assert-Cache ($saved.players[0].ship.name -ceq 'VikingShip' -and $saved.players[0].aboard -and $saved.players[0].lifetimeStats[0].value -eq 2) 'Serialized cache lost ship state or lifetime stats.'
    Assert-Cache ($saved.players[0].privateKeys.Count -eq 2 -and $saved.players[0].activity.state -ceq 'sailing') 'Serialized cache lost private keys or activity.'
    Assert-Cache ($saved.players[0].lifetimeStats[0].name -ceq 'deaths' -and $saved.players[0].lifetimeStats[0].unit -ceq 'count') 'Serialized statistic enums changed their JSON values.'

    Invoke-CacheMethod $loadMethod @($directory, $logger, $logger) | Out-Null
    $restored = $currentProperty.GetValue($null)
    Assert-Cache ($restored.Players.Length -eq 1 -and -not $restored.Players[0].Online) 'Reload must restore players as offline.'
    Assert-Cache ($restored.Players[0].Stats.Health -eq 75) 'Reload lost last-known health.'
    Assert-Cache ($restored.Players[0].SteamUserId -eq $record.SteamUserId) 'Reload lost Steam identity.'
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
    foreach ($statusName in @(
        'Dead', 'RecoveringCorpse', 'Drowning', 'Freezing', 'Burning', 'FightingBoss',
        'SiegingFortress', 'HuntingSerpent', 'DefendingBase', 'InPvP', 'InCombat',
        'DungeonDelving', 'LostAtSea', 'Portaling', 'CartHauling', 'Terraforming',
        'Mining', 'Logging', 'Farming', 'TamingRanching', 'Cooking', 'Brewing', 'Smelting',
        'Crafting', 'SortingChests', 'Trading', 'StalkingSneaking', 'Hunting', 'Encumbered',
        'Resting', 'IdlingAtBase', 'IdlingAtOutpost', 'IdlingInDanger', 'Exploring', 'Adventuring'
    )) {
        Assert-Cache ([Enum]::IsDefined($activityStatusType, $statusName)) "Activity status '$statusName' is missing."
    }
    $report = [Activator]::CreateInstance($dataType)
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
    $report.Activity.ActiveStates = [Array]::CreateInstance($activityStatusType, [Enum]::GetValues($activityStatusType).Length)
    [Enum]::GetValues($activityStatusType).CopyTo($report.Activity.ActiveStates, 0)
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($null -eq $validationError) 'Expanded activity state list was rejected.'
    $report.Activity = $null
    $validationError = Invoke-CacheMethod $validate @($report)
    Assert-Cache ($null -eq $validationError) 'Disabled activity collection report was rejected.'
    $report.Activity = $activity
    $serializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($dataType)
    $stream = [IO.MemoryStream]::new()
    try {
        $serializer.WriteObject($stream, $report.PSObject.BaseObject)
        $stream.Position = 0
        $roundTrip = $serializer.ReadObject($stream)
        Assert-Cache (-not $roundTrip.Statuses[0].HasDuration -and $roundTrip.Equipment[0].Quality -eq 20) 'Report JSON lost untimed status or gear.'
        Assert-Cache ($roundTrip.Followers[0].FollowerId -ceq '123:456') 'Client report JSON lost follower ID.'
        Assert-Cache ($roundTrip.Mount.Name -ceq 'Rex' -and $roundTrip.Mount.Stats.Stamina -eq 80) 'Client report JSON lost mount details.'
        Assert-Cache ($roundTrip.Ship.Name -ceq 'VikingShip') 'Client report JSON lost ship details.'
        $agingMethod = $dataType.GetMethod('WithElapsedSeconds', [Reflection.BindingFlags]'Instance,NonPublic')
        $aged = $agingMethod.Invoke($roundTrip, [object[]]@([float]60))
        Assert-Cache (-not $aged.Statuses[0].HasDuration -and $aged.Statuses[0].RemainingSeconds -eq 0) 'Timer aging lost untimed status semantics.'
        Assert-Cache ($aged.Followers[0].Statuses[0].RemainingSeconds -eq 0 -and $roundTrip.Followers[0].Statuses[0].RemainingSeconds -eq 30) 'Follower aging failed to clamp or mutated its original sample.'
        Assert-Cache ($aged.Followers[0].Statuses[0].ShieldRemainingAbsorption -eq 150) 'Timer aging lost shield absorption.'
    }
    finally { $stream.Dispose() }
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