# APS

Advanced Planning & Scheduling for integrated steel manufacturing.

## Current product direction

The canonical production architecture is the .NET solution under `src/`:

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
4. [`docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md`](docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md)
5. [`docs/APS_Backend_Visibility_Contract.md`](docs/APS_Backend_Visibility_Contract.md)
6. [`docs/APS_Steel_Domain_Architecture_Roadmap.md`](docs/APS_Steel_Domain_Architecture_Roadmap.md)
7. [`docs/dotnet-planning-core.md`](docs/dotnet-planning-core.md) — current implementation note, subordinate to the architecture/audit documents above.

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
Inventory / known incoming / committed or planned internal production
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
time-phased inventory / incoming / WIP / planned-supply netting
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
Plan Version
        ↓
Work Orders / process operations
        ↓
execution actuals + material genealogy
        ↓
current inventory/WIP/remaining demand
        ↓
local repair / replan
```

Demand/material requirement is causality. Campaign is manufacturing aggregation/optimization. Resource is an assignment, not the identity of the production requirement. Work Orders are downstream of the solved production graph.

## Backend implementation order

Backend work proceeds one primary issue at a time. The canonical sequence is defined in [`docs/APS_Backend_Work_Program.md`](docs/APS_Backend_Work_Program.md) and GitHub Issue #47.

Current phase order:

1. repository/document authority;
2. canonical backend path + MTO demand orchestration;
3. recursive BOM + one time-phased material ledger;
4. Campaign/route/thermal/capacity/resource-flexibility planning;
5. execution/material genealogy + diagnostics;
6. CTP/scenario/capacity convergence + complete backend visibility;
7. final end-to-end acceptance.

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
