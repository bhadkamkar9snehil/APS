# APS Documentation

APS contains the current .NET production architecture together with legacy Python/workbook material retained as migration and reference evidence.

## Canonical backend/domain references

For current backend/domain/planning work, read these **in order**:

1. **`APS_Backend_Acceptance_Audit_2026-08-18.md`** — comprehensive current-state audit: implemented strengths, partial/inconsistent chains, false-positive done states, architecture gaps, steel-domain flexibility, BOM/material, observability, validation and backend completion definition.
2. **`APS_End_to_End_Manufacturing_Planning_Flow.md`** — canonical causal flow from SAP demand/MTS through manufacturing requirement, recursive BOM, time-phased material, Campaigns, route/schedule, WOs, actual material and replan.
3. **`APS_Demand_to_Production_Order_and_Due_Date_Model.md`** — canonical SO-item -> qualified FG coverage -> MTO Production Order and required-date/service semantics.
4. **`APS_Backend_Work_Program.md`** — authoritative implementation order, issue-specification standard, status legend, dependency/acceptance matrix and one-issue-at-a-time execution discipline.
5. **`APS_Backend_Audit_Remediation_Map.md`** — maps audit findings to GitHub remediation issues and dependencies.
6. **`APS_Backend_Visibility_Contract.md`** — exhaustive catalog of backend facts, tables/read models, diagnostics, controls and planner/master levers that must become queryable before dependent UI work.
7. **`APS_Steel_Domain_Architecture_Roadmap.md`** — steel-domain architecture and planning/solver direction.
8. **`dotnet-planning-core.md`** — implementation note describing the .NET planning-core represented by the development branch; subordinate to the canonical audit/work-program documents where they differ.

Governing GitHub trackers:

- **Issue #2** — domain-true steel APS product/domain epic.
- **Issue #37** — backend acceptance-audit remediation/canonicalization epic.
- **Issue #44** — final end-to-end manufacturing-planning acceptance gate.
- **Issue #47** — one-primary-issue-at-a-time implementation governance.
- **Issue #49** — backend dependency/completion-evidence index.
- **Issue #46** — repository documentation cleanup/archive pass.

## Backend acceptance rule

A class/model existing is not sufficient. A capability is complete only when its **applicable** production chain is coherent:

```text
Domain/master
 -> SQL persistence
 -> authoritative provider/import
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version audit
 -> execution/replanning where relevant
 -> read model
 -> API/application exposure
```

If a layer genuinely does not apply, the owning issue must say why.

## Manufacturing-only product boundary

APS plans manufacturing. It may consume qualified inventory, authoritative known incoming material, released/committed internal production and APS-planned internal production.

For uncovered material:

```text
internally manufacturable?
  yes -> create upstream internal production requirement and recurse through BOM
  no  -> explicit Shortfall / NotManufacturableHere
```

APS does **not** recommend procurement or transfer actions. Purchased/transferred material already present in authoritative inventory/incoming integration is treated only as a known supply fact.

## Current backend implementation order

The canonical order is maintained in `APS_Backend_Work_Program.md` and GitHub Issue #47.

### Phase 0 — repository authority
1. #46 repository cleanup/document classification.

### Phase 1 — canonical boundaries and demand
2. #38 one authoritative planning/query/execution path and explicit demo isolation.
3. #45 MTO demand orchestration and allocation-level service dates.

### Phase 2 — material requirements
4. #33 recursive BOM/material-requirement graph.
5. #14 one time-phased material ledger/reservation engine.
6. #11 billet/known incoming/SMS-down contingency through #14.

### Phase 3 — Campaigns/routes/finite schedule/scenarios
7. #15 Campaign/grade-sequence/heat candidate optimization.
8. #34 route-driven manufacturing topology.
9. #9 thermal/superheat/transfer constraints.
10. #35 resource scheduling modes.
11. #16 late-binding resource assignment/commitment/redispatch.
12. #17 operating-state scenarios/outages using the same canonical planner.

### Phase 4 — execution/explanation
13. #18 complete execution/material genealogy.
14. #19 planner-grade diagnostics.

### Phase 5 — decision services/visibility
15. #43 CTP/scenario/capacity convergence.
16. #36 complete backend read/command surface.

Cross-cutting gates #39 (master wiring), #40 (logging), #41 (validation), #42 (effective rule consistency), #32 (operational/material fidelity) and #44 (end-to-end acceptance) are satisfied incrementally while implementing the active primary issue; they are not separate parallel redesign programs.

## Verification process rule

**Do not use GitHub Actions or CI for APS project verification.** Build/test/runtime verification will be performed later in the intended developer environment. Backend issues must not use GitHub Actions status as acceptance evidence.

Focused tests should still be written as implementation work proceeds; they are executed later in the intended environment.

## Production UI/UX references

UI design material remains useful, but dependent production UI implementation waits for authoritative backend visibility/end-to-end readiness.

- **`APS_UI_UX_Product_Blueprint.md`** — information architecture, planner workflows, entity inspector, plan lifecycle and interaction model.
- **`APS_UI_Implementation_Plan.md`** — dependency-ordered UI implementation plan and backend-to-UI coverage matrix.
- GitHub **Issue #20** — production UI/UX epic, with child issues #21-#31.

The backend audit/work program is authoritative for planning/domain behavior. A UI screen must never invent missing planning logic or move BOM/material/service/resource calculations into Razor/JavaScript to compensate for an incomplete backend.

## Existing prototype/reference documentation

The repository also contains earlier architecture/prototype documents such as:

- `APS_Functional_Concept_Guide.md`
- `APS_Gap_Analysis_vs_AVEVA.md`
- `APS_Generic_Architecture_And_Scenario_Workbench.md`
- `APS_Implementation_Plan_Config_Masters.md`
- `APS_Planning_Views_And_Visibility_Design.md`
- `APS_Roadmap_Industry_Agnostic.md`
- `APS Design Philosophy`
- `APS Fix Plan 1`

These are not automatically authoritative. Issue #46 will classify every substantive document as Canonical, Current Implementation Note, Reference or Archive and move/banner stale material accordingly.

## Legacy Python/workbook status

The Python/workbook prototype remains a **reference and migration source**. It contains valuable behavior such as recursive BOM explosion, CTP and capacity analysis that must be preserved where appropriate while migrating to the canonical .NET architecture.

Production direction is the .NET architecture under `src/`. Legacy Python must not become a hidden parallel production planner.

The existing Blazor `/planning` page is a reference/demo sandbox. Production UI eventually consumes dedicated application/query contracts and persisted Plan Version truth.