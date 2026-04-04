# APS Remediation - Fixes Summary

**Status:** Phase 1-4 Complete ✅  
**Branch:** `fix/aps-correctness-blockers`  
**Date:** April 4, 2026  

---

## Overview

This document summarizes the comprehensive remediation of the APS stack focusing on **correctness blockers**, **degraded-mode discipline**, **consistency**, and **quality**. All work is in feature branch `fix/aps-correctness-blockers` with 4 commits tracking phase progress.

---

## Phase 1: Critical Correctness Fixes ✅

### 1.1 Fixed Schedule Route Binding
**File:** `xaps_application_api.py`  
**Issue:** `/api/run/schedule` route was attached to helper `_calculate_material_plan()` (non-injectable signature)  
**Fix:** Created proper Flask function `run_schedule()`, moved route decorator to it  
**Verification:** Both `/api/run/schedule` and `/api/aps/schedule/run` now execute identical code  
**Commits:** Phase 1

### 1.2 Fixed BOM API Contract Mismatch
**File:** `xaps_application_api.py`, `engine/bom_explosion.py`  
**Issue:** Code passed dict to `net_requirements()` as if it were DataFrame
- `explode_bom_details()` returns: `{"exploded": DataFrame, "structure_errors": list, "feasible": bool}`
- Wrong: `gross = explode_bom_details(...); netted = net_requirements(gross, ...)`

**Fix:** Extract dict fields correctly
- Right: `result = explode_bom_details(...); netted = net_requirements(result["exploded"], ...)`
- Response now includes: `structure_errors`, `feasible` flags

**Verification:** Test `TestBOMExplosionAndNetting::test_explode_bom_details_returns_dict` ✅

### 1.3 Resolved CTP Version Skew
**Files:** 7 files across codebase  
**Issue:** API used `ctp_V1.py` (510 lines) instead of canonical `ctp.py` (1443 lines)  
**Fix:** Updated imports across all files:
- `xaps_application_api.py`
- `api_server.py`
- `aps_functions.py`
- `api_server_aps_concepts.py`
- `api_server_complete.py`
- `api_server_legacy.py`
- `tests/test_ctp.py`

**Result:** Single canonical CTP implementation now used everywhere

### 2.1 Replaced Placeholder Material-Plan Generation
**File:** `xaps_application_api.py`  
**Issue:** Generated fake data (generic "MAKE/CONVERT", zero inventory, no real BOM)  
**Fix:** Implemented real material simulation
- Created `_enrich_campaigns_with_material_data()` function
- For each campaign, calls `simulate_material_commit()` with actual BOM/inventory
- Populates: inventory_before/after, consumed, gross_requirements, shortages, structure_errors
- Material status now reflects: OK, SHORTAGE, STRUCTURE_ERROR, SIMULATION_ERROR

**Verification:** Material responses now contain actual simulation results, not fabricated data

### 3.1 Implemented Canonical Run Artifact Model
**File:** `xaps_application_api.py`  
**Issue:** Global mutable `_state`, no run_id, not reproducible, not thread-safe  
**Fix:** New run artifact system:
- Each schedule run generates UUID-based `run_id`
- Created `_run_artifacts` dict storing full planning context
- Function `_create_run_artifact()` captures:
  - `run_id`, `created_at`, `workbook_path`
  - `input_snapshot` (campaign_count, solver_config)
  - `config_snapshot` (all config values)
  - `results` (campaigns, schedules, capacity)
  - `solver_metadata` (status, detail)
  - `warnings` and `degraded_flags`
- API returns `run_id` to client
- Future: can query any prior run by `run_id`

**Verification:** `/api/run/schedule` response includes `run_id`

---

## Phase 2: Degraded-Mode Discipline ✅

### 4.1 Stopped Auto-Release on Missing BOM
**File:** `engine/campaign.py` (lines 764-782)  
**Issue:** Missing BOM auto-released campaigns with `READY` status  
**Fix:** Changed behavior - missing BOM now HOLDS campaigns
- `release_status = "MATERIAL HOLD"`
- `material_status = "MASTER_DATA_MISSING"`
- Error recorded: `{"type": "MISSING_BOM", "message": "BOM master data is not configured"}`

**Verification:** Test `TestCampaignMissingBOM::test_campaigns_held_on_missing_bom` ✅

### 4.2 Exposed Scheduler Default-Master Use
**File:** `xaps_application_api.py` (run_schedule_api)  
**Status:** Scheduler already returns `allow_default_masters` flag  
**Enhancement:** API now extracts this and includes in `degraded_flags` for run artifact  
**Visibility:** Dashboard can show warning when defaults were used

### 4.3 Made Greedy Fallback Explicit
**File:** `xaps_application_api.py` (run_schedule_api)  
**Status:** Scheduler already returns `solver_status = "GREEDY" or "GREEDY_FALLBACK"`  
**Enhancement:** API detects and tracks in `degraded_flags`:
- `"greedy_fallback": solver in {"GREEDY", "GREEDY_FALLBACK"}`
- Included in all responses and run artifacts
- Visible to downstream consumers (dashboard, CTP, etc.)

### 6.1 Fixed CTP Frozen-Job Logic
**File:** `engine/ctp.py`  
**Issue:** CTP looked for `campaign["scheduled_jobs"]` but scheduler returns DataFrames  
**Fix:** New function `_frozen_jobs_from_schedule_dataframe()`
- Extracts frozen jobs directly from schedule output DataFrame
- Properly maps: Job_ID, Resource_ID, Planned_Start, Planned_End
- Updated `run_ctp_api()` to pass frozen_jobs from schedule output

**Usage:** CTP now reliably sees prior schedule commitments

### 6.3 Tightened CTP Confidence Degradation
**File:** `engine/ctp.py` (`_schedule_confidence`)  
**Issue:** Confidence could be HIGH when using degraded inputs  
**Fix:** Strict degradation rules (Rule 6.3):

| Condition | Max Confidence |
|-----------|---|
| Master data risk OR material hold | LOW |
| Greedy scheduler OR degraded lineage OR default masters | MEDIUM |
| Optimal conditions (none above) | HIGH |

**Verification:** All 5 CTP confidence tests passing ✅

---

## Phase 3: Consistency and Persistence ✅

### 7.2 Added Workbook Schema Validation
**File:** `xaps_application_api.py`  
**Function:** `_validate_workbook_schema()`  
**Features:**
- Checks required sheets exist
- Validates required columns per sheet
- Returns detailed diagnostics:
  - `missing_sheets`: list
  - `missing_columns`: dict
  - `sheet_details`: row counts, column lists

**Enhanced Health Endpoint:** `/api/health` now returns:
- `schema`: complete validation result
- `run_id`: active run ID
- `active_run_degraded_flags`: current degradation state

**Usage:** Client can detect configuration issues before scheduling

### 8.1 Standardized Error Payloads
**File:** `xaps_application_api.py`  
**Function:** `_error_response()` - new standard error format  
**Payload Structure:**
```json
{
    "error": true,
    "error_code": "MASTER_ERROR_BOM",
    "error_message": "User-friendly description",
    "error_domain": "BOM",
    "trace_id": "run_uuid_or_unknown",
    "degraded_mode": false,
    "details": "Technical details",
    "timestamp": "2026-04-04T09:30:00"
}
```

**Error Domains:** API, MASTER_DATA, BOM, MATERIAL, SCHEDULER, CTP, WORKBOOK_IO

**Updated Routes:**
- `/api/run/bom` - structured BOM errors
- `/api/run/schedule` - structured schedule errors
- `/api/run/ctp` - structured CTP errors

**Verification:** Error responses now machine-parseable and traceable

---

## Phase 4: Quality - Unit Tests ✅

**File:** `tests/test_correctness_fixes.py` (318 lines)

### Test Coverage

**BOM Explosion & Netting (1.2)**
- ✅ `test_explode_bom_details_returns_dict` - contract validation
- ✅ `test_bom_cycle_detection` - structure error handling
- ✅ `test_net_requirements_against_inventory` - netting logic

**Material Simulation (2.1)**
- ✅ `test_simulate_material_commit_produces_shortages` - shortage identification
- ✅ `test_simulate_material_commit_fully_covered` - feasibility check

**Campaign Hold on Missing BOM (4.1)**
- ✅ `test_campaigns_held_on_missing_bom` - hold behavior

**CTP Confidence Degradation (6.3)**
- ✅ `test_confidence_low_with_default_masters` - defaults degrade confidence
- ✅ `test_confidence_low_with_greedy_scheduler` - greedy degrade confidence
- ✅ `test_confidence_low_with_degraded_lineage` - lineage degrade confidence
- ✅ `test_confidence_low_with_material_hold` - material hold degrade confidence
- ✅ `test_confidence_high_only_with_optimal` - strict HIGH requirement

**Run:** `python -m pytest tests/test_correctness_fixes.py -v`  
**Result:** 11/11 tests passing ✅

---

## Git Commits

```
1. Phase 1: Critical correctness fixes - route binding, BOM API, CTP resolution, material replacement, run artifacts
2. Phase 2: Degraded-mode discipline - auto-release stop, scheduler defaults, greedy fallback explicit
3. Phase 2-3: CTP improvements, workbook schema validation, standardized error payloads
4. Phase 4: Comprehensive unit tests for correctness fixes
```

---

## Architecture Improvements

### Before
- Anonymous global `_state` dict, mutable, not thread-safe
- Fake material plan with zero inventory
- Auto-release on missing BOM
- Silent fallback to greedy scheduler
- No run tracking
- No schema validation
- Inconsistent error responses

### After
- Run-oriented artifact model with UUID tracking
- Real material simulation from BOM/inventory
- Held campaigns on missing BOM - visible status
- Explicit degraded-mode flags for all fallbacks
- Full planning context captured per run
- Schema validation on health endpoint
- Standardized error payloads with domain/code

---

## Acceptance Criteria - Status

| Criterion | Status |
|-----------|--------|
| All schedule routes execute same code | ✅ |
| BOM API returns correct exploded/netted structures | ✅ |
| Only one CTP implementation is live | ✅ |
| No placeholder material plan returned | ✅ |
| Missing BOM silently produces released campaigns | ✅ FIXED |
| Default masters/greedy always visible downstream | ✅ |
| CTP confidence reflects degraded evidence | ✅ |
| Plan state is run-oriented, not anonymous globals | ✅ |
| Backend contracts explicit and tested | ✅ |
| Dashboard cannot present degraded as normal | ✅ (foundation laid) |

---

## Remaining Phase 5 (UI Alignment) - Not yet implemented

Once backend is deployed and stable:
- Update frontend API contract based on run_id model
- Add degraded-mode banners/badges in UI
- Remove any UI-side semantic patching
- Frontend contract regeneration

---

## Testing Recommendations

Before merging to main:
1. ✅ Run unit tests: `pytest tests/test_correctness_fixes.py -v`
2. Run integration tests against fixture workbooks
3. Manual testing:
   - Schedule with missing BOM - verify HOLD status
   - CTP with greedy fallback - verify MEDIUM confidence
   - Check `/api/health` schema validation output
   - Verify error responses include error_code and trace_id
4. Load test with concurrent schedule runs - verify run_ids don't collide

---

## Deployment Notes

1. **Backward Compatibility:** Run artifacts don't break existing workbook-based flow; still writes to workbook sheets
2. **DB/State Store:** Current in-memory `_run_artifacts` suitable for single-instance deployment; scale to Redis/DB if needed
3. **UI Updates:** Frontend shouldn't be blocked - old schema still works, new `run_id` field is optional
4. **Deprecation:** `ctp_V1.py` can be archived after validation; import redirection active for 1-2 releases

---

## Recommended Next Steps

1. **Immediate:** Merge this branch, deploy to staging
2. **Week 1:** Integration tests, manual e2e testing with real workbooks
3. **Week 2:** Frontend updates for run_id tracking, degraded-mode UI indicators
4. **Week 3:** Production deployment, monitor run artifact creation/queries
5. **Future:** DB persistence layer for long-term run history, dashboard run comparison view

---

**End of Summary**

For detailed method signatures, see inline docstrings in fixed files.
