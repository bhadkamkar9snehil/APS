Here is the detailed plan of action.

I am treating the uploaded `aps_planner.py` and the Claude history as the source of truth for this review. The history shows a claimed end state with multiple commits, API endpoints, UI screens, scheduler integration, and tests, but the actual uploaded `aps_planner.py` still reflects a very early-stage planner with a simplistic heuristic scheduler rather than a real CP-SAT implementation.  

# Executive view

## What is already right

The architectural direction is now broadly correct:

* Sales Orders as demand source
* Planning Orders as manufacturable lots
* Heat Batches as upstream batch objects
* Scheduled Operations as machine-level execution units

That layering is clearly present in `aps_planner.py` and is aligned with the later clarified APS approach in the Claude history.  

## What is still incomplete or wrong

The implementation is still only a **skeleton**, not a production-ready APS:

* lot formation is simplistic
* heat estimation is mathematically wrong in one key place
* finite scheduling is **not CP-SAT** despite being described that way
* there is no real sequence optimization
* no setup/changeover matrix is modeled
* no resource alternatives are modeled
* no precedence graph beyond implied SMS→RM timing
* no persistence/state model is defined
* the release flow is only lightly acknowledged in the history
* the history claims comprehensive implementation and tests, but the uploaded code does not substantiate that level of completion on the planner side.  

So the right next move is **not another conceptual rewrite**. It is a structured hardening of the current model.

---

# Recommended plan of action

## Phase 1 — Fix correctness issues in the current planner first

These are immediate blockers.

### 1. Fix heat calculation logic

Your `_estimate_heats()` uses `round(total_mt / heat_size)`. That is wrong for planning because 51 MT with 50 MT heat size can become 1 heat after rounding, which underestimates required capacity. It must be **ceiling**, not rounding. The Claude history itself said heat calculation should use ceiling division.  

### Action

Replace:

* `round(total_mt / heat_size)`

With:

* `math.ceil(total_mt / heat_size)`

### Why

APS must never under-plan heats. Underestimating heats breaks both feasibility and scheduling quality.

---

### 2. Fix heat quantity distribution

In `derive_heat_batches()`, quantity is spread evenly by:

* `qty_per_heat = po.total_qty_mt / heats_needed`

This gives fractional and overly neat heat loads that may not reflect practical fill logic. 

### Action

Change heat derivation to:

* fill heats up to nominal heat size
* keep the final heat as remainder
* optionally enforce minimum fill threshold if business rules require it

### Better pattern

For 130 MT at 50 MT heat size:

* 50
* 50
* 30

Not:

* 43.33
* 43.33
* 43.33

### Why

The current equal-split logic is computationally tidy but operationally weak.

---

### 3. Stop calling the current scheduler “CP-SAT”

The method `simulate_finite_schedule()` is not a CP-SAT scheduler. It is currently a heuristic load check:

* SMS hours = count × duration
* RM hours = sum × multiplier
* duration = `max(total_sms_hours, total_rm_hours)`

That is not finite scheduling with discrete decisions. 

### Action

Rename the current method immediately to something like:

* `estimate_schedule_load()`
  or
* `simulate_capacity_load()`

Then create a future real method:

* `solve_finite_schedule_cpsat()`

### Why

The naming is misleading and will contaminate all later UI/API semantics.

---

### 4. Add explicit precedence modeling

Right now the code assumes SMS and RM with aggregate hour math, but it does not construct actual operation-level precedence objects. `ScheduledOperation` exists, but the current scheduler does not populate a real operation graph.  

### Action

For each heat, explicitly create:

* SMS operation
* RM operation

With rule:

* RM start >= SMS end

### Why

Without this, the schedule is not a schedule. It is only a load summary.

---

## Phase 2 — Improve lot formation logic

This is the most important planning intelligence layer.

### 5. Make lot formation multi-criteria instead of only grade-first greedy grouping

Current lot formation is:

* group by grade
* sort by due date
* greedily accumulate until max lot / max heats exceeded

That is a decent starting heuristic, but it is too weak for real APS behavior. 

### Action

Add scoring-based lot proposal using weighted compatibility:

* grade compatibility
* due-date distance
* section/size compatibility
* route compatibility
* urgency penalty if mixed into slower groups
* lot size balance
* heat utilization efficiency

### Suggested structure

Add helper functions:

* `score_so_pairing(so_a, so_b, rules)`
* `score_so_for_lot(so, current_lot, rules)`
* `propose_lots_by_score(window_sos, rules)`

### Why

The Claude history explicitly recognized that “how to form lots” is the hard part and the key planning intelligence.  

---

### 6. Add urgent-order protection as real logic, not just a comment

The planner comments say urgent orders should not be delayed, but the current grouping logic does not truly isolate or prioritize them. It only groups by grade and due-date order. 

### Action

Implement urgent handling rules such as:

* isolate orders due within urgent window into separate lots when needed
* allow lot splits that prioritize urgent tonnage
* prevent urgent SOs from being trapped behind large same-grade non-urgent lots

### Why

This is central to the original critique of the campaign model and remains unresolved in the current planner.

---

### 7. Add size/section compatibility as an actual constraint

The code stores `section_mm` and `size_family`, but grouping logic does not materially use size compatibility beyond display aggregation. 

### Action

Add configurable rules:

* same-section only
* section range tolerance
* incompatible section families cannot share lot
* optional customer/route-driven splitting

### Why

Right now size is represented but not operationalized.

---

## Phase 3 — Build a real finite scheduler

This is the biggest engineering step.

### 8. Implement real operation objects before solving

Before CP-SAT, generate a normalized operation model.

### Action

Create something like:

* `OperationNode`
* `ResourceRequirement`
* `PrecedenceLink`

For each heat:

* SMS op on eligible SMS resources
* RM op on eligible RM resources
* optional downstream ops later

### Why

You need an internal scheduling model that is richer than `HeatBatch`.

---

### 9. Implement actual CP-SAT model

The Claude history repeatedly describes CP-SAT as the intended engine, but the current uploaded code does not implement it.  

### Action

Create `solve_finite_schedule_cpsat()` using OR-Tools:

* interval variables for each operation
* `AddNoOverlap` for each resource
* precedence constraints for SMS→RM
* optional alternative resources
* due-date lateness variables
* setup/changeover penalty variables

### Initial objective

Minimize weighted sum of:

* late orders
* total lateness
* grade transitions
* makespan
* schedule instability if frozen jobs exist

### Why

This is the real optimizer. Everything until then is only heuristic preprocessing.

---

### 10. Add sequence-dependent changeover logic

The Claude history emphasizes that grade changeovers matter and should be modeled in the optimizer, not as giant campaign blocks. That is correct, but the current code does not implement any transition cost logic. 

### Action

Add:

* compatibility matrix by grade family
* setup time matrix between consecutive heats
* forbidden transitions if needed
* optional high penalty instead of hard forbid

### Why

Without this, the scheduler will overestimate freedom and produce unrealistic sequences.

---

### 11. Support multiple resources properly

The current scheduler accepts `sms_resources` and `rm_resources` parameters, but the calculation still effectively assumes single sequential SMS and aggregate RM load. 

### Action

Model:

* alternative SMS resources
* alternative RM resources
* assignment decision variables
* per-resource no-overlap constraints

### Why

If you have more than one machine, the current scheduler is materially inaccurate.

---

## Phase 4 — Make the APIs and workflow robust

The history claims 7 APS endpoints and a full workflow. Treat that as workflow intent, not proof of maturity.  

### 12. Separate “proposal” endpoints from “execution” endpoints

Right now planning state appears to be shared through function attributes in the API layer, based on the history. That is fragile. 

### Action

Define distinct endpoint categories:

#### Read/proposal

* order pool
* select window
* propose lots
* derive heats
* simulate schedule

#### Planner modifications

* split lot
* merge lots
* freeze lot
* exclude order
* reprioritize objective weights

#### Release

* approve plan
* release to execution

### Why

Planner interaction must be explicit, not hidden in server memory.

---

### 13. Add explicit planning session state

Do not rely on transient function attributes or ad hoc in-memory state.

### Action

Create a planning session object/table:

* `planning_session_id`
* selected horizon
* candidate SO ids
* proposed planning orders
* derived heats
* simulation results
* frozen elements
* planner edits
* status

### Why

Without this, the UI workflow will be brittle and non-auditable.

---

### 14. Define release semantics clearly

The history says release exists, but it reads more like acknowledgement than real handoff. 

### Action

Clarify what release creates:

* production order records?
* heat instructions?
* planned schedule version?
* MES dispatch queue entries?

### Why

Release is not a button label. It is a business transaction.

---

## Phase 5 — UI improvements

The history claims 5 UI screens. Whether they exist or not, here is what they must support functionally. 

### 15. Order Pool

Must support:

* due-date filters
* priority filters
* grade filters
* route/size filters
* overdue/next horizon presets
* manual include/exclude

### 16. Planning Board

Must support:

* view proposed Planning Orders
* merge lots
* split lots
* freeze lots
* see why orders were grouped
* edit objective weights

### 17. Heat Builder

Must support:

* see heat fill per lot
* adjust heat split manually if needed
* view underfilled heats
* view compatibility issues

### 18. Finite Scheduler

Must support:

* actual machine sequence
* SMS and RM timelines
* late vs on-time indicators
* changeover markers
* resource load utilization
* frozen vs movable work

### 19. Release Board

Must support:

* approve/reject plan version
* compare scenarios
* release selected plan
* show release payload summary

---

## Phase 6 — Testing and validation

### 20. Add deterministic unit tests for planner math

Tests needed for:

* due-window selection
* heat estimation
* heat derivation
* lot grouping rules
* urgent order splitting
* compatibility scoring

The history claims comprehensive tests, but the planner code still needs very specific mathematical and rule tests around its current weak points. 

---

### 21. Add schedule feasibility regression tests

Create test scenarios like:

* single SMS, single RM, 3 heats
* 2 SMS, 1 RM, mixed due dates
* urgent order with incompatible grade
* impossible horizon
* frozen first heat, remaining heats rescheduled

### Why

You need confidence that model changes do not reintroduce “campaign-style artificial infeasibility.”

---

### 22. Add explainability outputs

Every proposal and schedule should explain itself.

### Action

Return rationale fields such as:

* why these SOs were grouped
* why this SO was excluded
* why this lot was split
* why order is late
* which constraint caused infeasibility

### Why

APS adoption fails when planners cannot understand why the system proposed something.

---

# Specific edits to make in `aps_planner.py`

## Immediate code edits

1. Import `math`
2. Replace `round()` with `math.ceil()` in `_estimate_heats()` 
3. Replace equal-split heat distribution with fill-to-capacity remainder logic 
4. Rename `simulate_finite_schedule()` or replace it with a real solver-backed implementation 
5. Add actual operation generation from heat batches
6. Add compatibility and changeover rule structures
7. Add scoring-based lot proposal helpers
8. Add planner override hooks: split/merge/freeze
9. Add plan/session persistence model abstraction

---

# Suggested implementation order

## Priority 1 — correctness

* fix heat estimation
* fix heat quantity split
* stop mislabeling heuristic load check as CP-SAT
* create operation-level model

## Priority 2 — planning quality

* scoring-based lot formation
* urgent-order logic
* size/route compatibility
* planner override support

## Priority 3 — scheduling quality

* true CP-SAT model
* multiple resources
* changeover matrices
* lateness objective
* freeze/stability rules

## Priority 4 — workflow hardening

* planning session persistence
* proper release transaction
* explainability outputs
* robust tests

---

# What I would not do now

Do **not** do another wholesale conceptual rename like switching everything back to “order pack” or reviving campaign language. The current architecture is close enough. The real issue now is implementation depth, not naming. The history already moved from campaign cleanup to the SO→PO→Heat→Schedule model, and that direction is sound.  

Do **not** keep using claimed performance numbers or “industry standard” claims as design validation. The code must validate itself through tests and scenario runs. The history explicitly contains overconfident claims from earlier reasoning.  

---

# Bottom line

Your APS is now on the **right architectural backbone**, but it is still at the **prototype/planning-skeleton** stage.

The most important fixes are:

* correct heat math
* real lot-formation intelligence
* real finite scheduling
* real changeover handling
* real planning session state
* real release semantics

Everything else is secondary.
