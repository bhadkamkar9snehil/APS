# APS Testing Strategy

**Status:** governing test strategy  
**Scope:** canonical .NET APS backend, persistence, planner workbench and Windows desktop-hosted UI  
**Re-baselined:** 23-Aug-2026 against `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`  
**Primary acceptance anchors:** #44, #31, #61

Implementation-state detail: [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

---

## 1. Purpose

APS correctness is a chain, not a raw test count:

```text
demand
 -> material/BOM
 -> campaigns/heats
 -> configured routes
 -> finite resource/material/thermal schedule
 -> Plan Version
 -> readiness/approval/release
 -> execution actuals/material state
 -> replan
 -> read model
 -> planner UI
```

Testing must protect the lowest useful rule and also prove important cross-layer workflows.

Goals:

1. prevent steel-planning rule regressions;
2. prevent persisted Plan Version/readback drift;
3. detect relational/provider defects that EF InMemory cannot prove;
4. protect release/approval lifecycle boundaries;
5. protect the Gantt as the central planner interaction surface;
6. keep failures deterministic and localizable;
7. prove realistic integrated behavior without replacing focused tests with one giant fixture;
8. make the Windows verification gate solution-driven rather than hand-maintained.

---

## 2. Governing principles

### Test behavior, not implementation trivia

Prefer assertions on business invariants, public contracts, persisted facts, rendered behavior and user-observable interactions.

Good examples:

- material shortfall never silently shrinks manufacturing demand;
- future internal supply can satisfy a later material need;
- same-type physical resources remain independent timelines;
- delayed billet thermal state can force configured reheating;
- actual measured billet state overrides a stale planned estimate on replan;
- atomic bulk moves are validated against their **final proposed schedule**, not each member's old baseline placement;
- frozen/running/completed operations cannot be moved;
- historical capacity readback is invariant to later live master/calendar edits;
- only an active Approved Plan Version can be released;
- release readiness is based on persisted Plan Version evidence;
- direct release-payload replay cannot bypass the repository lifecycle boundary.

Static source-text tests are appropriate only when the file itself is the product contract, such as dependency boundaries, generated/build wiring or forbidden repository references.

### Lowest useful layer first

Protect the root cause at the lowest layer that expresses it. Add a second integration/workflow test when a defect crosses layers.

### Do not lock known-wrong behavior

Do not add green characterization tests for a known bug simply to increase coverage. Fix the defect with the regression or keep the gap explicitly tracked.

### Deterministic time and identity

Use fixed UTC epochs and stable IDs/business keys where ordering or exact values matter. Avoid uncontrolled wall-clock dependencies in tests.

### Provider fidelity matters

EF Core InMemory does not prove unique constraints, foreign keys, transactions, SQL translation, concurrency or migration/schema behavior. Use SQLite relational tests for fast provider semantics and production-provider tests where provider-specific behavior matters.

### One realistic reference plant, many focused fixtures

#61 will own the persisted realistic reference dataset. It complements focused fixtures; it does not replace them.

---

## 3. Test layers and current projects

| Layer | Project / mechanism | Ownership |
|---|---|---|
| Repository architecture | `APS.Architecture.Tests` | project dependency graph, test registration, build/release wiring, repository boundaries |
| Domain/application/planning | `APS.Planning.Tests` | orchestration, BOM/material logic, campaign/route/resource/thermal semantics, solver behavior, Plan Version lifecycle/service behavior where relational storage is not the subject |
| Infrastructure/persistence | `APS.Infrastructure.Tests` | EF/SQLite/provider semantics, persistence/readback, query counts, transactions/concurrency, repository/service integration |
| Rendered UI/component | `APS.UI.Tests` + bUnit/state/model tests | component lifecycle/DOM contracts, Gantt models, selection, keyboard/accessibility, source contracts where browser execution is not required |
| Real browser/desktop workflow | #31 harness + Windows live QA | JS pointer geometry, fullscreen/localStorage/browser layout, long-open-session behavior, visual regression, end-to-end planner flows |
| Persisted integrated acceptance | #61 reference plant + #44 scenarios | canonical SQL-backed lifecycle at realistic density |

### Current registered test suite

Latest recorded Windows verification for `71e456d...` executed **336/336 tests**:

- `APS.Architecture.Tests`: **9**
- `APS.Infrastructure.Tests`: **12**
- `APS.Planning.Tests`: **182**
- `APS.UI.Tests`: **133**

Counts are evidence for that exact SHA, not a permanent target. Behavior coverage matters more than preserving the number.

---

## 4. Solution registration rule

Every executable test project under `tests/` must:

1. set `<IsTestProject>true</IsTestProject>`;
2. be registered in `APS.slnx`;
3. run under `build/verify.ps1`;
4. have clear layer ownership;
5. avoid higher-layer references unless intentionally testing integration.

The Windows verifier discovers test projects from `APS.slnx`; adding a test project to the solution automatically brings it into the gate.

---

## 5. Authoritative Windows verification gate

The old statement “do not use CI” is obsolete.

APS uses the shared self-hosted Windows Azure DevOps agent `EOS` as its authoritative automated build/test environment. The repository-owned [`../build/verify.ps1`](../build/verify.ps1) contract performs:

1. `dotnet restore APS.slnx`;
2. full Release `dotnet build APS.slnx --no-restore`;
3. every solution-registered `tests/*` project with TRX output;
4. self-contained `win-x64` publish of `APS.DesktopHost`.

See [`windows-ci.md`](windows-ci.md).

### What is not authoritative

- GitHub Actions/hosted CI is not the APS verification authority;
- a local compile alone is not a release-quality proof;
- a previous SHA's Windows result does not make a newer SHA green;
- static review is not a substitute for the Windows gate.

### Release packaging

`build/release.ps1` remains an explicit packaging path. Real release preparation must not bypass the complete test gate. `-SkipTests` is an inner-loop convenience only, never production release evidence.

---

## 6. Latest recorded integrated verification

For `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`, the recorded Windows evidence reports:

- Release build: **0 warnings, 0 errors**;
- tests: **336/336 passed**;
- self-contained Windows `APS.DesktopHost.exe` publish produced;
- SQLite `PRAGMA quick_check`: `ok`;
- pre-launch database backup created;
- live published desktop loaded the released baseline;
- **105 operations / 8 resources** rendered;
- Gantt, operation inspector, resource-load and capacity views exercised;
- released-baseline editing correctly blocked;
- final desktop process remained open and responsive.

This result belongs only to that exact baseline and must be replaced by new evidence after later code changes.

---

## 7. Current high-value regression coverage

### Release lifecycle/readiness

Current tests cover the new approval boundary, including:

- Feasible is not directly releasable;
- active Plan Version required for approval/release;
- unresolved persisted material findings block approval;
- non-firm or late external incoming evidence blocks readiness where applicable;
- valid planned internal manufacture/future supply is not rejected merely because stock is absent now;
- release repository rejects bypass/replay attempts;
- persisted MTO service readiness detects missing allocation, incomplete scheduled evidence and late completion.

This is stronger than the pre-consolidation Feasible-only release model.

### Atomic Gantt/workbench move validation

Current planning regressions cover final-state atomic move semantics:

- moving A into B's old slot while B moves away is not falsely blocked;
- selected operations overlapping in the **proposed** target schedule are blocked;
- moved predecessor/successor geometry is evaluated using proposed positions;
- a proposed precedence violation is detected;
- collision with non-selected/frozen work remains a blocker;
- query-count behavior is protected against per-move N+1 regressions.

### Time fence and pointer cancellation

Focused tests protect:

- authoritative proposal/request time-fence policy consistency;
- frozen-horizon move behavior;
- pointer-cancel/window-blur rollback of drag/pan/split state;
- no accidental .NET commit callback during cancellation;
- cleanup of proposal ghost/feedback/cursor/highlights/autoscroll state.

### Historical capacity/readback

Tests protect persisted Plan Version interpretation from live-master drift:

- historical resource scheduling assumptions are used where snapshotted;
- calendar/resource capacity facts do not change when live resource/calendar masters later change;
- cumulative capacity and compounded derating match solver semantics;
- compatibility fallback is explicit for older snapshots lacking the persisted assumption.

### EF/query hardening

Current infrastructure/planning tests include query-count and relational behavior protections for read/workbench and demand-reconciliation paths, including the fixed-query-count expectation across small versus larger move/input sets.

### Billet thermal behavior

#56 completion coverage includes:

- known-hot bypass where permitted;
- internally produced billet inside the hot window;
- delay/thermal aging forcing configured RHF where available;
- RHF-unavailable downstream blocking without erasing valid upstream billet production;
- conservative unknown/yard state;
- actual measured temperature/state precedence on replan;
- order-level thermal narrowing;
- historical Plan Version readback of decision basis.

### Route generality

#58 coverage protects configured downstream route behavior without the first-`HotRoll` architectural pivot, including billet-only/direct-hot and configured multi-step downstream chains.

---

## 8. #44 manufacturing acceptance matrix — current status

“Strong” means the core rule has strong focused executable coverage. It does **not** mean #44's complete cross-layer proof is done.

| Scenario | Invariant | Current state |
|---|---|---|
| A — fully FG-covered SO | stock coverage avoids unnecessary new manufacture | Partial integrated proof; focused demand coverage exists |
| B — partial FG coverage | only uncovered quantity becomes MTO manufacturing need | Strong focused coverage |
| C — billet inventory covers rolling | downstream plan without unnecessary SMS | Partial integrated proof |
| D — billet absent but manufacturable | upstream internal billet requirement/supply | Partial integrated timing proof |
| E — future internal billet | later supply satisfies later RM need without duplication | Strong rule foundation; replan/readback acceptance still important |
| F — deep BOM shortfall | leaf shortage explicit, no demand shrink | Strong focused coverage |
| G — SMS down, billet known | downstream contingency through qualified supply | Open integrated proof under #57 |
| H — SMS down, no billet | attributable shortfall, no fabricated supply | Open integrated proof under #57 |
| I — rare alternate LRF | eligible rare alternate survives/selects correctly | Strong foundation; #16 completes generic lifecycle |
| J — CCM flexibility | LRF-ready heat can use another valid CCM | Solver-owned CCM slice exists; #16 completes generic dispatch lifecycle |
| K — parallel resources | independent physical timelines | Strong focused coverage |
| L — cumulative shared RHF | overlap within configured capacity | Strong scheduling foundation; integrated readback remains relevant |
| M — mixed PO service dates | aggregation preserves independent service truth | Partial; due-date model still needs final allocation-grain refinement |
| N — partial actual production | actual + remaining future supply no double count | Partial; #18 integrated closure |
| O — downstream genealogy | physical and commercial lineage separately traversable | Open under #18 |
| P — month-long horizon | progressive future supply allowed | Strong time-phased rule foundation; #61 integrated density proof pending |
| Q — infeasible explanation | named domain cause + restoration evidence | Partial under #19 |
| R — scenario/CTP consistency | shared canonical rules | Open/partial under #43/#42 |
| S — billet thermal aging/actual replan | delay can force RHF; actual overrides estimate | **Strong focused coverage; #56 closed** |
| T — downstream route generality | route truth, no first-HotRoll pivot | **Strong focused/readback coverage; #58 closed** |

No row becomes “complete end to end” from one unit test. #44 still requires the applicable canonical chain to be demonstrated.

---

## 9. Gantt/workbench coverage model

The Gantt is the central operational workbench and requires four forms of evidence.

### Pure model/state tests

Protect:

- viewport/zoom/pan/fit arithmetic;
- clipping and virtualization;
- resource hierarchy/sorting/collapse;
- baseline classification;
- dependency geometry;
- capacity math;
- operation content/density decisions;
- multi-selection and proposed atomic move geometry;
- time-fence policy;
- historical capacity assumptions.

### Rendered component contracts

Use bUnit/state/component tests for:

- operation editing protection;
- semantic/accessibility attributes;
- keyboard navigation/context behavior;
- resource/operation selection;
- analysis dock and inspector contracts;
- toolbar/command disabled/enabled semantics;
- release/readiness presentation where applicable.

Post-Ponytail component consolidation means tests should assert current **behavior**, not require the old standalone Gantt layer filenames to exist.

### Browser/desktop interaction

#31 still owns a systematic browser/visual harness for things unit/bUnit tests cannot prove well:

- pointer drag and continuous feedback;
- edge autoscroll;
- actual browser focus/virtualization interaction;
- fullscreen/localStorage behavior;
- context-menu viewport placement;
- synchronized overlays while scroll/zoom changes;
- long-open-session Now/execution marker progression;
- deterministic 1080p/1440p/4K visual regression;
- realistic-density responsiveness.

Current main has already received live Windows desktop QA for the integrated baseline; that is valuable evidence, but it does not replace a repeatable #31 browser/visual harness.

### End-to-end planner workflows

Required long-term workflows include:

- calculate -> inspect exceptions -> compare -> approve -> release;
- move/bulk move -> preview -> validate -> apply as child Plan Version;
- alternate-resource redispatch while preserving heat/PO/material identity;
- execution update -> recovery/replan -> protect actual/running/committed work;
- material-shortage drilldown with future supply retained;
- CTP/scenario consistency;
- physical genealogy plus commercial traceability;
- master validation -> correction -> replan.

---

## 10. Persistence and query test priorities

`APS.Infrastructure.Tests` and integration tests should progressively prove:

### Schema/model

- unique business keys;
- FK/cascade behavior;
- indexes/alternate identities;
- migrations/upgrades where used;
- relational transactions/concurrency.

### Plan Version

- parent/child lineage;
- released immutability;
- Approval/Release lifecycle state;
- route decisions, including skips/reasons;
- eligible versus planned resource evidence;
- material requirements/reservations/coverage;
- thermal and capacity assumptions required for historical interpretation;
- service-readiness evidence;
- actual facts without rewriting historical planned facts.

### Query performance

Guard against regressions such as:

- N+1 validation loops;
- one query per moved operation;
- one query per demand/coverage row;
- sibling collection cartesian amplification;
- loading full entities when projections suffice.

Query-count tests should assert the shape that matters: increasing workload size should not linearly increase database round trips when the path is designed to batch/preload.

---

## 11. Test data policy

- use fixed UTC epochs;
- use stable readable business keys;
- use stable GUIDs where identity participates in ordering/persistence;
- prefer narrow builders over giant opaque fixtures;
- keep the future #61 reference dataset deterministic and reproducible from an empty database;
- record actual operation/campaign/material counts and elapsed time instead of inventing arbitrary performance counts.

---

## 12. Definition of a meaningful green

A change is meaningfully green when:

- the lowest useful regression protects the changed rule;
- cross-layer behavior is tested when the defect crosses layers;
- relational semantics use a relational provider when required;
- relevant Gantt interaction is covered at the appropriate model/component/browser level;
- every test project is registered in `APS.slnx`;
- the exact commit passes the authoritative EOS Windows `build/verify.ps1` contract before being claimed verified;
- release/runtime evidence is recorded separately when the change affects startup, migration, real data or desktop interaction.

Raw line coverage and raw test count are secondary metrics, not the APS acceptance standard.
