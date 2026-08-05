[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'dist'
}

$solution = Join-Path $root 'src\EmbyWatchTogether.sln'
$project = Join-Path $root 'src\EmbyWatchTogether\EmbyWatchTogether.csproj'
$testProject = Join-Path $root 'tests\EmbyWatchTogether.Tests\EmbyWatchTogether.Tests.csproj'
$publishDir = Join-Path $root '.publish'
$pluginDir = Join-Path $OutputDirectory 'EmbyWatchTogether'
$archivePath = Join-Path $OutputDirectory 'EmbyWatchTogether.zip'

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
