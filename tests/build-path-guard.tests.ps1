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
        [Parameter(Mandatory)]
        [string]$OutputDirectory
    )

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-NonInteractive',
        '-File',
        $scriptPath,
        '-OutputDirectory',
        $OutputDirectory,
        '-ValidatePathsOnly'
    )
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

Write-Output "Build path guard tests passed for $($seen.Count) rejected paths and one safe path."
