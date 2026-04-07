# APS Algorithm Configuration Tuning Guide

**Complete reference for all 47 Algorithm_Config parameters**

All parameters are stored in the `Algorithm_Config` sheet of `APS_BF_SMS_RM.xlsx` and loaded automatically on system startup.

---

## SCHEDULER CATEGORY (16 parameters)

### Machine Cycle Times

#### CYCLE_TIME_EAF_MINUTES
- **Current Value:** 90 minutes
- **Valid Range:** 30-180 minutes
- **Effect:** Time for EAF to heat one charge (affects campaign heat duration, schedule makespan, queue wait times)
- **Tuning Guide:**
  - **Decrease if:** You want faster scheduling (aggressive targets)
  - **Increase if:** Actual furnace heat times are longer (process change, higher throughput targets)
  - **Rule of thumb:** Match to real process data ±10%

#### CYCLE_TIME_LRF_MINUTES
- **Current Value:** 40 minutes
- **Valid Range:** 20-100 minutes
- **Effect:** LRF refining time per charge; affects scheduling between EAF and VD
- **Tuning Guide:**
  - Typically 30-50% of EAF cycle time
  - Tune when ladle metallurgy changes or throughput shifts

#### CYCLE_TIME_VD_MINUTES
- **Current Value:** 45 minutes
- **Valid Range:** 30-90 minutes
- **Effect:** VD vacuum processing time; critical for quality grades
- **Tuning Guide:**
  - Shorter for commodity grades (e.g., 40 min)
  - Longer for specialty grades requiring extended vacuum (e.g., 60 min)
  - Used only for VD_REQUIRED_GRADES

#### CYCLE_TIME_CCM_130_MINUTES
- **Current Value:** 50 minutes
- **Valid Range:** 30-90 minutes
- **Effect:** Casting time for BIL-130 (lower carbon) billets
- **Tuning Guide:**
  - 130mm billets typically 45-55 min
  - Increase if casting quality issues
  - Decrease if machinery upgraded

#### CYCLE_TIME_CCM_150_MINUTES
- **Current Value:** 60 minutes
- **Valid Range:** 40-100 minutes
- **Effect:** Casting time for BIL-150 (higher carbon/alloy) billets
- **Tuning Guide:**
  - Larger billets take ~10-15 min longer than BIL-130
  - Alloy content increases time (CrMo 4140 may need +5 min vs SAE 1065)

### Queue Violation Penalties

#### OBJECTIVE_QUEUE_VIOLATION_WEIGHT
- **Current Value:** 500 points
- **Valid Range:** 1-10000 points
- **Effect:** Penalty for violating queue time rules (min/max queue before operation)
- **Tuning Guide:**
  - **HIGH (1000+):** Strict queue compliance required (JIT, quality-sensitive)
  - **MEDIUM (300-700):** Standard manufacturing (current setting)
  - **LOW (1-100):** Flexible queuing (high WIP tolerance)
  - **Decision:** Increase if material degradation is a problem; decrease if queue times block opportunities

### Priority Weights

#### PRIORITY_WEIGHT_URGENT
- **Current Value:** 4 points per unit
- **Valid Range:** 1-10
- **Effect:** Multiplier for URGENT priority orders in objective function
- **Tuning Guide:**
  - Should be highest weight (typically 3-5x normal)
  - Current: 4x NORMAL (which is 1)
  - Increase if URGENT orders are slipping

#### PRIORITY_WEIGHT_HIGH
- **Current Value:** 3 points per unit
- **Valid Range:** 1-9
- **Effect:** Multiplier for HIGH priority orders
- **Tuning Guide:**
  - Typically 2-4x NORMAL
  - Current: 3x NORMAL

#### PRIORITY_WEIGHT_NORMAL
- **Current Value:** 2 points per unit
- **Valid Range:** 1-5
- **Effect:** Default priority weight for standard orders

#### PRIORITY_WEIGHT_LOW
- **Current Value:** 1 point per unit
- **Valid Range:** 1-3
- **Effect:** Weight for LOW priority orders
- **Tuning Guide:**
  - Should be minimum non-zero weight
  - Current setting balances throughput vs. priority

### Solver Configuration

#### SOLVER_TIME_LIMIT_SECONDS
- **Current Value:** 60 seconds
- **Valid Range:** 1-600 seconds
- **Effect:** Maximum time OR-Tools CP-SAT solver runs
- **Tuning Guide:**
  - **SHORT (<10s):** Fast decisions, may miss optimal (prototyping)
  - **MEDIUM (30-120s):** Balance of quality and speed (current)
  - **LONG (>300s):** Near-optimal solutions (batch planning mode)

#### SOLVER_LOG_SEARCH_PROGRESS
- **Current Value:** Y (Yes, log enabled)
- **Valid Options:** Y/N
- **Effect:** Whether CP-SAT logs search progress to console/logs
- **Tuning Guide:**
  - Enable (Y) for debugging solver behavior
  - Disable (N) for production to reduce log volume

#### SOLVER_RELATIVE_GAP_TOLERANCE
- **Current Value:** 0.05 (5%)
- **Valid Range:** 0.0-1.0
- **Effect:** Acceptable gap from mathematical optimum (0.0 = exact, 1.0 = any)
- **Tuning Guide:**
  - 0.05 (5%) = "good enough" for most manufacturing
  - 0.01 (1%) = near-optimal (slower, for critical decisions)
  - 0.2 (20%) = fast heuristic (for large problems)

#### SOLVER_ABSOLUTE_GAP_TOLERANCE
- **Current Value:** 0.0
- **Valid Range:** 0.0-1000.0
- **Effect:** Absolute improvement threshold for continuing search
- **Tuning Guide:**
  - Usually 0.0 (disabled) when using relative gap
  - Set if you know acceptable absolute improvement (e.g., reduce makespan by >10 minutes)

---

## CAMPAIGN CATEGORY (14 parameters)

### Batch Sizing

#### HEAT_SIZE_MT
- **Current Value:** 50 MT
- **Valid Range:** 10-200 MT
- **Effect:** Standard EAF heat size; used when batch qty not specified
- **Tuning Guide:**
  - **SMALL (10-30 MT):** Flexible, frequent campaigns (high SKU variety)
  - **MEDIUM (40-60 MT):** Current, balanced (standard steel plant)
  - **LARGE (80-120 MT):** High throughput, fewer campaigns (commodity focus)
  - **Decision:** Increase if furnace efficiency drops with smaller heats; decrease if queue times are high

#### CAMPAIGN_MIN_QUANTITY_MT
- **Current Value:** 10 MT
- **Valid Range:** 1-100 MT
- **Effect:** Minimum campaign size before creating new heat
- **Tuning Guide:**
  - Should be ~20% of HEAT_SIZE_MT
  - Prevent tiny leftover campaigns (waste)
  - Below this, campaigns are held or merged

#### CAMPAIGN_MAX_QUANTITY_MT
- **Current Value:** 250 MT
- **Valid Range:** HEAT_SIZE_MT to 500 MT
- **Effect:** Maximum campaign size before splitting into multiple campaigns
- **Tuning Guide:**
  - Prevent monster campaigns from tying up long sequences
  - Current: 5x HEAT_SIZE_MT (typical for batch campaigns)
  - Increase if splitting causes queue violations

### Material Yields

#### YIELD_CCM_PCT
- **Current Value:** 95% (0.95)
- **Valid Range:** 0.80-1.00
- **Effect:** Casting yield (scrap loss from ingot → billet)
- **Tuning Guide:**
  - 90-95% typical for steel casting
  - 95%+ = optimized casting (low scrap)
  - <90% = casting problems (quality, breakage)
  - Drives BOM billet requirements upward

#### YIELD_RM_5.5MM_PCT
- **Current Value:** 89% (0.89)
- **Valid Range:** 0.70-0.95
- **Effect:** Rolling mill yield for 5.5mm section
- **Tuning Guide:**
  - Thinner sections have lower yield (crop losses, twist defects)
  - 5.5mm is thin → 85-90% typical
  - Used for FG-WR-*-55 SKUs

#### YIELD_RM_6.0MM_PCT
- **Current Value:** 90% (0.90)
- **Valid Range:** 0.72-0.96
- **Effect:** Rolling mill yield for 6.0mm section

#### YIELD_RM_6.5MM_PCT
- **Current Value:** 90.5% (0.905)
- **Valid Range:** 0.75-0.98
- **Effect:** Rolling mill yield for 6.5mm section

#### YIELD_RM_7.0MM_PCT
- **Current Value:** 91% (0.91)
- **Valid Range:** 0.80-0.99
- **Effect:** Rolling mill yield for 7.0mm section

#### YIELD_RM_8.0MM_PCT
- **Current Value:** 92% (0.92)
- **Valid Range:** 0.85-0.99
- **Effect:** Rolling mill yield for 8.0mm section

#### YIELD_RM_10.0MM_PCT
- **Current Value:** 93% (0.93)
- **Valid Range:** 0.88-0.99
- **Effect:** Rolling mill yield for 10.0mm section

#### YIELD_RM_12.0MM_PCT
- **Current Value:** 94% (0.94)
- **Valid Range:** 0.90-0.99
- **Effect:** Rolling mill yield for 12.0mm section
- **Tuning Guide (all RM yields):**
  - Thicker sections have higher yield (less crop loss)
  - Grade affects yield (specialty < commodity)
  - Tune if actual scrap data changes

#### YIELD_RM_DEFAULT_PCT
- **Current Value:** 89% (0.89)
- **Valid Range:** 0.70-0.95
- **Effect:** Fallback for sections not explicitly configured
- **Tuning Guide:**
  - Used for unlisted sections or when demo mode enabled
  - Conservative (lower) to avoid material shortages

### Material Rules

#### VD_REQUIRED_GRADES
- **Current Value:** ['1080', 'CHQ1006', 'CrMo4140']
- **Type:** Comma-separated list of grade IDs
- **Effect:** Grades that must go through VD (vacuum degasser)
- **Tuning Guide:**
  - Add grades if vacuum processing required (e.g., low hydrogen)
  - Remove if process changes eliminate VD need
  - Each added grade increases plan complexity (VD bottleneck)

#### LOW_CARBON_BILLET_GRADES
- **Current Value:** ['1008', '1018', '1035']
- **Type:** Comma-separated list of grade IDs
- **Effect:** Grades that use BIL-130 (low carbon billet); others use BIL-150
- **Tuning Guide:**
  - Add grades if they fit BIL-130 spec
  - Remove if billet sourcing changes
  - Affects which billet inventory is consumed

---

## BOM CATEGORY (7 parameters)

### Yield Bounds

#### YIELD_MIN_BOUND_PCT
- **Current Value:** 1% (0.01)
- **Valid Range:** 0.001-0.5
- **Effect:** Minimum realistic yield (clamp floor)
- **Tuning Guide:**
  - Prevents implausible yields like 0.001% (1:100000 loss)
  - 0.01 (1%) = conservative minimum
  - Increase if actual processes have extreme variability

#### YIELD_MAX_BOUND_PCT
- **Current Value:** 100% (1.0)
- **Valid Range:** 0.5-1.0
- **Effect:** Maximum realistic yield (no surplus)
- **Tuning Guide:**
  - Always 1.0 (can't exceed 100%)
  - Do not modify

### Material Flow Types

#### INPUT_FLOW_TYPES
- **Current Value:** ['INPUT', 'MAKE']
- **Type:** Comma-separated list
- **Effect:** BOM flow types treated as inputs (material consumed)
- **Tuning Guide:**
  - Standard: INPUT (purchased) + MAKE (made in-house)
  - Add new types if materials flow differently (e.g., LEASE, SCRAP)
  - Drives inventory netting logic

#### BYPRODUCT_FLOW_TYPES
- **Current Value:** ['BYPRODUCT', 'SCRAP', 'WASTE']
- **Type:** Comma-separated list
- **Effect:** BOM flow types treated as outputs (material produced)
- **Tuning Guide:**
  - Used for waste tracking and recovery credits
  - Add if process creates recovery streams

### Byproduct Timing

#### BYPRODUCT_INVENTORY_MODE
- **Current Value:** deferred
- **Valid Options:** deferred | immediate
- **Effect:** When byproducts become available
- **Tuning Guide:**
  - **deferred:** Byproduct available after full production (typical)
  - **immediate:** Byproduct available as produced (scrap sale opportunities)

### Material Tolerance

#### ZERO_TOLERANCE_THRESHOLD
- **Current Value:** 0.000001 (1e-6 MT)
- **Valid Range:** 1e-9 to 0.001
- **Effect:** Quantities below this are treated as zero
- **Tuning Guide:**
  - Prevents false "shortage" on 0.000001 MT discrepancies
  - Current: 1 gram tolerance (very tight)
  - Increase to 0.001 (1 kg) if tracking grain lots, etc.

---

## CTP CATEGORY (6 parameters)

### Promise Scoring

#### CTP_SCORE_STOCK_ONLY
- **Current Value:** 60 points
- **Valid Range:** 10-100 points
- **Effect:** Score bonus for promising from stock (highest-confidence)
- **Tuning Guide:**
  - Should be highest score (good for customer satisfaction)
  - Increase if you want to prioritize "ship-from-shelf" strategy
  - Current: 60 vs 10 (merge) vs 4 (new) = clear hierarchy

#### CTP_SCORE_MERGE_CAMPAIGN
- **Current Value:** 10 points
- **Valid Range:** 1-50 points
- **Effect:** Score bonus for merging into existing campaign
- **Tuning Guide:**
  - Mid-range (less desirable than stock, more than new campaign)
  - Increase if merge efficiency is high
  - Current: 2.5x new campaign score

#### CTP_SCORE_NEW_CAMPAIGN
- **Current Value:** 4 points
- **Valid Range:** 1-20 points
- **Effect:** Score bonus for creating new campaign
- **Tuning Guide:**
  - Lowest score (least desirable, triggers new equipment usage)
  - Increase if new campaigns are easy (excess capacity)
  - Decrease if new campaigns are expensive (resource constraints)

#### CTP_MERGEABLE_SCORE_THRESHOLD
- **Current Value:** 55 points
- **Valid Range:** 10-90 points
- **Effect:** Minimum score to consider merge viable
- **Tuning Guide:**
  - Below this score → merge rejected (must create new campaign)
  - Should be <60 (stock score) but >4 (new score)
  - Increase if merging is risky (quality, scheduling conflicts)
  - Current: 55/60 = 92% of stock score (high bar)

### Promise Penalties

#### CTP_MERGE_PENALTY
- **Current Value:** 1 (dimensionless cost)
- **Valid Range:** 0-10
- **Effect:** Penalty applied if merge option not selected
- **Tuning Guide:**
  - Currently unused (not applied in scoring, reserved for future)
  - When active: penalty for missed merge opportunities
  - Increase to discourage leaving capacity unused

#### CTP_INVENTORY_ZERO_TOLERANCE
- **Current Value:** 1e-9 (0.000000001 MT)
- **Valid Range:** 1e-12 to 1e-3
- **Effect:** Quantities below this are treated as zero in promise logic
- **Tuning Guide:**
  - Prevents false "no inventory" on rounding errors
  - Current: 1 nanogram (extremely tight, assume any measurement is real)
  - Increase to 0.001 (1 kg) if measurement uncertainty is significant

---

## CAPACITY CATEGORY (3 parameters)

### Planning Horizon

#### CAPACITY_HORIZON_DAYS
- **Current Value:** 14 days
- **Valid Range:** 7-90 days
- **Effect:** Time window for capacity analysis
- **Tuning Guide:**
  - **SHORT (7-10 days):** Near-term focus (immediate bottleneck)
  - **MEDIUM (14-21 days):** Standard capacity review (current)
  - **LONG (30-90 days):** Strategic planning (constraint analysis)
  - Decision: Shorter if frequent replanning; longer if stable

### Setup & Changeover Defaults

#### CAPACITY_SETUP_HOURS_DEFAULT
- **Current Value:** 0 hours (0 minutes)
- **Valid Range:** 0-10 hours
- **Effect:** Default setup time when not in routing
- **Tuning Guide:**
  - 0 hours = setup included in cycle time or negligible
  - Increase if demo mode enabled and routing incomplete
  - Typical: 0.5-2 hours for foundry setup

#### CAPACITY_CHANGEOVER_HOURS_DEFAULT
- **Current Value:** 0 hours (0 minutes)
- **Valid Range:** 0-5 hours
- **Effect:** Default changeover time when not in changeover matrix
- **Tuning Guide:**
  - 0 hours = changeover included in cycle time
  - Increase if processes require grade cleaning (e.g., stainless→carbon)
  - Typical: 0.25-1 hour for mill changeovers

---

## Configuration Impact Summary

| Tuning Goal | Parameters to Adjust |
|---|---|
| Reduce schedule makespan | CYCLE_TIME_*, SOLVER_TIME_LIMIT_SECONDS |
| Improve queue compliance | OBJECTIVE_QUEUE_VIOLATION_WEIGHT |
| Prioritize urgent orders | PRIORITY_WEIGHT_URGENT |
| Reduce material shortages | YIELD_*_PCT (increase), CAMPAIGN_MIN_QUANTITY_MT (decrease) |
| Improve inventory turns | CAMPAIGN_MAX_QUANTITY_MT (decrease), HEAT_SIZE_MT (decrease) |
| Optimize merge decisions | CTP_SCORE_MERGE_CAMPAIGN, CTP_MERGEABLE_SCORE_THRESHOLD |
| Increase throughput | HEAT_SIZE_MT (increase), SOLVER_RELATIVE_GAP_TOLERANCE (increase) |
| Ensure schedule feasibility | CAMPAIGN_MAX_QUANTITY_MT, CAPACITY_HORIZON_DAYS |

---

## Testing Parameter Changes

**Safe workflow for tuning:**

1. Modify single parameter in Algorithm_Config sheet
2. Run `POST /api/run/aps` with same input data
3. Compare outputs (campaign count, schedule makespan, capacity utilization)
4. If improvement observed, keep change; else revert
5. Document parameter sensitivity in notes column

**Example:** To test if HEAT_SIZE_MT is optimal:
- Run with 40 MT → observe schedule result
- Run with 50 MT (current) → compare
- Run with 60 MT → compare
- Choose value that balances campaign frequency and resource utilization

---

**Last Updated:** 2026-04-04 | All 47 parameters documented
