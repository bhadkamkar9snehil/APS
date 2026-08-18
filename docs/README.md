# APS Documentation

APS contains both the original prototype documentation and the production .NET planning-core work.

## Canonical backend/domain references

For current backend/domain/planning work, start here **in this order**:

1. **`APS_Backend_Acceptance_Audit_2026-08-18.md`** — canonical current-state audit: implemented strengths, partial chains, false-positive done states, architecture inconsistencies, steel-domain flexibility gaps, BOM/material findings, observability/validation findings and backend completion definition.
2. **`APS_Backend_Audit_Remediation_Map.md`** — maps every audit finding to GitHub issues, dependencies and completion tests.
3. **`APS_Backend_Visibility_Contract.md`** — exhaustive catalog of backend facts, tables/read models, diagnostics, controls and planner/master levers that must be queryable for future UI.
4. **`APS_Steel_Domain_Architecture_Roadmap.md`** — canonical steel-domain architecture and planning/solver direction.
5. **`dotnet-planning-core.md`** — detailed description of the .NET planning-core implementation represented by the current development branch.
6. GitHub **Issue #2** — steel-domain product/domain epic.
7. GitHub **Issue #37** — governing backend acceptance-audit remediation/canonicalization epic.

### Backend acceptance rule

A class/model existing is not sufficient. A capability is complete only when its applicable chain is coherent:

```text
Domain/master
 -> SQL persistence
 -> master provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version audit
 -> execution/replanning where relevant
 -> read model
 -> API/application exposure
```

### Verification process rule

**Do not use GitHub Actions or CI for APS project verification.** Build/test/runtime verification will be performed later in the intended development environment. Backend issues must not use GitHub Actions status as acceptance evidence.

## Production UI/UX references

For production UI/UX development, start here:

- **`APS_UI_UX_Product_Blueprint.md`** — canonical information architecture, planner workflows, entity-inspector model, plan lifecycle, screen specifications, visualization policy and interaction safety rules.
- **`APS_UI_Implementation_Plan.md`** — dependency-ordered implementation plan, query/read-model requirements, component boundaries, backend-to-UI coverage matrix, routes and testing strategy.
- GitHub **Issue #20** — governing production UI/UX epic, with child issues #21-#31.

The backend audit and steel-domain roadmap remain authoritative for planning/domain behavior. The UI blueprint is authoritative for how completed backend behavior is exposed to users. A screen must not invent missing planning behavior or move calculations into Razor/JavaScript merely to complete the interface.

## Current development priority

Backend truth comes first.

1. close the findings under backend audit Epic #37;
2. complete full recursive BOM/time-phased supply/resource flexibility/thermal/campaign/diagnostic work at the canonical backend source;
3. complete backend query/read/command visibility contract (#36);
4. only then proceed with the dependent production UI workspaces.

The rule is **backend truth first, complete visibility immediately behind it**. Every important implemented backend capability should acquire a deliberate read/command surface; dependent screens do not ship ahead of missing authoritative contracts.

## Existing prototype/reference documentation

The repository also contains earlier architecture and prototype documents, including:

- `APS_Functional_Concept_Guide.md`
- `APS_Gap_Analysis_vs_AVEVA.md`
- `APS_Generic_Architecture_And_Scenario_Workbench.md`
- `APS_Implementation_Plan_Config_Masters.md`
- `APS_Planning_Views_And_Visibility_Design.md`
- `APS_Roadmap_Industry_Agnostic.md`
- `APS Design Philosophy`
- `APS Fix Plan 1`

These remain useful historical/reference material but must be interpreted through the current backend acceptance audit, steel-domain architecture and production UI blueprint.

## Repository architecture note

The existing Python/workbook prototype remains in the repository as a **reference and migration source**. It already contains valuable behavior such as recursive BOM explosion, CTP and capacity analysis that must be preserved where appropriate while being migrated into the canonical .NET architecture.

The production direction is the .NET architecture under `src/` with Domain/Application/Planning/Infrastructure/Integrations/Service/UI separation.

The planning kernel and domain model remain authoritative. The existing Blazor `/planning` page is a no-database reference/demo sandbox and should not be enlarged into the production planner workspace. Production pages should consume dedicated application/query contracts and persisted Plan Version state.