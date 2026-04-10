# Phase 2: Smart Refactoring — Config + Logic Rationalization

**Goal:** Not just swap hardcoded values for config lookups, but rationalize custom logic to align with standard APS patterns where applicable.

---

## Module Analysis & Refactoring Priorities

### 1. CAMPAIGN.py (882 lines) — HIGH PRIORITY
**Custom Logic Issues:**
- VD requirement hardcoded in function `needs_vd_for_grade()` — should be data from Excel
- Billet routing hardcoded in function `billet_family_for_grade()` — should use Routing sheet
- Campaign sizing constants scattered — consolidate to Algorithm_Config
- Yield factors hardcoded in multiple places — centralize in Algorithm_Config

**Phase 2 Actions:**
```python
# BEFORE: Grade hardcoded
def needs_vd_for_grade(grade):
    return grade in ['1080', 'CHQ1006', 'CrMo4140']

# AFTER: Config-driven
def needs_vd_for_grade(grade):
    vd_grades = get_config().get_list('VD_REQUIRED_GRADES', [])
    return grade in vd_grades
```

```python
# BEFORE: Billet routing hardcoded
def billet_family_for_grade(grade):
    if grade in ['1008', '1018', '1035']:
        return 'BIL-130'
    return 'BIL-150'

# AFTER: Data-driven from Excel grade master
def billet_family_for_grade(grade, grade_master_df):
    row = grade_master_df[grade_master_df['Grade'] == grade]
    if row.empty:
        return get_config().get('DEFAULT_BILLET_FAMILY', 'BIL-150')
    return row.iloc[0]['Billet_Family']
```

---

### 2. SCHEDULER.py (1833 lines) — MEDIUM PRIORITY
**Custom Logic Issues:**
- Queue violation penalty hardcoded — evaluate if modeling is correct
- SMS vs RM lateness weighting asymmetric — consider if intentional or bug
- Setup time only on first heat — should be configurable per-heat or per-campaign
- Resource preference cost injected manually — standardize

**Phase 2 Actions:**
```python
# BEFORE: Hardcoded
CYCLE_TIME_EAF_MIN = 90
QUEUE_VIOLATION_WEIGHT = 500
OBJECTIVE_SMS_LATENESS_RATIO = 0.5

# AFTER: Config-driven with evaluation
cycle_time_eaf = get_config().get_duration_minutes('CYCLE_TIME_EAF_MIN', 90)
queue_weight = get_config().get_weight('OBJECTIVE_QUEUE_VIOLATION_WEIGHT', 500)
sms_ratio = get_config().get_percentage('OBJECTIVE_SMS_LATENESS_RATIO', 0.5)

# EVALUATE: Is queue penalty the right way to model constraints?
# EVALUATE: Why SMS should be 0.5x importance of RM?
# CONSIDER: Make setup logic configurable instead of hardcoded "first heat only"
```

**Critical Evaluation Questions:**
- Queue violation penalty: Is this the right constraint modeling? Should it be hard constraint?
- SMS/RM lateness ratio: Is asymmetric weighting intentional? Document why.
- Setup time: Why only on first heat? Make configurable: SETUP_TIME_MODE (first_only, per_heat, per_campaign)

---

### 3. BOM_EXPLOSION.py (595 lines) — MEDIUM PRIORITY
**Custom Logic Issues:**
- Net requirement calculation completely custom — does it match standard BOM algorithm?
- Yield factors scattered throughout — centralize
- Byproduct handling manual — standardize with config

**Phase 2 Actions:**
```python
# BEFORE: Hardcoded yield bounds
YIELD_MIN_BOUND = 0.01
YIELD_MAX_BOUND = 1.0

# AFTER: Config-driven
def explode_bom(parent_sku, qty, bom_df):
    config = get_config()
    yield_min = config.get_percentage('YIELD_MIN_BOUND_PCT', 1) / 100
    yield_max = config.get_percentage('YIELD_MAX_BOUND_PCT', 100) / 100
    byproduct_mode = config.get('BYPRODUCT_INVENTORY_MODE', 'deferred')
    
    # ... explosion logic using configurable yield bounds
```

**Critical Evaluation Question:**
- Net requirements algorithm: Is this a standard BOM explosion + yield, or custom logic? Clarify assumptions.

---

### 4. CTP.py (1663 lines) — LOW PRIORITY (COMPLEX, CUSTOM)
**Custom Logic Issues:**
- Completely custom promise algorithm — not based on standard APS
- Scoring rules hardcoded — make configurable
- Merge vs new decision heuristic hardcoded — standardize

**Phase 2 Actions:**
```python
# BEFORE: Hardcoded
score_stock_only = 60
score_merge = 10
score_new = 4
mergeable_threshold = 55

# AFTER: Config-driven
score_stock_only = get_config().get('CTP_SCORE_STOCK_ONLY', 60)
score_merge = get_config().get('CTP_SCORE_MERGE_CAMPAIGN', 10)
score_new = get_config().get('CTP_SCORE_NEW_CAMPAIGN', 4)
mergeable_threshold = get_config().get('CTP_MERGEABLE_SCORE_THRESHOLD', 55)
```

**Important Note:**
CTP promise algorithm is custom-built. Before rationalizing:
1. Document why this approach vs standard APS methods
2. Evaluate if scoring thresholds are empirically optimal
3. Consider if algorithm should align with standard promise strategies

---

### 5. CAPACITY.py (345 lines) — LOW PRIORITY
**Custom Logic Issues:**
- Setup/changeover hardcoded — make configurable
- Capacity calculation custom — verify it matches standard formulas

**Phase 2 Actions:**
```python
# BEFORE: Hardcoded
CAPACITY_SETUP_HOURS_DEFAULT = 0.0
CAPACITY_CHANGEOVER_HOURS_DEFAULT = 0.0

# AFTER: Config-driven
setup_hours = get_config().get_duration_minutes('CAPACITY_SETUP_HOURS_DEFAULT') / 60
changeover_hours = get_config().get_duration_minutes('CAPACITY_CHANGEOVER_HOURS_DEFAULT') / 60
```

---

## Implementation Sequence

### Priority 1 (Week 2A): CAMPAIGN.py
- Move VD requirement to config (1-2 hours)
- Move billet family to Excel grade mapping (1-2 hours)
- Centralize yield factors in Algorithm_Config (1 hour)
- Consolidate campaign sizing to config (30 min)
- **Subtotal: 3-5 hours**

### Priority 2 (Week 2B): SCHEDULER.py
- Replace cycle time constants with config (1 hour)
- Replace weight constants with config (1 hour)
- **IMPORTANT:** Evaluate queue penalty modeling (2 hours)
  - Is current approach correct? Document reasoning.
  - Should SMS/RM ratio be configurable? (Implement if yes)
  - Should setup be per-campaign or per-heat? (Make configurable)
- Replace resource preference with config-based approach (1 hour)
- **Subtotal: 5-6 hours**

### Priority 3 (Week 2C): BOM_EXPLOSION.py + CAPACITY.py
- BOM: Centralize yield bounds and byproduct mode (2 hours)
- **IMPORTANT:** Evaluate net requirement algorithm (1 hour)
  - Is this standard BOM + yield, or custom? Document.
  - Should simplification be considered?
- Capacity: Move setup defaults to config (1 hour)
- **Subtotal: 4 hours**

### Priority 4 (Later): CTP.py
- Make all score thresholds configurable (2 hours)
- **IMPORTANT:** Document custom algorithm & assumptions (2 hours)
- **Subtotal: 4 hours**

---

## Testing Strategy (Critical)

After each module refactoring:
1. Run existing tests — should PASS (same behavior with config defaults)
2. Spot-check: Run with same hardcoded values in config vs old code
3. Spot-check: Modify one config value, verify result changes appropriately
4. Regression test: Run full scheduler, BOM, CTP with test data

---

## Success Criteria

✓ All hardcoded values moved to Algorithm_Config  
✓ Code reads config values, not hardcoded constants  
✓ Custom logic evaluated against standard APS patterns  
✓ Where non-standard (CTP), logic is documented & configurable  
✓ All existing tests pass without behavior change  
✓ Config values can be modified in Excel → system reflects changes  
✓ No config value affects output for standard APS workflows  

---

## Expected Outcome

**Before Phase 2:** 47 hardcoded values scattered across 5 modules, custom logic undocumented
**After Phase 2:** 
- All 46 parameters in Algorithm_Config
- Modules read from config, not hardcoded constants
- Custom logic evaluated & documented
- Non-standard approaches (CTP) are now configurable
- System behavior can be tuned from Excel without code changes
