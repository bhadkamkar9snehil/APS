# Installer & Auto-Update (Velopack)

**Re-baselined:** 23-Aug-2026 against current `main`.

APS desktop release packaging is an explicit Windows release action using Velopack. Continuous APS verification and release publishing are deliberately separate concerns.

## Runtime update path

Current desktop ownership is intentionally compact:

- `Program.Main` calls `VelopackApp.Build().Run()` before constructing the WPF application;
- `App.xaml.cs` owns the APS GitHub update repository URL because the desktop composition root is the runtime consumer;
- `App.xaml.cs` registers one `VelopackUpdateService` and exposes it through `IUpdateService`;
- `UpdateCheckWorker` owns background update checks;
- `DesktopMenuBar.razor` consumes the update service and has regression coverage for its reactive lifecycle;
- non-installed development launches report unsupported update behavior rather than pretending to be an installed Velopack application.

Recent Ponytail cleanup removed obsolete update settings/backend wrappers and corresponding appsettings entries. That was **consolidation**, not removal of desktop update behavior. Do not recreate the deleted wrappers unless a concrete second update backend/configuration source actually requires them.

The Velopack package ID remains `APS` and must be stable across releases.

## Authoritative verification before packaging

APS uses the shared self-hosted Windows Azure DevOps `EOS` agent and repository-owned [`../build/verify.ps1`](../build/verify.ps1) as the authoritative automated build/test contract.

Before a production release is treated as verified, the exact release SHA should have Windows evidence for:

1. solution restore;
2. full Release build;
3. every solution-registered test project;
4. self-contained `win-x64` DesktopHost publish smoke.

GitHub Actions/hosted CI are not substitutes for this Windows gate.

## Build a release

Run on Windows:

```powershell
pwsh build/release.ps1 -Version 1.0.0
```

The release script owns packaging/publish preparation. It runs the solution test gate unless `-SkipTests` is deliberately supplied for non-release local iteration.

A real production release must **not** use `-SkipTests` as its acceptance path.

The release sequence includes:

1. solution tests;
2. `APS.DesktopHost` publish;
3. Velopack `vpk pack` output under `build/Releases/<version>/`.

Runtime identifier/self-contained/ReadyToRun settings belong to `APS.DesktopHost.csproj`; avoid duplicating them in scripts.

If `-Version` is omitted, the script reads the desktop project version.

### Prerequisites

- Windows;
- .NET SDK matching `global.json` (currently 10.0.203 with latest-patch roll-forward);
- Velopack CLI:

```powershell
dotnet tool install -g vpk
```

## Publish a release

Publishing remains an explicit action after verified packaging. Example:

```powershell
vpk upload github `
  --repoUrl "https://github.com/bhadkamkar9snehil/APS" `
  --outputDir build/Releases/1.0.0 `
  --token "<token>" `
  --publish `
  --releaseName "APS Planner v1.0.0" `
  --tag "v1.0.0"
```

The Git tag, package version and desktop project version must refer to the same release.

Do not hard-code a “current published release” in this document; the GitHub Releases feed is runtime publishing authority.

## Persistent data

Velopack may replace the install directory during updates, so APS application data remains outside the install tree.

Current local application data is resolved through `LocalApplicationPaths`, including the self-contained SQLite database and logs. The normal data root is under:

```text
%LocalAppData%\APS-Data\
```

with `aps.db` in the data directory when no explicit APS connection string is configured.

The desktop host performs migration checks before starting hosted services so background services do not race database migration on startup.

## Release/runtime verification

For a production installer/update release, verify more than build/test:

- clean install or upgrade from an installed prior version;
- update discovery;
- download/prepared-update behavior;
- shutdown/restart and update application;
- resulting application/package version;
- Windows Installed Apps metadata;
- persistent database/log data survives install-directory replacement;
- migration/startup succeeds on the release database path;
- desktop window opens/responds;
- representative planner view loads;
- update menu correctly reflects supported/available/downloading/ready/error state;
- delta/full package behavior from the second published release onward.

When persistence/startup changes, create/retain the appropriate pre-launch database backup and run SQLite integrity/quick-check evidence as part of release QA.

## Latest recorded current-main evidence

For `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`, recorded Windows evidence includes:

- Release build 0 warnings/errors;
- 336/336 tests;
- self-contained `APS.DesktopHost.exe` publish;
- SQLite `PRAGMA quick_check: ok`;
- pre-launch backup;
- published desktop baseline opened and remained responsive.

This is evidence for that exact SHA only and does not replace verification of a later release commit.
