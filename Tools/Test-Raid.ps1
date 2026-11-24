[CmdletBinding()]
param(
    [ValidateRange(1, 65535)][int]$Port = 17777,
    [string]$ReportRoot = ([IO.Path]::Combine([IO.Path]::GetTempPath(), 'HeavyPocketsTests')),
    [switch]$HeadlessClients,
    [switch]$ShowClients,
    [switch]$CaptureMotion,
    [switch]$SurfacesOnly
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalRaidSession.ps1')
$security = New-LocalRaidSession
$project = Split-Path -Parent $PSScriptRoot
$build = Get-LocalRaidBuild $project
$serverExe = Join-Path $build 'Server/HeavyPocketsServer.exe'
$clientExe = Join-Path $build 'Client/HeavyPockets.exe'
foreach ($file in $serverExe, $clientExe) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build missing: $file" }
}
$run = [IO.Path]::Combine([IO.Path]::GetFullPath($ReportRoot), [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()

function Start-Player([string]$name, [string]$exe, [string[]]$options) {
    $argsForPlayer = @('-port', "$Port", '-logFile', "$run/$name.log", '-auditPath', "$run/$name.jsonl") + $options
    $visible = $ShowClients -and !$HeadlessClients -and $name -ne 'server'
    $process = Start-LocalRaidChild -Exe $exe -Arguments $argsForPlayer -Security $security -Role $name -Visible:$visible
    $processes.Add($process)
    Write-Host "$name PID $($process.Id)"
    return $process
}

function Wait-For([string]$name, [scriptblock]$condition, [int]$seconds) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (& $condition)) {
        foreach ($role in 'A','B') {
            $failed = Join-Path $run "$role.failed"
            if (Test-Path -LiteralPath $failed) { throw "$role failed: $([IO.File]::ReadAllText($failed))" }
        }
        foreach ($p in $processes) {
            if ($p.HasExited -and ($p.Id -eq $server.Id -or $p.ExitCode -ne 0)) {
                throw "PID $($p.Id) exited ($($p.ExitCode)) during $name"
            }
        }
        if ($timer.Elapsed.TotalSeconds -gt $seconds) { throw "Timeout waiting for $name" }
        Start-Sleep -Milliseconds 100
    }
}

try {
    Write-Host "Evidence directory: $run"
    $server = Start-Player 'server' $serverExe @('-server','-batchmode','-nographics')
    $serverLog = Join-Path $run 'server.log'
    Wait-For 'server bind' {
        (Test-Path -LiteralPath $serverLog) -and (Select-String -LiteralPath $serverLog -SimpleMatch 'HEAVY POCKETS SERVER READY' -Quiet)
    } 60

    $options = @('-client','-probeDir',$run,'-screen-fullscreen','0','-screen-width','1152','-screen-height','720','-force-d3d11')
    if ($ShowClients -and !$HeadlessClients) { $options += '-probeCapture' }
    if ($CaptureMotion -and $ShowClients -and !$HeadlessClients) { $options += '-probeMotion' }
    if ($SurfacesOnly) { $options += '-probeSurfaces' }
    if ($HeadlessClients) { $options += @('-batchmode','-nographics') }
    $a = Start-Player 'A' $clientExe ($options + @('-probeRole','A'))
    Wait-For 'first client connection' { Test-Path -LiteralPath (Join-Path $run 'A.connected') } 60
    $b = Start-Player 'B' $clientExe ($options + @('-probeRole','B'))
    [IO.File]::WriteAllText((Join-Path $run 'processes.json'), (@{
        server = $server.Id; A = $a.Id; B = $b.Id; headlessClients = $HeadlessClients.IsPresent
        captureFrames = ($ShowClients.IsPresent -and !$HeadlessClients.IsPresent)
    } | ConvertTo-Json))
    if (!$SurfacesOnly) {
        Wait-For 'blocked open before loot generation' { Test-Path -LiteralPath (Join-Path $run 'A.wall-denied') } 45
        $generated = 'HEAVY POCKETS CRATE GENERATED after authorized open'
        if (Select-String -Quiet -LiteralPath $serverLog -SimpleMatch $generated) {
            throw 'Crate was populated before the first authorized search'
        }
        [IO.File]::WriteAllText((Join-Path $run 'server-before-search.checked'), '0')
    }
    Wait-For 'both clients complete' {
        (Test-Path -LiteralPath (Join-Path $run 'A.done')) -and (Test-Path -LiteralPath (Join-Path $run 'B.done'))
    } 205
    foreach ($client in $a,$b) {
        if (-not $client.WaitForExit(15000) -or $client.ExitCode -ne 0) { throw "Client did not exit cleanly: $($client.Id)" }
    }
    if ($SurfacesOnly) {
        foreach ($role in 'A','B') {
            foreach ($mark in 'freight-1','freight-2','freight-3','landed') {
                if (!(Test-Path -LiteralPath (Join-Path $run "$role.$mark"))) { throw "Missing $role.$mark" }
            }
        }
        Write-Host 'Both clients confirmed the 0.44, 0.94 and 1.44 m landings and the unassisted drop to the quay.'
        return
    }
    $generationCount = @(Select-String -LiteralPath $serverLog -SimpleMatch $generated).Count
    [IO.File]::WriteAllText((Join-Path $run 'server-generation.count'), [string]$generationCount)
    & (Join-Path $PSScriptRoot 'Check-RaidAudit.ps1') -RunDirectory $run
} finally {
    foreach ($p in $processes) {
        try {
            if (-not $p.HasExited) { $p.Kill(); [void]$p.WaitForExit(5000) }
        } finally { $p.Dispose() }
    }
    Write-Host "Logs and wire traces: $run"
}
