[CmdletBinding()]
param([ValidateRange(1,65535)][int]$Port = 17777, [string]$UnityEditor)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalRaidSession.ps1')
$root = Split-Path -Parent $PSScriptRoot
Initialize-LocalRaidWindows
$workArea = [LocalRaidWindows]::WorkArea()
$closed = $null
if ($UnityEditor) {
    if (![Console]::IsInputRedirected) { throw 'Start the two-player editor session from the Heavy Pockets menu.' }
    . (Join-Path $PSScriptRoot 'EditorRaidLifetime.ps1')
    $closed = [EditorRaidLifetime]::MonitorEditor()
    if ($closed.IsCompleted) { return }
    try { & (Join-Path $PSScriptRoot 'Build-Raid.ps1') -UnityEditor $UnityEditor -ForEditorPair -StopRequested $closed }
    catch [OperationCanceledException] { return }
    if ($closed.IsCompleted) { return }
}
$security = New-LocalRaidSession
$buildSlot = Open-LocalRaidBuild $root
try {
$build = $buildSlot.Path
$server = Join-Path $build 'Server/HeavyPocketsServer.exe'
$client = Join-Path $build 'Client/HeavyPockets.exe'
foreach ($file in $server,$client) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing $file. Run Tools/Build-Raid.ps1 first." }
}
# Prefer the documented port. A second local session can use its own free port.
if (!$PSBoundParameters.ContainsKey('Port')) {
    $inUse = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners() |
        Where-Object { $_.Port -eq $Port }
    if ($inUse) {
        $reservation = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
        try { $Port = $reservation.Client.LocalEndPoint.Port } finally { $reservation.Dispose() }
    }
}

$run = Join-Path ([IO.Path]::GetTempPath()) ('HeavyPockets-Play-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
function Start-RaidProcess([string]$exe, [string[]]$arguments, [string]$role) {
    $p = Start-LocalRaidChild -Exe $exe -Arguments $arguments -Security $security -Role $role -Visible:($role -ne 'server')
    $children.Add($p)
    return $p
}
function Wait-Log([Diagnostics.Process]$process,[string]$log,[string]$marker) {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($closed -and $closed.IsCompleted) { throw [OperationCanceledException]::new() }
        if ($process.HasExited) { throw "Process stopped (code $($process.ExitCode)). See $log" }
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { return }
        Start-Sleep -Milliseconds 150
    }
    throw "Timeout waiting for $marker. See $log"
}
try {
    Write-Host "Local raid on 127.0.0.1:$Port. Logs: $run"
    $serverLog = Join-Path $run 'server.log'
    $s = Start-RaidProcess $server @('-server','-batchmode','-nographics','-twoPlayerTest','-port',"$Port",'-logFile',$serverLog) 'server'
    Wait-Log $s $serverLog 'HEAVY POCKETS SERVER READY'
    $clientLog = Join-Path $run 'A.log'
    $flags = @('-client','-port',"$Port",'-screen-fullscreen','0')
    $a = Start-RaidProcess $client ($flags + @('-logFile',$clientLog)) 'A'
    Wait-Log $a $clientLog 'HEAVY POCKETS CLIENT CONNECTED'
    [void][LocalRaidWindows]::Tile($a, $workArea, 0)
    $secondLog = Join-Path $run 'B.log'
    $b = Start-RaidProcess $client ($flags + @('-logFile',$secondLog)) 'B'
    Wait-Log $b $secondLog 'HEAVY POCKETS CLIENT CONNECTED'
    $firstPlaced = [LocalRaidWindows]::Tile($a, $workArea, 0)
    $secondPlaced = [LocalRaidWindows]::Tile($b, $workArea, 1)
    if (!$firstPlaced -or !$secondPlaced) { Write-Warning 'Windows did not position one of the game windows. Drag its title bar to reposition it.' }
    Write-Host 'Both players connected. Alt+Tab switches players. Esc releases the mouse.'
    while (!$a.HasExited -or !$b.HasExited) {
        if ($closed -and ($closed.IsCompleted -or $a.HasExited -or $b.HasExited)) { break }
        if ($s.HasExited) { throw "Server stopped. See $serverLog" }
        Start-Sleep -Milliseconds 500
    }
} catch [OperationCanceledException] {
    if (!$closed -or !$closed.IsCompleted) { throw }
} finally {
    foreach ($p in $children) {
        if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }
        $p.Dispose()
    }
}
} finally {
    if ($buildSlot.Lease) { $buildSlot.Lease.Dispose() }
}
