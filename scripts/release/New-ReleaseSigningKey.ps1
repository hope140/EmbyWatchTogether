#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$KeyId,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyOutputPath,

    [Parameter(Mandatory = $true)]
    [string]$PublicKeyOutputPath,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
        throw 'Output paths must not be empty.'
    }

    try {
        return [System.IO.Path]::GetFullPath($Path)
    }
    catch {
        throw 'An output path is invalid.'
    }
}

function Test-PathWithinDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Directory
    )

    $normalizedDirectory = $Directory
    while ($normalizedDirectory.Length -gt 0 -and
        ($normalizedDirectory.EndsWith([System.IO.Path]::DirectorySeparatorChar) -or
         $normalizedDirectory.EndsWith([System.IO.Path]::AltDirectorySeparatorChar))) {
        $normalizedDirectory = $normalizedDirectory.Substring(0, $normalizedDirectory.Length - 1)
    }

    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    return $Path.Equals($normalizedDirectory, $comparison) -or
        $Path.StartsWith($normalizedDirectory + [System.IO.Path]::DirectorySeparatorChar, $comparison) -or
        $Path.StartsWith($normalizedDirectory + [System.IO.Path]::AltDirectorySeparatorChar, $comparison)
}

function Write-Utf8Text {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [bool]$Overwrite
    )

    $parentDirectory = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrEmpty($parentDirectory)) {
        throw 'An output path has no parent directory.'
    }

    $null = [System.IO.Directory]::CreateDirectory($parentDirectory)
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($Text)
    $stream = $null
    try {
        $mode = if ($Overwrite) {
            [System.IO.FileMode]::Create
        }
        else {
            [System.IO.FileMode]::CreateNew
        }

        $stream = [System.IO.File]::Open(
            $Path,
            $mode,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }

        [System.Array]::Clear($bytes, 0, $bytes.Length)
    }
}

Assert-SafeKeyId -Value $KeyId

$privateKeyPath = Get-FullPath -Path $PrivateKeyOutputPath
$publicKeyPath = Get-FullPath -Path $PublicKeyOutputPath
$repositoryRoot = Get-FullPath -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..')

if ($privateKeyPath.Equals($publicKeyPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Private and public key output paths must be different.'
}

if (Test-PathWithinDirectory -Path $privateKeyPath -Directory $repositoryRoot) {
    throw 'The private key output path must be outside the repository directory.'
}

foreach ($path in @($privateKeyPath, $publicKeyPath)) {
    if ([System.IO.Directory]::Exists($path)) {
        throw 'An output path points to a directory.'
    }

    if (-not $Force.IsPresent -and [System.IO.File]::Exists($path)) {
        throw 'Refusing to overwrite an existing output file. Use -Force only when replacement is intentional.'
    }
}

$rsa = [System.Security.Cryptography.RSA]::Create(3072)
$privateKeyDer = $null
$privateKeyBase64 = $null
try {
    $privateKeyDer = $rsa.ExportPkcs8PrivateKey()
    $privateKeyBase64 = [System.Convert]::ToBase64String($privateKeyDer)
    $publicParameters = $rsa.ExportParameters($false)
    $publicKeyXml = '<RSAKeyValue><Modulus>' +
        [System.Convert]::ToBase64String($publicParameters.Modulus) +
        '</Modulus><Exponent>' +
        [System.Convert]::ToBase64String($publicParameters.Exponent) +
        '</Exponent></RSAKeyValue>'

    Write-Utf8Text -Path $privateKeyPath -Text $privateKeyBase64 -Overwrite $Force.IsPresent
    Write-Utf8Text -Path $publicKeyPath -Text $publicKeyXml -Overwrite $Force.IsPresent
}
finally {
    if ($null -ne $privateKeyDer) {
        [System.Array]::Clear($privateKeyDer, 0, $privateKeyDer.Length)
    }

    $privateKeyBase64 = $null
    $rsa.Dispose()
}

$secretNameHint = 'WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64'
Write-Output ('keyId={0}' -f $KeyId)
Write-Output ('publicKeyPath={0}' -f $publicKeyPath)
Write-Output ('privateKeyPath={0}' -f $privateKeyPath)
Write-Output ('GitHub secret name hint: {0}' -f $secretNameHint)
