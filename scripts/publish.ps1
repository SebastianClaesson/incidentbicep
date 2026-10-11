<#
.SYNOPSIS
    Builds and packages the bicep-ext-incidentio extension locally.
.DESCRIPTION
    Cross-compiles self-contained binaries for Windows, Linux and macOS, then packages
    them with 'bicep publish-extension' into a local folder (or an OCI registry target).
.PARAMETER OutputPath
    Where to write the packaged extension. Defaults to ./extension-publish/bicep-ext-incidentio.
    Can also be a registry reference, e.g. 'br:myregistry.azurecr.io/bicep/extensions/incidentio:0.1.0'.
.PARAMETER Runtimes
    Runtime identifiers to build. Defaults to win-x64, linux-x64, osx-arm64.
.EXAMPLE
    ./scripts/publish.ps1
.EXAMPLE
    ./scripts/publish.ps1 -Runtimes win-x64
#>
param(
    [string]$OutputPath = "$PSScriptRoot/../extension-publish/bicep-ext-incidentio",
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string[]]$Runtimes = @('win-x64', 'linux-x64', 'osx-arm64')
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '../src/bicep-ext-incidentio.csproj'
$buildRoot = Join-Path $PSScriptRoot '../extension-publish/bin'

$binaryArgs = @()
foreach ($rid in $Runtimes) {
    Write-Host "Building for $rid..." -ForegroundColor Cyan
    $out = Join-Path $buildRoot $rid
    dotnet publish $projectPath --configuration Release -r $rid -o $out
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $rid" }

    $exe = if ($rid -like 'win-*') { 'bicep-ext-incidentio.exe' } else { 'bicep-ext-incidentio' }
    $binaryArgs += "--bin-$rid", (Join-Path $out $exe)
}

Write-Host "Packaging extension..." -ForegroundColor Cyan
bicep publish-extension @binaryArgs --target $OutputPath --force
if ($LASTEXITCODE -ne 0) { throw "bicep publish-extension failed" }

# Bicep caches extensions; clear the local cache so the next deploy picks up this build.
$cache = Join-Path $HOME '.bicep/local'
if (Test-Path $cache) { Remove-Item -Recurse -Force $cache }

Write-Host "Extension published to: $OutputPath" -ForegroundColor Green
