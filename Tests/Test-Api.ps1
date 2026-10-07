param(
    [int]$Port = 8785
)

$ErrorActionPreference = "Stop"

function Get-PropertyValue([xml]$Project, [string]$Name) {
    $node = $Project.SelectSingleNode("/Project/PropertyGroup/$Name")
    if ($null -eq $node) {
        throw "Required property '$Name' is missing."
    }
    return $node.InnerText
}

function Invoke-SshWithInput([string]$RemoteCommand, [string]$InputText) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new("ssh")
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    foreach ($argument in @("-o", "BatchMode=yes", "-o", "ConnectTimeout=10", $script:RemoteHost, $RemoteCommand)) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Could not start ssh."
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not [string]::IsNullOrEmpty($InputText)) {
        $process.StandardInput.Write($InputText)
    }
    $process.StandardInput.Close()
    $process.WaitForExit()

    [pscustomobject]@{
        ExitCode = $process.ExitCode
        StandardOutput = $stdoutTask.GetAwaiter().GetResult()
        StandardError = $stderrTask.GetAwaiter().GetResult()
    }
}

$sharedPropsPath = Join-Path $PSScriptRoot "..\environment.props"
$localPropsPath = Join-Path $PSScriptRoot "..\environment.local.props"
if (-not (Test-Path -LiteralPath $localPropsPath)) {
    throw "Create environment.local.props with DeployRemoteHost, DeployComposeDirectory, and DeployApiPassword first."
}

[xml]$sharedProps = Get-Content -LiteralPath $sharedPropsPath -Raw
[xml]$localProps = Get-Content -LiteralPath $localPropsPath -Raw
$script:RemoteHost = Get-PropertyValue $localProps "DeployRemoteHost"
$composeDirectory = Get-PropertyValue $localProps "DeployComposeDirectory"
$composeService = Get-PropertyValue $sharedProps "DeployComposeService"
$password = Get-PropertyValue $localProps "DeployApiPassword"

if ([string]::IsNullOrWhiteSpace($password)) {
    throw "DeployApiPassword must be non-empty before running the server API test."
}
if ($password -match "[\r\n]") {
    throw "DeployApiPassword must be a single line."
}

$baseAddress = "http://127.0.0.1:$Port"
$unauthenticatedCommand = "cd $composeDirectory && docker compose exec -T $composeService curl -q --noproxy '*' --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 5 $baseAddress/api/v1/health"
$unauthenticated = Invoke-SshWithInput $unauthenticatedCommand ""
if ($unauthenticated.ExitCode -ne 0) {
    throw "Could not reach the API in the Compose container: $($unauthenticated.StandardError.Trim())"
}
if ($unauthenticated.StandardOutput.Trim() -ne "401") {
    throw "Expected HTTP 401 without a password; received '$($unauthenticated.StandardOutput.Trim())'."
}
Write-Host "PASS: API rejects requests without a password (401)."

$remoteCurl = "cd $composeDirectory && docker compose exec -T -i $composeService curl -q --noproxy '*' --silent --show-error --fail --header @-"

$healthResponse = Invoke-SshWithInput "$remoteCurl $baseAddress/api/v1/health" "X-API-Password: $password`n"
if ($healthResponse.ExitCode -ne 0) {
    throw "Authenticated health request failed: $($healthResponse.StandardError.Trim())"
}
$health = $healthResponse.StandardOutput | ConvertFrom-Json
if ($health.status -ne "ok" -or $null -eq $health.updatedAtUtc -or $null -eq $health.connectedPlayerCount) {
    throw "Health response is missing expected fields."
}
Write-Host "PASS: health is ok; connected players: $($health.connectedPlayerCount)."

$listResponse = Invoke-SshWithInput "$remoteCurl $baseAddress/api/v1/players" "X-API-Password: $password`n"
if ($listResponse.ExitCode -ne 0) {
    throw "Authenticated players-list request failed: $($listResponse.StandardError.Trim())"
}
$playerList = $listResponse.StandardOutput | ConvertFrom-Json
if ($null -eq $playerList.updatedAtUtc -or $null -eq $playerList.players) {
    throw "Player list response is missing updatedAtUtc or players."
}

foreach ($summary in @($playerList.players)) {
    if ([string]::IsNullOrWhiteSpace([string]$summary.playerId) -or
        $summary.steamUserId -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string]$summary.name) -or
        $summary.online -isnot [bool] -or
        [string]::IsNullOrWhiteSpace([string]$summary.lastSeenUtc) -or
        $null -eq $summary.position -or
        $null -eq $summary.position.x -or
        $null -eq $summary.position.y -or
        $null -eq $summary.position.z) {
        throw "A player summary is missing its ID, name, or position."
    }

    $detailResponse = Invoke-SshWithInput "$remoteCurl $baseAddress/api/v1/players/$($summary.playerId)" "X-API-Password: $password`n"
    if ($detailResponse.ExitCode -ne 0) {
        throw "Details request for player '$($summary.playerId)' failed: $($detailResponse.StandardError.Trim())"
    }
    $player = $detailResponse.StandardOutput | ConvertFrom-Json
    if ($player.playerId -ne $summary.playerId -or
        $player.steamUserId -isnot [string] -or
        $player.steamUserId -cne $summary.steamUserId -or
        $player.online -isnot [bool] -or
        $player.characterDataAvailable -isnot [bool] -or
        [string]::IsNullOrWhiteSpace([string]$player.lastSeenUtc) -or
        $null -eq $player.characterDataAgeSeconds -or
        $null -eq $player.statuses -or $null -eq $player.foods -or
        $null -eq $player.stats -or $null -eq $player.equipment) {
        throw "Detailed response for '$($summary.name)' is missing character data."
    }

    if (-not $player.characterDataAvailable) {
        if ($player.online) { throw 'An online player has not supplied a character report; inspect client-report logs.' }
        Write-Host 'Offline player retained without a previous character report.'
        continue
    }

    foreach ($timedEffect in @($player.statuses) + @($player.foods)) {
        if ([string]::IsNullOrWhiteSpace([string]$timedEffect.name) -or
            $null -eq $timedEffect.remainingSeconds -or
            $timedEffect.hasDuration -isnot [bool] -or
            [string]::IsNullOrWhiteSpace([string]$timedEffect.iconKey)) {
            throw "A status or food record is missing its name, duration flag, timer, or icon key."
        }
    }
    foreach ($statName in @('health', 'maxHealth', 'stamina', 'maxStamina', 'eitr', 'maxEitr')) {
        if ($null -eq $player.stats.$statName) { throw "Player stats are missing '$statName'." }
    }
    foreach ($gear in @($player.equipment)) {
        if ([string]::IsNullOrWhiteSpace([string]$gear.name) -or
            [string]::IsNullOrWhiteSpace([string]$gear.slot) -or
            $null -eq $gear.quality -or
            [string]::IsNullOrWhiteSpace([string]$gear.iconKey)) {
            throw "An equipment record is missing name, slot, quality, or icon key."
        }
    }
}

Write-Host "PASS: player list schema is valid; records: $(@($playerList.players).Count)."
if (@($playerList.players).Count -eq 0) {
    Write-Host 'SKIP: no cached players to exercise the detail endpoint.'
}
else {
    Write-Host "PASS: detailed cached-player schema is valid."
}

$password = $null