function Get-LocalRaidBuild([string]$root) {
    $build = Join-Path $root 'Build/Development'
    if (Test-Path -LiteralPath $build -PathType Container) { return $build }
    return (Join-Path $root 'Build')
}

# Ephemeral local issuer. No certificate-store installation, network service or persistent key.
function New-LocalRaidSession {
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $sha = [Security.Cryptography.SHA256]::Create()
    $caKey = $serverKey = $authority = $leaf = $null
    try {
        $keyOptions = [Security.Cryptography.CngKeyCreationParameters]::new()
        $keyOptions.ExportPolicy = [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
        # Unity Transport's native TLS server expects the RSA/PKCS#1 profile.
        $alg = [Security.Cryptography.CngAlgorithm]::Rsa
        $rsaSize = [Security.Cryptography.CngProperty]::new('Length',[BitConverter]::GetBytes([int]2048),0)
        $keyOptions.Parameters.Add($rsaSize)
        $caKey = [Security.Cryptography.RSACng]::new(2048)
        $serverKey = [Security.Cryptography.RSACng]::new([Security.Cryptography.CngKey]::Create($alg, [NullString]::Value, $keyOptions))
        $hash = [Security.Cryptography.HashAlgorithmName]::SHA256
        $padding = [Security.Cryptography.RSASignaturePadding]::Pkcs1
        $caReq = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Heavy Pockets Local Raid', $caKey, $hash, $padding)
        $caReq.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
        $caReq.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign,$true))
        $after = [DateTimeOffset]::UtcNow.AddMinutes(-2)
        $until = $after.AddHours(12)
        $signer = [Security.Cryptography.X509Certificates.X509SignatureGenerator]::CreateForRSA($caKey, $padding)
        $issuerSerial = [byte[]]::new(16)
        $rng.GetBytes($issuerSerial)
        $authority = $caReq.Create($caReq.SubjectName, $signer, $after, $until, $issuerSerial)
        $hostName = 'heavy-pockets.local'
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$hostName", $serverKey, $hash, $padding)
        $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName($hostName)
        $request.CertificateExtensions.Add($san.Build())
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(([Security.Cryptography.X509Certificates.X509KeyUsageFlags]'DigitalSignature,KeyEncipherment'),$true))
        $purpose = [Security.Cryptography.OidCollection]::new()
        [void]$purpose.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($purpose,$true))
        $serial = [byte[]]::new(16)
        $rng.GetBytes($serial)
        $leaf = $request.Create($authority.SubjectName, $signer, $after.AddSeconds(1), $until.AddSeconds(-1), $serial)
        function Format-CredentialPem([string]$kind, [byte[]]$bytes) {
            $data = [Convert]::ToBase64String($bytes, [Base64FormattingOptions]::InsertLineBreaks)
            return "-----BEGIN $kind-----`n$data`n-----END $kind-----`n"
        }
        $tickets = @{}
        $digests = foreach ($role in 'A','B') {
            $bytes = [byte[]]::new(32)
            $rng.GetBytes($bytes)
            $tickets[$role] = [Convert]::ToBase64String($bytes)
            [Convert]::ToBase64String($sha.ComputeHash($bytes))
            [Array]::Clear($bytes,0,$bytes.Length)
        }
        # Windows PowerShell 5.1 can export an unencrypted PKCS#8 envelope.
        # Its privateKey OCTET STRING contains the PKCS#1 key used by Unity.
        function Read-DerLength([IO.BinaryReader]$reader,[int]$tag) {
            if ($reader.ReadByte() -ne $tag) { throw 'Unexpected RSA key format.' }
            [int]$length = $reader.ReadByte()
            if ($length -lt 128) { return $length }
            $count = $length -band 127
            if ($count -lt 1 -or $count -gt 3) { throw 'Invalid key length.' }
            $length = 0
            for ($i=0; $i -lt $count; $i++) { $length = ($length -shl 8) + $reader.ReadByte() }
            return $length
        }
        $pkcs8 = $serverKey.Key.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
        $reader = [IO.BinaryReader]::new([IO.MemoryStream]::new($pkcs8))
        $pkcs1 = $null
        try {
            [void](Read-DerLength $reader 48)
            [void]$reader.ReadBytes((Read-DerLength $reader 2))
            [void]$reader.ReadBytes((Read-DerLength $reader 48))
            $pkcs1 = $reader.ReadBytes((Read-DerLength $reader 4))
            return @{
                PublicCA = Format-CredentialPem 'CERTIFICATE' $authority.RawData
                ServerCert = Format-CredentialPem 'CERTIFICATE' $leaf.RawData
                ServerKey = Format-CredentialPem 'RSA PRIVATE KEY' $pkcs1
                Tickets = $tickets
                Digests = $digests -join ','
            }
        } finally {
            $reader.Dispose()
            if ($pkcs1) { [Array]::Clear($pkcs1,0,$pkcs1.Length) }
            [Array]::Clear($pkcs8,0,$pkcs8.Length)
        }
    } finally {
        foreach ($object in $leaf,$authority,$serverKey,$caKey,$sha,$rng) {
            if ($null -ne $object) { $object.Dispose() }
        }
    }
}

# Build a per-process environment. Clients receive only the public CA and their own ticket.
function Start-LocalRaidChild {
    param([string]$Exe,[string[]]$Arguments,[hashtable]$Security,
          [ValidateSet('server','A','B')][string]$Role,[switch]$Visible)
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Exe
    $info.UseShellExecute = $false
    $info.CreateNoWindow = !$Visible
    $info.WindowStyle = if ($Visible) { 'Normal' } else { 'Hidden' }
    $info.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    foreach ($name in 'HP_RAID_CA','HP_RAID_TICKET','HP_RAID_CERT','HP_RAID_KEY','HP_RAID_DIGESTS') {
        $info.EnvironmentVariables.Remove($name)
    }
    if ($Role -eq 'server') {
        $info.EnvironmentVariables['HP_RAID_CERT'] = $Security.ServerCert
        $info.EnvironmentVariables['HP_RAID_KEY'] = $Security.ServerKey
        $info.EnvironmentVariables['HP_RAID_DIGESTS'] = $Security.Digests
    } else {
        $info.EnvironmentVariables['HP_RAID_CA'] = $Security.PublicCA
        $info.EnvironmentVariables['HP_RAID_TICKET'] = $Security.Tickets[$Role]
    }
    return [Diagnostics.Process]::Start($info)
}
