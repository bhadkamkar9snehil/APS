# APS Testing Strategy

**Status:** Governing test strategy  
**Scope:** Canonical .NET APS backend, persistence, planner workbench, and desktop-hosted UI  
**Primary acceptance anchors:** #44, #31, #61

## 1. Purpose

APS is not adequately protected by a large count of isolated planning tests. The system is a manufacturing-planning product whose correctness depends on a chain of truths remaining consistent:

`demand -> material/BOM -> campaigns/heats -> routes -> finite schedule -> Plan Version -> release -> execution actuals -> material/WIP -> replan -> read model -> planner UI`

The test system must prove that chain at the lowest useful layer and then prove the important cross-layer workflows again at integration and user-workflow level.

The goals are:

1. prevent business-rule regressions in steel planning;
2. prevent persistence/readback drift between in-memory planning truth and stored Plan Versions;
3. prevent provider-specific defects from being hidden by EF InMemory tests;
4. prevent UI interaction regressions in the Gantt and decision workbench;
5. make failures localizable to the owning layer;
6. keep the normal test suite deterministic and reproducible;
7. use realistic integrated acceptance data without replacing focused tests with one giant fixture;
8. make release packaging depend on the whole executable test gate, not one project.

## 2. Governing principles

### 2.1 Test behavior, not implementation trivia

A test should normally assert a business invariant, public contract, persisted fact, rendered state, or user-observable interaction.

Examples of good contracts:

- material shortage remains explicit and does not silently reduce manufacturing demand;
- an LRF-ready heat may retain multiple eligible CCMs until commitment;
- running/completed operations cannot be dragged in the Gantt;
- a persisted Plan Version can be read back with the same route/resource/material decisions;
- duplicate business keys are rejected by the relational provider;
- release is impossible when release-readiness invariants are not satisfied.

Tests that merely search source files for arbitrary class names or CSS snippets are not substitutes for product behavior. Static file inspection is acceptable only when the file itself is the product contract, for example project dependency boundaries, release-script gates, fixed metadata, or forbidden repository references.

### 2.2 Lowest useful layer first

Every defect should be protected at the lowest layer that can express the actual cause.

If the defect crosses layers, add a second workflow/integration test proving the complete behavior.

Example:

- wrong material netting formula -> focused planning/material test;
- same wrong netting caused a released plan to fabricate supply -> focused planning test **plus** release/readback acceptance test.

### 2.3 No green tests that lock known-wrong behavior

Do not add characterization tests that assert a known bug merely to increase coverage. A known defect either:

- receives a failing test together with the production fix in the same change; or
- remains an explicitly tracked acceptance gap until the implementation is corrected.

Do not commit skipped tests as a substitute for implementation.

### 2.4 Deterministic time and identity

Tests should use fixed UTC epochs and stable IDs/business keys whenever the exact value matters.

Avoid `DateTime.UtcNow`, `DateTime.Now`, random delays, and uncontrolled `Guid.NewGuid()` in assertions or fixtures. Random GUIDs are acceptable only when their exact value is irrelevant and cannot influence ordering or reproducibility.

Time-dependent production code should increasingly receive an explicit reference time or clock abstraction. Planner tests already have a natural `ReferenceTimeUtc`; use it.

### 2.5 Provider fidelity matters

EF Core InMemory is acceptable for application/service tests where database semantics are irrelevant. It must not be used as evidence for:

- unique constraints;
- foreign keys/cascades;
- relational transactions;
- SQL translation;
- provider-specific mappings;
- concurrency behavior;
- migration/schema correctness.

Use in-memory SQLite for fast relational contract tests. Use the production SQL Server provider for the smaller set of tests whose behavior is SQL Server-specific.

### 2.6 One realistic reference plant, many focused fixtures

Issue #61 defines the deterministic persisted integrated steel-plant dataset. That dataset is for integrated acceptance, realistic density, performance, scenario, and end-to-end evidence.

It does **not** replace focused tests. Small fixtures should continue to isolate individual rules such as LRF alternates, CCM flexibility, recursive BOM, thermal windows, or cumulative RHF capacity.

## 3. Test layers and ownership

| Layer | Project / mechanism | What it owns | What it must not pretend to prove |
|---|---|---|---|
| Repository architecture | `APS.Architecture.Tests` | project dependency graph, test-project registration, release-gate wiring, repository-level invariants | business behavior or rendered UI |
| Domain/application/planning | primarily `APS.Planning.Tests` | steel planning rules, orchestration, material logic, route/resource semantics, solver decisions, Plan Version behavior where persistence is not the subject | relational database semantics or browser JS |
| Infrastructure/persistence | `APS.Infrastructure.Tests` | EF mappings, SQLite/SQL provider behavior, persistence/readback, transactions, execution persistence, repository/service integration | pixel/UI interaction fidelity |
| Rendered Blazor components | `APS.UI.Tests` + bUnit | component lifecycle, DOM output, selection/status/filter behavior, keyboard events, accessibility attributes, workbench component contracts | browser layout engine, pointer geometry, localStorage/fullscreen/real JS behavior |
| Browser workflow / visual regression | future browser harness under #31 | pointer drag/autoscroll/pan, focus, JS interop, responsive geometry, 1080p/1440p/4K screenshots, real planner workflows | solver internals |
| Persisted integrated acceptance | deterministic #61 reference plant | canonical SQL-backed flow across provider -> planner -> Plan Version -> release/execution/replan -> reads | replacement for focused unit/regression tests |

### Why there is no empty `APS.Domain.Tests` project

The current Domain and Application projects are predominantly entities, enums, records and interfaces/contracts. Creating a project full of property/default-value tests would add noise without protecting behavior. Domain behavior should be tested directly when behavior exists there. Until then, the planning/application behavior that consumes those contracts belongs in `APS.Planning.Tests`, while storage semantics belong in `APS.Infrastructure.Tests`.

If meaningful pure domain services/aggregates are introduced later, create `APS.Domain.Tests` at that point rather than pre-populating an empty symmetry project.

## 4. Test project rules

Every executable test project under `tests/` must:

1. set `<IsTestProject>true</IsTestProject>`;
2. be registered in `APS.slnx`;
3. run under the release test gate;
4. use the same target framework family as the product unless isolation requires otherwise;
5. have a clearly defined owning layer;
6. avoid references to higher product layers unless the test is explicitly an integration test.

`APS.Architecture.Tests` enforces the current production project dependency graph and prevents orphaned test projects.

## 5. Release gate

The local release path is authoritative for APS project verification; the repository explicitly does not use GitHub Actions/hosted CI as the APS verification mechanism.

`build/release.ps1` must run:

```text
dotnet test APS.slnx --configuration Release
```

before publish/pack unless a developer deliberately uses `-SkipTests` for non-release local iteration.

A real release must not use `-SkipTests`.

### Gate intent

The release gate should fail for:

- compilation failures in any registered test project;
- architecture boundary violations;
- planning/domain regressions;
- persistence/provider regressions;
- rendered UI component regressions.

Browser/visual suites may be a separate explicit pre-release command if their runtime makes them unsuitable for every inner-loop run, but their result is still required before a production release once #31's harness exists.

## 6. Test data policy

### 6.1 Fixed epochs

Use named fixed epochs such as:

```csharp
new DateTime(2026, 8, 22, 6, 0, 0, DateTimeKind.Utc)
```

rather than wall-clock time.

### 6.2 Stable business keys

Prefer readable keys that explain the scenario:

- `SO-10042`
- `PO-10042`
- `HEAT-2042`
- `CMP-G42-01`
- `LRF-01`, `LRF-02`
- `CCM-01`, `CCM-02`
- `RM-01`, `RM-02`

Use stable GUID constants when entity identity participates in ordering, persistence, or expected outputs.

### 6.3 Builders over giant fixtures

Repeated setup should move into narrowly named builders only after repetition becomes material. Builders must expose the scenario facts rather than hide them behind dozens of defaults.

Good:

```text
PlantBuilder.WithTwoCasters()
DemandBuilder.ForGrade("G42").Due(...).Quantity(100)
```

Bad:

```text
CreateStandardFixture42()
```

where the scenario cannot be understood without opening the helper.

### 6.4 Reference dataset

The #61 dataset should use deterministic seed/business keys and a fixed reference epoch. It should be recreated from an empty database and produce the same logical masters/demand/state.

Performance evidence should record actual operation counts and elapsed time from that dataset rather than assert an arbitrary synthetic count unrelated to the plant topology.

## 7. APS manufacturing acceptance matrix

The A–T scenarios below come from #44 and are the canonical backend acceptance spine. “Strong” means focused executable coverage already exists in the current suite for the core rule; it does not imply the whole #44 cross-layer acceptance path is complete. “Partial” means some rule-level coverage exists but the complete invariant/readback/workflow is not yet demonstrated. “Gap” means the required product behavior or acceptance path is still materially open.

| #44 scenario | Required invariant | Current focused evidence | Status / next test level |
|---|---|---|---|
| A — fully FG-covered SO | qualified FG prevents unnecessary manufacturing while demand allocation stays visible | demand orchestration / material allocation tests | Partial: add persisted demand/readback acceptance |
| B — partial FG coverage | only uncovered quantity becomes MTO manufacturing requirement | demand orchestration regression tests | Strong rule coverage; add integrated reference-plant assertion |
| C — billet inventory covers rolling | rolling proceeds without unnecessary SMS production | material/dispatch flexibility and route-aware sourcing tests | Partial: add persisted Plan Version/readback path |
| D — billet absent but internally manufacturable | internal billet requirement produces upstream Campaign/heat/CCM supply before consumption | recursive material + rolling/billet supply tests | Partial: integrated timing chain remains important |
| E — future internal billet | later internal receipt satisfies RM without duplicate replacement heat | time-phased material/late-supply tests | Partial: prove through replan/readback |
| F — deep BOM shortfall | recursive netting to leaf; non-manufacturable leaf remains explicit shortfall | recursive material requirement/late-supply tests | Strong rule coverage; add persisted shortage visibility |
| G — SMS down, billet known | downstream rolling remains feasible from qualified billet/receipt | material/dispatch flexibility + scenario work | Partial; #57 completion evidence required |
| H — SMS down, no billet | requirement remains visible with attributable shortfall; no fabricated supply | material shortage/scenario work | Partial; #57 completion evidence required |
| I — rare alternate LRF | technically qualified alternate remains eligible and selectable without changing heat/order identity | operation commitment/resource flexibility tests; activated infrastructure redispatch tests | Strong focused coverage; add UI/workflow redispatch path |
| J — CCM flexibility | LRF-ready heat can move CCM-1 -> CCM-2 if feasibility remains valid | multi-caster + operation flexibility tests | Partial: add canonical preview/apply validation workflow |
| K — parallel resources | physical CCM/RM resources remain independent concurrent timelines | multi-caster/resource-scheduling tests | Strong focused coverage; add realistic-density schedule assertion |
| L — cumulative shared RHF | overlapping feed permitted within cumulative capacity and rejected above capacity | resource scheduling / capacity semantics tests | Partial: strengthen boundary/over-capacity cases and persisted readback |
| M — mixed PO service dates | aggregation never loses independent due-date/customer service truth | service-date scheduling tests | Partial: add delivery/read-model acceptance |
| N — partial actual production | actual + remaining future supply never double counts | replanning actual-state / material ledger tests | Partial: add persisted execution -> replan -> readback flow |
| O — downstream genealogy | physical material genealogy and commercial lineage remain separately traversable | traceability/execution foundations | Gap/partial under #18: needs integrated actual material path |
| P — month-long horizon | material may be produced progressively; not all required at campaign start | unified time-phased material coverage / late-supply tests | Strong rule coverage; reference-plant month-horizon evidence still required |
| Q — infeasible explanation | named domain cause + restoration evidence, not only `Infeasible` | schedule infeasibility diagnostics tests | Partial: binding/slack evidence remains incomplete |
| R — scenario/CTP consistency | normal plan, scenario and CTP share route/material/resource semantics | canonical boundary/scenario/CTP foundations | Gap/partial under #43/#42: needs cross-path consistency acceptance |
| S — billet thermal aging / actual replan | thermal window ages; delay can force RHF; actual temperature replaces estimate | thermal constraint tests | Partial; #56 actual-replan completion evidence required |
| T — downstream route generality | billet-only, direct CCM->HotRoll and multi-pass routes use configured route truth | downstream route projection/readback tests | Partial; #58 completion evidence required |

### Matrix rule

No scenario becomes “complete” merely because one unit test exists. #44 requires concrete canonical .NET evidence across the applicable path:

`Domain/master -> SQL/provider -> application/planning -> solver -> Plan Version -> release/execution/replan -> read API`

Focused tests identify the exact rule failure. Integrated acceptance proves that the rule survives the complete lifecycle.

## 8. Gantt and planning-workbench test matrix

The Gantt is the central operational workbench and needs four distinct forms of coverage.

### 8.1 Pure geometry/model tests

Keep fast deterministic tests for:

- visible row virtualization and overscan;
- time clipping;
- zoom and snap arithmetic;
- drag candidate geometry;
- dependency geometry;
- baseline placement/change classification;
- resource hierarchy and collapse/sort behavior;
- capacity model calculations;
- operation-content density decisions;
- adaptive lane sizing once implemented.

These tests should not depend on a browser.

### 8.2 Rendered component tests

Use bUnit for component contracts such as:

- operation drag protection by execution state;
- eligible-resource data exposed to the interaction layer;
- Ctrl/Meta toggle and Shift range selection;
- keyboard context menu;
- keyboard operation navigation;
- selected/frozen/running semantic attributes;
- non-color-only status indicators;
- accessible names using business identifiers;
- toolbar state and disabled/enabled command semantics;
- analysis-dock tab/state behavior;
- release readiness presentation once the backend contract exists.

Rendered tests should assert DOM/events, not inspect `.razor` source text.

### 8.3 Browser interaction tests

A browser harness under #31 must cover the JS/browser behavior that bUnit cannot prove:

1. horizontal pan updates continuously while dragging/panning;
2. operation drag shows live ghost and snap guide;
3. edge autoscroll continuously recomputes candidate/snap feedback;
4. final drop matches the visible proposal;
5. cross-resource drag only permits eligible target lanes;
6. frozen/running/completed operations cannot be moved;
7. Ctrl/Cmd multi-select and Shift range select;
8. keyboard navigation retains visible focus as rows virtualize;
9. splitter resizing and resource-grid column resizing;
10. density/zoom/snap preferences survive reload through localStorage;
11. fullscreen enter/exit restores layout/focus;
12. context menus remain within viewport and are keyboard operable;
13. dependency overlays remain aligned while scrolling/zooming;
14. baseline compare modes remain aligned;
15. current-time/execution overlays advance correctly in long-open sessions;
16. 1080p, 1440p and 4K deterministic screenshots;
17. realistic operation counts remain responsive and usable.

### 8.4 End-to-end planner workflows

The browser/user workflow layer should eventually cover, at minimum:

- run plan -> inspect exceptions -> diagnose -> compare -> release;
- move operation -> preview full impact -> acknowledge warnings if required -> apply -> persisted child Plan Version;
- alternate-resource redispatch for LRF/CCM while preserving heat/order identity;
- execution update -> recovery/replan -> protected actual/running operations;
- material-shortage inspection without suppressing future manufacturing need;
- CTP promise -> scenario consistency;
- physical genealogy and commercial traceability traversal;
- master validation error -> correction -> rerun.

## 9. Persistence test matrix

`APS.Infrastructure.Tests` must progressively cover:

### Schema/model contracts

- full model creates on SQLite;
- unique business keys are enforced;
- foreign keys and configured cascades behave as intended;
- indexes/alternate keys critical to identity are present;
- migrations can create/upgrade a clean database where migration infrastructure is available.

### Plan Version persistence

- parent/child lineage persists;
- released versions are immutable;
- route decisions persist, including skipped operations;
- eligible resource options persist independently from selected resource;
- material requirements/reservations/ledger/sourcing alternatives persist;
- planning assumptions persist for later explanation/comparison;
- actual resource/time/quantity persists without rewriting historical plan truth.

### Transactional behavior

Cross-entity lifecycle writes that must be atomic need relational transaction tests. Examples:

- release creating WOs + scheduled operations;
- redispatch revision + operation state;
- execution actual + material output;
- replan child version + snapshots.

### SQL Server-specific tests

Only add SQL Server tests where SQLite cannot represent the production behavior. Keep their number small and explicit. Examples may include provider-specific migration/SQL behavior or concurrency semantics.

## 10. Planning and solver test matrix

Planning tests should be organized by invariant rather than implementation class.

### Demand and coverage

- full/partial FG coverage;
- MTO/MTS distinction;
- independent service dates and customer identity;
- no disappearance of manufacturing requirement.

### Material

- recursive BOM depth;
- inventory/known incoming/WIP netted once;
- required-at time;
- future internal receipts;
- non-manufacturable explicit shortfall;
- no replacement supply duplication after partial actuals;
- material shortage does not make manufacturing demand vanish.

### Campaign/heat

- grade sequence families;
- heat sizing from physical envelopes;
- campaign composition traceability;
- campaign split/merge/resequence when those commands are implemented;
- month-long campaign material timing.

### Route/resource

- configured route operation presence/order;
- optional/forbidden operations;
- all eligible physical resources retained until commitment;
- rare alternate LRF;
- CCM flexibility;
- parallel identical-type resources;
- disjunctive vs cumulative scheduling semantics;
- maintenance/derating calendars.

### Thermal

- liquid-steel windows;
- hot-direct billet aging;
- forced RHF after thermal aging;
- authoritative actual temperature replacing estimate on replan.

### Execution/replan

- running/completed/committed protection;
- partial actual quantity;
- off-plan physical actual truth retained and flagged;
- bounded repair vs broad replan;
- no duplicate material supply;
- persisted genealogy.

### Diagnostics and decision support

- named infeasibility causes;
- finite-capacity binding evidence;
- slack/headroom where supported;
- scenario comparison;
- CTP consistency;
- release-readiness reasons.

## 11. Performance tests

Do not use elapsed-time assertions on tiny synthetic fixtures as the primary performance evidence.

Use two levels:

### Deterministic algorithmic budgets

Fast tests may guard obvious complexity regressions, for example Gantt scene virtualization should mount only a bounded visible subset of a 10,000-operation input.

These are guardrails, not workstation benchmarks.

### Reference-plant benchmarks

Once #61 is available, record:

- demand count;
- campaign/heat count;
- operation count;
- material event count;
- solver wall time;
- workbench query time;
- initial Gantt render time;
- pan/zoom/selection interaction responsiveness;
- memory where practical.

Keep the hardware/environment in the evidence so comparisons are meaningful.

## 12. Accessibility and visual regression

Under #31, major workspaces require deterministic visual regression at common desktop sizes:

- 1920x1080;
- 2560x1440;
- representative 4K layout.

Screenshots should use deterministic data, fixed reference time, stable viewport, disabled nonessential animation, and stable font/render conditions.

Accessibility tests should explicitly cover:

- keyboard reachability and focus order;
- visible focus;
- screen-reader names for important commands/operations;
- status not encoded by color alone;
- contrast for critical operational states;
- reduced-motion behavior;
- menus/dialogs/context menus with correct focus return.

## 13. Regression policy

For every production defect:

1. identify the actual violated invariant;
2. add the smallest deterministic regression test that reproduces it;
3. fix production code;
4. if the defect crossed persistence/UI/lifecycle boundaries, add or extend the relevant integration/workflow test;
5. do not make the test pass by broadening tolerances unless the domain contract itself changed;
6. name the test after the behavior, not the bug ticket.

Example test name:

`Future_internal_billet_receipt_prevents_duplicate_replacement_heat`

not:

`Issue_123_test`.

Issue numbers can be referenced in comments only when they add historical context.

## 14. Test naming and structure

Use behavior-oriented names:

`<condition>_<expected outcome>`

or

`<operation>_<expected invariant>`

Keep Arrange/Act/Assert visually obvious without mandatory comments.

A test should normally have one behavioral reason to fail. Multiple assertions are appropriate when they prove one invariant, for example redispatch preserving heat identity **and** changing only the resource.

## 15. What not to do

- Do not rely only on EF InMemory for persistence confidence.
- Do not add hundreds of source-string assertions for Razor markup.
- Do not assert private implementation details when a public outcome exists.
- Do not use arbitrary sleeps.
- Do not create random synthetic plants per test run.
- Do not use current material availability to shrink the manufacturing requirement in fixtures.
- Do not treat solver `Feasible` alone as proof that a plan is release-ready.
- Do not call a UI feature tested merely because its C# model has unit tests.
- Do not use one huge #61 fixture to debug every failed planning rule.
- Do not allow test projects to exist outside the solution/release gate.

## 16. Near-term implementation order

1. **Test architecture and gate** — activate all test projects, enforce solution registration, run full solution before release.
2. **Relational persistence baseline** — SQLite model/schema/constraint/cascade tests, then Plan Version persistence/readback.
3. **Rendered Gantt component coverage** — operation block, resource grid, toolbar, analysis dock, release/validation state.
4. **Known planning regressions** — add tests together with fixes for release readiness, move-impact validation, late-demand semantics, binding evidence, persisted undo/recovery.
5. **Browser harness under #31** — pointer/keyboard/JS/layout/visual regression.
6. **#61 reference plant acceptance** — integrated A–T evidence and realistic performance.
7. **SQL Server-specific acceptance** — only where provider behavior cannot be proven with SQLite.

## 17. Definition of a properly tested APS feature

A feature is properly tested when:

- its governing domain invariant has a deterministic executable test;
- persistence semantics are tested when the feature stores or mutates canonical truth;
- its read model is tested when planner/execution users depend on that information;
- its rendered state/interaction is tested when exposed in Blazor;
- browser behavior is tested when it depends on JS, pointer geometry, layout, focus, or browser APIs;
- a cross-cutting manufacturing scenario is represented in #44/#61 integrated acceptance where applicable;
- the relevant tests are registered in the solution and executed by the release gate.

That is the standard APS should use instead of raw line coverage or raw test count.
