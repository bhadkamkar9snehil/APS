# APS

Advanced Planning & Scheduling for integrated steel manufacturing.

## Current product direction

The canonical production architecture is the .NET solution under `src/`, and **`main` is the integration branch and canonical code snapshot**:

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

The older Python/workbook/Flask implementation remains in this repository because it contains useful proven behavior and migration/parity material, including recursive BOM, CTP, capacity and workbook-era planning flows. It is **legacy/reference implementation**, not the canonical production architecture.

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

See [`docs/APS_Repository_Cleanup_Manifest_2026-08-18.md`](docs/APS_Repository_Cleanup_Manifest_2026-08-18.md) for the document classification/move manifest.

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

For each hard-compatible requirement group, the planner evaluates candidate campaign partitions against explicit service and manufacturing economics, including:

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

## Manufacturing routes and operational flexibility

The configured `ManufacturingRoute` is the authoritative process sequence. APS does **not** assume one fixed integrated-steel topology such as `EAF → LRF → VD → CCM → RHF → RM`.

### Steelmaking and VD

Pre-CCM operations are driven by the configured route plus grade/order requirements. VD is therefore simple and explicit:

- a grade/order that **requires VD** includes it;
- a grade/order for which VD is **optional** can skip it;
- a grade/order that **forbids VD** cannot be routed through it.

The same principle applies to other configured steelmaking/refining operations: their presence comes from route/master-data semantics, not a hard-coded whitelist.

### Rolling and reheating

There is no special architectural split at the first HotRoll operation. A rolling requirement is a quantity/allocation anchor from cast-intermediate input to the required final section; the physical downstream chain is projected directly from the ordered configured route.

Examples of valid configured paths include:

```text
CCM → HotRoll
CCM → Reheat → HotRoll
CCM → HotRoll → ColdRoll → Finish
CCM → HotRoll → Reheat → HotRoll
billet inventory → Reheat → HotRoll
```

Operations may also exist before the first HotRoll if the route requires them.

**Hot charge is preferred, not forced.** Fresh or explicitly known-hot billet can go directly to an eligible mill when grade/order policy allows it and a physical hot-transfer path is available. This saves reheating energy, time and capacity.

**Reheating is conditional, not universal.** Yard/cold billet, a route/order that explicitly requires reheating, a prohibition on direct hot charge, or a loss of guaranteed hot continuity selects the configured Reheat operation. If reheating is required but no eligible reheating resource exists, the plan reports a named infeasibility rather than inventing a furnace or silently bypassing the requirement.

An inventory/decoupling point deliberately breaks guaranteed hot continuity. The billet remains valid material supply, but a later HotRoll must re-establish thermal readiness through the configured route.

### Planning versus execution

APS deliberately separates the upstream need to make billets from the later operational choice of which billet reaches a mill. A downstream mill outage should not conceptually erase the upstream manufacturing requirement when the plant must continue producing cast intermediate material.

The route model now supports that decoupled architecture, while the remaining execution-time behavior is completed in the follow-up layers:

- **#56** — time/temperature-aware billet state, hot/cold aging and reheating decision fidelity;
- **#16** — late-binding resource assignment, commitment and operational redispatch;
- **#18** — execution/material genealogy and actual-state closure.

This distinction is intentional: planning describes valid manufacturing alternatives and constraints; execution/replanning selects among still-valid alternatives using the actual plant/material state.

### Persisted/readback behavior

Every included downstream route step—including the first HotRoll—is represented as a `RouteOperationPlan`, scheduled against eligible physical resources, persisted in the Plan Version, released as the corresponding Work Order/process operation, and exposed through the rolling/finishing read model. Skipped optional/forbidden route decisions retain reason codes so the UI does not reconstruct a fixed plant diagram.

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

Backend work proceeds one primary issue at a time. GitHub Issue #47 is the authoritative live execution order; [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md) is the repository work-program document.

Completed foundations include canonical repository/path cleanup, MTO demand orchestration, recursive BOM, time-phased material coverage, known-incoming material handling, route-driven pre-CCM topology, downstream route projection without a first-HotRoll pivot, liquid-steel thermal constraints, resource scheduling modes, operating-state scenarios, and candidate Campaign/grade-sequence/heat optimization.

After #58, the primary sequence continues with **#56 billet thermal planning**, then **#16 late-binding/redispatch**, **#18 execution/genealogy**, **#19 diagnostics**, **#57 scenario/material comparison**, and the remaining decision/read/configuration/reference acceptance work per #47.

UI implementation remains dependent on backend truth/read-model readiness rather than filling missing planning behavior client-side.

## Repository structure

### Canonical .NET direction

- `APS.slnx` — .NET solution
- `src/APS.Domain` — manufacturing/planning domain model
- `src/APS.Application` — application contracts/orchestration
- `src/APS.Planning` — Campaign/material/route/finite-scheduling logic
- `src/APS.Infrastructure` — persistence/providers/read models
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

Do not infer production authority from the fact that a legacy file remains executable or that an old divergent Git branch is still retained for history. **`main` is the integrated product truth.** Feature branches should be short-lived and merged back to `main`; genuinely divergent historical lineages are archival/reference material until deliberately reconciled.

## Verification rule

**Do not use GitHub Actions or CI for APS project verification.**

Focused tests should be written in the repository, but build/test/runtime verification is performed later in the intended developer environment. Do not claim current HEAD is green unless that local verification has actually been run.
