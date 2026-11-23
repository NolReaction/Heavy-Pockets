[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '../Assets/HeavyPockets/Runtime/LocalRaidCredentials.cs'
# Exercise the exact server-side admission code offline. Only the Unity transport
# is replaced. This check covers ticket consumption, not the DTLS handshake.
$shim = @'
namespace Unity.Netcode.Transports.UTP {
    internal sealed class UnityTransport {
        internal bool UseEncryption { get; set; }
        internal void SetClientSecrets(string host, string ca) { }
        internal void SetServerSecrets(string cert, string key) { }
    }
}
namespace HeavyPockets {
    public static class OfflineAdmission {
        public static Func<byte[],bool> Load() {
            return LocalRaidCredentials.Configure(new Unity.Netcode.Transports.UTP.UnityTransport(), true).TryConsume;
        }
    }
}
'@
Add-Type -TypeDefinition ("#define HEAVY_POCKETS_SERVER`n" + [IO.File]::ReadAllText($runtime) + "`n" + $shim)

[byte[]]$first = 0..31
[byte[]]$second = 32..63
[byte[]]$unknown = 64..95
$sha = [Security.Cryptography.SHA256]::Create()
$previous = @{}
$names = 'HP_RAID_DIGESTS','HP_RAID_CERT','HP_RAID_KEY'
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $digests = [Convert]::ToBase64String($sha.ComputeHash($first)) + ',' + [Convert]::ToBase64String($sha.ComputeHash($second))
    [Environment]::SetEnvironmentVariable('HP_RAID_DIGESTS', $digests)
    [Environment]::SetEnvironmentVariable('HP_RAID_CERT', 'offline-test')
    [Environment]::SetEnvironmentVariable('HP_RAID_KEY', 'offline-test')
    $consume = [HeavyPockets.OfflineAdmission]::Load()
    if ($consume.Invoke($null)) { throw 'Accepted a missing ticket' }
    if ($consume.Invoke([byte[]](0..30))) { throw 'Accepted a truncated ticket' }
    if ($consume.Invoke([byte[]](0..32))) { throw 'Accepted an overlong ticket' }
    if ($consume.Invoke($unknown)) { throw 'Accepted a foreign ticket' }
    if (!$consume.Invoke($first)) { throw 'Rejected the first invitation' }
    if ($consume.Invoke($first)) { throw 'Accepted a used invitation' }
    if (!$consume.Invoke($second)) { throw 'Rejected the independent second invitation' }
    if ($consume.Invoke($second)) { throw 'Accepted a used invitation after exhausting the list' }
    Write-Output 'PASS Local admission accepted each invitation once and rejected absent, malformed, foreign, and used tickets.'
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    $sha.Dispose()
}
