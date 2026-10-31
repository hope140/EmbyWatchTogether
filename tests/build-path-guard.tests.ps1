[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\scripts\build.ps1'))
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$volumeRoot = [System.IO.Path]::GetPathRoot($repositoryRoot)
$hostExecutable = (Get-Process -Id $PID).Path

function Invoke-PathValidation {
    param(
        [string]$OutputDirectory
    )

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        $scriptPath,
        '-ValidatePathsOnly'
    )
    if (-not [string]::IsNullOrEmpty($OutputDirectory)) {
        $arguments += @('-OutputDirectory', $OutputDirectory)
    }
    $output = & $hostExecutable @arguments 2>&1

    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String)
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-PathRejected {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $result = Invoke-PathValidation -OutputDirectory $Path
    Assert-True -Condition ($result.ExitCode -ne 0) -Message "Expected '$Path' to be rejected, but validation exited with code $($result.ExitCode). Output: $($result.Output)"
    Assert-True -Condition ($result.Output -match 'Refusing OutputDirectory') -Message "Expected an actionable refusal for '$Path'. Output: $($result.Output)"
}

$rejectedPaths = [System.Collections.Generic.List[string]]::new()
$rejectedPaths.Add($volumeRoot)
$rejectedPaths.Add($repositoryRoot)

$ancestor = Split-Path -Parent $repositoryRoot
while (-not [string]::IsNullOrEmpty($ancestor)) {
    $rejectedPaths.Add($ancestor)
    if ($ancestor.Equals($volumeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        break
    }
    $ancestor = Split-Path -Parent $ancestor
}

$rejectedPaths.Add((Join-Path $repositoryRoot 'scripts\..'))
$rejectedPaths.Add((Join-Path $repositoryRoot '.\'))
$rejectedPaths.Add((Join-Path $repositoryRoot 'scripts\..\src'))
foreach ($directoryName in @('src', 'tests', 'scripts', 'docs', '.github', '.git', '.codex', '.agents', '.publish')) {
    $rejectedPaths.Add((Join-Path $repositoryRoot $directoryName))
    $rejectedPaths.Add((Join-Path $repositoryRoot "$directoryName/build-output"))
}

$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($path in $rejectedPaths) {
    if ($seen.Add($path)) {
        Assert-PathRejected -Path $path
    }
}

$safeOutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('EmbyWatchTogether-build-guard-' + [System.Guid]::NewGuid().ToString('N'))
$safeResult = Invoke-PathValidation -OutputDirectory $safeOutputDirectory
Assert-True -Condition ($safeResult.ExitCode -eq 0) -Message "Expected safe output directory '$safeOutputDirectory' to pass, but validation exited with code $($safeResult.ExitCode). Output: $($safeResult.Output)"
Assert-True -Condition ($safeResult.Output -match 'Path validation passed') -Message "Expected a successful validation message for '$safeOutputDirectory'. Output: $($safeResult.Output)"
Assert-True -Condition (-not (Test-Path -LiteralPath $safeOutputDirectory)) -Message "Path-only validation created '$safeOutputDirectory'."

$safePaths = @(
    (Join-Path $repositoryRoot 'dist'),
    (Join-Path $repositoryRoot 'src-output'),
    (Join-Path $repositoryRoot '.publish-output')
)
foreach ($path in $safePaths) {
    $existedBefore = Test-Path -LiteralPath $path
    $result = Invoke-PathValidation -OutputDirectory $path
    Assert-True -Condition ($result.ExitCode -eq 0) -Message "Expected safe output '$path' to pass. Output: $($result.Output)"
    Assert-True -Condition ((Test-Path -LiteralPath $path) -eq $existedBefore) -Message "Path-only validation changed '$path'."
}
$defaultResult = Invoke-PathValidation
Assert-True -Condition ($defaultResult.ExitCode -eq 0) -Message "Expected default dist output to pass. Output: $($defaultResult.Output)"

# All alias checks use validation-only mode. Links are created outside the
# repository and removed directly before the fixture directory is cleaned up.
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('EmbyWatchTogether-build-alias-' + [System.Guid]::NewGuid().ToString('N'))
$createdLinks = [System.Collections.Generic.List[string]]::new()
$originalScriptPath = $scriptPath
$linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    $sourceAlias = Join-Path $fixtureRoot 'source-alias'
    New-Item -ItemType $linkType -Path $sourceAlias -Target (Join-Path $repositoryRoot 'src') -ErrorAction Stop | Out-Null
    $createdLinks.Add($sourceAlias)
    Assert-PathRejected -Path $sourceAlias
    Assert-PathRejected -Path (Join-Path $sourceAlias 'not-created')

    $externalOutput = Join-Path $fixtureRoot 'output'
    New-Item -ItemType Directory -Path $externalOutput -Force | Out-Null
    $pluginAlias = Join-Path $externalOutput 'EmbyWatchTogether'
    New-Item -ItemType $linkType -Path $pluginAlias -Target (Join-Path $repositoryRoot 'src/EmbyWatchTogether') -ErrorAction Stop | Out-Null
    $createdLinks.Add($pluginAlias)
    Assert-PathRejected -Path $externalOutput

    # A staging alias must also be rejected when the chosen output is safe.
    $fixtureScripts = Join-Path $fixtureRoot 'scripts'
    New-Item -ItemType Directory -Path $fixtureScripts -Force | Out-Null
    $scriptPath = Join-Path $fixtureScripts 'build.ps1'
    Copy-Item -LiteralPath $originalScriptPath -Destination $scriptPath
    $stagingAlias = Join-Path $fixtureRoot '.publish'
    New-Item -ItemType $linkType -Path $stagingAlias -Target (Join-Path $repositoryRoot 'src') -ErrorAction Stop | Out-Null
    $createdLinks.Add($stagingAlias)
    Assert-PathRejected -Path (Join-Path $fixtureRoot 'dist')
    Assert-True -Condition (-not (Test-Path -LiteralPath (Join-Path $fixtureRoot 'dist'))) -Message 'Path-only validation created fixture output.'
}
finally {
    $scriptPath = $originalScriptPath
    foreach ($link in $createdLinks) {
        [System.IO.Directory]::Delete($link)
    }
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

Write-Output "Build path guard tests passed for $($seen.Count) protected paths, four alias cases, and five safe outputs."
