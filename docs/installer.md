# Installer & Auto-Update (Velopack)

Status: **implemented, not yet released**. The mechanism is fully wired but no version has been
packed/installed/published yet — see "Current state" below.

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
3. `vpk pack` on the publish output, producing a `Setup.exe` installer plus delta update packages
   in `build/Releases/`.

If `-Version` is omitted, the script reads `<Version>` from the `APS.DesktopHost.csproj` instead.

The installer registers **APS Planner** in Windows Installed Apps, creates Start Menu and Desktop
shortcuts, and installs per-user; application binaries land under the Velopack-managed install
directory while persistent data (SQL connection is external, but logs) lives under
`%LocalAppData%\APS\Data`.

**Prerequisite**: the Velopack CLI must be installed once per machine:

```powershell
dotnet tool install -g vpk
```

For tags matching `v*.*.*`, `.github/workflows/release.yml` downloads the preceding Velopack
release, packages the new version, and publishes it with `vpk upload github`. Tag and
`APS.DesktopHost.csproj` `<Version>` must match, or the workflow fails fast rather than publishing
a mismatched release.

## Current state

- Local `build/release.ps1` has not been run yet on this machine (no `Setup.exe` has been produced).
- No version has been tagged or pushed, so `.github/workflows/release.yml` has never run and no
  GitHub Release exists yet.
- The app has never been installed via the installer — every launch so far has been a dev build run
  directly from `bin/`, which is why update checks report `Unsupported`.
- No `app-icon.ico` exists yet; `vpk pack` runs without `--icon` and falls back to a default icon
  until one is added.

## Release verification

Once a release exists: verify from an installed preceding version — discover, download, close and
reopen before applying, confirm the prepared update is recovered, restart, and verify the new
version in APS Planner, executable metadata, and Windows Installed Apps. Exercise delta and full
-package fallback for the second release onward (the first release has no prior version to diff
against).
