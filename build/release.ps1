<#
.SYNOPSIS
    Builds, tests, and packages APS Planner (APS.DesktopHost) as a Velopack installer release.

.DESCRIPTION
    This is the project's explicit release-packaging path. Continuous verification runs on the
    shared self-hosted Windows Azure DevOps agent, but release packaging remains manual so a normal
    feature branch or CI run cannot accidentally manufacture a distributable release.

    The release script does, in order:

      1. dotnet test    — runs the planning and UI test suites, stops on any failure.
      2. dotnet publish — publishes APS.DesktopHost for win-x64, self-contained, with
                           PublishReadyToRun (already set in the csproj).
      3. vpk pack       — wraps that publish output into a Velopack release: a Setup.exe installer
                           plus update metadata, written to build/Releases/<version>/.

.PARAMETER Version
    Release version (e.g. "1.2.0"). If omitted, the script reads <Version> from
    APS.DesktopHost.csproj.

.PARAMETER AppId
    Velopack app id used to identify this app across releases/updates. Defaults to "APS". Change
    only if you also update UpdateSettings.RepositoryUrl-related tooling to match — the app id
    must stay consistent release-to-release or update detection breaks.

.PARAMETER Configuration
    Build configuration for publish. Defaults to "Release".

.PARAMETER SkipTests
    Skip the dotnet test step. Use only for quick local iteration — never for a real release.

.PREREQUISITES
    - .NET 10 SDK.
    - Windows (win-x64 self-contained publish + vpk pack both require running on Windows).
    - Velopack CLI: install once with `dotnet tool install -g vpk`
      (upgrade later with `dotnet tool update -g vpk`).

.EXAMPLE
    pwsh build/release.ps1 -Version 1.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$AppId = "APS",
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopHostProject = Join-Path $repoRoot "src/APS.DesktopHost/APS.DesktopHost.csproj"
$testProjects = @(
    (Join-Path $repoRoot "tests/APS.Planning.Tests/APS.Planning.Tests.csproj"),
    (Join-Path $repoRoot "tests/APS.UI.Tests/APS.UI.Tests.csproj")
)
$appIcon = Join-Path $repoRoot "src/APS.DesktopHost/Assets/app-icon.ico"
$publishDir = Join-Path $repoRoot "build/publish/win-x64"

function Get-CsprojVersion {
    param([string]$CsprojPath)
    $xml = [xml](Get-Content -Path $CsprojPath)
    $versionNode = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if (-not $versionNode) {
        throw "Could not find <Version> in $CsprojPath. Pass -Version explicitly."
    }
    return $versionNode
}

if (-not $Version) {
    $Version = Get-CsprojVersion -CsprojPath $desktopHostProject
    Write-Host "No -Version supplied; using <Version> from csproj: $Version"
}

$releasesDir = Join-Path $repoRoot "build/Releases/$Version"

# --- Step 1: tests ---------------------------------------------------------
if (-not $SkipTests) {
    foreach ($testProject in $testProjects) {
        Write-Host "==> dotnet test $testProject"
        dotnet test $testProject --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed for '$testProject' (exit code $LASTEXITCODE). Aborting release."
        }
    }
}
else {
    Write-Host "==> Skipping tests (-SkipTests passed). Do not use this path for a real release."
}

# --- Step 2: publish --------------------------------------------------------
Write-Host "==> dotnet publish $desktopHostProject (win-x64, self-contained, ReadyToRun)"
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
dotnet publish $desktopHostProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit code $LASTEXITCODE). Aborting release."
}

# --- Step 3: Velopack pack --------------------------------------------------
$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if (-not $vpk) {
    throw "vpk (Velopack CLI) was not found on PATH. Install it with: dotnet tool install -g vpk"
}

Write-Host "==> vpk pack (app id: $AppId, version: $Version)"
if (Test-Path $releasesDir) {
    Remove-Item -Recurse -Force $releasesDir
}
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
vpk pack `
    --packId $AppId `
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
