# Current APS Documentation

**Authority:** this index identifies the documents that currently define APS implementation state and target behavior.

Canonical code authority is `main`. As of this re-baseline, the integrated implementation snapshot is `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`.

## Read first

1. [`APS_CURRENT_STATE_2026-08-23.md`](APS_CURRENT_STATE_2026-08-23.md) — **current implementation-state authority**: integrated branch lineage, backend completion/current issue, Plan Version approval/release, Gantt/UI consolidation and latest recorded Windows verification.
2. [`../APS_Backend_Work_Program.md`](../APS_Backend_Work_Program.md) — dependency-ordered remaining backend work; current primary is #16, not #56.
3. [`../APS_Backend_Canonical_Path_Inventory.md`](../APS_Backend_Canonical_Path_Inventory.md) — canonical production planning/query/release/execution path and demo/compatibility boundaries.
4. [`../APS_End_to_End_Manufacturing_Planning_Flow.md`](../APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical manufacturing-planning causal flow.
5. [`../APS_Testing_Strategy.md`](../APS_Testing_Strategy.md) — test ownership and acceptance strategy.
6. [`../windows-ci.md`](../windows-ci.md) — shared EOS Windows verification contract.
7. [`APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — current Gantt/workbench implementation status.

## Current target/specification documents

These are current design/contract references. They can intentionally describe behavior not yet complete; use the current-state document to distinguish target from implementation.

- [`../APS_Backend_Visibility_Contract.md`](../APS_Backend_Visibility_Contract.md)
- [`../APS_Demand_to_Production_Order_and_Due_Date_Model.md`](../APS_Demand_to_Production_Order_and_Due_Date_Model.md)
- [`../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md)
- [`../APS_UI_UX_Product_Blueprint.md`](../APS_UI_UX_Product_Blueprint.md)
- [`../APS_UI_Implementation_Plan.md`](../APS_UI_Implementation_Plan.md)
- [`../APS_Steel_Domain_Architecture_Roadmap.md`](../APS_Steel_Domain_Architecture_Roadmap.md)

## Current implementation facts that older documents often get wrong

- PR #69 / `integrate/current-workstream` is merged; `main` is the baseline.
- `codex/gantt-workbench-overhaul` and Ponytail cleanup branches are contained by `main`.
- #56 billet thermal aging/reheat/replan behavior is complete and integrated.
- #16 late-bound resource commitment/redispatch is the current primary backend issue.
- Plan lifecycle now includes a persisted `Approved` state; release requires active Approved + readiness.
- persisted release readiness includes material/supply evidence and MTO service-completion evidence.
- the production Blazor UI/Gantt are substantially implemented; UI is not wholly “future/deferred.”
- post-overhaul component deletions were mainly consolidation: Gantt baseline/calendar/campaign/execution/proposal behavior still exists in the canonical lane/viewport path.
- the authoritative verification environment is the shared Windows Azure DevOps `EOS` agent using `build/verify.ps1`; the stale blanket statement “do not use CI” is no longer correct.

## Dated/historical inputs

The following remain useful but are **not live implementation-state truth**:

- [`../APS_Backend_Acceptance_Audit_2026-08-18.md`](../APS_Backend_Acceptance_Audit_2026-08-18.md) — dated audit baseline;
- [`../APS_Backend_Audit_Remediation_Map.md`](../APS_Backend_Audit_Remediation_Map.md) — audit-to-remediation mapping from that period;
- demand-orchestration gap/checklist documents written while #45 was open;
- repository cleanup plans/manifests dated 18-Aug;
- [`APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md`](APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md) — pre-overhaul reconnaissance baseline;
- `../superpowers/plans/*` and `../superpowers/specs/*` — implementation/design history unless explicitly promoted by a current authority document.

Do not use a dated report to reopen or duplicate work without tracing current `main` first.

## Historical source

The retired Python/workbook implementation and older UIs are available for deliberate historical comparison at tag `v0.2.5`; they are not production authority.

## Verification rule

Use the exact Windows verification result for the exact commit being claimed green. The shared self-hosted `EOS` Windows agent and repository-owned `build/verify.ps1` are the authoritative automated gate. GitHub Actions/hosted CI are not substitutes.
