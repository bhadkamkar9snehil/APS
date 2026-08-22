# APS Windows CI

APS uses the shared self-hosted Windows Azure DevOps agent registered in the `EOS` Azure DevOps project.

## What runs automatically

`azure-pipelines.yml` is intentionally branch-agnostic:

- pushes to any branch are eligible;
- pull requests targeting any branch are eligible;
- redundant queued push builds for the same branch are batched;
- superseded PR builds are cancelled;
- every run gets a clean Azure Pipelines workspace and clean Git checkout.

The job targets:

- pool: `Default`
- agent demand: `Agent.Name -equals EOS`

A single self-hosted agent naturally serializes EOS and APS Windows jobs, preventing two repositories from mutating the same working directory at the same time.

## Verification contract

`build/verify.ps1` is the authoritative non-release verification path. It performs:

1. `dotnet restore APS.slnx`
2. full Release build of `APS.slnx`
3. `APS.Planning.Tests`
4. `APS.UI.Tests`
5. self-contained `win-x64` publish smoke test of `APS.DesktopHost`

The pipeline publishes TRX test results, the desktop publish output, the compiler/build log, .NET SDK information and Git context.

## SDK

`global.json` pins .NET SDK `10.0.203` with `latestPatch` roll-forward. This removes ambiguity between local development and the shared Windows build VM.

## Release packaging is separate

`build/release.ps1` remains an explicit release action. Continuous CI does not run `vpk pack` and therefore cannot accidentally create a distributable release from an arbitrary feature branch.

## Building an arbitrary branch manually

The EOS Azure DevOps project also owns a manual pipeline named **Windows Build Lab**. Select:

- repository: `APS`
- ref: any branch, tag, full Git ref, or commit SHA

The Build Lab clones that exact ref into a disposable temp workspace on the same Windows VM and runs the same build/test/publish-smoke contract. This is the fallback for long-lived branches that predate `azure-pipelines.yml` or for one-off commit verification.
