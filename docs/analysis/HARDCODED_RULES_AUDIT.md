# Hardcoded Rules Audit — Complete Algorithm Review

**Date:** 2026-04-04  
**Status:** ✓ AUDIT COMPLETE — 47 hardcoded business rules identified  
**Goal:** Convert all to Excel-driven configuration

---

## Executive Summary

The APS scheduler contains **47 hardcoded values and business rules** scattered across 5 key modules. These control critical scheduling behavior but are buried in code, making them impossible for planners to tune without developer intervention.

**Current State:** Code-driven (every rule change requires Python edit + restart)  
**Target State:** Configuration-driven (every rule configurable via Excel + API)

---

## Audit Results by Module

### 1. SCHEDULER.PY — 16 Hardcoded Rules

| # | Value | Line | Purpose | Current | Type | Config Key |
|---|-------|------|---------|---------|------|------------|
| S1 | EAF_TIME | 22 | EAF cycle time (minutes) | 90 | Duration | CYCLE_TIME_EAF_MIN |
| S2 | LRF_TIME | 23 | LRF cycle time (minutes) | 40 | Duration | CYCLE_TIME_LRF_MIN |
| S3 | VD_TIME | 24 | VD cycle time (minutes) | 45 | Duration | CYCLE_TIME_VD_MIN |
| S4 | CCM_130 | 25 | CCM-130 cycle time (minutes) | 50 | Duration | CYCLE_TIME_CCM_130_MIN |
| S5 | CCM_150 | 26 | CCM-150 cycle time (minutes) | 60 | Duration | CYCLE_TIME_CCM_150_MIN |
| S6 | QUEUE_VIOLATION_WEIGHT | 48 | Queue violation penalty in objective | 500 | Weight | OBJECTIVE_QUEUE_VIOLATION_WEIGHT |
| S7 | Priority weight: URGENT | 213 | Lateness weight for URGENT orders | 4 | Weight | PRIORITY_WEIGHT_URGENT |
| S8 | Priority weight: HIGH | 215 | Lateness weight for HIGH orders | 3 | Weight | PRIORITY_WEIGHT_HIGH |
| S9 | Priority weight: NORMAL | 217 | Lateness weight for NORMAL orders | 2 | Weight | PRIORITY_WEIGHT_NORMAL |
| S10 | Priority weight: LOW | 219 | Lateness weight for LOW orders | 1 | Weight | PRIORITY_WEIGHT_LOW |
| S11 | SMS lateness discount | 1096 | SMS lateness = RM lateness × 0.5 | 0.5 | Ratio | OBJECTIVE_SMS_LATENESS_RATIO |
| S12 | Planning horizon | 865, 1464 | Default planning window (days) | 14 | Days | PLANNING_HORIZON_DAYS |
| S13 | Solver time limit | 874, 1257 | CP-SAT solver timeout (seconds) | 30 | Seconds | SOLVER_TIME_LIMIT_SECONDS |
| S14 | Solver workers | 1257 | Number of search workers for OR-Tools | 4 | Count | SOLVER_NUM_SEARCH_WORKERS |
| S15 | Max horizon extension | 900 | Extra days beyond planning horizon | 7 | Days | PLANNING_HORIZON_EXTENSION_DAYS |
| S16 | Setup time: first heat only | 1057, 1554 | Setup included only on first heat per campaign | True | Boolean | SETUP_TIME_FIRST_HEAT_ONLY |

---

### 2. CAMPAIGN.PY — 14 Hardcoded Rules

| # | Value | Line | Purpose | Current | Type | Config Key |
|---|-------|------|---------|---------|------|------------|
| C1 | HEAT_SIZE_MT | 24 | Standard heat size for SMS | 50.0 | Quantity (MT) | HEAT_SIZE_MT |
| C2 | CCM_YIELD | 25 | CCM casting yield factor | 0.95 | Percentage | YIELD_CCM_PCT |
| C3 | DEFAULT_RM_YIELD | 27 | Default RM rolling yield | 0.89 | Percentage | YIELD_RM_DEFAULT_PCT |
| C4 | RM_YIELD[5.5mm] | 26 | RM yield for 5.5mm section | 0.88 | Percentage | YIELD_RM_5_5MM_PCT |
| C5 | RM_YIELD[6.5mm] | 26 | RM yield for 6.5mm section | 0.89 | Percentage | YIELD_RM_6_5MM_PCT |
| C6 | RM_YIELD[8.0mm] | 26 | RM yield for 8.0mm section | 0.90 | Percentage | YIELD_RM_8_0MM_PCT |
| C7 | RM_YIELD[10.0mm] | 26 | RM yield for 10.0mm section | 0.91 | Percentage | YIELD_RM_10_0MM_PCT |
| C8 | RM_YIELD[12.0mm] | 26 | RM yield for 12.0mm section | 0.92 | Percentage | YIELD_RM_12_0MM_PCT |
| C9 | BOM depth limit | 178 | Max levels in BOM explosion | 12 | Count | BOM_MAX_DEPTH |
| C10 | Min campaign size | 551 | Minimum batch size for campaigns | 100.0 | MT | CAMPAIGN_MIN_SIZE_MT |
| C11 | Max campaign size | 552 | Maximum batch size for campaigns | 500.0 | MT | CAMPAIGN_MAX_SIZE_MT |
| C12 | Low carbon grades | 48 | BIL-130 billet qualifier (SAE1008,1018,1035) | Hardcoded set | List | LOW_CARBON_BILLET_GRADES |
| C13 | VD required grades | 46 | Which grades need VD (1080, CHQ, CrMo) | Hardcoded set | List | VD_REQUIRED_GRADES |
| C14 | Yield loss default | 243, 298 | Default % yield loss on BOM calcs | 0.0 | Percentage | YIELD_LOSS_DEFAULT_PCT |

---

### 3. BOM_EXPLOSION.PY — 8 Hardcoded Rules

| # | Value | Line | Purpose | Current | Type | Config Key |
|---|-------|------|---------|---------|------|------------|
| B1 | Min yield bound | 68 | Minimum yield allowed (safety) | 0.01 | Percentage | YIELD_MIN_BOUND_PCT |
| B2 | Max yield bound | 68 | Maximum yield allowed (cap) | 1.0 | Percentage | YIELD_MAX_BOUND_PCT |
| B3 | Scrap % conversion | 68 | Scrap % formula: yield = 1 - scrap% | Hardcoded | Formula | SCRAP_TO_YIELD_FORMULA |
| B4 | Yield % preference | 63-69 | Use Yield_Pct over Scrap_% | Hardcoded | Rule | YIELD_COLUMN_PREFERENCE_ORDER |
| B5 | Byproduct mode | 13 | Default byproduct inventory handling | "deferred" | Choice | BYPRODUCT_INVENTORY_MODE |
| B6 | Input flow types | 10 | Which BOM rows are inputs (6 values) | {"", "INPUT", "CONSUME", "CONSUMED", "REQUIRED"} | Set | INPUT_FLOW_TYPE_LIST |
| B7 | Byproduct flow types | 11 | Which BOM rows are outputs (5 values) | {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"} | Set | BYPRODUCT_FLOW_TYPE_LIST |
| B8 | Tolerance for zero | 607, 676, 752 | Threshold below which qty = 0 | 1e-6 | Threshold | ZERO_TOLERANCE_THRESHOLD |

---

### 4. CTP.PY — 6 Hardcoded Rules

| # | Value | Line | Purpose | Current | Type | Config Key |
|---|-------|------|---------|---------|------|------------|
| T1 | Stock coverage score | 419 | Points for stock-only promise | 60.0 | Points | CTP_SCORE_STOCK_ONLY |
| T2 | Merge option score | 426, 430 | Points for merge vs new campaign | 10.0, 4.0 | Points | CTP_SCORE_MERGE / CTP_SCORE_NEW_CAMPAIGN |
| T3 | Mergeable threshold | 458 | Min score for merge feasibility | 55.0 | Points | CTP_MERGEABLE_SCORE_THRESHOLD |
| T4 | Decision precedence | 24-37 | Ranking of CTP decisions (14 values) | 1-14 | Rank | CTP_DECISION_PRECEDENCE_* |
| T5 | Inventory tolerance | 912 | Zero qty threshold | 1e-9 | Threshold | INVENTORY_ZERO_TOLERANCE |
| T6 | Merge penalty | 1368 | Cost of not merging | 0 or 1 | Cost | CTP_MERGE_PENALTY |

---

### 5. CAPACITY.PY — 3 Hardcoded Rules

| # | Value | Line | Purpose | Current | Type | Config Key |
|---|-------|------|---------|---------|------|------------|
| K1 | Capacity horizon | 290 | Default capacity planning window | 14 | Days | CAPACITY_HORIZON_DAYS |
| K2 | Setup hours init | 282 | Default setup hours (before calc) | 0.0 | Hours | CAPACITY_SETUP_HOURS_DEFAULT |
| K3 | Changeover hours init | 283 | Default changeover hours (before calc) | 0.0 | Hours | CAPACITY_CHANGEOVER_HOURS_DEFAULT |

---

## Business Rules Summary

### Decision Logic Rules (Not Direct Values)
| # | Rule | Location | Purpose | Impact |
|---|------|----------|---------|--------|
| R1 | First heat includes setup | scheduler.py:1057 | Only first heat in campaign adds setup time | Affects SMS cycle time calculation |
| R2 | Campaign serialization | scheduler.py:1104-1108 | Previous campaign end controls next campaign start | Affects schedule density and parallelization |
| R3 | RM lateness vs SMS lateness | scheduler.py:1093-1098 | SMS lateness weighted 50% of RM | Asymmetric scheduling priority |
| R4 | BOM cycle detection | campaign.py:194-195 | Detect and handle BOM cycles | Prevents infinite recursion |
| R5 | Multi-heat setup logic | campaign.py / scheduler.py | Setup only on first heat | Affects total SMS duration |
| R6 | Yield preference order | bom_explosion.py:63-69 | Yield_Pct > Scrap_% in priority | Affects material requirement calculation |
| R7 | Queue enforcement mode | scheduler.py:991-997 | Hard vs Soft queue constraints | Affects schedule feasibility |
| R8 | CTP mergeable logic | ctp.py:458 | Score threshold for merge feasibility | Controls when orders can share campaign |
| R9 | Priority rank mapping | scheduler.py:212-219 | URGENT(1)->4, HIGH(2)->3, NORMAL(3)->2, LOW(4)->1 | Controls lateness weighting |
| R10 | Changeover asymmetry | scheduler.py:1121-1122 | Different changeover times per direction | Affects RM sequencing |

---

## Configuration Sheet Design

### New Excel Sheet: "Algorithm_Config"

**Purpose:** Store all 47 hardcoded rules as configurable parameters

**Structure:**
```
Column A: Config_Key (unique identifier)
Column B: Category (SCHEDULER, CAMPAIGN, BOM, CTP, CAPACITY)
Column C: Parameter_Name (human-readable)
Column D: Current_Value (hardcoded value)
Column E: Data_Type (Duration, Weight, Percentage, Count, Boolean, Choice, Set, List)
Column F: Min_Value (validation bound)
Column G: Max_Value (validation bound)
Column H: Description (business purpose)
Column I: Impact_Level (HIGH, MEDIUM, LOW)
Column J: Change_Log (who, when, why)
```

### Initial Data (47 rows):
- Rows 1-5: Scheduler times (EAF, LRF, VD, CCM-130, CCM-150)
- Rows 6-14: Scheduler weights (queue, priority x4, SMS ratio)
- Rows 15-17: Solver parameters (time limit, workers, horizon)
- Rows 18-31: Campaign rules (heat size, yields, grades, campaign sizing)
- Rows 32-39: BOM rules (yields, byproducts, flow types)
- Rows 40-45: CTP rules (scores, thresholds, precedence)
- Rows 46-48: Capacity rules

---

## Implementation Approach

### Phase 1: Create Configuration Infrastructure (3-4 hours)

1. **Add Algorithm_Config sheet to workbook**
   - 47 rows of parameters with validation rules
   - Data type enforcement (numeric, boolean, choice)

2. **Create loader in engine/config.py**
   - Load Algorithm_Config sheet on scheduler startup
   - Validate all values (min/max bounds, types)
   - Cache in memory for fast access
   - Report any validation errors with helpful messages

3. **Create API endpoints in xaps_application_api.py**
   - GET /api/config/algorithm → Fetch all current settings
   - GET /api/config/algorithm/{key} → Fetch single parameter
   - PUT /api/config/algorithm/{key} → Update single parameter
   - POST /api/config/algorithm/validate → Pre-validate before commit
   - POST /api/config/algorithm/export → Export to Excel for backup

### Phase 2: Refactor Modules to Use Config (6-8 hours)

1. **engine/scheduler.py**
   - Replace EAF_TIME = 90 with config.get("CYCLE_TIME_EAF_MIN")
   - Replace QUEUE_VIOLATION_WEIGHT with config.get("OBJECTIVE_QUEUE_VIOLATION_WEIGHT")
   - Replace priority weights with config lookup
   - Replace solver parameters with config
   - 16 replacements total

2. **engine/campaign.py**
   - Replace HEAT_SIZE_MT with config.get("HEAT_SIZE_MT")
   - Replace CCM_YIELD, RM_YIELD dict with config lookups
   - Replace campaign min/max with config
   - Replace LOW_CARBON_BILLET_GRADES, VD_GRADES with config
   - 14 replacements total

3. **engine/bom_explosion.py**
   - Replace yield bounds with config
   - Replace flow type sets with config
   - Replace byproduct mode with config
   - 8 replacements total

4. **engine/ctp.py**
   - Replace score thresholds with config
   - Replace decision precedence with config
   - 6 replacements total

5. **engine/capacity.py**
   - Replace horizon, setup defaults with config
   - 3 replacements total

### Phase 3: Create Configuration UI (2-3 hours)

1. **API for configuration management**
   - Built on xaps_application_api.py
   - Endpoints for CRUD operations on Algorithm_Config

2. **Documentation & Validation**
   - Clear impact descriptions for each parameter
   - Validation rules (min/max, data type)
   - Audit trail of changes
   - Rollback capability

---

## Impact Assessment

### Scheduling Algorithm
- **Cycle times:** Can now tune SMS throughput without code change
- **Priority weights:** Can now adjust urgency penalties dynamically
- **Queue violations:** Can now balance hard vs soft constraints
- **Solver:** Can now adjust timeouts and search strategy

### Campaign Building
- **Heat sizes:** Can now adjust batch sizes for different product lines
- **Yield factors:** Can now update yields as process improves
- **Campaign sizing:** Can now adjust min/max per sales period
- **Grade assignments:** Can now modify material routing rules

### BOM Explosion
- **Yield handling:** Can now tune yield assumptions per product
- **Byproducts:** Can now control when byproducts are available

### CTP
- **Score thresholds:** Can now tune merge vs new campaign decisions
- **Decision ranking:** Can prioritize different promise types

### Capacity Planning
- **Horizons:** Can now adjust planning windows

---

## Risk Mitigation

### Validation
- All parameters validated before use
- Min/max bounds enforced
- Data type checking
- Dependency checking (e.g., min_campaign < max_campaign)

### Audit Trail
- Every change logged with timestamp, user, reason
- Version history maintained
- Rollback to previous configuration possible
- Export/import for backup and comparison

### Testing
- Config-driven unit tests
- Scenario testing with different configurations
- Performance testing (solver time with different timeouts)
- Sensitivity analysis (impact of changing each parameter)

---

## Excel Sheet Structure (Algorithm_Config)

```
A               B           C                           D      E           F     G      H                           I      J
Config_Key      Category    Parameter_Name              Value  Type        Min   Max    Description                 Impact Change_Log
CYCLE_TIME_EAF_MIN  SCHEDULER   EAF Cycle Time (min)       90     Duration    60    180    Time for EAF heat melting   HIGH   Initial
CYCLE_TIME_LRF_MIN  SCHEDULER   LRF Cycle Time (min)       40     Duration    20    120    Time for LRF refining       HIGH   Initial
...
HEAT_SIZE_MT    CAMPAIGN    Standard Heat Size (MT)    50.0   Quantity    20    100    SMS batch size              HIGH   Initial
CCM_YIELD       CAMPAIGN    CCM Casting Yield (%)     95      Percentage  80    100    Casting process yield       MEDIUM Initial
LOW_CARBON_BILLET_GRADES CAMPAIGN Low Carbon Grades (,sep) 1008,1018,1035 List        List of grades for BIL-130  MEDIUM Initial
```

---

## Next Steps

1. **Immediate:** Design Algorithm_Config sheet structure (30 min)
2. **Week 1:** Implement config.py loader and validation (2-3 hours)
3. **Week 1:** Add API endpoints for config management (2 hours)
4. **Week 2:** Refactor scheduler.py, campaign.py to use config (4-5 hours)
5. **Week 2:** Refactor bom_explosion.py, ctp.py, capacity.py (3-4 hours)
6. **Week 3:** Comprehensive testing and validation (4 hours)
7. **Week 3:** Documentation and training (2 hours)

**Total Effort:** 20-25 developer hours over 3 weeks

---

## Expected Outcomes

✓ **Planner Control:** All 47 business rules configurable without code changes  
✓ **Audit Trail:** Every change tracked with reason and impact  
✓ **Flexibility:** Can tune scheduling behavior for different products/seasons  
✓ **Reproducibility:** Configuration versions can be saved and compared  
✓ **Extensibility:** New parameters can be added without code modification  
✓ **Governance:** Validation prevents invalid configurations

