# Plan: Turn APS into a Proper Integrated Steel Plant Scheduler

## Context

The current APS has a functional pipeline (campaigns → BOM → capacity → schedule → Excel output) but the core scheduler has a critical flaw: the CP-SAT model **cannot choose which machine to assign work to** — it only optimizes timing on pre-assigned machines. Scenarios are shallow (2 dimensions, 6 KPIs). Output representation is missing material plans, has an unreadable Gantt, and lacks fulfillment metrics. This plan fixes these issues in priority order.

---

## Phase 1: Fix the Core Scheduler (Critical)

### 1A. True Alternate-Machine Assignment in CP-SAT
**File:** `engine/scheduler.py`

**Problem:** `make_interval()` assigns `machine = machine_group[0]` at build time. Solver only decides timing, not placement across 2 EAFs, 3 LRFs, 2 CCMs, 2 RMs.

**Fix:** Replace single intervals with **optional interval variables** per candidate machine:
- For each task, create one `NewOptionalIntervalVar` per machine in the group, gated by a `NewBoolVar`
- `AddExactlyOne` constraint on the Bool vars → exactly one machine selected
- Link optional interval start/end to canonical task start/end via `OnlyEnforceIf`
- Each optional interval goes into its machine's no-overlap list
- After solving, read which Bool is true → that's the assigned machine
- Frozen jobs still pin to their known machine (single mandatory interval)

### 1B. Greedy Fallback Uses All Machines
**File:** `engine/scheduler.py`

**Problem:** `_greedy_fallback()` hardcodes `EAF-01, LRF-01, CCM-01, RM-01` only.

**Fix:** For each operation, pick the machine in the group with the **earliest available clock**: `min(machine_clocks[m] for m in group)`. Simple change, huge impact on greedy schedule quality.

### 1C. SMS Lateness Penalties
**File:** `engine/scheduler.py`

**Problem:** Only RM tasks have lateness variables. SMS operations have no penalty, so solver doesn't care if EAF/LRF/CCM are late.

**Fix:** Add a lateness variable for the last CCM end in each campaign, penalized against `due_date - estimated_RM_duration`. Weight at ~50% of RM weight.

### 1D. Configurable Downtime Window
**File:** `engine/scheduler.py`, `aps_functions.py`, `scenarios/scenario_runner.py`

**Problem:** Downtime is always fixed at minute 0 of the horizon. Real maintenance happens mid-schedule.

**Fix:** Add `machine_down_start_hour` parameter. Read `"Machine Down Start (Hr)"` from Scenarios sheet. Pass through to scheduler. In CP-SAT, use it as the start offset for the downtime blocking interval.

### 1E. Solver Time Limit Control
**File:** `engine/scheduler.py`, `aps_functions.py`

**Fix:** Read `"Solver Time Limit (sec)"` from Scenarios sheet (default 30). Pass to `schedule()`. Allows users to give the solver more time for complex instances with alternate machines.

---

## Phase 2: Use Routing Data (High Impact)

### 2A. Read Operation Durations from Routing Sheet
**Files:** `engine/scheduler.py`, `engine/capacity.py`, `aps_functions.py`, `scenarios/scenario_runner.py`

**Problem:** EAF=90min, LRF=40min, CCM=50/60min are hardcoded constants. Routing sheet data is loaded but never used.

**Fix:**
- New helper `build_operation_times(routing_df, grade) -> dict` that looks up cycle times per operation per grade from the Routing DataFrame. Falls back to current hardcoded defaults.
- `schedule()` and `_greedy_fallback()` accept optional `routing` param, use it for per-grade durations.
- `compute_demand_hours()` accepts optional `routing` param.
- `aps_functions.py` passes `data["routing"]` through all call chains.

### 2B. Activate Changeover Matrix for RM
**Files:** `engine/scheduler.py`, `aps_functions.py`

**Problem:** `Changeover_Matrix` is loaded but never consumed. RM setup is a fixed per-section constant.

**Fix:**
- `schedule()` accepts optional `changeover_matrix` DataFrame.
- Between consecutive RM tasks with different grades, look up changeover time from matrix and add it as a minimum gap constraint (CP-SAT: `Add(prev_rm_end + changeover_minutes <= next_rm_start)`)
- In greedy fallback, add changeover time to clock between grade changes.
- `aps_functions.py` passes `data.get("changeover")` through.

---

## Phase 3: Proper What-If Scenarios

### 3A. Richer Scenario KPIs
**Files:** `scenarios/scenario_runner.py`, `aps_functions.py`

**Problem:** Only 6 KPIs (heats, campaigns, released, held, overloaded, utilisation).

**Fix:** After scheduling, compute additional metrics from results:
- **On-Time %**: `(on-time released campaigns / total released) * 100`
- **Weighted Lateness (hrs)**: Sum of priority-weighted lateness across RM tasks
- **Bottleneck**: Resource with highest utilisation
- **Throughput (MT/day)**: Total released MT / horizon days
- **Avg Lead-Time Margin (hrs)**: Average `(due_date - RM_end)` for on-time campaigns

Update `SCENARIO_OUTPUT_HEADERS` to include new columns.

### 3B. More Scenario Dimensions
**Files:** `scenarios/scenario_runner.py`, `aps_functions.py`, `build_template_v3.py`

**Fix:** Read additional parameters from Scenarios sheet:
- `"Yield Loss (%)"`: Reduce RM/CCM yield in campaign calculations
- `"Rush Order MT"`: Inject a high-priority rush order
- `"Machine Down Start (Hr)"`: When downtime begins (pairs with existing down hours)
- `"Extra Shift Hours"`: Add hours to all resources for overtime scenario

Generate 6-8 scenarios from these dimensions when populated. Keep existing 4 as base for backward compat.

Add the new parameter rows to `build_template_v3.py` Scenarios sheet.

---

## Phase 4: Output Representation

### 4A. Material Plan Sheet
**File:** `aps_functions.py`

**Fix:** New function `_render_material_plan(wb, campaigns)` creates a `Material_Plan` sheet showing per-campaign material consumption:
- Columns: Campaign_ID, Grade, Material_SKU, Material_Name, Required_Qty, Available_Before, Consumed, Remaining_After, Status
- Released campaigns show consumed quantities; held campaigns show shortages highlighted
- Called from `run_schedule_for_workbook()` after scheduling

### 4B. Campaign Gantt (Replace Task-Level Gantt)
**File:** `aps_functions.py`

**Problem:** Current Gantt is 56+ columns, one row per task — unreadable.

**Fix:** Redesign `_render_schedule_gantt()`:
- **Resource swim-lanes**: One row per (Resource_ID) with campaign bars inside
- **4-hour buckets** (still manageable width)
- **Campaign-colored bars**: Each campaign gets a consistent color across all its resources
- **Summary row per resource**: Show utilisation % as a number
- This makes the Gantt actually usable for identifying bottlenecks and idle gaps

### 4C. Enhanced KPI Dashboard
**File:** `aps_functions.py`

**Fix:** Add to `_refresh_kpi_dashboard()`:
- **Lead-Time Margin** block: min/avg/max `(due_date - RM_end)` for released campaigns
- **Bottleneck Ranking**: Top 3 resources by utilisation in a mini-table
- **Throughput chart**: Cumulative MT over time (stepped by campaign RM_End)
- **Fulfillment Rate** tile in the scorecard

### 4D. Scenario Comparison Improvements
**File:** `aps_functions.py`

**Fix:** The scenario output chart should show the new KPIs (On-Time %, Throughput, Bottleneck). Add a second chart: radar/spider chart with On-Time %, Utilisation, Throughput, Lead-Time Margin per scenario (approximated as a grouped bar if radar is complex in Excel COM).

---

## Implementation Order

| Step | What | File(s) | Effort | Impact |
|------|------|---------|--------|--------|
| 1 | Alternate machine CP-SAT | scheduler.py | High | **Critical** |
| 2 | Greedy uses all machines | scheduler.py | Low | High |
| 3 | SMS lateness penalties | scheduler.py | Low | High |
| 4 | Routing-based durations | scheduler.py, capacity.py, aps_functions.py | Medium | High |
| 5 | Changeover matrix active | scheduler.py, aps_functions.py | Medium | Medium |
| 6 | Configurable downtime | scheduler.py, aps_functions.py, scenario_runner.py | Low | Medium |
| 7 | Solver time limit | scheduler.py, aps_functions.py | Low | Low |
| 8 | Richer scenario KPIs | scenario_runner.py, aps_functions.py | Medium | High |
| 9 | More scenario dimensions | scenario_runner.py, aps_functions.py, build_template_v3.py | Medium | Medium |
| 10 | Material plan sheet | aps_functions.py | Medium | Medium |
| 11 | Campaign Gantt redesign | aps_functions.py | Medium | High |
| 12 | KPI dashboard enhancements | aps_functions.py | Medium | Medium |
| 13 | Scenario comparison improvements | aps_functions.py | Low | Medium |

Steps 1-3 are the scheduler core — must go first and together.
Steps 4-5 are routing data activation — independent of scheduler core.
Steps 6-7 are small parameter additions.
Steps 8-13 are output/scenario improvements — independent of each other.

---

## Files Modified

| File | Changes |
|------|---------|
| `engine/scheduler.py` | Steps 1-7: Alternate machines, greedy fix, SMS lateness, routing durations, changeover, downtime, solver limit |
| `engine/capacity.py` | Step 4: Accept routing param, use per-grade durations |
| `scenarios/scenario_runner.py` | Steps 6, 8, 9: Downtime passthrough, richer KPIs, more dimensions |
| `aps_functions.py` | Steps 4-13: Parameter passthrough, material plan, Gantt redesign, KPI enhancements, scenario improvements |
| `build_template_v3.py` | Step 9: Add new Scenario parameter rows to template |

## Verification

After each phase:
1. `python build_template.py` — regenerate workbook (only needed if template changed)
2. `python run_all.py all` — run full pipeline, verify no errors
3. Check `Schedule_Output` — verify multiple machines appear (EAF-01 AND EAF-02, etc.)
4. Check `Campaign_Schedule` — verify campaigns are released/held correctly
5. Check `Scenario_Output` — verify new KPI columns populated
6. Check `Material_Plan` sheet exists and shows per-campaign material usage
7. Check `Schedule_Gantt` — verify campaign-level bars are readable
8. Check `KPI_Dashboard` — verify new tiles and charts
