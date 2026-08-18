# APS Documentation

APS contains both the original prototype documentation and the production .NET planning-core work.

## Canonical current architecture references

For current backend/domain development, start here:

- **`APS_Steel_Domain_Architecture_Roadmap.md`** — canonical steel-domain architecture and planning/solver direction.
- **`dotnet-planning-core.md`** — detailed description of the .NET planning-core implementation represented by the current development branch.
- GitHub **Issue #2** — steel-domain implementation epic and its child issues.

For production UI/UX development, start here:

- **`APS_UI_UX_Product_Blueprint.md`** — canonical information architecture, planner workflows, entity-inspector model, plan lifecycle, screen specifications, visualization policy and interaction safety rules.
- **`APS_UI_Implementation_Plan.md`** — dependency-ordered implementation plan, query/read-model requirements, component boundaries, backend-to-UI coverage matrix, routes and testing strategy.
- GitHub **Issue #20** — governing production UI/UX epic, with child issues #21-#31.

The steel-domain roadmap remains authoritative for planning/domain behavior. The UI blueprint is authoritative for how that behavior is exposed to users. A screen must not invent missing planning behavior or move calculations into Razor/JavaScript merely to complete the interface.

## Current development priority

The domain foundation and production UI are now treated as one product sequence:

1. continue closing remaining backend/domain gaps at their canonical source rather than with UI workarounds;
2. build the UI-enabling application/query contracts and planner workspace state (#22);
3. build the production shell/design system/context inspector (#21);
4. implement the Control Tower and Plan Version lifecycle (#23);
5. implement Demand/Campaign, physical production/schedule, material, execution/replan/traceability and decision workbenches (#24-#30);
6. harden with accessibility, large-data performance, visual regression and E2E planner flows (#31).

The rule is **backend truth first, UI expression immediately behind it**. Every important implemented backend capability should acquire a deliberate UI/UX home; dependent screens do not ship ahead of missing authoritative contracts.

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

These remain useful historical/reference material but must be interpreted through the current steel-domain architecture and the new production UI blueprint.

## Repository architecture note

The existing Python/workbook prototype remains in the repository as a reference and migration source. The production direction is the .NET architecture under `src/` with Domain/Application/Planning/Infrastructure/Integrations/Service/UI separation.

The planning kernel and domain model remain authoritative. The existing Blazor `/planning` page is a no-database reference/demo sandbox and should not be enlarged into the production planner workspace. Production pages should consume dedicated application/query contracts and persisted Plan Version state.