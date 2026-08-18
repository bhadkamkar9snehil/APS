# APS Backend Visibility and Control Contract

Status: **canonical backend-to-UI exposure contract**

Scope: backend groundwork only. This document does not specify visual design. It defines **everything the backend must make inspectable, queryable and controllable** so the future UI never has to reverse-engineer planner state.

Principle:

> If APS computes, selects, rejects, reserves, commits, releases, executes or diagnoses something meaningful, that fact must have an intentional read path. If the planner is allowed to change it, that lever must have an intentional command/master contract.

---

# 1. Global plan context

## Read information

- current authoritative Plan Version
- Plan Version number/ID
- parent/baseline Plan Version
- scenario ID/name
- planning horizon start/end
- planning reason / trigger
- created by / created at
- solver status
- plan lifecycle state
- release state/time
- superseded state
- feasibility/trust/degraded-mode indicators
- objective score and objective component breakdown
- plan warnings
- plan diagnostics count by severity/domain
- master-data version/effective snapshot references
- planning assumptions/fallback flags

## Controls/levers

- calculate plan
- replan from current execution/inventory
- create child scenario
- compare plans
- accept/review plan
- release plan
- supersede plan
- cancel pending run
- set frozen/slushy/liquid policy
- select baseline for replan

---

# 2. Demand / Sales Orders / Production Orders

## Read tables/views

### Sales Orders
- SO number
- item number
- customer
- material
- grade
- section/product
- ordered quantity
- open quantity
- due date
- priority/service class
- order status
- MTO/MTS classification where relevant
- special requirement reference
- coverage status
- lateness/risk status

### Production Orders
- PO number
- demand source: MTO/MTS
- source SO/item(s)
- material
- grade/family/sequence class
- caster section
- final section
- route
- planned quantity
- remaining quantity
- due date
- priority
- status
- target stock / projected stock for MTS
- stock-policy reference

### Demand allocations
- SO -> PO quantity allocation
- PO -> Campaign allocation
- PO -> Heat allocation
- PO -> Work Order allocation
- PO -> material reservation/supply allocation

## Read diagnostics

- why PO is uncovered
- why PO is split
- why PO was not grouped with another PO
- customer requirement conflict
- material shortfall/late supply
- route/resource infeasibility

## Controls/levers

- planner priority override where policy permits
- due-date/service scenario override
- MTS min/target/max policy maintenance
- draft split/merge/grouping preference
- hard lock/pegging where permitted
- customer requirement correction via master/order integration workflow

---

# 3. Customer/SAP requirement snapshot

## Read information

- customer/customer group
- SO/item
- requirement source/version
- grade/material default requirement
- customer override
- effective resolved requirement
- chemistry min/max/target by element
- VD required/optional/forbidden
- LRF/secondary metallurgy requirement
- RHF required/bypass policy
- TMT/process requirement
- allowed/preferred/forbidden route
- allowed/preferred/forbidden resources
- superheat/casting-temperature envelope
- cut length
- bundle/coil target/min/max
- segregation/mixing rule
- inspection/testing requirement
- marking/packing reference

## Explainability

For every effective value expose:

- value
- source: grade / material / customer / SO / policy
- whether hard/soft
- overridden value if narrowed

---

# 4. BOM and material-requirement graph

## BOM master read views

- BOM code
- version
- status
- effective from/to
- output material/specification
- plant/site selector
- route selector
- grade/family selector
- product-family selector
- component material
- flow type: INPUT / BYPRODUCT / COPRODUCT / WASTE
- quantity per output
- UOM
- yield
- scrap/loss
- source/location/quality restriction
- precedence/effective-selection basis

## Exploded Plan Version requirement tree

For every requirement node:

- requirement ID
- parent requirement ID
- root SO/PO
- full path
- BOM level derived, never authoritative master input
- material/specification
- UOM
- gross requirement
- covered quantity
- net requirement
- required-at time
- timing basis
- location
- quality/customer qualification
- selected BOM/version
- quantity-per/output
- yield/scrap assumption
- byproduct/co-product output
- planning status

## Required material statuses

- AvailableNow
- PlannedAvailable
- SupplyActionRequired
- Shortfall
- LateSupply
- Unsourced

## Controls/levers

- BOM CRUD/versioning
- effective-date activation
- planner source override where allowed
- approve/reject manual supply assumption
- choose approved sourcing alternative
- re-run material explosion

---

# 5. Inventory and supply

## Inventory table

- material/spec
- grade
- section/product form
- lot ID when known
- quantity
- reserved quantity
- available quantity
- projected available quantity
- UOM
- location
- stage: FG / CastIntermediate / OtherIntermediate / RawMaterial
- quality state
- available-from time
- heat/source/certificate reference
- customer restriction

## Supply table

Supply source types:

- ExistingInventory
- InternalCastActual
- InternalCastCommitted
- InternalCastPlanned
- ExternalFirm
- ExternalPlannedBuy
- TransferPlanned
- ManualPlanned
- InTransit

Expose:

- source ID/reference
- material/spec
- quantity
- reserved quantity
- expected receipt
- location
- supplier/source location
- quality/certificate state
- thermal state
- commitment state
- Plan Version ownership

## Supply requirement/actions table

- requirement ID
- PO/root-demand ID
- material/spec
- need quantity
- need time
- selected action: MAKE/BUY/TRANSFER/MANUAL/UNSOURCED
- selected planned quantity
- MOQ/order multiple
- projected excess
- expected receipt
- lead time
- source/supplier/location
- service feasibility
- penalty/preference
- selected reason

## Sourcing alternatives table

For every alternative:

- action type
- source rule
- allowed/rejected
- rejection reason
- required lead time
- expected availability
- quantity/lot size
- cost/preference penalty
- route feasibility
- material/quality feasibility

---

# 6. Material reservations and time-phased ledger

## Reservation table

- reservation ID
- Plan Version
- requirement ID
- PO/Campaign/operation
- supply source
- material/spec
- quantity
- UOM
- reserved-at
- available-at
- status
- lot-level identity where available

## Time-phased material events

- pool/material key
- event time
- event type
- source/consumer
- receipt quantity
- consumption quantity
- running projected balance
- requirement/reservation ID
- source type
- confidence/commitment state

## Required views

- ledger by material
- ledger by PO
- ledger by campaign
- ledger by rolling plan
- supply-to-demand pegging
- shortages by need time
- future committed production
- projected excess
- zero-balance/material-risk windows

---

# 7. Campaign planning

## Campaign table

- campaign ID/number
- status
- route
- caster section
- required date/window
- total rolling quantity
- existing/intermediate supplied quantity
- fresh steel quantity
- external/planned supplied quantity
- MTO quantity
- MTS quantity
- customer/segregation class
- grade/sequence composition
- heat count
- campaign objective score

## Campaign allocations

- campaign -> PO
- quantity
- intermediate quantity
- fresh steel quantity
- supply-source composition

## Candidate campaigns

For selected and rejected alternatives expose:

- candidate ID
- PO membership
- quantity
- grade sequence
- proposed heat structure
- service score
- transition score
- heat-utilization score
- setup/campaign-count score
- MTS deviation score
- downstream feasibility score
- stability score
- selected/rejected
- rejection reason

## Planner levers

- max/min campaign quantity policy
- MTO/MTS mixing policy
- grade-mixing policy
- customer segregation
- objective weights
- proposed manual grouping/split constraint
- freeze specific campaign membership

---

# 8. Grade sequence and transitions

## Grade sequence view

- campaign
- sequence position
- grade
- grade family
- sequence class
- casting class
- quantity
- heat count

## Transition evaluation

For every adjacent pair:

- from grade/section/product family
- to grade/section/product family
- effective rule source
- allowed/forbidden
- transition time
- penalty
- sequence-break requirement
- resource scope
- exact/class/family/default precedence source

## Controls/master levers

- transition rules
- grade-family/class membership
- resource-specific override
- section/product-family transition rule

---

# 9. Heat structure and heat allocation

## Heat table

- heat ID
- campaign
- sequence number
- grade
- furnace input quantity
- expected usable cast output
- min/nominal/max feasible envelope
- capacity basis/resource class
- required process route
- customer/quality requirement summary
- planned/actual state

## Heat -> PO allocation

- heat
- PO
- output quantity
- steelmaking input quantity
- customer segregation

## Heat sizing explanation

- eligible furnace/capacity classes
- selected heat count
- target utilization
- residual/partial-heat reason
- yield assumption
- rejected heat structures/reasons

---

# 10. Plant topology and resource master

## Plant hierarchy

- Plant
- Area
- Process Stage
- Physical Resource

## Resource fields

- ResourceId/code/name
- ProcessUnitType
- ResourceType
- active/state
- scheduling mode
- capacity basis/value
- min/nominal/max heat size where relevant
- throughput/rate
- strand count
- location
- operating mode
- derating
- preferred/forbidden flags

## Resource capability table

- resource
- process operation
- route
- grade/family/class
- input section/family
- output section/family
- product family
- capability class
- throughput
- min/max quantity
- temperature capability
- preferred/penalty

## Calendar/outage table

- resource
- interval
- availability state
- maintenance/breakdown
- derating
- reason/source

## Plant flow links

- from resource/stage
- to resource/stage
- transfer time
- min/max queue
- hot-transfer flag
- buffer/decoupling semantics
- temperature-loss model/reference
- enabled/disabled

---

# 11. Manufacturing routes

## Route view

- route code/version
- material/product applicability
- grade applicability
- plant
- active state

## Route operations

- sequence
- ProcessOperationType
- required/optional/forbidden condition
- input material/section
- output material/section
- yield
- queue/transfer bounds
- decoupling point
- finite-scheduled flag
- release WO mapping type

## Route-resource capability

- operation
- eligible resource
- capability basis
- duration/rate
- preference
- exclusions

## Controls

- route CRUD/version/effective date
- optional-operation condition
- finite-scheduling scope
- resource qualification

---

# 12. Resource assignment / late-binding dispatch

## For every scheduled operation expose

- PlanningKey
- source entity
- operation type
- eligible-resource alternatives
- planned resource
- selected penalty
- commitment state
- committed resource
- actual resource
- commitment policy
- commitment trigger
- dispatch acknowledgement state
- off-plan deviation flag

## Resource alternative table

- operation
- resource
- eligible true/false
- eligibility basis
- exclusion reason
- duration
- preference penalty
- pair-flow feasibility
- thermal feasibility
- calendar availability at plan time

## Dispatch revision history

- revision ID
- old resource
- new resource
- reason
- requested by/source
- revalidation result
- child Plan Version
- timestamp

## Controls

- request local redispatch
- acknowledge/commit assignment
- force broad replan
- override resource only when policy/permissions allow

---

# 13. Steelmaking operation train

For each heat:

- primary steelmaking operation: EAF/BOF/IF/etc.
- LF/LRF/secondary metallurgy
- VD/RH/AOD/VOD or configured treatment
- CCM

For each operation expose:

- planned resource
- resource alternatives
- planned start/end
- actual start/end
- planned/actual quantity
- predecessor/successor
- min/max queue
- transfer time
- thermal requirement
- commitment/execution status
- delay variance
- resource variance

---

# 14. Thermal / superheat

## Master data

- grade liquidus/reference temperature
- target/min/max superheat
- casting-temperature range
- operation entry range
- operation exit range
- resource heating/correction capability
- transfer/holding loss model
- billet thermal-state classes
- RHF entry/discharge temperature
- RM minimum feed temperature

## Plan facts

For each constrained transition expose:

- estimated upstream exit temp/range
- transfer duration
- heat-loss assumption
- predicted downstream arrival temp/range
- hard minimum/maximum
- preferred range
- max feasible wait
- risk margin
- correction/reheat requirement
- assumption source

---

# 15. CCM / casting / strand output

## CCM master

- strand count
- formats
- casting speed/range
- grade/casting-class eligibility
- tundish/sequence capacity/life
- sequence-break setup
- section-transition rules
- crop/yield

## Cast sequence

- logical sequence ID
- selected physical CCM
- eligible CCMs
- tundish/sequence identity
- ordered heats
- grade/section transitions
- planned start/end
- break reason

## Strand output

- heat
- CCM
- strand number
- output quantity
- billet format
- expected pieces/cut length when modeled
- expected receipt time

---

# 16. RHF / rolling / downstream production

## Rolling plan

- rolling-plan ID
- PO/Campaign allocations
- billet source composition
- grade
- input section
- output section
- quantity
- selected RM / alternatives
- planned start/end

## Feed mode

- direct hot charge
- internal billet inventory
- committed future billet
- external billet
- planned purchase/transfer
- RHF required/bypassed

## RHF

- selected/shared resource
- scheduling mode
- charge/occupancy quantity
- residence time
- discharge temperature
- downstream RM

## Downstream route

- TMT
- cooling
- cutting
- bundling
- coiling
- finishing

For every route operation expose:

- upstream plan
- input/output material
- input/output section
- quantity
- selected/eligible resource
- queue window
- inventory-decoupling flag
- planned/actual timing

---

# 17. Packaging / planned physical units

## Planned bundle/coil outputs

- PO
- material/product
- total planned quantity
- target/min/max unit weight
- cut length
- piece weight
- expected pieces
- expected bundle/coil count
- remainder handling
- customer packaging rule
- mixing/segregation rule

## Actual material units

- actual lot/unit ID
- bundle/coil number
- actual weight
- piece count
- quality state
- parent lot(s)
- producing operation/WO
- SO/PO allocation

---

# 18. Finite schedule

## Operation schedule table

- task ID
- PlanningKey
- operation type
- source entity
- physical resource
- start/end
- duration
- setup/changeover
- priority
- due date
- tardiness
- assignment penalty
- time-fence state
- commitment state

## Dependency table

- predecessor
- successor
- min lag
- max lag
- resource-pair restriction
- material dependency
- thermal/queue basis

## Per-resource sequence

- physical resource
- sequence position
- predecessor/successor
- transition time/penalty
- calendar blocks

## Capacity semantics

- resource scheduling mode
- cumulative demand if applicable
- capacity limit
- scheduled occupancy

---

# 19. Capacity and bottlenecks

## Rough-cut capacity

- physical resource
- available hours/capacity
- demand hours
- process/setup/changeover
- utilization
- overload
- basis = rough-cut

## Finite scheduled occupancy

- physical resource
- scheduled intervals
- available calendar
- utilization/occupancy
- idle/starved intervals
- overload impossible by solver but capacity bottleneck evidence
- basis = finite schedule

Never combine rough-cut and finite occupancy into one unlabeled KPI.

---

# 20. Plan Version compare

## Operation deltas

- added
- removed
- moved start/end
- resource changed
- commitment changed
- quantity changed

## Campaign/heat deltas

- campaign membership
- allocation quantity
- grade sequence
- heat count/size
- source path

## Material deltas

- reservation changed
- source changed
- MAKE/BUY/TRANSFER changed
- expected receipt changed
- shortage/late status changed

## Service deltas

- PO lateness
- due-date risk
- uncovered demand

## Explain reason

- execution actual
- outage
- supply delay
- campaign optimization
- resource redispatch
- master/scenario change

---

# 21. Scenario planning

## Scenario master

- scenario ID/name
- baseline plan
- resource overrides
- outage intervals
- derating
- capability/quality restriction
- material/inventory/supply override
- sourcing policy override

## Scenario results

- resulting Plan Version
- feasibility
- service delta
- campaign delta
- material delta
- resource/capacity delta
- external supply delta

---

# 22. CTP / promise

## Request

- customer/material/grade/section
- quantity
- requested date
- location
- special requirements

## Alternatives

- stock-only
- join existing campaign
- new campaign
- later achievable date
- split delivery
- approved expedite/source option

## Promise basis

- material source
- campaign/heat
- resource/capacity
- frozen-plan impact
- inventory trust
- solver status
- blocker/reason

CTP must use the same canonical planning kernel, not a hidden independent planner.

---

# 23. Work Orders / release

## Work Order table

- WO number
- type
- campaign
- material
- grade
- section
- planned quantity
- resource when WO grain maps to one resource
- planned start/end
- status
- external execution ID

## WO allocations

- WO -> PO
- quantity
- PO -> SO/item

## WO process operations

- PlanningKey
- ProcessOperationType
- planned resource
- actual resource
- planned/actual start/end
- planned/actual quantity
- status

---

# 24. Execution history

## Operation events

- event ID/source
- operation/PlanningKey
- prior/new status
- actual resource
- actual start/end
- actual quantity
- reason/comment
- source: Manual / MES API / reconciliation
- received time
- idempotency key

## Heat/casting actuals

- heat
- EAF/LRF/VD/CCM actuals
- temperature data when available
- strand output
- produced billet lots

## Replan impact

- completed/fixed
- running/fixed resource/start
- held
- remaining quantity
- affected downstream operations

---

# 25. Material genealogy / traceability

## Commercial lineage

```text
SO/item -> PO -> Campaign allocation -> Heat/WO allocation
```

## Physical lineage

```text
Heat
 -> CastSequence
 -> Strand
 -> Billet lot/piece
 -> RHF/RM consumption
 -> Rolled lot
 -> TMT/cut/bundle or coil
 -> FG lot
 -> inventory allocation
 -> SO/delivery
```

## Required queries

- upstream ancestors recursively
- downstream descendants recursively
- material transformation quantity/yield
- external-source ancestry
- quality/certificate state
- commercial demand allocation

---

# 26. Diagnostics / explainability

## Diagnostic table

- issue code
- severity
- hard/soft
- domain category
- message
- affected entity type/ID
- Plan Version
- evidence/reference
- suggested action
- advisory/non-authoritative flag

## Categories

- MasterData
- Demand
- Campaign
- Heat
- Route
- Resource
- Sequence
- Thermal
- Material
- Capacity
- FrozenPlan
- Execution
- Integration

## Explainability views

- Why was this campaign formed?
- Why was this heat size selected?
- Why was this resource selected?
- Why was this alternative rejected?
- Why is this material late/short?
- Why is this PO infeasible?
- What minimum safe change could restore feasibility?

---

# 27. Master-data catalogs and levers

Every master must support list/detail/effective-value/validation/impact read contracts.

Catalogs:

- Plants
- Areas
- Process stages
- Resources
- Resource capabilities
- Resource calendars
- Resource scheduling mode/capacity
- Flow links
- Manufacturing routes
- Route operations
- Route-resource capabilities
- Grades
- Grade families
- sequence classes
- casting classes
- chemistry requirements
- process requirements
- customer/order requirement profiles
- CrossSections
- MaterialSpecifications
- PackagingSpecifications
- TransitionRules
- ThermalProfiles
- AssignmentCommitmentPolicies
- SourcingRules
- BOM headers/versions/components
- StockPolicies
- Scenarios/overrides

---

# 28. Backend operational logs

Operational logs are separate from plan facts.

Query/operational support should be able to correlate:

- TraceId
- RequestId
- PlanningRunId
- PlanVersionId
- ScenarioId
- ProductionOrderId
- CampaignId
- HeatId
- PlanningKey
- ResourceId
- WorkOrderId
- MaterialRequirementId
- MaterialLotId
- ExternalEventId

Standard application logging remains `ILogger<T>` with Serilog host configuration.

---

# 29. Required command inventory

The backend command surface should intentionally include:

## Planning
- calculate
- replan
- scenario run
- compare
- release

## Resource/dispatch
- request redispatch/local repair
- acknowledge assignment
- commit assignment
- broad replan request

## Material
- approve manual supply
- choose/override source where policy permits
- update external ETA/confirmation
- release/cancel reservation

## Execution
- update WO
- update process operation
- update heat/casting actual
- record material transformation/output

## Masters
- CRUD/version/activate/deactivate with validation and impact checks

## Planning policy
- freeze/slushy/liquid horizon
- campaign policy
- stock policy
- source policy
- objective weights
- assignment commitment policy

No command may mutate a solver decision without revalidation where feasibility is affected.

---

# 30. Visibility completion criterion

A backend capability is UI-ready only when:

- its authoritative data source is known;
- its IDs are stable;
- it has a typed read model;
- it has filtering/drill-through references;
- its hard/soft/reason metadata is available;
- its supported lever has a typed command/master contract;
- historical Plan Version meaning is preserved;
- no UI-side planning calculation is required;
- no core screen needs to deserialize opaque JSON;
- no meaningful backend fact is accessible only through database inspection.
