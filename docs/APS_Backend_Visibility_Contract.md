# APS Backend Visibility and Control Contract

**Status:** canonical backend-to-UI exposure contract  
**Owner/completeness gate:** #36  
**Re-baselined:** 23-Aug-2026 against current `main`

This document defines what the backend must make intentionally inspectable/queryable/controllable so the production UI never has to reverse-engineer planning truth.

It is a **target completeness contract**, not a claim that every field below is already implemented. Current implementation state is in [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

Principle:

> If APS computes, selects, rejects, reserves, commits, approves, releases, executes or diagnoses a meaningful fact, that fact needs an intentional typed read path. If a planner may change it, the lever needs an intentional validated command/master contract.

---

# 1. Global Plan Version context

## Read

- Plan Version ID/number;
- parent/baseline Plan Version;
- scenario ID/name;
- planning horizon/reference time;
- trigger/reason;
- created time/actor where available;
- solver status/objective;
- lifecycle status;
- active/superseded state;
- Approved/released state/time;
- release readiness;
- readiness findings by stable code;
- warnings/diagnostics summary;
- effective planning-assumption/snapshot basis;
- compatibility fallback flags for older Plan Versions.

## Commands

- calculate;
- replan/child Plan Version;
- compare;
- get persisted readiness;
- approve;
- release;
- scenario run where supported;
- frozen/time-fence/repair controls through validated planning commands.

The UI must not invent a second lifecycle state machine. Current persisted lifecycle includes:

```text
Draft -> Feasible -> Approved -> Released
```

with Failed/Superseded where applicable.

---

# 2. Demand / Sales Orders / Production Orders

## Sales Order/customer-demand read

- SO/item;
- customer;
- material/product;
- grade/specification;
- final section/product form;
- ordered/open quantity;
- customer required date;
- confirmed date where authoritative;
- priority/service class;
- special requirement/profile reference;
- qualified FG coverage;
- manufacturing requirement;
- projected service/completion state.

## Production Order read

- PO identity;
- MTO/MTS source;
- source SO/item;
- material/grade/section/route;
- planned/remaining quantity;
- FG allocated quantity;
- required date and its semantic basis;
- priority/status;
- Campaign/heat/rolling/route/WO allocations;
- expected completion/service status;
- release-readiness contribution.

## Required explainability

- why demand is fully/partially/uncovered;
- how qualified FG coverage reduced manufacturing need;
- why a PO exists or was reconciled;
- why demand was split/segregated;
- which persisted production allocations serve the PO;
- why service is late/missing/incomplete.

### Date-model note

The current production path still uses a generic `RequiredDate` in important places. The target read model should explicitly distinguish customer-required, confirmed and production-required-by semantics where the source/model supports them. UI must not guess this distinction.

---

# 3. Customer/grade/order requirement resolution

Expose effective requirements and their sources:

- grade/material defaults;
- customer/order narrowing;
- chemistry/process requirements;
- required/optional/forbidden treatment steps;
- route/resource restrictions;
- thermal envelopes;
- segregation/mixing rules;
- packaging/inspection requirements;
- hard versus soft;
- exact master/rule source;
- overridden/inherited value where useful.

Do not make UI infer precedence from raw master rows.

---

# 4. BOM/material requirement graph

For each persisted/planned requirement expose:

- requirement ID / parent/root;
- SO/PO root lineage;
- material/specification/UOM;
- gross/net/covered/shortfall quantity;
- required-at time and timing basis;
- selected BOM/version/path;
- yield/loss assumptions;
- internally manufacturable state;
- supplying upstream requirement/operation when internal;
- planning status;
- explanation/diagnostic source.

Statuses may include current domain values such as:

- AvailableNow;
- PlannedAvailable;
- SupplyActionRequired where the lower-level planner emits it;
- Shortfall;
- LateSupply;
- Unsourced;
- NotManufacturableHere;
- CycleBlocked.

### Production-scope rule

Current production APS is manufacturing-only. The read surface must **not** present speculative BUY/TRANSFER/MANUAL actions as production-authoritative recommendations while `PlanningLifecycleService` rejects those controls.

If domain compatibility/demo types still contain those action values, UI must distinguish them from production authority.

---

# 5. Inventory and authoritative supply

## Inventory read

- material/spec/grade/section;
- lot/piece identity where known;
- quantity/reserved/available/projected available;
- UOM;
- location/stage;
- quality state;
- available-from time;
- source/heat/certificate;
- customer qualification/restriction;
- thermal state where available.

## Supply read

Production-authoritative supply can include:

- existing qualified inventory;
- actual internal output;
- committed/released internal future output;
- APS-planned internal output;
- authoritative external/in-transit incoming material already known to the integration/state model.

Expose:

- source type/reference;
- material/spec/quantity;
- availability/receipt time;
- location/source;
- commitment/confidence state;
- quality/certificate state;
- thermal state;
- Plan Version/execution ownership where applicable.

Do not fabricate a supplier/PO/transfer recommendation merely because a material requirement is uncovered.

---

# 6. Material reservations and time-phased ledger

## Reservation

- reservation ID;
- Plan Version;
- requirement/root demand;
- PO/Campaign/operation;
- supply source;
- material/spec;
- quantity/UOM;
- reserved/available time;
- status;
- lot-level identity where known.

## Time-phased event

- material pool key;
- event time;
- receipt/consumption type;
- source/consumer;
- quantity;
- projected balance;
- requirement/reservation;
- source type/commitment.

Required views:

- material ledger;
- PO/Campaign/operation coverage;
- supply-to-demand pegging;
- shortage-by-need-time;
- committed/planned internal future supply;
- zero-balance/risk windows.

Current stock must never become the implicit end of the planning horizon.

---

# 7. Campaign / grade sequence / heat structure

## Campaign read

- Campaign identity/status;
- route/section;
- quantity composition;
- MTO/MTS composition;
- PO allocation quantities;
- grade sequence;
- heat structure/count;
- earliest/latest allocation service dates;
- selected candidate/objective evidence where persisted;
- transition/service/downstream/stability score components where available.

## Candidate/rejection visibility

Expose selected and rejected alternatives as #19/#36 mature:

- membership;
- hard incompatibility;
- transition reason;
- furnace/heat feasibility;
- downstream feasibility;
- customer segregation;
- service impact;
- stability impact;
- selected/rejected reason.

Manual UI changes must become validated planning constraints/replans rather than direct entity mutation.

---

# 8. Routes, operations and physical resources

## Route

- route identity/version;
- ordered operations;
- optional/skipped decision and reason;
- input/output material/section;
- yield;
- queue/transfer bounds;
- inventory/decoupling semantics;
- release WO mapping.

## Resource

- physical ResourceId/code/name;
- process/unit type;
- operating state;
- scheduling mode;
- capacity basis/nominal capacity/factor;
- calendar/unavailable/derated intervals;
- capabilities;
- flow links.

No read/API should collapse same-type resources into a single pseudo-resource.

---

# 9. Late-bound resource assignment / commitment / redispatch

#16 remains the current primary backend completion owner.

For every finite operation the target read contract is:

- operation/planning key;
- eligible physical resources;
- excluded candidates and reason;
- duration/throughput/preference basis per candidate;
- planned resource;
- commitment state/policy;
- committed resource;
- actual resource;
- off-plan actual flag;
- redispatch/local-repair revision history;
- child Plan Version/reason;
- revalidation/impact evidence.

Target lifecycle:

```text
Eligible Resources
 -> Planned Resource
 -> Commitment State
 -> Committed Resource
 -> Actual Resource
```

UI must not infer eligibility from equipment type or mutate `ResourceId` directly.

---

# 10. Thermal visibility

## Liquid steel

Expose effective thermal constraints and resource-pair/queue evidence required by the configured liquid-steel route.

## Billet thermal — #56 implemented

For each billet/downstream feed decision expose, where persisted/readable:

- source thermal basis: planned/actual/categorical/unknown;
- source exit/available state;
- rolling-entry minimum/target requirement;
- transfer/wait/buffer duration;
- loss/holding rule;
- predicted temperature/state;
- actual measured temperature/state when authoritative;
- hot-direct/hot-buffered/reheat-required outcome;
- reason direct hot charge was rejected;
- whether reheating came from thermal state or independent route/order policy;
- rejected hot paths/warnings.

Do not present #56 as a future-only concept.

---

# 11. Finite schedule / Gantt contract

Expose each scheduled operation with:

- stable PlanningKey;
- source entity/route operation;
- physical resource;
- start/end/duration;
- eligible resources;
- commitment/execution state;
- predecessor/successor dependencies;
- min/max lags where applicable;
- calendar/frozen/time-fence state;
- baseline delta;
- Campaign/heat/PO/material lineage;
- binding/slack evidence where authoritative;
- diagnostics/warnings.

Resource/capacity read models must distinguish:

- disjunctive versus cumulative scheduling mode;
- nominal/effective capacity;
- calendar/operating-state derating;
- solved occupancy;
- historical persisted assumption versus current live master.

UI geometry is not planner truth.

---

# 12. Plan Version baseline/comparison

Current operation comparison foundation exists. Target complete comparison includes:

- added/removed/moved/resource-changed operations;
- service changes;
- Campaign/heat composition;
- material requirements/reservations/shortfalls;
- capacity/occupancy differences;
- scenario/effective assumption changes;
- diagnostic changes;
- attribution to scenario versus changed demand/master input where determinable.

#57 owns the broader persisted comparison expansion; do not build a separate UI-only comparer.

---

# 13. Release readiness / approval / release

Expose:

- Plan Version current lifecycle status;
- IsActive;
- IsReleaseReady;
- stable readiness findings with entity references;
- material evidence missing/unresolved states;
- supply evidence missing/non-firm/late states where relevant;
- MTO service completion missing/incomplete/late findings;
- approval result/time if stored;
- released state/time;
- Work Orders/operations created from release.

Commands:

- GetReadiness;
- Approve;
- Release.

The UI must not parse exception prose to decide whether a plan is releasable.

---

# 14. Execution / Work Orders / actuals

Expose:

- released Work Order and operation mapping;
- PO/SO allocations;
- planned resource/time/quantity;
- committed resource/state;
- actual resource/start/end/quantity;
- status history/provenance;
- source-system event/idempotency identity;
- variance;
- produced/consumed material facts where implemented.

Running/completed actuals are physical truth and must not rewrite historical planned assignment.

#18 remains the completion owner for full downstream transformation/genealogy.

---

# 15. Commercial lineage and physical genealogy

Keep them separate but traversable.

### Commercial

```text
SO/item -> PO -> Campaign/Heat/WO/operation allocation
```

### Physical

```text
source material/heat -> cast/strand -> billet -> downstream transformation -> FG unit
```

Target read API must support forward/backward recursive traversal with quantity/provenance where meaningful, including externally sourced billet with no internal heat parent.

---

# 16. Diagnostics / explainability

#19 target model should expose:

- stable code/category;
- severity;
- hard/soft;
- source stage/service;
- affected entity references;
- evidence values;
- consequence;
- advisory restoration/minimum-relaxation guidance;
- objective/penalty component evidence for feasible plans where available.

UI must not infer domain cause from generic `Infeasible` or parse prose.

---

# 17. Scenario / CTP / capacity

## Scenario

Expose scenario identity/overrides, resulting Plan Version and comparison against baseline from canonical persisted facts.

## CTP

Expose request/result using the same canonical demand/material/route/resource/thermal rules as normal planning. Include promise basis, earliest date and blocker/evidence.

## Capacity

Keep separate:

- rough-cut estimate;
- finite scheduled occupancy.

#43/#57 remain backend owners for convergence/completeness.

---

# 18. Master data / effective values

Typed list/detail/effective-value/validation reads are required for planning-affecting masters, including:

- Plant/Area/Stage/Resource;
- resource scheduling/capacity;
- capabilities/calendars/flow links;
- routes/route operations/resource capabilities;
- grade/chemistry/process/thermal requirements;
- material/section/packaging;
- transition/effective rules;
- BOM;
- scenarios/overrides;
- planning commitment/resource-assignment policy as introduced.

#39 owns wiring completeness; #60 owns validated operational authoring for the newer planning-critical masters; #41 owns application-boundary validation conventions.

UI must not use direct `DbContext` access to compensate for missing commands.

---

# 19. UI command-safety contract

Every state-changing command must be:

- explicit;
- validated;
- auditable;
- tied to a Plan Version/current entity identity;
- concurrency/stale-state aware where applicable;
- incapable of silently weakening hard physical/customer/metallurgy constraints.

Examples:

- calculate/replan;
- move/bulk-move proposal apply;
- resource commitment/redispatch;
- approve/release;
- execution update;
- master-data save/retire;
- scenario run;
- CTP request.

No production command exists for speculative procurement/transfer recommendation under the current manufacturing-only product boundary.

---

# 20. Persistence visibility rule

Core planner facts needed for filtering, drilldown, comparison and historical interpretation should be relational snapshots/typed projections where practical.

JSON may remain immutable backup/detail, but no core planner screen should require:

- direct SQL inspection;
- deserializing opaque internal blobs in UI;
- joining live mutable masters to reinterpret a historical decision when the effective assumption should have been snapshotted;
- re-running planner logic just to explain an existing Plan Version.

Recent historical-capacity hardening is the model: persisted scheduling/calendar assumptions are used where available and compatibility fallback is explicit for older Plan Versions.

---

# 21. Current completion state versus target

Already materially exposed/implemented on `main`:

- current/recent Plan Version and planner workbench context;
- demand/supply/Campaign/steelmaking/rolling/schedule workspaces;
- finite Gantt/read-model facts;
- Plan Version comparison foundation;
- Work Orders/execution/read foundations;
- material/inventory views;
- Plan Version readiness/approval/release application lifecycle;
- historical capacity assumptions;
- substantial master-data UI/read foundations.

Still incomplete as a **visibility-completeness program**:

- full #16 alternatives/commitment/redispatch/exclusion evidence;
- #18 complete physical transformation/genealogy;
- #19 normalized diagnostics/restoration evidence;
- #57 rich service/material/Campaign/capacity/diagnostic compare;
- #43 CTP/scenario/capacity convergence;
- #60 validated operational master authoring;
- all remaining facts required by the production UI without opaque JSON or UI-side reconstruction.

Therefore #36 remains open even though the production UI already exists.

---

# 22. Completion criteria for #36

Close only when:

- every meaningful backend planning/execution fact has an intentional typed read contract or is explicitly documented internal-only;
- every planner-controlled lever has a validated command/master contract;
- current and historical Plan Versions can be rendered/explained without mutable-master drift;
- UI does not recompute material balance, resource eligibility, route decisions, thermal outcomes, readiness or diagnostics;
- no core screen must deserialize opaque JSON or inspect SQL directly;
- CTP/scenario/capacity/compare/execution consume the same canonical truth rather than parallel view-specific logic;
- the contract inventory is updated with concrete application/API ownership.
