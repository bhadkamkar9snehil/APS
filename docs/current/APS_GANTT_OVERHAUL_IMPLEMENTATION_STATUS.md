# APS Gantt Workbench Overhaul — Implementation Status

Date: 2026-08-22  
Branch: `codex/gantt-workbench-overhaul`

## Delivered

| Area | Status | Evidence |
|---|---|---|
| Authoritative viewport | Complete | One UTC tick-precise viewport owns zoom, pan, fit/reset, clipping, snap, row density, grid width and mounted row range. |
| Resource hierarchy | Complete | Plant, area, process stage and resource metadata are projected into the schedule lanes. |
| Calendar and capacity truth | Complete | Resource-specific availability intervals and capacity buckets come from the planning read model; missing operations are never treated as downtime. |
| Reusable synchronized Gantt | Complete | Resource grid, dual-tier time scale and timeline are componentized and share one geometry. |
| Navigation and performance | Complete | Pointer-anchored zoom, empty-space pan, persistent splitter, `ResizeObserver`, animation-frame coalescing and row virtualization are implemented. |
| Proposal drag lifecycle | Complete with runtime recheck noted below | Source remains fixed, a ghost carries the proposal, grab offset is preserved, eligible lanes are explicit, Escape/pointer cancel/blur clean up, and running/completed work is rejected in both UI geometry and the authoritative command service. |
| Baseline comparison | Complete | Unchanged, time moved, resource changed, added and removed states are classified. All-baseline and changed-only modes are persisted. Original baseline resources remain visible when assignments change. |
| Scheduling layers | Complete | Baseline, calendar, campaign, dependency, marker/fence, execution and proposal layers are explicit components on the shared coordinate system. |
| Binding chain semantics | Safe extension point | `PlanningBindingEvidenceView` carries solver/read-model causes and slack. The UI states `Binding chain unavailable` when evidence is absent; no pixel-adjacency critical-path heuristic remains. |
| Synchronized resource load | Complete | Collapsible capacity region uses the same time axis, aggregates at hour/shift/day scale and exposes processing, downtime and overload with resource/time focus. |
| Accessibility | Complete for implemented controls | Operations and capacity buckets are native buttons with semantic labels; resource rows and splitter are keyboard-focusable; significant state has text/tooltips in addition to color; reduced motion and focus-visible rules remain active. |

## Verification completed

- `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --no-restore`: 109 passed.
- `dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --no-restore`: 160 passed.
- `dotnet build APS.slnx --no-restore`: succeeded with 0 warnings and 0 errors.
- Live workbench smoke proof before the final layer/capacity rebuild loaded the existing persisted plan with 105 operations across 8 resources and exercised fit, density persistence, selection and inspector behavior.

## Explicit boundaries and follow-up inputs

- The in-app browser security policy blocked the final local-page reload after the layer/capacity rebuild. The final build and deterministic suites are verified, but a final visual acceptance pass of those last two slices remains a human/browser check; it is not claimed here.
- No authoritative shift-template boundary collection is present in the current workbench read model. Shift snap therefore does not fabricate boundaries and behaves as free placement until canonical boundaries are supplied.
- Genuine binding-chain visualization remains disabled unless solver/read-model `PlanningBindingEvidenceView` records are supplied.
- Capacity exposes only categories supported by the current read model: processing, unavailable/downtime and overload. Setup/changeover/idle are not invented.
