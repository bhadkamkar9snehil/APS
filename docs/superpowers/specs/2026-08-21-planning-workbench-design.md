# APS Unified Planner Cockpit Design

## Purpose

Replace the module-led finite-schedule experience with one state-aware PPC cockpit. The planner must spend the majority of working time here: create or clone a scenario, form campaigns, optimize, manually adjust, inspect impact, compare alternatives, validate, release, monitor manufacturing, trace material and demand, and start a recovery scenario without losing schedule context.

## Product boundary

The cockpit owns planning from current demand and actual production state through scenario calculation, campaign formation, interactive adjustment, comparison, approval, release, execution monitoring, traceability, and recovery planning. Master configuration and integration administration are opened from the cockpit menu as administrative workspaces. Manufacturing actuals are authoritative execution feedback, not editable planning decoration.

## Domain objects and lifecycle

- A **Planning Scenario** is the complete cross-process candidate schedule. It is the object planners save, compare, validate, approve, and release.
- A **Campaign** is a steelmaking/casting grouping inside a scenario. It owns compatible heats, grade order, caster section, and transition decisions. It is never a synonym for the complete plan.
- A **Campaign Template** is a reusable campaign pattern. Reuse creates a new campaign instance; it never mutates historical campaigns.
- A **Released Plan** is the immutable execution baseline produced from one persisted feasible scenario.
- **Operations Performance** is the actual manufacturing feedback for released work: status, actual start/end, quantity, material movement, resource state, and exceptions.
- A **Recovery Scenario** is a child scenario created from the active released plan plus the latest actual state. Completed work is frozen, running work is protected, and future flexible work can be repaired.

Scenario states are `Draft -> Solving -> NeedsAttention|Feasible -> Approved -> Released -> Executing -> Completed|Superseded`. A released scenario cannot return to Draft. Replanning always creates a child scenario.

## Governing rules

- A released plan is immutable. Editing starts a scenario/replan derived from it.
- The default launch route is the Planning Workbench. Supporting registers never displace it as the primary planning surface.
- The application has no persistent page-navigation sidebar. A compact desktop menu bar owns global commands and administrative navigation.
- The workbench exposes four state-aware modes: Plan, Campaigns, Execution, and Recovery. They share one selection, timeline, scenario context, and comparison baseline.
- Resource lanes are the default hierarchy. Demand, campaign, material, and exception lenses reuse the same selection and timeline.
- Manual moves are staged proposals. The planner sees feasibility and impact before applying them.
- Hard-constraint violations cannot be applied. Soft violations require acknowledgement.
- Applying a proposal recalculates the affected dependency scope and creates a new persisted Plan Version; it never mutates historical schedule facts.
- Every applied planning command supports undo and redo by creating another persisted result from the same baseline and command history.
- No internal database identifiers are user-facing.
- Status is never communicated by color alone.
- The installed local database and historical Plan Versions remain compatible and are not reset or reseeded.
- Dependency lines are hidden by default. Only the selected operation chain or an explicitly requested focused chain is rendered.
- Released-plan execution views are read-only. A planner must create a recovery scenario before changing future work.
- Opening analysis, traceability, execution, or setup never destroys the active scenario, timeline window, selection, or comparison baseline.
- The resource Gantt is the visual centre of gravity. Supporting surfaces overlay it temporarily or occupy collapsible, resizable docks.

## Industry benchmark

The workbench deliberately follows the common operating model of leading APS products while keeping APS's steel-specific campaign, heat, cast, billet, and rolling lineage visible:

| Leader | Proven interaction model | APS workbench decision |
| --- | --- | --- |
| SAP S/4HANA PP/DS | The Detailed Scheduling Planning Board combines resource/time charts, pegging, alerts, heuristics, manual drag-and-drop rescheduling, alternative-resource moves, and undo. | One central resource Gantt with dependency cues, exception queue, drag-to-propose, resource alternatives, impact validation, and Plan-Version undo/redo. |
| Siemens Opcenter APS | An interactive planning board combines multi-constraint scheduling, what-if simulation, impact analysis, order priority, material constraints, and capable-to-promise. | Immutable baseline plus persisted child scenarios, before/after overlay, demand/campaign/material lenses, explicit solver-repair impact, and release only from a feasible plan. |
| DELMIA Ortems | Finite-capacity planning synchronizes demand, inventory, materials, work orders, resources, and disruption response rather than treating the Gantt as a standalone drawing. | The Gantt is backed by the aggregate workbench read model and canonical lifecycle; visual moves never bypass material, capacity, route, thermal, or sequence validation. |

Primary references: [SAP Detailed Scheduling Planning Board](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/f899ce30af9044299d573ea30b533f1c/644dc95360267614e10000000a174cb4.html), [SAP manual scheduling with drag-and-drop](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/f899ce30af9044299d573ea30b533f1c/b74dc95360267614e10000000a174cb4.html), [Siemens Opcenter Scheduling Standard](https://www.siemens.com/en-us/products/opcenter/scheduling-standard/), and [DELMIA Ortems](https://www.3ds.com/products/delmia/ortems).

## Desktop shell and menu bar

The persistent application chrome is a single compact desktop menu bar. It contains the APS icon and these menus:

- **File**: New scenario, Clone scenario, Open scenario, Save checkpoint, Import, Export, Exit.
- **Plan**: Refresh inputs, Optimize, Repair selection, Validate, Approve, Release plan.
- **View**: Demand queue, Inspector, Analysis dock, Baseline, Dependencies, Tight chain, Fit horizon, Fit selection, Full screen, Appearance.
- **Analyze**: Control overview, Exceptions, Capacity, Delivery, Material, Campaign KPIs, Scenario comparison, Traceability.
- **Execute**: Execution monitor, Record actual, Work orders, Create recovery scenario.
- **Configure**: Demand and supply, Inventory, Plants, Resources, Process stages, Grades, Routes, Materials, Cross sections, External supply.
- **Help**: Planner guide, Keyboard shortcuts, Diagnostics, About.

Menus use accessible native button/menu semantics, arrow-key navigation, Escape dismissal, click-outside dismissal, and disabled commands with a concise reason. The menu bar does not display a global Plan Context strip, horizon, solver, trigger, or internal plan identifier.

Administrative commands may replace the Gantt with an administrative workspace, but the menu bar remains fixed and `Back to schedule` restores the previous scenario, time window, and selection.

## Screen anatomy

The cockpit contains these synchronized regions:

1. **Scenario command strip**: business scenario name, lifecycle state, baseline, dirty/checkpoint state, Plan/Campaigns/Execution/Recovery modes, Create scenario, Undo, Redo, Optimize, Validate, Approve, and Release. Dates, counts, and objective details move into a compact scenario popover instead of consuming a permanent header row.
2. **Gantt toolbar**: time scale, pan, Fit, grouping, search, focused-selection chip, baseline, dependencies, tight chain, queue, inspector, and analysis controls.
3. **Resource Gantt**: sticky resource hierarchy and time axis, operations, campaign spans, downtime, frozen/stable zones, baseline ghosts, actual overlays, current-time marker, and selected dependency chain.
4. **Overlay queue**: demand, campaigns, materials, events, and exceptions. It slides over the left side of the Gantt, has its own scroll, and never permanently consumes timeline width.
5. **Overlay inspector**: business identity, lineage, planned and actual timing, resource alternatives, commitment, explanation, and contextual actions. It slides over the right side and closes with Escape.
6. **Bottom analysis dock**: Control overview, Exceptions, Capacity, Delivery, Material, Campaign KPIs, Scenario Comparison, Execution Monitor, and Traceability. It is collapsed by default, resizable when open, and retains the Gantt above it.
7. **Impact tray**: staged change, hard conflicts, warnings, changed operations, delivery/material/capacity impact, KPI deltas, and Apply locally / Apply and repair / Discard. It replaces the analysis dock only while a proposal is staged.

## Gantt space and sizing rules

- The menu bar is at most 32 px high. The scenario command strip and Gantt toolbar together are at most 104 px high at desktop widths.
- The footer is removed. Version and update status move to Help > About and a non-blocking update command.
- The Gantt receives all remaining width and height. It is never wrapped in a page container with padding or a maximum width.
- The resource-name column defaults to 168 px and can be resized between 136 px and 280 px.
- With eight or fewer visible resource lanes, lane height expands evenly to fill the available Gantt viewport, with a minimum of 64 px and a maximum of 104 px.
- With more lanes than fit, each lane retains at least 64 px and the Gantt scrolls vertically.
- Queue and inspector overlays default to 320 px and 360 px respectively, are independently resizable, and do not change the underlying time scale.
- The analysis dock opens to 30% of the workbench height, can resize between 160 px and 60% of the workbench, and collapses to a 30 px tab rail.
- Focused selection is always represented by a removable chip next to search. `Clear` and Escape restore the complete unfiltered schedule.

## Control overview role

Control Tower is renamed **Control overview** and becomes an analysis lens rather than a competing page. It summarizes the active scenario or released baseline at the level appropriate to the current mode:

- Plan and Campaigns: feasibility, uncovered demand, late demand, bottlenecks, material exposure, campaign health, and changes versus baseline.
- Execution: schedule adherence, delayed/running/held operations, resource downtime, produced quantity, and projected delivery impact.
- Recovery: deviations that require intervention, frozen work, repair scope, and projected recovery outcome.

Selecting a Control overview item filters or highlights the corresponding Gantt objects. Closing the dock preserves the selection; clearing focus restores the full schedule.

## Planner operating flow

1. Refresh demand, inventory, capability, calendars, and current operations performance.
2. Create a blank scenario, clone a prior scenario, or create a recovery scenario from the active released plan.
3. Name the scenario, set horizon/fences, select an objective profile, and define scope.
4. Run finite-capacity optimization. The result remains a saved candidate, never an automatic release.
5. Review demand, campaign, material, capacity, and exception lenses over the same timeline.
6. Create/split/merge/resequence campaigns and move/reassign/lock/deallocate operations.
7. Preview each consequential change. Apply it locally or allow canonical solver repair of affected successors.
8. Save named checkpoints and compare candidate scenarios against the released baseline or another candidate.
9. Validate release readiness, acknowledge permitted warnings, approve, and release the complete plan.
10. Monitor actual manufacturing against the released baseline.
11. When demand, capability, resource, material, or execution deviation requires intervention, create a recovery scenario, repair future work, validate, approve, and release a superseding version.

## Workbench lenses

- Resource: process group -> physical resource -> operation.
- Campaign: campaign -> heat -> process operations.
- Demand: sales/production order -> requirement chain -> operations.
- Material: material pool -> receipt/consumption events -> consuming operations.
- Exception: severity/type -> affected planning objects.

Resource is the default. A selection is preserved when the lens changes.

## Gantt interaction

- Pan horizontally and vertically; zoom from multi-day to minute-level.
- Select one block, Ctrl-select several, Shift-select a contiguous range, and clear selection with Escape.
- Drag horizontally to propose a time change and vertically to propose an eligible resource change.
- Show a ghost at the candidate position while the original remains visible.
- Snap by shift, hour, 30 minutes, 15 minutes, or free placement.
- Autoscroll while dragging near an edge.
- Expand/collapse process groups and fit the horizon or current selection.
- Open contextual actions for pin, unpin, deallocate, repair, find alternatives, focus lineage, and compare.
- Render an accessible list/table equivalent for keyboard and assistive-technology operation.

## Proposal validation and impact

A proposal contains the operation planning key, target resource, target start, target end, commitment state, and planner reason. Validation checks:

- Resource and route eligibility
- Resource operating state and calendar
- Overlap/capacity rules
- Frozen and stable time-fence policy
- Predecessor and successor timing
- Campaign and cast sequencing
- Material availability and reservations
- Thermal eligibility and transfer windows
- Downstream due-date impact

The result identifies hard blockers, soft warnings, affected operations/orders/campaigns, and before/after KPI deltas. Apply is enabled only without hard blockers.

## Solver actions

The planner can validate only, repair selected operations, repair an order, repair a campaign, repair affected resources, replan the flexible horizon, or optimize the entire scenario. These actions use the canonical lifecycle and persist a new Plan Version. The current planning engine remains the only schedule authority.

## Campaign planning

Campaign spans appear above operations. The inspector supports create, add/remove orders, split, merge, dissolve, reorder heats, move the campaign, pin its sequence, and optimize selected campaigns. Actions that lack a safe domain command remain visible but disabled with a precise explanation; they must not pretend to work.

Campaign mode keeps the same resource Gantt and adds a campaign composition rail above it. It shows campaign compatibility, grade transition cost, heat count, caster assignment, due-date exposure, existing/fresh billet balance, and downstream effects. `Save campaign template` creates a reusable template; `Save scenario` persists the complete planning candidate.

## Execution and recovery

Execution mode overlays planned and actual start/end, operation state, produced quantity, material receipts/consumption, resource downtime, and projected completion. Deviations are classified as informational, attention, or replan-required.

`Create recovery scenario` snapshots the active released baseline and latest actual state. Completed operations are fixed, running operations retain their actual start and protected resource commitment, and only future flexible work can move without an authorized exception. Releasing the recovery scenario supersedes the future portion of the active plan while preserving both histories.

## Material and demand context

Selecting demand highlights its full production chain. Selecting a material pool shows planned receipts, consumptions, running balance, shortages, earliest feasible supply, and affected operations. The queue exposes newly changed, unplanned, late, partially covered, and excluded demand.

## Comparison

The baseline is selectable from Plan Version history. Scenario comparison opens in the bottom dock and supports overlay, changed-only, and KPI summaries without navigating away from the schedule. Moved or resource-changed operations show their baseline ghost and exact delta. Added and removed operations remain distinguishable without relying only on color.

## Execution monitoring and traceability

Execution Monitor is a workbench mode plus a bottom-dock lens. It overlays actual state on the same released schedule and exposes deviations, work-order status, resource events, output, and projected completion. It is not a separate primary page.

Traceability opens from the selected operation, campaign, order, work order, or material. The bottom dock shows upstream and downstream lineage while the related Gantt chain is highlighted. A global trace search is available under Analyze > Traceability when no object is selected.

## Release readiness

Release is available only for a persisted feasible Plan Version. The workbench presents uncovered demand, hard conflicts, acknowledged warnings, changed frozen/firm operations, material risk, and affected orders before invoking the existing persisted release service.

## Persistence and recovery

Plan results and applied overrides are persisted through the canonical lifecycle. UI-only preferences (lens, zoom, collapsed groups, layer visibility, dock sizes) persist locally. A crash or restart reopens the most recently selected persisted plan without modifying the database.

## Performance and accessibility

- Render only lanes and blocks intersecting the current viewport/time window.
- Use stable planning keys and resource IDs internally while showing business codes.
- Keep scrolling and zooming responsive for thousands of operations.
- Support keyboard selection/actions, visible focus, tooltips, text status, and a schedule table alternative.
- Preserve light/dark/system themes and all configured non-blue, non-cyan accents.
- Keep dropdown menus, drawers, docks, and administrative workspaces lazy so the initial workbench does not load every supporting page.

## Test and build policy

- Preserve planning-engine, persistence, migration, release-readiness, and database-compatibility tests.
- Replace brittle tests that merely search Razor or generated CSS text with behavioural state/component tests for menu commands, panel state, focus clearing, lifecycle permissions, and Gantt sizing decisions.
- During implementation, run the smallest affected test project or named test class. Run the complete solution test suite before each pushed checkpoint.
- Tailwind rebuilds only when Razor, CSS input, or theme-token sources are newer than the generated stylesheet. Ordinary C# changes reuse the existing stylesheet.
- Release packaging, self-contained publishing, installer creation, asset hashing, and updater checks run only when the user explicitly requests a release.
- A normal development checkpoint consists of incremental build, focused tests, full tests before push, database integrity check, and startup-log verification.

## Acceptance

The cockpit is complete when the existing database opens unchanged and:

- no persistent navigation sidebar or Plan Context header is present;
- the menu bar reaches every supported planning, analysis, execution, traceability, and setup capability;
- the Gantt fills the available workbench and expands a small resource set vertically;
- queue, inspector, Control overview, comparison, execution, and traceability can be opened and closed without losing scenario or timeline context;
- a planner can create/clone a named scenario, optimize it, form and edit campaigns, stage and validate a move, see before/after impact, persist a child Plan Version, undo/redo, compare scenarios, validate/approve/release, monitor actual execution, and create a recovery scenario;
- no internal IDs are exposed and no historical or seeded data is lost.
