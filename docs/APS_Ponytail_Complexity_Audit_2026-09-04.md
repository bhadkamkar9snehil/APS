# APS Ponytail / Cyclomatic-Complexity Audit — 2026-09-04

Branch audited: `codex/ui-workbench-chrome-legibility`

Tracking issue: [#70 — Ponytail / cyclomatic-complexity audit follow-up](https://github.com/bhadkamkar9snehil/APS/issues/70)

## Scope and method

This is a source-level complexity audit of the canonical .NET APS implementation. The retired Python backend is out of scope. Generated EF migration designer files and `ApsDbContextModelSnapshot.cs` are also excluded: generated size is not an actionable cyclomatic-complexity signal.

The current environment does not have the Windows/.NET verification runtime or a configured cyclomatic-complexity analyzer, so this audit does **not** invent McCabe numbers. It uses decision density, nesting, method/file size, duplicated decision authorities, parallel state machines, and unnecessary abstraction as risk indicators. Exact metrics can be added at the Windows gate later without introducing a new production dependency.

The Ponytail rule used here is: remove accidental complexity first; do not split cohesive algorithms merely to make a metric smaller. Prefer an existing implementation over a parallel implementation, shared rules over duplicated rules, platform/runtime facilities over new dependencies, and small direct code over speculative factories/interfaces/frameworks. Validation, planning invariants, execution evidence, release safety, and domain-specific solver logic are preserved even when they are branch-heavy.

## Findings

| ID | Severity | Area | Finding | Action / status |
|---|---|---|---|---|
| PC-01 | P1 | Execution | `HeatExecutionService` duplicated the generic operation execution state machine: transition rules, generic operation status/resource mutation, and commitment behavior had a second authority beside `OperationExecutionService`. | **Fixed.** Heat execution now stages casting/material evidence but delegates generic execution state to the canonical operation execution core in the same `DbContext` save. |
| PC-02 | P2 | Order service | Service-window validation existed independently in persistence and Blazor UI. The two copies could drift. | **Fixed.** `OrderServicePolicyRules.ValidationError` is now the single rule set used by both `OrderServicePolicyService` and `OrderService.razor`. |
| PC-03 | P2 | Order service UX | UI copy still described the accepted late boundary as something campaign/scheduling could use as a replacement production target, while the implementation deliberately keeps requested/confirmed delivery as the optimization target and uses tolerance as separate service/release evidence. | **Fixed.** UI now states the actual behavior and the save message refers to service-readiness effect rather than promising schedule movement. |
| PC-04 | P2 | Route projection | `MultiStageRouteProjector.Apply` had very high decision density: route lookup, route-section continuity, optional/forbidden operations, thermal/reheat decisions, resource eligibility, dependency construction, and cursor mutation occurred inside nested rolling/operation loops. | **Fixed at source level in `417363528536c119d287345176961ae9146552b9`.** The projector now has explicit domain stages for rolling preflight, operation inclusion/skip/stop decisions, hot-roll entry validation, route-task creation, thermal evidence, feed-cursor advancement and route-end validation. Shared projection mechanics remain inside the same partial projector instead of being replaced by a generic framework. Windows regression tests are still required. |
| PC-05 | P2 | Campaign planning | `CampaignPlanningService.FormCampaigns` combines supply netting, sourcing, campaign candidate selection, allocation drawdown, and heat construction. It is now the largest actionable McCabe-risk hotspot. | **Open / next target.** Refactor only around existing domain boundaries (for example supply allocation/netting vs campaign composition/heat construction) with before/after tests. Do not create a strategy/factory layer solely to lower method complexity. |
| PC-06 | P2 | Finite scheduling | `FiniteScheduleOptimizer.Solve` is branch-heavy and long because it constructs one mutable CP-SAT model: active resources, cumulative/disjunctive capacity, calendars, dependencies, stability, service objective, and solve diagnostics. | **Accepted intentional complexity for now.** Splitting stateful model construction into arbitrary micro-services would obscure solver invariants. Extract only complete model-building concerns when tests prove equivalence. |
| PC-07 | P3 | Planning orchestration | `PlanningEngine.RunCore` is long and contains repeated fail-fast checks after physical projection stages. | **Accepted.** The method is predominantly a linear, visible production pipeline. Replacing it with a handler/pipeline framework would increase indirection without removing domain complexity. |
| PC-08 | P3 | Workbench commands | `PlanningWorkbenchCommandService.ValidateMove` has many independent branches, but each branch corresponds to a planner-visible constraint finding (execution protection, eligibility, availability, horizon, time fence, calendar, resource overlap, predecessors, successors). | **Accepted.** Keeping the constraint sequence visible is clearer than hiding each `if` behind many one-use helpers. |
| PC-09 | P3 | Diagnostics | `PlanningConfigurationDiagnosticsService` is a large file, but `GetAsync` already delegates to cohesive diagnostic groups (`AddGlobalDiagnostics`, route, resource, transition, thermal, scenario). | **No finding requiring refactor.** This is the decomposition pattern preferred for other large rule-oriented services. |
| PC-10 | P3 | Gantt / UI | `FiniteSchedule.razor` remains a very large UI file. Size alone is not cyclomatic complexity, and the Gantt implementation already uses extracted state/models/components. | **Deferred.** Further component splitting requires rendered browser verification so interaction state, drag/drop, context menus and focus behavior are not fragmented for metric cosmetics. |
| PC-11 | P3 | Demand orchestration | `ProductionDemandOrchestrationService` is a large lifecycle/reconciliation service. Closed-order status semantics are also repeated in the Order Service UI. | **Deferred.** Consolidate lifecycle status semantics when this service is next changed under a runnable test gate; do not edit a large reconciliation path just to remove a tiny helper duplicate. |
| PC-12 | P4 | Planning helpers | Several planning files contain tiny local wildcard/equality helpers such as `Matches(configured, actual)`. | **Explicitly accepted.** Centralizing one-line context-local string predicates into a new utility abstraction would satisfy DRY mechanically while adding navigation/indirection; that is not a useful Ponytail refactor. |

## Remediation completed

### 1. One execution state machine

`OperationExecutionService` exposes an internal canonical apply core that can defer `SaveChanges`. `HeatExecutionService` uses that core instead of maintaining its own generic transition and commitment logic. Casting-specific rows (`HeatExecutionActual`, strand material actuals, material lots) remain specialized, but generic operation truth has one owner.

This removes the highest-risk kind of duplicated complexity: two implementations deciding whether the same physical operation is Planned/Ready/Running/Held/Completed and what resource/commitment state that implies.

Focused tests were extended so heat execution is expected to update generic operation history/state and so an actual caster outside the planned eligible set is recorded as an off-plan fact rather than silently accepted as planned.

### 2. One order-service validation rule set

`OrderServicePolicyRules.ValidationError` owns earliest/latest/Hard/Flexible boundary validation. Both persistence and UI call it. The UI no longer has a parallel validation implementation.

The test suite contains a compact theory covering valid Standard/Hard/Flexible policies and the invalid boundary cases.

### 3. User-visible description aligned with actual planning semantics

The Order Service screen now makes the distinction explicit:

- requested/confirmed delivery remains the preferred optimization target;
- acceptable earliest/latest dates are separate service evidence;
- release can tolerate finishing after the preferred target only while still inside the agreed boundary.

This prevents a future maintainer from "fixing" scheduling toward the wrong target because the UI/documentation described a different model than the code.

### 4. Downstream route projection decomposed by domain stage

Commit `417363528536c119d287345176961ae9146552b9` addresses the largest remaining route-projection complexity hotspot.

The old `MultiStageRouteProjector.Apply` mixed almost the entire downstream routing lifecycle inside one nested rolling/operation loop. It now delegates to cohesive stages:

- build reusable projection context and per-heat remaining supply;
- prepare one rolling projection and validate route/order/section/feed prerequisites;
- evaluate whether each configured operation must stop, skip or be included;
- validate hot-roll entry independently from general route/resource eligibility;
- construct finite-schedule tasks/dependencies;
- record hot-roll thermal evidence;
- advance mutable feed state after an included operation;
- validate the final projected route endpoint.

The refactor deliberately did **not** introduce a generic pipeline, strategy hierarchy, factory, repository abstraction or new package. Route/capability/thermal primitives remain private members of the same partial projector. The change is intended to expose stable APS domain boundaries rather than move every conditional into a one-use helper.

Behavioral intent is unchanged. Existing downstream route and hot-charge regression suites are the required verification authority; no runtime pass is claimed here.

## Why the remaining large methods are not being mechanically split

Cyclomatic complexity is useful as a hotspot detector, not a refactoring objective by itself. The remaining hotspots are physical-planning algorithms. Their branches encode real invariants such as supply allocation, route continuity, charge mode, thermal viability, eligibility, time fences, cumulative capacity and dependency feasibility.

A refactor is useful only when it removes a duplicated decision authority or exposes a stable domain boundary. Moving every branch to a one-use helper, adding a factory for one implementation, or introducing a generic pipeline would lower a per-method metric while increasing total system complexity. Those changes are intentionally rejected by this audit.

`CampaignPlanningService.FormCampaigns` is now the next actionable hotspot because it combines several separable APS concerns. `FiniteScheduleOptimizer.Solve` remains high-complexity but is intentionally treated more conservatively because its branches collectively build one CP-SAT model.

## Windows verification gate

These changes are source-level only in this environment. Before merging/releasing, run the repository's normal Windows verification gate and at minimum the planning test project containing:

- `DownstreamRouteProjectionTests`
- `DownstreamRouteHotChargeTests`
- `HeatExecutionTests`
- `OrderServiceWindowTests`
- existing operation execution / commitment tests
- existing planning lifecycle / release-readiness tests

Then run the application and verify downstream route projection, direct-hot/reheat behavior, Order Service, heat actual entry, operation execution state, Work Orders, and release readiness against a real local database.

No test/build/runtime pass is claimed by this document.

## Acceptance state

- Open P0 findings: **0**
- Open P1 findings: **0** after source remediation
- PC-04, the largest route-projection decision-density issue, is **fixed at source level** and awaits the Windows regression gate.
- PC-05 (`CampaignPlanningService.FormCampaigns`) is the **next largest actionable complexity issue**.
- PC-06 remains intentionally high-complexity solver construction and is not being cosmetically split.
- P3/P4 items are either deferred until the correct runtime/browser gate or explicitly accepted where abstraction would make the code worse.
- No new framework, package, factory hierarchy, repository layer, or analyzer dependency was added to satisfy the audit.
