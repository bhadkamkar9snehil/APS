# Installer & Auto-Update (Velopack)

APS desktop releases are built and packaged locally with Velopack. GitHub Actions is not used as the APS verification or release pipeline.

## Runtime update path

- `Program.Main` calls `VelopackApp.Build().Run()` before constructing the WPF application.
- `App.xaml.cs` constructs `VelopackUpdateService` with a `GithubSource` for the public APS repository.
- `UpdateCheckWorker` checks at startup and then roughly hourly, with jitter and exponential backoff after feed failures.
- Update checks do not download automatically.
- `IUpdateService` exposes update state and explicit download/apply operations to the UI.
- Non-installed development launches report `Unsupported` and do not poll the GitHub release feed.

The repository URL is owned by the desktop composition root because that is its only runtime consumer. The Velopack package id is fixed as `APS` by the release script and must remain stable across releases.

## Build a release

Run on Windows:

```powershell
pwsh build/release.ps1 -Version 1.0.0
```

The script performs:

1. `dotnet test APS.slnx` and aborts on failure unless `-SkipTests` is explicitly supplied for local iteration.
2. `dotnet publish` of `APS.DesktopHost.csproj`.
3. `vpk pack`, producing installer/update assets in `build/Releases/<version>/`.

Runtime identifier, self-contained publishing and ReadyToRun are owned by `APS.DesktopHost.csproj`; the release script does not duplicate those settings.

If `-Version` is omitted, the script reads `<Version>` from `APS.DesktopHost.csproj`.

### Prerequisites

- .NET 10 SDK
- Windows
- Velopack CLI:

```powershell
dotnet tool install -g vpk
```

## Publish the release

Publishing is manual and local. After `build/release.ps1` succeeds, upload the generated release directory with Velopack and a GitHub token with the required repository permission, for example:

```powershell
vpk upload github `
  --repoUrl "https://github.com/bhadkamkar9snehil/APS" `
  --outputDir build/Releases/1.0.0 `
  --token "<token>" `
  --publish `
  --releaseName "APS Planner v1.0.0" `
  --tag "v1.0.0"
```

The tag, package version and `APS.DesktopHost.csproj` version must describe the same release.

## Persistent data

Velopack owns the application install directory and may replace it during updates. Persistent APS data therefore lives outside that tree under the `LocalApplicationPaths` location:

```text
%LocalAppData%\APS-Data\Data\
```

The self-contained APS SQLite database is `aps.db` in that data directory when no explicit APS connection string is configured. Logs live under its `logs` child directory.

## Versioning

`src/APS.DesktopHost/APS.DesktopHost.csproj` is the application-version authority. Use semantic versioning:

- patch for compatible fixes and small refinements;
- minor for substantial planner capabilities/workflows;
- major only for a declared stable milestone or compatibility-breaking product change.

Release tags use `v<Version>` and must point at the exact commit used to build the published assets. Historical prototypes use `archive/*` tags and are not part of the active release lineage.

Do not hard-code a “current published release” in this document; the GitHub Releases feed is the authority for what is currently published.

## Release verification

For a real release, verify from an installed preceding version:

- update discovery;
- download and prepared-update persistence;
- application shutdown/restart and update application;
- resulting application version and Windows Installed Apps metadata;
- delta/full-package behavior from the second published release onward.

The repository policy remains: build/test/runtime verification is performed in the intended developer environment, not inferred from GitHub Actions status.
