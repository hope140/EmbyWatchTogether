[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [switch]$ValidatePathsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-NormalizedAbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'A path is required and cannot be empty or whitespace.'
    }

    try {
        $providerPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
        $fullPath = [System.IO.Path]::GetFullPath($providerPath)
        $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    }
    catch {
        throw "Unable to resolve path '$Path' as an absolute filesystem path: $($_.Exception.Message)"
    }

    if ([string]::IsNullOrEmpty($pathRoot)) {
        throw "Unable to resolve path '$Path' to a filesystem root."
    }

    if ($fullPath.Length -gt $pathRoot.Length) {
        $fullPath = $fullPath.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )
    }

    return $fullPath
}

function Test-SameOrAncestorPath {
    param(
        [Parameter(Mandatory)]
        [string]$Ancestor,
        [Parameter(Mandatory)]
        [string]$Candidate
    )

    if ($Candidate.Equals($Ancestor, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $ancestorPrefix = $Ancestor
    if (-not ($ancestorPrefix.EndsWith([System.IO.Path]::DirectorySeparatorChar) -or
            $ancestorPrefix.EndsWith([System.IO.Path]::AltDirectorySeparatorChar))) {
        $ancestorPrefix += [System.IO.Path]::DirectorySeparatorChar
    }

    return $Candidate.StartsWith($ancestorPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

$root = ConvertTo-NormalizedAbsolutePath -Path (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'dist'
}

$outputDirectoryPath = ConvertTo-NormalizedAbsolutePath -Path $OutputDirectory
$volumeRoot = [System.IO.Path]::GetPathRoot($outputDirectoryPath)
$pluginDir = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $outputDirectoryPath 'EmbyWatchTogether')

if ($outputDirectoryPath.Equals($volumeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing OutputDirectory '$OutputDirectory': resolved path '$outputDirectoryPath' is the volume root '$volumeRoot'. Choose a dedicated output directory below the repository or another non-root directory."
}

if (Test-SameOrAncestorPath -Ancestor $outputDirectoryPath -Candidate $root) {
    throw "Refusing OutputDirectory '$OutputDirectory': resolved path '$outputDirectoryPath' is the repository root or an ancestor of '$root'. Choose a dedicated output directory such as '$root\dist' or a temporary directory outside the repository ancestors."
}

if (Test-SameOrAncestorPath -Ancestor $pluginDir -Candidate $root) {
    throw "Refusing OutputDirectory '$OutputDirectory': the EmbyWatchTogether target '$pluginDir' is the repository root or an ancestor of '$root'. Choose a different dedicated output directory."
}

$solution = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $root 'src\EmbyWatchTogether.sln')
$project = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $root 'src\EmbyWatchTogether\EmbyWatchTogether.csproj')
$testProject = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $root 'tests\EmbyWatchTogether.Tests\EmbyWatchTogether.Tests.csproj')
$publishDir = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $root '.publish')
$archivePath = ConvertTo-NormalizedAbsolutePath -Path (Join-Path $outputDirectoryPath 'EmbyWatchTogether.zip')

if ($ValidatePathsOnly) {
    Write-Output 'Path validation passed.'
    Write-Output "==> output directory: $outputDirectoryPath"
    Write-Output "==> plugin folder: $pluginDir"
    Write-Output "==> archive: $archivePath"
    return
}

Write-Output "==> dotnet build $solution"
dotnet build $solution -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

Write-Output "==> dotnet test $testProject"
dotnet test $testProject -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }

Write-Output "==> dotnet publish $project"
if (Test-Path -LiteralPath $publishDir) {
    [System.IO.Directory]::Delete($publishDir, $true)
}
dotnet publish $project -c $Configuration -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

if (Test-Path -LiteralPath $pluginDir) {
    [System.IO.Directory]::Delete($pluginDir, $true)
}
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publishDir 'Emby.Plugins.WatchTogether.dll') `
    -Destination (Join-Path $pluginDir 'Emby.Plugins.WatchTogether.dll') -Force

if (Test-Path -LiteralPath $archivePath) {
    [System.IO.File]::Delete($archivePath)
}
Compress-Archive -Path (Join-Path $pluginDir '*') -DestinationPath $archivePath -Force

Write-Output "==> plugin folder: $pluginDir"
Write-Output "==> archive: $archivePath"
