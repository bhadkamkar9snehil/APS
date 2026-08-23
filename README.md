# APS

Advanced Planning & Scheduling for integrated steel manufacturing.

## Current authority

The canonical product is the .NET solution under `src/`, and **`main` is the integrated product baseline**.

Current implementation/status authority:

1. [`docs/current/APS_CURRENT_STATE_2026-08-23.md`](docs/current/APS_CURRENT_STATE_2026-08-23.md) — exact integrated state, current backend program, release lifecycle, Gantt/UI consolidation and latest recorded Windows verification.
2. [`docs/current/README.md`](docs/current/README.md) — documentation authority/index.
3. [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md) — ordered remaining backend work.
4. [`docs/APS_End_to_End_Manufacturing_Planning_Flow.md`](docs/APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical manufacturing-planning causal flow.
5. [`docs/APS_Backend_Canonical_Path_Inventory.md`](docs/APS_Backend_Canonical_Path_Inventory.md) — authoritative production lifecycle and compatibility/demo boundaries.
6. [`docs/APS_Testing_Strategy.md`](docs/APS_Testing_Strategy.md) and [`docs/windows-ci.md`](docs/windows-ci.md) — verification/test contract.
7. [`docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md) and [`docs/current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](docs/current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — Gantt target and current implementation status.

Dated audits, reconnaissance reports and implementation plans are valuable historical inputs; they are **not current-state authority** when they disagree with current `main`.

## Canonical architecture

```text
APS.Domain
APS.Application
APS.Planning
APS.Infrastructure
APS.Service
APS.UI
APS.DesktopHost
```

The planning kernel is .NET/ASP.NET Core with OR-Tools CP-SAT finite scheduling, SQLite persistence, immutable Plan Versions, release/execution/replanning services, and a Blazor planner UI hosted in the Windows desktop application/service host.

The retired Python/workbook/Flask implementation and earlier UIs are not active runtime/build dependencies. Their last retained snapshot is available at tag `v0.2.5` for deliberate historical comparison.

## Canonical manufacturing-planning loop

```text
SAP SO item / MTS requirement
 -> qualified FG coverage
 -> MTO/MTS Production Order manufacturing requirement
 -> recursive BOM/material requirements
 -> time-phased inventory / incoming / WIP / planned-internal-supply coverage
 -> internal production requirement or explicit shortfall
 -> Campaign / grade-sequence / heat optimization
 -> configured ManufacturingRoute operations
 -> finite resource/material/thermal schedule
 -> immutable Plan Version
 -> release-readiness review
 -> Approved Plan Version
 -> persisted release to Work Orders / process operations
 -> execution actuals + material state
 -> bounded repair / replan through the same lifecycle
```

Demand/material requirement is causality. Campaign is manufacturing aggregation/optimization. A physical resource is an assignment, not the identity of the production requirement.

## Current product boundary

Current production code enforces a **manufacturing-only** APS boundary:

- current inventory is not the planning-horizon limit;
- known incoming, committed/released WIP and APS-planned internal production are time-phased supply;
- internally manufacturable uncovered demand creates upstream manufacturing requirements;
- non-manufacturable uncovered quantity remains explicit `Shortfall` / `NotManufacturableHere`;
- speculative BUY/TRANSFER/manual-supply planning is rejected by the production lifecycle. Authoritative purchased/transferred incoming material can still be consumed as known supply.

This is a current code-and-test contract. A future product-scope change must update code, acceptance tests and documentation together.

## Manufacturing routes and thermal behavior

`ManufacturingRoute` is authoritative. APS does not assume a fixed `EAF -> LRF -> VD -> CCM -> RHF -> RM` topology.

Implemented behavior includes:

- route-driven pre-CCM and downstream operations;
- conditional/required/forbidden VD semantics;
- no first-`HotRoll` architecture pivot;
- physical resource alternatives and independent same-type resource timelines;
- liquid-steel thermal constraints;
- time/temperature-aware billet thermal aging;
- direct hot charge when still eligible;
- configured reheating when thermal/order/route conditions require it;
- actual measured billet state taking precedence during replan;
- inventory decoupling without erasing valid upstream billet production.

## Plan lifecycle and release

The current persisted lifecycle includes:

```text
Draft -> Feasible -> Approved -> Released
```

Release is no longer a direct Feasible-to-Released transition. Approval and release readiness are evaluated from persisted Plan Version evidence, including material/supply evidence and persisted MTO service-completion checks. Release requires an active Approved Plan Version and repository persistence defends the same boundary.

## Current backend priority

#56 billet thermal planning is **complete and integrated**. The current primary backend issue is:

**#16 — late-bound resource assignment, commitment and operational redispatch.**

The ordered remaining program is maintained in [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md).

## Gantt / planner workbench

The large Gantt overhaul and subsequent hardening are integrated on `main`.

Post-overhaul Ponytail cleanup **consolidated implementation**, it did not intentionally remove the corresponding user behavior. Several small baseline/calendar/campaign/execution/proposal Razor layer files were folded into the canonical lane/viewport rendering path. The current `GanttResourceLane` still renders the baseline, calendar unavailability, campaign spans, execution actuals, markers/frozen fence, operations and staged proposals.

See [`docs/current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](docs/current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) for current behavior and remaining follow-ups.

## Repository structure

- `APS.slnx` — .NET solution
- `src/APS.Domain` — manufacturing/planning domain
- `src/APS.Application` — application contracts/orchestration
- `src/APS.Planning` — material/campaign/route/finite scheduling logic
- `src/APS.Infrastructure` — persistence, providers, lifecycle/read models
- `src/APS.Service` — ASP.NET Core host/API
- `src/APS.UI` — production Blazor UI/workbenches
- `src/APS.DesktopHost` — Windows desktop host
- `tests/APS.Architecture.Tests` — dependency/repository contracts
- `tests/APS.Infrastructure.Tests` — relational/provider/persistence contracts
- `tests/APS.Planning.Tests` — planning regression/acceptance tests
- `tests/APS.UI.Tests` — UI state/model/component contracts

## Verification

APS uses a **Windows-authoritative verification contract**.

`build/verify.ps1` is run on the shared self-hosted Windows Azure DevOps agent `EOS` (pool `Default`) and performs:

1. restore;
2. full Release solution build;
3. every registered `tests/*` project discovered from `APS.slnx`;
4. self-contained `win-x64` `APS.DesktopHost` publish smoke.

GitHub Actions or hosted CI are **not substitutes** for this APS Windows gate. Do not call a later SHA green from an older result.

The latest recorded verification for `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f` reports:

- Release build: **0 warnings, 0 errors**;
- **336/336 tests passed** — Architecture 9, Infrastructure 12, Planning 182, UI 133;
- self-contained Windows publish produced;
- SQLite `PRAGMA quick_check: ok`;
- live published desktop verification loaded 105 operations across 8 resources and exercised the Gantt/inspector/resource-load/capacity views with released-baseline editing correctly blocked.

See [`docs/windows-ci.md`](docs/windows-ci.md) for the executable verification contract.
