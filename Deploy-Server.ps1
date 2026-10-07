param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath
)

$ErrorActionPreference = "Stop"

function Get-BuildProperty([xml]$Project, [xml]$LocalProject, [string]$Name) {
    $node = $LocalProject.SelectSingleNode("/Project/PropertyGroup/$Name")
    if ($null -eq $node) {
        $node = $Project.SelectSingleNode("/Project/PropertyGroup/$Name")
    }
    if ($null -eq $node) {
        throw "Deployment property '$Name' is missing from environment.props."
    }
    return $node.InnerText
}

$environmentPath = Join-Path $PSScriptRoot "environment.props"
$localEnvironmentPath = Join-Path $PSScriptRoot "environment.local.props"
if (-not (Test-Path -LiteralPath $localEnvironmentPath)) {
    throw "Create environment.local.props and set DeployApiPassword before deploying."
}

[xml]$environment = Get-Content -LiteralPath $environmentPath -Raw
[xml]$localEnvironment = Get-Content -LiteralPath $localEnvironmentPath -Raw
$passwordNode = $localEnvironment.SelectSingleNode("/Project/PropertyGroup/DeployApiPassword")
if ($null -eq $passwordNode -or [string]::IsNullOrWhiteSpace($passwordNode.InnerText)) {
    throw "Set a non-empty DeployApiPassword in environment.local.props before deploying."
}
$password = $passwordNode.InnerText
if ($password -match "[\r\n]") {
    throw "DeployApiPassword must be a single line."
}
if ($password -ne $password.Trim()) {
    throw "DeployApiPassword must not start or end with whitespace."
}
if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Build output was not found: $AssemblyPath"
}

$localPluginDirectory = Get-BuildProperty $environment $localEnvironment "DeployLocalPluginDirectory"
if (-not (Test-Path -LiteralPath $localPluginDirectory -PathType Container)) {
    throw "Local Valheim plugin directory was not found: $localPluginDirectory"
}
$localPluginPath = Join-Path $localPluginDirectory "PlayerDetailSync.dll"
try {
    Copy-Item -LiteralPath $AssemblyPath -Destination $localPluginPath -Force -ErrorAction Stop
}
catch {
    throw "Could not install the local plugin. Close Valheim if it is running, then rebuild. $($_.Exception.Message)"
}

$remoteHost = Get-BuildProperty $environment $localEnvironment "DeployRemoteHost"
$composeDirectory = Get-BuildProperty $environment $localEnvironment "DeployComposeDirectory"
$service = Get-BuildProperty $environment $localEnvironment "DeployComposeService"
$pluginDirectory = Get-BuildProperty $environment $localEnvironment "DeployContainerPluginDirectory"
$configDirectory = Get-BuildProperty $environment $localEnvironment "DeployContainerConfigDirectory"
$configFileName = "JoksterCube.PlayerDetailSync.cfg"
$nonce = [Guid]::NewGuid().ToString("N")
$dllStageName = ".PlayerDetailSync.$nonce.dll.stage"
$configStageName = ".PlayerDetailSync.$nonce.cfg.stage"
$containerDllStage = "$pluginDirectory/.$nonce.dll.stage"
$containerConfigStage = "$configDirectory/.$nonce.cfg.stage"
$containerConfigPath = "$configDirectory/$configFileName"
$containerDllPath = "$pluginDirectory/PlayerDetailSync.dll"
$remoteDllStage = "$composeDirectory/$dllStageName"
$remoteConfigStage = "$composeDirectory/$configStageName"
$sshOptions = @("-o", "BatchMode=yes", "-o", "ConnectTimeout=10")
$temporaryConfig = New-TemporaryFile
$stagingAttempted = $false

try {
    $readConfigCommand = "cd $composeDirectory && docker compose exec -T $service sh -c 'if [ -f $containerConfigPath ]; then cat $containerConfigPath; fi'"
    $configLines = & ssh @sshOptions $remoteHost $readConfigCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the server config through Docker Compose. Confirm the valheim service is running."
    }

    $configText = ""
    if ($null -ne $configLines) {
        $configText = [string]::Join("`n", [string[]]$configLines)
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    if ($configText.Length -gt 0) {
        $splitLines = [regex]::Split($configText, "\r?\n")
        for ($index = 0; $index -lt $splitLines.Length; $index++) {
            if ($index -eq ($splitLines.Length - 1) -and $splitLines[$index] -eq "") { continue }
            $lines.Add($splitLines[$index])
        }
    }

    $sectionStart = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*\[1 - HTTP API\]\s*$') {
            $sectionStart = $index
            break
        }
    }

    if ($sectionStart -lt 0) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -ne "") { $lines.Add("") }
        $lines.Add("[1 - HTTP API]")
        $lines.Add("Password = $password")
    }
    else {
        $sectionEnd = $lines.Count
        for ($index = $sectionStart + 1; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -match '^\s*\[') {
                $sectionEnd = $index
                break
            }
        }

        $passwordLine = -1
        for ($index = $sectionStart + 1; $index -lt $sectionEnd; $index++) {
            if ($lines[$index] -match '^\s*Bearer Token\s*=') {
                $lines.RemoveAt($index)
                $index--
                $sectionEnd--
            }
            elseif ($lines[$index] -match '^\s*Password\s*=') {
                $passwordLine = $index
            }
        }

        if ($passwordLine -ge 0) {
            $lines[$passwordLine] = "Password = $password"
        }
        else {
            $lines.Insert($sectionEnd, "Password = $password")
        }
    }

    $configContents = [string]::Join([Environment]::NewLine, $lines) + [Environment]::NewLine
    [IO.File]::WriteAllText($temporaryConfig.FullName, $configContents, [Text.UTF8Encoding]::new($false))

    $stagingAttempted = $true
    & scp -q @sshOptions $AssemblyPath "${remoteHost}:$remoteDllStage"
    if ($LASTEXITCODE -ne 0) { throw "Could not stage the plugin DLL on the server." }

    & scp -q @sshOptions $temporaryConfig.FullName "${remoteHost}:$remoteConfigStage"
    if ($LASTEXITCODE -ne 0) { throw "Could not stage the server config on the server." }

    $legacyConfigPath = "$configDirectory/config/$configFileName"
    $applyCommand = "cd $composeDirectory && docker compose cp $dllStageName ${service}:$containerDllStage && docker compose exec -T --user 0 $service sh -c 'mv $containerDllStage $containerDllPath' && docker compose exec -T --user 0 $service sh -c 'mkdir -p $configDirectory' && docker compose cp $configStageName ${service}:$containerConfigStage && docker compose exec -T --user 0 $service sh -c 'mv $containerConfigStage $containerConfigPath && rm -f $legacyConfigPath' && rm -f $dllStageName $configStageName && docker compose up -d --force-recreate"
    $applyOutput = & ssh @sshOptions $remoteHost $applyCommand 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Remote deployment failed: $($applyOutput -join ' ')"
    }

    $stagingAttempted = $false
    Write-Host "Installed PlayerDetailSync.dll locally and deployed it with the server API password config."
}
finally {
    if ($stagingAttempted) {
        $cleanupCommand = "cd $composeDirectory && rm -f $dllStageName $configStageName; docker compose exec -T --user 0 $service sh -c 'rm -f $containerDllStage $containerConfigStage' 2>/dev/null || true"
        $cleanupOutput = & ssh @sshOptions $remoteHost $cleanupCommand 2>&1
        $null = $cleanupOutput
    }
    Remove-Item -LiteralPath $temporaryConfig.FullName -Force -ErrorAction SilentlyContinue
    $password = $null
    $configText = $null
    $configLines = $null
}
