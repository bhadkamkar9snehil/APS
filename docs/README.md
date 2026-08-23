# APS Documentation

Documentation is classified by **authority and purpose**, not merely by age.

## Current implementation authority

Start here:

1. [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md) — current integrated `main` state and latest recorded Windows verification.
2. [`current/README.md`](current/README.md) — current documentation index.
3. [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) — ordered remaining backend program.
4. [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md) — production lifecycle/path authority.
5. [`APS_End_to_End_Manufacturing_Planning_Flow.md`](APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical causal flow.
6. [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md) and [`windows-ci.md`](windows-ci.md) — test and Windows verification contract.
7. [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — current Gantt implementation status.

`main` is the code authority. A current-state document cannot override behavior that the current production call path does not implement.

## Current specifications and target contracts

These define intended or still-evolving behavior and remain useful even when implementation is incomplete:

- [`APS_Backend_Visibility_Contract.md`](APS_Backend_Visibility_Contract.md)
- [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md)
- [`APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md)
- [`APS_UI_UX_Product_Blueprint.md`](APS_UI_UX_Product_Blueprint.md)
- [`APS_UI_Implementation_Plan.md`](APS_UI_Implementation_Plan.md)
- [`APS_Steel_Domain_Architecture_Roadmap.md`](APS_Steel_Domain_Architecture_Roadmap.md)

A specification describes the target. Use the current-state document and code to determine whether a target is already implemented.

## Dated audits and implementation history

The following kinds of files are **point-in-time evidence**, not live status authority:

- `APS_Backend_Acceptance_Audit_2026-08-18.md`;
- `APS_Backend_Audit_Remediation_Map.md` when it describes the 18-Aug audit state;
- demand-orchestration gap/checklist files written while #45 was open;
- repository cleanup plans/manifests dated 18-Aug;
- `current/APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md` — retained as the pre-overhaul Gantt baseline;
- `superpowers/plans/*` and `superpowers/specs/*`;
- branch-specific completion reports.

These documents are still valuable for rationale, acceptance intent and provenance. They must not be cited to claim a feature is currently absent or unfinished without checking current `main`.

## Reference and archive

[`reference/`](reference/) contains useful non-authoritative design/reference material. [`archive/`](archive/) contains superseded historical material.

A filename such as `COMPLETE`, `FINAL`, `SUMMARY` or `STATUS` does not make a document authoritative. Its classification and date matter.

The retired Python/workbook/Flask implementation and earlier UIs are historical. Their final retained snapshot is available at tag `v0.2.5`.

## Product/backend completion rule

A capability is complete only when its applicable production chain is coherent:

```text
Domain/master
 -> persistence/provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version evidence
 -> release/execution/replan where applicable
 -> typed read/API/UI consumption where applicable
```

A class, test fixture, old API route, workbook sheet or historical completion report is not sufficient by itself.

## UI status

The production Blazor UI is **already substantially implemented**. It is no longer correct to describe all UI work as future/deferred. Current `main` contains the planner shell, finite-schedule/Gantt workbench, analysis/inspector/resource-load/capacity surfaces and multiple domain workspaces.

The gated `/demo/planning` sandbox remains a demo path and must not be confused with the production planner lifecycle.

## Verification authority

APS uses a Windows-authoritative verification contract:

- shared self-hosted Azure DevOps agent `EOS`;
- repository-owned [`../build/verify.ps1`](../build/verify.ps1);
- Release build;
- all solution-registered test projects;
- self-contained `win-x64` DesktopHost publish smoke.

GitHub Actions or hosted CI are **not substitutes** for the APS Windows gate. See [`windows-ci.md`](windows-ci.md).
