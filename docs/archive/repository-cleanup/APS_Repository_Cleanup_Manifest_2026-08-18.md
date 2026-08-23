# APS Repository Cleanup Manifest — 2026-08-18

**Issue:** #46  
**Scope:** documentation authority and repository navigation only. No backend planning behavior is changed by this cleanup.

## Cleanup decision

To preserve stable GitHub issue/external links, canonical/current documents remain at the `docs/` root. `docs/current/README.md` is the authoritative current-document index.

Non-authoritative documents are physically separated:

- `docs/reference/` — useful legacy/migration/reference material;
- `docs/archive/` — superseded historical material.

Executable source is **not moved** in this pass.

---

## Current / authoritative documents — keep at existing paths

| Path | Classification | Reason |
|---|---|---|
| `docs/APS_Backend_Acceptance_Audit_2026-08-18.md` | Canonical | Current backend implementation/audit truth |
| `docs/APS_Backend_Audit_Remediation_Map.md` | Canonical | Audit finding -> issue/dependency map |
| `docs/APS_Backend_Visibility_Contract.md` | Canonical | Backend read/command/visibility contract |
| `docs/APS_Backend_Work_Program.md` | Canonical | Ordered one-primary-issue-at-a-time program |
| `docs/APS_End_to_End_Manufacturing_Planning_Flow.md` | Canonical | Demand -> material -> manufacture -> execution -> replan causal flow |
| `docs/APS_Demand_to_Production_Order_and_Due_Date_Model.md` | Canonical | SO/PO/service-date model |
| `docs/APS_Steel_Domain_Architecture_Roadmap.md` | Canonical | Current steel-domain architecture/solver direction |
| `docs/dotnet-planning-core.md` | Current implementation note | Describes current .NET planning-core implementation; subordinate to canonical docs |
| `docs/APS_Demand_Orchestration_Acceptance_Scenarios.md` | Current supporting spec | Issue #45 acceptance scenarios |
| `docs/APS_Demand_Orchestration_Gap_and_Implementation_Checklist.md` | Current supporting spec | Issue #45 implementation seam/checklist |
| `docs/APS_Demand_Service_Date_Implementation_Map.md` | Current supporting spec | Service-date implementation mapping |
| `docs/APS_Due_Date_and_Campaign_Clubbing_Audit.md` | Current supporting spec | Campaign/date audit feeding current issues |
| `docs/APS_SO_PO_Campaign_Service_Date_Examples.md` | Current supporting spec | Current service-date examples |
| `docs/APS_SO_PO_Campaign_Summary.md` | Current supporting spec | Current SO/PO/Campaign summary |
| `docs/APS_Repository_Cleanup_and_Documentation_Archive_Plan.md` | Current governance | #46 cleanup plan |
| `docs/APS_Repository_Current_Reference_Archive_Classification_Rules.md` | Current governance | Current authority classification rules |
| `docs/APS_Repo_Cleanup_Acceptance_Scenarios.md` | Current governance | #46 acceptance scenarios |
| `docs/APS_Repository_Cleanup_Manifest_2026-08-18.md` | Current governance | This cleanup inventory/manifest |
| `docs/APS_UI_UX_Product_Blueprint.md` | Current future-product design | Canonical UI/product design, implementation deferred |
| `docs/APS_UI_Implementation_Plan.md` | Current future-product design | UI implementation plan, dependent on backend readiness |

---

## Moved to reference

| Old path | New path | Classification | Why retained |
|---|---|---|---|
| `docs/APS Design Philosophy` | `docs/reference/APS_Design_Philosophy.md` | Reference | Earlier design philosophy; useful context, not current authority |
| `docs/APS_Excel_CRUD_API.md` | `docs/reference/legacy-api/APS_Excel_CRUD_API.md` | Reference | Workbook-era CRUD/API behavior for migration/parity |
| `docs/APS_Functional_Concept_Guide.md` | `docs/reference/APS_Functional_Concept_Guide.md` | Reference | Earlier functional model useful for historical context |
| `docs/APS_Gap_Analysis_vs_AVEVA.md` | `docs/reference/APS_Gap_Analysis_vs_AVEVA.md` | Reference | Competitive/gap analysis, not current architecture contract |
| `docs/APS_Generic_Architecture_And_Scenario_Workbench.md` | `docs/reference/APS_Generic_Architecture_And_Scenario_Workbench.md` | Reference | Earlier generic architecture/scenario ideas |
| `docs/APS_Implementation_Plan_Config_Masters.md` | `docs/reference/legacy-configuration/APS_Implementation_Plan_Config_Masters.md` | Reference | Earlier configuration-master design useful during migration |
| `docs/APS_Roadmap_Industry_Agnostic.md` | `docs/reference/APS_Roadmap_Industry_Agnostic.md` | Reference | Earlier generic roadmap; current steel-domain roadmap supersedes product authority |
| `docs/api/CONFIG_API_REFERENCE.md` | `docs/reference/legacy-api/CONFIG_API_REFERENCE.md` | Reference | Legacy configuration API reference |
| `docs/system/ALGORITHM_CONFIG_SHEET_TEMPLATE.md` | `docs/reference/legacy-configuration/ALGORITHM_CONFIG_SHEET_TEMPLATE.md` | Reference | Workbook-era configuration reference |
| `docs/system/CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md` | `docs/reference/legacy-configuration/CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md` | Reference | Earlier configuration-driven implementation ideas |
| `docs/system/CONFIGURATION_SYSTEM_PACKAGE.md` | `docs/reference/legacy-configuration/CONFIGURATION_SYSTEM_PACKAGE.md` | Reference | Legacy configuration package reference |
| `docs/system/CONFIGURATION_SYSTEM_SUMMARY.md` | `docs/reference/legacy-configuration/CONFIGURATION_SYSTEM_SUMMARY.md` | Reference | Legacy configuration summary |
| `docs/reference/PARAMETER_TUNING_GUIDE.md` | unchanged | Reference | Existing legacy tuning guide |

---

## Moved to archive

### Root historical documents

| Old path | New path | Classification | Superseded by / reason |
|---|---|---|---|
| `docs/APS Fix Plan 1` | `docs/archive/fix-plans/APS_Fix_Plan_1.md` | Archive | Historical fix plan; current audit/work program supersede it |
| `docs/APS_Planning_Views_And_Visibility_Design.md` | `docs/archive/ui/APS_Planning_Views_And_Visibility_Design.md` | Archive | Superseded by backend visibility contract + current UI blueprint |
| `docs/XAPS_UI_TO_API_MATRIX.md` | `docs/archive/ui/XAPS_UI_TO_API_MATRIX.md` | Archive | Workbook/static-UI API mapping no longer authoritative |
| `docs/README_SHORT.txt` | `docs/archive/misc/README_SHORT.txt` | Archive | Low-information historical note |
| `docs/wiggly-kindling-lynx.md` | `docs/archive/misc/wiggly-kindling-lynx.md` | Archive | Orphaned historical planning note with no current authority |

### Legacy analysis

| Old path | New path |
|---|---|
| `docs/analysis/API_FILE_STATUS_AND_LINEAGE.md` | `docs/archive/legacy-analysis/API_FILE_STATUS_AND_LINEAGE.md` |
| `docs/analysis/AUDIT_REPORT_FINAL.md` | `docs/archive/legacy-analysis/AUDIT_REPORT_FINAL.md` |
| `docs/analysis/CAMPAIGN_LOGIC_ANALYSIS.md` | `docs/archive/legacy-analysis/CAMPAIGN_LOGIC_ANALYSIS.md` |
| `docs/analysis/EXCEL_PARAMS_UNUSED_ANALYSIS.txt` | `docs/archive/legacy-analysis/EXCEL_PARAMS_UNUSED_ANALYSIS.txt` |
| `docs/analysis/HARDCODED_RULES_AUDIT.md` | `docs/archive/legacy-analysis/HARDCODED_RULES_AUDIT.md` |
| `docs/analysis/PYTHON_SCRIPTS_AUDIT.md` | `docs/archive/legacy-analysis/PYTHON_SCRIPTS_AUDIT.md` |
| `docs/analysis/SIMPLIFICATION_AUDIT.md` | `docs/archive/legacy-analysis/SIMPLIFICATION_AUDIT.md` |

These are retained as historical audit evidence. `APS_Backend_Acceptance_Audit_2026-08-18.md` is current authority.

### Legacy API consolidation

| Old path | New path |
|---|---|
| `docs/api/API_COMPARISON_QUICK_REFERENCE.md` | `docs/archive/legacy-api/API_COMPARISON_QUICK_REFERENCE.md` |
| `docs/api/API_CONSOLIDATION_ANALYSIS.md` | `docs/archive/legacy-api/API_CONSOLIDATION_ANALYSIS.md` |
| `docs/api/API_CONSOLIDATION_VERIFICATION_CHECKLIST.md` | `docs/archive/legacy-api/API_CONSOLIDATION_VERIFICATION_CHECKLIST.md` |

These describe an older API surface and are not production contracts.

### Legacy design/schedule/UI analysis

| Old path | New path |
|---|---|
| `docs/design/CARD_HEIGHT_OPTIMIZATION.md` | `docs/archive/ui/CARD_HEIGHT_OPTIMIZATION.md` |
| `docs/design/DATA_WIRING_VERIFICATION.md` | `docs/archive/legacy-design/DATA_WIRING_VERIFICATION.md` |
| `docs/design/DESIGN_SYSTEM_STANDARDIZATION.md` | `docs/archive/ui/DESIGN_SYSTEM_STANDARDIZATION.md` |
| `docs/design/SCHEDULE_LOGIC_ANALYSIS.md` | `docs/archive/legacy-design/SCHEDULE_LOGIC_ANALYSIS.md` |
| `docs/design/UI_LAYOUT_IMPROVEMENTS.md` | `docs/archive/ui/UI_LAYOUT_IMPROVEMENTS.md` |

Current backend audit/work program and UI blueprint supersede these as authority.

### Legacy implementation-phase reports

| Old path | New path |
|---|---|
| `docs/implementation/IMPLEMENTATION_SUMMARY.md` | `docs/archive/legacy-implementation/IMPLEMENTATION_SUMMARY.md` |
| `docs/implementation/INTEGRATION_CHECKLIST.md` | `docs/archive/legacy-implementation/INTEGRATION_CHECKLIST.md` |
| `docs/implementation/PHASE1_2_SUMMARY.md` | `docs/archive/legacy-implementation/PHASE1_2_SUMMARY.md` |
| `docs/implementation/PHASE1_COMPLETION_REPORT.md` | `docs/archive/legacy-implementation/PHASE1_COMPLETION_REPORT.md` |
| `docs/implementation/PHASE2_PROGRESS.md` | `docs/archive/legacy-implementation/PHASE2_PROGRESS.md` |
| `docs/implementation/PHASE2_REFACTORING_ROADMAP.md` | `docs/archive/legacy-implementation/PHASE2_REFACTORING_ROADMAP.md` |
| `docs/implementation/PHASE3_TESTING_PLAN.md` | `docs/archive/legacy-implementation/PHASE3_TESTING_PLAN.md` |
| `docs/implementation/PHASE_CLOSURE_SUMMARY.md` | `docs/archive/legacy-implementation/PHASE_CLOSURE_SUMMARY.md` |
| `docs/implementation/QUICK_WINS_IMPLEMENTATION.md` | `docs/archive/legacy-implementation/QUICK_WINS_IMPLEMENTATION.md` |

These are historical milestone reports and must not be interpreted as current completion evidence.

### Legacy optimization/fix summaries

| Old path | New path |
|---|---|
| `docs/optimization/CONSISTENCY_IMPROVEMENTS_COMPLETE.md` | `docs/archive/legacy-optimization/CONSISTENCY_IMPROVEMENTS_COMPLETE.md` |
| `docs/optimization/FIXES_SUMMARY.md` | `docs/archive/legacy-optimization/FIXES_SUMMARY.md` |
| `docs/optimization/MASTER_DATA_OPTIMIZATION_COMPLETE.md` | `docs/archive/legacy-optimization/MASTER_DATA_OPTIMIZATION_COMPLETE.md` |
| `docs/optimization/MASTER_DATA_OPTIMIZATION_SUMMARY.md` | `docs/archive/legacy-optimization/MASTER_DATA_OPTIMIZATION_SUMMARY.md` |

The word `COMPLETE` in these filenames is historical and does not indicate current backend acceptance status.

---

## Executable/source classification — superseded by v0.2.6 retirement

| Repository area | Classification after cleanup |
|---|---|
| `src/APS.Domain`, `src/APS.Application`, `src/APS.Planning`, `src/APS.Infrastructure`, `src/APS.Integrations`, `src/APS.Service`, `src/APS.UI` | Production-direction .NET architecture |
| `tests/APS.Planning.Tests` | .NET planning regression/acceptance tests |
| retired Python/workbook application, workbook and Python tests/tools | Removed from the active tree in v0.2.6; final snapshot at tag `v0.2.5` |
| earlier static/React UI prototypes and root artifact archive | Removed from the active tree in v0.2.6; final snapshot at tag `v0.2.5` |

Issue #46 itself did not relocate executable source. The later v0.2.6 cleanup deliberately retired the now-independent legacy implementation after the .NET solution became authoritative.

---

## Authority after cleanup

```text
README.md
  -> docs/README.md
       -> docs/current/README.md     CURRENT AUTHORITY INDEX
       -> docs/reference/README.md   NON-AUTHORITATIVE REFERENCE
       -> docs/archive/README.md     SUPERSEDED HISTORY
```

## Acceptance evidence

- current product/backend truth can be found from the root README in under one minute;
- old Python/workbook behavior remains recoverable from tag `v0.2.5` but is absent from the active tree;
- stale `COMPLETE`, phase, optimization, API and UI documents no longer sit beside canonical documents;
- current GitHub issue links to canonical root documents remain valid because current file paths were preserved;
- no backend implementation code changed;
- GitHub Actions/CI remains outside APS project verification.
