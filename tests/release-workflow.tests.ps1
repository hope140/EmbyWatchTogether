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

Assert-Matches -Text $workflowText -Pattern 'actions/checkout@v4' -Message 'The workflow checkout action is missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}ref:\s*\$\{\{\s*inputs\.tag\s*\}\}\s*$' -Message 'Checkout must use the requested tag as ref.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}fetch-depth:\s*0\s*$' -Message 'Checkout must use fetch-depth 0.'
Assert-Matches -Text $workflowText -Pattern 'actions/setup-dotnet@v4' -Message 'The .NET setup action is missing.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}dotnet-version:\s*[\x27\"]?10\.0\.x[\x27\"]?\s*$' -Message 'The workflow must use the .NET 10 SDK.'

$runBlocks = @(Get-RunBlocks -Text $workflowText)
Assert-True -Condition ($runBlocks.Count -ge 8) -Message 'The workflow is missing required PowerShell run steps.'
$runText = $runBlocks -join "`n"

Assert-Matches -Text $runText -Pattern 'RELEASE_TAG|RELEASE_KEY_ID' -Message 'PowerShell steps do not consume the environment inputs.'
Assert-Matches -Text $runText -Pattern 'WATCH_TOGETHER_RELEASE_SIGNING_KEY_PKCS8_B64' -Message 'The signing secret environment variable is not checked or consumed.'
Assert-Matches -Text $runText -Pattern 'ReleaseTrustStore\.cs' -Message 'The workflow does not inspect ReleaseTrustStore.cs.'
Assert-Matches -Text $runText -Pattern 'RSAKeyValue' -Message 'The workflow does not require an RSAKeyValue public key.'
Assert-Matches -Text $runText -Pattern 'trustStoreText|keyIndex|keyToken' -Message 'The workflow does not tie the requested key_id to the trust store source.'
Assert-Matches -Text $runText -Pattern 'throw' -Message 'The trust-store validation is not fail closed.'

Assert-Matches -Text $runText -Pattern 'dotnet\s+build\s+src[\\/]EmbyWatchTogether\.sln\s+-c\s+Release\s+--nologo' -Message 'The Release solution build command is missing.'
Assert-Matches -Text $runText -Pattern 'dotnet\s+test\s+tests[\\/]EmbyWatchTogether\.Tests[\\/]EmbyWatchTogether\.Tests\.csproj\s+-c\s+Release\s+--nologo\s+-v\s+minimal' -Message 'The complete Release test command is missing.'
Assert-Matches -Text $runText -Pattern 'pwsh\s+scripts[\\/]build\.ps1\s+-Configuration\s+Release' -Message 'The release packaging command is missing.'
Assert-Matches -Text $runText -Pattern 'pwsh\s+tests[\\/]release-signing\.tests\.ps1' -Message 'The release signing test command is missing.'
Assert-Matches -Text $runText -Pattern 'Sign-ReleaseManifest\.ps1' -Message 'The release manifest signing command is missing.'
Assert-Matches -Text $runText -Pattern 'gh\s+release\s+create\s+\$env:RELEASE_TAG' -Message 'The gh release create command is missing.'
Assert-Matches -Text $runText -Pattern '--verify-tag' -Message 'The release creation command must use --verify-tag.'
Assert-Matches -Text $runText -Pattern '--generate-notes' -Message 'The release creation command must generate notes.'
Assert-Matches -Text $runText -Pattern '--title\s+\(\x27Release\s*\x27\s+\+\s+\$env:RELEASE_TAG\)' -Message 'The release title must contain the requested tag.'

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
