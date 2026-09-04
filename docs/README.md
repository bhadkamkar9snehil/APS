# APS Documentation

Documentation is classified by **authority and purpose**, not by filename age or words such as `FINAL`, `COMPLETE` or `STATUS`.

Canonical code authority is `main`. Historical documents are preserved, but they do not override current code or current-state documentation.

## 1. Start here — current implementation authority

1. [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md) — current integrated implementation snapshot and latest recorded exact Windows verification.
2. [`current/README.md`](current/README.md) — current/canonical documentation index.
3. [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) — ordered remaining backend program; current primary is #16.
4. [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md) — production planning/query/approval/release/execution authority.
5. [`APS_End_to_End_Manufacturing_Planning_Flow.md`](APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical demand→material→campaign→route→schedule→release→execution→replan causality.
6. [`APS_Steel_Domain_Architecture_Roadmap.md`](APS_Steel_Domain_Architecture_Roadmap.md) — current steel-domain architecture and remaining roadmap.
7. [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md) and [`windows-ci.md`](windows-ci.md) — test strategy and authoritative Windows verification contract.
8. [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — current Gantt/workbench implementation status after overhaul and Ponytail consolidation.

`main` code wins if a status document becomes stale.

## 1a. Live defect/verification logs

Dated logs of issues found by actually running the product, kept separate from status/target documents so a defect list doesn't get read as either. Current:

- [`APS_UI_Functional_Audit_2026-09-04.md`](APS_UI_Functional_Audit_2026-09-04.md) — UI defects and confirmed-working behavior, verified by driving the running app, not by reading source alone.

## 2. Current specifications / target contracts

These remain current target/semantic contracts. Some intentionally include behavior that is still incomplete; use the current-state/status documents to distinguish target from implementation.

- [`APS_Backend_Visibility_Contract.md`](APS_Backend_Visibility_Contract.md)
- [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md)
- [`APS_SO_PO_Campaign_Summary.md`](APS_SO_PO_Campaign_Summary.md)
- [`APS_SO_PO_Campaign_Service_Date_Examples.md`](APS_SO_PO_Campaign_Service_Date_Examples.md)
- [`APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md)
- [`APS_UI_UX_Product_Blueprint.md`](APS_UI_UX_Product_Blueprint.md)
- [`APS_UI_Implementation_Plan.md`](APS_UI_Implementation_Plan.md)

### Gantt requirements detail

The current Gantt requirements document separates normative requirements from historical starting-state observations. It explicitly incorporates the detailed requirement families from [`reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md`](reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md) while superseding that file's old branch/component/current-defect claims.

## 3. Historical stable-path pointers

Several old root paths are intentionally retained so GitHub issue/external links continue working. Their bodies now state that they are historical and link to the preserved original under `archive/`.

Examples:

- [`APS_Backend_Acceptance_Audit_2026-08-18.md`](APS_Backend_Acceptance_Audit_2026-08-18.md);
- [`APS_Backend_Audit_Remediation_Map.md`](APS_Backend_Audit_Remediation_Map.md);
- [`APS_Repository_Cleanup_and_Documentation_Archive_Plan.md`](APS_Repository_Cleanup_and_Documentation_Archive_Plan.md);
- [`APS_Repository_Cleanup_Manifest_2026-08-18.md`](APS_Repository_Cleanup_Manifest_2026-08-18.md);
- demand-orchestration gap/audit/checklist files from issue #45;
- [`current/APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md`](current/APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md);
- date-stamped [`superpowers/`](superpowers/) plans/specifications.

Do not use these pointers or their archived originals to claim that current `main` is missing work without tracing current code/status first.

## 4. Reference

[`reference/`](reference/) contains supporting non-authoritative material such as:

- DHTMLX behavioral research;
- detailed historical Gantt requirement derivation;
- earlier functional/product architecture thinking;
- workbook/Flask parity/API references;
- configuration/master-data references;
- legacy tuning material.

See [`reference/README.md`](reference/README.md).

A current canonical document may explicitly incorporate a reference's detailed definitions; otherwise current docs win on conflict.

## 5. Archive

[`archive/`](archive/) contains superseded historical material. The archive now explicitly separates:

- dated backend audits;
- completed demand-orchestration implementation material;
- superseded steel-domain roadmaps;
- pre-overhaul Gantt reconnaissance;
- date-stamped Superpowers implementation/design snapshots;
- repository-cleanup history;
- older API/design/UI/optimization/fix-plan history.

See [`archive/README.md`](archive/README.md).

Historical text is preserved intentionally, including old “current”, branch, issue and CI statements. **Archive means those statements are point-in-time evidence, not present-tense authority.**

## 6. Production-completion rule

A capability is complete only when its applicable production chain is coherent:

```text
Domain/master
 -> persistence/provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version evidence
 -> approval/release where applicable
 -> execution/replan where applicable
 -> typed read/API/UI consumption where applicable
```

A class, unit test, old API route, workbook sheet, UI button or historical completion report is not sufficient by itself.

## 7. Current UI status

The production Blazor UI is **substantially implemented**. It is no longer correct to describe APS as having only a future UI or only the Planning Sandbox.

Current `main` contains production planner/domain pages and the central Gantt/workbench. The gated `/demo/planning` sandbox remains explicitly non-authoritative.

Ponytail cleanup consolidated several UI/Gantt/theme/update wrappers. Deleted standalone Gantt layer files do **not** imply loss of baseline/calendar/campaign/execution/proposal behavior; current behavior is described in the Gantt implementation-status document.

## 8. Verification authority

APS uses a Windows-authoritative verification contract:

- shared self-hosted Azure DevOps agent `EOS`;
- repository-owned [`../build/verify.ps1`](../build/verify.ps1);
- full Release solution build;
- every solution-registered test project;
- self-contained `win-x64` DesktopHost publish smoke.

GitHub Actions/hosted CI are not substitutes for the APS Windows gate. Never call a later SHA green from an older run. See [`windows-ci.md`](windows-ci.md).

## 9. Retired implementation source

The Python/workbook/Flask prototype and earlier UIs are not active runtime/build source on current `main`. Their final retained source snapshot is available through Git history at tag `v0.2.5`.
