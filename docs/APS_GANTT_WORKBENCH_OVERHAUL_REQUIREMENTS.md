# APS Gantt Workbench Requirements

**Status:** canonical current product/interaction contract  
**Re-baselined:** 23-Aug-2026 against integrated `main`  
**Current implementation status:** [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md)  
**Detailed 22-Aug requirement baseline:** [`reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md`](reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md)  
**Behavioral benchmark:** [`reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md`](reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md)

APS owns this capability. DHTMLX Gantt is a behavioral and interaction-quality reference, not an APS dependency, data model, or project-management semantics source.

## 1. How to read this contract

The 22-Aug full specification remains the detailed source for the requirement families and IDs below. This current document **incorporates those detailed requirements by reference**, except for statements in the old document that described the then-current implementation/branch/file layout.

The following parts of the 22-Aug full document are therefore **historical starting-point evidence, not current implementation status**:

- references to `claude/project-status-review-o2dx1j` as the active branch;
- the old `Have / Build / Adapt / Skip` assessment;
- the starting defects such as fixed 176 px grid width, fixed 15-minute snap, source-bar dragging, invalid-lane highlighting, horizontal-only autoscroll, single-tier scale, straight dependency lines and the old “tight chain” approximation;
- claims that most Gantt rendering lived in one monolithic `FiniteSchedule.razor`;
- the original proposed one-component-per-layer file decomposition;
- the original test counts and branch-specific implementation observations.

Those observations were valuable because they generated the overhaul requirements. They must **not** be used to reopen fixed defects or infer missing functionality in current `main`. Current implementation truth is maintained only in the current implementation-status document and code.

## 2. Product thesis

The APS Gantt is the central planning instrument: a **finite-capacity resource-time control surface**, not a project WBS chart.

Resources and process groups form the default vertical hierarchy. Operations occupy physical resource time. Campaigns, heats, demand, material, baselines, actuals, constraints, downtime, capacity, planning fences and proposed changes are synchronized layers over one time-coordinate system.

A capability is complete only when three things agree:

1. **Visual primitive** — the planner can clearly see the object/state.
2. **Interaction behavior** — pointer, keyboard, focus, scrolling, zooming, selection, drag, cancellation and feedback are predictable.
3. **APS semantics** — the action obeys the same material, route, thermal, resource, campaign, time-fence, execution and Plan Version rules as the canonical planning engine.

No UI gesture becomes a second scheduler.

## 3. Non-negotiable Gantt invariants

1. Operation geometry is time-accurate at every zoom.
2. Resource lanes represent physical `ResourceId` instances; same-type equipment is not automatically interchangeable.
3. All timeline layers use one coordinate model.
4. Grid/timeline headers and rows remain synchronized through scroll, zoom, resize and virtualization.
5. Current time, Plan Version reference time, frozen horizon and resource downtime are distinct concepts.
6. Direct manipulation creates a **proposal**, never an immediate authoritative schedule mutation.
7. Validation/apply uses the canonical planning command/replan path and produces persisted Plan Version truth.
8. Completed/running/frozen/committed work follows backend policy before any edit affordance is enabled.
9. Eligible target resources come from planning/resource-option evidence, not resource-type guessing.
10. Material absent today does not make future work disappear; planned supply and explicit shortfall remain visible.
11. Baseline and actuals never overwrite planned historical truth.
12. Dependency/binding information comes from authoritative planning evidence, not pixel adjacency.
13. Capacity views identify their basis; rough-cut and finite occupancy are not interchangeable KPIs.
14. Selection, viewport, proposal and history have explicit owners; duplicate UI truth is prohibited.
15. Accessibility and keyboard operation are part of the control, not a later cosmetic pass.
16. Large schedules must use viewport/time-window discipline rather than mounting the full plan DOM.

## 4. Requirement families retained from the detailed specification

The detailed requirement definitions in the full 22-Aug document remain the target contract under these families:

| Family | Scope | Current interpretation |
|---|---|---|
| `KEEP-*` | Correct architectural ideas from the original workbench | Preserve behavior, not necessarily the old component structure. |
| `LYT-*` | Workbench anatomy, synchronized grid/timeline, splitters, density | Current implementation should be judged by resulting geometry and usability. |
| `TIM-*` | UTC viewport, zoom levels, multi-tier scale, pan, fit/reset, markers | One coordinate engine and stable viewport semantics remain mandatory. |
| `GRD-*` | Resource hierarchy, columns, sort/filter, resizing | View-only resource organization must not change solver truth. |
| `BAR-*` | Operation geometry/content/status/tooltip | Pixel-width-adaptive content and time-truthful geometry remain mandatory. |
| `SEL-*` | Single/multi selection and semantic bulk operations | Bulk operations are atomic planning proposals. |
| `DND-*` | Drag lifecycle | Source remains fixed, proposal ghost, grab-offset preservation, eligibility, snapping, 2D autoscroll, deterministic cancellation, stage-before-apply. |
| `DEP-*` | Dependencies, queue/thermal windows, binding chain | Rich planning evidence; no fake CPM/tight-chain inference. |
| `CAP-*` | Resource load/capacity | Synchronized capacity basis with click-through to causing operations. |
| `CAL-*` | Calendars, downtime, fences | Actual configured resource calendars; no generic weekend assumptions. |
| `CMP-*` | Baseline/scenario comparison | Immutable baseline, resource-change deltas, added/removed/moved semantics. |
| `APS-*` | Campaign/heat/material/resource flexibility | Steel/manufacturing semantics that exceed generic Gantt controls. |
| `CTX-*` | Queue, inspector, contextual commands | Modeless context and no fake actions. |
| `HIS-*` | Undo/redo/history | Semantic history grounded in persisted Plan Versions. |
| `EXE-*` | Planned-vs-actual and recovery | Actual geometry and protected execution state remain explicit. |
| `A11Y-*` | Keyboard/accessibility | Gantt is operable without hundreds of tab stops or color-only status. |
| `PERF-*` | Virtualization/rendering budgets | Pointer interaction remains client-local; offscreen work is not fully mounted. |
| `UTL-*` | Fullscreen/export/preferences | View utilities do not mutate planning truth. |
| `ARC-*` | Ownership/architecture | Preserve clear ownership/shared geometry; **do not require one Razor file per visual layer**. |
| `DTO-*` | Read-model evidence | Add typed facts only where the current backend/read model does not yet expose them. |
| `CMD-*` | Planning commands | Commands remain semantic, validated and Plan-Version-aware. |

## 5. Important architecture correction after Ponytail cleanup

The original requirements proposed separate files such as `GanttBaselineLayer`, `GanttCalendarLayer`, `GanttCampaignLayer`, `GanttDependencyLayer`, `GanttExecutionLayer`, `GanttMarkerLayer` and `GanttProposalLayer`.

That proposal was a **responsibility-decomposition suggestion**, not a product requirement that every visual layer must own a separate Razor component forever.

The integrated implementation and later Ponytail pass consolidated several of those wrappers into the canonical lane/viewport scene. This is acceptable because:

- the represented behaviors remain;
- they share the same time/lane geometry;
- duplicated component indirection/state was reduced;
- tests and workbench behavior remain the acceptance boundary.

Future refactors should split a layer only when it gains meaningful independent behavior, lifecycle, performance isolation or testability. Do not recreate wrapper components merely to match an old file diagram.

## 6. Direct-manipulation contract

For a movable operation, pointer-down/drag behavior must preserve the detailed `DND-*` semantics:

```text
pointer-down
 -> capture source placement + grab offset + eligible resources + snap policy
 -> drag threshold crossed
 -> source remains fixed
 -> candidate ghost follows pointer
 -> target start derives from pointer time minus original grab offset
 -> candidate snaps under current policy
 -> eligible/ineligible lanes communicate before drop
 -> horizontal/vertical autoscroll keeps candidate calculation live
 -> Escape/pointercancel/blur destroys transient state without commit
 -> drop stages PlanningMoveProposal
 -> canonical validation returns blockers/warnings/impact
 -> explicit Apply creates persisted child/replan truth
```

For multi-selection, the proposed **final schedule** is evaluated atomically. Selected operations moving away from old slots must not falsely block each other, while real proposed overlap, precedence, frozen-work or non-selected-resource conflicts remain blockers.

## 7. Resource flexibility contract

The Gantt must expose operational flexibility without pretending that every same-type machine is eligible.

For each schedulable operation the planner should be able to understand:

```text
operation requirement
 -> eligible physical resources
 -> planned assignment
 -> commitment state
 -> committed assignment
 -> actual resource
```

This is especially important for cases such as an LRF-ready heat that may still be technically eligible for another CCM before commitment. The Gantt can stage the redispatch; #16 owns the complete generic backend commitment/redispatch lifecycle.

## 8. Material contract

The schedule is forward-looking.

A downstream operation may be planned against:

- current qualified material;
- authoritative known incoming material;
- released/running internal WIP;
- APS-planned internal production.

If material cannot be available by the required time, show an explicit material exposure/shortfall/late-supply condition. Do not hide the demand or remove the operation simply because inventory is zero at the planning reference time.

A month-long campaign may consume progressively produced material.

## 9. Baseline, execution and history

### Baseline

Baseline is immutable historical planning truth. A resource-changed operation must be able to show the baseline placement on the original resource while current placement appears on the new resource.

### Execution

Actual start/end/resource/quantity/material are separate execution facts. Completed and running operations obey execution protection rules. Actuals do not rewrite historical plan geometry.

### History

A staged proposal is not history. Applied planning commands create/activate valid persisted Plan Version lineage. Undo/redo semantics must never delete historical Plan Versions.

## 10. Binding-chain and diagnostic contract

Do not label visually adjacent operations “critical.”

A genuine binding/critical indication requires planning evidence such as:

- slack/headroom;
- predecessor/resource-sequence constraint;
- campaign/cast-sequence constraint;
- material receipt;
- queue/thermal window;
- frozen/commitment boundary;
- due/service pressure;
- alternative resource/material restoration evidence where available.

Until the authoritative evidence exists, the UI should state that binding evidence is unavailable rather than fabricate it.

## 11. Performance and scale

The detailed `PERF-*` requirements remain in force. In particular:

- resource rows and operation bars are virtualized/clipped to visible windows with overscan;
- large hidden/collapsed regions do not mount expensive children;
- dependencies are focused by default rather than rendering the whole network;
- drag/pan/scroll geometry updates do not make .NET/server round-trips on every pointer move;
- selection/focus survives virtualized remounts through stable planning keys;
- capacity panels use aggregate buckets rather than duplicating every operation into every cell.

Performance acceptance should use realistic reference-plan density, not only tiny fixtures.

## 12. Accessibility

The detailed `A11Y-*` contract remains current:

- one sensible Gantt entry focus, not hundreds of tab stops;
- internal keyboard navigation;
- visible focus in supported themes;
- operation accessible name includes business identity, resource, time and state;
- status never depends on color alone;
- context menu/inspector reachable from keyboard;
- a synchronized textual/table representation remains available for dense review and assistive technology.

## 13. Current completion interpretation

The overhaul is **integrated**, so the old Phase 0/Phase 1 implementation sequence is historical. Do not run that sequence again mechanically.

Current work should instead use this decision rule:

1. read [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md);
2. compare the relevant current behavior with the incorporated detailed requirement ID;
3. if current behavior satisfies it, keep it closed;
4. if the requirement depends on missing backend evidence/command authority, attach it to the owning backend issue rather than implementing UI-side truth;
5. if an interaction defect is found, fix the smallest canonical owner and add a regression test;
6. verify behavior on the authoritative Windows environment before calling a changed executable SHA green.

Known current follow-ups include long-open-session wall-clock progression, live running-segment semantics, genuine binding evidence, commands still lacking backend authority, and systematic browser/visual regression coverage. The current status document is the authoritative list.

## 14. Definition of done for future Gantt changes

A Gantt change is done only when, as applicable:

- geometry remains aligned/time-accurate after resize/zoom/scroll;
- pointer and keyboard interactions are cancellable and deterministic;
- source/proposal/committed states are visually distinct;
- target eligibility and blockers are explained before apply where evidence exists;
- material/future-supply semantics remain forward-looking;
- historical Plan Version/baseline/actual truth is not mutated;
- behavior is usable at realistic schedule density;
- accessibility semantics remain intact;
- focused component/model tests protect the defect/requirement;
- the exact changed executable SHA is verified through the Windows gate before claiming it green.

## 15. References

- [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) — current implementation truth.
- [`reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md`](reference/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md) — complete detailed 22-Aug requirement definitions and historical starting audit incorporated by this contract.
- [`reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md`](reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md) — DHTMLX behavioral benchmark.
- [`APS_UI_UX_Product_Blueprint.md`](APS_UI_UX_Product_Blueprint.md) — production UX principles.
- [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) — current backend issue order.
- [`windows-ci.md`](windows-ci.md) — authoritative Windows verification contract.
