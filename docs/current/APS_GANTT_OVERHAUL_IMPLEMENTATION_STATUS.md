# APS Gantt Workbench Overhaul — Implementation Status

Date: 2026-08-22  
Branch: `codex/gantt-workbench-overhaul`

## Delivered

| Area | Status | Evidence |
|---|---|---|
| Authoritative viewport | Complete | One UTC tick-precise viewport owns zoom, pan, fit/reset, clipping, snap, row density, grid width and mounted row range. |
| Resource hierarchy | Complete | Plant, area, process-stage and resource metadata now produce real synchronized group rows. Groups collapse without row drift and persist stable hierarchy keys as local UI preferences; absent hierarchy levels are not fabricated. Resource focus uses one workbench-state owner and treats the full grid row and timeline lane consistently. |
| Resource grid contract | Complete | Resource is permanently visible; state, busy time, load, operation count, next start and exception count are configurable columns with clamped drag-resizable widths. Visibility, widths and sort direction persist locally. Sorting is confined to each authoritative process-stage group and never changes schedule or solver order. Filtered views report shown versus total resources and warn when they hide a critical resource/operation exception. |
| Calendar and capacity truth | Complete | Resource-specific availability intervals and capacity buckets come from the planning read model; missing operations are never treated as downtime. |
| Reusable synchronized Gantt | Complete | Resource grid, dual-tier time scale and timeline are componentized and share one geometry. |
| Operation semantics | Complete for returned facts | Blocks adapt content to width and expose execution, single-source/alternate-resource count, commitment and baseline-change cues through text/glyphs as well as color; accessible names carry the same canonical facts. |
| Tooltip and high contrast | Complete for returned facts | Hover/focus text carries business ID, process, resource, exact planned geometry, quantity, grade/section, campaign/heat, linked orders/due dates, commitment/execution, eligibility, solver binding/slack and returned warning summaries. Forced-colors rules use distinct outline/border patterns for selection, frozen, running, completed, held and baseline-change states. |
| Navigation and performance | Complete | Pointer-anchored zoom, empty-space pan, persistent splitter, `ResizeObserver`, animation-frame coalescing and row/time virtualization are implemented. A hierarchical 10,000-operation workload mounted 336 operation models and built the warmed scene in 3.2 ms. |
| Fit and marker management | Complete | Fit-all, visible-resource, selection, campaign, demand-chain and explicit UTC range actions use the authoritative viewport. Due dates, wall-clock Now, plan reference and frozen fence are distinct switchable categories; no current time is inferred from the plan reference. |
| Proposal drag lifecycle | Complete with final rendered interaction recheck noted below | Source remains fixed, a ghost carries the proposal, grab offset is preserved, eligible lanes are explicit, Escape/pointer cancel/blur clean up, and running/completed work is rejected in both UI geometry and the authoritative command service. Shift snap uses target-resource calendar boundaries and explicitly rejects a target/window with no boundary. |
| Baseline comparison | Complete | Unchanged, time moved, resource changed, added and removed states are classified. All-baseline, changed-only and expanded compare-subrow modes share synchronized row geometry. Original baseline resources remain visible when assignments change. |
| Scheduling layers | Complete | Baseline, calendar, campaign, dependency, marker/fence, execution and proposal layers are explicit components on the shared coordinate system. Focused dependencies retain geometry across row virtualization and expose type, category, minimum/current lag and headroom. |
| Binding chain semantics | Safe extension point | `PlanningBindingEvidenceView` carries solver/read-model causes and slack. The UI states `Binding chain unavailable` when evidence is absent; no pixel-adjacency critical-path heuristic remains. |
| Synchronized resource load | Complete | Collapsible capacity region uses the same time axis, aggregates at hour/shift/day scale, exposes processing/downtime/overload, focuses the selected resource/time range and marks contributing operations with a non-color `L` cue. Its persisted height is directly drag-resizable. |
| Keyboard and assistive access | Complete for P1 mappings | The resource grid and operation field each expose one roving Tab entry rather than every row/bar. Arrow, Home/End and Page keys navigate internally; Space toggles selection; Alt+arrows pan/scroll; Ctrl/Cmd undo/redo uses persisted history; Shift+F10 opens the real menu. An in-product shortcut panel documents the exact mappings and the synchronized schedule table shares selection. |
| Multi-selection and atomic move | Complete | Ctrl/Cmd-click toggles operations; Shift-click selects an unambiguous visible sequence within one resource lane; the compact summary reports occupied time, resources, campaign/order context and eligibility. Horizontal multi-drag renders all mounted proposal ghosts, preserves every relative offset/resource assignment, validates all items together, rejects duplicates/internal disjunctive overlap, and sends one override collection through one persisted child Plan-Version replan. |
| Compact shell | Complete | The control toolbar remains one horizontally scrollable row, schedule list/capacity/queue/inspector are overlays or collapsible regions, and Fullscreen API state is synchronized back to .NET. |
| Planner inspector | Complete for returned facts | Plan, actuals, lineage, baseline delta, scheduling mode/eligibility/commitment/routing, binding evidence, material pools and PO reservations are shown only from the workbench read model. |
| Execution geometry | Complete for returned facts | Execution/Recovery modes render returned actual start/end as an explicit `A` segment on the shared timeline; an open running segment ends at planning reference time and no actual is inferred when the read model returns none. |

## Verification completed

- `node --check src/APS.UI/wwwroot/planning-workbench.js`: passed.
- `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --no-restore`: 135 passed.
- `dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --no-restore`: 163 passed.
- `dotnet build APS.slnx --no-restore`: succeeded with 0 warnings and 0 errors.
- The hierarchical 10,000-operation performance gate measured 3.2 ms warmed scene construction, 336 mounted operation models and 14/126 mounted display rows.
- Latest service-host SSR against the existing database returned the 105-operation field, exactly one operation Tab stop, 16 mounted hierarchy/grid rows, configurable-column and fit-range controls, shown/total resource truth, dense canonical tooltip text, and truthful `Binding chain unavailable` and `Shift unavailable` states.
- `/`, `/api/health`, `_content/APS.UI/planning-workbench.js` and `_content/APS.UI/tailwind.css` returned HTTP 200.
- `%LOCALAPPDATA%\APS-Data\Data\aps.db` returned SQLite `pragma quick_check` = `ok` after the real-data render.
- The exact final Debug desktop build launched as PID 4004 with a responding native `APS Planner` window.

## Explicit boundaries and follow-up inputs

- The in-app browser security policy blocked the final local-page reload and forbade alternate browser workarounds. Final light/dark, practical-size and pointer-drag visual acceptance therefore remains unclaimed.
- The earlier user-owned DesktopHost process had exited before the final launch. The current DesktopHost build is running and responsive, but visual acceptance remains unclaimed because the permitted browser inspection surface is blocked.
- The active plan has calendar facts but no shift boundary inside the visible eight resource lanes/window. Shift snap is disabled as `Shift unavailable`; target-resource drops also fail clearly if a boundary is absent. No boundary is fabricated and no free-placement fallback is used.
- Genuine binding-chain visualization remains disabled unless solver/read-model `PlanningBindingEvidenceView` records are supplied.
- Capacity exposes only categories supported by the current read model: processing, unavailable/downtime and overload. Setup/changeover/idle are not invented.
- Pin/unpin, scoped repair and operation-to-material trace commands remain visible but disabled in the context menu with the missing authoritative contract stated explicitly.
