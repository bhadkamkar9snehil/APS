# APS Generic Architecture And Scenario Workbench

## 1. Purpose

This document defines the future layout of the APS as a **generic planning platform** that can be configured for different industries without rewriting the planning logic for each domain.

The target is:

- one APS core
- one common planning language
- multiple industry packs
- multiple site packs
- readable plan outputs
- a proper what-if and simulation workbench

The goal is **not** to force steel, oil, tire, or any other industry into identical data sheets or identical views.

The goal is to make them share the same **planning grammar** while allowing domain-specific configurators and domain-specific views on top of that grammar.

---

## 2. Core Design Principles

### 2.1 One engine, many configurations

The planning engine should only understand generic manufacturing concepts:

- demand
- item
- material transformation
- resource
- resource pool
- route step
- setup/changeover
- transfer and residence constraints
- buffer/storage
- line/stream topology
- release rule
- promise rule
- scenario override

Industry-specific language should sit on top of these concepts, not inside them.

### 2.2 Separate structure from policy

The APS must distinguish between:

- **what exists**: items, resources, routes, lines, tanks, tools
- **what is allowed**: pairings, capacities, shelf-life rules, queue windows
- **what is preferred**: sequencing goals, continuity goals, campaigning preferences
- **what is simulated**: outages, demand spikes, recipe changes, alternate sourcing

This avoids mixing plant master data with planning policy and scenario overrides.

### 2.3 Readability is part of the product

A plan is only useful if planners can quickly answer:

- what was released
- what was held
- what is running where
- what inventory is consumed or short
- what constraint is driving lateness
- what changed between scenarios

The APS should therefore produce planning views designed for decisions, not just raw tables.

### 2.4 Degraded planning must be obvious

If a plan uses fallback logic, degraded inventory lineage, missing routing, or policy exceptions, the output should clearly state that the plan is degraded.

Warnings should not be buried in background metadata.

---

## 3. Three-Layer Architecture

## 3.1 Layer 1: Core APS Model

This layer is industry-neutral.

It defines the canonical planning objects:

- `Demand`
- `Item`
- `Transformation`
- `Resource`
- `Resource_Pool`
- `Route`
- `Route_Step`
- `Buffer`
- `Line_or_Stream`
- `Constraint`
- `Policy`
- `Scenario`
- `Plan_Result`

This layer should never depend on steel-specific, oil-specific, or tire-specific words.

## 3.2 Layer 2: Industry Pack

This layer maps industry language into the core model.

Examples:

- In steel:
  - heat = batch unit
  - campaign = release batch
  - CCM to RM pairing = stream topology
  - billet/byproduct/scrap = transformation outputs

- In oil:
  - crude run = release batch
  - unit sequence = route
  - tank farm = buffers
  - cuts/byproducts = co-products

- In tire:
  - compound batch = batch unit
  - mixer/building/curing = route steps
  - mold/tool = constrained resource
  - green tire and cured tire = intermediate and finished items

The industry pack should define:

- planning vocabulary
- default entities
- industry-specific policies
- industry-specific output terminology
- industry-specific validation rules

## 3.3 Layer 3: Site Pack

This layer defines a particular plant or business unit.

It contains:

- actual items
- actual resources
- actual routings
- actual line topology
- actual calendars and downtime
- actual setup/changeover matrices
- actual inventory policies
- actual planning objectives and release policies

This layer is where one steel plant differs from another, or where one refinery differs from another.

---

## 4. Future Configuration Layout

The APS should be organized into five configuration zones.

## 4.1 Zone A: Global Planning Policy

This is the smallest layer and should contain only plant-wide planning decisions.

Typical content:

- planning horizon
- release policy
- schedule anchor policy
- overlap vs serialization policy
- fallback policy
- promise policy
- byproduct policy
- inventory lineage policy
- scenario approval policy

This zone should be compact and heavily validated.

## 4.2 Zone B: Canonical Master Data

This is the backbone of the APS.

Typical entities:

- items
- material classes
- resources
- resource pools
- resource calendars
- buffers/storage locations
- transformations
- routes
- route conditions
- changeover rules
- transfer and residence rules
- line/stream topology

This zone should use controlled values and strong validation.

## 4.3 Zone C: Industry Pack Configuration

This is where domain rules are plugged in.

Typical content:

- industry-specific planning terms
- default route templates
- default material roles
- default co-product treatment
- default quality gates
- default continuity logic
- industry-specific KPIs
- output label mappings

This keeps the APS generic without making the planner read generic jargon all day.

## 4.4 Zone D: Site Planning Policy

This zone captures how one plant wants the model to behave.

Typical content:

- preferred line pairings
- release batch sizing rules
- line opening thresholds
- continuity targets
- setup minimization priorities
- lateness penalty weighting
- inventory reservation logic
- preferred alternate routes
- material hold rules

This is where plant behavior becomes operationally realistic.

## 4.5 Zone E: Scenario Overrides

This zone should never overwrite baseline masters.

It should only describe temporary scenario changes such as:

- demand increase
- demand cancellation
- new order insertion
- resource outage
- alternate route enablement
- shift extension
- yield loss
- lead-time change
- inventory adjustment
- line policy change

Scenarios should be layered on top of the baseline, not mixed into it.

---

## 5. Canonical Master Model

To support multiple industries, the APS should standardize around the following master entities.

## 5.1 Items

Every material or product should have:

- item identifier
- item class
- unit of measure
- product family
- grade/specification
- storage behavior
- shelf-life or age constraint
- whether it is demand-facing, intermediate, co-product, or waste

## 5.2 Transformations

This generalizes BOMs, recipes, and process yields.

Each transformation should define:

- source step or producing process
- input items
- output items
- yield/loss rules
- co-product rules
- batch-size behavior
- quality restrictions
- validity conditions

## 5.3 Resources

Each resource should define:

- resource identity
- resource type
- pool membership
- capacity mode
- calendar
- continuity behavior
- eligible operations
- setup/changeover behavior
- upstream/downstream pairing restrictions

## 5.4 Routes And Steps

Routes should be defined as condition-based valid paths, not a single hardwired sequence.

Each route step should support:

- step type
- sequence
- required or optional status
- valid resources or pools
- standard duration logic
- queue and transfer relationships
- quality or attribute conditions

## 5.5 Buffers And Storage

Not all manufacturing flows are immediate handoffs.

Buffers should support:

- inventory location
- storage capacity
- shelf-life/aging
- hold rules
- transfer lead time
- dispatch priority

## 5.6 Streams Or Lines

This is a critical future enhancement.

A stream or line should represent:

- allowed upstream/downstream resource pairing
- continuity expectations
- direct-feed or delayed-feed rules
- opening and closing cost
- cross-feed allowance
- minimum run size

This lets the same APS model:

- steel cast-to-roll streams
- oil process-to-tank-to-blend paths
- tire mixer-to-building-to-curing flow families

---

## 6. Planning Output Layout

The current APS should evolve from large raw output tables into a readable multi-view planning workbench.

Every run should produce both:

- a machine-readable detail model
- a planner-readable set of views

## 6.1 Control View

This is the top-level run summary.

It should answer:

- when was the plan anchored
- which policy pack was used
- whether the run is baseline or scenario
- whether degraded logic was used
- total demand
- total released
- total held
- on-time percentage
- most constrained area
- major blockers

This is the first screen a planner should see.

## 6.2 Release View

This is the operational replacement for today’s campaign-oriented summary.

Each release unit should show:

- release identifier
- demand group or customer set
- item family
- quantity
- release status
- reason if held
- heat or batch basis
- due date
- completion date
- lateness
- line/stream assignment
- warnings

This should be simple, scannable, and filterable.

## 6.3 Material Commitment View

This should clearly show:

- starting inventory
- reserved inventory
- consumed inventory
- co-products generated
- shortages
- holds triggered by material
- inventory lineage quality

This view should explain why material-feasible and material-infeasible releases differ.

## 6.4 Process Flow View

This is the step-by-step plan view.

It should show:

- release or batch unit
- step sequence
- assigned resource
- planned start
- planned end
- transfer wait
- queue wait
- setup/changeover context
- violation flags

This is the main detailed plan, but it must remain readable.

## 6.5 Equipment Dispatch View

This is the machine-centric view.

For each resource, show:

- job order
- product or release
- start and end
- setup/changeover
- idle gap
- blocked/starved markers
- continuity breach markers

This lets supervisors read the schedule from the asset side.

## 6.6 Constraint And Exception View

This is one of the most important future readability improvements.

Instead of making planners hunt through detail rows, the APS should explicitly show:

- material holds
- missing master data
- degraded planning modes
- line continuity breaks
- queue violations
- promise risks
- overtime dependence
- overloaded resources

This should be treated as a first-class planning output, not an afterthought.

## 6.7 Comparison View

This is needed for both baseline vs scenario and scenario vs scenario reading.

It should show deltas for:

- released quantity
- held quantity
- lateness
- throughput
- utilization
- inventory risk
- most constrained resource/line
- plan quality flags

The comparison view is the bridge between analytics and decision-making.

---

## 7. Readability Principles For Plans

To make plans easier to understand, the APS should follow these rules.

## 7.1 One row, one meaning

Avoid rows that mix:

- demand linkage
- overbuild
- inventory reservation
- schedule detail

If a plan overbuilds, the excess should be shown explicitly as:

- balancing quantity
- minimum batch quantity effect
- stock build

It should not be hidden inside demand-linked rows.

## 7.2 Separate summaries from details

Every planning view should begin with a small summary ribbon and then show detail below it.

Summary:

- total volume
- count of releases
- count of holds
- count of warnings
- lateness

Detail:

- row-level operational information

## 7.3 Use reason fields aggressively

Statuses without reasons are weak.

Every hold, late status, or degradation should have a reason field, such as:

- material shortage
- invalid transformation path
- missing route
- invalid line pairing
- queue breach
- no feasible resource
- non-authoritative inventory lineage

## 7.4 Show planning basis explicitly

The planner should always know whether a result is based on:

- finite schedule
- rough-cut estimate
- plant completion only
- delivery-modeled promise
- authoritative inventory lineage
- conservative fallback logic

## 7.5 Keep drill-down paths stable

A planner should be able to start from:

- release
- resource
- material
- scenario

and drill into the same underlying plan without changing terminology halfway through.

---

## 8. Scenario And Simulation Workbench

The APS should move from a simple scenario summary runner to a true scenario workbench.

## 8.1 Baseline First

Every scenario must be generated from a named baseline.

A scenario should never mutate the baseline itself.

The workbench should make this explicit:

- baseline plan
- scenario overlay
- scenario result
- delta versus baseline

## 8.2 Scenario Types

Scenarios should be categorized so planners know what they are testing.

Recommended categories:

- demand scenarios
- supply scenarios
- capacity scenarios
- maintenance/disruption scenarios
- routing/policy scenarios
- inventory scenarios
- service/promise scenarios
- stress-test scenarios

## 8.3 Scenario Definition Model

Each scenario should define:

- scenario name
- scenario type
- baseline reference
- active override set
- start and end validity
- owner
- purpose
- approval state

Each override should be explicit, for example:

- `RM-02 unavailable for 2 days`
- `demand +15% for Product Family A`
- `allow alternate route B`
- `defer byproduct availability`
- `open second stream at lower threshold`

## 8.4 Scenario Outputs

A real scenario should produce a full plan, not only KPI differences.

For each scenario, the APS should generate:

- release view
- material commitment view
- process flow view
- equipment dispatch view
- comparison view
- exception view

This allows planners to ask not just “which scenario is better,” but also “what actually changes on the floor.”

## 8.5 Side-By-Side Comparison

The workbench should support comparison at three levels.

### Level 1: Executive Comparison

- released volume
- service level
- lateness
- throughput
- utilization
- risk flags

### Level 2: Operational Comparison

- which releases move earlier or later
- which resource sequences change
- which materials become constrained
- which lines open or close

### Level 3: Constraint Comparison

- which rule becomes binding
- which queue/residence breach appears
- which inventory assumption changes
- which scenario uses degraded logic

## 8.6 Promote Scenario To Plan

Once a scenario is accepted, there should be a controlled action:

- baseline remains preserved
- selected scenario becomes approved plan candidate
- planners can publish or operationalize it

This is important because scenario work should lead naturally into planning decisions.

---

## 9. Simulation Modes

The APS should support different simulation intents.

## 9.1 What-If Simulation

Quick comparison of one or more temporary changes against the current baseline.

Use case:

- what if RM-01 goes down tomorrow
- what if demand spikes by 20%
- what if we allow a second line to open

## 9.2 Stress Testing

Test robustness under unfavorable conditions.

Use case:

- what happens if yield worsens
- what happens if one shared resource becomes unavailable
- what happens if inventory lineage becomes non-authoritative

## 9.3 Policy Simulation

Test different operating rules rather than disruptions.

Use case:

- strict serialization versus controlled overlap
- immediate versus deferred co-product availability
- aggressive versus conservative promise logic

## 9.4 Strategic Simulation

Used for planning design rather than daily dispatch.

Use case:

- add a second curing line
- shift a tank policy
- change line topology
- redesign campaign sizing

This is closer to network design and S&OP support than daily scheduling.

---

## 10. How This Layout Supports Multiple Industries

## 10.1 Steel

Readable views would emphasize:

- release batches
- heats or cast runs
- stream continuity
- SMS and RM synchronization
- queue and hold windows
- co-products and scrap treatment

## 10.2 Oil

Readable views would emphasize:

- unit runs
- tank movements
- blend decisions
- storage and residence constraints
- co-product yield shifts
- unit continuity and changeover cost

## 10.3 Tire

Readable views would emphasize:

- compound batches
- mold/tool constraints
- curing press utilization
- batch-to-order traceability
- intermediate inventory buffers
- changeover and tool-family sequencing

The core APS stays the same. The views and terminology change by industry pack.

---

## 11. Recommended Rollout Path

## Phase 1: Canonical Planning Language

Stabilize the generic planning vocabulary and stop adding new steel-specific terms into the core.

## Phase 2: Configuration Zoning

Separate:

- global policies
- canonical masters
- industry pack rules
- site pack rules
- scenario overrides

## Phase 3: Readable Plan Views

Redesign planning outputs into:

- control view
- release view
- material commitment view
- process flow view
- equipment dispatch view
- exception view

## Phase 4: Scenario Workbench

Move from KPI-only scenario summaries to full plan comparison.

## Phase 5: Stream And Topology Modeling

Introduce formal lines/streams so continuity-driven operations can be modeled cleanly.

## Phase 6: Industry Packs

Keep steel as the first fully mature pack, then add oil and tire using the same underlying model.

---

## 12. Decision Recommendations

The following choices are recommended for the future APS.

### Recommendation 1

Do not aim for identical layouts across industries.

Aim for a shared planning meta-model with industry-specific terminology and views.

### Recommendation 2

Make the APS configuration layout layered and explicit:

- global policy
- canonical masters
- industry pack
- site pack
- scenarios

### Recommendation 3

Treat planning readability as a core product requirement.

The APS should expose:

- why a plan is valid
- why a plan is degraded
- why a release is held
- why one scenario is better than another

### Recommendation 4

Promote scenario planning from a KPI summary tool into a true alternative-plan workbench.

### Recommendation 5

Build future industry support by extending the same planning grammar, not by cloning the APS into separate domain-specific engines.

---

## 13. Target End-State

The end-state APS should behave like this:

- one generic core planning model
- one robust configuration structure
- domain packs for industry vocabulary and default behavior
- site packs for real plant logic
- readable planning views for planners and operations
- a scenario lab that can compare full alternative plans, not just summary metrics

That is the right foundation for a future APS that can grow from steel into oil, tire, and other manufacturing environments without becoming fragile or domain-locked.
