#requires -Version 7.0

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw ('{0} Expected: {1}; actual: {2}.' -f $Message, $Expected, $Actual)
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $threw = $false
    try {
        & $Action
    }
    catch {
        $threw = $true
    }

    Assert-True -Condition $threw -Message $Message
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = $null
    $sha256 = $null
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $bytes = $sha256.ComputeHash($stream)
        return ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
    }
    finally {
        if ($null -ne $sha256) {
            $sha256.Dispose()
        }

        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Get-ManifestValues {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $values = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($line in $Text.Split("`n")) {
        $separator = $line.IndexOf('=')
        Assert-True -Condition ($separator -gt 0) -Message 'Manifest contains an invalid field.'
        $values.Add($line.Substring(0, $separator), $line.Substring($separator + 1))
    }

    return $values
}

function Import-PublicKeyXml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$XmlText
    )

    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $document.LoadXml($XmlText)
    Assert-Equal -Expected 'RSAKeyValue' -Actual $document.DocumentElement.Name -Message 'Public key root is invalid.'

    $modulusNode = $document.DocumentElement.SelectSingleNode('./Modulus')
    $exponentNode = $document.DocumentElement.SelectSingleNode('./Exponent')
    Assert-True -Condition ($null -ne $modulusNode -and $null -ne $exponentNode) -Message 'Public key components are missing.'

    $parameters = [System.Security.Cryptography.RSAParameters]::new()
    $parameters.Modulus = [System.Convert]::FromBase64String($modulusNode.InnerText)
    $parameters.Exponent = [System.Convert]::FromBase64String($exponentNode.InnerText)
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportParameters($parameters)
    return $rsa
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$newKeyScript = Join-Path -Path $repositoryRoot -ChildPath 'scripts\release\New-ReleaseSigningKey.ps1'
$signManifestScript = Join-Path -Path $repositoryRoot -ChildPath 'scripts\release\Sign-ReleaseManifest.ps1'
$dllPath = [System.IO.Path]::GetFullPath((Join-Path -Path $repositoryRoot -ChildPath 'src\EmbyWatchTogether\bin\Release\netstandard2.0\Emby.Plugins.WatchTogether.dll'))

Assert-True -Condition ([System.IO.File]::Exists($newKeyScript)) -Message 'Signing key script is missing.'
Assert-True -Condition ([System.IO.File]::Exists($signManifestScript)) -Message 'Manifest signing script is missing.'
if (-not [System.IO.File]::Exists($dllPath)) {
    throw 'Release plugin DLL is missing. Run dotnet build src\EmbyWatchTogether.sln -c Release --nologo first; this test does not build or modify the repository.'
}

$tempDirectory = $null
$privateKeyPath = $null
$publicKeyPath = $null
$manifestPath = $null
$signaturePath = $null
$wrongDllPath = $null
$repositoryPrivateKeyPath = $null
$verificationKey = $null
$runtimeKey = $null
$privateKeyBytes = $null

try {
    $tempDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath (
        'emby-watch-together-release-signing-' + [Guid]::NewGuid().ToString('N'))
    $null = [System.IO.Directory]::CreateDirectory($tempDirectory)
    $privateKeyPath = Join-Path -Path $tempDirectory -ChildPath 'release-private-key.pkcs8.b64'
    $publicKeyPath = Join-Path -Path $tempDirectory -ChildPath 'release-public-key.xml'
    $manifestPath = Join-Path -Path $tempDirectory -ChildPath 'EmbyWatchTogether.release.manifest'
    $signaturePath = Join-Path -Path $tempDirectory -ChildPath 'EmbyWatchTogether.release.manifest.sig'
    $wrongDllPath = Join-Path -Path $tempDirectory -ChildPath 'wrong.dll'
    $repositoryPrivateKeyPath = Join-Path -Path $repositoryRoot -ChildPath (
        '.release-signing-test-private-' + [Guid]::NewGuid().ToString('N') + '.txt')

    $keySummary = & $newKeyScript `
        -KeyId 'release-test-1' `
        -PrivateKeyOutputPath $privateKeyPath `
        -PublicKeyOutputPath $publicKeyPath | Out-String
    $privateKeyText = [System.IO.File]::ReadAllText($privateKeyPath)
    $publicKeyText = [System.IO.File]::ReadAllText($publicKeyPath)
    Assert-True -Condition ($keySummary.Contains('keyId=release-test-1')) -Message 'Key summary omitted keyId.'
    Assert-True -Condition ($keySummary.Contains($privateKeyPath)) -Message 'Key summary omitted private key path.'
    Assert-True -Condition ($keySummary.Contains($publicKeyPath)) -Message 'Key summary omitted public key path.'
    Assert-True -Condition ($keySummary.Contains('secret name hint')) -Message 'Key summary omitted the secret name hint.'
    Assert-True -Condition (-not $keySummary.Contains($privateKeyText)) -Message 'Key summary exposed private key content.'

    $privateKeyBytes = [System.Convert]::FromBase64String($privateKeyText)
    $bytesRead = 0
    $runtimeKey = [System.Security.Cryptography.RSA]::Create()
    $runtimeKey.ImportPkcs8PrivateKey($privateKeyBytes, [ref]$bytesRead)
    Assert-Equal -Expected $privateKeyBytes.Length -Actual $bytesRead -Message 'PKCS#8 key was not fully consumed.'
    Assert-True -Condition ($runtimeKey.KeySize -ge 3072) -Message 'Generated RSA key is weaker than 3072 bits.'
    $verificationKey = Import-PublicKeyXml -XmlText $publicKeyText

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version
    $tag = 'v' + $assemblyVersion.ToString()
    $signSummary = & $signManifestScript `
        -DllPath $dllPath `
        -Tag $tag `
        -KeyId 'release-test-1' `
        -PrivateKeyPkcs8Base64 $privateKeyText `
        -ManifestOutputPath $manifestPath `
        -SignatureOutputPath $signaturePath | Out-String
    Assert-True -Condition ($signSummary.Contains('tag=' + $tag)) -Message 'Sign summary omitted tag.'
    Assert-True -Condition ($signSummary.Contains('keyId=release-test-1')) -Message 'Sign summary omitted keyId.'
    Assert-True -Condition ($signSummary.Contains('assetPath=' + $dllPath)) -Message 'Sign summary omitted asset path.'

    $manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
    $signatureFileBytes = [System.IO.File]::ReadAllBytes($signaturePath)
    Assert-True -Condition ($manifestBytes.Length -le 16KB) -Message 'Manifest exceeds 16 KiB.'
    Assert-True -Condition ($signatureFileBytes.Length -le 8KB) -Message 'Signature exceeds 8 KiB.'
    Assert-True -Condition ($manifestBytes.Length -gt 0) -Message 'Manifest is empty.'
    Assert-True -Condition (-not ($manifestBytes.Length -ge 3 -and
        $manifestBytes[0] -eq 0xEF -and $manifestBytes[1] -eq 0xBB -and $manifestBytes[2] -eq 0xBF)) -Message 'Manifest contains a UTF-8 BOM.'
    Assert-True -Condition (-not ($manifestBytes -contains 0x0D)) -Message 'Manifest contains CR line endings.'
    Assert-True -Condition ($manifestBytes[$manifestBytes.Length - 1] -ne 0x0A) -Message 'Manifest has a trailing newline.'

    $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $manifestText = $strictUtf8.GetString($manifestBytes)
    $expectedLines = @(
        'schema=1'
        'keyId=release-test-1'
        ('tag={0}' -f $tag)
        ('version={0}' -f $tag.Substring(1))
        'assetName=Emby.Plugins.WatchTogether.dll'
        ('size={0}' -f ([System.IO.FileInfo]::new($dllPath).Length))
        ('sha256={0}' -f (Get-FileSha256 -Path $dllPath))
    )
    Assert-Equal -Expected ($expectedLines -join "`n") -Actual $manifestText -Message 'Manifest is not canonical.'

    $manifestValues = Get-ManifestValues -Text $manifestText
    Assert-Equal -Expected '1' -Actual $manifestValues['schema'] -Message 'Manifest schema is invalid.'
    Assert-Equal -Expected 'release-test-1' -Actual $manifestValues['keyId'] -Message 'Manifest keyId is invalid.'
    Assert-Equal -Expected $tag -Actual $manifestValues['tag'] -Message 'Manifest tag is invalid.'
    Assert-Equal -Expected $tag.Substring(1) -Actual $manifestValues['version'] -Message 'Manifest version is invalid.'
    Assert-Equal -Expected 'Emby.Plugins.WatchTogether.dll' -Actual $manifestValues['assetName'] -Message 'Manifest assetName is invalid.'
    Assert-Equal -Expected ([System.IO.FileInfo]::new($dllPath).Length) -Actual ([long]$manifestValues['size']) -Message 'Manifest size is invalid.'
    Assert-Equal -Expected (Get-FileSha256 -Path $dllPath) -Actual $manifestValues['sha256'] -Message 'Manifest hash is invalid.'

    $signatureText = [System.Text.Encoding]::ASCII.GetString($signatureFileBytes)
    $asciiRoundTrip = [System.Text.Encoding]::ASCII.GetBytes($signatureText)
    Assert-True -Condition (
        [System.Convert]::ToBase64String($signatureFileBytes) -eq
        [System.Convert]::ToBase64String($asciiRoundTrip)) -Message 'Signature encoding is not ASCII.'
    Assert-True -Condition ($signatureText.Length % 4 -eq 0) -Message 'Signature base64 length is invalid.'
    Assert-True -Condition ([System.Text.RegularExpressions.Regex]::IsMatch(
        $signatureText,
        '\A[A-Za-z0-9+/]*={0,2}\z',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) -Message 'Signature is not strict base64.'
    $signatureBytes = [System.Convert]::FromBase64String($signatureText)
    Assert-True -Condition ($verificationKey.VerifyData(
        $manifestBytes,
        $signatureBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) -Message 'RSA signature verification failed.'
    Assert-True -Condition ($runtimeKey.VerifyData(
        $manifestBytes,
        $signatureBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)) -Message 'Runtime RSA signature verification failed.'

    Assert-Throws -Action {
        & $newKeyScript `
            -KeyId 'release-test-1' `
            -PrivateKeyOutputPath $privateKeyPath `
            -PublicKeyOutputPath $publicKeyPath
    } -Message 'Key generation did not refuse to overwrite existing files.'

    Assert-Throws -Action {
        & $newKeyScript `
            -KeyId 'release-test-1' `
            -PrivateKeyOutputPath $repositoryPrivateKeyPath `
            -PublicKeyOutputPath (Join-Path $tempDirectory 'rejected-public.xml')
    } -Message 'Key generation accepted a private path inside the repository.'
    Assert-True -Condition (-not [System.IO.File]::Exists($repositoryPrivateKeyPath)) -Message 'Rejected repository private key path left a file behind.'

    Assert-Throws -Action {
        & $signManifestScript `
            -DllPath $dllPath `
            -Tag ('v' + $assemblyVersion.Major + '.01.' + $assemblyVersion.Build) `
            -KeyId 'release-test-1' `
            -PrivateKeyPkcs8Base64 $privateKeyText `
            -ManifestOutputPath (Join-Path $tempDirectory 'invalid-tag.manifest') `
            -SignatureOutputPath (Join-Path $tempDirectory 'invalid-tag.sig')
    } -Message 'Signer accepted a non-canonical tag.'

    Assert-Throws -Action {
        & $signManifestScript `
            -DllPath $dllPath `
            -Tag ('v' + $assemblyVersion.Major + '.' + $assemblyVersion.Minor + '.' + $assemblyVersion.Build + '.8') `
            -KeyId 'release-test-1' `
            -PrivateKeyPkcs8Base64 $privateKeyText `
            -ManifestOutputPath (Join-Path $tempDirectory 'mismatch.manifest') `
            -SignatureOutputPath (Join-Path $tempDirectory 'mismatch.sig')
    } -Message 'Signer accepted a tag that does not match AssemblyVersion.'

    [System.IO.File]::Copy($dllPath, $wrongDllPath)
    Assert-Throws -Action {
        & $signManifestScript `
            -DllPath $wrongDllPath `
            -Tag $tag `
            -KeyId 'release-test-1' `
            -PrivateKeyPkcs8Base64 $privateKeyText `
            -ManifestOutputPath (Join-Path $tempDirectory 'wrong-name.manifest') `
            -SignatureOutputPath (Join-Path $tempDirectory 'wrong-name.sig')
    } -Message 'Signer accepted a DLL with the wrong filename.'

    Assert-Throws -Action {
        & $signManifestScript `
            -DllPath $dllPath `
            -Tag $tag `
            -KeyId 'release test' `
            -PrivateKeyPkcs8Base64 $privateKeyText `
            -ManifestOutputPath (Join-Path $tempDirectory 'invalid-key.manifest') `
            -SignatureOutputPath (Join-Path $tempDirectory 'invalid-key.sig')
    } -Message 'Signer accepted an invalid keyId.'

    Write-Output 'release signing tests passed'
}
finally {
    if ($null -ne $verificationKey) {
        $verificationKey.Dispose()
    }

    if ($null -ne $runtimeKey) {
        $runtimeKey.Dispose()
    }

    if ($null -ne $privateKeyBytes) {
        [System.Array]::Clear($privateKeyBytes, 0, $privateKeyBytes.Length)
    }

    if ($null -ne $repositoryPrivateKeyPath -and [System.IO.File]::Exists($repositoryPrivateKeyPath)) {
        [System.IO.File]::Delete($repositoryPrivateKeyPath)
    }

    if ($null -ne $tempDirectory -and [System.IO.Directory]::Exists($tempDirectory)) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}
