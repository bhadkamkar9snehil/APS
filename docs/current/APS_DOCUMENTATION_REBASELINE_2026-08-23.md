# APS Documentation Re-baseline Manifest — 2026-08-23

**Status:** current documentation-governance record  
**Executable baseline reviewed:** `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`  
**Documentation branch:** `docs/rebaseline-main-20260823`

## Purpose

This pass removed documentation drift after the large backend/Gantt/UI integration, .NET/Ponytail hardening and persisted release-readiness work.

The goal was not to rewrite history. It was to make the repository answer three questions unambiguously:

1. What is implemented now?
2. What remains a target/requirement?
3. Which documents are historical evidence only?

## Current-state authority created/updated

- `README.md` — current product/architecture/verification entry point.
- `docs/README.md` — repository documentation authority index.
- `docs/current/README.md` — current/canonical index.
- `docs/current/APS_CURRENT_STATE_2026-08-23.md` — integrated implementation snapshot.
- `docs/APS_Backend_Work_Program.md` — current remaining backend sequence; #16 is primary.
- `docs/APS_Backend_Canonical_Path_Inventory.md` — canonical lifecycle including persisted approval/readiness/release.
- `docs/APS_End_to_End_Manufacturing_Planning_Flow.md` — re-baselined full causal flow.
- `docs/APS_Steel_Domain_Architecture_Roadmap.md` — rewritten from current steel-domain foundations rather than the old EAF/LRF/VD/UI gap snapshot.
- `docs/current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md` — current post-overhaul/post-Ponytail Gantt state.
- `docs/APS_Testing_Strategy.md` — current test ownership/acceptance matrix.
- `docs/windows-ci.md` — authoritative EOS Windows verification contract.

## Current target/specification documents updated

- `docs/APS_Backend_Visibility_Contract.md`
- `docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md`
- `docs/APS_SO_PO_Campaign_Summary.md`
- `docs/APS_SO_PO_Campaign_Service_Date_Examples.md`
- `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`
- `docs/APS_UI_UX_Product_Blueprint.md`
- `docs/APS_UI_Implementation_Plan.md`
- `docs/dotnet-planning-core.md`
- `docs/installer.md`

Key corrections include:

- #56 is complete/integrated; #16 is current primary.
- allocation-level quantity/date/priority service obligations are implemented; the old “all shared work inherits campaign earliest due date” limitation is historical.
- planning is time-phased and is not limited to opening inventory.
- Plan lifecycle includes persisted `Approved` and release readiness.
- production Blazor UI/Gantt already exists; the repository no longer describes it as wholly future/sandbox-only.
- Ponytail deleted redundant Gantt/theme/update wrappers without intentionally deleting the represented behavior.
- the old blanket “do not use CI” rule is superseded by the shared self-hosted Windows EOS verification contract; GitHub Actions remain non-authoritative.

## Historical documents preserved and demoted

The following originals were preserved byte-for-byte under `docs/archive/` while their stable paths were converted to historical pointers where links needed to remain valid:

### Backend audit history

- `archive/backend-audits/APS_Backend_Acceptance_Audit_2026-08-18.md`
- `archive/backend-audits/APS_Backend_Audit_Remediation_Map.md`

### Demand-orchestration history

- `archive/demand-orchestration/APS_Demand_Orchestration_Acceptance_Scenarios.md`
- `archive/demand-orchestration/APS_Demand_Orchestration_Gap_and_Implementation_Checklist.md`
- `archive/demand-orchestration/APS_Demand_Service_Date_Implementation_Map.md`
- `archive/demand-orchestration/APS_Due_Date_and_Campaign_Clubbing_Audit.md`

### Repository-cleanup history

- `archive/repository-cleanup/APS_Repository_Cleanup_and_Documentation_Archive_Plan.md`
- `archive/repository-cleanup/APS_Repository_Cleanup_Manifest_2026-08-18.md`
- `archive/repository-cleanup/APS_Repo_Cleanup_Acceptance_Scenarios.md`

### Steel-domain history

- `archive/domain-roadmaps/APS_Steel_Domain_Architecture_Roadmap_pre_2026-08-23.md`

### Gantt history

- `archive/gantt/APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md`
- the full detailed 22-Aug Gantt requirements/start-state audit is additionally retained at `reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md`; the current canonical Gantt requirements document incorporates its detailed requirement families while superseding its old current-state/file-layout claims.

### Superpowers history

Full date-stamped originals are under:

- `archive/superpowers/plans/`
- `archive/superpowers/specs/`

The stable `docs/superpowers/...` paths now identify themselves as historical pointers.

## Reference/archive index corrections

- `docs/reference/README.md` now correctly states that the retired Python/workbook/Flask implementation is not active source in current `main`; use tag `v0.2.5` for the retained historical source snapshot.
- `docs/archive/README.md` now lists the new audit/demand/roadmap/Gantt/Superpowers/repository-cleanup archive categories and explains that historical “current”/branch/CI wording remains point-in-time evidence only.
- `docs/superpowers/README.md` classifies the dated plans/specs explicitly.

## Verification doctrine after re-baseline

Current wording is:

> APS executable verification is authoritative only when performed on the shared self-hosted Windows Azure DevOps `EOS` agent (or its Windows Build Lab path) using repository-owned `build/verify.ps1`, with evidence inspected for the exact executable SHA being claimed green. GitHub Actions/hosted CI are not substitutes.

The latest recorded executable verification remains tied to `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`; this documentation-only re-baseline does not manufacture a new executable verification result.

## Diff-scope audit before integration

The branch comparison against `main` showed:

- `ahead_by = 48` at the pre-manifest audit point;
- `behind_by = 0`;
- every changed path was `README.md` or under `docs/`;
- no `src/`, `tests/`, `build/`, pipeline, project, solution or runtime file was changed.

This manifest itself is documentation-only and does not change that scope.

## Authority rule after this pass

When documents conflict:

1. current `main` code/persisted contracts;
2. exact-SHA Windows verification evidence for build/test/runtime claims;
3. `current/APS_CURRENT_STATE_2026-08-23.md`;
4. current work program/canonical architecture contracts;
5. current target specifications;
6. dated audits/reference/archive material.

Do not reopen completed work from an archived audit without tracing current `main` first.
