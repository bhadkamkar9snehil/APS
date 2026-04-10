# Configuration System — Integration Checklist

**Target:** Shift APS from code-driven to configuration-driven architecture  
**Scope:** 47 business rules across 5 modules  
**Timeline:** 3 weeks, 30 hours developer effort  

---

## Pre-Implementation Checklist

- [ ] **Review** HARDCODED_RULES_AUDIT.md (understand all 47 rules)
- [ ] **Review** CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md (understand implementation approach)
- [ ] **Review** ALGORITHM_CONFIG_SHEET_TEMPLATE.md (understand Excel structure)
- [ ] **Review** CONFIGURATION_SYSTEM_SUMMARY.md (understand benefits and ROI)
- [ ] **Identify** developer who will lead implementation (20-25 hours availability)
- [ ] **Schedule** 3-week sprint with no competing priorities
- [ ] **Notify** planning team that configuration system will be ready in 3 weeks
- [ ] **Backup** current APS_BF_SMS_RM.xlsx before making changes

---

## Week 1: Foundation (3-4 hours)

### Step 1.1: Create Algorithm_Config Sheet (1 hour)

**Task:** Add new sheet to APS_BF_SMS_RM.xlsx with all 47 parameters

- [ ] Open APS_BF_SMS_RM.xlsx in Excel
- [ ] Insert new sheet: Right-click → Insert Sheet → Name: "Algorithm_Config"
- [ ] Copy header row from ALGORITHM_CONFIG_SHEET_TEMPLATE.md:
  ```
  Config_Key | Category | Parameter_Name | Current_Value | Data_Type | Min_Value | Max_Value | Unit | Description | Impact | Valid_Options | Notes | Last_Updated | Updated_By | Change_Reason
  ```
- [ ] Copy all 47 data rows from template (use copy/paste from Word or CSV)
- [ ] Set column widths for readability:
  - A (Config_Key): 35
  - B (Category): 12
  - C (Parameter_Name): 35
  - D (Current_Value): 15
  - E-G: 12 each
  - H-O: 20 each
- [ ] Format header row: Bold, fill light gray
- [ ] Add validation:
  - Column E (Data_Type): List validation with allowed types
  - Columns F-G: Numeric validation for Min/Max
  - Highlight any rows where Min > Max (should be 0)
- [ ] Save file: Ctrl+S
- [ ] Verify: Check that all 47 parameters loaded correctly

**Acceptance:** Algorithm_Config sheet visible with all 47 rows, properly formatted

---

### Step 1.2: Create Configuration Loader (2-3 hours)

**Task:** Implement engine/config.py with full validation and access methods

- [ ] Create new file: `engine/config.py`
- [ ] Copy AlgorithmConfig class from CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md
- [ ] Implement required methods:
  - `__init__(config_df)` — Initialize from DataFrame
  - `_load_from_dataframe(config_df)` — Parse Algorithm_Config sheet
  - `_convert_value(value, data_type)` — Type conversion
  - `_validate_value(key, value, data_type, min, max)` — Validation
  - `get(key, default)` — Get parameter value
  - `get_duration_minutes(key, default)` — Type-safe duration access
  - `get_percentage(key, default)` — Type-safe percentage access
  - `get_weight(key, default)` — Type-safe weight access
  - `get_list(key, default)` — Type-safe list access
  - `get_bool(key, default)` — Type-safe boolean access
  - `all_params()` — Get all parameters
  - `params_by_category(category)` — Filter by category
  - `update(key, value, user, reason)` — Update parameter
- [ ] Add global singleton:
  - `_config_instance` global variable
  - `load_algorithm_config(config_df)` function
  - `get_config()` function
- [ ] Add type hints: All method signatures with `-> Type`
- [ ] Add docstrings: Module, class, and all methods
- [ ] Test in Python REPL:
  ```python
  from engine.config import AlgorithmConfig
  config = AlgorithmConfig()
  assert config.get("CYCLE_TIME_EAF_MIN", 90) == 90  # Should work
  ```

**Acceptance:** engine/config.py created, imports cleanly, basic get/set works

---

### Step 1.3: Integrate Loader into Workbook (1 hour)

**Task:** Update data/loader.py to load Algorithm_Config on startup

- [ ] Open `data/loader.py`
- [ ] Find the function that loads all sheets (usually named `load_workbook` or similar)
- [ ] Add this after other sheets are loaded:
  ```python
  # Load algorithm configuration
  if 'Algorithm_Config' in wb.sheet_names:
      from engine.config import load_algorithm_config
      config_df = wb['Algorithm_Config'].to_frame()  # or pd.read_excel with sheet_name
      config = load_algorithm_config(config_df)
      print(f"[Config] Loaded {len(config.all_params())} algorithm parameters")
  else:
      print("[Config] Algorithm_Config sheet not found, using code defaults")
  ```
- [ ] Test by running scheduler:
  ```bash
  python api_server.py
  ```
  - Should see: "[Config] Loaded 47 algorithm parameters"
  - Or: "[Config] Algorithm_Config sheet not found" (if sheet not created yet)
- [ ] Verify no errors in startup

**Acceptance:** Scheduler starts cleanly and loads configuration from Algorithm_Config

---

### Step 1.4: Test Configuration Access (1 hour)

**Task:** Verify configuration system works end-to-end

- [ ] Run scheduler with test data
- [ ] Add debug logging to verify config reads:
  ```python
  config = get_config()
  print(f"EAF Time: {config.get_duration_minutes('CYCLE_TIME_EAF_MIN')}")
  print(f"Heat Size: {config.get('HEAT_SIZE_MT')}")
  ```
- [ ] Verify correct values loaded from Excel
- [ ] Verify validation catches invalid values:
  - Try setting negative solver time (should reject)
  - Try setting min > max (should reject)
- [ ] Test all accessor methods:
  - `get_duration_minutes(key)` returns int
  - `get_percentage(key)` returns float
  - `get_list(key)` returns list
  - `get_bool(key)` returns bool
- [ ] Verify global singleton pattern:
  ```python
  from engine.config import get_config
  config1 = get_config()
  config2 = get_config()
  assert config1 is config2  # Same instance
  ```

**Acceptance:** Configuration loads, validates, and provides type-safe access

---

## Week 2: Refactoring (6-8 hours)

### Step 2.1: Refactor scheduler.py (4 hours)

**Task:** Replace 16 hardcoded values with config lookups

Reference: CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md sections marked "scheduler.py"

- [ ] **Cycle Times (5 replacements):**
  - Line 22: `EAF_TIME = 90` → `eaf_time = config.get_duration_minutes("CYCLE_TIME_EAF_MIN", 90)`
  - Line 23: `LRF_TIME = 40` → `lrf_time = config.get_duration_minutes("CYCLE_TIME_LRF_MIN", 40)`
  - Line 24: `VD_TIME = 45` → `vd_time = config.get_duration_minutes("CYCLE_TIME_VD_MIN", 45)`
  - Line 25: `CCM_130 = 50` → `ccm_130_time = config.get_duration_minutes("CYCLE_TIME_CCM_130_MIN", 50)`
  - Line 26: `CCM_150 = 60` → `ccm_150_time = config.get_duration_minutes("CYCLE_TIME_CCM_150_MIN", 60)`

- [ ] **Queue & Priority Weights (7 replacements):**
  - Line 48: `QUEUE_VIOLATION_WEIGHT = 500` → `queue_weight = config.get_weight("OBJECTIVE_QUEUE_VIOLATION_WEIGHT", 500)`
  - Line 213-219: Priority weight mapping → Use config for each:
    ```python
    priority_weights = {
        1: config.get_weight("PRIORITY_WEIGHT_URGENT", 4),
        2: config.get_weight("PRIORITY_WEIGHT_HIGH", 3),
        3: config.get_weight("PRIORITY_WEIGHT_NORMAL", 2),
        4: config.get_weight("PRIORITY_WEIGHT_LOW", 1),
    }
    ```
  - Line 1096: `* 0.5` → `* config.get("OBJECTIVE_SMS_LATENESS_RATIO", 0.5)`

- [ ] **Solver Parameters (4 replacements):**
  - Line 865: `planning_horizon_days: int = 14` → Add config lookup
  - Line 874: `solver_time_limit_sec: float = 30.0` → Add config lookup
  - Line 1257: `num_search_workers = 4` → Use config
  - Line 900: horizon extension → Use config

- [ ] **Test after each replacement:**
  ```bash
  python -m pytest tests/test_scheduler.py -v
  ```
  - Should pass all scheduler tests
  - No behavioral changes (same default values)

**Acceptance:** 16 hardcoded values replaced, all scheduler tests pass

---

### Step 2.2: Refactor campaign.py (2 hours)

**Task:** Replace 14 hardcoded values with config lookups

Reference: CONFIGURATION_DRIVEN_IMPLEMENTATION_PLAN.md sections marked "campaign.py"

- [ ] **Batch Sizing (3 replacements):**
  - Line 24: `HEAT_SIZE_MT = 50.0` → Config lookup
  - Line 551: `min_campaign_mt: float = 100.0` → Config lookup
  - Line 552: `max_campaign_mt: float = 500.0` → Config lookup

- [ ] **Yield Factors (8 replacements):**
  - Line 25: `CCM_YIELD = 0.95` → Config lookup
  - Line 26-27: `RM_YIELD_BY_SEC` dict → Build from config:
    ```python
    RM_YIELD_BY_SEC = {
        5.5: config.get_percentage("YIELD_RM_5_5MM_PCT", 88) / 100,
        6.5: config.get_percentage("YIELD_RM_6_5MM_PCT", 89) / 100,
        8.0: config.get_percentage("YIELD_RM_8_0MM_PCT", 90) / 100,
        10.0: config.get_percentage("YIELD_RM_10_0MM_PCT", 91) / 100,
        12.0: config.get_percentage("YIELD_RM_12_0MM_PCT", 92) / 100,
    }
    ```
  - Line 27: `DEFAULT_RM_YIELD = 0.89` → Config lookup

- [ ] **Material Rules (3 replacements):**
  - Line 48: `LOW_CARBON_BILLET_GRADES = {...}` → Config lookup
  - Line 46: `VD_GRADES = {...}` → Config lookup
  - Line 178: `max_depth: int = 12` → Config lookup

- [ ] **Test after completion:**
  ```bash
  python -m pytest tests/test_campaign_config.py -v
  ```

**Acceptance:** 14 hardcoded values replaced, all campaign tests pass

---

### Step 2.3: Refactor bom_explosion.py (1 hour)

**Task:** Replace 8 hardcoded values with config lookups

- [ ] **Yield Bounds (2 replacements):**
  - Line 68: Min bound (0.01) → Config
  - Line 68: Max bound (1.0) → Config

- [ ] **Flow Type Sets (3 replacements):**
  - Line 10: `INPUT_FLOW_TYPES` → Config lookup
  - Line 11: `BYPRODUCT_FLOW_TYPES` → Config lookup
  - Line 13: `PRODUCTION_BYPRODUCT_INVENTORY_MODE` → Config lookup

- [ ] **Other (3 replacements):**
  - Line 607, 676, 752: Zero tolerance (1e-6) → Config lookup
  - Line 94-108: Byproduct mode → Config lookup

- [ ] **Test:** `python -m pytest tests/test_bom_capacity.py -v`

**Acceptance:** 8 hardcoded values replaced, all BOM tests pass

---

### Step 2.4: Refactor ctp.py (1 hour)

**Task:** Replace 6 hardcoded values with config lookups

- [ ] **CTP Scores (3 replacements):**
  - Line 419: Stock score (60) → Config
  - Line 426, 430: Merge/new scores (10, 4) → Config

- [ ] **Decision Logic (3 replacements):**
  - Line 458: Mergeable threshold (55) → Config
  - Line 912: Inventory tolerance → Config
  - Line 1368: Merge penalty → Config

- [ ] **Test:** `python -m pytest tests/test_ctp.py -v`

**Acceptance:** 6 hardcoded values replaced, all CTP tests pass

---

### Step 2.5: Refactor capacity.py (30 min)

**Task:** Replace 3 hardcoded values with config lookups

- [ ] **Defaults (3 replacements):**
  - Line 290: Horizon days → Config
  - Line 282: Setup hours default → Config
  - Line 283: Changeover hours default → Config

- [ ] **Test:** All capacity tests pass

**Acceptance:** 3 hardcoded values replaced, all capacity tests pass

---

### Step 2.6: Complete Regression Testing (1 hour)

**Task:** Run full test suite to ensure no regressions

- [ ] Run entire test suite:
  ```bash
  python -m pytest tests/ -v --tb=short
  ```
- [ ] All tests should pass (47+ tests)
- [ ] No behavior changes (using same default values)
- [ ] Code coverage should be maintained

**Acceptance:** Full test suite passes, 0 regressions

---

## Week 3: API & Completion (2-3 hours)

### Step 3.1: Implement Configuration API (2 hours)

**Task:** Add endpoints to xaps_application_api.py

- [ ] **Read Endpoints (3):**
  - [ ] `GET /api/config/algorithm` — Get all 47 parameters
  - [ ] `GET /api/config/algorithm/{key}` — Get single parameter
  - [ ] `GET /api/config/algorithm/category/{category}` — Get by category

- [ ] **Write Endpoints (2):**
  - [ ] `PUT /api/config/algorithm/{key}` — Update single parameter
  - [ ] `POST /api/config/algorithm/validate` — Pre-validate changes

- [ ] **Export/History Endpoints (2):**
  - [ ] `POST /api/config/algorithm/export` — Export to CSV
  - [ ] `GET /api/config/algorithm/changes` — Get change history (if tracking)

- [ ] **Test each endpoint:**
  ```bash
  # Read all
  curl http://localhost:5000/api/config/algorithm
  
  # Read one
  curl http://localhost:5000/api/config/algorithm/CYCLE_TIME_EAF_MIN
  
  # Update
  curl -X PUT http://localhost:5000/api/config/algorithm/CYCLE_TIME_EAF_MIN \
    -d '{"value": 100}'
  
  # Validate
  curl -X POST http://localhost:5000/api/config/algorithm/validate \
    -d '{"changes": {"SOLVER_TIME_LIMIT_SECONDS": 120}}'
  ```

**Acceptance:** All API endpoints work, return correct JSON, validate properly

---

### Step 3.2: Documentation & User Guide (1 hour)

**Task:** Create user-facing documentation

- [ ] **Excel Sheet Guide:**
  - How to modify parameters
  - Validation rules
  - Safe change process
  - Example scenarios (speed up, quality improvement, lead time reduction)

- [ ] **API Documentation:**
  - Endpoint reference (URL, method, request/response)
  - cURL examples
  - Python client example
  - Error handling

- [ ] **Change Management Process:**
  - When/why to change parameters
  - Typical parameter ranges
  - Impact of common changes
  - Troubleshooting guide

- [ ] **FAQ:**
  - "Will changes affect running schedules?" (No, but need to reload)
  - "Can we have multiple configurations?" (Yes, export/import)
  - "What if I make invalid change?" (Validation prevents it)
  - "How do we track changes?" (Audit trail in change log)

**Acceptance:** Documentation complete, clear to planners how to use system

---

### Step 3.3: Final Testing & Verification (1 hour)

**Task:** Full integration testing

- [ ] **Configuration Loading:**
  - [ ] Scheduler starts cleanly
  - [ ] All 47 parameters load correctly
  - [ ] Defaults work if Algorithm_Config missing

- [ ] **Parameter Validation:**
  - [ ] Invalid values rejected (negative, out of bounds)
  - [ ] Min > Max violations caught
  - [ ] Data type validation works

- [ ] **Behavior Consistency:**
  - [ ] Same results with config values vs hardcoded (testing same values)
  - [ ] Changing config changes behavior correctly
  - [ ] Solver respects time limit parameter
  - [ ] Campaign sizing respects min/max

- [ ] **API Correctness:**
  - [ ] All endpoints return correct data
  - [ ] Updates persist (reflected in next read)
  - [ ] Validation catches invalid changes

- [ ] **Performance:**
  - [ ] Solver time unchanged (config access negligible)
  - [ ] Startup time < 1 second extra
  - [ ] No memory leaks (config held in memory, < 1MB)

**Acceptance:** Full integration test pass, ready for production

---

## Post-Implementation Checklist

### Go-Live Preparation
- [ ] **Backup:** Create backup of APS_BF_SMS_RM.xlsx before deploying
- [ ] **Training:** Brief planning team on using Algorithm_Config
- [ ] **Monitoring:** Log all configuration changes
- [ ] **Rollback Plan:** Know how to revert if issues found

### First Week Monitoring
- [ ] **Check Logs:** Configuration loaded successfully on each run
- [ ] **User Feedback:** Any issues from planning team?
- [ ] **A/B Test:** Run same data with different configs, verify results differ as expected
- [ ] **Performance:** Monitor solver time (should be unchanged)

### Documentation
- [ ] **Update Wiki:** Configuration system usage guide
- [ ] **API Docs:** Available in `/docs` or Swagger
- [ ] **FAQ:** Address common questions
- [ ] **Change Log:** First entries of who changed what and why

---

## Success Metrics

| Metric | Target | How to Measure |
|--------|--------|-----------------|
| All hardcoded rules configured | 47/47 | Grep for magic numbers in code; should find 0 new ones |
| Configuration loads without error | 100% | Scheduler startup message confirms loading |
| API endpoints functional | 6/6 | Run endpoint test suite |
| Tests pass | 100% | `pytest tests/ --tb=short` returns 0 failures |
| Documentation complete | Yes | User guide exists and is understandable |
| Performance impact | <5% | Measure solver time before/after; should be similar |

---

## Common Issues & Solutions

### Issue: "Algorithm_Config sheet not found"
- **Cause:** Sheet not created in workbook
- **Solution:** Create sheet per Step 1.1, copy all 47 parameters
- **Prevention:** Verify sheet exists before running scheduler

### Issue: "Invalid value: X > max: Y"
- **Cause:** Configuration value outside validation bounds
- **Solution:** Correct value in Algorithm_Config; should be between Min and Max
- **Prevention:** Use data validation in Excel to prevent invalid entries

### Issue: "Config change not taking effect"
- **Cause:** Scheduler caches config at startup; didn't reload
- **Solution:** Restart scheduler to reload Algorithm_Config sheet
- **Prevention:** Clearly communicate that config changes require scheduler restart

### Issue: "Different scheduler behavior with same data"
- **Cause:** Different configuration values between runs
- **Solution:** Export config before each run for comparison; track changes in audit log
- **Prevention:** Use config export feature to save snapshots

---

## Approval Gates

- [ ] **Stage 1 (After Week 1):** Configuration sheet created and loads cleanly
- [ ] **Stage 2 (After Week 2):** All code refactored, tests pass, no behavior changes
- [ ] **Stage 3 (After Week 3):** API complete, documented, tested
- [ ] **Final (Before Go-Live):** Full integration test passed, team trained, rollback plan ready

---

**Status:** Implementation checklist complete. Ready to execute 3-week implementation.

