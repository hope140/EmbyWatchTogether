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

function Assert-Matches {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Pattern,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Assert-True -Condition ($Text -match $Pattern) -Message $Message
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

    Assert-True -Condition ($Text -notmatch $Pattern) -Message $Message
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workflowPath = Join-Path $repositoryRoot '.github\workflows\ci.yml'
Assert-True -Condition ([System.IO.File]::Exists($workflowPath)) -Message 'The CI workflow is missing.'

$workflowBytes = [System.IO.File]::ReadAllBytes($workflowPath)
Assert-True -Condition ($workflowBytes.Length -gt 0) -Message 'The CI workflow is empty.'
Assert-True -Condition (-not ($workflowBytes.Length -ge 3 -and
        $workflowBytes[0] -eq 0xEF -and $workflowBytes[1] -eq 0xBB -and $workflowBytes[2] -eq 0xBF)) `
    -Message 'The CI workflow must not contain a UTF-8 BOM.'

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
try {
    $workflowText = $strictUtf8.GetString($workflowBytes)
}
catch {
    throw 'The CI workflow is not strict UTF-8.'
}

$workflowText = $workflowText.Replace("`r`n", "`n").Replace("`r", "`n")

Assert-Matches -Text $workflowText -Pattern '(?m)^on:\s*$' -Message 'The CI workflow trigger block is missing.'
Assert-Matches -Text $workflowText -Pattern '(?ms)^on:\s*\n(?:(?!^permissions:).)*^\s{2}push:\s*\n\s{4}branches:\s*\n\s{6}-\s*main\s*\n\s{6}-\s*beta\s*' `
    -Message 'The CI workflow must run on pushes to main and beta.'
Assert-Matches -Text $workflowText -Pattern '(?ms)^on:\s*\n(?:(?!^permissions:).)*^\s{2}pull_request:\s*$' `
    -Message 'The CI workflow must run for pull requests.'

Assert-Matches -Text $workflowText -Pattern '(?m)^permissions:\s*\n\s{2}contents:\s*read\s*$' `
    -Message 'The CI workflow must grant contents read permission only.'
Assert-NotMatches -Text $workflowText -Pattern '(?m)^\s+contents:\s*(?:write|none)\s*$' `
    -Message 'The CI workflow must not grant contents write permission.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s+uses:\s*actions/checkout@[0-9a-f]{40}\s+#\s*v5\s*$' `
    -Message 'The CI workflow checkout action must use a pinned v5 SHA.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s+uses:\s*actions/setup-dotnet@[0-9a-f]{40}\s+#\s*v5\s*$' `
    -Message 'The CI workflow setup-dotnet action must use a pinned v5 SHA.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s+uses:\s*actions/upload-artifact@[0-9a-f]{40}\s+#\s*v4\.6\.2\s*$' `
    -Message 'The test artifact action must use the approved pinned v4.6.2 SHA.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}persist-credentials:\s*false\s*$' `
    -Message 'Checkout credentials must not persist.'
Assert-Matches -Text $workflowText -Pattern '(?m)^\s{10}dotnet-version:\s*[\x27\"]?10\.0\.x[\x27\"]?\s*$' `
    -Message 'The CI workflow must use the .NET 10 SDK.'
Assert-NotMatches -Text $workflowText -Pattern '\$\{\{\s*secrets\.' `
    -Message 'The CI workflow must not reference secrets.'
Assert-NotMatches -Text $workflowText -Pattern '(?im)^\s*(?:gh\s+release|git\s+push|docker\s+push|scp\s|ssh\s)' `
    -Message 'The CI workflow must not publish or deploy.'

Assert-Matches -Text $workflowText -Pattern 'dotnet\s+restore\s+src[\\/]EmbyWatchTogether\.sln\s+--nologo' `
    -Message 'The CI workflow must restore the explicit solution.'
Assert-Matches -Text $workflowText -Pattern 'dotnet\s+test\s+tests[\\/]EmbyWatchTogether\.Tests[\\/]EmbyWatchTogether\.Tests\.csproj\s+-c\s+Release' `
    -Message 'The CI workflow must test the explicit test project in Release.'
Assert-Matches -Text $workflowText -Pattern '--logger\s+"trx;LogFileName=EmbyWatchTogether\.Tests\.trx"\s+--results-directory\s+artifacts/test-results' `
    -Message 'The test step must write a TRX result into artifacts/test-results.'
Assert-Matches -Text $workflowText -Pattern 'dotnet\s+build\s+src[\\/]EmbyWatchTogether\.sln\s+-c\s+Release\s+--nologo' `
    -Message 'The CI workflow must build the explicit solution in Release.'
foreach ($scriptName in @('build-path-guard.tests.ps1', 'release-signing.tests.ps1', 'release-workflow.tests.ps1', 'ci-workflow.tests.ps1')) {
    Assert-Matches -Text $workflowText -Pattern ([System.Text.RegularExpressions.Regex]::Escape("tests/$scriptName")) `
        -Message ("The CI workflow must run tests/{0}." -f $scriptName)
}

$nativeCommands = @(
    'dotnet restore src/EmbyWatchTogether.sln --nologo',
    'dotnet test tests/EmbyWatchTogether.Tests/EmbyWatchTogether.Tests.csproj -c Release',
    'dotnet build src/EmbyWatchTogether.sln -c Release',
    'pwsh -NoProfile -File tests/build-path-guard.tests.ps1',
    'pwsh -NoProfile -File tests/release-signing.tests.ps1',
    'pwsh -NoProfile -File tests/release-workflow.tests.ps1',
    'pwsh -NoProfile -File tests/ci-workflow.tests.ps1'
)
foreach ($command in $nativeCommands) {
    $commandIndex = $workflowText.IndexOf($command, [System.StringComparison]::Ordinal)
    Assert-True -Condition ($commandIndex -ge 0) -Message ("Missing CI command: {0}." -f $command)
    $nextStepIndex = $workflowText.IndexOf("`n      - name:", $commandIndex, [System.StringComparison]::Ordinal)
    $stepText = if ($nextStepIndex -ge 0) {
        $workflowText.Substring($commandIndex, $nextStepIndex - $commandIndex)
    }
    else {
        $workflowText.Substring($commandIndex)
    }
    Assert-Matches -Text $stepText -Pattern '(?s)if\s*\(\$LASTEXITCODE\s+-ne\s+0\)\s*\{\s*exit\s+\$LASTEXITCODE\s*\}' `
        -Message ("The native exit code is not propagated after: {0}." -f $command)
}

$artifactIndex = $workflowText.IndexOf('Upload test results', [System.StringComparison]::Ordinal)
Assert-True -Condition ($artifactIndex -ge 0) -Message 'The test result artifact step is missing.'
$artifactText = $workflowText.Substring($artifactIndex)
Assert-Matches -Text $artifactText -Pattern '(?m)^\s+if:\s*\$\{\{\s*always\(\)\s*\}\}\s*$' `
    -Message 'The test result artifact must be uploaded even when a prior step fails.'
Assert-Matches -Text $artifactText -Pattern '(?m)^\s{10}path:\s*artifacts/test-results/\*\.trx\s*$' `
    -Message 'The artifact step must upload TRX files.'

Write-Output 'CI workflow static tests passed'
