# APS Current State — 23 August 2026

**Authority:** current implementation/status snapshot for the canonical `main` branch.  
**Baseline commit:** `71e456d2fe124173cdd1f0bfeac82e18f53dc45f` (`merge: retain late APS release-readiness work`).  
**Supersedes as status authority:** older branch-specific implementation reports, the 18-Aug backend audit as a current-state report, and status text that still names #56 or a pre-consolidation branch as the active implementation baseline.

This document describes **what is actually integrated on `main`**. Design/specification documents remain useful for target behavior, but current-state claims must be checked against this document, current code, and the live issue tracker.

---

## 1. Repository authority

`main` is the single integrated product baseline.

The following development lines are fully contained by `main` and are historical work branches rather than current implementation authorities:

- `integrate/current-workstream` / merged PR #69;
- `codex/gantt-workbench-overhaul`;
- `codex/holistic-test-overhaul`;
- `chore/ponytail-cleanup-v2` and `chore/ponytail-cleanup-v3`;
- `chore/dotnet-skills-hardening`;
- earlier Claude/review branches already merged into the same lineage.

Do not start new work by rebasing old design assumptions from those branches. Audit current `main` first.

The canonical product projects are:

```text
APS.Domain
APS.Application
APS.Planning
APS.Infrastructure
APS.Service
APS.UI
APS.DesktopHost
```

The retired Python/workbook/Flask implementation is historical only and remains available through Git history/tag `v0.2.5` when deliberate comparison is required.

---

## 2. Canonical production path

The production lifecycle remains the .NET path:

```text
SAP SO/MTS demand
 -> demand reconciliation and qualified FG coverage
 -> MTO/MTS Production Orders
 -> recursive BOM/material requirements
 -> time-phased material coverage and reservations
 -> internally manufacturable requirements / explicit shortfall
 -> Campaign/grade-sequence/heat optimization
 -> configured ManufacturingRoute projection
 -> finite resource/material/thermal schedule
 -> immutable Plan Version
 -> readiness review and approval
 -> release from persisted Plan Version truth
 -> Work Orders / process operations
 -> execution actuals and material state
 -> bounded repair/replan through the same lifecycle
```

Production mode does not use the demo/sandbox planner as a hidden fallback.

### Current product boundary

Current production code enforces a **manufacturing-only** planning boundary. APS consumes authoritative inventory, known incoming supply, committed/released WIP and APS-planned internal production. Production planning rejects speculative BUY/TRANSFER/manual-supply planning controls. Uncovered material that cannot be manufactured internally remains explicit shortfall/`NotManufacturableHere` rather than fabricated supply.

This is an intentional code-and-test contract in the current baseline. If product scope changes later, the lifecycle validation, material-supply contracts, acceptance tests and documentation must change together.

---

## 3. Completed manufacturing foundations integrated on `main`

The following backend tranches are implemented/integrated and must not be treated as open from older documents:

- #38 — canonical production path / demo isolation;
- #45 — SO item -> qualified FG coverage -> MTO Production Order/service-date orchestration;
- #33 — recursive BOM/material-requirement graph;
- #14 — time-phased material ledger/reservation/future-supply foundation;
- #11 — billet/known-incoming material contingency foundation;
- #15 — candidate Campaign/grade-sequence/heat optimization;
- #34 — configured route-driven pre-CCM topology;
- #58 — configured downstream route projection without a first-`HotRoll` architectural pivot;
- #9 — liquid-steel thermal/resource-pair constraint foundation;
- #56 — time/temperature-aware billet thermal aging, hot-charge/reheat decision and actual-state replan behavior;
- #35 — resource scheduling modes/cumulative capacity foundation;
- #17 — resource operating-state/scenario overlay foundation.

### #56 completion is important

Older roadmaps still call #56 the current primary issue. That is stale. The implementation is already on `main` and includes:

- source-exit and rolling-entry thermal criteria;
- transfer/wait/holding-loss effects;
- delayed hot-path fallback through configured optional reheating where valid;
- conservative treatment of unknown/yard state;
- actual measured thermal state taking precedence on replan;
- persisted Plan Version evidence explaining the hot/reheat decision.

The current primary backend issue after this tranche is **#16 — late-bound resource assignment, commitment and operational redispatch**.

---

## 4. Current backend program

### Current primary

**#16 — late-bound resource assignment, commitment and operational redispatch.**

Already working foundations include alternative finite-schedule resource options, physical-resource parallelism, solver-owned CCM selection for the completed caster slice, persisted alternatives in several paths, time fences, and child-plan move/replan behavior. The remaining issue is the complete generic lifecycle:

```text
Eligible Resources
 -> Planned Resource
 -> Commitment State
 -> Committed Resource
 -> Actual Resource
 -> auditable redispatch/local repair
```

This must remain generic across configured process operations; do not add a CCM-only or rolling-only dispatch architecture.

### Ordered remaining backend work

After #16, the current program remains:

1. #18 — execution/material genealogy and actual-state closure;
2. #19 — planner-grade diagnostics and restoration guidance;
3. #57 — scenario material contingency plus richer Plan Version comparison;
4. #43 — CTP/scenario/capacity convergence on the canonical planning kernel;
5. #36 — complete typed backend read/command exposure;
6. #60 — validated operational authoring for planning-critical masters;
7. #61 — deterministic persisted integrated-steel reference dataset;
8. #44 — final end-to-end manufacturing-planning acceptance gate.

Cross-cutting gates continue through the owning tranche: #39 master wiring, #40 logging, #41 validation, #42 effective-rule consistency and #32 operational/material fidelity. #62 remains scope-gated taxonomy. #59 remains independent cross-platform Tailwind verification.

---

## 5. Plan Version lifecycle and release readiness

The persisted lifecycle now explicitly contains:

```text
Draft -> Feasible -> Approved -> Released
```

with `Superseded` and `Failed` states as applicable.

`Approved` is a real persisted `PlanVersionStatus`; release is no longer allowed directly from `Feasible`.

### Approval/readiness boundary

Current persisted readiness evaluates, at minimum:

- active Plan Version status;
- persisted material-requirement evidence;
- material shortfall/late/unsourced/cycle/non-manufacturable findings;
- persisted supply-action evidence;
- firm/on-time external incoming evidence where such authoritative supply exists;
- persisted MTO production allocations;
- presence of scheduled production operations serving those allocations;
- planned completion versus the persisted Production Order required date.

Release requires an **active Approved** Plan Version and re-evaluates readiness. Repository persistence also enforces the lifecycle boundary so callers cannot bypass it by replaying a release payload directly.

### Remaining service-date refinement

The current readiness gate is materially stronger than the earlier Feasible-only release path, but the long-term due-date model still distinguishes customer-required date, production-required-by date and allocation-grain service performance. Documents should not imply that the present `ProductionOrder.RequiredDate` comparison is the final service-date architecture.

---

## 6. Gantt / planning workbench current state

The Gantt overhaul is integrated on `main`; `codex/gantt-workbench-overhaul` is historical.

Implemented behavior includes:

- authoritative synchronized UTC viewport;
- hierarchical resource rows and row/time virtualization;
- configurable synchronized resource grid;
- resource calendars and capacity truth from the read model;
- dual-tier timeline and shared geometry;
- baseline comparison modes;
- campaign spans;
- dependencies and focused dependency geometry;
- wall-clock/reference/frozen-fence marker categories;
- execution actual overlays;
- staged single and atomic bulk-move proposals;
- pointer-anchored zoom, pan, fit/reset, splitter resizing and persisted preferences;
- operation inspector and analysis dock;
- keyboard navigation and accessibility contracts;
- multi-selection and atomic multi-operation move validation;
- synchronized capacity/resource-load region;
- released-baseline edit protection;
- Gantt pointer-cancel/blur cleanup;
- query-count and historical-capacity regressions protecting the workbench backend.

### Ponytail consolidation did not remove the Gantt features

The post-overhaul Ponytail cleanup deleted several tiny layer components such as the former standalone baseline/calendar/campaign/execution/proposal layer files. Their behavior was **consolidated into the canonical lane/viewport rendering path**, primarily `GanttResourceLane.razor` and associated Gantt models/state. This reduced component indirection; it did not intentionally remove those visual/behavioral layers.

The same principle applies to theme/update cleanup: obsolete wrapper/configuration objects were removed where the remaining canonical service/component already owned the behavior. Current behavior must be judged from the rendered/current path, not file-count changes.

### Known workbench follow-ups visible in current code

- the wall-clock Now marker is captured per component instance rather than driven by a periodic clock update, so a very long-open planner session can display a stale Now position;
- an open execution actual is currently drawn to the Plan Version reference time, not continuously to wall-clock time;
- genuine binding-chain visualization remains dependent on persisted/read-model binding evidence rather than a pixel-based heuristic;
- commands without authoritative backend contracts must remain disabled rather than simulated in the UI.

These are follow-ups; they do not invalidate the integrated overhaul.

---

## 7. UI/theme/update consolidation

The current UI is no longer accurately described as “future UI implementation deferred.” A substantial production Blazor shell and planner workbench are already implemented.

Recent cleanup deliberately reduced wrapper/component count:

- theme preference/accent/color helper types were removed where the simplified `ThemeService`/`theme.js` path owns the remaining behavior;
- layout wrappers such as old `NavGroup`, `NavItem`, `PlanContextBar` and `AppearancePopover` were removed or their responsibilities folded into the current shell;
- desktop update configuration/back-end wrappers were simplified while retaining the reactive menu/update lifecycle in `DesktopMenuBar`/desktop host;
- several small Gantt rendering-layer files were folded into the canonical lane/viewport rendering path.

This is **architectural simplification**, not evidence that the associated end-user feature was deleted. Any future cleanup must still be verified behaviorally.

---

## 8. Verification contract

APS verification is Windows-authoritative.

### Authoritative automated verification path

The repository-owned `build/verify.ps1` runs on the shared self-hosted Windows Azure DevOps agent:

- Azure DevOps project: EOS;
- pool: `Default`;
- agent: `EOS`;
- SDK pinned by `global.json` (`10.0.203`, latest patch roll-forward).

The verifier performs:

1. solution restore;
2. full Release build of `APS.slnx`;
3. every registered `tests/*` project discovered from `APS.slnx`;
4. self-contained `win-x64` `APS.DesktopHost` publish smoke.

The EOS **Windows Build Lab** can run the same repository-owned verification contract for an arbitrary APS branch/tag/SHA.

**GitHub Actions or hosted CI are not substitutes for this APS Windows verification contract.** A green claim requires inspection of the actual Windows/Azure output or equivalent explicitly recorded Windows verification evidence.

### Latest recorded verification for this baseline

For `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`, the recorded 23-Aug-2026 Windows verification evidence reports:

- Release build: **0 warnings, 0 errors**;
- tests: **336/336 passed**;
  - Architecture: 9;
  - Infrastructure: 12;
  - Planning: 182;
  - UI: 133;
- self-contained Windows `APS.DesktopHost.exe` publish produced;
- SQLite `PRAGMA quick_check`: `ok`;
- pre-launch database backup created;
- published desktop binary loaded the released execution baseline;
- 105 operations / 8 resources rendered;
- Gantt, operation inspector, resource-load and capacity views exercised;
- released-baseline editing correctly blocked;
- final desktop process remained open and responsive.

This is the latest recorded baseline evidence. Future commits must be reverified; this result must not be projected onto later SHAs automatically.

---

## 9. Documentation authority from this point

Use this order when implementation-state documents disagree:

1. current `main` code and persisted contracts;
2. latest completed Windows verification evidence for the exact SHA;
3. this current-state document;
4. `docs/APS_Backend_Work_Program.md` for ordered remaining work;
5. current targeted design/requirements documents;
6. dated audits, reconnaissance, implementation plans and completion reports as historical evidence only.

In particular:

- `APS_Backend_Acceptance_Audit_2026-08-18.md` is a valuable dated audit input, **not** current implementation-state truth;
- `APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md` is a pre-overhaul reconnaissance baseline, **not** the current Gantt description;
- `docs/superpowers/plans/*` and `docs/superpowers/specs/*` are implementation/design history unless explicitly promoted by a current authority document;
- branch names inside dated reports describe where the work was performed at that time and do not override the integrated `main` baseline.

When a future change closes/reopens a major issue or materially changes architecture, update this document and the relevant current program/status document in the same tranche so documentation does not drift again.
