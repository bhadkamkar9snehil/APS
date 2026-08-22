# APS Gantt — DHTMLX Behavioral and Visual Benchmark

**Purpose:** Explain what the important DHTMLX Gantt capabilities actually *do and feel like*, so APS does not reduce them to feature names.  
**Companion requirement:** `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`  
**Benchmark date:** August 2026  
**Reference generation:** DHTMLX Gantt v10 documentation

---

# 1. Why this benchmark exists

A checkbox such as `Drag & drop — Have` is nearly useless for a Gantt product review.

A mature Gantt feature is a coordinated collection of geometry, state, visual affordance, pointer behavior, keyboard behavior, scrolling, constraints, editing policy, and feedback. DHTMLX is valuable as a reference because these behaviors have been exercised over many releases. APS should learn from that maturity without copying DHTMLX's generic project/WBS data model.

The question for APS is therefore not:

> “Does APS have feature X?”

It is:

> “Does an APS planner experience the same level of predictable control when performing the equivalent manufacturing action?”

---

# 2. The basic DHTMLX visual model

DHTMLX presents two synchronized horizontal regions:

1. a **grid** at the left;
2. a **timeline** at the right.

The grid is not merely a label strip. Its columns can represent task name, start, duration and custom fields. The timeline shares the same row structure, so each data row corresponds precisely to the bar area on the right.

The important visual qualities are:

- a clear vertical split between structured data and time geometry;
- fixed/sticky logical row identity while time scrolls horizontally;
- one vertical row coordinate shared by grid and timeline;
- scale headers directly above timeline cells;
- bars occupying the row's time interval;
- links rendered in a separate layer connecting bar endpoints;
- scrollbars/resizers treated as part of the layout model rather than page-level hacks.

DHTMLX's default grid has task name, start, duration and an add control. APS should not copy those fields, but should copy the idea that the left side is a **real configurable grid** rather than a 176 px resource caption.

Reference: https://docs.dhtmlx.com/gantt/guides/specifying-columns/

---

# 3. Grid behavior in detail

## 3.1 Column geometry

DHTMLX columns have explicit or flexible widths. A column can have min/max bounds. In a configured layout, the entire grid can also have a resizer between it and the timeline.

### What the planner perceives

- Hover near a resizable border → cursor/target indicates resize.
- Drag column border → the column changes continuously.
- Drag main grid divider → timeline gains/loses width while staying synchronized.
- Grid does not become a separate page panel detached from timeline rows.

### APS lesson

APS requires two levels of resize:

1. individual resource-grid column widths;
2. overall grid/timeline split width.

The two must be independent. A user widening “Next operation” should not unpredictably destroy the total grid width unless the chosen grid-width policy explicitly says so.

## 3.2 Sorting

DHTMLX supports per-column sorting and custom comparators. The useful behavior is not just that rows reorder; the header communicates active sort and direction.

### APS lesson

Resource sorting is a **view transform**, not a scheduling command. A planner sorting by utilization must never change solver priority.

## 3.3 Tree hierarchy

DHTMLX uses tree rows for project hierarchy. Expand/collapse changes visible rows while timeline bars remain synchronized.

### APS lesson

Use the same mechanism for manufacturing hierarchy:

`Area → Process type → Resource`

Never introduce WBS semantics just to imitate DHTMLX.

---

# 4. Timeline scale and zoom behavior

DHTMLX v10's zoom extension has named levels and can use multiple scale rows. Its `zoomToFit()` does something more sophisticated than “set visible start to min date and end to max date”:

- it knows the **actual timeline viewport width**;
- it evaluates available zoom levels;
- it chooses the most detailed level at which the requested content fits without horizontal scrolling;
- it can fit all tasks, visible tasks, a subtree, or an explicit range;
- it has padding controls;
- `resetZoom()` restores the scale that existed before the first fit.

Reference: https://docs.dhtmlx.com/gantt/guides/zoom/

## 4.1 Visual implication

A fit action does not merely stretch bars to fill 100% of a CSS container. It selects an intelligible calendar scale. A week should still look like a week, with meaningful day/hour divisions.

## 4.2 APS adaptation

APS should have manufacturing-oriented levels:

- 30-minute/detail;
- shift;
- day;
- 3-day;
- week;
- 2-week;
- month;
- Fit.

At most useful levels the scale should have an upper and lower row, for example:

`22 Aug · Saturday`  
`08:00 | 10:00 | 12:00 | 14:00 ...`

or:

`Week 34 · August`  
`Mon 17 | Tue 18 | Wed 19 ...`

The planner should never need to mentally reconstruct which date an isolated `14:00` tick belongs to.

---

# 5. Task/operation bar interaction in DHTMLX

DHTMLX's default task bar communicates multiple drag modes through geometry:

- drag the **body** → move the task in time;
- hover/drag the **left/right border** → resize start or finish/duration;
- drag the **progress knob** → change progress;
- hover task → round **dependency handles** become usable at start/end.

Reference: https://docs.dhtmlx.com/gantt/guides/overview/  
Drag details: https://docs.dhtmlx.com/gantt/guides/dnd/

The critical design lesson is that each manipulation mode has a specific **handle or hit region**. The user is not asked to guess whether the same generic drag will move, resize, link, or change progress.

## 5.1 DHTMLX drag lifecycle

DHTMLX provides before/during/after drag events. The app can:

- block drag for particular tasks;
- restrict dates during movement;
- identify whether the drag is move, resize or progress;
- know whether resize is from start or finish;
- set minimum duration;
- disable specific resize handles;
- autoscroll while dragging.

This is a product-design benchmark even though APS will implement its own mechanics.

## 5.2 APS adaptation

APS operation bars should expose fewer manipulation modes by default because manufacturing durations and dependencies are not freeform project fields.

**Normal manufacturing operation:** body drag = stage time/resource move. No duration handle. No progress knob. No editable dependency handles.

**Planner-created resource event:** may show resize handles if event duration is editable.

**Explicit duration-override operation:** may show resize handles only when the domain command permits it.

The visual affordance must tell the truth about what the model allows.

---

# 6. Drag autoscroll and distant targets

DHTMLX explicitly supports autoscroll during task drag because a real schedule is commonly wider/taller than one viewport.

This matters far more in APS, where a planner may move a heat to another caster several lanes away or to a later time not initially visible.

### Expected APS feel

1. Planner presses a bar.
2. Small movement threshold prevents accidental drag.
3. Original remains as source anchor.
4. Ghost follows pointer preserving grab position.
5. Target resource eligibility becomes visible.
6. Snap guide moves with ghost.
7. Near right/left edge → time scroll accelerates.
8. Near top/bottom edge → resource list scrolls.
9. Candidate time stays mathematically correct while viewport moves.
10. Escape cancels immediately.
11. Drop leaves proposal ghost in place while validation runs.

A drag implementation that only translates the original block and snaps the cursor coordinate is not behaviorally equivalent.

---

# 7. Dependencies and link visualization

DHTMLX supports four link semantics:

- Finish → Start (FS)
- Start → Start (SS)
- Finish → Finish (FF)
- Start → Finish (SF)

Link objects can also contain lag. In the default interface, dependency creation uses round handles at task endpoints. Links are routed as connector paths with target direction, not plain center-to-center lines.

References:

- https://docs.dhtmlx.com/gantt/guides/link-properties/
- https://docs.dhtmlx.com/gantt/guides/overview/

## APS adaptation

APS should **copy the visual clarity, not editable dependency creation**.

Manufacturing dependencies come from routing, heat/cast/campaign relationships, resource sequence, material state and transfer rules. A user should generally inspect them, not redraw the manufacturing route with the mouse.

A proper APS link should therefore show:

- exact source/target operation endpoint;
- semantic direction;
- hard/soft character;
- min lag/queue time;
- max queue/thermal limit when relevant;
- current wait;
- remaining headroom;
- reason/category.

A straight dashed line between lane centers loses most of this information.

---

# 8. Critical path: what DHTMLX actually means

DHTMLX's critical path capability is not “tasks that touch each other.” It uses scheduling slack and dependency logic. It exposes free and total slack APIs and can highlight critical tasks/links.

References:

- https://docs.dhtmlx.com/gantt/guides/critical-path/
- https://docs.dhtmlx.com/gantt/api/method/getfreeslack/

## APS adaptation

Generic CPM cannot simply be transplanted because APS has finite resource sequences, material arrivals, campaign rules and thermal windows.

The equivalent feature should answer:

> “Which operations currently have no practical scheduling freedom, and *what consumes that freedom*?”

Examples:

- `0 min headroom — CCM-01 sequence`
- `12 min thermal headroom — LRF→CCM`
- `Material arrival binds start at 23 Aug 04:00`
- `Frozen fence prevents earlier move`
- `Due date becomes late with >18 min slip`

This is a **binding-chain** feature, not a highlight based on a one-minute gap.

---

# 9. Baselines: how DHTMLX solves visual density

DHTMLX can render baselines in three ways:

1. **taskRow** — baseline in same row as the current task;
2. **separateRow** — baselines share a dedicated subrow beneath current task;
3. **individualRow** — each baseline gets its own subrow.

Reference: https://docs.dhtmlx.com/gantt/guides/inbuilt-baselines/

The important lesson is that comparison density is **mode-dependent**. A single rendering style is not adequate for every task size and comparison depth.

## APS adaptation

### Fast compare

Thin neutral baseline ghost below current operation. Ideal for daily work.

### Deep compare

Expand resource row / compare subrow. Useful when many operations moved or when resource reassignments must be traced.

### Changed-only

Hide unchanged operations from comparison analysis while still preserving enough context to understand sequence.

### Resource changes

Baseline stays on original resource; current operation stays on new resource. The UI must not force both into one row simply because comparison is enabled.

---

# 10. Markers and time bands

DHTMLX's marker extension treats timeline markers as entities with:

- start date;
- optional end date;
- CSS class;
- label text;
- tooltip;
- update/hide/delete behavior.

The classic example is a `Now` marker that is periodically updated.

Reference: https://docs.dhtmlx.com/gantt/guides/markers/

## APS adaptation

APS needs a marker registry because several industrial concepts share the time axis but are semantically different:

- wall-clock Now;
- reference/actuals timestamp;
- frozen-fence boundary;
- firm/stable boundary;
- selected demand due date;
- campaign due date;
- important material receipt;
- breakdown start/end;
- maintenance interval.

They must not become an unstructured collection of colored vertical lines.

---

# 11. Resource management: the most relevant DHTMLX PRO reference

DHTMLX exposes a separate resource section made of a `resourceGrid` and either:

- `resourceTimeline`; or
- `resourceHistogram`.

The resource section can sit below the main Gantt. A resizer controls its height. Crucially, the resource timeline can share the same horizontal scrollbar/scale as the main timeline.

Reference: https://docs.dhtmlx.com/gantt/guides/resource-management/

## 11.1 Resource timeline

Rows correspond to resources. Time cells contain blocks/values indicating assignments during that time bucket.

## 11.2 Resource histogram

Each time cell can render:

- allocated load;
- capacity line;
- label;
- class/status.

Capacity can vary by resource.

## 11.3 Critical DHTMLX caveat

DHTMLX explicitly says the Gantt component itself does **not** calculate resource load out of the box; applications supply the calculation through the API/templates.

This is advantageous for APS. APS already has finite scheduling/capacity truth and does not need a generic Gantt library's resource algorithm.

## 11.4 APS should go beyond DHTMLX

A useful APS resource panel should break occupancy into:

- productive processing;
- setup/changeover;
- planned downtime;
- actual downtime;
- idle available time;
- projected conflict/recovery pressure.

Clicking a capacity bucket should take the planner directly to the operations responsible.

---

# 12. Layout composition: why it matters

DHTMLX's layout is defined as cells/views with shared scroll groups. A common layout can contain:

- grid;
- resizer;
- timeline;
- vertical scrollbar;
- horizontal scrollbar;
- lower resource grid;
- lower resource timeline/histogram;
- a horizontal resizer between upper and lower sections.

Reference: https://docs.dhtmlx.com/gantt/guides/layout-config/

The important concept is **synchronized structural composition**.

APS should not approximate this with independent absolutely positioned panels that happen to look aligned at one screen size.

The resource grid, timeline header, body, baseline layer, dependency layer, capacity panel and scrollbars should derive from one layout/viewport state.

---

# 13. Keyboard model

DHTMLX does not make every task a separate Tab stop. The Gantt receives focus and then internal keyboard navigation moves among rows/cells. It includes horizontal/vertical scrolling shortcuts and selection behavior.

Reference: https://docs.dhtmlx.com/gantt/guides/keyboard-navigation/

## APS lesson

This is particularly important for a dense production schedule. Hundreds of bars cannot each participate naively in the page tab order.

APS should use:

- one Gantt entry focus;
- arrow/page navigation internally;
- Space selection;
- Enter inspection;
- context-menu keyboard access;
- safe move mode;
- predictable Escape cancellation;
- workbench undo/redo shortcuts.

Do not copy DHTMLX project shortcuts such as Delete task, create task, indent/outdent, because those operations do not map safely to APS.

---

# 14. Smart rendering / virtualization

DHTMLX lists smart rendering as a core capability. The visible result is simple: large schedules remain navigable because the control avoids treating every offscreen item as equal rendering work.

APS must make this architectural from the start.

### Required internal model

- virtual resource rows;
- visible-time clipping;
- overscan;
- geometry cache;
- focused dependency subset;
- aggregated resource histogram buckets;
- stable keys so selection survives mount/unmount.

If the Gantt only works smoothly with demo-sized schedules, it is not a finished APS Gantt.

---

# 15. DHTMLX behavior that APS should deliberately reject

A benchmark is not a shopping list.

## Reject generic progress drag

Production actuals are authoritative.

## Reject generic task creation from blank timeline

APS manufacturing operations are derived by planning/routing logic.

## Reject freeform dependency creation

Manufacturing routes are governed data/logic.

## Reject generic WBS/project summary hierarchy

Resource/process/campaign/heat/order are the APS hierarchy.

## Reject unrestricted task duration resize

Manufacturing duration is usually derived, not a drawing property.

## Reject client-side autoscheduling as schedule authority

APS solver/lifecycle remains authoritative.

## Reject hiding non-working time as the normal manufacturing view

Elapsed time has operational meaning for thermal windows, queue, downtime and transfers.

---

# 16. Current APS versus benchmark — behavioral scorecard

This is intentionally stricter than the previous feature list.

| Area | Current branch | DHTMLX-grade criterion | Verdict |
| --- | --- | --- | --- |
| Grid/timeline split | fixed 176 px label column | real grid + resizer + shared row/scroll model | **Rebuild** |
| Columns | single combined resource/load text | configurable columns, widths, sort | **Missing** |
| Hierarchy | flat ordered lanes | expandable hierarchy | **Missing** |
| Scale | single tick row | multi-tier customizable scale | **Missing** |
| Fixed zoom presets | 8h/1d/3d/7d/Fit | named levels | **Partial** |
| Fit | content range + padding | viewport-aware level selection + reset | **Partial** |
| Wheel zoom | none | anchored interactive zoom | **Missing** |
| Timeline pan | toolbar + scroll | scroll + drag-pan + keyboard | **Partial** |
| Move drag | moves real block | source anchor + mature drag lifecycle | **Poor** |
| Grab offset | not preserved | bar remains attached at grab point | **Bug** |
| Snap | fixed 15 min | configurable / contextual | **Partial** |
| Autoscroll | horizontal only | distant target support | **Partial** |
| Resource eligibility | validates after visual lane target | invalid targets prevented/communicated | **Poor** |
| Resize | absent | DHTMLX has handles | **Correct to omit by default**, domain-gate later |
| Progress edit | absent | DHTMLX has knob | **Correct to reject** |
| Links | straight dashed SVG lines | routed endpoint connectors | **Poor** |
| Criticality | 1-minute predecessor adjacency | slack/constraint based | **Incorrect semantic claim** |
| Baseline | changed dashed ghosts | complete render modes | **Partial** |
| Resource histogram | absent | synchronized resource panel | **Missing/high value** |
| Markers | due/fence lines | managed marker entities | **Partial** |
| Calendar/downtime | limited visible use | explicit resource calendar | **Missing/partial** |
| Keyboard | page key handler exists | internal schedule navigation | **Partial** |
| Smart rendering | no clear true lane virtualization in current markup | viewport rendering | **Missing/high risk** |
| Tooltip | HTML title | designed rich tooltip | **Weak** |
| Inspector | modeless side overlay | DHTMLX lightbox equivalent context | **Good direction / APS better pattern** |
| Undo/redo | persisted versions but duplicated state stacks | predictable semantic history | **Good domain model, architecture cleanup required** |

---

# 17. Visual target: what the APS Gantt should feel like

The desired impression is not “DHTMLX skinned like APS.” It is:

- **dense but not cramped**;
- **mechanical, not dashboard-like**;
- **stable under manipulation**;
- **time geometry is trustworthy**;
- **resources feel like rows in an instrument**, not cards;
- **bars are schedule marks first, text containers second**;
- **pan/zoom feels continuous and anchored**;
- **drag always shows origin and candidate**;
- **constraint feedback appears where the action happens**;
- **comparison is layered, not a separate report**;
- **capacity is synchronized to the same clock**;
- **every color/outline has one job**;
- **the user can explain why an operation is where it is**.

The central benchmark is confidence: after moving, zooming, comparing, filtering or changing resource context, a planner should never wonder whether the schedule itself changed or only the view changed.

---

# 18. Official DHTMLX references used

- Overview / interface: https://docs.dhtmlx.com/gantt/guides/overview/
- Product overview: https://docs.dhtmlx.com/gantt/
- Edition comparison: https://docs.dhtmlx.com/gantt/guides/editions-comparison/
- v10 migration: https://docs.dhtmlx.com/gantt/migration/
- Grid columns: https://docs.dhtmlx.com/gantt/guides/specifying-columns/
- Layout: https://docs.dhtmlx.com/gantt/guides/layout-config/
- Zoom: https://docs.dhtmlx.com/gantt/guides/zoom/
- Drag and drop: https://docs.dhtmlx.com/gantt/guides/dnd/
- Links: https://docs.dhtmlx.com/gantt/guides/link-properties/
- Critical path: https://docs.dhtmlx.com/gantt/guides/critical-path/
- Baselines: https://docs.dhtmlx.com/gantt/guides/inbuilt-baselines/
- Markers: https://docs.dhtmlx.com/gantt/guides/markers/
- Resource management: https://docs.dhtmlx.com/gantt/guides/resource-management/
- Keyboard navigation: https://docs.dhtmlx.com/gantt/guides/keyboard-navigation/

