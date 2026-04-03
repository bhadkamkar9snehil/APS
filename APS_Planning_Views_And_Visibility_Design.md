# APS Planning Views And Visibility Design

## 1. Purpose

This document defines a better planning-view architecture for the current APS so planners, PPC, operations, and management can understand:

- what demand is covered
- what will actually run
- what is held and why
- what materials are constraining release
- what each resource is expected to do
- where the plan is weak, degraded, or risky
- what changes between alternative plans

The focus here is **visibility**, not engine redesign.

This is intentionally grounded in what the current APS already produces:

- release/campaign plan
- detailed process schedule
- equipment-wise dispatch view
- material allocation and shortages
- BOM-based net requirement view
- rough-cut capacity map
- plant-completion-based promise view
- scenario reruns

The aim is to turn these outputs into a more coherent **planning workbench**.

---

## 2. Current Visibility Gaps

The current APS already has valuable data, but it is spread across output sheets that are still too technical or too isolated.

Today’s biggest visibility gaps are:

- no single control view that shows the health of the current plan
- release status, material status, and equipment execution are split across separate sheets
- overbuild, balancing quantity, and exact demand coverage are not shown explicitly enough
- degraded planning conditions are visible, but not yet organized into a first-class exception view
- scenarios are currently comparison-oriented, not true side-by-side plan workspaces
- capacity is visible, but rough-cut vs finite schedule basis is still easy to misunderstand
- CTP is visible, but not yet connected to a broader promise-risk view

The answer is not to create more raw tables.

The answer is to design a small set of views with clear roles.

---

## 3. Visibility Model

The APS should expose visibility at five levels:

### 3.1 Executive Visibility

Answers:

- are we on track
- how much demand is covered
- how much is at risk
- which constraint is hurting us most
- what changed since the last accepted plan

### 3.2 Planner Visibility

Answers:

- which releases are approved, held, or late
- which material issue is blocking release
- which line or machine is the bottleneck
- which orders are split, overbuilt, or pushed
- whether the plan used degraded assumptions

### 3.3 Dispatcher Visibility

Answers:

- what each machine should run next
- which jobs are frozen
- where queue or continuity risk exists
- where idle gaps or starvation will occur

### 3.4 Material Visibility

Answers:

- what stock was used
- what shortages exist
- what intermediates or byproducts are generated
- which release consumed which materials
- whether material lineage is authoritative

### 3.5 Scenario Visibility

Answers:

- what changed in the alternative plan
- which releases moved or became late
- which resource sequence changed
- which scenario is operationally safer

---

## 4. Proposed View Layout

The APS should evolve toward a view set with the following structure.

## 4.1 Control Tower

This becomes the top operational summary.

Purpose:

- one-screen health check of the current plan

Audience:

- management
- PPC
- planning leads

Must show:

- plan anchor
- planning horizon
- baseline or scenario name
- released quantity
- held quantity
- on-time percentage
- total lateness
- total campaigns or release units
- total batch units/heats
- top constrained plant/line/resource
- degraded planning flags
- major blockers

Recommended sections:

- plan status ribbon
- service ribbon
- risk ribbon
- bottleneck ribbon
- exception ribbon

This should replace the habit of opening multiple sheets just to know whether the plan is trustworthy.

## 4.2 Release Cockpit

This should be the main planner-facing release summary.

Purpose:

- one row per release unit, with all major decisions visible

Audience:

- PPC
- planners
- operations coordination

Recommended columns:

- release ID
- demand groups or sales orders covered
- item family
- grade/specification
- quantity
- batch/heats count
- release status
- hold reason
- material issue
- planning warnings
- due date
- planned completion
- lateness or margin
- line or stream assignment
- demand coverage status
- overbuild quantity
- balancing quantity

This should build on today’s `Campaign_Schedule`, but become more explicit about demand coverage, overbuild, and risk.

## 4.3 Demand Coverage View

This is a new high-value view.

Purpose:

- show exactly how each demand line is satisfied

Audience:

- planners
- customer service
- order management

Must reconcile:

- open demand quantity
- finished stock coverage
- released make quantity
- held quantity
- overbuild or balancing quantity
- remaining uncovered quantity

Recommended columns:

- demand ID
- item
- requested quantity
- covered by stock
- assigned to release
- held quantity
- planned completion
- promise basis
- late risk
- comments

This view is important because it prevents hidden over-assignment or hidden under-coverage.

## 4.4 Material Risk And Commitment View

This is the evolution of today’s `Material_Plan`.

Purpose:

- explain why a release is material-feasible or blocked

Audience:

- material planners
- PPC
- procurement coordination

Must show:

- inventory before
- reserved quantity
- consumed quantity
- remaining inventory
- shortage quantity
- byproduct/co-product generated
- lineage quality
- hold reason

Recommended grouping:

- by release
- then by plant
- then by material

This view should also show whether the plan relied on degraded assumptions:

- recomputed inventory lineage
- conservative inventory blend
- non-authoritative snapshot chain

## 4.5 Process Flow View

This is the evolution of today’s detailed schedule.

Purpose:

- step-by-step operational plan with better readability

Audience:

- planners
- process coordinators
- dispatch supervisors

Must show:

- release or batch unit
- step
- assigned resource
- planned start
- planned end
- duration
- transfer wait
- queue wait
- setup/changeover context
- frozen marker
- violation marker

Recommended grouping:

- release header
- batch or heat subheader
- detailed process rows
- rolling/dispatch subheader

This view already exists in partial form, but it should become more obviously readable and less like a technical export.

## 4.6 Equipment Dispatch View

This is the machine-centric execution view.

Purpose:

- tell each area what each machine should run in sequence

Audience:

- shift supervisors
- dispatchers
- control room

Must show:

- machine
- planned sequence
- release
- job
- start
- end
- duration
- setup/changeover indicator
- idle gap
- queue risk
- frozen/running state

This should remain operationally simple and should not get overloaded with planning-only metadata.

## 4.7 Constraint And Exception View

This should become a first-class sheet, not just scattered warnings.

Purpose:

- one place to see why the plan is weak, degraded, blocked, or risky

Audience:

- planners
- master-data owners
- management

Exception categories:

- material shortages
- invalid BOM or transformation path
- missing route/resource master data
- degraded inventory lineage
- legacy or fallback logic used
- queue violations
- continuity breaks
- overloads
- no feasible machine or route
- promise-risk cases

Recommended fields:

- severity
- exception type
- release or item affected
- resource or plant affected
- reason
- planning impact
- blocker or warning
- recommended action

This is one of the highest-value visibility additions we can make.

## 4.8 Capacity And Load View

This should be split clearly into two lenses.

### Rough-Cut Load Lens

Purpose:

- fast heuristic bottleneck scan

Must show:

- available hours
- demand hours
- overload
- utilization
- capacity basis = rough-cut

### Finite Occupancy Lens

Purpose:

- actual scheduled machine occupancy from the accepted plan

Must show:

- scheduled hours
- setup/changeover occupancy
- idle hours
- overload
- queue or continuity pressure
- capacity basis = finite schedule

This separation is essential for trust and readability.

## 4.9 Promise And Service View

This extends today’s CTP output into a broader planning-service view.

Purpose:

- explain promise viability and service risk

Audience:

- customer service
- planning
- commercial operations

Must show:

- request
- requested date
- earliest completion
- delivery basis
- plant-completion feasibility
- delivery feasibility
- inventory lineage quality
- campaign action
- whether demand joins existing release
- whether a new release is needed
- risk or blocker reason

This view should be understandable even by users who do not know the internal APS objects.

## 4.10 Scenario Lab

This is the most important new visibility layer after the Control Tower.

Purpose:

- compare full alternative plans, not just summary KPIs

Audience:

- planners
- management
- S&OP style decision meetings

The Scenario Lab should have four subviews:

### Scenario Register

One row per scenario:

- scenario name
- type
- baseline reference
- override summary
- status
- approved or draft

### Scenario Summary Compare

Compare:

- released quantity
- held quantity
- on-time percentage
- lateness
- throughput
- overload count
- top bottleneck
- degraded-planning count

### Scenario Delta View

Show exactly what changed:

- releases added or removed
- releases moved earlier or later
- new holds
- material deltas
- resource sequence changes

### Scenario Drilldown Views

For the selected scenario, show:

- release cockpit
- process flow
- equipment dispatch
- material plan
- exception view

This is what turns scenarios into a real planning workbench.

---

## 5. Readability Rules For All Views

To improve visibility, every planning view should follow a few consistent rules.

## 5.1 One row should represent one business meaning

Avoid rows that mix:

- demand coverage
- stock draw
- balancing quantity
- process execution

These should be separated clearly.

## 5.2 Every status should have a reason

Examples:

- `MATERIAL HOLD` is not enough
- `LATE` is not enough
- `BLOCKED` is not enough

The view should also say why.

## 5.3 Every view should show its planning basis

Examples:

- finite schedule
- rough-cut heuristic
- plant completion only
- delivery modeled
- authoritative inventory lineage
- degraded lineage

## 5.4 Summary first, detail below

Every major sheet should begin with:

- what this view is
- what period it covers
- what plan it belongs to
- a small KPI ribbon

Then show detail.

## 5.5 Filters should reflect real planner questions

Not just technical fields.

Recommended common filters:

- release status
- plant
- line/stream
- resource
- material issue
- due-date risk
- scenario
- warning or blocker type

---

## 6. What We Can Build Immediately On The Existing Engine

Without redesigning the core engine, the current APS can already support the following improvements.

## 6.1 Control Tower

Can be derived from existing:

- `Campaign_Schedule`
- `Schedule_Output`
- `Material_Plan`
- `Capacity_Map`
- `CTP_Output`
- `Scenario_Output`

## 6.2 Constraint And Exception View

Can be built from existing flags:

- release status
- material issue
- heats calculation warnings
- queue violation markers
- inventory lineage fields
- capacity overloads
- CTP solver or blocker status

## 6.3 Demand Coverage View

Mostly derivable today from:

- sales orders
- FG stock consumption
- campaign production orders
- scheduled RM rows
- held campaigns

This is one of the highest-value immediate additions.

## 6.4 Finite Capacity Lens

Already partly supported through schedule-derived occupancy.

This can be made into a planner-facing view without major engine change.

## 6.5 Scenario Delta View

The scenario runner already generates alternative schedules.

What is missing is mainly the comparison presentation:

- release deltas
- resource deltas
- material deltas
- exception deltas

---

## 7. What Needs Moderate Enhancement

The following views are still compatible with the current engine, but would benefit from more derived logic.

## 7.1 Demand Coverage Reconciliation

To make this truly strong, the APS should explicitly separate:

- stock-covered demand
- make quantity
- held quantity
- overbuild quantity
- balancing quantity

## 7.2 Exception Severity

The current engine exposes many warnings and blockers, but they should be normalized into:

- blocker
- severe warning
- warning
- informational

## 7.3 Scenario Drilldown Workspace

The engine already reruns scenarios, but the workbook still needs a structure to hold:

- multiple alternative release views
- multiple alternative schedule views
- multiple alternative material views

without clutter.

## 7.4 Line And Continuity Visibility

The current engine is resource-family oriented.

Before we introduce formal stream topology, we can still improve visibility by exposing:

- machine continuity gaps
- queue pressure
- campaign handoff gaps
- RM starvation risk

---

## 8. Recommended View Roadmap

The best way to improve visibility without destabilizing the APS is to phase it.

## Phase 1: High-Value Visibility

Build first:

- Control Tower
- Release Cockpit enhancement
- Constraint And Exception View
- Demand Coverage View

These will improve trust the fastest.

## Phase 2: Operational Visibility

Build next:

- improved Process Flow View
- improved Equipment Dispatch View
- finite Capacity And Load View

These help planners and supervisors use the plan operationally.

## Phase 3: Scenario Workbench

Build next:

- Scenario Register
- Scenario Summary Compare
- Scenario Delta View
- selected-scenario drilldown views

This turns the APS into a real planning lab.

## Phase 4: Generic-Ready Views

Once the generic APS model matures, map the same visibility model into:

- steel views
- oil views
- tire views

without changing the underlying view architecture.

---

## 9. Recommended Next Build Slice

If the goal is to improve visibility quickly on the current APS, the next best implementation slice is:

1. add a new `Exception_View`
2. add a new `Demand_Coverage`
3. upgrade `Scenario_Output` into a two-level scenario sheet:
   - top summary compare
   - bottom selected-scenario detail
4. upgrade the top summary into a real `Control_Tower`

That combination would immediately make the APS easier to trust, easier to explain, and easier to operate.

---

## 10. End-State For Visibility

The APS should eventually feel like a connected planning workbench rather than a collection of output sheets.

The ideal flow should be:

- start in Control Tower
- move into Release Cockpit
- inspect Material Risk or Equipment Dispatch if needed
- open Exception View to understand blockers
- compare alternatives in Scenario Lab
- promote the selected scenario as the accepted plan

That is the visibility model we should build toward on top of the existing engine.
