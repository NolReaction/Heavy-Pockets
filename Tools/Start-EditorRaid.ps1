[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$server = $null
$failure = 'Could not initialize the local raid helper.'
try {
    # Unity launches this script in a dedicated process and holds the input pipe open.
    if (![Console]::IsInputRedirected -or ![Console]::IsOutputRedirected) {
        $failure = 'Start the editor raid by pressing Play in Unity.'
        throw $failure
    }
    $root = Split-Path -Parent $PSScriptRoot
    . (Join-Path $PSScriptRoot 'LocalRaidSession.ps1')
    $exe = Join-Path (Get-LocalRaidBuild $root) 'Server/HeavyPocketsServer.exe'
    if (!(Test-Path -LiteralPath $exe -PathType Leaf)) {
        $failure = 'Missing local server. Build the Windows dedicated server from the Heavy Pockets menu.'
        throw $failure
    }

    . (Join-Path $PSScriptRoot 'EditorRaidLifetime.ps1')
    $closed = [EditorRaidLifetime]::MonitorEditor()
    $failure = 'Could not prepare the encrypted local server session.'
    $security = New-LocalRaidSession
    if ($closed.IsCompleted) { exit 0 }

    $reservation = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    try { $port = $reservation.Client.LocalEndPoint.Port } finally { $reservation.Dispose() }
    $log = Join-Path ([IO.Path]::GetTempPath()) ('HeavyPockets-Editor-' + [guid]::NewGuid().ToString('N') + '.log')
    $failure = "The local raid server could not start. See $log"
    $server = Start-LocalRaidChild -Exe $exe -Arguments @('-server','-batchmode','-nographics','-port',"$port",'-logFile',$log) -Security $security -Role server
    $ready = "HEAVY POCKETS SERVER READY 127.0.0.1:$port"
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while (!$closed.IsCompleted) {
        if ($server.HasExited) { throw $failure }
        if ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -SimpleMatch $ready -Quiet)) { break }
        if ($deadline.Elapsed.TotalSeconds -ge 35) { throw $failure }
        [void]$closed.Wait(100)
    }
    if ($closed.IsCompleted) { exit 0 }

    $invitation = @{ port = $port; ca = $security.PublicCA; ticket = $security.Tickets['A'] }
    [Console]::Out.WriteLine(($invitation | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
    $security = $invitation = $null
    $failure = "The local raid server stopped unexpectedly. See $log"
    while (!$closed.IsCompleted) {
        if ($server.HasExited) { throw $failure }
        [void]$closed.Wait(200)
    }
} catch {
    [Console]::Error.WriteLine($failure)
    exit 1
} finally {
    if ($null -ne $server) {
        try {
            if (!$server.HasExited) { $server.Kill(); [void]$server.WaitForExit(3000) }
        } finally { $server.Dispose() }
    }
}
