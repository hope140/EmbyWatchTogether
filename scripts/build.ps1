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

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Get-ChildItem -LiteralPath $publishDir -Filter *.dll | Where-Object {
    $_.Name -eq 'Emby.Plugins.WatchTogether.dll' -or $_.Name -like 'System.*' -or $_.Name -like 'Microsoft.Bcl.*'
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $pluginDir $_.Name) -Force
}

if (Test-Path -LiteralPath $archivePath) {
    [System.IO.File]::Delete($archivePath)
}
Compress-Archive -Path (Join-Path $pluginDir '*') -DestinationPath $archivePath -Force

Write-Output "==> plugin folder: $pluginDir"
Write-Output "==> archive: $archivePath"
