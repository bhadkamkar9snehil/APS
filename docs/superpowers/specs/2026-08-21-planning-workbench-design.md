# APS Unified Planner Lifecycle Workbench Design

## Purpose

Replace the module-led finite-schedule experience with one state-aware PPC workbench. The planner must spend the majority of working time here: create or clone a scenario, form campaigns, optimize, manually adjust, inspect impact, validate, release, monitor manufacturing, and start a recovery scenario without losing lineage or context.

## Product boundary

The workbench owns planning from current demand and actual production state through scenario calculation, campaign formation, interactive adjustment, comparison, approval, release, execution monitoring, and recovery planning. Master configuration and integration administration remain outside it. Manufacturing actuals are authoritative execution feedback, not editable planning decoration.

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

## Industry benchmark

The workbench deliberately follows the common operating model of leading APS products while keeping APS's steel-specific campaign, heat, cast, billet, and rolling lineage visible:

| Leader | Proven interaction model | APS workbench decision |
| --- | --- | --- |
| SAP S/4HANA PP/DS | The Detailed Scheduling Planning Board combines resource/time charts, pegging, alerts, heuristics, manual drag-and-drop rescheduling, alternative-resource moves, and undo. | One central resource Gantt with dependency cues, exception queue, drag-to-propose, resource alternatives, impact validation, and Plan-Version undo/redo. |
| Siemens Opcenter APS | An interactive planning board combines multi-constraint scheduling, what-if simulation, impact analysis, order priority, material constraints, and capable-to-promise. | Immutable baseline plus persisted child scenarios, before/after overlay, demand/campaign/material lenses, explicit solver-repair impact, and release only from a feasible plan. |
| DELMIA Ortems | Finite-capacity planning synchronizes demand, inventory, materials, work orders, resources, and disruption response rather than treating the Gantt as a standalone drawing. | The Gantt is backed by the aggregate workbench read model and canonical lifecycle; visual moves never bypass material, capacity, route, thermal, or sequence validation. |

Primary references: [SAP Detailed Scheduling Planning Board](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/f899ce30af9044299d573ea30b533f1c/644dc95360267614e10000000a174cb4.html), [SAP manual scheduling with drag-and-drop](https://help.sap.com/docs/SAP_S4HANA_ON-PREMISE/f899ce30af9044299d573ea30b533f1c/b74dc95360267614e10000000a174cb4.html), [Siemens Opcenter Scheduling Standard](https://www.siemens.com/en-us/products/opcenter/scheduling-standard/), and [DELMIA Ortems](https://www.3ds.com/products/delmia/ortems).

## Screen anatomy

The screen contains six synchronized regions:

1. Scenario header: human scenario name, parent/released baseline, state, horizon, actuals timestamp, feasibility, save state, Compare, Save checkpoint, Optimize, Validate, Approve, and Release.
2. Lifecycle rail: Plan, Campaigns, Execution, and Recovery. Each mode states the current job and exposes only actions legal in the scenario state.
3. Planning queue: orders, campaigns, materials, events, and exceptions, including unscheduled and attention-required items.
4. Toolbar: undo/redo, validation, repair scope, optimization, zoom, fit, grouping, layers, search, and focused-chain controls.
5. Gantt canvas: sticky resource hierarchy and time axis, operations, campaign spans, downtime, frozen/stable zones, baseline ghosts, actual overlays, current-time marker, and selected dependency chain.
6. Inspector: business identity, demand/campaign/material lineage, planned and actual timing, resource alternatives, commitment, explanation, and contextual actions.
7. Impact dock: proposed change, hard conflicts, warnings, changed operations, delivery/material/capacity impact, KPI deltas, and Apply locally / Apply and repair / Discard.
8. Analysis dock: Exceptions, Capacity, Delivery, Material, Campaign KPIs, and Scenario Comparison.

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

The baseline is selectable from Plan Version history. Comparison supports overlay, changed-only, and KPI summaries. Moved or resource-changed operations show their baseline ghost and exact delta. Added and removed operations remain distinguishable without relying only on color.

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

## Acceptance

The workbench is complete when the existing database opens unchanged and a planner can create/clone a named scenario, optimize it, form and edit campaigns, inspect one schedule through every lens, stage and validate a move, see before/after impact, persist a child Plan Version, undo/redo, compare scenarios, validate/approve/release, monitor actual execution, and create a recovery scenario without exposing internal IDs or losing historical data.
