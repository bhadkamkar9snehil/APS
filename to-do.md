# TO-DO

## UI Principles

- One tab, one KPI row. The only KPI row for a tab lives in the shared top summary area.
- Do not add page-local KPI strips such as BOM-only or Material-only rows under the page header.
- The shared KPI row must be tab-aware, center aligned, and resize responsively without creating a second KPI system.
- The shared KPI row should be expanded by default and fully collapsible from its own control.

- The material tab has too many KPIs. It also has too much noise.
- Need to organise the entire structure of the page startijng from Material text downwards
- Dashboard needs serious re-work
    - Gantt
    - Colourful cards or visualizations to show values of all the planning constraints
- Unifiy the various cards (the long horiontal ones) that are shown in the entire software. Use a single class. Take Planning Status Card, Alert Card and Active Planning Orders Card. All should be same.
- The buttons in the second row should be right aligned or better yet merged to the tab strip somehow. Remove the text, shorted the buttons and move it to the right side.
- Somehow ensure that the relevent buttons are shown right of tabs based on the tab
- The global KPI strip cards need to be shortened and also dynamically show KPIs based on the tab which is opened
- All the text colour that is used needs to be improved to improve constrasts
- Execution Tab Gantt needs to be organised into plant wise gantts
    - The plant wise gantt should come dynamically from the config
- The arrangement of plants or equipment should be as per the routing sequence. RM after CCM.
- The KPIs shown in execution tab (beneatht he equipment timeline button strip) needs to be of the same type as all SUB KPIs.
- Define global and sub KPI OR create a single KPI card style.
- Show buttons or ways so that SO wise, PO wise and Global Gantts are also visible easily.
- The interactivity with gantt needs to be looked at to be cohessive
- Unify ALL the various kinds of statuses in the bottom strip
- Assign a right aligned area on top tab strip and bottom status bar strip which shows content per tab
    - The status bar will show statuses based on what tab is selected
- 

ChatGPT's Version including findings from screenshots.


---

| Priority | Section       | Area                                | Current State                                                       | Problem / Gap                                                                                     | Detailed To-Do                                                                                                                                                                                                                                             | Files                                                                     | Impact Type                | Dependency                    | Status   |
| -------- | ------------- | ----------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- | -------------------------- | ----------------------------- | -------- |
| **AUDIT** | **— AUDIT-BASED FIX LIST —** | **Items below were identified by auditing recent uncommitted changes against this to-do. Fix before next commit.** | | | | | | | |
| A0 | Engine — Scheduler | Greedy fallback missing SMS lateness field | `_greedy_fallback` return dict now has `weighted_rm_lateness_hours` but the value assigned is the total RM lateness (not split), and `weighted_sms_lateness_hours` is absent entirely | CP-SAT path correctly returns three separate fields (`weighted_rm_lateness_hours`, `weighted_sms_lateness_hours`, `weighted_lateness_hours`); greedy path returns only two and the RM field is mis-assigned, causing divergent response shapes between solver paths | In `_greedy_fallback` return dict: add `"weighted_sms_lateness_hours": 0.0` (greedy does not compute SMS lateness separately); rename the existing `weighted_rm_lateness_hours` assignment to use `weighted_lateness_minutes` (as it currently does) but note it is RM-only; or simplify to just set both split fields explicitly | `engine/scheduler.py` (~line 1967 return dict) | Bug — API response contract | None | Fixed |
| A0 | Engine — Scheduler | SMS changeover loop is O(n²) over all tasks, not grouped by operation | New `for left_idx, left in enumerate(sms_all_tasks)` loop pairs every SMS task against every other SMS task, skipping mismatched operations inside the inner loop | With 20 campaigns × 5 heats × 3 SMS ops = 300 tasks, the loop executes ~44 850 iterations even though cross-operation pairs are always skipped. This bloats model-build time with no solver benefit | Pre-group `sms_all_tasks` by `operation` before the pairing loop: `from itertools import groupby`; iterate groups, pair only within same-operation sets. Reduces iterations to ~(tasks_per_op choose 2) per op | `engine/scheduler.py` (~line 1340 SMS changeover loop) | Performance — solver build time | None | Fixed |
| A0 | Engine — Scheduler | `QUEUE_VIOLATION_WEIGHT` constant stale after default change | `QUEUE_VIOLATION_WEIGHT = 500` constant at module level while `_get_queue_violation_weight()` now defaults to `120` | Any code referencing the module-level constant directly receives 500; function returns 120; config override still works but the constant is now misleading | Update constant to `120` to match new default, or remove the constant entirely since all callers should use `_get_queue_violation_weight()` | `engine/scheduler.py` (line ~62) | Correctness — weight mismatch | None | Fixed |
| A0 | API | `_material_summary_from_entities` uses entity label as a dict key | Function builds a summary dict that assigns counts/quantities under both a dynamic `entity_label` string key (e.g., `"Planning Orders"`) and a hardcoded `"Campaigns"` string key in the same dict | For campaign level, `entity_label` key is overwritten by the hardcoded `"Campaigns"` key. For PO/heat levels, the dict has two semantically different keys (`"Planning Orders"`/`"Heats"` and `"Campaigns"`) that confuse consumers; JS cannot reliably read a field named after a string value | Replace the hardcoded `"Campaigns"` key with `entity_label` consistently; always use `"entity_label"` as the key name (string), with the label string as its value; remove the dual-key pattern | `xaps_application_api.py` (`_material_summary_from_entities`) | Bug — API response structure | Material mode endpoints | Fixed |
| A0 | API | `_capacity_rows()` calls `_load_all()` on every invocation | `_capacity_rows` now loads `resources_df` by calling `_load_all()` inline each time it is called | `_load_all()` reads the workbook; if capacity is polled or called multiple times in a request cycle, this is an expensive repeated read with no caching guard | Fetch `resources_df = _load_all().get("resources")` once at API startup or cache it in module-level `_state`; pass it into `_capacity_rows` or resolve it from `_state` before the enrichment loop | `xaps_application_api.py` (`_capacity_rows`) | Performance — repeated workbook reads | None | Fixed |
| A1 | API | Heat-level material plan detail fields always empty `{}` | In `_material_plan_for_detail_level`, heat entities always set `material_shortages`, `material_consumed`, `material_gross_requirements`, `inventory_before`, `inventory_after` to `{}` regardless of underlying data | UI heat-level detail panel (`renderMaterialDetail`) attempts to render these fields and will always show empty shortage and inventory sections, making the heat view useless for material decision-making | Populate heat-level detail fields by projecting scaled material rows from `_scaled_material_rows`: map shortage rows where `shortage_qty > 0` to `material_shortages`; sum consumed rows to `material_consumed`; use gross required to `material_gross_requirements`. Document explicitly if full heat-level breakdown is deferred | `xaps_application_api.py` (`_material_plan_for_detail_level`, heat branch) | Product gap — heat material view | Heat material plan design | Open |
| A1 | API | Synthetic heat fallback uses hardcoded `expected_duration_hours=2.0` | In `_material_plan_for_detail_level` heat fallback path, synthetic heats are built with `expected_duration_hours=2.0` when `_heat_batches` is empty | This hardcoded duration affects any capacity or time-based display on the heat-level material view; it is not drawn from config and will be wrong for most operations | Read from config: `get_config().get("HEAT_CYCLE_TIME_HOURS", 2.0)` or compute from `HEAT_SIZE_MT / throughput_rate`; at minimum add a comment marking this as a placeholder | `xaps_application_api.py` (`_material_plan_for_detail_level`, heat synthetic fallback) | Accuracy — planning data | Config system | Open |
| A1 | API | Release signature check returns cryptic 400 on first deploy | `aps_planning_release` now requires a `planning_signature` in `_last_result`; if `_last_result` was stored before this change was deployed, the signature field is missing and the endpoint returns HTTP 400 with a generic "stale simulation" error | Any team member who simulated before deploying this change must re-simulate before they can release; the error message does not explain this clearly enough | Check for missing `simulated_signature` explicitly and return a clear HTTP 400 body: `"Simulation was run before signature tracking was added. Please re-run Simulate before releasing."` to distinguish from a genuine stale-orders conflict (HTTP 409) | `xaps_application_api.py` (`aps_planning_release`, signature check block) | UX + deploy safety | None | Open |
| A1 | API | `_publish_planning_snapshot` always sets `run_id = None` | After simulate, release, and unrelease, `_publish_planning_snapshot` resets `_state["run_id"]` to `None` | Any endpoint or UI consumer that checks `_state["run_id"]` to determine whether a schedule has been committed will always see `None` during the planning phase, making it impossible to distinguish "plan in progress" from "no run yet" | After release, set `_state["run_id"]` to a sentinel like `"PLANNING_RELEASED"` or derive a deterministic ID from the release timestamp; after unrelease, set to `"PLANNING_UNRELEASED"`; reserve `None` for the truly uninitialized state | `xaps_application_api.py` (`_publish_planning_snapshot`) | API state correctness | Release/unrelease flow | Open |
| A1 | API | `_material_rows_to_detail_rows` sets `type` equal to `status` | Function sets both `row["status"]` and `row["type"]` to the same value: `r.get("status") or "COVERED"` | Consumers expecting `"type"` to be a material type code (e.g., scrap classification, raw material category) receive a status string instead; this causes silent semantic confusion in any code that branches on `type` | Set `row["type"]` from the material's actual type field (`r.get("material_type") or r.get("type") or ""`); keep `row["status"]` as the coverage/readiness status | `xaps_application_api.py` (`_material_rows_to_detail_rows`) | Bug — data field semantics | Material detail rendering | Open |
| A0 | UI — HTML/JS | `setCapacityDiagnosticCard` and `setMasterAuditCard` slot IDs unverified | `app.js` `setCapacityDiagnosticCard(slot, card)` references `capacityDiagLabel{n}`, `capacityDiagValue{n}`, `capacityDiagSub{n}`, `capacityDiagCard{n}`; `setMasterAuditCard(slot, card)` references `masterAuditValue{n}`, `masterAuditSub{n}`, `masterAuditCard{n}` | If the element IDs in `index.html`'s new `capacity-diagnostic-strip` and `master-audit-strip` do not exactly match these patterns, all KPI slots silently stay blank | Open `index.html`, locate every `id=` in `#capacityDiagnosticStrip` and `#masterAuditStrip`, and confirm each matches the pattern used in JS. Fix any mismatches by aligning HTML IDs to JS expectations | `ui_design/index.html`, `ui_design/app.js` (`setCapacityDiagnosticCard`, `setMasterAuditCard`) | Bug — silent UI blank | Capacity/Master page load | Fixed |
| A0 | UI — JS | Material mode preference restored after first data fetch | `restoreMaterialModePreference()` is called inside `initAppUi()`, but `loadApplicationState()` fires material plan fetch using `materialPlanUrl()` which reads `state.materialMode` | If `loadApplicationState()` executes before `initAppUi()` completes the preference restore, the initial material fetch uses the default `"campaign"` mode regardless of the user's saved preference, causing a redundant re-fetch | Move `restoreMaterialModePreference()` to execute before any data fetches — either at the top of `loadApplicationState()` or as the first statement in the `DOMContentLoaded` handler, before `loadApplicationState()` is called | `ui_design/app.js` (`initAppUi`, `loadApplicationState` call order) | Bug — state init race | Material mode persistence | Fixed |
| A0 | UI — JS/HTML | `proposePlanningOrders` throws on empty SO selection with no UI guard | `proposePlanningOrders()` now throws and the API returns HTTP 400 when `so_ids` is an empty array, but the Propose button has no disabled state when pool selection count is zero | Planner clicks Propose with nothing checked, sees a generic error notification instead of an inline preventative message; the API call is wasted | Disable the Propose button (`psPropose.disabled = true`) inside `updateSOPoolSelectionCount()` when `checkedCount === 0`; re-enable when `checkedCount > 0`; add `title="Select at least one Sales Order"` for tooltip | `ui_design/app.js` (`updateSOPoolSelectionCount`, `proposePlanningOrders`), `ui_design/index.html` | UX — prevent invalid action | Pool selection count | Fixed |
| A1 | UI — JS/HTML | Scenario delta comparison table has no empty state | New `#scenarioDeltaBody` tbody in `renderScenarios()` is populated from `state.scenarioOutput`; when that array is empty the table body renders with no rows and no placeholder | Table shows a header row with no data below it, with no explanation that scenarios have not been run or saved yet | In `renderScenarios()`, when `state.scenarioOutput` is empty, inject a single `<tr><td colspan="7" class="table-empty-cell">No scenario output — run and save a scenario to compare</td></tr>` into `#scenarioDeltaBody` | `ui_design/app.js` (`renderScenarios`) | UX — empty state | Scenario output endpoint | Open |
| A1 | UI — JS/HTML | Old capacity KPI element IDs orphaned after `renderCapacity()` refactor | `renderCapacityBars()` previously called `setText('capOverloaded', ...)`, `setText('capSlack', ...)`, `setText('capAvg', ...)`. The new `renderCapacity()` no longer calls these. If `index.html` still contains elements with those IDs, they retain stale or zero values | Stale capacity KPI numbers remain visible if the old elements were not removed from the DOM during the HTML rework | Search `index.html` for `id="capOverloaded"`, `id="capSlack"`, `id="capAvg"`: if present, remove them or re-wire them to new `setCapacityDiagnosticCard` calls in `renderCapacityDiagnostics()` | `ui_design/index.html`, `ui_design/app.js` | Bug — stale DOM values | Capacity page refactor | Open |
| A1 | UI — CSS | `--height-metric: 2.45rem` clips multi-line or longer-label metric cards | Token reduced from `3.05rem` to `2.45rem`; `BOM summary`, `Capacity diagnostic`, and `Master audit` strips use `.metric` cards with two-line labels or longer sub-text strings | At 2.45rem minimum height, any label that wraps or any sub-text below the value will be clipped or overflow the card boundary | Audit all `.metric` card contexts by testing in-browser at 1280px; apply per-strip `min-height` overrides (e.g., `.capacity-diagnostic-strip .metric { min-height: 2.8rem }`) where content is known to be taller than the global token | `ui_design/styles.css` | Visual — content clipping | All KPI strips | Open |
| A1 | UI — CSS | `.metric` 2-col grid layout conflicts with vertical-stack usage contexts | CSS changed `.metric` from `flex column` to a 2-column named-area grid (`label` top-left, `value` top-right, `sub` spanning full bottom row) | Any `.metric` that was previously rendered as a stacked column (label above value) and is not tagged with `kpi-card--sub` now renders label-left/value-right, which looks wrong for cards whose value is wide or multi-character. The pool summary grid (`planning-summary-grid--pool`) and execution KPI strip both use `.metric` and may be affected | Identify every `.metric` usage that should remain vertically stacked; ensure they carry `kpi-card--sub` class (which overrides to column layout), or add a scoped CSS rule per strip. Check: pool summary, execution KPI strip, capacity diagnostic strip, master audit strip | `ui_design/styles.css`, `ui_design/index.html` | Visual — layout regression | KPI system unification | Open |
| P0       | Navigation    | Top tab order                       | Material is after Execution                                         | BOM and Material are separated in the primary workflow even though they are conceptually adjacent | Reorder tabs to `Dashboard > Planning > BOM > Material > Execution > Capacity > CTP > Scenarios > Master Data`; verify active tab styling, tab focus order, and page activation still work after DOM reorder                                               | `index.html`                                                              | UX                         | None                          | Done     |
| P0       | Navigation    | Global action architecture          | Contextual top actions exist for Planning, BOM, Execution, Material | Pattern exists but is not yet fully normalized as the one true action system for every page       | Make the top-right navbar zone the only page-level action area; remove or avoid duplicated page-local action clusters; define explicit action pattern per tab                                                                                              | `index.html`, `app.js`, `styles.css`                                      | UI + UX                    | Tab order stable              | Done     |
| P0       | KPI System    | Shared KPI model                    | Top summary is already shared and tab-aware                         | Risk remains that secondary KPI systems creep back into pages                                     | Audit all tabs and remove duplicate local KPI strips; keep only subordinate inline stats where truly needed; enforce “one tab, one KPI row”                                                                                                                | `index.html`, `app.js`, `styles.css`                                      | UI architecture            | Shared summary stays          | Done     |
| P0       | KPI System    | KPI collapse UX                     | Collapse logic works; button sits in its own row                    | Current control is bulky, ugly, and wastes vertical space                                         | Remove `top-summary-tools` row; replace with compact anchored chevron/handle on the KPI container; use icon-first control; reclaim actual vertical height on collapse                                                                                      | `index.html`, `styles.css`, `app.js`                                      | UX + CSS                   | Shared KPI strip retained     | Done     |
| P0       | KPI System    | KPI semantics by tab                | Dynamic KPI rendering exists                                        | KPI meanings still need hard review for usefulness and consistency                                | Finalize KPI contracts per tab: Dashboard = plan health; Planning = workflow; BOM = coverage/net/gross/short; Material = ready/convert/held/short; Execution = released/in-flight/late/hold; Capacity = bottleneck/overload/slack; CTP = decision outcomes | `app.js`                                                                  | UX + product logic         | Tab model stable              | Done     |
| P0       | Design System | Card system                         | CSS cleaned up but still has `.dash-card`-style fragmentation       | Different pages still feel like separate products                                                 | Create one base card shell and a small modifier set; normalize card header height, body padding, border, radius, shadow, empty states, action placement                                                                                                    | `styles.css`, `index.html`                                                | CSS + UI consistency       | Token scale cleanup           | Done     |
| P0       | Design System | KPI component family                | `.metric`, mini-stats, section stats still differ                   | KPI language is still fragmented                                                                  | Define one KPI family with variants: top-summary, sub-KPI, inline-KPI; unify label/value/subtext/tone styling                                                                                                                                              | `styles.css`, `index.html`                                                | CSS + UI consistency       | Card unification helpful      | Done     |
| P0       | Design System | Status token system                 | Chips and badges exist                                              | Footer chips, pipeline badges, material statuses, release states are not fully unified            | Define one semantic status mapping: success, warning, danger, info, neutral; apply it across footer, planning, execution, capacity, material, BOM                                                                                                          | `styles.css`, `app.js`, `index.html`                                      | UI consistency             | KPI/card cleanup              | Done     |
| P0       | Design System | Typography and spacing              | Tokens improved in latest CSS                                       | Underlying stylesheet likely still contains hard-coded size and gap drift                         | Eliminate off-scale font sizes, spacing values, border radii, and inconsistent body/header paddings; enforce token-only usage for key layout components                                                                                                    | `styles.css`                                                              | CSS maintainability        | Design audit alignment        | Done     |
| P0       | Dashboard     | Overall structure                   | Three-column cockpit exists                                         | Still reads as stacked panels instead of one coherent operational story                           | Rework dashboard so one region is clearly dominant; left = planning status + alerts, center = schedule/capacity focal visual, right = material/release readiness                                                                                           | `index.html`, `styles.css`, `app.js`                                      | UX + layout                | Card system cleanup           | Done     |
| P0       | Dashboard     | Constraint visibility               | Dashboard shows some plan info                                      | Important planning constraints are still not surfaced as explicit visuals                         | Add compact visual cards/bars for horizon pressure, lateness pressure, shortage pressure, hold pressure, bottleneck pressure, release readiness                                                                                                            | `index.html`, `styles.css`, `app.js`                                      | UX + product clarity       | KPI semantics defined         | Done     |
| P0       | Dashboard     | Long-panel consistency              | Dashboard panels are closer than before                             | Long horizontal panel patterns still not fully unified                                            | Standardize title row, action slot, meta slot, empty state, row density, and internal padding for Planning Status, Alerts, Active Orders, Material/Capacity panels                                                                                         | `styles.css`, `index.html`                                                | UI consistency             | Card system                   | Done     |
| P0       | Material      | Information architecture            | Material is conceptually release-readiness oriented                 | Page still risks reading like a browser/report instead of a release workspace                     | Force the page hierarchy to start with: release verdict, blockers, recommended next action, then supporting detail; raw tables must be lower in the hierarchy                                                                                              | `index.html`, `styles.css`, `app.js`                                      | UX + product logic         | Material data stable          | Done     |
| P0       | Material      | Tab adjacency                       | Material not next to BOM                                            | Mental model for material analysis is broken                                                      | Same as row 1, but explicitly validate that BOM-to-Material movement feels direct and intentional in navigation flow                                                                                                                                       | `index.html`                                                              | UX                         | Row 1                         | Done     |
| P0       | Material      | Granularity model                   | Material is campaign-centric                                        | No first-class way to inspect readiness per PO or per heat                                        | Add Material view modes: `Campaign`, `Planning Order`, `Heat`; keep Campaign as default; make Heat a real mode, not a fake drill-down                                                                                                                      | `index.html`, `app.js`, `xaps_application_api.py`                         | UX + API                   | Backend support needed        | Done     |
| P0       | Material      | Backend material-plan detail levels | API auto-computes material plan using campaign detail               | Frontend cannot truthfully show heat-level material readiness without backend support             | Extend `_calculate_material_plan()` or add companion endpoints to return campaign-level, planning-order-level, and heat-level material readiness payloads (implemented via `detail_level` projection in `/api/aps/material/plan`)                        | `xaps_application_api.py`                                                 | API + product logic        | Material mode design          | Done     |
| P0       | Material      | Material mode controls              | Only refresh action exists in context zone                          | No mode switch for Campaign / PO / Heat                                                           | Add segmented control or compact selector to Material top action area; persist selected mode in frontend state; rerender summary + detail by mode                                                                                                          | `index.html`, `app.js`, `styles.css`                                      | UX + JS                    | Backend detail levels         | Done     |
| P0       | Material      | Left-side grouping                  | Risk-oriented grouping direction exists conceptually                | Default grouping/selection still needs to be explicit and rigorous                                | Group entities by `Ready`, `Needs Convert`, `Short`, `Held`; sort within groups by severity, due date, and release urgency; default to highest-risk entity                                                                                                 | `app.js`, `styles.css`                                                    | UX + logic                 | Material mode stable          | Done     |
| P0       | Material      | Detail panel hierarchy              | Better than before                                                  | Still needs stricter decision-first hierarchy                                                     | Structure detail panel as: verdict hero > blockers/actions > plant/stage readiness > shortages > convert requirements > supporting materials > lineage/explanation                                                                                         | `index.html`, `styles.css`, `app.js`                                      | UX                         | Material data stable          | Done     |
| P0       | Material      | Actionability                       | Material can show state                                             | “What to do next” is still not explicit enough                                                    | Add clear recommended actions such as `Release`, `Convert`, `Expedite`, `Substitute`, `Hold`, `Investigate Shortage`, `Wait for Upstream`; tie each to the displayed blocker state                                                                         | `app.js`, `index.html`, `styles.css`                                      | UX + product clarity       | Detail hierarchy              | Done     |
| P0       | Execution     | Plant-wise Gantt                    | Multiple execution views exist                                      | Gantt is not yet fully organized plant-wise from routing logic                                    | Group execution timelines by plant and/or process family; make plant-wise schedule a first-class view                                                                                                                                                      | `app.js`, `index.html`                                                    | UX + JS                    | Routing/metadata usable       | Done     |
| P0       | Execution     | Routing-sequence ordering           | Basic operation inference exists                                    | Equipment/plant order may still be arbitrary or overly heuristic                                  | Order plants/equipment by routing sequence; fallback only when routing is incomplete; reflect upstream-to-downstream flow explicitly                                                                                                                       | `app.js`, `xaps_application_api.py`, `engine/scheduler.py`                | JS + API + engine metadata | Routing data available        | Done     |
| P0       | Execution     | Multi-slice views                   | Some execution switching exists                                     | Need easier access to global, plant-wise, SO-wise, PO-wise views                                  | Add direct switching for `Global`, `Plant`, `SO`, `PO`; make them visible and fast, not buried or implicit                                                                                                                                                 | `index.html`, `app.js`                                                    | UX + JS                    | Execution data model          | Done     |
| P0       | Execution     | KPI consistency                     | Execution has page-specific micro-stat language                     | Execution KPIs still do not fully match the global KPI/sub-KPI family                             | Replace execution-specific KPI styling with unified KPI component family; normalize labels, values, and tones                                                                                                                                              | `styles.css`, `index.html`                                                | UI consistency             | KPI family defined            | Done     |
| P0       | Execution     | Gantt interaction coherence         | Tooltips, detail panel, zoom exist                                  | Interaction model still feels piecemeal                                                           | Standardize hover, click-to-pin detail, selection highlight, zoom behavior, empty states, and cross-view consistency                                                                                                                                       | `app.js`, `styles.css`                                                    | UX + JS                    | Execution view modes          | Done     |
| P0       | Bottom Strip  | Tab-aware bottom status             | Footer strip exists                                                 | Footer is not fully used as a tab-aware contextual strip                                          | Make bottom strip content change by active tab: planning run state, BOM warnings, material blockers, execution selection state, capacity bottleneck, CTP result status                                                                                     | `app.js`, `index.html`                                                    | UX                         | Status token system           | Done     |
| P0       | Bottom Strip  | Unified footer statuses             | Footer chips exist                                                  | Footer semantics are still fragmented                                                             | Normalize progress bar, chip tones, text hierarchy, and per-tab status templates; align with rest of design system                                                                                                                                         | `styles.css`, `app.js`                                                    | UI consistency             | Status tokens                 | Done     |
| P0       | Flow Audit    | Release simulation contract         | Release allowed when no simulation snapshot exists                  | Planner can release without an explicit current feasibility run                                   | Require `/api/aps/planning/simulate` before release and block release if simulation snapshot is missing or non-authoritative                                                                                                                               | `xaps_application_api.py`                                                 | API + release safety       | Planning simulate endpoint    | Done     |
| P0       | Flow Audit    | Simulation freshness check          | Release only checks feasible flag                                   | Feasible result can be stale if planning orders changed after simulation                          | Add stable planning-order signature in simulate result and compare it during release; reject with conflict if signature differs                                                                                                                            | `xaps_application_api.py`                                                 | API + release safety       | Release contract              | Done     |
| P0       | Flow Audit    | Schedule horizon payload parity     | UI sends `horizon_days`; backend read `horizon` only               | Planner-selected horizon could be ignored during schedule run                                     | Accept both `horizon` and `horizon_days` on `/api/aps/schedule/run`; keep backward compatibility                                                                                                                                                           | `xaps_application_api.py`, `app.js`                                       | API + UX correctness       | Run schedule wiring           | Done     |
| P0       | Flow Audit    | Release board feasibility filter    | Feasible POs inferred from `po.heats` (usually absent)             | Release queue can show false-feasible POs                                                         | Gate feasibility using scheduled campaign/PO mapping first, fallback to heat IDs via derived heat batches                                                                                                                                                   | `app.js`                                                                  | UX + release safety        | Simulate schedule rows        | Done     |
| P0       | Flow Audit    | Execution data source precedence    | Execution pages prefer persisted gantt even after fresh simulate    | Planner can see stale execution bars while sim cards show newer state                             | Prefer `lastScheduleRows` when a fresh simulation exists; fallback to persisted gantt only when needed                                                                                                                                                     | `app.js`                                                                  | UX + state consistency     | Simulation state              | Done     |
| P1       | Flow Audit    | Heat-size parameter consistency     | UI sends `heat_size_mt`; backend ignored and used workbook default  | Planner cannot trust derive-heats response to reflect current input                               | Respect payload `heat_size_mt` in `/api/aps/planning/heats/derive` with safe fallback to config value                                                                                                                                                     | `xaps_application_api.py`                                                 | API + planning trust       | Heats derive API              | Done     |
| P1       | Flow Audit    | Pipeline BOM demand scope           | Pipeline BOM uses full workbook open demand                         | BOM totals in planning flow diverge from selected planning set                                    | Run pipeline BOM using selected planning orders via `/api/aps/bom/tree`; keep full workbook BOM for explicit BOM action                                                                                                                                   | `app.js`, `xaps_application_api.py`                                       | UX + planning correctness  | Planning orders available     | Done     |
| P1       | Flow Audit    | Released SO recirculation           | Released SOs can return to planning pool via `PLANNED` status       | Previously released demand can be accidentally re-proposed                                        | Exclude `Campaign_Group = APS_RELEASED` rows from open planning candidate mask                                                                                                                                                                             | `xaps_application_api.py`                                                 | API + data integrity       | Sales order status semantics  | Done     |
| P1       | Flow Audit    | Simulate-to-material coherence      | `/planning/simulate` does not refresh run-artifact material/campaign | Material and overview endpoints can stay artifact-stale after planning-only changes               | Recompute and publish planning-scoped material/campaign payloads after simulate, release, and unrelease; align dashboard/material/campaign endpoints to active planning state (implemented with planning snapshot publisher + mutation-time republish)                                         | `xaps_application_api.py`, `app.js`                                       | API + cross-tab coherence  | Artifact/state model          | Done     |
| P1       | Flow Audit    | Empty selection semantics           | Propose with no checked SOs falls back to entire window             | Planner may think “none selected” means “propose none”                                            | Make propose behavior explicit: either enforce at least one selected SO or show explicit “all selected by default” confirmation in UI                                                                                                                     | `app.js`, `xaps_application_api.py`                                       | UX + planning clarity      | Pool selection UX             | Done     |
| P1       | Layout        | Scroll containment audit            | Page containment work was done                                      | Needs regression audit against latest active files and new changes                                | Re-test all pages for fixed header behavior, bounded page content, no nested awkward scrollbars, and resilient min-height/flex behavior                                                                                                                    | `index.html`, `styles.css`                                                | QA + layout                | Ongoing after UI changes      | Done     |
| P1       | Planning      | Workflow hierarchy                  | Planning is the main APS workflow                                   | Flow can still be clearer and more sequential                                                     | Strengthen the visual and behavioral hierarchy between pool selection, planning-order proposal, heat derivation, simulation, and release                                                                                                                   | `index.html`, `app.js`, `styles.css`                                      | UX                         | Shared action model           | Done     |
| P1       | Planning      | Pipeline feedback                   | Stage statuses exist                                                | Feedback can be more productized and more obviously sequential                                    | Improve stage badges, running/done/error semantics, step transitions, and carry-forward of pipeline state into footer and summary                                                                                                                          | `app.js`, `styles.css`                                                    | UX + JS                    | Planning workflow stable      | Done     |
| P1       | BOM           | Total-plan material role clarity    | BOM is already defined as total exploded/netted demand              | Page still needs stronger identity separation from Material                                       | Strengthen BOM as the total-plan material page; improve summary narrative, grouping, and distinction from release-readiness logic                                                                                                                          | `app.js`, `index.html`, `styles.css`                                      | UX                         | BOM data stable               | Done     |
| P1       | BOM           | Structure-error surfacing           | BOM engine supports cycle/max-level error recording                 | UI may not surface structure errors strongly enough                                               | Show BOM cycle errors, max-level exceeded, degraded feasibility, and path-based structure warnings clearly in BOM view and footer                                                                                                                          | `xaps_application_api.py`, `app.js`                                       | API + UX                   | Error payloads available      | Done     |
| P1       | Capacity      | Diagnostic richness                 | Capacity bars exist                                                 | Capacity page still risks being too shallow                                                       | Show demand vs available hours, process vs setup vs changeover burden, overloaded count, slack count, and bottleneck narratives                                                                                                                            | `app.js`, `index.html`, `xaps_application_api.py`                         | UX + JS + API              | Capacity metrics available    | Done     |
| P1       | Capacity      | Capacity breakdown fidelity         | Capacity engine tracks multiple hour buckets                        | UI compresses too much into utilization bars                                                      | Expose `Demand_Hrs`, `Process_Hrs`, `Setup_Hrs`, `Changeover_Hrs`, `Task_Count`, available hours, and effective utilization in table and/or detail views                                                                                                   | `app.js`, `xaps_application_api.py`                                       | Product clarity            | Capacity engine data          | Done     |
| P1       | CTP           | Decision explanation                | CTP supports rich decision classes                                  | UI likely does not explain promise reasoning deeply enough                                        | Surface clear categories for stock-only, merged, new campaign, later-date, material block, capacity block, policy-only block, mixed blockers                                                                                                               | `xaps_application_api.py`, `app.js`                                       | API + UX                   | CTP payload review            | Done     |
| P1       | CTP           | Inventory lineage trust             | CTP tracks authoritative vs degraded inventory lineage              | Trust state is operationally important but may be hidden                                          | Display lineage confidence explicitly: `Authoritative`, `Recomputed`, `Conservative Blend`; explain what each means for planner confidence                                                                                                                 | `xaps_application_api.py`, `app.js`                                       | API + UX                   | CTP payload review            | Done     |
| P1       | Master Data   | Audit surfacing                     | Master Data exists as workbook-backed CRUD surface                  | Needs stronger health/audit posture                                                               | Surface missing masters, routing gaps, config conflicts, stale resources, queue-time problems, and required setup warnings                                                                                                                                 | `xaps_application_api.py`, `app.js`                                       | API + UX                   | Audit support                 | Done     |
| P1       | Scenarios     | Comparison UX                       | Scenario support exists                                             | Needs better “what changed?” usability                                                            | Show scenario deltas for planned MT, heats, on-time %, material holds, bottleneck load, and release readiness shifts                                                                                                                                       | `app.js`, `index.html`                                                    | UX + JS                    | Scenario metrics availability | Done     |
| P2       | Accessibility | Keyboard/focus behavior             | Basic tab roles exist                                               | Complex controls likely need stronger keyboard and focus treatment                                | Audit focus rings, tab order, segmented controls, detail drawers, and contextual actions for full keyboard usability                                                                                                                                       | `index.html`, `styles.css`, `app.js`                                      | Accessibility              | UI stabilization              | Not done |
| P2       | Content       | Empty states and instructional copy | Generic empty states exist                                          | Empty states can be more operationally helpful                                                    | Replace generic placeholders with context-aware instructions like “Run pipeline”, “No shortages in current run”, “No released lots yet”, “No overloads detected”                                                                                           | `app.js`, `index.html`                                                    | UX copy                    | Page-specific logic           | Partial  |
| P2       | Export        | Operational exports                 | No clear export pattern across major pages                          | High-value views should be exportable                                                             | Add export for BOM net/gross, Material readiness list, Capacity table, Execution dispatch list, and CTP result history                                                                                                                                     | `app.js`, `xaps_application_api.py`                                       | JS + API                   | Stable data shapes            | Not done |
| P2       | Traceability  | Run diagnostics visibility          | API has run artifacts and trace IDs                                 | UI likely underuses them                                                                          | Surface run ID, timestamp, solver status, degraded flags, and last-run diagnostics in a compact diagnostics zone or drawer                                                                                                                                 | `xaps_application_api.py`, `app.js`                                       | API + UX                   | Run artifact model            | Not done |
| P1       | Architecture  | APS model alignment                 | Repo contains both campaign-first and planning-order-first concepts | UI and product language can become ambiguous between campaign, PO, and heat                       | Clarify and enforce where campaign view is valid and where PlanningOrder/HeatBatch are the correct dominant entities; reduce terminology drift in UI                                                                                                       | `README.md`, `xaps_application_api.py`, `app.js`, `engine/aps_planner.py` | Product architecture       | Material granularity work     | Done     |
| P1       | Architecture  | Excel vs API expectation management | API mode and Excel mode are intentionally separate                  | Users can still misread why Excel and web differ after schedule runs                              | Add clear messaging that REST scheduling uses in-memory run artifacts and does not automatically write workbook sheets; surface this where confusion is likely                                                                                             | `README.md`, `app.js`                                                     | Product clarity            | None                          | Done     |
| P1       | Engine        | Scheduler objective balance         | Audit already identified objective weaknesses                       | Current optimization can distort lateness, idle time, and queue penalties                         | Rebalance SMS vs RM lateness weight, add idle/fragmentation minimization, make queue penalties proportional, consider early-finish incentive                                                                                                               | `engine/scheduler.py`                                                     | Engine logic               | Solver regression testing     | Done     |
| P1       | Engine        | SMS changeover enforcement          | Audit says RM changeover enforced, SMS not fully enforced           | Schedule realism and CTP trust can be compromised                                                 | Add SMS changeover constraints and more clearly separate transfer time from changeover time                                                                                                                                                                | `engine/scheduler.py`                                                     | Engine logic               | Changeover matrix behavior    | Done     |
| P1       | Engine        | Campaign grouping flexibility       | Audit found split/merge limitations                                 | Campaign grouping may not reflect urgency, utilization, or partial release needs well enough      | Add urgency-aware split logic, evaluate merge-for-utilization options, review all-or-nothing hold behavior, improve campaign priority inheritance consistency                                                                                              | `engine/campaign.py`                                                      | Engine logic               | Business rule review          | Done     |
| P1       | Engine        | CTP alternative ranking             | CTP uses precedence model                                           | Ranking does not yet strongly reflect feasibility margin/confidence                               | Improve CTP alternative ranking with feasibility/confidence scoring and clearer rationale selection                                                                                                                                                        | `engine/ctp.py`                                                           | Engine logic               | CTP payload review            | Done     |




---

| Priority | Category         | Area                       | Problem                                                                                                       | Detailed To-Do                                                                                                                                                                                                                                               | Files                                | JS Needed     |
| -------- | ---------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------ | ------------- |
| P0       | Architecture     | ~~Top navigation~~             | ~~Actions are still page-local instead of truly tab-contextual~~                                                  | ~~Make the top strip the single action area for page-level primary actions. Remove page-header action duplication for BOM, Execution, Material. Use one right-aligned action slot pattern everywhere. Keep only secondary explanatory content in page headers.~~ | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Architecture     | ~~Top navigation~~             | ~~`navbarContextSlot` exists but is not being used as the real page action system~~                               | ~~Wire the slot so each tab injects its own actions and optional micro-status. Planning keeps pipeline actions, Execution gets rerun/patch/view actions, BOM gets explosion action, Material gets refresh/action tools.~~                                        | `app.js`, `index.html`               | Yes           |
| P0       | Architecture     | ~~Global KPI strip~~           | ~~KPI strip is still mostly global and static instead of tab-aware~~                                              | ~~Replace fixed global summary semantics with tab-aware KPI rendering. Dashboard should show plan-wide KPIs, Execution should show schedule KPIs, Material should show risk KPIs, BOM should show coverage KPIs.~~                                               | `app.js`, `index.html`               | Yes           |
| P0       | Design system    | Cards                      | `.card`, `.dash-card`, pipeline stages, detail panes, Material sections all feel like different products      | Create one base shell class and a small modifier set. Unify header spacing, border, radius, shadow, and body padding. Remove the separate “dash card” visual identity.                                                                                       | `styles.css`, `index.html`           | No            |
| P0       | Design system    | KPI system                 | Global metrics, execution KPIs, BOM KPIs, mini stats and dashboard stats are inconsistent                     | Create one KPI component family with size variants: global, section, inline. Stop using separate visual systems for `.metric` and `.dash-mini-stat`.                                                                                                         | `styles.css`, `index.html`           | Minimal       |
| P0       | Design system    | Status system              | Footer chips, badges, pills, severity tags and status labels are fragmented                                   | Define one semantic status token system for success, warning, danger, info, neutral. Apply it across footer, Material, Pipeline, BOM, Execution, and Dashboard.                                                                                              | `styles.css`, `index.html`           | Minimal       |
| P0       | Visual           | Typography and contrast    | Contrast is improved from older versions but still too soft and too inconsistent in secondary text            | Tighten hierarchy: titles, subtitles, labels, captions. Darken `--text-soft` usage in key places. Reduce overuse of faint text for operationally important information.                                                                                      | `styles.css`                         | No            |
| P0       | Visual           | Spacing and density        | Some areas are still over-padded while others are too compressed                                              | Normalize spacing scale across navbar, KPI strip, card headers, tables, filters, detail panels, and Gantt surfaces.                                                                                                                                          | `styles.css`                         | No            |
| P0       | Dashboard        | Overall structure          | Dashboard still lacks a single dominant focal area                                                            | Redesign dashboard around one primary operational visual. Left = planning flow / issues, center = main schedule / bottleneck visual, right = constraints / material / active orders.                                                                         | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Dashboard        | Constraint visibility      | Planning constraints are still mostly textual or scattered                                                    | Add explicit visual cards or mini bars for horizon, utilization pressure, lateness, shortage pressure, held status, and capacity risk.                                                                                                                       | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Dashboard        | Card consistency           | Planning Status, Alerts, Active Planning Orders remain visually related but still not fully unified           | Convert all long dashboard strips into one standard panel/list card pattern with consistent header/action/meta placement.                                                                                                                                    | `styles.css`, `index.html`           | No            |
| P0       | Material         | Information architecture   | Material is still campaign-browser-like instead of release-readiness workspace                                | Reframe Material around one question: can this campaign release? Build shell around release verdict, blockers, next actions, and supporting detail.                                                                                                          | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Material         | ~~KPI strip~~                  | ~~Too many KPIs and too much noise remain in Material~~                                                           | ~~Reduce Material strip to 4–5 decision KPIs only: Ready, Needs Convert, Short, Held, maybe Total Campaigns. Remove less actionable metrics from top strip.~~                                                                                                    | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Material         | ~~Left tree~~                  | ~~Tree is still not grouped by risk/action~~                                                                      | ~~Group left tree by Ready / Needs Convert / Short / Held, then campaigns under those groups. Default selection should be highest-risk campaign, not first entry.~~                                                                                              | `app.js`, `styles.css`               | Yes           |
| P0       | Material         | ~~Detail panel~~               | ~~Detail is richer than shell but still reads like a report~~                                                     | ~~Rebuild right side into: hero verdict, action recommendations, plant coverage, shortage table, then supporting materials table. Put the decision layer above the raw rows.~~                                                                                   | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Material         | ~~Actions~~                    | ~~No obvious “what to do next” structure~~                                                                        | ~~Add a clear action area: Convert, Expedite, Substitute, Hold, Review Inventory. Even if initially non-functional, the layout should support the workflow.~~                                                                                                    | `index.html`, `styles.css`           | No / Later JS |
| P0       | Execution        | ~~Core structure~~             | ~~Execution still behaves like 3 mini-pages stitched together~~                                                   | ~~Reframe Equipment Timeline, Released Lots, and Plant Gantt as coordinated modes of one execution workspace, not disconnected panels.~~                                                                                                                         | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Execution        | ~~Timeline-first design~~      | ~~Timeline is still a card inside a page instead of the primary execution surface~~                               | ~~Make the timeline the dominant visual and keep auxiliary information secondary. Reduce header clutter above it.~~                                                                                                                                              | `index.html`, `styles.css`           | No            |
| P0       | Execution        | ~~Right panel~~                | ~~Right panel is still conditional and underpowered~~                                                             | ~~Make the execution side panel persist. Default state should explain what selection reveals. Selected state should show campaign summary, linked SOs, due, heats, and material context.~~                                                                       | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Execution        | ~~Timeline header text~~       | ~~Subtitle copy still reads like filler~~                                                                         | ~~Replace generic help copy with contextual operational copy: visible horizon, active filters, selection state, or schedule state.~~                                                                                                                             | `index.html`, `app.js`               | Yes           |
| P0       | Execution        | ~~Fake chips / helper tokens~~ | ~~Decorative helper pills near timeline header do not do enough~~                                                 | ~~Remove fake helper chips or replace them with one meaningful status indicator. No decorative control-like elements that are not real.~~                                                                                                                        | `index.html`, `styles.css`           | No            |
| P0       | Execution        | ~~Routing order~~              | ~~Resource order still needs to visually reinforce routing~~                                                      | ~~Make resource and plant order explicitly reflect routing sequence, including RM after CCM, not arbitrary order.~~                                                                                                                                              | `app.js`, `styles.css`               | Yes           |
| P0       | Execution        | ~~Plant Gantt~~                | ~~Plant view is not yet the mature plant-wise Gantt discussed earlier~~                                           | ~~Build true plant-grouped Gantt mode sourced from routing/config ordering instead of a visually relabeled alternative view.~~                                                                                                                                   | `app.js`, `styles.css`, `index.html` | Yes           |
| P0       | Execution        | ~~Gantt modes~~                | ~~SO / PO / Global schedule views are still missing or not prominent~~                                            | ~~Add a clear view-mode strip: Plant / Equipment / SO / PO / Global. Make them obvious and colocated with timeline context.~~                                                                                                                                    | `index.html`, `styles.css`, `app.js` | Yes           |
| P0       | Execution        | ~~Gantt readability~~          | ~~Timeline still reads as generated debug output in parts~~                                                       | ~~Strengthen time axis, lane structure, day boundaries, bar density, and label contrast. Reduce the “thin inline-generated” feeling.~~                                                                                                                           | `styles.css`, `app.js`               | Yes           |
| P0       | Execution        | ~~Cohesive interaction~~       | ~~Timeline selection does not yet feel fully cohesive across all execution surfaces~~                             | ~~Align Released Lots selection, timeline selection, plant-gantt selection, and right-panel rendering into one clear selection model.~~                                                                                                                          | `app.js`                             | Yes           |
| P1       | Execution        | ~~Capacity in context~~        | ~~Execution lacks strong inline capacity/utilization context~~                                                    | ~~Add right-side or lower-side execution-adjacent load/utilization summary so capacity is not mentally detached from schedule review.~~                                                                                                                          | `index.html`, `styles.css`, `app.js` | Yes           |
| P1       | Execution        | ~~Embedded constraints~~       | ~~Alerts and blockers are still not properly living in the Gantt~~                                                | ~~Add direct timeline signals for late/held/conflict/maintenance/bottleneck states.~~                                                                                                                                                                            | `styles.css`, `app.js`               | Yes           |
| P1       | BOM              | ~~Action placement~~           | ~~BOM Explosion action is still tied to BOM page shell instead of true tab context~~                              | ~~Move the action into top tab action region and keep BOM page header focused on title and explanation.~~                                                                                                                                                        | `index.html`, `app.js`, `styles.css` | Yes           |
| P1       | BOM              | ~~Detail structure~~           | ~~BOM detail is still list-heavy and operationally flat~~                                                         | ~~Improve hierarchy: stage summary, selected stage hero, condensed item cards, clearer covered/short/partial distinctions.~~                                                                                                                                     | `styles.css`, `index.html`, `app.js` | Yes           |
| P1       | Capacity         | ~~Relationship to execution~~  | ~~Capacity page is useful but still too detached from execution review~~                                          | ~~Align Capacity visuals and component model with Execution so it feels like the same scheduling system.~~                                                                                                                                                       | `styles.css`, `index.html`, `app.js` | Yes           |
| P1       | Planning         | ~~Pipeline flow~~              | ~~Pipeline is still a stacked accordion more than a true flow~~                                                   | ~~Improve directional feel, stage progression, and status legibility. Reduce the feeling of independent vertical cards.~~                                                                                                                                        | `styles.css`, `index.html`, `app.js` | Yes           |
| P1       | Planning         | ~~Table dominance~~            | ~~Pipeline stages quickly devolve into tables~~                                                                   | ~~Add summary/action layer before each large table: risk, count, ready, blocked, selected, what next.~~                                                                                                                                                          | `index.html`, `styles.css`, `app.js` | Yes           |
| P1       | Footer           | ~~Status bar~~                 | ~~Footer is still mostly global and not tab-aware~~                                                               | ~~Split footer into global health + tab-specific context. Right side should adapt to active tab.~~                                                                                                                                                               | `index.html`, `styles.css`, `app.js` | Yes           |
| P1       | Empty states     | ~~Guidance~~                   | ~~Empty states across Execution, Material, Dashboard, BOM are still too plain~~                                   | ~~Improve empty-state structure with meaningful guidance and next-action wording.~~                                                                                                                                                                              | `index.html`, `styles.css`           | No            |
| P1       | Tables           | ~~Reusability and density~~    | ~~Tables across Planning, BOM, Capacity, Master Data, Material are visually related but not standardized enough~~ | ~~Tighten one standard table density, header style, hover behavior, and action-cell pattern.~~                                                                                                                                                                   | `styles.css`                         | No            |
| P1       | Buttons          | ~~Density and wording~~        | ~~Buttons are improved but still too text-heavy in many places~~                                                  | ~~Shorten labels further where appropriate, introduce stronger icon+short-text pattern, and avoid duplicate action wording.~~                                                                                                                                    | `styles.css`, `index.html`           | No            |
| P1       | Radius / corners | ~~Visual consistency~~         | ~~Radius language is better but still inconsistent across some components~~                                       | ~~Enforce one radius strategy across cards, tables, timeline containers, tree panels, and pills.~~                                                                                                                                                               | `styles.css`                         | No            |
| P2       | Dashboard        | Deep visual analytics      | Dashboard still lacks the richer schedule/constraint visualization you referenced from other software         | Add constraint mini-bars, schedule health visuals, and eventually a true central Gantt/heatmap hybrid.                                                                                                                                                       | `index.html`, `styles.css`, `app.js` | Yes           |
| P2       | Interaction      | Advanced Gantt editing     | Patch / drag / manual adjust logic is still immature                                                          | Leave for later after structure, routing, and visibility problems are solved.                                                                                                                                                                                | `app.js`, `styles.css`               | Yes           |
| P2       | System polish    | Per-tab KPI + action sync  | Full per-tab KPI/action/footer coherence is not yet finished                                                  | Once top strip and footer logic are done, align them so every tab has clear top actions, summary KPIs, and bottom status context.                                                                                                                            | `app.js`, `index.html`, `styles.css` | Yes           |




---

# ✅ COMPLETE UI FIX TABLE (WITH ISSUE TYPES)

| Issue Type      | Area                 | What is wrong                                       | Where (File / Component)                | What to change                                                               |
| --------------- | -------------------- | --------------------------------------------------- | --------------------------------------- | ---------------------------------------------------------------------------- |
| **Layout**      | ~~Top Navigation~~       | ~~Buttons in second row, disconnected from tabs~~       | `index.html → .top-controls`, `.navbar` | ~~Move controls into `.navbar` → create `.tabs-right` container (flex-end)~~     |
| **Interaction** | ~~Tab Context~~          | ~~Buttons not contextual to tab~~                       | `app.js → activatePage()`               | ~~Inject tab-specific actions dynamically into `.tabs-right`~~                   |
| **Visual**      | Buttons              | Too many, text-heavy                                | `.btn` in `styles.css`                  | Create `.btn-icon`, reduce padding, remove text labels where possible        |
| **Component**   | ~~KPI System~~           | ~~Global KPI strip static, irrelevant per tab~~         | `index.html → .top-summary`             | ~~Replace with dynamic KPI renderer per tab~~                                    |
| **Layout**      | KPI Strip            | Too tall, too dominant                              | `.metric`                               | Reduce height (`--height-metric`), tighter padding                           |
| **Component**   | KPI Types            | Global vs sub KPIs inconsistent                     | `.metric`, `.dash-mini-stat`            | Create single `.kpi-card` with variants: `--global`, `--section`, `--inline` |
| **Visual**      | Text Contrast        | Low contrast across UI                              | `:root colors` in `styles.css`          | Darken `--text-soft`, reduce usage of `--text-faint`                         |
| **Component**   | Cards                | Multiple card styles (`.card`, `.dash-card`, etc.)  | `styles.css`, `index.html`              | Replace all with unified `.panel` class                                      |
| **Component**   | Horizontal Cards     | Planning Status, Alerts, Active Orders inconsistent | Dashboard sections                      | Convert all into `.panel.panel--list`                                        |
| **Layout**      | Dashboard            | No focal hierarchy                                  | `page-dashboard` in `index.html`        | Redesign into 3 zones: left (pipeline), center (gantt), right (constraints)  |
| **Visual**      | Dashboard Cards      | Flat, no visual encoding                            | `.dash-card`                            | Add color-coded headers + icon indicators                                    |
| **Visual**      | Planning Constraints | Shown as text only                                  | Dashboard                               | Convert into mini visual bars / gauges                                       |
| **Layout**      | ~~Material Page~~        | ~~Too many KPIs, noisy~~                                | `page-material → .material-kpi-row`     | ~~Reduce to 4 KPIs only~~                                                        |
| **Layout**      | Material Page        | No structure below header                           | `materialLayout`                        | Reorganize: Left (campaigns) / Right (detail hero + sections)                |
| **Component**   | ~~Material KPIs~~        | ~~Not contextual~~                                      | `renderMaterialKPIs()`                  | ~~Show only risk KPIs (Ready / At Risk / Short / Held)~~                         |
| **Interaction** | ~~Material Tree~~        | ~~Weak grouping~~                                       | `materialTree`                          | ~~Group by risk → campaign~~                                                     |
| **Visual**      | ~~Material Detail~~      | ~~Hard to scan~~                                        | `renderMaterialDetail()`                | ~~Break into sections: Coverage / Shortage / Actions~~                           |
| **Layout**      | ~~Execution Gantt~~      | ~~Flat, not plant grouped~~                             | `app.js → gantt rendering`              | ~~Group by plant containers~~                                                    |
| **System**      | ~~Plant Order~~          | ~~Not aligned to routing~~                              | scheduler + UI                          | ~~Use routing sequence (EAF → LRF → VD → CCM → RM)~~                             |
| **Interaction** | ~~Gantt Views~~          | ~~SO/PO/Global not visible~~                            | Execution controls                      | ~~Add view toggle strip: `Plant / Equipment / SO / PO / Global`~~                |
| **Component**   | ~~Execution KPIs~~       | ~~Different style from rest~~                           | Execution KPI row                       | ~~Replace with `.kpi-card--section`~~                                            |
| **Visual**      | ~~Gantt~~                | ~~Looks like debug view~~                               | timeline CSS                            | ~~Add color bands, highlight bottlenecks~~                                       |
| **Interaction** | ~~Gantt~~                | ~~Weak interactivity~~                                  | `app.js` gantt handlers                 | ~~Add hover highlight, click → detail panel~~                                    |
| **Layout**      | Bottom Status Bar    | Static, not contextual                              | `.status-footer`                        | Split into left (global) + right (tab-specific)                              |
| **Component**   | Status Chips         | Multiple styles                                     | `.chip` variants                        | Unify into `.status-chip`                                                    |
| **System**      | Status Meaning       | Color semantics unclear                             | global styles                           | Standardize: red=critical, amber=warning, green=ok                           |
| **Layout**      | Page Structure       | Feels like stacked strips                           | `.page-content` usage                   | Introduce hierarchy: control → insight → detail                              |
| **Visual**      | Typography           | Too many sizes                                      | `styles.css`                            | Enforce 5-level scale only                                                   |
| **Layout**      | Tables               | Over-dominant                                       | Planning/Material/Execution tables      | Always add summary layer before tables                                       |
| **Interaction** | Pipeline             | Static blocks                                       | Dashboard pipeline                      | Convert to directional flow with state                                       |
| **Visual**      | Empty States         | Poor handling                                       | Splash + empty panels                   | Add loading, guidance, placeholders                                          |
| **Component**   | Radius               | Inconsistent rounding                               | `styles.css`                            | Use single `--radius`                                                        |
| **Component**   | Spacing              | Too many values                                     | `styles.css`                            | Enforce spacing scale only                                                   |
| **Interaction** | Right Panel          | Inconsistent usage                                  | Planning/Material                       | Make persistent contextual detail panel                                      |
| **Visual**      | Density              | Too dense or too empty                              | All screens                             | Apply progressive disclosure                                                 |

---

# X-APS Complete To-Do List

## Document purpose

This document consolidates the current master UI to-do, the latest uploaded frontend files, and the supporting architecture/logic notes into one implementation-ready backlog. It is intended to be the single working list for UI, frontend behavior, API wiring, and planning-logic improvements. The active product surface is the static UI driven by `index.html`, `styles.css`, `app.js`, backed by `xaps_application_api.py` and the `engine/` modules.     

---

## 1. Core product principles that must govern all fixes

* There must be **one shared KPI system**, not a global KPI strip plus local page KPI strips.
* The top KPI row must be **tab-aware** and must change meaning based on the active tab.
* The top tab strip must be the **single page-level action zone**.
* Cards, statuses, buttons, spacing, typography, and surfaces must feel like **one product**, not multiple stitched-together interfaces.
* The Material page must answer: **“Can this lot/campaign release, and what is blocking it?”**
* The BOM page must answer: **“What does the active plan require in total?”**
* Material and BOM are related, but they are not the same page and must not collapse into one view.
* Execution must feel like a true **handoff and dispatch workspace**, not just another reporting page.  

---

## 2. Current state summary

### 2.1 What is already in place

* The UI already has a shared top summary row with tab-aware KPI rendering logic.
* The top strip already has contextual actions for Planning, BOM, Execution, and Material.
* The app already supports a single-page contained layout with page scroll containment improvements.
* The Material tab is already positioned as a release-readiness view, but the implementation is still campaign-centric.
* The API already auto-computes a material plan after scheduling and stores it in the active run artifact.    

### 2.2 What is still fundamentally wrong

* Material is still **not next to BOM** in the top navigation.
* The Material page is still structurally **campaign-centric**, not truly multi-granular.
* There is still **no first-class per-heat material mode**.
* The KPI collapse control works, but its UX is bulky and consumes vertical space.
* The CSS is cleaner than before, but the design system is still **not fully unified**.
* Dashboard, Material, Execution, and bottom status patterns still need stronger consistency.    

---

## 3. Priority framework

### P0 — must fix first

These items directly affect product structure, clarity, workflow correctness, or release decisions.

### P1 — should fix next

These items improve consistency, readability, workflow quality, and user confidence.

### P2 — valuable improvements

These items improve polish, maintainability, and deeper operational usefulness.

---

# 4. P0 To-Do Items

## 4.1 Navigation and tab ordering

### P0.1 Reorder tabs so Material sits next to BOM

**Problem**
The current order is `Dashboard → Planning → BOM → Execution → Material → Capacity → CTP → Scenarios → Master Data`, which breaks the intended material-flow mental model. Material must sit adjacent to BOM. 

**Required change**

* Move `Material` so the top strip reads:

  * Dashboard
  * Planning
  * BOM
  * Material
  * Execution
  * Capacity
  * CTP
  * Scenarios
  * Master Data

**Files**

* `index.html`

**Notes**

* This is mostly a markup/order fix.
* Existing `data-page` activation logic should continue to work without a redesign because tab activation is keyed by `data-page`, not by visual position. 

---

## 4.2 Top-strip architecture and page actions

### P0.2 Complete the top-strip single action area pattern

**Problem**
The app has the basic context slot pattern, but it still needs to become the definitive and clean action model for every tab. Some action semantics are still not fully normalized.  

**Required change**

* Keep one right-aligned action zone for page-level actions.
* Ensure every tab has a clear action pattern:

  * Planning: planning window + pipeline controls
  * BOM: run explosion / refresh / grouping controls
  * Material: refresh + view-mode controls
  * Execution: rerun / filter / view controls
  * Capacity: export / grouping / threshold controls
  * CTP: run request / reset / history controls
* Remove page-local duplicated action clusters if any still exist inside page content.

**Files**

* `index.html`
* `app.js`
* `styles.css`

---

## 4.3 KPI system and top summary UX

### P0.3 Keep one KPI system only

**Problem**
The product requirement is explicit: one shared KPI row, tab-aware, no duplicate page-local KPI rows. That principle must remain enforced everywhere.  

**Required change**

* Audit all tabs and remove any secondary KPI strips that duplicate the top summary.
* Use the top summary as the single KPI surface for each tab.
* Keep per-card inline stats only when they are subordinate, not competing KPI systems.

**Files**

* `index.html`
* `app.js`
* `styles.css`

### P0.4 Redesign the KPI collapse UX

**Problem**
The current “Hide KPIs” control is functionally correct but visually heavy and vertically wasteful. It sits in its own tool row, which defeats the purpose of reclaiming space.   

**Required change**

* Remove the separate `top-summary-tools` row pattern.
* Replace it with a compact inline edge control attached to the summary container.
* Use:

  * corner-mounted chevron handle
  * icon-first compact state
  * minimal hover-reveal label
  * smoother collapse/expand behavior
* When collapsed, reclaim real vertical space.

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P0.5 Tighten KPI semantics by tab

**Problem**
The KPI row is already tab-aware, but the semantics still need to be reviewed for precision and usefulness. 

**Required change**

* Dashboard: global plan health
* Planning: order pool / planning orders / heats / progress / feasibility
* BOM: coverage / gross / net / short / blocked material counts
* Material: ready / convert / short / held / release-risk counts
* Execution: released lots / due soon / in-progress / late / holds
* Capacity: bottleneck / avg util / overloaded / slack / critical assets
* CTP: confirmed / conditional / later-date / blocked mix
* Scenarios: active / last compare / delta metrics
* Master Data: audit issues / stale sheets / config conflicts / required master gaps

**Files**

* `app.js`

---

## 4.4 Design system unification

### P0.6 Unify card system

**Problem**
The design-system audit explicitly identified fragmented card types, and the current CSS still contains separate `.dash-card` identity instead of one canonical shell family.  

**Required change**

* Create one base card shell for:

  * dashboard panels
  * planning panels
  * material panels
  * alerts
  * list cards
  * tables
* Normalize:

  * header height
  * header padding
  * body padding
  * border
  * radius
  * shadow
* Remove “this page has its own card language” behavior.

**Files**

* `styles.css`
* `index.html`

### P0.7 Unify KPI components

**Problem**
Global metrics, dashboard mini stats, section KPIs, and status counts still feel like separate components.  

**Required change**

* Define one KPI family with variants:

  * top summary KPI
  * sub KPI
  * inline KPI
* Stop visually splitting `.metric` and mini-stat styles into unrelated systems.
* Normalize hierarchy:

  * label
  * value
  * supporting text
  * tone state

**Files**

* `styles.css`
* `index.html`

### P0.8 Unify status tokens

**Problem**
Status chips, badges, warnings, footer statuses, and release states still need a single semantic language. 

**Required change**

* Create one semantic mapping:

  * success
  * warning
  * danger
  * info
  * neutral
* Apply across:

  * footer strip
  * planning pipeline
  * material states
  * execution states
  * dashboard alerts
  * badges and chips

**Files**

* `styles.css`
* `app.js`
* `index.html`

### P0.9 Finish typography and spacing cleanup

**Problem**
The design audit identified too many font sizes, spacing values, radius values, and visual densities. The new stylesheet improved this but did not fully finish the work.  

**Required change**

* Remove stray hard-coded font sizes not aligned to the token scale.
* Remove stray hard-coded gap and padding values.
* Normalize all cards and toolbars to the token scale.
* Improve important-text contrast where faint text is still overused.

**Files**

* `styles.css`

---

## 4.5 Dashboard overhaul

### P0.10 Rework dashboard around one dominant operational story

**Problem**
The dashboard is improved but still feels like multiple stacked panels rather than one coherent cockpit. The to-do explicitly asks for a stronger primary focal area and better constraint visibility. 

**Required change**

* Make the dashboard read as:

  * left = planning status and alerts
  * center = schedule/capacity focal area
  * right = material/release readiness and active work
* Ensure one clearly dominant visual region exists.
* Reduce the sense of “three unrelated columns.”

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P0.11 Surface planning constraints visually, not just textually

**Problem**
Constraint visibility is still weak. The to-do explicitly called out horizon, utilization pressure, lateness, shortages, held status, and capacity risk. 

**Required change**

* Add explicit visual cards or compact bars for:

  * planning horizon pressure
  * late-order pressure
  * material shortage pressure
  * hold pressure
  * bottleneck pressure
  * release readiness

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P0.12 Fully unify long horizontal dashboard cards

**Problem**
Planning Status, Alerts, and Active Orders still risk feeling like slightly different components. 

**Required change**

* Use one long-panel pattern everywhere on the dashboard.
* Standardize:

  * title row
  * action slot
  * meta slot
  * empty state
  * list density

**Files**

* `styles.css`
* `index.html`

---

## 4.6 Material tab redesign

### P0.13 Keep Material as a release-readiness workspace

**Problem**
Material must answer release-readiness, not act like a generic report or campaign browser. This is already the intended product meaning. 

**Required change**

* Ensure the Material page always starts with decision-oriented content:

  * can release?
  * what blocks release?
  * what is the next action?
* Push raw tables lower in the hierarchy.

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P0.14 Add explicit Material granularity modes

**Problem**
The current Material implementation is campaign-centric and there is no first-class heat-level view. The API auto-computes material plan using `detail_level="campaign"`, which is the root reason the UI has no true heat provision. 

**Required change**

* Add a granularity model:

  * Campaign
  * Planning Order
  * Heat
* Keep Campaign as the default mode.
* Make Heat a drill-down or alternate mode, not a hidden derived detail.

**Files**

* `xaps_application_api.py`
* `app.js`
* `index.html`

### P0.15 Extend backend material-plan generation beyond campaign granularity

**Problem**
The frontend cannot honestly show heat-level material readiness until the backend supports it. The current active run artifact creation uses campaign detail by default. 

**Required change**

* Extend `_calculate_material_plan()` or add a new API path so material data can be requested as:

  * campaign-level
  * planning-order-level
  * heat-level
* Include release blockers, shortages, convert requirements, and readiness status at each granularity.

**Files**

* `xaps_application_api.py`

### P0.16 Add Material mode controls to the top action area

**Problem**
If the Material tab gains multiple granularities, the mode switch must live in the top strip, not buried inside page content.

**Required change**

* Add a mode toggle beside the Material refresh action:

  * Campaign
  * PO
  * Heat
* Persist selected mode in frontend state.

**Files**

* `index.html`
* `app.js`
* `styles.css`

### P0.17 Reorganize Material detail hierarchy

**Problem**
Even with improvements, the Material page still needs a stricter decision-first composition.

**Required structure**

1. Verdict hero
2. Recommended actions
3. Plant/stage readiness summary
4. Shortage list
5. Convert/make requirements
6. Supporting material detail table
7. Supporting lineage/explanation metadata

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P0.18 Improve Material grouping and default selection

**Problem**
Material should group work by operational risk and should default to the most urgent/highest-risk item first.

**Required change**

* Group left-side tree/list by:

  * Ready
  * Needs Convert
  * Short
  * Held
* Sort within each group by severity, due date, and release urgency.
* Default selected item should be highest-risk entry, not first in raw order.

**Files**

* `app.js`
* `styles.css`

### P0.19 Make actions explicit on Material page

**Problem**
Material needs obvious “what next?” controls instead of passive reporting.

**Required change**
Add explicit recommended-action states such as:

* Convert
* Expedite
* Substitute
* Release
* Hold
* Investigate shortage
* Wait for upstream completion

**Files**

* `app.js`
* `index.html`
* `styles.css`

---

## 4.7 Execution tab redesign

### P0.20 Organize execution Gantt plant-wise

**Problem**
The to-do explicitly calls for plant-wise Gantts sourced dynamically from config and sequenced in routing order. 

**Required change**

* Group execution timeline by plant.
* Plant ordering must follow routing sequence.
* Default order must reflect upstream to downstream flow, not arbitrary grouping.

**Files**

* `app.js`
* `xaps_application_api.py`
* possibly config-driven support in engine/API

### P0.21 Ensure plant/equipment order follows routing logic

**Problem**
You explicitly called out that plant/equipment arrangement must respect routing sequence, not arbitrary order.

**Required change**

* Use routing-derived operation order where available.
* Fall back to canonical stage order only when routing is incomplete.

**Files**

* `app.js`
* `xaps_application_api.py`
* `engine/scheduler.py` where needed for consistent metadata

### P0.22 Make execution sub-KPIs consistent with the rest of the app

**Problem**
The to-do explicitly called out KPI inconsistency beneath the equipment timeline strip. 

**Required change**

* Replace execution-specific odd KPI styles with the shared KPI family.
* Use the same semantic and visual hierarchy as the rest of the app.

**Files**

* `styles.css`
* `index.html`

### P0.23 Add easy switching between global, SO-wise, PO-wise, and plant-wise execution views

**Problem**
Execution needs multiple valid slices of the same schedule.

**Required change**

* Provide direct switches for:

  * Global timeline
  * Plant-wise
  * SO-wise
  * PO-wise
* Make these first-class subviews, not hidden behavior.

**Files**

* `app.js`
* `index.html`

### P0.24 Make Gantt interaction more cohesive

**Problem**
The to-do explicitly says execution interactivity needs review.

**Required change**

* Standardize:

  * hover detail
  * click-to-pin detail
  * selection highlight
  * zoom behavior
  * empty state
* Ensure timeline and plant views feel related, not separate widgets.

**Files**

* `app.js`
* `styles.css`

---

## 4.8 Bottom status strip

### P0.25 Make bottom strip tab-aware

**Problem**
The to-do explicitly called for right-aligned tab-aware content in the bottom status strip. 

**Required change**

* Show different bottom-strip status content based on active tab.
* Example:

  * Planning: pipeline stage / selected pool / run status
  * BOM: explosion status / structure warnings
  * Material: selected entity readiness / blockers
  * Execution: selected campaign/lot / timeline status
  * Capacity: bottleneck / overload count
  * CTP: last promise result state

**Files**

* `app.js`
* `index.html`

### P0.26 Unify all footer statuses

**Problem**
The to-do explicitly says unify all the various statuses in the bottom strip. 

**Required change**

* Normalize status chips, progress bars, text hierarchy, and severity colors.
* Make the bottom strip feel like the same design system as the top summary and cards.

**Files**

* `styles.css`
* `app.js`

---

# 5. P1 To-Do Items

## 5.1 Layout and page containment

### P1.1 Verify page-content containment across all active pages

**Problem**
Single-page scroll containment was marked complete, but it should be re-audited against the latest active files because layout regressions are common. 

**Required change**

* Check every active page for:

  * fixed header behavior
  * correct scroll containment
  * no nested awkward scrollbars
  * proper flex shrink/grow behavior
* Regressions should be fixed before feature work piles on.

**Files**

* `index.html`
* `styles.css`

---

## 5.2 Planning tab improvements

### P1.2 Make planning page clearly the primary APS workflow

**Problem**
Planning is the core APS page and should visually read that way. The README defines it as the main workflow surface. 

**Required change**

* Make pipeline stages clearer.
* Improve hierarchy between:

  * pool selection
  * planning-order proposal
  * heat derivation
  * simulation
  * release
* Ensure actions and progress feel sequential.

**Files**

* `index.html`
* `app.js`
* `styles.css`

### P1.3 Improve pipeline feedback and progress clarity

**Problem**
Pipeline stage status is present but can be clearer and more productized.

**Required change**

* Stronger visual stage progression
* Better “running / done / blocked / error” semantics
* Better carry-forward of run context into bottom status strip

**Files**

* `app.js`
* `styles.css`

---

## 5.3 BOM tab improvements

### P1.4 Strengthen BOM as total-plan material page

**Problem**
BOM and Material must be clearly differentiated.

**Required change**

* Re-emphasize BOM as total exploded/netted requirement
* Improve grouping by:

  * plant
  * stage
  * material type
  * coverage state
* Improve summary narrative at the top of the page

**Files**

* `app.js`
* `index.html`
* `styles.css`

### P1.5 Improve BOM structure-error visibility

**Problem**
The BOM engine explicitly supports structure errors like cycle and max-level issues. These deserve better UI surfacing. 

**Required change**

* Surface:

  * BOM cycle errors
  * max-level exceeded
  * degraded feasibility due to structure issues
* Show clear warnings in BOM view and bottom status strip.

**Files**

* `xaps_application_api.py`
* `app.js`

---

## 5.4 Capacity tab improvements

### P1.6 Make Capacity page more diagnostic

**Problem**
Capacity should identify where the load sits and what constrains the plan, not just display bars. 

**Required change**

* Highlight:

  * bottleneck resource
  * overloaded group count
  * slack/underutilized assets
  * setup/changeover burden
  * demand vs available hours
* Provide sort/group controls by:

  * plant
  * operation family
  * utilization severity

**Files**

* `app.js`
* `index.html`

### P1.7 Align Capacity semantics with capacity engine outputs

**Problem**
The capacity engine explicitly tracks demand, process, setup, and changeover hours. The UI should expose these meaningfully. 

**Required change**

* Show breakdowns for:

  * Demand_Hrs
  * Process_Hrs
  * Setup_Hrs
  * Changeover_Hrs
  * Task_Count
  * Available hours
* Avoid collapsing all of it into a single utilization bar.

**Files**

* `app.js`
* `xaps_application_api.py`

---

## 5.5 CTP page improvements

### P1.8 Improve explanation quality for CTP outcomes

**Problem**
CTP already supports many decision classes and inventory lineage states; the UI should explain them better. 

**Required change**

* Clearly surface:

  * stock-only promise
  * merged promise
  * new-campaign promise
  * later-date promise
  * material block
  * capacity block
  * inventory-trust degradation
* Show why an answer is blocked, not just that it is blocked.

**Files**

* `xaps_application_api.py`
* `app.js`

### P1.9 Surface inventory lineage trust state in CTP

**Problem**
CTP already differentiates authoritative, recomputed, and conservative-blend inventory lineage states. That matters operationally. 

**Required change**

* Display lineage trust state in UI with severity:

  * authoritative
  * recomputed
  * conservative blend
* Explain implications for planner confidence.

**Files**

* `app.js`
* `xaps_application_api.py`

---

## 5.6 Master Data and Scenarios

### P1.10 Add clearer master-data audit surfacing

**Problem**
Master Data should not just be CRUD. It should expose the health of the workbook-backed system.

**Required change**

* Surface:

  * missing master rows
  * invalid routing
  * config conflicts
  * stale/inactive resources
  * bad queue-time definitions

**Files**

* `xaps_application_api.py`
* `app.js`

### P1.11 Improve scenario comparison UX

**Problem**
Scenarios should support operational comparison, not just persistence.

**Required change**

* Show delta in:

  * planned MT
  * heats
  * on-time %
  * bottleneck load
  * material holds
* Make comparison read as “what changed if I apply this scenario?”

**Files**

* `app.js`
* `index.html`

---

# 6. P2 To-Do Items

## 6.1 Accessibility and interaction polish

### P2.1 Improve keyboard and focus behavior

**Required change**

* Ensure tabs, toggles, segmented controls, and detail panels have visible and consistent focus behavior.
* Improve keyboard navigation across top tabs, subviews, and detail panels.

**Files**

* `index.html`
* `styles.css`
* `app.js`

### P2.2 Improve empty states and instructional copy

**Required change**

* Replace generic empties with context-aware operational guidance:

  * “Run planning pipeline”
  * “No shortages in current run”
  * “No released lots yet”
  * “No capacity overloads found”

**Files**

* `app.js`
* `index.html`

---

## 6.2 Export and traceability

### P2.3 Add export actions for high-value views

**Required change**

* Add export for:

  * BOM net/gross view
  * Material readiness list
  * Capacity table
  * Execution dispatch list
  * CTP result history

**Files**

* `app.js`
* `xaps_application_api.py`

### P2.4 Improve run traceability

**Problem**
The API already has run artifacts and trace IDs. The UI should use them more explicitly. 

**Required change**

* Surface run ID, solver status, degraded flags, and timestamp in a clean diagnostics drawer or footer zone.

**Files**

* `xaps_application_api.py`
* `app.js`

---

# 7. Backend and data-model To-Do

## 7.1 Align Material page with APS planner model

**Problem**
The APS planner defines a correct layered model: `SalesOrder -> PlanningOrder -> HeatBatch -> ScheduledOperation`. Material should align better with this model instead of staying purely campaign-first. 

**Required change**

* Introduce material-readiness mapping for:

  * PlanningOrder
  * HeatBatch
* Keep campaign view where useful, but stop making it the only reality.

**Files**

* `engine/aps_planner.py`
* `xaps_application_api.py`

## 7.2 Clarify coexistence of campaign engine and APS planner

**Problem**
The repo contains both campaign-first and APS planner-first concepts. This creates confusion unless intentionally bridged.  

**Required change**

* Document and enforce where:

  * campaign model is still authoritative
  * planning-order model is authoritative
* Reduce UI ambiguity between PO, campaign, and heat identity.

**Files**

* `README.md`
* `xaps_application_api.py`
* `app.js`

## 7.3 Revisit Excel vs API expectations

**Problem**
Data wiring notes already explain that Excel mode and API mode are architecturally separate, especially for Material. Users will still get confused unless the UI communicates this. 

**Required change**

* Add diagnostics/help text where needed:

  * API mode uses in-memory run artifacts
  * Excel workbook is not auto-written during REST scheduling
* Ensure users do not assume web actions instantly mutate workbook sheets.

**Files**

* `app.js`
* `README.md`

---

# 8. Engine and scheduling To-Do

## 8.1 Scheduling objective improvements

**Problem**
The schedule logic audit identified important optimization weaknesses in lateness balance, idle time, and queue-violation modeling. 

**Required change**

* Rebalance SMS vs RM lateness weighting
* Add idle-time or fragmentation minimization
* Make queue-violation penalties proportional
* Add early-finish incentive where appropriate

**Files**

* `engine/scheduler.py`

## 8.2 Add SMS changeover enforcement

**Problem**
The audit identified that changeovers are enforced only on RM, not SMS equipment. This is a correctness issue. 

**Required change**

* Add changeover constraints across SMS stages where applicable.
* Distinguish transfer time from changeover time more clearly.

**Files**

* `engine/scheduler.py`

## 8.3 Improve campaign grouping logic

**Problem**
The audit identified limitations in campaign split/merge logic and all-or-nothing material holds. 

**Required change**

* Add split-by-urgency heuristic
* Review merge-for-utilization opportunities
* Review partial release vs hard-hold behavior
* Improve priority inheritance consistency

**Files**

* `engine/campaign.py`

## 8.4 Improve CTP ranking logic

**Problem**
CTP alternatives are ranked by precedence but not strongly by feasibility margin. 

**Required change**

* Improve ranking with confidence/feasibility margin scoring.
* Surface better rationale for chosen promise path.

**Files**

* `engine/ctp.py`

---

# 9. Immediate change bundle for the three issues you explicitly raised

## 9.1 Put Material next to BOM

**Status:** Done
**Action:** Reorder tabs in `index.html`.
**Priority:** P0. 

## 9.2 Stop forcing Material into one campaign-only lens

**Status:** Done
**Action:** Add Material granularity modes and backend support for Heat view.
**Priority:** P0. 

## 9.3 Replace the ugly KPI collapse button

**Status:** Done
**Action:** Remove the separate tool row and replace it with a compact anchored handle.
**Priority:** P0.   

---

# 10. Suggested implementation order

## Phase 1 — Structural fixes

1. Reorder tabs
2. Redesign KPI collapse UX
3. Finish top-strip action pattern
4. Unify card/KPI/status system
5. Rework bottom strip into tab-aware status zone

## Phase 2 — Material and Execution correctness

6. Add Material granularity model
7. Extend material-plan backend for PO/Heat views
8. Rebuild Material detail hierarchy
9. Make Execution plant-wise and routing-sequenced
10. Add execution multi-slice views

## Phase 3 — Dashboard and supporting pages

11. Rework dashboard focal structure
12. Improve BOM summary and structure-error surfacing
13. Improve Capacity diagnostics
14. Improve CTP explanation surfaces
15. Improve Scenarios and Master Data visibility

## Phase 4 — Logic and engine improvements

16. Scheduler objective improvements
17. SMS changeover enforcement
18. Campaign grouping refinement
19. CTP ranking refinement

---

# 11. Definition of done

A task should only be considered done when all of the following are true:

* The UI behavior is implemented in the active static app, not just in documentation.
* The design matches the shared design system and does not introduce a page-specific one-off style.
* The bottom strip and top summary remain consistent with the new behavior.
* Empty states, loading states, and degraded/error states are handled.
* The behavior is grounded in the actual API/data model and is not simulated only in frontend state.
* The change does not break page containment or introduce scroll regressions.
* The change does not create a second competing KPI or action system.

---

# 12. Final condensed checklist

## Must do now

* [x] Move Material next to BOM
* [x] Replace current KPI toggle UX
* [x] Finish one true top action strip model
* [ ] Finish one true card/KPI/status design system
* [x] Make bottom strip tab-aware
* [x] Add Material mode switch: Campaign / PO / Heat
* [x] Extend backend material plan beyond campaign-only
* [x] Rebuild Material as verdict-first workspace
* [x] Make Execution plant-wise and routing-sequenced
* [x] Unify execution KPIs with global KPI family

## Should do next

* [x] Rework dashboard into one stronger cockpit
* [x] Improve BOM summary and error surfacing
* [x] Make Capacity more diagnostic
* [x] Improve CTP explanation and trust-state UI
* [x] Improve Master Data audit visibility
* [x] Improve Scenario comparison UX

## Logic backlog

* [x] Improve scheduler objective balance
* [x] Add SMS changeover constraints
* [x] Improve campaign split/merge logic
* [x] Improve CTP ranking quality

---

# 13. Audit pass — 2026-04-11

## What was validated in this pass

* Execution order now uses routing/config sequence before fallback operation order.
* Execution now exposes local sub-KPIs using the same KPI family (`metric` / `kpi-card`).
* Capacity now starts with a decision-first diagnostic strip (peak, overload, high-util, headroom).

## Open findings from this pass (now closed)

| Priority | Area | Finding | Why it matters | Next fix |
| -------- | ---- | ------- | -------------- | -------- |
| P1 | ~~Flow / Material freshness~~ | ~~Material plan is republished on simulate/release/unrelease, but not on propose/derive stage actions.~~ | ~~Planner can adjust planning orders and still see stale material readiness until simulate/refresh cycle.~~ | ~~Publish planning-scoped material snapshot (or explicit stale-state flag) after propose and derive-heats mutations.~~ |
| P1 | ~~Capacity diagnostics depth~~ | ~~Diagnostic strip exists, but detailed burden split (`Process_Hrs`, `Setup_Hrs`, `Changeover_Hrs`, `Task_Count`) is still not surfaced.~~ | ~~Planner sees overload but not the root burden composition needed for corrective action.~~ | ~~Add expandable per-resource burden breakdown in Capacity table/detail panel.~~ |
| P1 | ~~CTP trust/explanation~~ | ~~CTP result messaging remains compact and does not consistently expose decision class + lineage confidence in-page.~~ | ~~Promise decisions are harder to trust operationally during escalation calls.~~ | ~~Add explicit decision-class badge and lineage-confidence badge in CTP result + history rows.~~ |
