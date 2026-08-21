# APS Planning Workbench Design

## Purpose

Replace the module-led finite-schedule experience with one Gantt-first PPC workbench. A planner must be able to understand demand, campaigns, heats, material, capacity, constraints, schedule changes, and release readiness without leaving the screen.

## Product boundary

The workbench owns planning from a persisted baseline through scenario calculation, interactive adjustment, comparison, and release. Master configuration and routine shop-floor data entry remain outside it. Execution actuals remain visible as planning context.

## Governing rules

- A released plan is immutable. Editing starts a scenario/replan derived from it.
- Resource lanes are the default hierarchy. Demand, campaign, material, and exception lenses reuse the same selection and timeline.
- Manual moves are staged proposals. The planner sees feasibility and impact before applying them.
- Hard-constraint violations cannot be applied. Soft violations require acknowledgement.
- Applying a proposal recalculates the affected dependency scope and creates a new persisted Plan Version; it never mutates historical schedule facts.
- Every applied planning command supports undo and redo by creating another persisted result from the same baseline and command history.
- No internal database identifiers are user-facing.
- Status is never communicated by color alone.
- The installed local database and historical Plan Versions remain compatible and are not reset or reseeded.

## Screen anatomy

The screen contains six synchronized regions:

1. Scenario header: plant, current plan, baseline, horizon, feasibility, dirty state, Calculate, Compare, Save, and Release.
2. Planning queue: orders, campaigns, materials, and exceptions, including unscheduled and attention-required items.
3. Toolbar: undo/redo, validation, repair scope, optimization, zoom, fit, grouping, layers, and search.
4. Gantt canvas: sticky resource labels and time axis, grouped resource lanes, operations, campaign spans, downtime, frozen/stable zones, baseline ghosts, current-time marker, and dependency cues.
5. Inspector: business identity, demand/campaign/material lineage, timing, resource alternatives, commitment, explanation, and contextual actions for the selection.
6. Exception and impact dock: hard conflicts, warnings, changes, late demand, material risk, capacity risk, and staged-proposal impact.

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

The workbench is complete when the current seeded plan can be opened, filtered, zoomed, inspected, compared, used to stage and validate a move, applied through a persisted replan, undone/redone, reviewed for exceptions/material/campaign impact, and released from this screen without losing existing data.
