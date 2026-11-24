[CmdletBinding()]
param(
    [string]$UnityEditor = $env:UNITY_EDITOR,
    [ValidateSet('Both','Server','Client')][string]$Target = 'Both',
    [switch]$ForEditorPair,
    [Threading.Tasks.Task]$StopRequested,
    [switch]$Protected,
    [switch]$UsePluginConfig,
    [string]$EosPackageRoot = $env:HP_EOS_PACKAGE_ROOT
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = '6000.3.26f1'
if (!$UnityEditor) {
    $hubPath = Join-Path $env:APPDATA 'UnityHub/secondaryInstallPath.json'
    $editors = @("$env:ProgramFiles/Unity/Hub/Editor")
    if (Test-Path -LiteralPath $hubPath) { $editors += (Get-Content -Raw -LiteralPath $hubPath | ConvertFrom-Json) }
    foreach ($folder in $editors) {
        $path = Join-Path $folder "$version/Editor/Unity.exe"
        if (Test-Path -LiteralPath $path) { $UnityEditor = $path; break }
    }
}
if (!$UnityEditor -or !(Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Pass -UnityEditor with the path to Unity $version (Unity.exe)."
}
if ($UsePluginConfig -and !$Protected) { throw '-UsePluginConfig applies to -Protected builds.' }
. (Join-Path $PSScriptRoot 'EosBuild.ps1')
. (Join-Path $PSScriptRoot 'LocalDevelopmentBuild.ps1')
$writer = $writeSlot = $null
try {
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'Packages/manifest.json') | ConvertFrom-Json
$installed = $manifest.dependencies.'com.playeveryware.eos'
$buildPath = Join-Path $root 'Build'
if ($Protected) { $buildPath = Join-Path $buildPath 'Protected' }
else {
    $writer = Open-LocalRaidBuildWriter $root $StopRequested
    $selected = Get-LocalRaidBuild $root
    $buildPath = $selected
}
[void][IO.Directory]::CreateDirectory($buildPath)
$pairStamp = Join-Path $buildPath 'two-player-build.sha256'
if ($ForEditorPair) {
    if ($Protected -or $Target -ne 'Both') { throw 'The editor pair requires both local development players.' }
    # Include asset contents and their names so deleted or renamed colliders, models and
    # Resources also invalidate the pair. Skip the same local-only trees as the build.
    $files = @(foreach ($directory in 'Assets','Packages','ProjectSettings') {
        Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File |
            Where-Object { $_.FullName.Substring($root.Length) -notmatch '^\\Assets\\(StreamingAssets\\EOS|Samples\\Epic Online Services Plugin for Unity)(\\|\.meta$|$)' }
    })
    $files += Get-Item -LiteralPath $PSCommandPath,(Join-Path $PSScriptRoot 'EosBuild.ps1'),(Join-Path $PSScriptRoot 'LocalDevelopmentBuild.ps1')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $inputs = foreach ($file in $files | Sort-Object FullName) {
            $stream = [IO.File]::OpenRead($file.FullName)
            try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose() }
            $file.FullName.Substring($root.Length) + ' ' + $digest
        }
        $fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($inputs -join "`n"))))
    }
    finally { $sha.Dispose() }
    if ((Test-Path -LiteralPath $pairStamp -PathType Leaf) -and
        [IO.File]::ReadAllText($pairStamp) -eq $fingerprint -and
        (Test-Path -LiteralPath (Join-Path $buildPath 'Server/HeavyPocketsServer.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $buildPath 'Client/HeavyPockets.exe') -PathType Leaf)) {
        Write-Output 'Local two-player build is up to date.'
        return
    }
}
if (!$Protected) {
    $writeSlot = Open-LocalRaidWriteSlot $root $selected
    $buildPath = $writeSlot.Path
    Initialize-LocalRaidWriteSlot $root $buildPath $selected $Target
    $pairStamp = Join-Path $buildPath 'two-player-build.sha256'
}
$project = $root
$package = $null
if ($Protected) {
    if (!$EosPackageRoot) { $EosPackageRoot = Find-InstalledEos $root }
    if (!$EosPackageRoot) {
        if (!$installed) { throw 'Install the project-root EOS 6.3.0 tarball via Unity Package Manager.' }
        $importLog = Join-Path $buildPath 'EOS-package-import.log'
        Write-Host 'Resolving the Unity package graph.'
        $import = Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"'+$root+'"'),'-logFile',('"'+$importLog+'"')) -WindowStyle Hidden -PassThru
        $import.WaitForExit()
        $EosPackageRoot = Find-InstalledEos $root
        if ($import.ExitCode -ne 0 -or !$EosPackageRoot) { throw "Unity could not resolve the EOS package. See $importLog" }
    }
    $package = Get-EosPackage $EosPackageRoot ($Target -ne 'Server')
}
$stage = $null
if ($installed -or $Protected -or $ForEditorPair -or (Test-Path -LiteralPath (Join-Path $root 'Temp/UnityLockfile'))) {
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $stageName = 'HeavyPockets-Unity-' + [guid]::NewGuid().ToString('N')
    $stage = Join-Path $tempBase $stageName
    if (Test-Path -LiteralPath $stage) { throw 'Unity staging path already exists.' }
}
try {
    if ($stage) {
        [void][IO.Directory]::CreateDirectory($stage)
        Write-Host "Staging the Unity project at $stage"
        Initialize-RaidBuildProject $root $stage $UsePluginConfig.IsPresent $package
        $project = $stage
    }
    $targets = if ($Target -eq 'Both') { @('Server','Client') } else { @($Target) }
    foreach ($name in $targets) {
        if ($StopRequested -and $StopRequested.IsCompleted) { throw [OperationCanceledException]::new() }
        $subtarget = if ($name -eq 'Server') { 'Server' } else { 'Player' }
        $log = Join-Path $buildPath "$name-build.log"
        Write-Host "Building $name. See $log"
        $method = if ($Protected) {"Protected$name"} else {$name}
        $arguments = @('-batchmode','-nographics','-quit','-projectPath',$project,'-buildTarget','Win64','-standaloneBuildSubtarget',$subtarget,'-executeMethod',"HeavyPockets.Editor.RaidBuild.$method",'-logFile',$log)
        $arguments += @('-hpBuildOutput',$buildPath)
        $quoted = @($arguments | ForEach-Object { '"' + $_ + '"' })
        $process = Start-Process -FilePath $UnityEditor -WindowStyle Hidden -ArgumentList $quoted -PassThru
        try {
            while (!$process.WaitForExit(250)) {
                if ($StopRequested -and $StopRequested.IsCompleted) {
                    $process.Kill()
                    $process.WaitForExit()
                    throw [OperationCanceledException]::new()
                }
            }
            $exitCode = $process.ExitCode
        } finally { $process.Dispose() }
        if ($exitCode -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'HEAVY POCKETS BUILD OK' -Quiet) -or
            (Select-String -LiteralPath $log -Pattern '^Shader error in ' -Quiet)) {
            throw "$name build failed (exit $exitCode). See $log"
        }
        if ($Protected) { Complete-EosPlayer (Join-Path $buildPath $name) $name $package }
        Write-Output "$name build ready. Log: $log"
    }
    if ($ForEditorPair) { [IO.File]::WriteAllText($pairStamp, $fingerprint) }
    if (!$Protected) {
        $writeSlot.Lease.Dispose()
        Publish-LocalRaidBuild $root $buildPath
    }
} finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $actual = (Resolve-Path -LiteralPath $stage).Path
        if ((Split-Path $actual -Parent) -ne $tempBase -or (Split-Path $actual -Leaf) -ne $stageName) {
            throw 'Unexpected Unity project cleanup path.'
        }
        Remove-Item -LiteralPath $actual -Recurse -Force
    }
}
} finally {
    if ($writeSlot) { $writeSlot.Lease.Dispose() }
    if ($writer) { $writer.Dispose() }
}
