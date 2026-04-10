# APS Functional Concept Guide

**Purpose**

This document explains the planning system in business and operations terms only.
It describes what the APS does, what planning decisions it makes, what each major planning view means, and which planning policies are currently enforced.

---

## 1. What This APS Is Designed To Do

This APS is built to answer one core question:

**Given current demand, inventory, route rules, material availability, and plant constraints, what can be released, when can it run, and where are the risks?**

In practical terms, the APS supports five planning activities:

1. Convert open customer demand into **net make demand** after using available finished goods.
2. Group compatible demand into **production campaigns**.
3. Check whether those campaigns are **material-feasible** in sequence.
4. Build a **finite production schedule** across the plant.
5. Evaluate **what-if scenarios** and **capable-to-promise** requests against that same planning logic.

This is not just a calculator of hours or a dispatch list generator. It is a release-and-schedule decision system.

---

## 2. Planning Scope

The current APS models an integrated steel flow with these major stages:

1. **Blast Furnace**
2. **SMS / Steelmaking**
3. **Rolling Mill**

Within SMS, the modeled operating stages are:

1. **EAF**
2. **LRF**
3. **VD** when required
4. **CCM**

Within Rolling Mill, the APS models rolling as the final production stage tied directly to customer-facing finished goods.

The planning logic therefore spans both:

1. **material transformation**
2. **finite equipment scheduling**

That combination is what lets the APS decide not just whether something is required, but whether it is actually releasable and schedulable.

---

## 3. Main Planning Objects

### Sales Order

A sales order represents customer demand for a finished SKU by quantity and date.

Each order carries, directly or indirectly:

1. customer priority
2. delivery expectation
3. product grade
4. section or size
5. route family
6. campaign compatibility signals

### Finished Goods Stock

Finished goods stock is the first source used to satisfy open demand.

If stock covers part or all of an order:

1. the covered quantity is consumed first
2. only the uncovered balance becomes plant make demand

This is a critical concept in the APS:

**planning starts from net make quantity, not blindly from order quantity.**

### Campaign

A campaign is the APS release unit.

It is a planned bundle of compatible production demand that should move through the plant as one planning family.

A campaign carries:

1. total planned output tonnage
2. heat count
3. grade and product family identity
4. order membership
5. release status
6. material feasibility status
7. inventory before and after commitment

### Production Order

Within a campaign, each remaining demand slice is still preserved as a production-order level requirement.

This is what allows the APS to:

1. schedule rolling at order level
2. trace campaign content back to specific sales orders
3. support campaign splits when quantity exceeds campaign size limits

### Heat

A heat is the primary batch concept used to size upstream plant execution.

The APS calculates how many heats are required for a campaign by tracing finished demand back to the configured primary batch stage.

That means heat count is not merely a rounded tonnage rule. It is intended to be grounded in the material structure of the plant.

---

## 4. Demand-to-Release Logic

The APS uses the following release sequence:

1. identify open demand
2. consume available finished goods
3. create remaining make demand
4. group compatible make demand into campaigns
5. size campaigns within configured min and max ranges
6. calculate required heats
7. check campaign material feasibility in release order
8. release only those campaigns that remain feasible

This sequence matters because a later campaign does not get to assume inventory that has already been committed to an earlier one.

That is one of the biggest differences between a simple requirement view and an actual APS release engine.

---

## 5. How Campaigning Works

Campaigning is the bridge between customer demand and plant execution.

The APS groups orders by compatibility signals such as:

1. grade
2. route family
3. route variant
4. product family
5. campaign group

Campaigning is used for two reasons:

1. to create realistic plant release lots
2. to preserve process continuity and reduce unnecessary fragmentation

### Campaign Size

Each campaign is bounded by configured minimum and maximum tonnage rules.

This means:

1. very small demand may still need to be grouped into a campaign unit
2. very large compatible demand may be split into more than one campaign

### Manual Campaign Assignment

The planner can also manually pre-bundle orders into a campaign family.

If that manually assigned family is larger than the maximum campaign size, the APS may still split it into more than one campaign while preserving the manual grouping identity.

So manual campaign assignment is best understood as:

**planner-controlled campaign grouping intent, not an unconditional single-campaign override.**

---

## 6. Heat Calculation and Primary Batch Trace

Heat calculation is one of the most important planning steps because it drives upstream plant load.

The APS determines required heats by tracing campaign demand back through the product structure to the configured primary batch stage.

This means the APS asks:

**How much primary batch material is required to satisfy the finished demand in this campaign?**

Then it converts that requirement into a heat count using the configured batch size.

### Production Rule

The current production policy is strict:

1. if primary-batch trace is valid, the campaign can proceed to material checking
2. if primary-batch trace fails, the campaign is blocked

This is deliberate. The APS is no longer allowed to quietly create a seemingly valid heat count from broken structure data.

That protects planners from releasing campaigns with misleading upstream load.

---

## 7. Material Logic

Material planning in this APS operates in two distinct ways:

1. **network requirement view**
2. **release-sequenced commitment view**

These are related, but they do not answer the same question.

### BOM Explosion

The BOM explosion view answers:

**What materials does the plant network need in total for the current demand pool?**

It is:

1. aggregated across demand
2. grouped by plant and material type
3. netted against available inventory

It is useful for seeing total requirement pressure.

It is not the same thing as release feasibility.

### Material Plan

The Material Plan answers:

**For each campaign in release order, what was available, what got consumed, what remains, and what caused any hold?**

It is:

1. campaign-by-campaign
2. sequence-aware
3. inventory-state-aware

This means later campaigns see inventory **after** earlier campaigns consume stock.

### Why They Differ

`BOM_Output` and `Material_Plan` should not be expected to match line-for-line.

That is intentional.

`BOM_Output` is the total requirement lens.

`Material_Plan` is the release commitment lens.

---

## 8. Material Feasibility and Campaign Holds

Campaign material feasibility is checked sequentially.

For each campaign, the APS evaluates:

1. gross requirement by material
2. available inventory before release
3. covered quantity
4. remaining inventory after commitment
5. shortages or structure faults

### Release Outcomes

A campaign can end up as:

1. **RELEASED**
2. **MATERIAL HOLD**

### What Causes a Hold

A campaign is held when:

1. required material is not available
2. product structure is broken
3. primary-batch trace is invalid

The key concept is:

**held campaigns do not consume future inventory.**

This prevents a bad or infeasible campaign from contaminating the release position of later campaigns.

---

## 9. Byproducts and Secondary Material Effects

The APS recognizes byproducts and process-side outputs such as waste or recoverable side streams.

However, the production policy is conservative:

**byproducts are treated as deferred rather than immediately reusable in live planning.**

That means the APS does not assume a byproduct becomes instantly available to satisfy another requirement in the same planning pass.

This policy avoids optimistic planning where a side stream is counted before it is operationally usable.

---

## 10. Finite Scheduling Logic

Once campaigns are released, the APS schedules them across the plant as finite work.

This is not a simple capacity estimate. It is a real sequencing decision across equipment and stages.

### SMS Sequence

Each heat moves through the required SMS route:

1. EAF
2. LRF
3. VD when required
4. CCM

### Rolling Sequence

After upstream production completes, rolling orders are scheduled as the final campaign output stage.

### What the Scheduler Respects

The finite schedule is anchored on:

1. explicit planning start
2. equipment availability
3. route sequence
4. queue-time rules
5. transfer-time rules
6. downtime windows
7. frozen work already in progress
8. campaign serialization policy

This is what makes the schedule operationally meaningful rather than just mathematically compact.

---

## 11. Queue and Transfer Logic

The APS distinguishes between:

1. **transfer time**
2. **queue wait**

Transfer time is the physical time required to move material from one stage to the next.

Queue wait is the additional time after transfer before the next stage actually starts.

This distinction matters because:

1. a normal transfer should not be treated as a process violation
2. excess waiting after transfer can create a real metallurgical or flow risk

The APS therefore evaluates queue status using:

**elapsed gap minus transfer time**

That gives a much more realistic view of whether the stage-to-stage handoff is healthy.

---

## 12. Frozen Work and Running Work

The APS can honor work that is already in progress or already committed on a machine.

This is represented as frozen work.

Frozen work acts as a hard scheduling anchor:

1. it pins occupied machine time
2. it prevents new work from being scheduled through that same time window
3. it can influence the planning start if no explicit anchor is supplied

The APS also validates frozen work instead of silently tolerating bad assignments.

That means frozen work must be:

1. timestamp-valid
2. resource-valid
3. compatible with the machine family it claims to use

---

## 13. Campaign Serialization Policy

The APS supports two operating philosophies for campaign release flow:

1. **strict end-to-end serialization**
2. **overlap after upstream milestones**

### Strict End-to-End

Under strict serialization, the next campaign waits until the prior campaign has fully completed end-to-end.

This is the more conservative policy.

### Overlap-After-SMS

Under overlap-after-SMS logic, the next campaign may begin once the prior campaign has progressed far enough upstream, rather than waiting for total end-to-end completion.

This can materially improve throughput if the plant actually operates that way.

The APS therefore treats campaign overlap as a business rule, not as a hidden modeling assumption.

---

## 14. Capacity Views

The APS supports two different ways of looking at load.

### Rough-Cut Capacity

This is a fast screening view.

It answers:

**Where is the likely load pressure if current campaign demand is translated into standard machine hours?**

It is useful for:

1. bottleneck screening
2. directional overload review
3. scenario comparison at a high level

It is not intended to equal the finite schedule line-for-line.

### Finite Schedule Occupancy

This is the detailed dispatch truth.

It answers:

**What work is actually scheduled on which resource and at what time?**

If the question is operational release, dispatch, or exact machine occupancy, this is the authoritative basis.

---

## 15. Capable-to-Promise

The APS also supports promise evaluation for new requests.

CTP asks:

**Can the plant absorb this request, and if so, what is the earliest modeled completion point?**

### How It Evaluates a Request

The APS checks:

1. whether current stock can satisfy the request
2. whether the request can join an existing compatible campaign
3. whether a new campaign must be created
4. whether required materials are available after already committed campaigns
5. what completion date the finite schedule produces

### Important Semantic Rule

The current CTP result is based on **plant completion**, not customer delivery.

So the APS now explicitly distinguishes:

1. earliest plant completion
2. delivery feasibility, when delivery is not modeled

This avoids overstating what the promise actually means.

### Inventory Lineage Rule

The CTP result also depends on whether committed inventory position is trustworthy.

If committed inventory lineage is not authoritative, the APS treats that as a planning-quality issue rather than silently issuing a confident promise.

---

## 16. Scenario Planning

The scenario function is intended to answer:

**How does the plan change if key assumptions change?**

Typical scenario levers include:

1. demand increase
2. equipment loss
3. extra available hours
4. yield change
5. rush demand
6. campaign size policy changes

A proper scenario run is not just a KPI tweak.

It should:

1. start from the same baseline demand and inventory state
2. apply the scenario assumptions
3. rebuild campaigns
4. rerun material checks
5. reschedule the plant
6. compare outputs side by side

That is the functional meaning of scenario planning in this APS.

---

## 17. Main Planning Views and What They Mean

### Campaign Schedule

The management summary of released and held campaigns.

Use it for:

1. release status
2. campaign size
3. heat count
4. stage milestone dates
5. due-date status

### Schedule Output

The full planner view across campaigns, heats, operations, and rolling orders.

Use it for:

1. operation timing
2. queue status
3. campaign flow
4. order-linked rolling dispatch

### Equipment Schedule

The machine-by-machine dispatch view.

Use it for:

1. shift handover
2. machine-level sequencing
3. overlap validation
4. equipment ownership of work

### Material Plan

The campaign commitment and hold-diagnosis view.

Use it for:

1. inventory before release
2. quantity covered by inventory
3. make-or-convert quantities
4. shortages
5. campaign hold reasons

### BOM Output

The network requirement view.

Use it for:

1. total plant material requirement
2. by-plant breakdown
3. by-material-type breakdown
4. stock coverage and residual requirement

### Capacity Map

The rough-cut load screen.

Use it for:

1. bottleneck screening
2. directional capacity pressure
3. fast scenario comparison

### CTP Output

The request feasibility view.

Use it for:

1. stock response
2. campaign-join response
3. plant completion position
4. material blockers
5. planning-quality blockers

---

## 18. What “Correct” Looks Like in This APS

A healthy APS plan should satisfy all of the following:

1. no order is accidentally duplicated across campaigns
2. finished goods coverage is used before new make quantity is created
3. campaign tonnage reconciles to scheduled rolling output
4. held campaigns do not leak into released production
5. queue statuses reflect actual waiting after transfer
6. machine schedules do not overlap
7. each heat follows a valid route sequence
8. due-date status matches actual terminal completion
9. material commitment reflects release order
10. CTP does not claim delivery feasibility when only plant completion is modeled

These are not cosmetic checks. They define whether planners can trust the system.

---

## 19. Current Planning Policies Locked for Safer Operation

The current APS has several deliberate planning guardrails:

1. missing critical route or resource master data is treated as a blocker, not something to invent around
2. failed primary-batch trace blocks campaign release
3. byproduct availability is handled conservatively
4. non-authoritative committed inventory lineage can block promise logic
5. rough-cut capacity is treated as a heuristic, not as a substitute for finite scheduling

These policies are important because they reduce the risk of plausible-looking but operationally unsafe plans.

---

## 20. Current Intentional Boundaries

The APS is strong on production release and plant scheduling, but a few boundaries remain intentional:

1. customer delivery is not yet modeled as a transport or dispatch process separate from plant completion
2. rough-cut capacity remains a screening tool, not a full schedule mirror
3. scenario comparison still needs to mature into a richer side-by-side planning workbench
4. serialization policy still depends on the selected operating rule and must match actual plant practice

These are not hidden defects. They are important scope boundaries to understand when using the APS.

---

## 21. Short Functional Summary

The APS we have built is a **release-driven, material-aware, finite scheduling system** for an integrated steel flow.

Its logic can be summarized in one sentence:

**Net the demand, build compatible campaigns, validate material feasibility in sequence, schedule the released work finitely across the plant, and use that same planning logic for scenarios and promise checks.**

That is the core functional identity of the system.
