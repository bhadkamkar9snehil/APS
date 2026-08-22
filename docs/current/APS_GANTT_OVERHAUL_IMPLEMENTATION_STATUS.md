# APS Gantt Workbench Overhaul — Implementation Status

Date: 2026-08-22  
Branch: `codex/gantt-workbench-overhaul`

## Delivered

| Area | Status | Evidence |
|---|---|---|
| Authoritative viewport | Complete | One UTC tick-precise viewport owns zoom, pan, fit/reset, clipping, snap, row density, grid width and mounted row range. |
| Resource hierarchy | Complete | Plant, area, process-stage and resource metadata now produce real synchronized group rows. Groups collapse without row drift and persist stable hierarchy keys as local UI preferences; absent hierarchy levels are not fabricated. Resource focus uses one workbench-state owner and treats the full grid row and timeline lane consistently. |
| Calendar and capacity truth | Complete | Resource-specific availability intervals and capacity buckets come from the planning read model; missing operations are never treated as downtime. |
| Reusable synchronized Gantt | Complete | Resource grid, dual-tier time scale and timeline are componentized and share one geometry. |
| Operation semantics | Complete for returned facts | Blocks adapt content to width and expose execution, single-source/alternate-resource count, commitment and baseline-change cues through text/glyphs as well as color; accessible names carry the same canonical facts. |
| Navigation and performance | Complete | Pointer-anchored zoom, empty-space pan, persistent splitter, `ResizeObserver`, animation-frame coalescing and row/time virtualization are implemented. A hierarchical 10,000-operation workload mounted 336 operation models and built the warmed scene in 3.2 ms. |
| Proposal drag lifecycle | Complete with final rendered interaction recheck noted below | Source remains fixed, a ghost carries the proposal, grab offset is preserved, eligible lanes are explicit, Escape/pointer cancel/blur clean up, and running/completed work is rejected in both UI geometry and the authoritative command service. Shift snap uses target-resource calendar boundaries and explicitly rejects a target/window with no boundary. |
| Baseline comparison | Complete | Unchanged, time moved, resource changed, added and removed states are classified. All-baseline, changed-only and expanded compare-subrow modes share synchronized row geometry. Original baseline resources remain visible when assignments change. |
| Scheduling layers | Complete | Baseline, calendar, campaign, dependency, marker/fence, execution and proposal layers are explicit components on the shared coordinate system. Focused dependencies retain geometry across row virtualization and expose type, category, minimum/current lag and headroom. |
| Binding chain semantics | Safe extension point | `PlanningBindingEvidenceView` carries solver/read-model causes and slack. The UI states `Binding chain unavailable` when evidence is absent; no pixel-adjacency critical-path heuristic remains. |
| Synchronized resource load | Complete | Collapsible capacity region uses the same time axis, aggregates at hour/shift/day scale, exposes processing/downtime/overload, focuses the selected resource/time range and marks contributing operations with a non-color `L` cue. |
| Keyboard and assistive access | Complete for the delivered surface | Exactly one mounted operation is a roving Tab stop; arrow keys navigate by lane/time; Shift+F10/context-menu key opens the real operation menu; a synchronized schedule table supports dense textual review; semantic labels, reduced motion and focus-visible rules remain active. |
| Compact shell | Complete | The control toolbar remains one horizontally scrollable row, schedule list/capacity/queue/inspector are overlays or collapsible regions, and Fullscreen API state is synchronized back to .NET. |
| Planner inspector | Complete for returned facts | Plan, actuals, lineage, baseline delta, scheduling mode/eligibility/commitment/routing, binding evidence, material pools and PO reservations are shown only from the workbench read model. |

## Verification completed

- `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --no-restore`: 126 passed.
- `dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --no-restore`: 160 passed.
- `dotnet build APS.slnx --no-restore`: succeeded with 0 warnings and 0 errors.
- The hierarchical 10,000-operation performance gate measured 3.2 ms warmed scene construction, 336 mounted operation models and 14/126 mounted display rows.
- Latest service-host SSR against the existing database returned 105 operation buttons, exactly one operation Tab stop, eight authoritative hierarchy group toggles for eight resources, Compare Subrow/Schedule List/Fullscreen controls, truthful `Binding chain unavailable`, and `Shift unavailable` because the visible eight lanes return no calendar boundary.
- `/`, `/api/health`, `_content/APS.UI/planning-workbench.js` and `_content/APS.UI/tailwind.css` returned HTTP 200.

## Explicit boundaries and follow-up inputs

- The in-app browser security policy blocked the final local-page reload and forbade alternate browser workarounds. Final light/dark, practical-size and pointer-drag visual acceptance therefore remains unclaimed.
- A user-owned `APS.DesktopHost` process from `build/publish/workbench-0.4.0-cockpit` (PID 19300 during verification) was already running. The current DesktopHost build succeeded with 0 warnings, but it was not duplicated or used to replace the user's running process; current-build desktop launch proof remains pending.
- The active plan has calendar facts but no shift boundary inside the visible eight resource lanes/window. Shift snap is disabled as `Shift unavailable`; target-resource drops also fail clearly if a boundary is absent. No boundary is fabricated and no free-placement fallback is used.
- Genuine binding-chain visualization remains disabled unless solver/read-model `PlanningBindingEvidenceView` records are supplied.
- Capacity exposes only categories supported by the current read model: processing, unavailable/downtime and overload. Setup/changeover/idle are not invented.
- The canonical command service exposes one validated move at a time, not an atomic bulk-move contract. Multi-selection/bulk apply is not presented as working.
- Pin/unpin, scoped repair and operation-to-material trace commands remain visible but disabled in the context menu with the missing authoritative contract stated explicitly.
