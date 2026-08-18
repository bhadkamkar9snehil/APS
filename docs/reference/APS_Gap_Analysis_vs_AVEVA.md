# Gap Analysis: Custom Steel APS vs AVEVA APS (PlanetTogether)

**Date:** April 2026
**Scope:** Integrated Steel Plant APS — BF + SMS + Rolling Mills
**Reference:** AVEVA Advanced Planning and Scheduling for AVEVA MES (Presentation)

---

## Executive Summary

Our custom APS (Excel + Python + OR-Tools CP-SAT) covers the core finite-capacity scheduling loop for an integrated steel plant. It correctly models campaigns, BOM explosion, CP-SAT optimization, and scenario analysis. However, AVEVA APS (PlanetTogether) is a mature commercial platform with a significantly broader constraint model, real-time replanning, enterprise integration, and multi-plant capability.

This report identifies **19 capability gaps** across 6 dimensions. Of these:
- **4 are Critical** — gaps that directly limit planning accuracy in a steel plant
- **7 are High** — gaps that limit operational usability and operator trust
- **5 are Medium** — enhancements that improve planning quality over time
- **3 are Low / Out-of-scope** — reasonable to exclude from an Excel+Python system

---

## Section 1: Constraint Model Comparison

| Constraint Type | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| Machine/Resource capacity | Yes — Resources with Capabilities + Attributes | Yes — EAF/LRF/CCM/RM pools via CP-SAT | **None** | On par for core machine capacity |
| Alternate machine selection | Yes — rule-based + optimized | Yes — `NewOptionalIntervalVar` + `AddExactlyOne` | **None** | Implemented via CP-SAT |
| Routing / Operation sequences | Yes — Routes with predecessor/successor | Yes — fixed EAF→LRF→VD→CCM→RM sequence | **Medium** | We have hard-coded sequence; AVEVA supports arbitrary DAG routes |
| Overlapping / parallel operations | Yes — operations can overlap within a job | No — strictly sequential per heat | **High** | LRF heating can overlap EAF tap-to-tap in some steel plants |
| Setup / Changeover times | Yes — matrix-based per resource | Yes — grade-to-grade changeover matrix on RM | **Low** | SMS changeover not yet modeled; AVEVA handles all resources |
| Queue times (wait between ops) | Yes — min/max queue time constraints | No | **Critical** | Steel: liquid steel has max queue time (temperature loss); missing this causes infeasible plans |
| Transfer times (logistics lag) | Yes — configurable per route step | No | **High** | CCM→RM transfer time not modeled; plans can show RM starting before billet arrives |
| Labor constraints | Yes — Labor resources, shifts, certifications | No | **High** | Crane operators, furnace operators not capacity-constrained |
| Tank / Buffer / Inventory modeling | Yes — Tank resources with min/max fill levels | No | **Critical** | Hot metal ladle tracking, torpedo car balancing not modeled |
| Machine downtime | Yes — planned and unplanned, real-time | Yes — single machine, configurable start hour | **Medium** | We support one downtime window; AVEVA supports multiple, concurrent, per-shift |
| Overtime / extra shifts | Yes — Overtime Intervals tool, automatic optimization | Partial — extra_shift_hours as flat add | **Medium** | AVEVA can optimize which overtime windows to use; we just add hours globally |

---

## Section 2: Optimization and Scheduling Engine

| Capability | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| Optimization engine | GPS-like dynamic solver + TOS (Theory of Scheduling) | OR-Tools CP-SAT | **None** | Both use constraint-based optimization |
| TOC / Buffered Release | Yes — drum-buffer-rope scheduling, automatic buffer sizing | No | **High** | TOC-based release prevents WIP starvation at bottleneck; our system releases all campaigns simultaneously |
| Bottleneck identification | Yes — Buffer Management Workbench, automatic avoidance | Partial — bottleneck KPI in scenario output | **Medium** | We identify bottleneck but don't automatically resequence to protect it |
| Optimization objective sliders | Yes — slider-based weighting (OTD vs throughput vs cost) | No — fixed penalty weights in code | **Medium** | AVEVA allows planners to tune weights without touching code; our weights are hardcoded |
| Frozen job support | Yes — lock any operation | Yes — `frozen_jobs` parameter in scheduler | **None** | Implemented |
| Greedy fallback | N/A — commercial solver handles infeasibility | Yes — greedy fallback if CP-SAT fails | **None** | Our approach is appropriate |
| Multi-objective: cost, margin | Yes — cost-based sequencing, margin tracking | Partial — `avg_margin_hrs` KPI only | **Medium** | We report margin but don't optimize it |

---

## Section 3: What-If Scenario Planning

| Capability | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| Scenario isolation | Yes — unlimited sandboxed scenarios, never touch live plan | Partial — scenarios rerun full solver each time | **High** | Our scenarios are completely isolated computationally (good), but they overwrite the Scenario_Output sheet rather than being truly side-by-side persistent |
| Number of scenarios | Unlimited, named, persistent | 1 at a time (row per scenario dimension) | **High** | We run a grid of dimensions but cannot save and compare scenario A vs B vs C over sessions |
| Scenario dimensions | Arbitrary demand, resource, inventory, routing changes | 7 dimensions: demand spike, machine down, yield loss, rush order, extra shift, down start hr, solver limit | **Medium** | Our dimensions cover the most common cases but lack cost/price changes, routing alternates, multi-plant shifts |
| CTP (Capable-to-Promise) | Yes — promise a delivery date against real constraints | No | **Critical** | Planners cannot answer "when can I promise delivery of X?" without running a full schedule |
| Scenario comparison UI | Side-by-side visual Gantt comparison | Tabular KPI grid only | **High** | Our Scenario_Output shows numbers; AVEVA shows visual plan differences |

---

## Section 4: Demand Planning and Supply

| Capability | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| Demand input | Sales Orders + Forecasts + Safety Stock | Sales Orders only (SO sheet) | **High** | No forecast horizon beyond confirmed SOs; no safety stock replenishment logic |
| MPS / MRP | Yes — Master Production Schedule layer + MRP netting | BOM explosion netting only | **Medium** | We net against inventory but don't generate replenishment purchase orders |
| Purchase order awareness | Yes — outstanding POs consumed as supply | No | **Medium** | Incoming raw material POs not considered; plan may show false shortages |
| Safety stock | Yes — demand planning with safety stock buffers | No | **Medium** | No minimum inventory floor enforced during material commit |
| Shortage alerts with drill-down | Yes — real-time shortage alerts, pegged to specific SO | Partial — SHORTAGE flag in Material_Plan | **High** | We flag shortages but don't peg them back to which SOs are at risk |

---

## Section 5: Visualization and User Interaction

| Capability | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| Interactive Gantt | Yes — drag-and-drop, constraint-based, real-time feedback | Read-only swim-lane in Schedule_Gantt sheet | **High** | Excel Gantt cannot be interactive; this is an inherent Excel limitation |
| Real-time replanning | Yes — GPS-like, rescheduling on every change | Batch only — must click Run Schedule button | **High** | Changes to SOs or machine status require manual re-run |
| Dispatch lists | Yes — operator-facing dispatch list per work center | No | **Medium** | No shop-floor-facing view extracted from plan |
| KPI dashboard | Yes — customizable KPI views | Yes — KPI_Dashboard sheet with 12 KPIs | **Low** | Our dashboard is good; AVEVA's is more customizable |
| Campaign / heat visibility | Yes — heat-level drill-down in Gantt | Partial — Schedule_Output shows heats | **Low** | With the planned formatting improvements, our visibility will be adequate |

---

## Section 6: Integration and Data Flow

| Capability | AVEVA APS | Our APS | Gap Severity | Notes |
|---|---|---|---|---|
| MES integration | Yes — bidirectional with AVEVA MES; actuals fed back in real-time | None — Theo_vs_Actual column is placeholder | **Critical** | No actual production feedback; plan diverges from reality after Day 1 without manual updates |
| ERP integration | Yes — SAP/Oracle BOM, SO, inventory sync | None — manual data entry in Excel sheets | **High** | All data must be manually maintained in the workbook |
| Supply chain integration | Yes — supplier lead times, PO statuses | None | **Low** | Out of scope for plant-level APS |
| Multi-plant planning | Yes — cross-plant scheduling with transfer times | Single plant only | **Low** | Single-plant scope is appropriate for current use case |
| API / web services | Yes — REST API for ERP/MES integration | None | **Low** | Out of scope for Excel+Python |

---

## Section 7: Prioritized Gap Closure Roadmap

### Tier 1 — Critical (Address in next development cycle)

| # | Gap | Recommended Fix | Effort |
|---|---|---|---|
| C1 | **Queue times** (max time between SMS ops — temperature limits) | Add `max_queue_min` parameter per route step pair in Routing sheet; enforce in CP-SAT with `AddNoOverlap` or explicit `model.Add(start[next] <= end[prev] + max_queue)` | Medium |
| C2 | **CTP (Capable-to-Promise)** | Add a `run_ctp(so_id, qty, grade)` function that runs a targeted schedule for just that SO, returns earliest feasible end date | Medium |
| C3 | **MES feedback / actual tracking** | Add an "Actuals" sheet where operators log heat start/end times; `aps_functions.py` reads actuals and freezes completed operations as frozen_jobs, reoptimizes remaining | High |
| C4 | **Tank/ladle buffer constraints** | Add torpedo car and ladle availability as resource pools in CP-SAT (similar to EAF/LRF pools); track hot metal transport capacity | High |

### Tier 2 — High (Address within 2–3 development cycles)

| # | Gap | Recommended Fix | Effort |
|---|---|---|---|
| H1 | **Transfer times** (CCM→RM lag) | Add `Transfer_Time_Min` column to Routing sheet per operation transition; enforce as minimum gap in scheduler | Low |
| H2 | **Scenario persistence** | Save each scenario run as a named sheet (Scenario_A, Scenario_B, etc.) and maintain a scenario registry in a hidden sheet | Medium |
| H3 | **Shortage pegging to SOs** | In `simulate_material_commit()`, track which SOs are affected by each shortage and surface in BOM_Output and Material_Plan | Medium |
| H4 | **Forecast demand** | Add a Forecast sheet; `campaign.py` considers both confirmed SOs and forecast-driven planned orders | High |
| H5 | **TOC buffered release** | Identify bottleneck resource; release campaigns in sequence gated by bottleneck capacity buffer, not all-at-once | High |
| H6 | **Labor constraints** | Add a Labor sheet (operators, shifts, certifications); integrate as resource pool in CP-SAT | High |
| H7 | **Shortage alerts with drill-down** | Add a Shortage_Alert sheet that shows: Material → Shortage Qty → Affected Campaign → Affected SO → Due Date impact | Low |

### Tier 3 — Medium (Future enhancements)

| # | Gap | Recommended Fix | Effort |
|---|---|---|---|
| M1 | **Overlapping operations** | Allow LRF heating to run concurrently with next EAF heat tap-to-tap via overlapping optional intervals | High |
| M2 | **Optimization weight sliders** | Add a Tuning section to Control_Panel (penalty weights for OTD, throughput, cost); read in `aps_functions.py` before calling scheduler | Low |
| M3 | **SMS changeover matrix** | Extend changeover_matrix to EAF and CCM (grade-dependent setup); read from a Changeover sheet | Low |
| M4 | **Purchase order supply** | Add a PO_Supply sheet; `bom_explosion.py` nets PO incoming supply against gross requirements | Medium |
| M5 | **Safety stock floors** | Add Min_Stock column to Inventory sheet; material commit respects floor | Low |

### Tier 4 — Out of Scope / Accept as Limitation

| # | Gap | Reason to Accept |
|---|---|---|
| L1 | Drag-and-drop interactive Gantt | Fundamental Excel limitation; would require a web UI rewrite |
| L2 | Real-time replanning | Excel+Python is inherently batch; acceptable if Run Schedule is fast (<60s) |
| L3 | Multi-plant planning | Single-plant scope is correct for this deployment |

---

## Section 8: Feature Coverage Summary

```
Capability Area                    Our APS     AVEVA APS     Coverage
─────────────────────────────────────────────────────────────────────
Core machine capacity               ████████    ██████████    80%
Alternate machine selection         ██████████  ██████████   100%
Routing / operation sequence        ███████     ██████████    70%
Queue & transfer times              ██          ██████████    20%
Labor constraints                   ░░░░░░      ██████████     0%
Tank / buffer modeling              ░░░░░░      ██████████     0%
BOM explosion & netting             ████████    ██████████    80%
Campaign planning                   █████████   ██████████    90%
What-if scenarios                   ██████      ██████████    60%
CTP                                 ░░░░░░      ██████████     0%
Forecast / safety stock             ██          ██████████    20%
Shortage alerts & pegging           ████        ██████████    40%
Visual Gantt                        ████        ██████████    40%
MES integration / actuals           █           ██████████    10%
ERP integration                     ░░░░░░      ██████████     0%
Bottleneck management               ████        ██████████    40%
TOC / buffered release              ░░░░░░      ██████████     0%
─────────────────────────────────────────────────────────────────────
Overall coverage estimate           ~50%        100%
```

---

## Section 9: Conclusions

Our custom APS is **well-suited for its stated purpose**: finite-capacity scheduling for a single integrated steel plant with campaign-based grouping, CP-SAT optimization, and Excel-based planning. The core scheduling engine is production-grade.

The most dangerous gaps — gaps that could cause the plan to be **physically infeasible or misleading** — are:

1. **No queue time constraints**: Liquid steel will solidify if held too long between EAF tap and CCM casting. The current system could schedule a heat with a 4-hour gap between CCM start and LRF end, which is operationally impossible. This must be fixed first.

2. **No MES actuals feedback**: After Day 1, any plan deviation (delayed heat, equipment breakdown, yield loss) is invisible to the system. The plan becomes stale immediately without a feedback mechanism.

3. **No CTP**: Planners currently cannot answer customer delivery queries without running a full manual scenario. A targeted CTP function would provide this in seconds.

4. **No transfer time enforcement**: The plan can show RM rolling starting before the billet physically arrives from CCM, especially when they are in different bays.

The remaining gaps are meaningful quality-of-life improvements (labor, scenarios, forecasting) that mirror AVEVA's commercial advantages but are not critical blockers for the current operating environment.

---

*Report generated from AVEVA APS product presentation (21 slides) cross-referenced against current APS codebase (engine/scheduler.py, engine/campaign.py, engine/bom_explosion.py, aps_functions.py, build_template_v3.py).*
