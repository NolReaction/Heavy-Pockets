# Windows development players are published as pairs. Existing sessions retain their chosen slot.
function Get-LocalRaidBuild([string]$root) {
    $build = Join-Path $root 'Build'
    $pointer = Join-Path $build 'development-active.txt'
    if ([IO.File]::Exists($pointer)) {
        $stream = [IO.File]::Open($pointer, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader = [IO.StreamReader]::new($stream)
        try { $slot = $reader.ReadToEnd().Trim() } finally { $reader.Dispose() }
        if ($slot -notin 'Development-A','Development-B','Development-C') { throw 'Invalid local build selection.' }
        return (Join-Path $build $slot)
    }
    $legacy = Join-Path $build 'Development'
    if ([IO.Directory]::Exists($legacy)) { return $legacy }
    return $build
}

function Open-LocalRaidBuild([string]$root) {
    while ($true) {
        $path = Get-LocalRaidBuild $root
        $lease = Join-Path $path '.local-raid-slot'
        if (![IO.File]::Exists($lease)) { return @{ Path = $path; Lease = $null } } # Legacy directory is read-only.
        try { $handle = [IO.File]::Open($lease, 'Open', 'Read', 'Read') }
        catch [IO.IOException] { Start-Sleep -Milliseconds 50; continue }
        if ($path -eq (Get-LocalRaidBuild $root)) { return @{ Path = $path; Lease = $handle } }
        $handle.Dispose()
    }
}

function Open-LocalRaidBuildWriter([string]$root, [Threading.Tasks.Task]$StopRequested) {
    $build = Join-Path $root 'Build'
    [void][IO.Directory]::CreateDirectory($build)
    while ($true) {
        if ($StopRequested -and $StopRequested.IsCompleted) { throw [OperationCanceledException]::new() }
        try { return [IO.File]::Open((Join-Path $build '.development-build.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
        catch [IO.IOException] { Start-Sleep -Milliseconds 200 }
    }
}

function Open-LocalRaidWriteSlot([string]$root, [string]$selected) {
    $build = Join-Path $root 'Build'
    foreach ($slot in 'Development-A','Development-B','Development-C') {
        $path = Join-Path $build $slot
        if ($path -eq $selected) { continue }
        $marker = Join-Path $path '.local-raid-slot'
        if ([IO.Directory]::Exists($path) -and (![IO.File]::Exists($marker) -or
            ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint))) { continue }
        [void][IO.Directory]::CreateDirectory($path)
        try { $handle = [IO.File]::Open($marker, 'OpenOrCreate', 'ReadWrite', 'None') }
        catch [IO.IOException] { continue }
        # A player launched directly from Explorer has no helper holding a lease.
        try {
            foreach ($exe in 'Server/HeavyPocketsServer.exe','Client/HeavyPockets.exe') {
                $file = Join-Path $path $exe
                if ([IO.File]::Exists($file)) { [IO.File]::Open($file, 'Open', 'ReadWrite', 'None').Dispose() }
            }
        } catch { $handle.Dispose(); continue }
        return @{ Path = $path; Lease = $handle }
    }
    throw 'All local development build slots are in use. The current published players remain available.'
}

function Initialize-LocalRaidWriteSlot([string]$root, [string]$path, [string]$selected, [string]$target) {
    $parent = [IO.Path]::GetFullPath((Join-Path $root 'Build'))
    $actual = (Resolve-Path -LiteralPath $path).Path
    if ((Split-Path $actual -Parent) -ne $parent -or (Split-Path $actual -Leaf) -notin 'Development-A','Development-B','Development-C' -or
        ![IO.File]::Exists((Join-Path $actual '.local-raid-slot')) -or $actual -eq $selected) { throw 'Unexpected build slot.' }
    # Prepare only generated player directories in a managed, exclusively leased slot.
    foreach ($role in 'Server','Client') {
        $dest = Join-Path $actual $role
        if (Test-Path -LiteralPath $dest) {
            $resolved = (Resolve-Path -LiteralPath $dest).Path
            if ((Split-Path $resolved -Parent) -ne $actual -or
                ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Unexpected local player directory.'
            }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
        $previous = Join-Path $selected $role
        if ($target -ne 'Both' -and $target -ne $role -and [IO.Directory]::Exists($previous)) {
            Copy-Item -LiteralPath $previous -Destination $dest -Recurse
        }
    }
    $stamp = Join-Path $actual 'two-player-build.sha256'
    if ([IO.File]::Exists($stamp)) { [IO.File]::Delete($stamp) }
}

function Publish-LocalRaidBuild([string]$root, [string]$slotPath) {
    $activeFile = Join-Path $root 'Build/development-active.txt'
    $pending = $activeFile + '.new'
    [IO.File]::WriteAllText($pending, (Split-Path $slotPath -Leaf))
    if ([IO.File]::Exists($activeFile)) { [IO.File]::Replace($pending, $activeFile, [NullString]::Value) }
    else { [IO.File]::Move($pending, $activeFile) }
}
