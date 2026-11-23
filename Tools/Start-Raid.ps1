[CmdletBinding()]
param([ValidateRange(1,65535)][int]$Port = 17777)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalRaidSession.ps1')
$root = Split-Path -Parent $PSScriptRoot
$security = New-LocalRaidSession
$build = Get-LocalRaidBuild $root
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
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Process stopped (code $($process.ExitCode)). See $log" }
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch $marker -Quiet)) { return }
        Start-Sleep -Milliseconds 150
    }
    throw "Timeout waiting for $marker. See $log"
}
try {
    Write-Host "Local raid on 127.0.0.1:$Port. Logs: $run"
    $serverLog = Join-Path $run 'server.log'
    $s = Start-RaidProcess $server @('-server','-batchmode','-nographics','-port',"$Port",'-logFile',$serverLog) 'server'
    Wait-Log $s $serverLog 'HEAVY POCKETS SERVER READY'
    $clientLog = Join-Path $run 'A.log'
    $a = Start-RaidProcess $client @('-client','-port',"$Port",'-logFile',$clientLog) 'A'
    Wait-Log $a $clientLog 'HEAVY POCKETS CLIENT CONNECTED'
    $secondLog = Join-Path $run 'B.log'
    $b = Start-RaidProcess $client @('-client','-port',"$Port",'-logFile',$secondLog) 'B'
    Wait-Log $b $secondLog 'HEAVY POCKETS CLIENT CONNECTED'
    Write-Host 'Heavy Pockets is ready. Click a game window to play. Close both game windows to finish this raid.'
    while (!$a.HasExited -or !$b.HasExited) {
        if ($s.HasExited) { throw "Server stopped. See $serverLog" }
        Start-Sleep -Milliseconds 500
    }
} finally {
    foreach ($p in $children) {
        if (!$p.HasExited) { $p.Kill(); $p.WaitForExit() }
        $p.Dispose()
    }
}
