# APS Gantt Workbench Overhaul Requirements

**Status:** Guiding product and interaction specification  
**Applies to:** `claude/project-status-review-o2dx1j` and successor branches  
**Scope:** The central APS Planning Workbench Gantt, its resource grid, timeline, interaction engine, comparison layers, resource/capacity views, planning context, and execution/recovery overlays  
**Benchmark:** DHTMLX Gantt v10 interaction and rendering behavior, adapted to APS finite-capacity manufacturing semantics  
**Decision:** APS will build and own this capability. DHTMLX is a behavioral/reference benchmark, not a dependency or data model to copy.

---

## 1. Purpose and product thesis

The Gantt is not one visualization among many in APS. It is the primary planning instrument. A planner should be able to spend most of the working day in this surface without repeatedly leaving it for context, diagnosis, comparison, or safe schedule manipulation.

The current workbench contains many useful concepts, but it is not yet a mature Gantt control. Several features match DHTMLX terminology while only implementing a thin visual approximation. For this document, a feature is **not complete because a button, line, block, or boolean exists**. It is complete only when all three layers are correct:

1. **Visual primitive:** the planner can clearly see the relevant object/state.
2. **Interaction behavior:** mouse, keyboard, focus, scrolling, zooming, dragging, selection, cancellation, and feedback behave predictably under real planning use.
3. **APS semantics:** the action reflects finite-capacity, routing, material, resource, campaign, thermal, time-fence, execution, and plan-version rules. Visual actions never bypass the planning engine.

DHTMLX is useful because it demonstrates mature interaction mechanics: a synchronized grid/timeline, composable layouts, resizers, rich drag modes, keyboard navigation, smart rendering, multiple scale levels, fit-to-view, resource diagrams/histograms, baselines, markers, dependency links, and extensive editability controls. APS must reproduce the **quality of interaction**, while replacing DHTMLX's WBS/project-management semantics with APS production semantics.

### 1.1 Core principle

> **The APS Gantt is a finite-capacity resource-time control surface, not a project WBS chart.**

Resources and process groups form the default vertical hierarchy. Operations occupy resource time. Campaigns, heats, demand, material, baselines, actuals, constraints, downtime, and planning fences are synchronized layers over the same schedule coordinate system.

### 1.2 Normative language

- **MUST** / **MUST NOT**: release-blocking requirement.
- **SHOULD** / **SHOULD NOT**: expected behavior unless a documented domain reason overrides it.
- **MAY**: optional enhancement.
- **P0**: foundation; required before claiming the Gantt overhaul complete.
- **P1**: required for planner-grade release.
- **P2**: important advanced capability.
- **P3**: deferred / opportunistic.

---

# 2. Benchmark corrections: problems in the previous DHTMLX feature list

The earlier feature inventory was useful as a first sweep, but it must not be used as an implementation checklist without correction.

## 2.1 Version and license correction

DHTMLX Gantt v10 changed the free edition from the former GPL v2 distribution to **Community under MIT**. Previous v9.x and earlier free versions remain GPL v2. Any document describing the current Community edition simply as “GPLv2” is stale.

Reference: https://docs.dhtmlx.com/gantt/guides/editions-comparison/  
Migration: https://docs.dhtmlx.com/gantt/migration/

This licensing change does not affect the APS decision: APS is not adopting DHTMLX. It matters because the benchmark must be based on the current product rather than an outdated feature matrix.

## 2.2 Feature-tier corrections

The current DHTMLX comparison lists, among other things:

- grid column and grid resizing from the UI in Community and PRO;
- per-column sorting in Community and PRO;
- inline grid editing in Community and PRO;
- keyboard navigation in Community and PRO;
- markers in Community and PRO;
- smart rendering in Community and PRO;
- timeline click-drag scrolling in Community and PRO;
- resource management, multi-task selection, multi-task horizontal dragging, critical path, baselines/deadlines, unscheduled tasks, grouping, undo/redo, resource/task calendars and dynamic loading as PRO capabilities.

The edition tier is not a product requirement for APS, but incorrect tier information is a warning that the old list was assembled at the label level rather than from current detailed behavior.

## 2.3 “Have” is not a binary status

The old list marks several APS capabilities as “Have” when the current implementation only shares the name:

| Named capability | Current APS reality | Requirement verdict |
| --- | --- | --- |
| Drag rescheduling | A block is translated with the pointer, any lane can visually become a drop target, drop position becomes target start, then server validation runs. Pointer grab offset is not preserved. | **Not complete. Rebuild interaction.** |
| Critical path / tight chain | `IsTightChain` checks whether a predecessor ends within about one minute of the operation start. It does not calculate total/free slack or identify the binding finite-capacity constraint. | **Not critical path. Replace with APS binding-chain model.** |
| Baseline | Dashed blocks are drawn for comparison changes. It is not a complete baseline rendering model and has no compare-row mode. | **Partial. Expand.** |
| Split/resizable layout | Current schedule has a fixed `176px` resource column. | **Not complete. Build real synchronized split layout.** |
| Zoom / Fit | APS exposes fixed 8 h / 1 d / 3 d / 7 d / Fit windows. Fit is content min/max plus padding. | **Useful primitive, not DHTMLX-grade zoom-to-fit.** |
| Resource assignment | Alternative resources exist in the inspector/read model. | **Domain data exists; Gantt interaction needs eligible-lane semantics.** |
| Undo/redo | Plan-version switching exists, but command history is duplicated in page/state code. | **Semantics valuable; state architecture must be consolidated.** |
| Unscheduled panel | APS has a demand queue. DHTMLX “unscheduled tasks” are dated task entities without schedule dates. | **Different concepts. Do not equate them.** |

## 2.4 The correct way to benchmark DHTMLX

For each feature we must ask:

1. What is visually rendered?
2. What exact pointer/keyboard gesture initiates it?
3. What remains visible during interaction?
4. How are valid/invalid targets communicated before drop?
5. What is snapped, to which anchor, and at what resolution?
6. What happens near viewport edges?
7. How is focus preserved?
8. What can be cancelled and how?
9. What is immediately local versus server/solver validated?
10. What happens when the operation is readonly, frozen, running, completed, or single-sourced?
11. How does the behavior survive filtering, collapsed groups, scrolling, resizing, and zoom changes?
12. How is the state represented accessibly without relying on color?

A capability should only be marked complete when these behaviors are specified and tested.

---

# 3. Audit of the current APS workbench

This section records the starting point, not a criticism of the domain architecture. Several strong ideas should be preserved.

## 3.1 Current useful foundations — keep and deepen

### KEEP-01 — Gantt-first workbench

The workbench already treats the schedule as the main planning surface. This is correct and remains non-negotiable.

### KEEP-02 — Immutable released plan and child replans

Direct manipulation ultimately creates or switches to persisted Plan Versions rather than mutating historical facts. This is stronger than a generic client-side Gantt undo model and must remain the authority.

### KEEP-03 — Staged move and impact validation

The current `PlanningMoveProposal` → `ValidateMoveAsync` → impact → apply/replan workflow is the right safety model. The interaction around it must become much richer, but the separation between **proposal** and **committed plan** is correct.

### KEEP-04 — Queue and inspector as temporary workbench surfaces

Left queue and right inspector overlays preserve the Gantt as the central surface. The pattern is good. They should be resizable, modeless, keyboard reachable, and selection-aware.

### KEEP-05 — Frozen-fence concept

A frozen planning zone is a real APS concept and deserves a first-class visual layer. It must remain distinct from current time, stable/firm horizons, and resource downtime.

### KEEP-06 — Process-specific operation identity

Process-stage coloring can help rapid scanning if it remains restrained and if execution/commitment state is also expressed with shape, outline, icon, or text. Process color must not be overloaded to communicate every other state.

### KEEP-07 — Baseline and comparison intent

The existing comparison model is strategically correct. Baseline rendering needs to become a dedicated layer with multiple density modes rather than a dashed afterthought.

### KEEP-08 — Focused dependency view

Hiding a spaghetti network by default is correct. DHTMLX can render a full project network, but APS should default to the selected operation's relevant chain and let the planner deliberately broaden it.

## 3.2 Current implementation problems — must be removed

### FIX-01 — Drag anchor bug

The current JavaScript snaps the **pointer x-coordinate** to the time grid and passes that ratio as the target start. If the user grabs the middle or right side of a bar, the operation start jumps to the cursor location rather than preserving the grab offset.

**Required behavior:** capture `grabOffsetMinutes = pointerTimeAtDown - operationStart`; while moving, candidate start = pointerTime - grabOffset; then snap the candidate start. The bar must feel attached at the exact point the planner grabbed.

### FIX-02 — Moving the real bar instead of a proposal ghost

The current interaction applies a CSS transform to the real operation block during drag. This removes the visual anchor for “where it came from.”

**Required behavior:** keep the source operation visible in its original position. Render a distinct elevated **candidate ghost** that follows the pointer. The source may dim slightly but must remain fixed. After drop, the candidate remains staged until validation/apply or cancel.

### FIX-03 — Invalid lanes look valid until after drop

Any resource lane under the pointer can receive the current drop-target highlight.

**Required behavior:** when drag starts, compute eligible target resources from operation resource options and domain state. Eligible lanes get a subtle target affordance; known-ineligible lanes become no-drop/dimmed. If eligibility itself requires server evaluation, represent “checking” explicitly rather than pretending validity.

### FIX-04 — Fixed 15-minute snap

Current drag is hardwired to 15 minutes.

**Required behavior:** support a planner snap policy: `Shift boundary`, `1 h`, `30 min`, `15 min`, `5 min`, and `Free` where allowed. The selected snap mode must be visible and persisted as a view preference. Modifier keys may temporarily bypass/change snap.

### FIX-05 — Horizontal-only drag autoscroll

Current drag autoscroll only changes `scrollLeft`.

**Required behavior:** autoscroll both axes. Edge zones must be proportional and speed must ramp smoothly with proximity to the edge. Autoscroll must stop immediately on pointer-up, pointer-cancel, Escape, focus loss, or window blur.

### FIX-06 — False “critical path” semantics

Current “tight chain” is a temporal adjacency test. DHTMLX critical path is based on scheduling slack; APS needs something even more domain-aware.

**Required behavior:** rename the product concept to **Binding chain** or **Schedule-critical chain** unless/until a precise solver-backed definition is implemented. Each critical/binding operation must expose its computed slack and the constraint making it binding: predecessor, resource sequence, campaign/cast sequence, material receipt, queue/thermal window, commitment/fence, or due-date pressure.

### FIX-07 — Single fixed resource column

The current `176px` left column cannot carry planner-grade resource context.

**Required behavior:** a real grid/timeline split with draggable divider, min/max widths, optional columns, persistent widths, synchronized header/body, and horizontal grid scrolling when columns exceed available width.

### FIX-08 — Single-tier time axis

Current tick logic produces one row of labels and coarse fixed step choices.

**Required behavior:** at least two synchronized scale tiers for most zoom levels, with scale semantics changing by zoom. Examples are defined later.

### FIX-09 — Straight dependency lines

Current dependencies are straight dashed SVG lines from lane center to lane center. They lack ports, arrows, path routing, overlap management, constraint type, lag, and hover explanation.

**Required behavior:** orthogonal routed paths anchored to operation start/end ports with arrowheads and semantic hover/focus details. Do not make manufacturing routing editable by dragging links unless an explicit domain use case is approved.

### FIX-10 — Overloaded operation cards

The current bar tries to fit IDs, time range, tonnage, grade, heat, execution badges and other state into a small rectangle using very small text.

**Required behavior:** content adapts to available **pixel width**. A narrow bar is a mark, not a miniature form. Full detail belongs in tooltip/inspector.

### FIX-11 — Due-marker forest

Every grouped exact due date can become a vertical marker.

**Required behavior:** marker density control. At wide zoom, due dates aggregate by bucket; focused demand gets an exact marker; only exceptional/selected due dates become visually strong.

### FIX-12 — Duplicate state ownership

`PlanningWorkbenchState`, `PlannerCockpitState`, and page-private history currently overlap responsibility for drawers, analysis, and undo/redo.

**Required behavior:** one canonical workbench state graph with explicit substate for viewport, selection, layers, docks, proposal, and history. Page components consume state; they do not invent parallel truth.

### FIX-13 — Component-plan drift

The implementation plan describes dedicated Gantt components, but much of the real Gantt still lives in one large `FiniteSchedule.razor`.

**Required behavior:** split rendering and interaction by responsibility before adding large feature volume. See architecture requirements.

---

# 4. Target workbench anatomy

The layout must feel like a serious planning instrument, not a dashboard around a small chart.

## LYT-001 — Gantt owns the viewport — P0

After desktop menu/scenario controls, the Gantt MUST receive all remaining width and height. It MUST NOT live inside a centered page container, oversized card, decorative dashboard shell, or large padded canvas.

## LYT-002 — Compact global chrome — P0

Persistent non-Gantt chrome SHOULD consume no more than roughly 96–112 px vertically at normal desktop width, excluding temporary status/impact surfaces. Controls should collapse into menus rather than permanently consume rows.

## LYT-003 — Synchronized resource grid and timeline — P0

The central surface MUST be a true two-part control:

- left: resource/process grid;
- right: timeline;
- a draggable divider between them;
- header and body widths remain synchronized;
- vertical scroll is shared;
- horizontal timeline scroll does not shift the resource grid;
- optional resource-grid horizontal scroll is independent;
- resizing either pane must not corrupt time coordinates.

This follows the interaction discipline of DHTMLX's grid/timeline layout, not its WBS task semantics.

## LYT-004 — Resource grid default width — P0

Default width: **320 px**.  
Minimum: **220 px**.  
Maximum: **45% of available Gantt width**.  
Compact single-column mode MAY shrink to ~160–180 px by explicit planner choice.

The divider MUST have a visible hover/focus target wider than the rendered hairline.

## LYT-005 — Resizable secondary panels — P1

Queue, inspector, capacity/load panel, and bottom analysis dock SHOULD be individually resizable and preserve size as local UI preference.

Opening them MUST NOT silently call Fit or change the selected time range. The timeline viewport remains logically stable unless the planner explicitly requests a fit operation.

## LYT-006 — Density modes — P1

Provide at least:

- **Compact**: ~44–52 px resource rows;
- **Standard**: ~56–64 px;
- **Expanded**: ~72–88 px for comparison/actual layers.

For a very small number of visible resources, rows MAY expand to fill the viewport, but there MUST be a maximum so bars do not become enormous.

Individual freeform row-height dragging is P2; consistent density modes are preferable initially.

---

# 5. Timeline and navigation requirements

DHTMLX's zoom system is more than a set of buttons. It has named levels, configurable subscales, viewport-aware `zoomToFit`, and a `resetZoom` that returns to the previous scale. APS must achieve equivalent planning ergonomics.

## TIM-001 — Unified time-coordinate engine — P0

All timeline layers MUST use one time-to-pixel transform:

`x = f(timestamp, viewportStart, viewportEnd, timelinePixelWidth)`

and one inverse transform:

`timestamp = f⁻¹(x)`

Operations, baselines, markers, fences, downtime, dependency ports, campaign spans, drag ghosts, capacity buckets, due markers and selection must use the same transform.

Percentage calculations duplicated across Razor elements are not an acceptable long-term coordinate engine.

## TIM-002 — Named zoom levels — P0

Minimum supported levels:

1. **30 min / Detail**
2. **Shift (8 h)**
3. **Day**
4. **3 Days**
5. **Week**
6. **2 Weeks**
7. **Month**
8. **Fit**

The exact horizon may exceed a month, but the scale system must not be hardcoded to only four windows.

## TIM-003 — Multi-tier scale — P0

The header MUST normally contain two tiers. Recommended defaults:

| Zoom | Upper tier | Lower tier |
| --- | --- | --- |
| Detail | date + shift | 15/30 minute or hour ticks |
| Shift | date + shift label | 30 min / 1 h |
| Day | day + date | 1–2 h |
| 3 Days | day + date | 2–4 h |
| Week | week/date context | day |
| 2 Weeks | month/week | day |
| Month | month/week | day or multi-day |

The upper tier must remain readable during horizontal scroll and should use sticky/continued labels where a period began offscreen.

## TIM-004 — Pointer-anchored wheel zoom — P0

Ctrl/Cmd + mouse wheel SHOULD zoom around the timestamp currently under the pointer, not around the center of the viewport.

The timestamp under the pointer should remain under approximately the same pixel after zoom, subject to horizon bounds.

## TIM-005 — Pan behavior — P0

Support:

- horizontal scrollbar;
- Shift + wheel horizontal scroll;
- click-drag pan from empty timeline background (configurable mouse button/gesture);
- toolbar Earlier/Later actions;
- keyboard pan;
- touchpad natural horizontal scroll.

Dragging an operation must never accidentally trigger timeline panning.

## TIM-006 — Fit modes — P0

Provide:

- Fit all scheduled content;
- Fit visible resources;
- Fit selection;
- Fit campaign;
- Fit demand/order chain;
- Fit explicit date range.

Fit MUST choose the most detailed available scale that keeps the requested range inside the **actual timeline pixel width** without unnecessary horizontal scroll. It must account for the current grid width.

## TIM-007 — Reset previous zoom — P0

After a Fit operation, a `Reset zoom` / second Fit action MUST restore the exact pre-fit viewport/zoom state. This matches a key DHTMLX v10 behavior and prevents Fit from being a destructive navigation action.

## TIM-008 — Zoom stability — P0

Changing zoom MUST NOT:

- change selection;
- clear queue/inspector context;
- alter resource ordering;
- shift the logical anchor unexpectedly;
- cause bars to overlap solely because a minimum CSS width was imposed.

A visually clickable minimum hit target may be larger than the true bar width, but the **rendered duration geometry** must remain time-accurate.

## TIM-009 — Current time vs planning reference time — P1

APS must distinguish:

- wall-clock “Now”;
- plan/reference actuals time;
- frozen-fence end;
- stable/firm-fence end if configured.

These are not the same marker. Each gets its own label/tooltip and visual treatment.

## TIM-010 — Marker manager — P1

Markers are a first-class layer with:

- point or interval markers;
- priority/z-order;
- label collision rules;
- grouped/aggregated due-date markers;
- focus/selection emphasis;
- show/hide controls by category.

A marker must never become an unexplained colored line.

---

# 6. Resource grid and hierarchy

## GRD-001 — Default hierarchy — P0

Default row hierarchy:

`Plant/Area → Process Unit Type → Physical Resource`

Example:

- SMS
  - EAF
    - EAF-01
    - EAF-02
  - LRF
    - LRF-01
    - LRF-02
  - CCM
    - CCM-01
    - CCM-02

Groups can collapse without changing schedule data.

## GRD-002 — Grid columns — P0

The resource grid SHOULD support configurable columns. Initial set:

- Resource code/name;
- State / availability;
- Occupied hours in visible range;
- Utilisation % in visible range;
- Operation count;
- Next scheduled start / next job;
- Exception count;
- Optional process group / area if hierarchy is flattened.

The default view should remain compact. Not every column must be shown.

## GRD-003 — Column resize — P0

Every shown column SHOULD support drag resize within sensible min/max width. Width persists locally. Double-click divider MAY auto-size to visible content.

## GRD-004 — Column show/hide — P1

Column chooser with reset-to-default. Hidden columns are a display preference only and MUST NOT change planning logic.

## GRD-005 — Sorting is view-only — P0

Sorting resources MUST NOT change solver sequence, priority, dispatch sequence, or persisted schedule.

Default sort: process order then resource code.

Additional sorts:

- resource code/name;
- utilisation;
- operating state;
- exception count;
- next operation;
- late-risk exposure.

The active sort and direction must be visible in the header.

## GRD-006 — Manual row reorder is view-only — P2

If provided, drag-reordering resources only changes the user's view preference. It MUST NEVER imply scheduling priority.

A safer P1 alternative is **Pin resource** / **Favorites**, keeping canonical process order for the remaining rows.

## GRD-007 — Filtering safety — P1

Filtering/hiding resource lanes must show a persistent count such as `8 of 13 resources shown`. If hidden lanes contain critical exceptions, a warning badge must say so.

`Reset filters` must be one action.

## GRD-008 — Inline editing restriction — P0

Do not use Gantt grid inline editing for resource master data. Resource capability/calendar/master configuration belongs in the appropriate administrative workspace.

The Gantt grid may expose safe **view controls** inline, not master-data mutation.

---

# 7. Operation bar rendering

## BAR-001 — Time-accurate geometry — P0

Operation body width must represent actual scheduled duration exactly within the current viewport. Do not impose a minimum percentage that visually converts a 5-minute operation into a materially longer duration.

For accessibility/hit testing, a transparent interaction target may extend beyond a tiny bar.

## BAR-002 — Adaptive content by pixel width — P0

Content is selected by actual rendered width:

- **< 24 px:** no text; status/selection conveyed by outline/shape; tooltip on hover/focus.
- **24–64 px:** short business identifier only.
- **64–120 px:** identifier + primary grade/heat signal.
- **120–220 px:** identifier + grade/section + compact timing/quantity.
- **> 220 px:** richer two-line content where useful.

Do not squeeze five lines into a bar using 9–10 px typography.

## BAR-003 — Primary identity — P0

Bar label depends on current mode/lens but must always use business identity:

- Plan: production order / planning order identity;
- Campaign: campaign + heat sequence;
- Execution: operation/process + work-order identity where available;
- Recovery: operation + deviation signal.

Internal GUIDs and planning keys remain non-user-facing.

## BAR-004 — Process versus status encoding — P0

Process stage may own the bar's base hue. Execution state, commitment state, selection, frozen status, exception and criticality must use secondary channels such as:

- outline;
- hatch/pattern;
- icon;
- progress fill;
- border style;
- corner marker;
- text/tooltip.

Color alone is insufficient.

## BAR-005 — Setup/changeover segments — P1

APS SHOULD visually distinguish schedule occupancy that is not primary processing:

- setup;
- grade/section changeover;
- cleaning/conditioning where modeled;
- transfer/queue when it occupies a constrained resource.

Preferred representation: a narrow preceding/attached segment with a distinct neutral pattern or texture, not a second unrelated saturated bar.

The inspector must show exact minutes and reason.

## BAR-006 — Progress — P1

Progress inside a bar represents **authoritative execution progress only**. A planner must not drag a progress knob to fabricate actual progress.

If production actuals are not available, do not show a synthetic percent as though it were actual.

## BAR-007 — Hover tooltip — P0

After a short delay (~250 ms), hover/focus tooltip should show:

- operation/business ID;
- process;
- resource;
- planned start/end/duration;
- setup/changeover if applicable;
- quantity;
- grade/cross section;
- campaign/heat;
- linked demand/order;
- commitment/execution state;
- due/slack/binding signal;
- material/thermal/resource warning summary.

Tooltip is primarily informational and should not trap the pointer. Actions belong in context menu/inspector.

## BAR-008 — Selection visibility — P0

Selected bar must remain unmistakable in light/dark themes, independent of process color. Use focus ring/outline + optional elevated shadow. Selection must survive zoom, pan and opening overlays.

---

# 8. Selection and bulk planning

## SEL-001 — Single selection — P0

Click selects an operation and synchronizes inspector, queue focus and workspace selection.

Escape clears transient focus/selection according to a deterministic priority order: context menu → active drag → staged proposal → transient panel focus → selected operation.

## SEL-002 — Multi-selection — P1

Support Ctrl/Cmd-click toggling operations into a selection set.

Shift-click SHOULD select a contiguous visible sequence where the concept is unambiguous (for example consecutive operations in a resource lane or campaign sequence). It must not select an arbitrary rectangle across unrelated resources without clear semantics.

## SEL-003 — Selection summary — P1

For 2+ selected items, show compact summary:

- count;
- total occupied time;
- resources involved;
- campaigns/orders involved;
- earliest start/latest end;
- eligibility for bulk actions.

## SEL-004 — Bulk move — P1

Horizontal bulk move preserves relative offsets. The proposal is one atomic planning command; partial application is not silently allowed.

If moving a campaign or cast sequence, prefer a **domain-level bulk command** preserving campaign semantics over a blind geometric offset of unrelated operation records.

## SEL-005 — Bulk resource reassignment — P2

Only available if a meaningful one-to-one resource mapping can be established and every operation remains eligible. Otherwise guide the planner to `Repair selection` rather than pretending arbitrary vertical multi-drag is safe.

---

# 9. Direct manipulation and drag behavior

This is one of the areas where DHTMLX earns its maturity. APS must specify the whole gesture lifecycle.

## DND-001 — Pointer-down state — P0

On operation pointer-down capture:

- operation ID;
- source resource;
- original start/end;
- pointer coordinates;
- pointer timestamp;
- grab offset from operation start;
- current viewport;
- eligible resources;
- current snap mode;
- operation duration;
- current selection set.

Do not begin a drag until movement exceeds a small threshold.

## DND-002 — Source remains visible — P0

Once drag activates:

- original operation stays fixed;
- original may dim to ~45–60% opacity;
- candidate ghost follows pointer;
- candidate carries target resource, start/end and time delta;
- source-to-candidate relationship remains obvious.

## DND-003 — Candidate ghost — P0

Ghost should be visually elevated and semantically different from a committed operation. It must never be mistaken for saved schedule data.

Ghost label should at minimum show target start and delta (e.g. `14:30 · +2h15m`) when width allows.

## DND-004 — Preserve grab offset — P0

Candidate start = snapped `(pointer timestamp - initial grab offset)`, not snapped pointer timestamp.

This is a release-blocking acceptance test.

## DND-005 — Eligible target lanes — P0

On drag start:

- eligible lanes receive a subtle positive target affordance;
- current/source lane remains valid if time move allowed;
- known ineligible lanes visibly reject;
- completed/running/fixed operations do not enter drag state;
- resource groups themselves are not drop targets.

## DND-006 — Snap guide — P0

Render a vertical snap guide through the target timeline and display snapped timestamp. Guide must use the same time-coordinate transform as the scale.

## DND-007 — Configurable snap — P0

Snap modes defined in TIM/DND must be consistent with keyboard move commands and manual target-time input.

## DND-008 — Edge autoscroll — P0

Horizontal and vertical edge autoscroll with acceleration. Candidate calculation updates continuously as viewport scrolls.

## DND-009 — Escape cancellation — P0

Escape immediately cancels drag, removes ghost/guide/target states, restores pointer/cursor and does not call server validation.

Pointer cancel, browser blur and lost capture must do the same safely.

## DND-010 — Drop stages, never commits — P0

Drop creates a local `PlanningMoveProposal` and immediately renders the staged candidate. It MUST NOT mutate the authoritative plan.

Then:

1. run cheap local validations;
2. show `Checking feasibility…` if server validation is pending;
3. invoke canonical validation;
4. show blockers/warnings/impact;
5. enable Apply only if policy permits;
6. Apply creates the persisted replan/child version.

## DND-011 — Validation feedback latency — P1

Local feedback target: < 50 ms.  
Server validation target: < 500 ms for a normal single move.  
If longer, show active calculation state and allow cancel/discard.

## DND-012 — Horizontal resize — P2 / domain-gated

Generic DHTMLX task resize is **not automatically appropriate** for manufacturing operations whose duration derives from process standards, quantity, speed, setup, or route.

Default: no resize handles.

Resize may be enabled only for explicitly overrideable objects, such as:

- planned downtime/maintenance event;
- planner-entered hold window;
- operation duration override when the domain model explicitly supports it.

Any duration override needs min/max bounds, reason, validation, audit trail, and recalculation.

## DND-013 — Click-drag creation — P3 / event-only

Do not create manufacturing operations by drawing arbitrary bars.

Click-drag creation MAY be used later for planned resource events (maintenance, outage, block, hold) if such a domain command exists.

---

# 10. Dependencies, precedence, queue windows and binding chains

## DEP-001 — Rich dependency DTO — P0/P1

Replace bare predecessor-string relationships with a link model capable of representing:

- source planning key;
- target planning key;
- relation kind (FS/SS/FF/SF where applicable);
- minimum lag;
- maximum lag / queue limit where applicable;
- hard/soft enforcement;
- reason/category (`Routing`, `CampaignSequence`, `ResourceSequence`, `Transfer`, `Thermal`, `Material`, etc.);
- free slack / total slack or APS equivalent;
- current violation status.

APS will likely use FS most often, but the renderer and contract must not assume every dependency is identical.

## DEP-002 — Focused links by default — P0

Default: hidden or selected-chain only.

Modes:

- Off;
- Selected chain;
- Selected predecessors;
- Selected successors;
- Binding chain;
- All visible (explicit expert action only).

## DEP-003 — Routed connectors — P0

Connectors use orthogonal/elbow routing with:

- start/end ports appropriate to relation type;
- arrowhead at target;
- consistent gap around operation bodies;
- hit/hover target wider than visible stroke;
- link focus state;
- clipping at viewport edges with continuation cue when counterpart is offscreen.

Straight center-to-center lines are not sufficient.

## DEP-004 — Link explanation — P1

Hover/focus displays, for example:

`LRF → CCM · FS · min 5 min · max 45 min thermal window · 12 min current wait · 33 min headroom`

For resource sequence:

`EAF-01 sequence · HEAT-104 follows HEAT-103 · changeover 20 min`

## DEP-005 — Links are mostly readonly — P0

Unlike a generic project Gantt, planners should not drag handles to invent manufacturing route dependencies. Link creation/editing is disabled unless a future explicit planning use case is designed.

## DEP-006 — Binding chain, not fake CPM — P1

DHTMLX critical path highlights zero/negative-slack project tasks. APS needs a finite-capacity equivalent derived from the planning model.

A binding operation must expose:

- computed time slack/headroom;
- constraint class that consumes the slack;
- affected delivery/campaign/order;
- whether moving it causes downstream impact;
- whether an alternative resource/material arrival can release the constraint.

The name **Binding chain** is preferred unless the implementation truly satisfies a defined critical-path algorithm.

## DEP-007 — Thermal and queue windows — P1

Where process transfers have min/max queue or thermal limits, represent the admissible window in detail/focus modes. The UI should show **remaining headroom**, not only a red violation after the fact.

---

# 11. Resource capacity and workload

DHTMLX separates the main task Gantt from a synchronized `resourceGrid` plus either `resourceTimeline` or `resourceHistogram`. Critically, its own docs state that the component does not calculate resource load by itself; applications provide allocation/capacity logic. APS already owns the domain calculation and should therefore build a stronger manufacturing-specific view.

## CAP-001 — Synchronized load panel — P0/P1

Provide an optional capacity/load panel sharing exactly the same horizontal time scale/scroll as the main Gantt.

Recommended panel modes:

1. **Utilisation timeline:** busy/idle/blocked per resource by time bucket.
2. **Histogram:** scheduled occupancy versus available capacity.
3. **Changeover view:** processing versus setup/changeover/downtime contribution.

## CAP-002 — Bucket size follows zoom — P1

Examples:

- detail/shift: 15–60 min buckets;
- day: hourly/2-hour;
- 3-day/week: shift/day buckets;
- 2-week/month: day buckets.

Do not aggregate a 30-day histogram at the same resolution as an 8-hour shift.

## CAP-003 — Capacity definition — P1

Each bucket's available capacity must reflect:

- resource calendar;
- planned downtime;
- resource operating state;
- configured efficiency/capability policy where appropriate;
- committed/frozen work;
- scheduled processing;
- setup/changeover occupancy.

The UI must state which capacity basis is being shown.

## CAP-004 — Click-through — P1

Clicking a load bucket focuses the corresponding resource/time range and highlights the operations producing the load. The planner should be able to move directly from “92% occupied” to the operations causing it.

## CAP-005 — Overload semantics — P1

A strict finite schedule may normally prevent simultaneous machine overload, so “overload” must not be a meaningless red bar. APS should distinguish:

- scheduled occupancy near limit;
- actual execution overrun causing projected conflict;
- soft capacity violation;
- unavailable resource with committed future work;
- bottleneck due to demand concentration despite no mathematical overlap.

## CAP-006 — Inline lane load — P2

Resource row may include a tiny visible-window utilization meter, but it must not replace the synchronized load panel for temporal diagnosis.

---

# 12. Calendars, downtime and planning fences

## CAL-001 — Resource calendars as visible truth — P0/P1

Resource unavailability must be rendered directly on the resource lane, not hidden in a settings screen.

Examples:

- planned maintenance;
- breakdown;
- shift/calendar closure;
- reserved engineering window;
- unavailable process configuration.

## CAL-002 — Calendar interval layer — P1

Unavailable intervals use a neutral hatch/recessed band with explicit tooltip and reason. The visual must not resemble a scheduled operation.

## CAL-003 — Do not misuse “weekends” — P0

Steel operations may be continuous. Generic weekend shading is not a useful default. Render actual configured resource calendars instead.

## CAL-004 — Do not compress elapsed time by default — P0

DHTMLX can hide/skip non-working units. APS SHOULD preserve real elapsed time by default because queue time, cooling, thermal headroom, transfer and maintenance context depend on actual clock distance.

A compressed-calendar display MAY exist later as an analysis mode, but must be clearly labeled and disabled for baseline/actual comparison where distortion would mislead.

## CAL-005 — Fence bands — P0

Render planning commitment zones across the timeline header and body:

- Actual/completed history;
- Frozen;
- Firm/stable;
- Flexible.

Zones need labels at the header boundary and explanatory tooltip. Do not represent all zones as variations of red.

## CAL-006 — Fence interaction — P0

Dragging into/within a protected zone must show its policy before drop. Authorized frozen override is an explicit audited action, not a generic checkbox hidden in the inspector after the user has already tried a move.

---

# 13. Baseline and scenario comparison

DHTMLX supports baseline rendering in the task row, a shared separate subrow, or individual subrows. APS should adopt this density choice while keeping baseline immutable.

## CMP-001 — Complete baseline layer — P0/P1

When baseline is enabled, every comparable baseline operation in scope should be available to render, not only changed items if that makes unchanged context disappear.

## CMP-002 — Compare modes — P1

At minimum:

- **Ghost overlay:** thin neutral baseline directly beneath/behind current bar;
- **Compare subrow:** baseline in a dedicated subrow when detailed comparison is requested;
- **Changed only:** filter to added/removed/moved/resource-changed/retimed operations.

## CMP-003 — Resource-change baseline — P1

If an operation moved to another resource, baseline ghost stays on the **original resource lane** while current operation appears on the new lane. The connection/delta should be discoverable on selection.

## CMP-004 — Exact delta — P1

Inspector/comparison dock shows:

- start delta;
- end delta;
- resource changed from/to;
- duration delta;
- delivery delta;
- setup/changeover delta;
- material/capacity delta where calculated.

## CMP-005 — Added/removed operations — P1

Added and removed operations must be distinguishable without color alone. For example, added current bar gets `+`; removed baseline gets a strikethrough/removed marker.

## CMP-006 — Baseline is readonly — P0

Baseline objects never receive drag handles or edit affordances.

---

# 14. APS-native layers beyond a generic Gantt

These are areas where APS should exceed DHTMLX rather than mimic it.

## APS-001 — Campaign span layer — P1

Campaign mode displays campaign spans/sequence bands aligned to operations. A campaign span is not a generic project summary task. It represents manufacturing grouping and should show:

- campaign number;
- grade/sequence family;
- heat count;
- caster/section context;
- due exposure;
- transition/changeover burden;
- campaign status/commitment.

## APS-002 — Heat identity — P0/P1

Heat sequence is first-class. Selecting a heat highlights the complete EAF→LRF→VD?→CCM chain and related downstream billet/rolling demand when lineage exists.

## APS-003 — Material availability layer — P1

Material view must be able to show, relative to operation time:

- planned receipts;
- required consumption;
- reservation/pegging;
- running balance;
- shortfall;
- earliest feasible supply;
- affected operations.

Do **not** hide future work merely because material is not in inventory now. Planned/future supply and explicit shortage are part of the plan.

## APS-004 — Material shortfall is not unscheduled disappearance — P0

An operation/order that needs material not currently available should remain visible where the scenario plans it, with shortage/supply risk clearly represented, unless the solver genuinely cannot schedule it. APS planning is forward-looking.

## APS-005 — Alternative-resource flexibility — P1

For operations with alternative eligible resources (for example an LRF-completed heat capable of going to another CCM), the Gantt should expose flexibility:

- eligible target resources;
- current assignment;
- alternative penalty/cost if modeled;
- resource state;
- downstream impact;
- single-source warning only when there truly is one eligible resource.

## APS-006 — Sequence/changeover signal — P1

Resource timeline should make sequence-sensitive transitions discoverable. On selection, show predecessor/successor grade/section and applicable changeover/setup. Large transition penalties should be visible before a planner moves work into a poor sequence.

## APS-007 — Waiting/queue gap — P1

Between connected process operations, selection should expose actual planned wait and min/max permissible wait. If a wait approaches max thermal/queue threshold, this is a warning before violation.

## APS-008 — Demand due exposure — P1

Selecting demand highlights all operations that serve it and the exact required date. If an operation serves several orders, the inspector must make allocation/pegging visible rather than pretending one bar belongs to one order only.

---

# 15. Queue, inspector and contextual actions

## CTX-001 — Queue remains modeless — P0

Demand/campaign/exception queues overlay or dock beside the Gantt without navigation away. Selection made from queue focuses the related schedule objects and, if needed, brings them into view.

## CTX-002 — Queue categories — P1

Demand queue should distinguish:

- new/unplanned;
- partially covered;
- late/projected late;
- material exposed;
- capacity constrained;
- excluded/held with reason;
- changed since baseline.

## CTX-003 — Drag from queue — P2

Dragging unscheduled demand into the Gantt must **not** create a raw operation at the pointer. If implemented, it stages a semantic “schedule this demand near here/on this resource family” request to the planning engine.

## CTX-004 — Inspector is modeless — P0

Inspector remains available while panning/zooming/selecting. It must not be a modal lightbox.

## CTX-005 — Inspector structure — P1

Recommended sections:

1. Identity
2. Plan
3. Actuals
4. Campaign/heat/order lineage
5. Resource alternatives
6. Material/thermal/queue constraints
7. Baseline delta
8. Scheduling explanation / why here
9. Actions
10. Change history

Only sections with data should expand by default.

## CTX-006 — Right-click context menu — P1

Operation context menu:

- Inspect;
- Focus chain;
- Fit selection;
- Compare with baseline;
- Move/reassign;
- Find alternate resource;
- Pin / unpin where supported;
- Repair selection / repair from here;
- Trace demand;
- Trace campaign/heat;
- Trace material;
- Copy business ID.

Unavailable commands remain visible but disabled with a concise reason when that helps discoverability.

Keyboard equivalent: Shift+F10 or context-menu key.

## CTX-007 — No fake actions — P0

A menu item is not considered implemented because it opens a toast saying “coming soon.” Either implement the domain command or clearly disable it.

---

# 16. Undo, redo, history and persisted planning truth

DHTMLX undo/redo stores old/new client task/link values. APS must retain the **interaction convenience** while using the stronger persisted Plan Version model.

## HIS-001 — One history owner — P0

Remove duplicate undo/redo stacks. Workbench command history has one state owner.

## HIS-002 — Semantic history entries — P1

History entry should say what it means, for example:

- `Move HEAT-104 · LRF-01 → LRF-02 · +45 min`
- `Repair Campaign C-017`
- `Pin CCM-02 assignment for Heat 104`

Not merely “previous Plan Version.”

## HIS-003 — Undo creates/activates valid persisted state — P0

Undo/redo must never delete historical Plan Versions. The UI may navigate version lineage or create a new child representation according to lifecycle design, but historical facts remain immutable.

## HIS-004 — Undo preview — P2

Tooltip/menu can show `Undo: <last semantic command>` and `Redo: <command>`.

## HIS-005 — Staged proposal is not history — P0

Dragging and validating without Apply must not create persisted version/history noise.

---

# 17. Execution and recovery overlays

## EXE-001 — Planned versus actual geometry — P1

Execution mode overlays actual start/end relative to planned schedule. Use distinct actual bar/segment or split geometry so the planner can see early/late start and duration overrun directly.

## EXE-002 — Completed/running protection — P0

Completed operations are fixed. Running operations retain actual start and protected resource commitment. Direct drag does not start for these states.

## EXE-003 — Running progress — P1

Running progress uses production actuals where possible. Projected completion may be rendered separately and explicitly labeled `Projected`.

## EXE-004 — Resource events — P1

Breakdown/downtime event overlays should immediately expose future scheduled work affected on that resource and support recovery-scenario entry.

## EXE-005 — Recovery scope — P1

Recovery mode visually distinguishes:

- completed/fixed;
- running/protected;
- frozen future;
- flexible future;
- changed/repaired;
- unresolved exception.

The planner must understand what can move before attempting drag.

---

# 18. Keyboard and accessibility

DHTMLX's useful model is that the whole Gantt receives focus as a single tab stop, then internal arrow/key navigation manages rows/cells. APS should adopt an analogous model without project-task shortcuts that are unsafe in manufacturing.

## A11Y-001 — Single entry focus — P0/P1

Tab enters the Gantt control. A second Tab can leave it or move to explicitly defined workbench control regions. Do not require hundreds of Tab presses through every operation.

## A11Y-002 — Internal navigation — P1

When Gantt has focus:

- Up/Down: previous/next visible resource or operation depending focus mode;
- Left/Right: previous/next operation or time movement in keyboard-move mode;
- PageUp/PageDown: larger vertical navigation;
- Home/End: first/last visible lane/operation;
- Space: select/toggle selection;
- Enter: inspect/focus selected operation;
- Esc: cancel current transient action;
- Alt+Left/Right: horizontal pan;
- Alt+Up/Down: vertical scroll;
- Ctrl/Cmd+Z / redo shortcut: workbench undo/redo.

Exact mapping must be documented in a shortcut panel.

## A11Y-003 — Keyboard move — P2

A selected eligible operation can enter `Move` mode via keyboard. Arrow keys adjust target time/resource using current snap, preview ghost appears, Enter stages/validates, Escape cancels.

## A11Y-004 — Accessible alternative table — P1

Provide a synchronized schedule table/list for assistive technology and dense textual review. Selection in table and Gantt is shared.

## A11Y-005 — ARIA and text equivalents — P0/P1

Every operation exposes an accessible name including identity, resource, start/end and state. Status cannot depend only on color.

## A11Y-006 — High contrast — P1

Themes must preserve distinguishability of selected, frozen, running, completed, warning, and baseline states under high contrast / forced-colors conditions.

---

# 19. Performance and rendering architecture

DHTMLX calls its optimization “smart rendering.” APS must implement equivalent viewport discipline because a planner Gantt that becomes sluggish under thousands of operations is functionally unusable.

## PERF-001 — Row virtualization — P0

Only visible resource rows plus overscan should mount expensive operation DOM. Collapsed groups must not render hidden children.

## PERF-002 — Time-window clipping — P0

Only operations intersecting visible time range plus modest horizontal overscan need full bar DOM. Offscreen dependencies may render continuation stubs rather than complete paths.

## PERF-003 — DOM budget — P0/P1

Target normal interactive DOM:

- < 500 mounted operation bars;
- < 1,500 time/grid/dependency visual primitives in normal view;
- capacity panel uses aggregated buckets rather than operation-per-cell duplication.

Numbers can be tuned after profiling, but performance work must have explicit budgets.

## PERF-004 — Interaction frame rate — P0

Pan, scroll, drag and zoom target 60 fps on a normal supported workstation. No server round-trip occurs on every pointermove.

## PERF-005 — Stable keys — P0

Bars/layers use stable planning keys internally so selection/focus does not disappear because a component was needlessly recreated during small state changes.

## PERF-006 — Cached geometry — P1

During an active drag/scroll frame, avoid repeated full DOM measurement for every bar. Cache viewport/grid rectangles and invalidate deliberately on resize/scroll/zoom.

## PERF-007 — Dependency scaling — P1

Do not render every dependency in a large schedule by default. Focused-link modes are a performance feature as well as a visual-design feature.

## PERF-008 — Data-window API — P2

For very large plans, support server/query filtering by resource set and time range while preserving enough offscreen relationship metadata to show continuation/focus correctly.

---

# 20. Fullscreen, export and preferences

## UTL-001 — Fullscreen workbench — P1

Provide a true schedule-focus/fullscreen mode hiding nonessential global chrome and maximizing grid/timeline space. Escape exits.

## UTL-002 — Export scopes — P2

Support explicit scopes rather than one ambiguous Export button:

- Current visible view;
- Current filtered resources + selected horizon;
- Full scenario schedule;
- Selected campaign/order chain;
- Capacity/load report;
- Excel schedule table.

PDF/PNG export should preserve visible layers/legend and include scenario/baseline timestamp metadata.

## UTL-003 — View preferences — P1

Persist locally:

- grid width;
- column widths/visibility;
- density;
- zoom level / last non-fit viewport;
- snap mode;
- collapsed resource groups;
- pinned resources;
- visible layers;
- panel sizes;
- capacity panel mode.

Do not persist transient selected operation or staged proposal across incompatible plan versions.

---

# 21. DHTMLX feature-by-feature disposition for APS

This table is the replacement for the simplistic Have/Build/Adapt/Skip list.

| DHTMLX capability | How DHTMLX behaves | APS disposition | APS interpretation |
| --- | --- | --- | --- |
| Configurable grid | Dedicated grid beside timeline | **Adopt strongly** | Resource hierarchy + planner columns |
| Grid resize | Divider resizes grid/timeline | **P0** | True synchronized splitter |
| Column resize | Header dividers | **P0** | Persistent resource-grid widths |
| Tree | Expand/collapse hierarchy | **P0** | Area/process/resource tree, not WBS |
| Sorting | Header sort with direction/custom comparator | **P1** | View-only resource sort |
| Inline grid edit | Cell editors | **Reject for master data** | Admin workspace owns resource masters |
| Placeholder/new task row | Create project task | **Reject** | Manufacturing operations are solver-derived |
| Zoom named levels | Hour/day/week/month/year etc. | **P0** | Manufacturing levels incl. shift/detail |
| Zoom-to-fit | Chooses most detailed level fitting viewport | **P0** | Fit all/visible/selection/campaign/order |
| Reset zoom | Restores pre-fit scale | **P0** | Must preserve planner navigation state |
| Custom scale | Multiple scale rows/templates | **P0** | Shift/date/hour-aware dual tiers |
| Timeline drag-scroll | Drag empty timeline to pan | **P0/P1** | Safe pan gesture |
| Keyboard nav | One control focus + internal navigation | **P1** | Planner-safe mappings |
| Task drag move | Rich DnD, pre/during/post events | **P0** | Proposal ghost + APS validation |
| Task resize | Start/end drag handles | **Domain-gated P2** | Only overrideable duration/events |
| Progress drag | Progress knob | **Reject** | Actual production owns progress |
| Multi-selection | Select many tasks | **P1** | Operation/campaign selection set |
| Multi-task drag | Move selected tasks horizontally | **P1** | Atomic semantic bulk move |
| Drag project | Move project + children | **Adapt** | Campaign/cast semantic move/repair |
| Autoscroll | Drag near edge scrolls | **P0** | Horizontal + vertical with acceleration |
| Link types | FS/SS/FF/SF + lag | **P1** | Rich readonly constraint links |
| Drag link creation | Create dependencies from handles | **Reject by default** | Route dependencies are not ad hoc |
| Critical path | Slack-based critical tasks/links | **Adapt strongly** | Solver-derived binding chain + reason |
| Baselines | Same row/separate/individual rows | **P1** | Immutable scenario comparison modes |
| Deadlines | Extra timeline elements | **P1** | Focused due exposure/markers |
| Markers | Point/range lines/bands | **P1** | Now/reference/fences/due/events manager |
| Working calendars | Task/resource/project calendars | **P1** | Actual resource calendars/downtime |
| Hide non-working time | Compress scale | **Defer / off by default** | Real elapsed time is important in APS |
| Resource panel | Resource grid + timeline | **P1** | Synchronized load panel |
| Resource histogram | allocated vs capacity by time cell | **P1** | APS capacity/occupancy/changeover buckets |
| Resource grouping | Group tasks by resource | **Already natural APS model** | Resources are default lanes |
| Resource assignment | Assign resources to tasks | **Adapt** | Solver assignment + eligible alternatives |
| Unassigned grouping | Not-assigned bucket | **Adapt** | Unscheduled/needs-repair queue, not fake resource |
| Unscheduled task | Task without dates | **Do not equate** | Demand queue is different semantic object |
| Project summary | Parent roll-up task | **Reject generic use** | Campaign span is domain-specific |
| Milestone | Zero-duration marker | **Adapt P2** | Cast completion, campaign ready, PO ready if real domain milestones |
| Split task | Fragments on same row | **Defer/domain-gated** | Only genuinely interruptible operations/events |
| Tooltips | Hover task details | **P0** | Dense APS operation tooltip |
| Lightbox | Modal task editor | **Reject as primary** | Modeless inspector is superior |
| Readonly per task | Editability control | **P0** | Running/completed/frozen/firm policy |
| Undo/redo | Client action stack | **Adapt strongly** | Persisted Plan Version semantic history |
| Dynamic loading | Lazy data retrieval | **P2** | Time/resource window API for huge schedules |
| Smart rendering | Render only useful visible elements | **P0** | Row/time virtualization |
| Fullscreen | Fullscreen chart | **P1** | Planner focus mode |
| Export PDF/PNG/Excel | Visual/data export | **P2** | APS-scoped export |
| RTL | Mirror layout | **P3** | Only if localization requires |
| Touch | Touch device support | **P3** | Pointer-compatible design, desktop first |
| Multiple Gantts | More than one chart | **Mostly reject** | One central schedule; compare overlays/docks instead |
| WBS | Project numbering | **Reject** | APS business hierarchy differs |
| Backward planning | Project scheduling mode | **Adapt at solver level** | Due-date/objective policy, not client cascade |
| Auto scheduling | Client/project dependency cascade | **Reject as authority** | APS solver performs repair/replan |
| Constraint control | Project constraints | **Adapt** | APS route/resource/material/fence/thermal constraints |
| Locales/skins | Generic component packaging | **Not a Gantt core priority** | APS design system/localization handles it |

---

# 22. Claude design audit: what to keep, what to discard

The previous Claude work is strongest in **macro workbench anatomy** and weakest in **low-level Gantt mechanics and feature-completeness claims**.

## Keep

1. Gantt as the visual centre of gravity.
2. Modeless demand queue and inspector.
3. Bottom analysis/impact dock instead of navigating away.
4. Immutable released plan + recovery scenario.
5. Staged proposal → validate → impact → apply.
6. Baseline overlay concept.
7. Process-specific visual identity.
8. Frozen planning fence.
9. Resource alternatives in inspector.
10. Focused dependency chain rather than always-on spaghetti.
11. Execution/recovery modes over the same schedule.
12. One selection shared across demand/campaign/material/exception lenses.

## Replace or heavily rework

1. Fixed 176 px resource column.
2. Monolithic Gantt markup inside `FiniteSchedule.razor`.
3. Single-tier time ruler.
4. Fixed 15-minute snapping.
5. Moving the actual source bar during drag.
6. Cursor-as-start drag bug.
7. Highlighting every lane as if eligible before domain validation.
8. Horizontal-only autoscroll.
9. Straight dashed dependency lines.
10. “Tight chain” presented as if it were critical path.
11. Dense 10 px operation-card content.
12. Exact due-date line for every due timestamp at every zoom.
13. Baseline only as changed dashed blocks without a complete comparison mode.
14. Duplicate queue/inspector/history state ownership.
15. Claims that a feature is “Have” when only a rough primitive exists.
16. Treating demand queue as equivalent to generic unscheduled tasks.
17. Treating row reorder as a scheduling action; it must only affect view order.
18. Treating generic task duration resizing as safe manufacturing editing.
19. Generic “project summary” constructs where APS should use campaign/heat/cast semantics.
20. Any control that looks functional but has no real domain command behind it.

---

# 23. Required component and state architecture

The Gantt should become an owned reusable control within APS rather than page-specific absolute-position markup.

## ARC-001 — Component decomposition — P0

Recommended boundaries:

- `WorkbenchGantt.razor` — orchestration and shared context;
- `GanttResourceGrid.razor` — hierarchy/grid columns;
- `GanttTimeScale.razor` — dual-tier axis;
- `GanttTimelineViewport.razor` — scroll/viewport container;
- `GanttResourceLane.razor` — virtual row shell;
- `GanttOperationLayer.razor` — bars;
- `GanttBaselineLayer.razor`;
- `GanttDependencyLayer.razor`;
- `GanttMarkerLayer.razor`;
- `GanttCalendarLayer.razor`;
- `GanttCampaignLayer.razor`;
- `GanttProposalLayer.razor`;
- `GanttCapacityPanel.razor`;
- `GanttTooltip.razor` / context menu;
- dedicated JS module for pointer/scroll/measurement interactions.

Names may vary; responsibility boundaries should not.

## ARC-002 — One viewport state — P0

`GanttViewportState` should own:

- visible start/end;
- zoom level;
- timeline pixel width;
- vertical scroll;
- grid width;
- fit restore state;
- density;
- snap mode;
- collapsed groups.

## ARC-003 — One selection state — P0

Selection owns single/multi selection, focused chain, hover/focus, and related business entity reference.

## ARC-004 — One proposal state — P0

Proposal state owns drag candidate, local findings, validation state, server impact, and apply/discard. It is distinct from authoritative schedule.

## ARC-005 — JS owns pointer mechanics, not planning truth — P0

JavaScript should handle:

- pointer capture;
- drag threshold;
- coordinate measurement;
- scrolling/autoscroll;
- wheel/pan gestures;
- resizer mechanics;
- hover positioning;
- requestAnimationFrame updates.

C# / application services continue to own:

- operation/resource eligibility;
- schedule data;
- validation;
- material/routing/campaign constraints;
- solver repair;
- persistence/release.

## ARC-006 — Avoid per-pointermove Blazor round trips — P0

Drag ghost must update client-side at frame rate. Only meaningful state transitions (drag start, target eligibility refresh if needed, drop/stage, validation result) cross the JS/.NET boundary.

---

# 24. Read-model and command-contract extensions

Current contracts are a good aggregate starting point but need richer Gantt-specific facts.

## DTO-001 — Dependency links

Add an explicit dependency/constraint link collection rather than only `PredecessorPlanningKeys`.

## DTO-002 — Slack/binding explanation

Operation detail needs solver-derived fields such as:

- free slack minutes;
- total/headroom minutes;
- binding flag;
- binding constraint category;
- explanation code/text;
- downstream due exposure.

## DTO-003 — Occupancy segments

Expose setup/changeover/process segments when known, not only total operation interval.

## DTO-004 — Resource calendar intervals

Resource lanes need visible-window availability/downtime intervals with reason and source.

## DTO-005 — Resource hierarchy

Read model needs plant/area/process/resource parent relationships and stable display order.

## DTO-006 — Baseline full placement

Comparison data must support full baseline placements for overlay mode, not only change records if unchanged context is required.

## DTO-007 — Capacity buckets

Provide aggregated capacity/utilisation buckets by requested visible range and resolution, with basis metadata.

## CMD-001 — Batch move/semantic group commands — P1

Evolve from only single `PlanningMoveProposal` toward atomic multi-operation/campaign moves where product requirements demand them.

## CMD-002 — Pin/unpin — P1/P2

If planner pinning is supported, make it a real planning commitment command with validation and audit trail, not a CSS lock icon.

## CMD-003 — Repair scope — P1

Explicit commands should support repair selection/order/campaign/resource scope while preserving the canonical planning engine as authority.

---

# 25. Acceptance tests: release-blocking behavior

The Gantt overhaul is not accepted by screenshot review alone.

## 25.1 Drag tests

1. Grab operation at 70% of its width, drag +2 hours, drop: candidate start changes exactly +2 hours after snapping; it does not jump by the 70% grab offset.
2. Original bar remains at source while ghost moves.
3. Escape during drag restores state with zero proposal/validation calls.
4. Pointer leaves viewport near right edge: horizontal autoscroll continues and target timestamp remains correct.
5. Pointer approaches lower edge: vertical autoscroll exposes later resource lanes.
6. Ineligible resource shows no-drop before drop when eligibility is known.
7. Completed/running operation cannot start drag.
8. Frozen operation displays required override policy before apply.
9. Zoom during staged proposal either safely preserves proposal geometry or is intentionally blocked with explanation.

## 25.2 Zoom/scroll tests

1. Ctrl-wheel over 14:00 keeps 14:00 under the pointer within tolerance after zoom.
2. Fit selection uses actual timeline width after grid resize.
3. Reset Zoom restores previous exact viewport.
4. Opening inspector does not silently alter time range.
5. Resizing grid keeps all timeline layers aligned.
6. Repeated zoom in/out does not accumulate date drift.

## 25.3 Dependency tests

1. Connector ports remain attached after row resize, zoom and grid resize.
2. Focused chain correctly clips offscreen edges.
3. Link tooltip shows type/lag/headroom/reason.
4. Binding chain result is based on returned planning data, not one-minute adjacency.

## 25.4 Baseline tests

1. Resource-changed operation displays baseline on original lane.
2. Compare subrow expands without causing timeline/header misalignment.
3. Added/removed operation distinguishable without color.
4. Baseline remains readonly under pointer/keyboard interactions.

## 25.5 Virtualization tests

1. 10,000-operation scenario does not mount 10,000 operation elements at once.
2. Selection remains stable when selected row leaves and re-enters viewport.
3. Scroll does not show blank row gaps during rapid movement.
4. Tooltip/inspector works after virtualized row remount.

## 25.6 Accessibility tests

1. Gantt is one sensible Tab stop, not hundreds.
2. Operation can be found, selected and inspected without mouse.
3. Focus indicator visible in every theme.
4. Screen-reader label includes identity/time/resource/state.
5. Context menu is keyboard reachable.

---

# 26. Performance budgets

Initial targets for desktop release candidate; profile and adjust only with evidence.

| Interaction | Target |
| --- | ---: |
| Scroll/pan/drag visual update | 60 fps target, no obvious jank |
| Local candidate geometry/validation | < 50 ms |
| Single move server validation | < 500 ms typical |
| Search/filter response | < 100 ms typical |
| Zoom scale switch | < 100 ms perceived |
| First interactive Gantt, normal scenario | < 1.5 s after data available |
| Mounted operation bars | normally < 500 |

A spinner is not a substitute for fixing avoidable client rendering work.

---

# 27. Implementation sequence

## Phase 0 — Foundation reset (P0)

Do these before adding feature volume:

1. extract Gantt from monolithic page into owned components;
2. create one viewport/time-coordinate engine;
3. build synchronized resource grid/timeline splitter;
4. implement row/time virtualization;
5. implement dual-tier scales;
6. implement pointer-anchored zoom/pan/Fit/reset;
7. redesign adaptive operation bars + tooltip;
8. rebuild drag lifecycle with fixed source + ghost + grab offset + eligibility + 2D autoscroll + cancellation;
9. consolidate workbench state/history ownership;
10. preserve canonical proposal/validation/replan model.

**Exit condition:** the basic Gantt feels mechanically solid before capacity, campaigns, and comparison complexity are layered on top.

## Phase 1 — Planner-grade schedule control (P1)

1. resource hierarchy/columns/sorting/filter safety;
2. context menu and inspector restructure;
3. rich dependency DTO + routed focused links;
4. binding-chain/slack explanation;
5. baseline compare modes;
6. synchronized capacity/load panel;
7. resource calendars/downtime/fences;
8. multi-selection + atomic bulk moves;
9. campaign/heat/demand focus layers;
10. material exposure and supply/shortage context;
11. keyboard navigation/accessibility;
12. fullscreen workbench.

## Phase 2 — Advanced planning ergonomics (P2)

1. pin/unpin domain actions;
2. keyboard move mode;
3. drag demand as semantic schedule request;
4. deep comparison subrows and expanded deltas;
5. large-plan dynamic data-window API;
6. domain-gated duration/event resizing;
7. export scopes;
8. user pin/reorder preferences.

## Phase 3 — Optional/deferred

1. touch-first interactions;
2. RTL if required;
3. compressed non-working-time view;
4. generic milestone layer beyond approved APS milestones;
5. interruptible/split operation editing;
6. multiple independent Gantt instances.

---

# 28. Design QA rubric

Every Gantt review must answer these questions before approval.

## Geometry

- Does every bar's width truthfully map to time?
- Do grid/timeline/header/layers remain aligned after resize/zoom/scroll?
- Is the time scale readable without guessing date context?

## Direct manipulation

- Does the object stay under the grabbed pointer position?
- Is source position visible while proposing a change?
- Are invalid targets obvious before drop?
- Can every transient action be cancelled?

## Planning semantics

- Is the user seeing a proposal or committed plan?
- Does the UI expose why a move is blocked/warned?
- Are material and future supply represented rather than silently filtering work out?
- Are resource alternatives real eligibility, not guessed by lane type?
- Are critical/binding indications calculated rather than visually inferred?

## Information density

- Is text legible at actual rendered width?
- Are details moved to tooltip/inspector instead of compressed into bars?
- Are due dates, dependency lines and markers density-controlled?

## State

- Does selection survive zoom/panel actions?
- Is there one source of truth for viewport/selection/proposal/history?
- Does undo explain the business action it will reverse?

## Performance

- Are offscreen rows/bars avoided?
- Does drag remain local and frame-rate independent of server latency?
- Does opening comparison/capacity avoid multiplying the entire DOM?

## Accessibility

- Can the same planning action be performed without precise mouse targeting?
- Is status understandable without color?
- Is focus visible and restored predictably?

---

# 29. Definition of done for the Gantt overhaul

The Gantt overhaul is complete only when all statements below are true:

1. The Gantt is a reusable owned APS control, not page-specific absolute-position markup.
2. Resource grid and timeline are synchronized and resizable.
3. Dual-tier, multi-level zoom works with pointer anchoring, fit scopes, and reset.
4. Operation geometry remains time-accurate at every zoom.
5. Drag preserves grab offset and uses fixed source + candidate ghost.
6. Known eligible/ineligible resources are communicated before drop.
7. Snap mode is configurable; autoscroll works both axes; Escape cancels safely.
8. Drop stages a proposal and never commits directly.
9. Baseline supports complete overlay and deep comparison behavior.
10. Dependency rendering uses rich routed links and selected/focused modes.
11. “Critical/binding” state is derived from planning data and explains the binding constraint.
12. Capacity/load is a synchronized time-based panel using APS capacity truth.
13. Calendars, downtime and planning fences are distinct visible layers.
14. Campaign, heat, demand, material and execution context reuse the same timeline/selection.
15. Multi-selection and supported bulk planning actions are atomic and domain-safe.
16. Undo/redo has one state owner and remains grounded in persisted Plan Versions.
17. Keyboard and assistive-technology access exists.
18. Large schedules are virtualized and remain interactive.
19. No generic project-management affordance is added simply because DHTMLX has it.
20. No feature is labelled complete merely because a similarly named visual primitive exists.

---

# 30. Primary references

## DHTMLX official documentation

- Overview: https://docs.dhtmlx.com/gantt/
- Community vs PRO comparison: https://docs.dhtmlx.com/gantt/guides/editions-comparison/
- v10 migration/license notes: https://docs.dhtmlx.com/gantt/migration/
- Layout: https://docs.dhtmlx.com/gantt/guides/layout-config/
- Zoom extension / zoom-to-fit: https://docs.dhtmlx.com/gantt/guides/zoom/
- Drag and drop: https://docs.dhtmlx.com/gantt/guides/dnd/
- Keyboard navigation: https://docs.dhtmlx.com/gantt/guides/keyboard-navigation/
- Resource management: https://docs.dhtmlx.com/gantt/guides/resource-management/
- Baselines / extra timeline elements: https://docs.dhtmlx.com/gantt/guides/inbuilt-baselines/
- Critical path: https://docs.dhtmlx.com/gantt/guides/critical-path/
- Link properties: https://docs.dhtmlx.com/gantt/guides/link-properties/
- Auto-scheduling / lag and lead: https://docs.dhtmlx.com/gantt/guides/auto-scheduling/

## APS implementation audited

Branch: `claude/project-status-review-o2dx1j`

- `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- `src/APS.UI/wwwroot/planning-workbench.js`
- `src/APS.UI/State/PlanningWorkbenchState.cs`
- `src/APS.UI/State/PlannerCockpitState.cs`
- `src/APS.Application/PlanningWorkbenchContracts.cs`
- `src/APS.Application/PlanningWorkbenchCommandContracts.cs`
- `src/APS.UI/wwwroot/tailwind-input.css`
- `docs/superpowers/specs/2026-08-21-planning-workbench-design.md`
- `docs/superpowers/plans/2026-08-21-planning-workbench.md`

---

# 31. Final product rule

When there is a conflict between “more Gantt features” and **planner confidence in what the schedule means**, confidence wins.

APS should feel as mechanically polished as DHTMLX, but more trustworthy for manufacturing because every bar, gap, resource alternative, material warning, capacity signal, campaign relation, time fence, baseline and proposed move is connected to the same finite-capacity planning truth.
