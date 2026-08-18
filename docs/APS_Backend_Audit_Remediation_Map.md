# APS Backend Audit — Remediation Map

Status: **canonical issue/dependency map**

Parent audit epic: **#37**

This document maps every major backend finding from `APS_Backend_Acceptance_Audit_2026-08-18.md` to an implementation issue. It exists to prevent audit findings from being lost in conversation or hidden inside a broad epic.

---

## 1. Issue map

| Finding | Issue(s) | Completion evidence required |
|---|---|---|
| Full recursive BOM missing from canonical .NET | #33 | recursive .NET BOM, inventory/supply netting at each node, Plan Version tree, read API |
| Time-phased material / future supply incomplete | #14, #32, #33 | one ledger; committed/planned/actual receipts; no double-use; required-at timing |
| MAKE/BUY/TRANSFER currently too billet-centric | #14, #32, #33 | recursive sourcing at every BOM node; selected/rejected alternatives |
| Campaign packing remains sort-and-fill dependent | #15 | candidate generation/optimization; order invariance; decision score/reasons |
| Resource alternatives not fully late-bound through operations | #16, #32 | Eligible -> Planned -> Committed -> Actual for every operation; local repair |
| Hard-coded EAF/LRF/VD route assumptions | #34 | route-driven arbitrary configured steelmaking operation chain |
| Universal NoOverlap is over-conservative | #35 | Disjunctive/Cumulative resource modes and solver/report support |
| Thermal/superheat not end-to-end | #9 | resource-pair thermal feasibility, hot-charge/RHF decision, Plan Version assumptions |
| Actual physical genealogy not end-to-end | #18 | recursive actual material transformation chain through bundle/coil |
| Diagnostics not planner-grade | #19 | named infeasibility causes, hard/soft, targeted advisory relaxation |
| Backend facts/levers not all queryable | #36 | complete read/command API inventory; relational plan facts where needed |
| Duplicate/legacy/demo production paths | #38 | authoritative call graph, explicit demo mode, dead/duplicate cleanup |
| Master data incompletely wired | #39 | Domain->SQL->Provider->Planner->Snapshot->Read matrix complete |
| Logging not standardized/wired | #40 | ILogger<T> + Serilog structured correlation + console/file support |
| FluentValidation referenced but inconsistent | #41 | validators registered/used or dependency removed; stable validation errors |
| Transition/capability semantics can diverge | #42 | one effective-rule resolver; all dimensions wired or removed |
| CTP/scenario/capacity can diverge from .NET kernel | #43 | same canonical planner/masters; rough-cut vs finite explicitly separated |

---

## 2. Dependencies

### Foundation/canonicalization

```text
#38 canonical pipeline
  -> #39 master wiring
  -> #41 validation
  -> #40 observability
  -> #42 effective rules
```

These issues should be addressed early because they reduce the chance of implementing the same domain rule several times.

### Material-planning chain

```text
#33 recursive BOM
     +
#14 time-phased material
     +
#32 operational supply/committed-future supply
     ↓
full recursive MAKE/BUY/TRANSFER planning
```

Important: implement this as **one material engine**, not a BOM engine plus a separate campaign-inventory engine.

### Production-route / solver chain

```text
#34 generic route projection
  -> #16 late-binding resources
  -> #35 scheduling modes
  -> #9 thermal constraints
```

Some work can proceed in parallel, but the final solver must consume one route/capability/thermal representation.

### Planning-quality chain

```text
#42 effective rules
#14/#33 material truth
#34/#16 route/resource truth
      ↓
#15 campaign candidate optimization
      ↓
#19 diagnostics/explainability
```

### Execution chain

```text
#16 resource commitment/actual
#14 material actuals
      ↓
#18 physical genealogy/replan execution truth
```

### Decision/read chain

```text
all authoritative planning facts
     ↓
#36 backend visibility
     ↓
#43 CTP/scenario/capacity convergence
```

---

## 3. Definition of an issue being actually complete

Do not close a backend issue simply because:

- an enum was added;
- a domain entity exists;
- a DbSet exists;
- a planner method exists;
- a test/demo can construct the object;
- a JSON field contains the information.

For each issue, explicitly verify the applicable chain:

```text
Model
SQL
Provider
Request
Planner
Solver
PlanVersion
Execution/Replan
ReadModel
API
Diagnostics
```

Mark non-applicable stages as N/A with a reason.

---

## 4. Production-mode rule

Authoritative planning must not silently use demo assumptions.

Examples that require explicit opt-in if retained:

- default heat size;
- default machine lists;
- default route;
- guessed cycle times;
- simple/legacy structure builder;
- non-authoritative heuristic fallback.

If such a fallback is used intentionally, the Plan Version and diagnostics must say so.

---

## 5. Material-planning completion tests

The material issues are not complete until the canonical .NET path can prove all of the following:

1. finished demand can explode to billet;
2. billet can explode to liquid steel/charge;
3. hot-metal route can explode to BF burden;
4. burden can explode to ore/coal/other leaf inputs;
5. intermediate inventory stops explosion for covered quantity;
6. partial inventory explodes only the uncovered remainder;
7. future committed production covers a later requirement;
8. future planned MAKE covers a later requirement;
9. approved BUY/TRANSFER creates a future receipt;
10. late receipt produces `LateSupply` rather than silently moving service status;
11. no approved source produces `Unsourced`;
12. BOM and campaign logic cannot double-net the same material;
13. material requirement can exist without finite scheduling the upstream producing plant.

---

## 6. Resource-flexibility completion tests

The resource issues are not complete until:

- EAF alternatives survive plan;
- a rare alternate LRF survives plan;
- VD/RH alternatives survive plan;
- CCM alternatives survive plan;
- RHF/RM alternatives survive plan;
- every alternative has an eligibility/exclusion basis;
- planned resource differs from commitment state;
- local redispatch reuses the same hard constraints;
- actual off-plan resource can be recorded without rewriting history;
- same-type machines remain independent timelines;
- a cumulative resource can overlap work within capacity without a fake sequence.

---

## 7. Domain-flexibility completion examples

The final route/material model must be able to express without code branching:

- EAF -> LF/LRF -> CCM;
- EAF -> LF/LRF -> VD -> CCM;
- EAF -> LF/LRF -> RH -> CCM;
- BOF -> LF/LRF -> CCM;
- induction furnace -> secondary metallurgy -> CCM;
- purchased billet -> RHF -> RM;
- internal hot billet -> direct RM;
- CCM -> direct rolling where configured;
- bar/TMT route ending in cut/bundle;
- rod/coil route ending in coil;
- shared RHF feeding multiple RMs;
- two independent CCMs and two independent RMs;
- optional upstream BOM through BF/hot metal without necessarily finite-scheduling BF.

---

## 8. Visibility completion

No backend issue should be considered product-complete if its meaningful decisions cannot eventually be inspected.

Use `APS_Backend_Visibility_Contract.md` to verify that each implemented capability has:

- read model;
- stable IDs;
- reason/evidence;
- Plan Version history where applicable;
- command/master lever if user-controllable;
- API/application access.

---

## 9. Verification process rule

Do **not** use GitHub Actions or CI for this project.

Verification will be performed later in the intended development environment using direct build/test/runtime verification. Documentation/issue completion should record what was verified and where, without treating GitHub Actions as an acceptance mechanism.
