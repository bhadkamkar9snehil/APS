# Current APS Documentation

**Authority:** this index identifies the documents that currently define APS implementation state and target behavior.

Canonical code authority is `main`. The documentation re-baseline is based on the integrated executable snapshot `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`; subsequent commits on the documentation branch change documentation/classification only unless explicitly stated otherwise.

## Read first

1. [`APS_CURRENT_STATE_2026-08-23.md`](APS_CURRENT_STATE_2026-08-23.md) — **current implementation-state authority**: integrated branch lineage, backend completion/current issue, Plan Version approval/release, Gantt/UI consolidation and latest recorded Windows verification.
2. [`../APS_Backend_Work_Program.md`](../APS_Backend_Work_Program.md) — dependency-ordered remaining backend work; current primary is #16.
3. [`../APS_Backend_Canonical_Path_Inventory.md`](../APS_Backend_Canonical_Path_Inventory.md) — canonical production planning/query/approval/release/execution path and demo/compatibility boundaries.
4. [`../APS_End_to_End_Manufacturing_Planning_Flow.md`](../APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical manufacturing-planning causal flow.
5. [`../APS_Steel_Domain_Architecture_Roadmap.md`](../APS_Steel_Domain_Architecture_Roadmap.md) — current steel-domain architecture and remaining roadmap.
6. [`../APS_Testing_Strategy.md`](../APS_Testing_Strategy.md) — test ownership and acceptance strategy.
7. [`../windows-ci.md`](../windows-ci.md) — shared EOS Windows verification contract.
8. [`APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — current Gantt/workbench implementation status after overhaul and Ponytail consolidation.

## Current target/specification documents

These are current design/contract references. They can intentionally describe behavior not yet complete; use the current-state/status documents to distinguish target from implementation.

- [`../APS_Backend_Visibility_Contract.md`](../APS_Backend_Visibility_Contract.md)
- [`../APS_Demand_to_Production_Order_and_Due_Date_Model.md`](../APS_Demand_to_Production_Order_and_Due_Date_Model.md)
- [`../APS_SO_PO_Campaign_Summary.md`](../APS_SO_PO_Campaign_Summary.md)
- [`../APS_SO_PO_Campaign_Service_Date_Examples.md`](../APS_SO_PO_Campaign_Service_Date_Examples.md)
- [`../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md)
- [`../APS_UI_UX_Product_Blueprint.md`](../APS_UI_UX_Product_Blueprint.md)
- [`../APS_UI_Implementation_Plan.md`](../APS_UI_Implementation_Plan.md)

The current Gantt requirements contract incorporates the detailed requirement families from the preserved 22-Aug full specification while treating its old branch/current-defect/file-layout observations as historical starting evidence only.

## Current implementation facts that older documents often get wrong

- PR #69 / `integrate/current-workstream` is merged; `main` is the baseline.
- `codex/gantt-workbench-overhaul` and Ponytail cleanup work are contained by `main`.
- #56 billet thermal aging/reheat/actual-state replan behavior is complete and integrated.
- #16 late-bound resource commitment/redispatch is the current primary backend issue.
- Plan lifecycle includes persisted `Approved`; release requires active Approved + persisted readiness.
- persisted release readiness includes material/supply evidence and MTO service-completion evidence.
- the production Blazor UI/Gantt are substantially implemented; UI is not wholly future/deferred.
- post-overhaul component deletions were primarily consolidation: baseline/calendar/campaign/execution/proposal Gantt behavior remains in the canonical rendering path.
- allocation-level service obligations preserve PO quantity/date/priority through shared physical production; the old “everything inherits campaign earliest due date” limitation is historical.
- planning is time-phased: zero opening inventory does not suppress future manufacturing when qualified incoming or planned internal material can become available later.
- the authoritative verification environment is the shared Windows Azure DevOps `EOS` agent using `build/verify.ps1`; the old blanket “do not use CI” instruction is obsolete.

## Dated/historical inputs

The following remain useful but are **not live implementation-state truth**:

- [`../APS_Backend_Acceptance_Audit_2026-08-18.md`](../APS_Backend_Acceptance_Audit_2026-08-18.md) — stable historical pointer; full original under `archive/backend-audits/`;
- [`../APS_Backend_Audit_Remediation_Map.md`](../APS_Backend_Audit_Remediation_Map.md) — historical audit-to-issue map;
- demand-orchestration gap/audit/checklist stable paths from #45 — originals under `archive/demand-orchestration/`;
- repository-cleanup plan/manifest/checklist — originals under `archive/repository-cleanup/`;
- [`APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md`](APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md) — stable pointer to the archived pre-overhaul reconnaissance;
- [`../superpowers/`](../superpowers/) date-stamped plans/specs — stable historical pointers; originals under `archive/superpowers/`;
- the old steel-domain roadmap snapshot — preserved under `archive/domain-roadmaps/`.

Do not use a dated report to reopen or duplicate work without tracing current `main` first.

## Reference material

[`../reference/README.md`](../reference/README.md) classifies supporting benchmark/legacy/reference documents. Reference material may be explicitly incorporated by a current canonical contract, but otherwise current code/current docs win on conflict.

## Historical source

The retired Python/workbook/Flask implementation and older UIs are available for deliberate historical comparison at tag `v0.2.5`; they are not active product source/runtime authority.

## Verification rule

Use the exact Windows verification result for the exact executable commit being claimed green. The shared self-hosted `EOS` Windows agent and repository-owned `build/verify.ps1` are the authoritative automated gate. GitHub Actions/hosted CI are not substitutes.
