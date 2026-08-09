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
        throw $Message
    }
}

function Assert-Matches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

function Assert-NotMatches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Text -match $Pattern) {
        throw $Message
    }
}

function Get-RunBlocks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $blocks = [System.Collections.Generic.List[string]]::new()
    $currentLines = $null
    $runIndent = -1

    foreach ($line in ($Text -split "`n")) {
        $leadingWhitespace = $line.Length - $line.TrimStart().Length
        if ($null -eq $currentLines) {
            if ($line -match '^(?<indent>\s*)run:\s*\|\s*$') {
                $runIndent = $Matches['indent'].Length
                $currentLines = [System.Collections.Generic.List[string]]::new()
            }

            continue
        }

        if ($line.Trim().Length -eq 0 -or $leadingWhitespace -gt $runIndent) {
            [void]$currentLines.Add($line)
            continue
        }

        [void]$blocks.Add(($currentLines -join "`n"))
        $currentLines = $null
        $runIndent = -1
    }

    if ($null -ne $currentLines) {
        [void]$blocks.Add(($currentLines -join "`n"))
    }

    return $blocks.ToArray()
}

function Assert-InputShape {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InputsText,

        [Parameter(Mandatory = $true)]
        [string]$InputName
    )

    $fieldPattern = '(?ms)^\s{6}' + [System.Text.RegularExpressions.Regex]::Escape($InputName) + ':\s*\n(?<field>.*?)(?=^\s{6}[A-Za-z_][A-Za-z0-9_-]*:\s*$|\z)'
    $fieldMatch = [System.Text.RegularExpressions.Regex]::Match($InputsText, $fieldPattern)
    Assert-True -Condition $fieldMatch.Success -Message ('The workflow_dispatch input is missing: {0}.' -f $InputName)

    $fieldText = $fieldMatch.Groups['field'].Value
    Assert-Matches -Text $fieldText -Pattern '(?m)^\s{8}required:\s*true\s*$' -Message ('The {0} input must be required.' -f $InputName)
    Assert-Matches -Text $fieldText -Pattern '(?m)^\s{8}type:\s*string\s*$' -Message ('The {0} input must be a string.' -f $InputName)
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '..'))
$workflowPath = Join-Path -Path $repositoryRoot -ChildPath '.github\workflows\release.yml'
Assert-True -Condition ([System.IO.File]::Exists($workflowPath)) -Message 'The signed release workflow is missing.'

$workflowBytes = [System.IO.File]::ReadAllBytes($workflowPath)
Assert-True -Condition ($workflowBytes.Length -gt 0) -Message 'The signed release workflow is empty.'
Assert-True -Condition (-not ($workflowBytes.Length -ge 3 -and
        $workflowBytes[0] -eq 0xEF -and
        $workflowBytes[1] -eq 0xBB -and
        $workflowBytes[2] -eq 0xBF)) -Message 'The signed release workflow must not contain a UTF-8 BOM.'

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
try {
    $workflowText = $strictUtf8.GetString($workflowBytes)
}
catch {
    throw 'The signed release workflow is not strict UTF-8.'
}

$workflowText = $workflowText.Replace("`r`n", "`n").Replace("`r", "`n")
$onMatch = [System.Text.RegularExpressions.Regex]::Match(
    $workflowText,
    '(?ms)^on:\s*\n(?<on>.*?)(?=^permissions:\s*$|^jobs:\s*$)')
Assert-True -Condition $onMatch.Success -Message 'The workflow on block is missing.'
$onText = $onMatch.Groups['on'].Value
Assert-Matches -Text $onText -Pattern '(?m)^\s{2}workflow_dispatch:\s*$' -Message 'The workflow must use workflow_dispatch.'
Assert-NotMatches -Text $onText -Pattern '(?m)^\s{2}(?!workflow_dispatch:)[A-Za-z_][A-Za-z0-9_-]*:\s*$' -Message 'The workflow contains an unapproved trigger.'
Assert-NotMatches -Text $onText -Pattern '(?m)^\s{2}(?:push|pull_request|schedule):\s*$' -Message 'The workflow contains a forbidden automatic trigger.'

$inputsMatch = [System.Text.RegularExpressions.Regex]::Match(
    $onText,
    '(?ms)^\s{4}inputs:\s*\n(?<inputs>.*)')
Assert-True -Condition $inputsMatch.Success -Message 'The workflow_dispatch inputs block is missing.'
$inputsText = $inputsMatch.Groups['inputs'].Value
Assert-InputShape -InputsText $inputsText -InputName 'tag'
Assert-InputShape -InputsText $inputsText -InputName 'key_id'

Assert-Matches -Text $workflowText -Pattern '(?m)^permissions:\s*$' -Message 'Top-level contents permissions are missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{2}contents:\s*write\s*$' -Message 'Top-level contents write permission is missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{4}environment:\s*release\s*$' -Message 'The release GitHub Environment is missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{4}permissions:\s*$' -Message 'Job-level permissions are missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{6}contents:\s*write\s*$' -Message 'Job-level contents write permission is missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{4}runs-on:\s*windows-latest\s*$' -Message 'The workflow must run on windows-latest.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{6}RELEASE_TAG:\s*\$\{\{\s*inputs\.tag\s*\}\}\s*$' -Message 'The tag input must be passed to PowerShell through an environment variable.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{6}RELEASE_KEY_ID:\s*\$\{\{\s*inputs\.key_id\s*\}\}\s*$' -Message 'The key_id input must be passed to PowerShell through an environment variable.'

$checkoutActionSha = 'fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09'
$setupDotnetActionSha = '26b0ec14cb23fa6904739307f278c14f94c95bf1'
$actionReferenceMatches = @([System.Text.RegularExpressions.Regex]::Matches(
        $workflowText,
        '(?m)^[ \t]*uses:[ \t]*actions/(?<action>checkout|setup-dotnet)@(?<reference>[^\s#]+)'))
Assert-Equal -Expected 2 -Actual $actionReferenceMatches.Count -Message 'The workflow must contain exactly one checkout and one setup-dotnet action reference.'
foreach ($actionReferenceMatch in $actionReferenceMatches) {
    $actionName = $actionReferenceMatch.Groups['action'].Value
    $actionReference = $actionReferenceMatch.Groups['reference'].Value
    Assert-Matches -Text $actionReference -Pattern '\A[0-9a-fA-F]{40}\z' `
        -Message ('The {0} action must use a complete 40-character SHA reference.' -f $actionName)
}
Assert-NotMatches -Text $workflowText `
    -Pattern '(?m)^[ \t]*uses:[ \t]*actions/(?:checkout|setup-dotnet)@v(?:4|5)(?:[ \t]+#.*)?[ \t]*$' `
    -Message 'The release workflow must not use a movable v4 or v5 action tag.'
Assert-Matches -Text $workflowText `
    -Pattern ('(?m)^[ \t]*uses:[ \t]*actions/checkout@{0}[ \t]+#[ \t]*v5[ \t]*$' -f [System.Text.RegularExpressions.Regex]::Escape($checkoutActionSha)) `
    -Message 'The workflow checkout action is not pinned to the approved v5 SHA.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}ref:\s*\$\{\{\s*inputs\.tag\s*\}\}\s*$' -Message 'Checkout must use the requested tag as ref.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}fetch-depth:\s*0\s*$' -Message 'Checkout must use fetch-depth 0.'
Assert-Matches -Text $workflowText `
    -Pattern ('(?m)^[ \t]*uses:[ \t]*actions/setup-dotnet@{0}[ \t]+#[ \t]*v5[ \t]*$' -f [System.Text.RegularExpressions.Regex]::Escape($setupDotnetActionSha)) `
    -Message 'The .NET setup action is not pinned to the approved v5 SHA.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}dotnet-version:\s*[\x27\"]?10\.0\.x[\x27\"]?\s*$' -Message 'The workflow must use the .NET 10 SDK.'

$runBlocks = @(Get-RunBlocks -Text $workflowText)
Assert-True -Condition ($runBlocks.Count -ge 9) -Message 'The workflow is missing required PowerShell run steps.'
$runText = $runBlocks -join "`n"

Assert-Matches -Text $runText -Pattern 'RELEASE_TAG|RELEASE_KEY_ID' -Message 'PowerShell steps do not consume the environment inputs.'
Assert-Matches -Text $runText -Pattern 'WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64' -Message 'The signing secret environment variable is not checked or consumed.'
Assert-Matches -Text $runText -Pattern 'ReleaseTrustStore\.cs' -Message 'The workflow does not inspect ReleaseTrustStore.cs.'
Assert-Matches -Text $runText -Pattern 'RSAKeyValue' -Message 'The workflow does not require an RSAKeyValue public key.'
Assert-Matches -Text $runText -Pattern 'trustStoreText|keyIndex|keyToken' -Message 'The workflow does not tie the requested key_id to the trust store source.'
Assert-Matches -Text $runText -Pattern 'throw' -Message 'The trust-store validation is not fail closed.'
Assert-Matches -Text $runText -Pattern 'Regex\]::Escape\(\$keyId\)' -Message 'The trust-store mapping regex must escape key_id.'
Assert-Matches -Text $runText -Pattern 'mappingPattern' -Message 'The exact trust-store mapping regex is missing.'
Assert-Matches -Text $runText -Pattern ([System.Text.RegularExpressions.Regex]::Escape("Groups['publicKey']")) -Message 'The trust-store mapping regex must capture only the publicKey value.'
$mappingPatternLines = @($runText -split "`n" | Where-Object { $_ -match '^\s*\$mappingPattern\s*=' })
Assert-True -Condition ($mappingPatternLines.Count -eq 1) -Message 'The workflow must define one exact keyId mapping regex.'
$mappingPatternSource = [string]$mappingPatternLines[0]
Assert-True -Condition $mappingPatternSource.Contains('\[\s*"') -Message 'The mapping regex must match the exact bracketed keyId.'
Assert-True -Condition $mappingPatternSource.Contains('"\s*\]\s*=\s*"') -Message 'The mapping regex must match the dictionary assignment.'
Assert-True -Condition $mappingPatternSource.Contains('(?<publicKey><RSAKeyValue>.*?</RSAKeyValue>)') -Message 'The mapping regex must capture the RSAKeyValue XML only.'
Assert-NotMatches -Text $runText -Pattern 'windowStart|windowLength|keyIndex|keyToken' -Message 'The workflow must not use a nearby-window trust-store search.'
Assert-Matches -Text $runText -Pattern 'RUNNER_TEMP' -Message 'The workflow must use RUNNER_TEMP for the temporary public key.'
Assert-Matches -Text $runText -Pattern 'EmbyWatchTogether\.release\.public-key\.xml' -Message 'The temporary public key filename is missing.'
Assert-Matches -Text $runText -Pattern 'WriteAllText\(\$publicKeyPath' -Message 'The mapped public key must be written to a temporary file.'
Assert-Matches -Text $runText -Pattern 'publicKeyUtf8' -Message 'The temporary public key must use a dedicated UTF-8 encoding.'
Assert-Matches -Text $runText -Pattern 'UTF8Encoding\]::new\(\$false' -Message 'The temporary public key must be written without a BOM.'
Assert-Matches -Text $runText -Pattern 'XmlReaderSettings' -Message 'The public key XML reader settings are missing.'
Assert-Matches -Text $runText -Pattern 'DtdProcessing' -Message 'The public key XML parser must configure DTD processing.'
Assert-Matches -Text $runText -Pattern 'DtdProcessing.*Prohibit|Prohibit.*DtdProcessing' -Message 'The public key XML parser must prohibit DTDs.'
Assert-Matches -Text $runText -Pattern 'XmlResolver\s*=\s*\$null' -Message 'The public key XML parser must disable XmlResolver.'
Assert-Matches -Text $runText -Pattern ([System.Text.RegularExpressions.Regex]::Escape("SelectNodes('./*')")) -Message 'The public key parser must inspect the complete child-element set.'
Assert-Matches -Text $runText -Pattern ([System.Text.RegularExpressions.Regex]::Escape("SelectNodes('./Modulus')")) -Message 'The public key parser must require a unique Modulus.'
Assert-Matches -Text $runText -Pattern ([System.Text.RegularExpressions.Regex]::Escape("SelectNodes('./Exponent')")) -Message 'The public key parser must require a unique Exponent.'
Assert-Matches -Text $runText -Pattern 'ConvertFrom-StrictBase64|FromBase64String' -Message 'The public key components must use strict base64 decoding.'
Assert-Matches -Text $runText -Pattern 'RSAParameters' -Message 'The workflow must build RSA parameters from the trusted XML.'
Assert-Matches -Text $runText -Pattern 'ImportParameters' -Message 'The workflow must import the trusted RSA parameters.'
Assert-Matches -Text $runText -Pattern 'VerifyData' -Message 'The workflow must verify the release signature.'
Assert-Matches -Text $runText -Pattern 'HashAlgorithmName\]::SHA256' -Message 'Signature verification must use SHA-256.'
Assert-Matches -Text $runText -Pattern 'RSASignaturePadding\]::Pkcs1' -Message 'Signature verification must use RSA PKCS#1 v1.5 padding.'
Assert-Matches -Text $runText -Pattern 'signatureIsValid' -Message 'The workflow must check the RSA verification result.'
Assert-Matches -Text $runText -Pattern 'if \(-not \$signatureIsValid\)' -Message 'Signature verification failure must throw.'
Assert-Matches -Text $workflowText -Pattern 'always\(\)' -Message 'Temporary public-key cleanup must run after every job outcome.'
Assert-Matches -Text $runText -Pattern 'Remove-Item' -Message 'Temporary public-key cleanup is missing.'
Assert-NotMatches -Text $runText -Pattern '(?im)Write-Output.*(?:publicKeyXml|publicKeyBytes|signatureText|signatureBytes|decodedSignature)' -Message 'The workflow must not output public-key or signature material.'

$writePublicKeyIndex = $runText.IndexOf('WriteAllText($publicKeyPath', [System.StringComparison]::Ordinal)
$readPublicKeyIndex = $runText.IndexOf('ReadAllBytes($publicKeyPath', [System.StringComparison]::Ordinal)
$verifySignatureIndex = $runText.IndexOf('VerifyData(', [System.StringComparison]::Ordinal)
$createReleaseIndex = $runText.IndexOf('gh release create', [System.StringComparison]::Ordinal)
$releaseNotesValidationIndex = $runText.IndexOf('$releaseNotesText', [System.StringComparison]::Ordinal)
$cleanupPublicKeyIndex = $runText.IndexOf('Remove-Item -LiteralPath $publicKeyPath', [System.StringComparison]::Ordinal)
Assert-True -Condition ($writePublicKeyIndex -ge 0 -and $writePublicKeyIndex -lt $readPublicKeyIndex) -Message 'The asset step must read the public key after validation writes it.'
Assert-True -Condition ($readPublicKeyIndex -lt $verifySignatureIndex -and $verifySignatureIndex -lt $createReleaseIndex) -Message 'Signature verification must occur before release creation.'
Assert-True -Condition ($releaseNotesValidationIndex -ge 0 -and $releaseNotesValidationIndex -lt $createReleaseIndex) -Message 'Release notes validation must occur before release creation.'
Assert-True -Condition ($createReleaseIndex -lt $cleanupPublicKeyIndex) -Message 'Temporary public-key cleanup must follow the release step.'

Assert-Matches -Text $runText -Pattern 'dotnet\s+build\s+src[\\/]EmbyWatchTogether\.sln\s+-c\s+Release\s+--nologo' -Message 'The Release solution build command is missing.'
Assert-Matches -Text $runText -Pattern 'dotnet\s+test\s+tests[\\/]EmbyWatchTogether\.Tests[\\/]EmbyWatchTogether\.Tests\.csproj\s+-c\s+Release\s+--nologo\s+-v\s+minimal' -Message 'The complete Release test command is missing.'
Assert-Matches -Text $runText -Pattern 'pwsh\s+scripts[\\/]build\.ps1\s+-Configuration\s+Release' -Message 'The release packaging command is missing.'
Assert-Matches -Text $runText -Pattern 'pwsh\s+tests[\\/]release-signing\.tests\.ps1' -Message 'The release signing test command is missing.'
Assert-Matches -Text $runText -Pattern 'Sign-ReleaseManifest\.ps1' -Message 'The release manifest signing command is missing.'
Assert-Matches -Text $runText -Pattern 'gh\s+release\s+create\s+\$env:RELEASE_TAG' -Message 'The gh release create command is missing.'
Assert-Matches -Text $runText -Pattern '--verify-tag' -Message 'The release creation command must use --verify-tag.'
Assert-Matches -Text $runText -Pattern 'docs/releases/\{0\}\.md' -Message 'The release notes path must be derived from the canonical tag.'
Assert-Matches -Text $runText -Pattern 'releaseNotesPath|releaseNotesBytes|strictUtf8|CJK|3400' -Message 'The workflow must validate release notes encoding, content, and Chinese characters.'
Assert-Matches -Text $runText -Pattern '--notes-file\s+\$releaseNotesPath' -Message 'The release creation command must use the validated notes file.'
Assert-NotMatches -Text $runText -Pattern '--generate-notes' -Message 'The release creation command must not generate unreviewed notes.'
Assert-Matches -Text $runText -Pattern '--title\s+\(\x27Release\s*\x27\s+\+\s+\$env:RELEASE_TAG\)' -Message 'The release title must contain the requested tag.'

$projectPath = Join-Path $repositoryRoot 'src/EmbyWatchTogether/EmbyWatchTogether.csproj'
$projectText = Get-Content -LiteralPath $projectPath -Raw
$versionMatch = [System.Text.RegularExpressions.Regex]::Match($projectText, '<Version>(?<version>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)</Version>')
Assert-True -Condition $versionMatch.Success -Message 'The project Version is missing or not four-part canonical.'
$notesPath = Join-Path $repositoryRoot ('docs/releases/v{0}.md' -f $versionMatch.Groups['version'].Value)
Assert-True -Condition ([System.IO.File]::Exists($notesPath)) -Message 'The version release notes file is missing.'
$notesBytes = [System.IO.File]::ReadAllBytes($notesPath)
Assert-True -Condition ($notesBytes.Length -gt 0) -Message 'The version release notes file is empty.'
Assert-True -Condition (-not ($notesBytes.Length -ge 3 -and $notesBytes[0] -eq 0xEF -and $notesBytes[1] -eq 0xBB -and $notesBytes[2] -eq 0xBF)) -Message 'The version release notes must not contain a UTF-8 BOM.'
try { $notesText = $strictUtf8.GetString($notesBytes) } catch { throw 'The version release notes are not strict UTF-8.' }
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($notesText)) -Message 'The version release notes must be non-empty.'
Assert-Matches -Text $notesText -Pattern '[\u3400-\u9FFF]' -Message 'The version release notes must contain Chinese text.'

foreach ($assetPath in @(
        'dist/EmbyWatchTogether/Emby.Plugins.WatchTogether.dll'
        'dist/EmbyWatchTogether.zip'
        'dist/EmbyWatchTogether.release.manifest'
        'dist/EmbyWatchTogether.release.manifest.sig')) {
    Assert-Matches -Text $runText -Pattern ([System.Text.RegularExpressions.Regex]::Escape($assetPath)) -Message ('The fixed release asset is missing: {0}.' -f $assetPath)
}

$secretExpressionMatches = [System.Text.RegularExpressions.Regex]::Matches(
    $workflowText,
    '\$\{\{\s*secrets\.([A-Za-z0-9_]+)\s*\}\}')
Assert-True -Condition ($secretExpressionMatches.Count -gt 0) -Message 'The signing secret is not mapped from GitHub secrets.'
foreach ($secretExpressionMatch in $secretExpressionMatches) {
    Assert-Equal -Expected 'WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64' `
        -Actual $secretExpressionMatch.Groups[1].Value `
        -Message 'An unexpected GitHub secret is referenced by the workflow.'
}

foreach ($line in ($workflowText -split "`n")) {
    if ($line -match '\$\{\{\s*secrets\.') {
        Assert-True -Condition ($line -match '^\s+WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64:\s*\$\{\{\s*secrets\.WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64\s*\}\}\s*$') `
            -Message 'The GitHub secret must be mapped only to the fixed environment variable.'
    }
}

Assert-NotMatches -Text $runText -Pattern '\$\{\{\s*secrets\.' -Message 'A GitHub secret expression appears inside a run script.'
Assert-NotMatches -Text $runText -Pattern '\$\{\{\s*inputs\.(?:tag|key_id)\s*\}\}' -Message 'A workflow input is interpolated directly inside a run script.'
Assert-NotMatches -Text $runText -Pattern '(?i)(?:^|\s)-PrivateKeyPkcs8Base64(?:\s|$)' -Message 'The signing script must receive its private key only from its environment.'
Assert-NotMatches -Text $runText -Pattern '(?im)^\s*(?:&\s*)?(?:git\s+)?(?:push|pull|ssh|scp|docker|deploy|deployment|server)\b' -Message 'The workflow contains a forbidden publish or deployment command.'
Assert-NotMatches -Text $workflowText -Pattern '(?im)^\s*(?:push|pull_request|schedule):\s*$' -Message 'The workflow contains a forbidden automatic trigger.'

Write-Output 'release workflow static tests passed'
