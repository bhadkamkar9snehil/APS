# APS Ponytail / Cyclomatic-Complexity Audit — 2026-09-04

Branch audited: `codex/ui-workbench-chrome-legibility`

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
| PC-04 | P2 | Route projection | `MultiStageRouteProjector.Apply` has very high decision density: route lookup, route-section continuity, optional/forbidden operations, thermal/reheat decisions, resource eligibility, dependency construction, and cursor mutation occur inside nested rolling/operation loops. | **Accepted for this pass; targeted refactor deferred to a green Windows gate.** This is domain algorithm complexity, not framework slop. A future change should extract one cohesive stage at a time, not introduce a generic pipeline/factory. |
| PC-05 | P2 | Campaign planning | `CampaignPlanningService.FormCampaigns` combines supply netting, sourcing, campaign candidate selection, allocation drawdown, and heat construction. It is a major McCabe-risk hotspot. | **Deferred with guardrail.** Refactor only around existing domain boundaries (for example supply allocation vs campaign composition) with before/after tests. Do not create a strategy/factory layer solely to lower method complexity. |
| PC-06 | P2 | Finite scheduling | `FiniteScheduleOptimizer.Solve` is branch-heavy and long because it constructs one mutable CP-SAT model: active resources, cumulative/disjunctive capacity, calendars, dependencies, stability, service objective, and solve diagnostics. | **Accepted intentional complexity.** Splitting stateful model construction into arbitrary micro-services would obscure solver invariants. Extract only complete model-building concerns when tests prove equivalence. |
| PC-07 | P3 | Planning orchestration | `PlanningEngine.RunCore` is long and contains repeated fail-fast checks after physical projection stages. | **Accepted.** The method is predominantly a linear, visible production pipeline. Replacing it with a handler/pipeline framework would increase indirection without removing domain complexity. |
| PC-08 | P3 | Workbench commands | `PlanningWorkbenchCommandService.ValidateMove` has many independent branches, but each branch corresponds to a planner-visible constraint finding (execution protection, eligibility, availability, horizon, time fence, calendar, resource overlap, predecessors, successors). | **Accepted.** Keeping the constraint sequence visible is clearer than hiding each `if` behind many one-use helpers. |
| PC-09 | P3 | Diagnostics | `PlanningConfigurationDiagnosticsService` is a large file, but `GetAsync` already delegates to cohesive diagnostic groups (`AddGlobalDiagnostics`, route, resource, transition, thermal, scenario). | **No finding requiring refactor.** This is the decomposition pattern preferred for other large rule-oriented services. |
| PC-10 | P3 | Gantt / UI | `FiniteSchedule.razor` remains a very large UI file. Size alone is not cyclomatic complexity, and the Gantt implementation already uses extracted state/models/components. | **Deferred.** Further component splitting requires rendered browser verification so interaction state, drag/drop, context menus and focus behavior are not fragmented for metric cosmetics. |
| PC-11 | P3 | Demand orchestration | `ProductionDemandOrchestrationService` is a large lifecycle/reconciliation service. Closed-order status semantics are also repeated in the Order Service UI. | **Deferred.** Consolidate lifecycle status semantics when this service is next changed under a runnable test gate; do not edit a large reconciliation path just to remove a tiny helper duplicate. |
| PC-12 | P4 | Planning helpers | Several planning files contain tiny local wildcard/equality helpers such as `Matches(configured, actual)`. | **Explicitly accepted.** Centralizing one-line context-local string predicates into a new utility abstraction would satisfy DRY mechanically while adding navigation/indirection; that is not a useful Ponytail refactor. |

## Remediation completed in this pass

### 1. One execution state machine

`OperationExecutionService` now exposes an internal canonical apply core that can defer `SaveChanges`. `HeatExecutionService` uses that core instead of maintaining its own generic transition and commitment logic. Casting-specific rows (`HeatExecutionActual`, strand material actuals, material lots) remain specialized, but generic operation truth has one owner.

This removes the highest-risk kind of duplicated complexity: two implementations deciding whether the same physical operation is Planned/Ready/Running/Held/Completed and what resource/commitment state that implies.

Focused tests were extended so heat execution is expected to update generic operation history/state and so an actual caster outside the planned eligible set is recorded as an off-plan fact rather than silently accepted as planned.

### 2. One order-service validation rule set

`OrderServicePolicyRules.ValidationError` now owns earliest/latest/Hard/Flexible boundary validation. Both persistence and UI call it. The UI no longer has a parallel `ValidateEdit` implementation.

The test suite now contains a compact theory covering valid Standard/Hard/Flexible policies and the invalid boundary cases.

### 3. User-visible description aligned with actual planning semantics

The Order Service screen now makes the distinction explicit:

- requested/confirmed delivery remains the preferred optimization target;
- acceptable earliest/latest dates are separate service evidence;
- release can tolerate finishing after the preferred target only while still inside the agreed boundary.

This prevents a future maintainer from "fixing" scheduling toward the wrong target because the UI/documentation described a different model than the code.

## Why the remaining large methods were not mechanically split

Cyclomatic complexity is useful as a hotspot detector, not a refactoring objective by itself. The main remaining hotspots are physical-planning algorithms. Their branches encode real invariants such as route continuity, charge mode, thermal viability, eligibility, time fences, cumulative capacity and dependency feasibility.

A refactor is useful only when it removes a duplicated decision authority or exposes a stable domain boundary. Moving every branch to a one-use helper, adding a factory for one implementation, or introducing a generic pipeline would lower a per-method metric while increasing total system complexity. Those changes are intentionally rejected by this audit.

## Windows verification gate

These changes are source-level only in this environment. Before merging/releasing, run the repository's normal Windows verification gate and at minimum the planning test project containing:

- `HeatExecutionTests`
- `OrderServiceWindowTests`
- existing operation execution / commitment tests
- existing planning lifecycle / release-readiness tests

Then run the application and verify Order Service, heat actual entry, operation execution state, Work Orders, and release readiness against a real local database.

No test/build/runtime pass is claimed by this document.

## Acceptance state

- Open P0 findings: **0**
- Open P1 findings: **0** after source remediation
- P2/P3 hotspots remain only where a no-runtime refactor would risk hiding or altering physical planning semantics; each is explicitly accepted or deferred above.
- No new framework, package, factory hierarchy, repository layer, or analyzer dependency was added to satisfy the audit.
