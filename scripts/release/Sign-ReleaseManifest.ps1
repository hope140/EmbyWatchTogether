#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$KeyId,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPkcs8Base64,

    [Parameter(Mandatory = $true)]
    [string]$ManifestOutputPath,

    [Parameter(Mandatory = $true)]
    [string]$SignatureOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assetName = 'Emby.Plugins.WatchTogether.dll'
$maxAssetBytes = 50MB
$maxManifestBytes = 16KB
$maxSignatureBytes = 8KB

function Assert-SafeKeyId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if (-not [System.Text.RegularExpressions.Regex]::IsMatch(
            $Value,
            '\A[A-Za-z0-9._-]{1,64}\z',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw 'KeyId must contain only ASCII letters, digits, dot, underscore, or hyphen, and be 1-64 characters long.'
    }
}

function Get-FullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'A path must not be empty.'
    }

    try {
        return [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        throw 'A supplied path is invalid.'
    }
}

function Get-StrictTagVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ([string]::IsNullOrEmpty($Value) -or
        -not $Value.StartsWith('v', [System.StringComparison]::Ordinal)) {
        throw 'Tag must be canonical v<version> with 3 or 4 numeric parts.'
    }

    $versionText = $Value.Substring(1)
    $parts = $versionText.Split('.')
    if ($parts.Length -lt 3 -or $parts.Length -gt 4) {
        throw 'Tag must be canonical v<version> with 3 or 4 numeric parts.'
    }

    foreach ($part in $parts) {
        if ($part.Length -eq 0 -or ($part.Length -gt 1 -and $part[0] -eq '0') -or
            -not [System.Text.RegularExpressions.Regex]::IsMatch(
                $part,
                '\A[0-9]+\z',
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            throw 'Tag must use non-negative version parts without leading zeroes.'
        }
    }

    try {
        return [Version]::Parse($versionText)
    }
    catch {
        throw 'Tag contains a version outside the supported .NET version range.'
    }
}

function Test-VersionEqual {
    param(
        [Parameter(Mandatory = $true)]
        [Version]$Left,

        [Parameter(Mandatory = $true)]
        [Version]$Right
    )

    $leftBuild = if ($Left.Build -lt 0) { 0 } else { $Left.Build }
    $rightBuild = if ($Right.Build -lt 0) { 0 } else { $Right.Build }
    $leftRevision = if ($Left.Revision -lt 0) { 0 } else { $Left.Revision }
    $rightRevision = if ($Right.Revision -lt 0) { 0 } else { $Right.Revision }

    return $Left.Major -eq $Right.Major -and
        $Left.Minor -eq $Right.Minor -and
        $leftBuild -eq $rightBuild -and
        $leftRevision -eq $rightRevision
}

function ConvertTo-LowerHex {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes
    )

    $builder = [System.Text.StringBuilder]::new($Bytes.Length * 2)
    foreach ($byte in $Bytes) {
        [void]$builder.Append($byte.ToString('x2', [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return $builder.ToString()
}

function Get-StreamingSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [long]$ExpectedSize
    )

    $stream = $null
    $sha256 = $null
    $hashBytes = $null
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        if ($stream.Length -ne $ExpectedSize) {
            throw 'The DLL size changed before hashing.'
        }

        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $hashBytes = $sha256.ComputeHash($stream)
        if ($stream.Length -ne $ExpectedSize) {
            throw 'The DLL size changed while hashing.'
        }

        return ConvertTo-LowerHex -Bytes $hashBytes
    }
    finally {
        if ($null -ne $hashBytes) {
            [System.Array]::Clear($hashBytes, 0, $hashBytes.Length)
        }

        if ($null -ne $sha256) {
            $sha256.Dispose()
        }

        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Write-Bytes {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [byte[]]$Bytes
    )

    $parentDirectory = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrEmpty($parentDirectory)) {
        throw 'An output path has no parent directory.'
    }

    $null = [System.IO.Directory]::CreateDirectory($parentDirectory)
    [System.IO.File]::WriteAllBytes($Path, $Bytes)
}

Assert-SafeKeyId -Value $KeyId
$tagVersion = Get-StrictTagVersion -Value $Tag
$dllFullPath = Get-FullPath -Path $DllPath
$manifestFullPath = Get-FullPath -Path $ManifestOutputPath
$signatureFullPath = Get-FullPath -Path $SignatureOutputPath

if ($manifestFullPath.Equals($signatureFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Manifest and signature output paths must be different.'
}

if ($manifestFullPath.Equals($dllFullPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $signatureFullPath.Equals($dllFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Manifest and signature outputs must not overwrite the DLL.'
}

if (-not [System.IO.File]::Exists($dllFullPath)) {
    throw 'The release DLL does not exist.'
}

if (-not [System.String]::Equals(
        [System.IO.Path]::GetFileName($dllFullPath),
        $assetName,
        [System.StringComparison]::Ordinal)) {
    throw ('The release DLL filename must be {0}.' -f $assetName)
}

$dllFileInfo = [System.IO.FileInfo]::new($dllFullPath)
$dllSize = $dllFileInfo.Length
if ($dllSize -le 0 -or $dllSize -gt $maxAssetBytes) {
    throw 'The release DLL must be larger than zero and no larger than 50 MiB.'
}

$assemblyName = $null
try {
    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dllFullPath)
}
catch {
    throw 'The release DLL assembly metadata could not be read.'
}

if ($null -eq $assemblyName -or
    -not [System.String]::Equals(
        $assemblyName.Name,
        'Emby.Plugins.WatchTogether',
        [System.StringComparison]::Ordinal) -or
    $null -eq $assemblyName.Version) {
    throw 'The release DLL assembly identity is invalid.'
}

if (-not (Test-VersionEqual -Left $tagVersion -Right $assemblyName.Version)) {
    throw ('Tag {0} does not match the DLL AssemblyVersion.' -f $Tag)
}

$sha256Hex = Get-StreamingSha256 -Path $dllFullPath -ExpectedSize $dllSize
$manifestLines = @(
    'schema=1'
    ('keyId={0}' -f $KeyId)
    ('tag={0}' -f $Tag)
    ('version={0}' -f $Tag.Substring(1))
    ('assetName={0}' -f $assetName)
    ('size={0}' -f $dllSize)
    ('sha256={0}' -f $sha256Hex)
)
$manifestText = $manifestLines -join "`n"
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$manifestBytes = $utf8.GetBytes($manifestText)
if ($manifestBytes.Length -gt $maxManifestBytes) {
    throw 'The release manifest exceeds 16 KiB.'
}

$privateKeyBytes = $null
$signatureBytes = $null
$rsa = $null
try {
    if ([string]::IsNullOrEmpty($PrivateKeyPkcs8Base64) -or
        ($PrivateKeyPkcs8Base64.Length % 4) -ne 0 -or
        -not [System.Text.RegularExpressions.Regex]::IsMatch(
            $PrivateKeyPkcs8Base64,
            '\A[A-Za-z0-9+/]*={0,2}\z',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw 'The private key is not strict base64.'
    }

    $privateKeyBytes = [System.Convert]::FromBase64String($PrivateKeyPkcs8Base64)
    $bytesRead = 0
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportPkcs8PrivateKey($privateKeyBytes, [ref]$bytesRead)
    if ($bytesRead -ne $privateKeyBytes.Length) {
        throw 'The PKCS#8 private key did not consume the complete input.'
    }

    $signatureBytes = $rsa.SignData(
        $manifestBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
}
catch {
    throw 'PrivateKeyPkcs8Base64 must contain one complete RSA PKCS#8 private key.'
}
finally {
    if ($null -ne $privateKeyBytes) {
        [System.Array]::Clear($privateKeyBytes, 0, $privateKeyBytes.Length)
    }

    if ($null -ne $rsa) {
        $rsa.Dispose()
    }

    $PrivateKeyPkcs8Base64 = $null
}

$signatureText = [System.Convert]::ToBase64String($signatureBytes)
$signatureFileBytes = [System.Text.Encoding]::ASCII.GetBytes($signatureText)
if ($signatureFileBytes.Length -gt $maxSignatureBytes) {
    throw 'The release signature exceeds 8 KiB.'
}

try {
    Write-Bytes -Path $manifestFullPath -Bytes $manifestBytes
    Write-Bytes -Path $signatureFullPath -Bytes $signatureFileBytes
}
finally {
    if ($null -ne $signatureBytes) {
        [System.Array]::Clear($signatureBytes, 0, $signatureBytes.Length)
    }

    [System.Array]::Clear($signatureFileBytes, 0, $signatureFileBytes.Length)
}

Write-Output ('assetPath={0}' -f $dllFullPath)
Write-Output ('size={0}' -f $dllSize)
Write-Output ('sha256={0}' -f $sha256Hex)
Write-Output ('tag={0}' -f $Tag)
Write-Output ('keyId={0}' -f $KeyId)
