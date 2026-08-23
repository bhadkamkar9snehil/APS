# APS Windows Verification

APS uses the shared self-hosted Windows Azure DevOps agent registered in the `EOS` Azure DevOps project.

This is the **authoritative automated APS build/test environment**. GitHub Actions or hosted CI may be useful for other purposes, but they are not substitutes for this Windows verification contract.

## Shared worker

The APS pipeline targets:

- Azure DevOps project: `EOS`;
- pool: `Default`;
- agent demand: `Agent.Name -equals EOS`.

The self-hosted Windows VM/agent is shared with EOS. A single agent naturally serializes jobs and keeps the authoritative Windows environment consistent.

The VM also has an out-of-band Azure control path in the EOS project for VM/agent recovery; APS verification itself remains repository-owned through the scripts described below.

## Automatic APS pipeline

`azure-pipelines.yml` is branch-agnostic:

- pushes to any branch are eligible;
- pull requests targeting any branch are eligible;
- redundant queued builds may be batched/cancelled according to pipeline settings;
- each run gets a clean workspace and checkout;
- build/test/publish behavior delegates to the repository contract rather than duplicating commands in Azure YAML.

## Authoritative verification contract

[`../build/verify.ps1`](../build/verify.ps1) performs:

1. `dotnet restore APS.slnx`;
2. full Release build of `APS.slnx`;
3. discovery of every `tests/*` project registered in `APS.slnx`;
4. execution of each registered test project with TRX output;
5. self-contained `win-x64` publish smoke of `APS.DesktopHost`;
6. diagnostic capture including SDK/Git/run context and build log.

This solution-driven discovery is deliberate: a new test project must be registered in `APS.slnx`; once registered, it automatically becomes part of the Windows gate.

## SDK

`global.json` pins .NET SDK `10.0.203` with `latestPatch` roll-forward. The exact checked-out ref owns its SDK contract.

## Manual arbitrary-ref verification

The EOS Azure DevOps project owns a manual pipeline named **Windows Build Lab**.

Select:

- repository: `APS`;
- ref: branch, tag, full Git ref or exact commit SHA.

The Build Lab clones the requested ref into a disposable temp workspace on the same Windows worker. Modern APS refs reuse their own `build/verify.ps1`; older historical refs can use the Build Lab compatibility fallback.

Use this path when an exact branch/SHA needs authoritative Windows verification without changing the normal APS integration flow.

## Release packaging

[`../build/release.ps1`](../build/release.ps1) is separate from continuous verification. Normal APS CI does not create a distributable Velopack release from arbitrary feature branches.

A production release must preserve the complete test gate. `-SkipTests` is an inner-loop developer option only and is not acceptable production release evidence.

## Green-claim rule

Do not say a commit is verified merely because:

- a previous SHA passed;
- local compilation succeeded;
- GitHub status is empty/green;
- static review found no errors;
- a different machine ran a subset of tests.

A green claim should identify the **exact SHA** and inspect the Windows verification output/evidence for that SHA.

## Latest recorded baseline evidence

For `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`, the recorded 23-Aug-2026 Windows verification reports:

- Release build: **0 warnings, 0 errors**;
- tests: **336/336 passed**;
  - Architecture: 9;
  - Infrastructure: 12;
  - Planning: 182;
  - UI: 133;
- self-contained Windows `APS.DesktopHost.exe` publish produced;
- SQLite `PRAGMA quick_check`: `ok`;
- pre-launch database backup created;
- live published desktop loaded the released execution baseline;
- 105 operations and 8 resources rendered;
- Gantt, operation inspector, resource-load and capacity views exercised;
- released-baseline editing correctly blocked;
- final desktop process remained open and responsive.

The first four build/test/publish items are directly aligned with the repository verification contract. Database/live desktop checks are additional recorded Windows release/runtime evidence and should be repeated when later changes materially affect startup, persistence or UI runtime behavior.

## Relationship to historical documentation

Older APS documents often say **“Do not use GitHub Actions or CI for APS project verification.”** That wording predates the shared EOS Windows pipeline and is obsolete.

The correct rule is:

> **Do not substitute GitHub Actions or hosted CI for the authoritative APS Windows verification contract. Use the shared EOS Windows agent / Windows Build Lab and the repository-owned `build/verify.ps1` contract.**
