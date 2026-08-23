<#
.SYNOPSIS
    Builds, tests, and packages APS Planner (APS.DesktopHost) as a Velopack installer release.

.DESCRIPTION
    This is the project's explicit release-packaging path. Continuous verification runs on the
    shared self-hosted Windows Azure DevOps agent, but release packaging remains manual so a normal
    feature branch or CI run cannot accidentally manufacture a distributable release.

    The release script does, in order:

      1. dotnet test    — runs every test project registered in APS.slnx and stops on any failure.
      2. dotnet publish — publishes APS.DesktopHost using the runtime, self-contained and
                           ReadyToRun settings owned by APS.DesktopHost.csproj.
      3. vpk pack       — wraps that publish output into a Velopack release: a Setup.exe installer
                           plus update metadata, written to build/Releases/<version>/.

.PARAMETER Version
    Release version (e.g. "1.2.0"). If omitted, reads <Version> from APS.DesktopHost.csproj.

.PARAMETER Configuration
    Build configuration. Defaults to "Release".

.PARAMETER SkipTests
    Skip the dotnet test step. Use only for quick local iteration, never for a real release.

.PREREQUISITES
    - .NET 10 SDK.
    - Windows.
    - Velopack CLI: `dotnet tool install -g vpk`

.EXAMPLE
    pwsh build/release.ps1 -Version 1.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$appId = "APS"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopHostProject = Join-Path $repoRoot "src/APS.DesktopHost/APS.DesktopHost.csproj"
$solution = Join-Path $repoRoot "APS.slnx"
$appIcon = Join-Path $repoRoot "src/APS.DesktopHost/Assets/app-icon.ico"
$publishDir = Join-Path $repoRoot "build/publish/win-x64"

function Get-CsprojVersion {
    param([string]$CsprojPath)
    $xml = [xml](Get-Content -Path $CsprojPath)
    $versionNode = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $versionNode) {
        throw "Could not find <Version> in $CsprojPath. Pass -Version explicitly."
    }
    $versionNode
}

if (-not $Version) {
    $Version = Get-CsprojVersion -CsprojPath $desktopHostProject
    Write-Host "No -Version supplied; using <Version> from csproj: $Version"
}

$releasesDir = Join-Path $repoRoot "build/Releases/$Version"

if (-not $SkipTests) {
    Write-Host "==> dotnet test $solution"
    dotnet test $solution --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed (exit code $LASTEXITCODE). Aborting release."
    }
}
else {
    Write-Host "==> Skipping tests (-SkipTests passed). Do not use this path for a real release."
}

Write-Host "==> dotnet publish $desktopHostProject"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
dotnet publish $desktopHostProject `
    --configuration $Configuration `
    -p:Version=$Version `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code $LASTEXITCODE). Aborting release."
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk (Velopack CLI) was not found on PATH. Install it with: dotnet tool install -g vpk"
}

Write-Host "==> vpk pack (app id: $appId, version: $Version)"
if (Test-Path $releasesDir) {
    Remove-Item -Recurse -Force $releasesDir
}
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
vpk pack `
    --packId $appId `
    --packVersion $Version `
    --packTitle "APS Planner" `
    --packAuthors "APS" `
    --packDir $publishDir `
    --mainExe "APS.DesktopHost.exe" `
    --icon $appIcon `
    --shortcuts "Desktop,StartMenuRoot" `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed (exit code $LASTEXITCODE)."
}

Write-Host ""
Write-Host "Release $Version packed successfully. Output: $releasesDir"
Write-Host "Next step: publish $releasesDir with vpk upload so installed copies can discover the update."
