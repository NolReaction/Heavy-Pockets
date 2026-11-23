# Build-time helpers for the pinned Windows EOS Unity package.
function Assert-EosFile([string]$Path,[long]$Length,[string]$Hash) {
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($file.Length -ne $Length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $Hash) {
        throw "Unexpected EOS file or unresolved Git LFS pointer: $Path. Use the complete EOS Unity package 6.3.0."
    }
}

function Find-InstalledEos([string]$Project) {
    $cache = Join-Path $Project 'Library/PackageCache'
    foreach ($entry in @(Get-ChildItem -LiteralPath $cache -Directory -Filter 'com.playeveryware.eos@*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
        $manifest = Get-Content -LiteralPath (Join-Path $entry.FullName 'package.json') -Raw | ConvertFrom-Json
        if ($manifest.name -eq 'com.playeveryware.eos' -and $manifest.version -eq '6.3.0') { return $entry.FullName }
    }
}

function Get-EosPackage([string]$Path,[bool]$IncludeBootstrap) {
    $base = (Resolve-Path -LiteralPath $Path).Path
    if ((Split-Path $base -Leaf) -eq 'EOS_SDK') { $base = Split-Path (Split-Path $base -Parent) -Parent }
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $base 'package.json') | ConvertFrom-Json
    if ($manifest.name -ne 'com.playeveryware.eos' -or $manifest.version -ne '6.3.0') { throw 'Requires com.playeveryware.eos 6.3.0 (EOS SDK 1.20.0.3).' }
    $win = Join-Path $base 'Runtime/Windows/x64'
    Assert-EosFile "$win/EOSSDK-Win64-Shipping.dll" 19921848 'f947866b3a728912c2f7a50894b239a8f0944836aed4dd0e24e270e72a6946da'
    Assert-EosFile "$win/xaudio2_9redist.dll" 847440 '0e01f400baa09694cacfd2cfbff0d721c17b56785d2d6e29b6fdd117f70ccc1e'
    if ($IncludeBootstrap) {
        $tools = Join-Path $base 'PlatformSpecificAssets~/EOS/Windows'
        Assert-EosFile "$tools/EACLauncher.exe" 3993896 '46d2552552ac95c436389a2c9f1aba6fda37ab998acf9b9ee026f3262dd70c53'
        Assert-EosFile "$tools/EasyAntiCheat/EasyAntiCheat_EOS_Setup.exe" 985896 '8b60d4e8962ee5b4b7130afabb029673e284367bc22a590293d14f089c9f46a0'
        Assert-EosFile "$tools/EasyAntiCheat/SplashScreen.png" 259853 '6e4b8683c796555f14e2543ee256131008e22131d4ca07a3c1539afd8074ffcc'
    }
    return $base
}

function Copy-BuildTree([string]$Source,[string]$Destination,[string[]]$ExtraOptions = @()) {
    robocopy.exe $Source $Destination /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP @ExtraOptions | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Could not stage $Source (robocopy $LASTEXITCODE)" }
}

function Initialize-RaidBuildProject([string]$Project,[string]$Stage,[bool]$UsePluginConfig,[string]$Package) {
    $assets = Join-Path $Project 'Assets'
    $exclude = if (!$UsePluginConfig) {
        # Imported UPM samples depend on EOSManager from the full plugin.
        $samples = Join-Path $assets 'Samples/Epic Online Services Plugin for Unity'
        @('/XD', (Join-Path $assets 'StreamingAssets/EOS'), $samples,
          '/XF', (Join-Path $assets 'StreamingAssets/EOS.meta'), "$samples.meta")
    }
    Copy-BuildTree $assets (Join-Path $Stage 'Assets') $exclude
    foreach ($name in 'Packages','ProjectSettings') {
        Copy-BuildTree (Join-Path $Project $name) (Join-Path $Stage $name)
    }
    $cache = Join-Path $Project 'Library/PackageCache'
    if (Test-Path -LiteralPath $cache) {
        $skip = if (!$UsePluginConfig) { @('/XD','com.playeveryware.eos@*') }
        Copy-BuildTree $cache (Join-Path $Stage 'Library/PackageCache') $skip
    }
    $packageName = 'com.playeveryware.eos'
    $manifestPath = Join-Path $Stage 'Packages/manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $utf8 = [Text.UTF8Encoding]::new($false)
    if ($UsePluginConfig) {
        $archive = 'com.playeveryware.eos-6.3.0.tgz'
        if ($manifest.dependencies.$packageName -ne "file:../$archive") { throw "Install the project-root $archive via Unity Package Manager before building with -UsePluginConfig." }
        Copy-Item -LiteralPath (Join-Path $Project $archive) -Destination (Join-Path $Stage $archive)
        return
    }
    # The local game and the environment-configured EAC game do not use the plugin's
    # project-specific Editor integration or its StreamingAssets deployment credentials.
    $manifest.dependencies.PSObject.Properties.Remove($packageName)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 100), $utf8)
    $lockPath = Join-Path $Stage 'Packages/packages-lock.json'
    if (Test-Path -LiteralPath $lockPath) {
        $lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
        $lock.dependencies.PSObject.Properties.Remove($packageName)
        [IO.File]::WriteAllText($lockPath, ($lock | ConvertTo-Json -Depth 100), $utf8)
    }
    if (!$Package) { return }

    # Unity resolves embedded packages directly from Packages/. Keep the official SDK
    # assembly, GUIDs and x64 plugin import settings; the runtime owns EOS initialization.
    $runtime = Join-Path $Stage 'Packages/com.heavypockets.eos-runtime'
    if (Test-Path -LiteralPath $runtime) { throw "SDK package path already exists: $runtime" }
    [void][IO.Directory]::CreateDirectory($runtime)
    $description = @{ name = 'com.heavypockets.eos-runtime'; version = '6.3.0'; displayName = 'Heavy Pockets EOS SDK runtime' }
    [IO.File]::WriteAllText((Join-Path $runtime 'package.json'), ($description | ConvertTo-Json), $utf8)
    Copy-Item -LiteralPath (Join-Path $Package 'LICENSE.md') -Destination $runtime
    Copy-BuildTree (Join-Path $Package 'Runtime/EOS_SDK') (Join-Path $runtime 'EOS_SDK')
    Copy-Item -LiteralPath (Join-Path $Package 'Runtime/EOS_SDK.meta') -Destination $runtime
    $plugins = Join-Path $runtime 'Plugins/x86_64'
    [void][IO.Directory]::CreateDirectory($plugins)
    foreach ($file in 'EOSSDK-Win64-Shipping.dll','xaudio2_9redist.dll') {
        $src = Join-Path $Package "Runtime/Windows/x64/$file"
        Copy-Item -LiteralPath $src,"$src.meta" -Destination $plugins
    }
}

function Complete-EosPlayer([string]$PlayerPath,[string]$Role,[string]$Package) {
    $data = if ($Role -eq 'Server') {'HeavyPocketsServer_Data'} else {'HeavyPockets_Data'}
    $native = Join-Path $PlayerPath "$data/Plugins/x86_64/EOSSDK-Win64-Shipping.dll"
    Assert-EosFile $native 19921848 'f947866b3a728912c2f7a50894b239a8f0944836aed4dd0e24e270e72a6946da'
    $notices = Join-Path $PlayerPath 'Licenses/EOS'
    [void][IO.Directory]::CreateDirectory($notices)
    Copy-Item -LiteralPath (Join-Path $Package 'LICENSE.md') -Destination $notices
    foreach ($entry in @{'Managed'='Runtime/EOS_SDK'; 'Native'='Runtime/Windows/x64'}.GetEnumerator()) {
        $dest = Join-Path $notices $entry.Key
        [void][IO.Directory]::CreateDirectory($dest)
        foreach ($name in 'license.txt','ThirdPartySoftwareNotice.txt') {
            Copy-Item -LiteralPath (Join-Path $Package ($entry.Value + '/' + $name)) -Destination $dest
        }
    }
    if ($Role -ne 'Client') { return }
    $win = Join-Path $Package 'PlatformSpecificAssets~/EOS/Windows'
    Copy-Item -LiteralPath "$win/EACLauncher.exe" -Destination $PlayerPath
    $eac = Join-Path $PlayerPath 'EasyAntiCheat'
    [void][IO.Directory]::CreateDirectory($eac)
    foreach ($file in 'EasyAntiCheat_EOS_Setup.exe','SplashScreen.png') {
        Copy-Item -LiteralPath (Join-Path "$win/EasyAntiCheat" $file) -Destination $eac
    }
    foreach ($folder in 'Licenses','Localization') {
        Copy-BuildTree (Join-Path "$win/EasyAntiCheat" $folder) (Join-Path $eac $folder)
    }
    $settings = Get-Content -Raw -LiteralPath "$win/EasyAntiCheat/Settings.json" | ConvertFrom-Json
    $settings.title = 'Heavy Pockets'
    $settings.executable = 'HeavyPockets.exe'
    [IO.File]::WriteAllText((Join-Path $eac 'Settings.template.json'), ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
