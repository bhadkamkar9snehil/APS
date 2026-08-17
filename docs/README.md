# APS Documentation

APS now contains both the original prototype documentation and the production .NET planning-core work.

## Canonical current architecture references

For current backend/domain development, start here:

- **`APS_Steel_Domain_Architecture_Roadmap.md`** — canonical current-state assessment, steel-domain target architecture, physical plant model, metallurgy/grade model, material and product forms, furnace-capacity-driven heat formation, EAF/LRF/VD/CCM routing, superheat, billet sourcing, reheating, rolling/TMT/bundles/coils, time-phased material balance, solver evolution, contingencies, genealogy, diagnostics, and implementation order.
- **`dotnet-planning-core.md`** — detailed description of the .NET planning-core implementation that exists today.
- GitHub **Issue #2** — governing steel-domain implementation epic, with child issues #3-#19.

The steel-domain roadmap is the authoritative design direction for new backend/planning work. Older generic/industry-agnostic documents remain useful historical references but must not override the current steel-specific architecture decisions.

## Current development priority

UI expansion is not the current priority. The immediate work is:

1. steel plant / physical equipment topology
2. grade, metallurgy and process requirements
3. material, cross-section and product-form masters
4. SAP/customer special-characteristic constraints
5. furnace-feasible heat formation
6. EAF -> LRF -> optional/required VD -> CCM heat routing
7. CCM / thermal / billet availability modeling
8. shared reheating + rolling / TMT / bundling / coil modeling
9. time-phased material balance
10. coupled campaign and physical-resource optimization

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

These should be read in the context of the newer canonical steel-domain roadmap.

## Repository architecture note

The existing Python prototype remains in the repository. The production direction is the .NET planning architecture under `src/` with domain/application/planning/infrastructure/integration/service/UI separation. The planning kernel and domain model are authoritative; the Blazor Planning Sandbox is a reference/demo and should not drive domain design.