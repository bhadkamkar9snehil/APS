# APS Steel-Domain Architecture and Roadmap

> Canonical reference for the current state, gaps, target steel-domain model, and implementation direction of APS.
>
> Governing implementation epic: GitHub issue #2.
>
> UI is intentionally not a current priority. This document is about the domain model, master data, planning logic, solver formulation, material flow, execution boundaries, and traceability.

---

## 1. Executive position

APS already has a strong planning/scheduling backbone: commercial lineage, campaign allocation, inventory netting, finite scheduling, plan versions, replanning, Work Order release, and material genealogy are substantially established.

The main weakness is that the physical production model is still too generic for a domain-true steel APS. The current production-structure path effectively begins at Heat -> CCM -> Billet -> Rolling. The next major evolution is to model the actual steel route and the constraints that make steel planning difficult:

```text
SAP SO / stock requirement
  -> Production Order
  -> Campaign
  -> furnace-feasible Heat structure
  -> EAF
  -> LRF
  -> optional/required VD
  -> CCM / cast sequence / strands
  -> billet supply
  -> optional/required reheating
  -> RM
  -> TMT / cooling / cutting / bundling / coiling / finishing
  -> individual bundle/coil / FG lot
  -> inventory allocation / SO / dispatch
```

The architecture should be explicitly **steel-specific at the domain level while keeping physical equipment counts data-driven**. EAF, LRF, VD, CCM, reheating furnace, RM, TMT, bundle, coil, heat, cast sequence, strand and billet are first-class steel concepts. Individual physical equipment such as `EAF-1`, `CCM-2`, `RM-1` remain configurable Resource records rather than hard-coded branches.

---

## 2. Current implementation status

The following is an architectural maturity snapshot, not a formal completion metric.

| Area | Current state | Assessment |
|---|---|---|
| SO -> PO -> Campaign -> WO lineage | Implemented | Strong |
| MTO + MTS | Implemented | Strong |
| FG + billet/intermediate inventory netting | Implemented | Strong foundation |
| Campaign allocation | Implemented | Needs better optimization |
| Heat formation inside campaign | Implemented | Needs furnace/route-specific heat rules |
| Multi-grade campaigns | Supported through sequence class | Needs proper grade/metallurgy master |
| Caster model | Multiple physical casters, capabilities, strand count | Good foundation |
| Rolling mills | Multiple physical mills + capabilities | Good foundation |
| EAF / LRF / VD | Not properly modeled/scheduled | Major gap |
| CCM sequencing | Implemented; upstream assignment is still heuristic | Needs deeper optimization |
| RM sequencing | CP-SAT-owned fixed-resource sequencing | Stronger after AddCircuit tranche |
| Parallel equipment | Explicitly per physical Resource | Correct |
| Hot/cold/finishing routes | Configurable | Good foundation |
| Grade transition rules | Generic rule model exists | Needs metallurgical hierarchy |
| Plant topology | Plant + stage + resource + flow links | Too shallow |
| Finite scheduling | CP-SAT + calendars + dependencies + AddCircuit | Strong foundation |
| Replanning | Versions + frozen/slushy + actuals | Good foundation |
| Material genealogy | WO -> lot -> downstream lot -> PO/SO | Strong |
| Execution integration | Adapter abstraction exists | Appropriate |
| UI | Planning Sandbox/reference | Not current priority |

Approximate maturity:

```text
Domain / lineage              ~90%
Inventory foundation          ~80%
Plan version / replanning     ~80%
Finite scheduler foundation   ~80%
Campaign formation            ~60%
CCM planning                  ~60%
Rolling planning              ~60%
Plant master model            ~50%
Grade/metallurgy model        ~30%
EAF/LRF/VD planning           ~10%
```

---

## 3. What Plant -> ProcessStage -> Resource should mean

The existing hierarchy is useful, but it needs precise steel-domain semantics.

### Plant

One planning site / steel works.

### Area

Major operating area, for example:

- steelmaking
- continuous casting
- billet yard / intermediate inventory
- rolling
- finishing
- finished-goods yard

### ProcessStage

A manufacturing step or capability class, **not a physical machine**.

Examples:

- EAF melting
- LRF refining
- VD treatment
- continuous casting
- billet reheating
- hot rolling
- cold rolling
- TMT/quench
- cooling
- cutting
- bundling
- coiling

### Resource

One physical independently capacity-constrained equipment instance.

Examples:

```text
EAF-1
EAF-2
LRF-1
VD-1
CCM-1
CCM-2
RHF-1
RM-1
RM-2
```

Every physical Resource has its own calendar, availability, capacity and schedule timeline.

**A ProcessStage must never imply one combined queue across all Resources of that stage.**

This is critical for parallel operation:

```text
CCM-1 timeline  -------------------------->
CCM-2 timeline  -------------------------->

RM-1 timeline   -------------------------->
RM-2 timeline   -------------------------->
```

The same principle applies to multiple EAF, LRF and VD units.

---

## 4. Steel-specific equipment taxonomy

The current broad `ResourceType` values such as Furnace/Refining/Caster/RollingMill are not sufficiently expressive.

Introduce a steel-specific classification such as `ProcessUnitType` or `SteelProcessType`:

```text
EAF
LRF
VD
CCM
ReheatingFurnace
HotRollingMill
ColdRollingMill
TMTWaterBox
CoolingBed
Shear
BundlingLine
Coiler
FinishingLine
MaterialBuffer
```

Keep broad ResourceType if useful for infrastructure/generalization, but the planner should not infer EAF/LRF/VD semantics from a generic category.

Example configured plant:

```text
Steelmaking
  EAF-1
  EAF-2
  ...
  LRF-1
  LRF-2
  ...
  VD-1
  ...

Casting
  CCM-1   4 strands
  CCM-2   4 strands

Rolling
  RHF-1   shared feed resource
  RM-1
  RM-2
```

EAF/LRF/VD counts are master data. The planning code must work for one, two or many units without changes.

---

## 5. Current biggest process gap: SMS is effectively Heat -> CCM

The current production-structure implementation is largely organized around caster planning and rolling planning. Conceptually it behaves close to:

```text
Campaign
  -> Heat
  -> CCM
  -> Billet
  -> RM
```

The target chain is:

```text
Campaign
  -> Heat
  -> EAF operation
  -> LRF operation
  -> optional/required VD operation
  -> CCM operation
  -> strand/billet output
  -> rolling supply path
```

Each heat should carry its own operation route and each operation should expose eligible physical resources.

Example:

```text
Heat H101
  EAF: eligible EAF-1 / EAF-2
  LRF: eligible LRF-1 / LRF-2
  VD: required; eligible VD-1
  CCM: eligible CCM-1 / CCM-2
```

The solver then schedules these as coupled operations with transfer/queue/thermal constraints.

---

## 6. Work Order type vs process-operation type

The execution-facing Work Order classification should stay relatively coarse, but APS needs finer process semantics.

Recommended split:

```text
WorkOrderType
  Steelmaking
  Casting
  HotRolling
  ColdRolling
  Finishing

ProcessOperationType
  EAF
  LRF
  VD
  CCM
  RHF
  RM
  TMT
  Cooling
  Cutting
  Bundling
  Coiling
```

Examples:

```text
WO Type: Steelmaking
Operation: EAF

WO Type: Steelmaking
Operation: LRF

WO Type: Steelmaking
Operation: VD

WO Type: Casting
Operation: CCM
```

APS retains operation-level schedule truth even if external execution groups multiple operations into one WO.

---

## 7. Grade/metallurgy master

The current grade treatment (`GradeCode`, `GradeFamilyCode`, `GradeSequenceClassCode`) is not sufficient for 350+ grades.

Introduce a first-class grade specification.

### Grade master

```text
SteelGrade
  GradeCode
  Description
  GradeFamily
  GradeSequenceClass
  CastingClass
  QualityClass
  ProductFamily compatibility
  Active
```

### Chemistry

```text
GradeChemistryRequirement
  GradeId
  ElementCode
  MinPct
  MaxPct
  TargetPct optional
```

The chemistry structure must support arbitrary alloying elements without schema changes.

### Process requirements

```text
GradeProcessRequirement
  ProcessType
  Requirement = Required / Optional / Forbidden
  CapabilityClass
  Min/Max processing constraints
  Max queue/hold constraints
```

Important examples:

- EAF required
- LRF required
- VD required / optional / forbidden
- CCM casting class requirement
- reheating required / optional / bypass allowed
- hot-charge eligibility
- TMT requirement

Grade master should also carry default superheat/casting-temperature requirements and stage yield/rate profiles where needed.

---

## 8. Do not create a 350 x 350 exact-grade matrix

Transition rules should be hierarchical.

Rule precedence:

```text
Exact grade override
  > Grade sequence-class rule
  > Grade-family rule
  > Default rule
```

Example:

```text
LowCarbon -> LowCarbon       preferred
LowCarbon -> MediumCarbon    acceptable
MediumCarbon -> LowCarbon    expensive
BearingSteel -> LowCarbon    forbidden

G1008 -> G1010               exact override
```

Rules remain directional and can define:

- allowed / forbidden
- transition time
- transition penalty
- mandatory sequence break
- tundish compatibility
- caster-specific override

The same hierarchical approach should exist for section/product transitions.

---

## 9. Material, billet and cross-section model

String codes such as `150X150`, `16MM`, etc. should remain identifiers but not the only source of planning semantics.

Introduce structured cross-section/material specifications:

```text
CrossSection
  Shape
  WidthMm
  HeightMm
  ThicknessMm
  DiameterMm
  SectionFamily
  CasterFormatClass
  RollingFamily
```

Material/product specification should represent:

- SAP material reference
- material stage: liquid steel / billet / intermediate / finished
- product form: billet / bar / rod / section / coil / bundle
- grade
- section/dimensions
- cut length
- theoretical mass-per-length where relevant
- route family
- rolling family
- packaging rules

This enables relationships such as:

```text
150x150 billet
  -> 8 mm TMT
  -> 10 mm TMT
  -> 12 mm TMT
  -> 16 mm TMT
```

without maintaining thousands of exact string combinations.

---

## 10. SAP SO/item/customer special characteristics

An SAP Sales Order is not only material, grade, section, quantity and due date.

Normalize order/customer requirements into an APS planning-requirement snapshot attached to the MTO Production Order.

Support at least:

- customer/customer group
- end-use or quality class
- tighter chemistry limits
- mandatory/forbidden VD
- mandatory/forbidden route or process stage
- allowed/preferred/forbidden physical resources where contractually required
- superheat/casting-temperature overrides
- TMT/mechanical-property process requirement
- dimensional tolerance
- cut length
- target/min/max bundle weight
- bundle composition/segregation policy
- coil weight/split requirement
- marking/packing reference
- inspection/testing requirement
- heat/lot/customer segregation
- whether different SO/customer quantities may share campaign/heat/rolling lot

Constraint precedence:

```text
Customer/SO hard requirement
  narrows
Grade/material default
  narrows
Plant/resource capability
```

Order/customer requirements may make a route stricter but must never silently loosen a hard grade/metallurgy constraint.

This also means two orders with the same nominal grade and final section may still be incompatible for campaign/heat pooling.

---

## 11. Furnace-capacity-driven heat formation

Current heat formation uses global nominal/minimum/maximum heat-size policy. That is acceptable only as an early placeholder.

The target rule is:

> Every planned heat quantity must be feasible for at least one physical steelmaking route/resource combination.

Heat formation must consider:

- eligible EAFs
- EAF nominal tap weight
- EAF min/max feasible heat weight
- ladle capacity where constraining
- grade-specific heat range
- order/customer restrictions
- VD route requirement
- expected steelmaking/casting yield
- CCM/cast-sequence compatibility
- downstream billet requirement
- residual/partial-heat policy

Example:

```text
EAF-1
  nominal 65 MT
  min 62 MT
  max 68 MT

EAF-2
  nominal 70 MT
  min 66 MT
  max 72 MT

Grade X
  feasible 62-68 MT

Grade Y
  feasible 65-70 MT
```

Campaign planning should generate feasible **heat-structure candidates** rather than immediately distributing tonnage around one global nominal value.

The heat should not be permanently bound to one EAF too early if several EAFs can make it. Candidate heat size and compatible furnace set are coupled decisions.

Inventory netting happens before fresh-steel heat formation. If qualified billet supply already covers rolling requirement, no internal heat should be generated merely because an order exists.

---

## 12. Campaign planning must evolve from deterministic fill to optimization

Current campaign formation is deterministic grouping using compatibility partitions and maximum campaign quantity. That is a strong first implementation but not a mature APS campaign optimizer.

Target:

```text
Production Order pool
  -> hard compatibility filtering
  -> campaign candidates
  -> grade-sequence candidates
  -> furnace-feasible heat structures
  -> cast/resource/route alternatives
  -> downstream feasibility evaluation
  -> global selection
```

Compatibility should account for:

- route
- grade family/sequence class/exact overrides
- customer requirements
- chemistry/quality segregation
- VD requirement
- caster format
- product-family rules
- MTO/MTS mixing policy
- inventory/supply source
- downstream capability

Objectives should be tiered:

1. hard feasibility
2. MTO fulfillment / due-date service
3. schedule stability
4. heat utilization / grade transition / cast-sequence efficiency
5. rolling/reheating/hot-charge efficiency
6. MTS target attainment / inventory economics / external billet cost

Campaign remains the planning anchor, and **heat formation remains inside campaign planning**.

---

## 13. Steelmaking route operations: EAF -> LRF -> VD -> CCM

For each heat, derive the required route from grade, customer/order requirements, material route, and active plant topology.

Examples:

```text
EAF -> LRF -> CCM
EAF -> LRF -> VD -> CCM
```

A VD-required grade cannot be scheduled on a route that skips VD.

Each heat operation should carry:

- HeatId
- ProcessOperationType
- eligible physical resources
- quantity
- duration/rate model
- predecessor/successor
- min/max queue/transfer time
- thermal envelope reference
- grade/order requirement snapshot
- solved resource/start/end
- actual resource/start/end when execution occurs

Every physical process unit remains independently schedulable.

---

## 14. Superheat and thermal constraints

Thermal modeling has two distinct domains and they must not be conflated.

### Liquid-steel thermal constraints

Model:

- liquidus/reference temperature when supplied
- target superheat at CCM
- minimum/maximum superheat
- hard casting-temperature window
- preferred target range
- EAF tap temperature range
- LRF exit/target range
- VD exit/target range
- CCM arrival/casting range

Support customer/order overrides that narrow grade defaults.

The planner does not initially need a first-principles thermodynamic model, but it needs auditable thermal-loss/holding assumptions sufficient to reject impossible schedules.

Potential model:

- transfer duration
- nominal temperature loss per minute or piecewise decay profile
- maximum queue/holding time
- transport/ladle class
- heating/temperature-correction capability at eligible stages

Hard thermal violations must remain hard constraints.

### Billet thermal state

Separately model hot billet / cold billet / reheated billet state for hot-charge and reheating decisions.

---

## 15. CCM domain model

Known physical configuration to support:

```text
CCM-1    4 strands
CCM-2    4 strands
```

Each CCM should define:

- strand count
- supported billet/bloom/slab formats
- supported grade/casting classes
- casting speed/rate profile
- min/max grade-sensitive casting speed
- tundish/sequence heat limits
- tundish-life assumptions where available
- startup/sequence-break/setup
- section-change rules
- grade-transition rules
- sequence-break requirements
- yield/crop-loss profile
- maintenance/calendar
- hot-transfer destinations

A cast sequence should explicitly contain:

- physical CCM
- section/format
- tundish/sequence identity
- ordered heat positions
- grade transitions
- expected start/end
- expected output
- sequence-break reason when applicable

Output progression:

```text
Heat
  -> CastSequence
  -> CCM
  -> Strand 1..4
  -> billet quantities
  -> later billet pieces/cut units
```

Initial planning can remain aggregate by strand, but the model must permit billet-piece/cut-pattern refinement later.

---

## 16. Billet supply is a planning source, not only an SMS output

Rolling demand can be satisfied from qualified sources:

```text
Finished goods
Existing billet inventory
External/purchased billet
In-transit billet
Internal actual cast billet
Internal planned cast billet
Manual planner supply
```

Each billet source needs:

- specification / grade / section
- quantity
- available timestamp
- location
- source type
- supplier/source reference
- lot/heat/certificate where available
- quality status
- customer restrictions
- reservation state
- thermal state where relevant

This makes the following contingency normal rather than exceptional:

```text
steelmaking unavailable
external/existing billet available
RHF and RM operational
=> rolling remains schedulable
```

Internal fresh heats are one supply path, not a prerequisite for every rolling requirement.

---

## 17. Shared reheating furnace and hot/cold charge

The model must support a single physical reheating furnace feeding both rolling mills.

Example topology:

```text
CCM-1 -----\
CCM-2 ------+--> billet/hot path --> RHF-1 --> RM-1
External ---+                         \------> RM-2
Inventory --/
```

`RHF-1` is modeled once and owns one capacity timeline shared by both downstream demand streams.

Possible feed paths:

```text
CCM -> direct/hot transfer -> RM
CCM -> yard -> RHF -> RM
Existing billet -> RHF -> RM
External billet -> RHF -> RM
Qualified sufficiently-hot billet -> RM
```

Whether RHF is mandatory is determined in precedence order by:

1. customer/SO hard requirement
2. grade process requirement
3. material thermal state / cold-charge requirement
4. plant operating policy

Examples:

- cold billet normally requires RHF
- some grades may compulsorily require RHF
- hot-charge-eligible material may bypass RHF only if thermal/time constraints are satisfied

RHF capability can include:

- charge/working capacity
- throughput
- residence time
- min/max residence
- grade/material/section compatibility
- discharge temperature
- calendar/downtime

---

## 18. Rolling, TMT, cutting, bundling and coils

Rolling must evolve from a generic RM block to a domain chain capable of representing actual output forms.

Long-product/TMT route:

```text
qualified billet
  -> optional RHF
  -> RM
  -> TMT/quench when required
  -> cooling bed
  -> cutting/shearing
  -> bundling/packing
  -> individual bundle FG lots
```

Coil route:

```text
qualified billet/intermediate
  -> rolling
  -> coiling
  -> individual coil lots
```

RM capability should include:

- input billet formats
- grade/family eligibility
- product/section range
- throughput by grade/product/section
- min/target campaign quantity
- grade/section/product transition rules
- setup/changeover
- RHF/hot-transfer compatibility
- calendar

TMT requirements can include:

- TMT required/optional/forbidden
- TMT capability/profile class
- customer override
- speed/throughput effect where known

### Bar/bundle planning

For TMT/long products model:

- bar diameter/section
- standard/customer cut length
- theoretical piece weight
- expected pieces/bars
- target/min/max bundle weight
- expected bundle count
- remainder handling
- bundle composition/segregation
- mixed-heat/mixed-lot policy
- tagging/marking reference

APS can plan expected bundle counts while execution creates actual bundle IDs/weights.

### Coil planning

Model:

- target/min/max coil weight
- expected coil count
- split policy
- customer-specific coil constraints
- eventual actual individual coil IDs

---

## 19. Time-phased inventory and material balance

Current static netting is useful but must evolve from:

```text
Available(material)
```

to:

```text
Available(material, time)
```

Example:

```text
08:00  opening billet stock      80 MT
11:00  Heat 101 output          +60 MT
12:00  external receipt         +40 MT
13:00  Heat 102 output          +60 MT
14:00  RM consumption           -70 MT
```

Canonical balance:

```text
ProjectedAvailable(t)
 = opening usable inventory
 + confirmed receipts by t
 + planned/released production by t
 + actual production by t
 - reservations/allocations by t
 - planned/released consumption by t
 - safety/reserve constraints
```

Material events should include:

- opening stock
- external receipts
- internal heat/strand production
- quality release/hold/rejection
- reheating state changes where relevant
- RM consumption
- downstream production
- dispatch

Reservations must prevent double-use.

Inventory-decoupling points such as billet yard should be explicit; direct/hot-transfer paths remain tightly coupled.

---

## 20. Rolling resource assignment still needs to move into CP-SAT

The finite scheduler now owns same-resource order through per-Resource AddCircuit sequencing, but upstream production-structure planning still normally selects a particular physical caster/mill before solve.

The target is:

```text
CP-SAT / coupled planning decides:
  eligible resource
  order on that resource
  exact time
```

not only:

```text
heuristic decides RM-1 vs RM-2
CP-SAT decides order/time afterward
```

The next resource-assignment tranche should expose multiple eligible physical RMs, and later EAF/LRF/VD/CCM alternatives, while preserving independent timelines per physical ResourceId.

Caster assignment needs special treatment because changing CCM also changes:

- cast-sequence/tundish membership
- strand/material identity
- billet availability
- transfer links

Therefore CCM assignment and cast-sequence formation must be coupled rather than treated as a trivial independent resource alternative.

---

## 21. Contingency and plant operating states

Partial plant operation is a normal planning condition.

Support:

- steelmaking area unavailable while rolling continues on qualified billets
- one EAF down
- one LRF down
- VD down
- one CCM down
- RHF down
- one RM down
- planned maintenance
- throughput derating
- grade-specific temporary restrictions

Contingency must emerge from explicit alternate routes/material sources, not hidden fallback code.

Example:

```text
steelmaking unavailable
 -> no new internal heats
 -> evaluate existing/external billet
 -> cold billet through RHF if required
 -> schedule available RM
```

If a VD-required grade has no available VD and there is no approved alternate route, it is infeasible/delayed.

---

## 22. Commercial lineage and physical genealogy

Two complementary traces must be preserved.

### Commercial/planning lineage

```text
SAP SO/item
  -> Production Order
  -> Campaign allocation
  -> Heat/route allocation
  -> Work Order allocation
```

This answers **why** material is being produced.

### Physical genealogy

```text
Heat
  -> EAF/LRF/VD operations
  -> CCM / CastSequence / Strand
  -> billet lot/piece
  -> RHF if used
  -> RM
  -> rolled intermediate
  -> TMT / cut / bundle OR coil
  -> individual FG lot
  -> inventory allocation
  -> SO/item/delivery
```

This answers **which physical material** fulfilled an order.

External billet genealogy starts at the external source lot/certificate rather than an internal heat.

Aggregation must never erase lineage. One WO can contain quantities from multiple Production Orders, and one physical output lot can be allocated downstream while preserving its origin.

---

## 23. Solver architecture

Preserve and extend the current CP-SAT foundation:

- optional intervals
- `AddExactlyOne` resource selection
- `NoOverlap` per physical resource
- AddCircuit sequencing per physical ResourceId
- directional sequence-dependent transition time
- adjacency-only transition penalty
- calendars/downtime
- dependencies
- min/max transfer/queue windows
- frozen/slushy/liquid stability
- weighted tardiness and tiered objectives

Non-negotiable invariant:

> Same process/equipment type does not imply one shared schedule. Every physical Resource gets its own capacity and sequencing problem.

Ultimately the solver/candidate layer must consider:

- furnace heat-size feasibility
- grade/customer/process requirements
- EAF/LRF/VD/CCM alternatives
- thermal/superheat feasibility
- cast sequence/tundish compatibility
- billet supply and timing
- RHF/hot/cold charge path
- RM alternatives
- TMT/finishing constraints
- due dates and priorities
- schedule stability

---

## 24. Planning diagnostics

A mature APS must explain why something cannot be planned.

Pre-solve/domain diagnostics should detect:

- grade has no valid route
- VD required but no eligible/available VD
- no EAF supports feasible heat size
- no CCM supports grade/format
- customer restrictions conflict with route/grade
- thermal/superheat window impossible
- billet cannot feed requested RM/product
- mandatory RHF unavailable
- qualified billet not available by required time
- packaging/customer requirement has no feasible route

Solver diagnostics should classify:

- due-date/capacity conflict
- thermal max-wait conflict
- RHF bottleneck
- resource outage
- frozen-plan conflict
- material timing conflict
- forbidden sequence transition

Relaxation suggestions must never silently relax hard metallurgy or customer requirements.

---

## 25. Implementation order

### Foundation

1. Plant/Area/ProcessStage/Resource + steel-specific ProcessUnitType — issue #3
2. Grade/metallurgy/process-requirement master — issue #4
3. Material/cross-section/product-form master — issue #5
4. SAP/customer special-characteristic snapshot — issue #6

### Steelmaking/casting

5. Furnace-capacity-driven heat formation — issue #7
6. Heat-route operations EAF -> LRF -> optional/required VD -> CCM — issue #8
7. Superheat/temperature/thermal-transfer model — issue #9
8. CCM/tundish/strand-output model — issue #10

### Billet/reheating/rolling

9. Internal/external billet supply and steelmaking-down contingency — issue #11
10. Shared RHF + hot/cold charge — issue #12
11. RM/TMT/cooling/cutting/bundling/coiling — issue #13
12. Time-phased material balance — issue #14

### Optimization

13. Candidate campaign/grade/heat optimization — issue #15
14. Coupled resource assignment across EAF/LRF/VD/CCM/RHF/RM — issue #16

### Operations

15. Operating-state scenarios/outages/contingencies — issue #17
16. Full-route release/execution/genealogy — issue #18
17. Steel-domain diagnostics and relaxation guidance — issue #19

---

## 26. Architectural invariants

1. Campaign remains the planning anchor.
2. Heat formation remains part of campaign planning.
3. No equipment count is hard-coded in business logic.
4. No global single heat size assumption.
5. No global single steel route assumption.
6. EAF/LRF/VD/CCM/RHF/RM and other steel steps are explicit domain concepts.
7. Every physical Resource has its own independent schedule timeline.
8. Two 4-strand CCMs and two RMs can operate concurrently.
9. Customer/SAP special requirements can narrow grade/route/resource/quality/packaging eligibility.
10. Hard metallurgy/quality/customer constraints remain hard.
11. Inventory, WIP and external supply are first-class planning inputs.
12. Steelmaking can be unavailable while rolling remains feasible from qualified billet supply.
13. Shared RHF capacity is modeled once across both RM feed streams.
14. Cold-charge/hot-charge/reheating decisions are explicit.
15. Commercial lineage and physical genealogy are both preserved.
16. Individual bundles/coils can be traced even if APS plans at a higher aggregate grain.
17. Replanning fixes history/actuals and optimizes only remaining work.
18. No silent heuristic fallback may be presented as a valid finite schedule.

---

## 27. Immediate next tranche

The next code tranche should implement the **Plant + Metallurgy Foundation** before adding more UI or secondary API work:

```text
Plant
  -> Area
  -> ProcessStage
  -> Physical Resource

Steel ProcessUnitType
  -> EAF
  -> LRF
  -> VD
  -> CCM
  -> Reheating Furnace
  -> RM
  -> downstream steel equipment

Grade Master
  -> families
  -> sequence/casting classes
  -> chemistry
  -> VD / reheating / TMT requirements
  -> superheat defaults
  -> resource/route capability classes

Material Master
  -> billet/intermediate/final product
  -> structured cross sections
  -> bar/coil/bundle characteristics

Order Requirement Snapshot
  -> SAP/customer special characteristics
```

Only after that foundation is correct should the current campaign/heat route be rewritten around EAF/LRF/VD/CCM operations and furnace-feasible heat-size candidates.

This is the transition point from a generic manufacturing scheduler with steel terminology to a robust steel-domain APS.