# APS Gantt Workbench Overhaul — Current Implementation Status

**Re-baselined:** 23-Aug-2026  
**Canonical branch:** `main`  
**Baseline:** `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`  
**Historical implementation branch:** `codex/gantt-workbench-overhaul` — fully contained by `main`

The Gantt overhaul is integrated. This document describes the **current consolidated implementation**, not the component/file layout that existed on the original overhaul branch.

---

## 1. Important clarification: Ponytail cleanup consolidated layers; it did not remove the Gantt behavior

After the overhaul, Ponytail cleanup removed several small Razor wrapper/layer components:

- `GanttBaselineLayer.razor`
- `GanttCalendarLayer.razor`
- `GanttCampaignLayer.razor`
- `GanttDependencyLayer.razor`
- `GanttExecutionLayer.razor`
- `GanttMarkerLayer.razor`
- `GanttProposalLayer.razor`

Those filenames disappearing is **not evidence that the corresponding behavior disappeared**.

The current rendering path intentionally owns more of the synchronized lane scene directly. In particular, `GanttResourceLane.razor` still renders or participates in:

| Behavior | Current ownership/evidence |
|---|---|
| timeline/grid ticks | synchronized scene + resource lane |
| wall-clock Now marker | resource lane |
| planning reference marker | resource lane |
| frozen horizon/fence | resource lane |
| unavailable calendar intervals | resource lane using workbench calendar facts |
| baseline overlays | resource lane / baseline models |
| campaign spans | resource lane / scene campaign spans |
| execution actual segments | resource lane in Execution/Recovery modes |
| operation blocks | `GanttOperationBlock` within lane |
| staged single move | resource lane proposal rendering |
| staged atomic bulk move | resource lane proposal rendering |
| dependency visualization/focus | current synchronized Gantt scene/timeline path |

The cleanup reduced component indirection and file count. It should be judged by behavior and tests, not by counting deleted components.

The same principle applies elsewhere in the UI: obsolete theme/layout/update wrappers were removed where current canonical services/components already owned the remaining behavior.

---

## 2. Integrated workbench capabilities

| Area | Current status | Notes |
|---|---|---|
| Authoritative UTC viewport | Implemented | One synchronized viewport owns visible UTC range, zoom, pan, fit/reset, clipping and mounted time/row geometry. |
| Resource hierarchy | Implemented | Plant/area/process/resource hierarchy, collapse and synchronized resource rows. |
| Resource grid | Implemented | Resource remains visible; configurable/sortable/resizable supporting columns stay synchronized with timeline rows. |
| Calendar truth | Implemented | Resource-specific unavailable intervals come from planning/read-model facts; absence of work is not treated as downtime. |
| Capacity truth | Implemented/hardened | Capacity view consumes persisted assumptions for historical plans and uses compounded solver-aligned derating semantics. |
| Operation blocks | Implemented | Width-adaptive operation content, selection, execution/commitment/baseline cues and accessible business facts. |
| Navigation | Implemented | Zoom/pan/fit, splitters, virtualization, keyboard navigation and synchronized selection. |
| Marker categories | Implemented | Wall-clock Now, Plan Version reference and frozen-fence markers are distinct concepts. |
| Proposal drag lifecycle | Implemented/hardened | Source remains fixed while proposal is staged; cancel/blur cleanup is regression-tested; released plans remain non-editable. |
| Cross-resource move | Implemented | Candidate target uses eligible resource semantics and authoritative move validation. |
| Multi-selection | Implemented | Ctrl/Cmd toggling, Shift range where unambiguous, summary/context and atomic proposal behavior. |
| Atomic bulk move | Implemented/hardened | Final proposed positions are validated together; moved members are not falsely blocked by one another's old slots. |
| Time-fence enforcement | Implemented/hardened | Preview/apply use authoritative request/proposal policy and consistent reference time; frozen work is protected. |
| Baseline comparison | Implemented | Unchanged/moved/resource-changed/added/removed baseline semantics and compare modes. |
| Campaign visualization | Implemented | Campaign spans align to the shared timeline geometry. |
| Dependencies | Implemented for returned facts | Focused dependencies use shared scene geometry; do not infer a fake critical path from pixel adjacency. |
| Execution overlay | Implemented for returned actuals | Planned versus actual segment is explicit in execution/recovery modes. |
| Resource load/capacity region | Implemented | Shares the same time axis and supports resource/time focus. |
| Inspector | Implemented | Operation/business/lineage/eligibility/material/baseline facts are shown from read models rather than UI reconstruction. |
| Analysis dock | Implemented foundation | Overview, Exceptions, Capacity, Delivery, Material, Compare, Execution and Traceability views exist; deeper views link to owning workspaces where appropriate. |
| Released-baseline safety | Implemented | Released execution baseline blocks edit/move actions and directs planning changes through new scenario/replan paths. |

---

## 3. Atomic move correctness now on `main`

The post-overhaul hardening corrected a key semantic requirement: a bulk move is validated as **one proposed final schedule**, not as N independent moves against stale baseline placements.

Regression coverage protects at least these cases:

1. A moves into B's old slot while B moves away -> no false collision;
2. two selected operations overlap in their proposed final positions -> blocker;
3. moved predecessor/successor that preserve ordering -> no false precedence blocker;
4. predecessor proposed after successor start -> precedence violation;
5. selected move collides with non-selected/frozen work -> blocker;
6. database query count does not grow linearly with the number of moved items on the batched validation path.

This closes the earlier “atomic bulk move proposed-state” gap in the old audit backlog.

---

## 4. Historical capacity correctness now on `main`

Historical workbench capacity is no longer allowed to drift with mutable current resource/calendar masters when the Plan Version persisted its assumptions.

Current behavior/tests protect:

- persisted resource scheduling mode;
- persisted capacity basis / nominal capacity / capacity factor;
- persisted operating state;
- persisted calendar intervals;
- compounded resource/calendar derating aligned with solver semantics;
- explicit compatibility fallback for older Plan Versions lacking the newer assumption snapshot.

This is important for plan comparison/audit: opening an old Plan Version must not silently reinterpret its capacity using today's master data.

---

## 5. Pointer cancellation and interaction cleanup

`pointercancel` and window blur are treated as cancellation, not commit.

Regression coverage checks rollback/cleanup of:

- capacity splitter state;
- resource-grid column splitter state;
- main splitter state;
- pan state;
- operation drag state;
- proposal ghost/feedback;
- cursor/highlight/snap guide/autoscroll state.

The cancellation path must not invoke .NET move/bulk-move commit callbacks.

---

## 6. Current component architecture

The current architecture favors fewer behavior-owning components rather than one Razor file per visual layer.

Key ownership includes:

- `GanttTimelineViewport.razor` — synchronized timeline/viewport composition;
- `GanttResourceLane.razor` — lane scene and several aligned overlays after Ponytail consolidation;
- `GanttOperationBlock.razor` — operation block semantics/interactions;
- Gantt scene/model/state classes — geometry, viewport, selection, hierarchy, baseline/capacity/dependency models;
- `planning-workbench.js` — browser-specific pointer/pan/drag/fullscreen/resize behavior;
- `WorkbenchAnalysisDock.razor` — analysis navigation/summary foundation.

The design objective is **clear ownership with shared geometry**, not maximum component count and not one monolithic file at any cost. Future refactors should split only where there is real behavioral ownership/testability benefit.

---

## 7. Current verification evidence

Latest recorded Windows evidence for `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`:

- Release build: **0 warnings, 0 errors**;
- tests: **336/336 passed**;
  - Architecture 9;
  - Infrastructure 12;
  - Planning 182;
  - UI 133;
- self-contained `win-x64` `APS.DesktopHost.exe` publish produced;
- SQLite `PRAGMA quick_check`: `ok`;
- live published desktop loaded the released execution baseline;
- **105 operations and 8 resources** rendered;
- Gantt, operation inspector, resource-load and capacity views exercised;
- released-baseline editing correctly blocked;
- final desktop process remained open and responsive.

The repository Windows verifier is [`../../build/verify.ps1`](../../build/verify.ps1); see [`../windows-ci.md`](../windows-ci.md).

The earlier overhaul-branch figures (for example 135 UI tests / 163 Planning tests and PID-specific QA notes) remain valid historical evidence for that branch at that time, but they are no longer the current suite/status authority.

---

## 8. Known current follow-ups

### Wall-clock Now progression

`GanttResourceLane` currently captures `DateTime.UtcNow` for the component instance. A planner left open for a long time can therefore display a stale wall-clock marker until the component is recreated/re-rendered by another state change. A dedicated clock/update mechanism is still desirable.

### Open execution segment progression

For an operation with `ActualStartUtc` and no `ActualEndUtc`, the current overlay ends at the **Plan Version reference time**. That is stable historical interpretation but not a continuously advancing live-running segment. Execution UI semantics should eventually distinguish “historical plan reference” from “live now” explicitly.

### Binding/critical-chain evidence

The UI must not fabricate critical/binding chains from visual adjacency. Genuine binding visualization depends on authoritative persisted/read-model evidence. Where that evidence is absent, the UI should say so.

### Disabled commands without backend authority

Pin/unpin, scoped repair, material trace or similar commands must remain disabled/linked to an owning workspace until an authoritative backend command/read contract exists. UI affordances must not mutate planning truth locally.

### Systematic browser/visual regression

The current baseline has received live Windows desktop verification, but #31 still owns a repeatable browser/visual regression harness for pointer geometry, long-open-session timing, fullscreen/localStorage, responsive workstation layouts and end-to-end flows.

---

## 9. Documents to use with this status

- [`../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md) — target/requirements authority;
- [`../reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md`](../reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md) — DHTMLX behavioral reference, not a dependency;
- [`APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md`](APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md) — **historical pre-overhaul reconnaissance**, not current implementation status;
- [`APS_CURRENT_STATE_2026-08-23.md`](APS_CURRENT_STATE_2026-08-23.md) — overall APS current-state authority.

Do not use the historical branch name or deleted standalone layer filenames to infer that integrated `main` lacks those behaviors.
