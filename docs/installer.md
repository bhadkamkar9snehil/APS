# Installer & Auto-Update (Velopack)

Status: **implemented and operational**. APS desktop releases are packaged locally and published to
GitHub Releases through the verified manual workflow below.

## What's in place

- **Package**: `Velopack` referenced in `src/APS.DesktopHost/APS.DesktopHost.csproj`.
- **Startup hook**: `Program.Main` calls `VelopackApp.Build().Run()` before constructing the WPF
  `App`, per Velopack's integration contract. This intercepts the special command-line invocations
  Velopack uses during install/uninstall/update (e.g. creating shortcuts) and exits early when one
  is detected — it must run before any other app code.
- **Update source**: `VelopackUpdateBackend` uses `GithubSource` against the public APS GitHub
  Releases repository configured in `UpdateSettings.RepositoryUrl`
  (`https://github.com/bhadkamkar9snehil/APS`).
- **Update lifecycle**: `UpdateCheckWorker` checks when the host starts, then roughly hourly with
  jitter while APS Planner is open. Feed failures back off to two, four, then six hours. Checks
  never download automatically. `IUpdateService` exposes immutable state; `MainLayout`'s footer
  shows it and lets the user trigger Download / Restart.
- **Explicit actions**: an available release remains available until Download is clicked. A
  prepared release survives application restarts through Velopack's `UpdatePendingRestart` marker.
  Restart uses `WaitExitThenApplyUpdates`, then closes APS Planner normally before files are
  replaced.
- **Development builds**: non-installed launches (running the .exe directly from `bin/`) report
  `Unsupported` and do not contact GitHub — confirmed by the startup log line "Skipping update
  checks because APS Planner is not running from a Velopack installation."

## Building a release

Run `build/release.ps1` from Windows (win-x64 publish + `vpk pack` both require Windows):

```powershell
pwsh build/release.ps1 -Version 1.0.0
```

It runs, in order:
1. `dotnet test` on `tests/APS.Planning.Tests` — aborts on any failure.
2. `dotnet publish` of `APS.DesktopHost.csproj` for `win-x64`, self-contained,
   `PublishReadyToRun=true`.
3. `vpk pack` on the publish output, producing a `Setup.exe` installer and update package in the
   clean version-specific directory `build/Releases/<version>/`.

The version-specific directory is recreated for every build. Packages from an older or mistakenly
higher version therefore cannot contaminate the current Velopack feed or block a valid package.

If `-Version` is omitted, the script reads `<Version>` from the `APS.DesktopHost.csproj` instead.

The installer registers **APS Planner** in Windows Installed Apps, creates Start Menu and Desktop
shortcuts, and installs per-user; application binaries land under the Velopack-managed install
directory while persistent data (SQL connection is external, but logs) lives under
`%LocalAppData%\APS\Data`.

**Prerequisite**: the Velopack CLI must be installed once per machine:

```powershell
dotnet tool install -g vpk
```

**Publishing is manual and local — no GitHub Actions.** Per explicit repository policy, GitHub
Actions (a paid feature) is never used for this project, for verification or for releases. After
`build/release.ps1` produces the packages in `build/Releases/<version>/`; publish them by running
`vpk upload github` directly from a Windows machine with the Velopack CLI and a GitHub token with
`repo` scope:

```powershell
vpk upload github `
  --repoUrl "https://github.com/bhadkamkar9snehil/APS" `
  --outputDir build/Releases/1.0.0 `
  --token "<token>" `
  --publish `
  --releaseName "APS Planner v1.0.0" `
  --tag "v1.0.0"
```

Tag and `APS.DesktopHost.csproj` `<Version>` should match by convention, but nothing enforces this
automatically since there is no CI step — check it by hand before publishing.

## Versioning policy

`src/APS.DesktopHost/APS.DesktopHost.csproj` is the sole application-version authority. Use
semantic versioning against the most recent published desktop release:

- patch (`0.3.0` to `0.3.1`) for compatible fixes and small refinements;
- minor (`0.3.x` to `0.4.0`) for substantial new planner capabilities or workflows;
- major (`0.x` to `1.0.0`, then `1.x` to `2.0.0`) only for a declared stable milestone or a
  compatibility-breaking product change.

The tag must be `v<Version>`, point at the exact commit used to build the assets, and match the
published GitHub release. Historical prototypes use `archive/*` tags so they cannot be mistaken for
the active desktop release lineage.

## Current state

- v0.3.0 is the current desktop release, built in `build/Releases/0.3.0/`, installed locally, and
  published through the manual GitHub release workflow.
- The active desktop release lineage is v0.1.0 through v0.3.0. Earlier prototype milestones are
  retained only under `archive/*` tags.
- `src/APS.DesktopHost/Assets/app-icon.ico` exists (7-size multi-res PNG-in-ICO, generated
  programmatically) and is wired into the `.exe`, the WPF window/taskbar icon, and
  `build/release.ps1`'s `vpk pack --icon`.

## Release verification

Once a release exists: verify from an installed preceding version — discover, download, close and
reopen before applying, confirm the prepared update is recovered, restart, and verify the new
version in APS Planner, executable metadata, and Windows Installed Apps. Exercise delta and full
-package fallback for the second release onward (the first release has no prior version to diff
against).
