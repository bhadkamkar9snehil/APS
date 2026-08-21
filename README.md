# APS

Advanced Planning & Scheduling for integrated steel manufacturing.

## Current product direction

The canonical production architecture is the .NET solution under `src/`, and `main` is the integration branch for the current product code:

```text
APS.Domain
APS.Application
APS.Planning
APS.Infrastructure
APS.Integrations
APS.Service
APS.UI
```

The planning kernel is .NET/ASP.NET Core with OR-Tools CP-SAT for finite scheduling, SQL Server persistence/integration infrastructure, Plan Version audit/replanning, and Blazor for the application host/reference UI.

The older Python/workbook/Flask implementation remains in this repository because it contains valuable proven behavior and migration/parity material, including recursive BOM, CTP, capacity and workbook-era planning flows. It is **legacy/reference implementation**, not the canonical production architecture.

## Start here

Read [`docs/current/README.md`](docs/current/README.md).

The primary backend documents are:

1. [`docs/APS_Backend_Acceptance_Audit_2026-08-18.md`](docs/APS_Backend_Acceptance_Audit_2026-08-18.md)
2. [`docs/APS_End_to_End_Manufacturing_Planning_Flow.md`](docs/APS_End_to_End_Manufacturing_Planning_Flow.md)
3. [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md)
4. [`docs/APS_Backend_Canonical_Path_Inventory.md`](docs/APS_Backend_Canonical_Path_Inventory.md) — authoritative production planning/query/release/execution lifecycle and demo/compatibility classifications.
5. [`docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md`](docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md)
6. [`docs/APS_Backend_Visibility_Contract.md`](docs/APS_Backend_Visibility_Contract.md)
7. [`docs/APS_Steel_Domain_Architecture_Roadmap.md`](docs/APS_Steel_Domain_Architecture_Roadmap.md)
8. [`docs/dotnet-planning-core.md`](docs/dotnet-planning-core.md) — current implementation note, subordinate to the architecture/audit documents above.

Documentation authority is deliberately separated:

```text
docs/current/README.md    current/canonical index
docs/reference/           useful non-authoritative legacy/reference material
docs/archive/             superseded historical material
```

See [`docs/APS_Repository_Cleanup_Manifest_2026-08-18.md`](docs/APS_Repository_Cleanup_Manifest_2026-08-18.md) for the exact classification/move manifest.

## Product boundary

APS is a **manufacturing planner**.

For any requirement:

```text
Requirement
   ↓
Inventory / known incoming / committed or APS-planned internal production
   ↓
Remaining requirement
   ↓
Can this plant manufacture it internally?
   ├─ YES → create upstream internal production requirement
   └─ NO  → expose SHORTFALL / NotManufacturableHere
```

APS does not recommend procurement or inter-plant transfer actions. Purchased/transferred material already present in authoritative inventory/incoming integration is simply treated as known supply.

## Canonical manufacturing-planning loop

```text
SAP SO item / MTS requirement
        ↓
qualified FG coverage
        ↓
MTO/MTS Production Order manufacturing requirement
        ↓
recursive BOM/material requirements
        ↓
time-phased inventory / incoming / WIP / planned-internal-supply netting
        ↓
internal production requirements or explicit shortfall
        ↓
Campaign optimization
        ↓
grade sequence + heats
        ↓
configured ManufacturingRoute operations
        ↓
finite resource/material/thermal schedule
        ↓
immutable Plan Version
        ↓
identity-only release from persisted plan truth
        ↓
Work Orders / process operations
        ↓
execution actuals + material genealogy
        ↓
current inventory/WIP/remaining demand
        ↓
local repair / replan through the same lifecycle
```

Demand/material requirement is causality. Campaign is manufacturing aggregation/optimization. Resource is an assignment, not the identity of the production requirement. Work Orders are downstream of the solved production graph.

## Campaign optimization

Campaign composition is not production-authoritative sort-and-fill.

For each hard-compatible requirement group, the planner now evaluates candidate campaign partitions against explicit service and manufacturing economics, including:

- allocation-level due-date/priority obligations;
- early-production cost and campaign setup/utilization;
- furnace-feasible heat envelopes and heat-target deviation;
- effective grade-transition prohibitions, transition time and penalties;
- required downstream route/resource feasibility;
- deterministic grade-sequence selection;
- MTO/MTS residual-heat economics;
- replan stability against persisted PO-to-Campaign quantity membership.

The baseline Plan Version contributes a **soft** campaign-stability objective during replan. Hard technical feasibility and customer-service dominance can still change the grouping. New or removed demand is excluded from the stability movement metric.

Candidate/objective evidence is retained in `CampaignCompositionDecision` and carried into Plan Version planning assumptions for later explanation and comparison.

## Canonical production lifecycle

Production APIs do not independently run campaign, structure, scheduling and release logic. The production path is:

```text
IPlanningLifecycleService
 -> authoritative master/inventory providers
 -> IPlanningEngine (Production mode)
 -> IPlanVersionRepository
 -> IPlannerWorkspaceQueryService
 -> IPersistedPlanReleaseService
 -> canonical execution services
 -> IReplanningActualStateProvider
 -> IPlanningLifecycleService.ReplanAsync
```

Component-level calculation APIs and the direct-kernel Blazor sandbox are demo-only, disabled by default and isolated under `/api/demo/planning/*` and `/demo/planning`.

## Backend implementation order

Backend work proceeds one primary issue at a time. GitHub Issue #47 is the authoritative live execution order; [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md) is the repository work-program document and should be kept aligned when repository documentation changes are authorized.

Completed foundations include canonical repository/path cleanup, MTO demand orchestration, recursive BOM, time-phased material coverage, known-incoming material handling, route-driven pre-CCM topology, liquid-steel thermal constraints, resource scheduling modes, operating-state scenarios, and candidate Campaign/grade-sequence/heat optimization (#15).

The next primary backend issue after #15 is **#58 — remove the first-HotRoll architectural pivot from downstream route projection**, followed by billet thermal planning (#56), late-binding/redispatch (#16), execution/genealogy (#18), diagnostics (#19), scenario/material comparison (#57), decision/read services and remaining configuration/reference acceptance work per #47.

UI implementation remains dependent on backend truth/read-model readiness rather than filling missing planning behavior client-side.

## Repository structure

### Canonical .NET direction

- `APS.slnx` — .NET solution
- `src/APS.Domain` — manufacturing/planning domain model
- `src/APS.Application` — application contracts/orchestration
- `src/APS.Planning` — Campaign/material/production-structure/finite-scheduling logic
- `src/APS.Infrastructure` — persistence/providers
- `src/APS.Integrations` — MES/integration adapters
- `src/APS.Service` — ASP.NET Core/Blazor host and service API
- `src/APS.UI` — Blazor feature pages/components
- `tests/APS.Planning.Tests` — .NET planning regression/acceptance tests

### Legacy/reference implementation retained for migration/parity

Examples include:

- `engine/`
- `xaps_application_api.py`
- `run_all.py`
- `aps_functions.py`
- workbook-oriented tools/scripts
- `APS_BF_SMS_RM.xlsx`
- earlier static/React UI assets

Do not infer production authority from the fact that a legacy file remains executable.

## Verification rule

**Do not use GitHub Actions or CI for APS project verification.**

Focused tests should be written in the repository, but build/test/runtime verification is performed later in the intended developer environment. Do not claim current HEAD is green unless that local verification has actually been run.
