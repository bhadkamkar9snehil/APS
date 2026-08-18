# API Consolidation - Verification & Action Checklist

This document provides step-by-step instructions to validate the consolidation analysis and execute the migration plan.

## Quick Facts (Already Verified)

✅ **xaps_application_api.py is the CURRENT PRODUCTION API**
- Both `start_aps.bat` and `start_aps_V1.bat` reference it
- It's 1496 lines (most comprehensive)
- Has advanced error handling, retry logic, state management
- Contains exclusive endpoints: job rescheduling, dispatch board, material commit simulation

✅ **All concept-oriented APIs use the same engine modules**
- Same core imports from engine/* subfolder
- Compatible data processing pipeline
- Routes are very similar but xaps has more completeness

⚠️ **api_server_complete.py has Generic Workbook CRUD**
- Provides `/api/sheets/*` generic endpoints
- May still be needed if UI or other tools depend on it
- Decision: Keep separate or merge into xaps?

---

## Phase 0: Pre-Migration Audit (DO FIRST)

### 0.1 Verify Which API Routes Are Actually Used by UI

**Goal:** Determine if generic CRUD or advanced routes are needed.

**Steps:**
```bash
cd c:\Users\bhadk\Documents\APS\aps-ui
grep -r "/api/" src/ --include="*.tsx" --include="*.ts" --include="*.jsx" --include="*.js" | sort | uniq
```

**Expected Output Pattern:**
- Primary data: `/api/data/*`, `/api/run/*`, `/api/aps/*`
- Order CRUD: `/api/orders*`
- Advanced: `/api/aps/campaigns/*/reschedule`, `/api/aps/schedule/jobs/*/reschedule`

**Decision Tree:**
- If only `/api/data/*`, `/api/run/*`, `/api/orders/*`, `/api/aps/*` → **xaps_application_api.py is complete** ✓
- If includes `/api/sheets/*` or `/api/meta/*` → **Must extract generic CRUD routes** ⚠️
- If includes other patterns → **Document and plan custom routes** ⚠️

### 0.2 Test Current Production API

**Goal:** Verify xaps_application_api.py works with current setup.

**Steps:**
```bash
cd c:\Users\bhadk\Documents\APS

# Start the API (in PowerShell)
$env:WORKBOOK_PATH = "$(pwd)\APS_BF_SMS_RM.xlsx"
python xaps_application_api.py

# In another terminal, test key endpoints
curl http://localhost:5000/api/health
curl http://localhost:5000/api/data/orders
curl http://localhost:5000/api/data/campaigns
curl http://localhost:5000/api/aps/orders/list
curl http://localhost:5000/api/aps/campaigns/list
```

**Expected Results:**
- `/api/health` → 200 OK with JSON response
- `/api/data/*` endpoints → 200 OK with data
- `/api/aps/*` endpoints → 200 OK with domain data
- No 404 errors

### 0.3 Run Test Suite

**Goal:** Verify all tests pass before consolidation.

**Steps:**
```bash
cd c:\Users\bhadk\Documents\APS
python -m pytest tests/ -v --tb=short
```

**Expected:** All tests pass or document known failures.

### 0.4 Check for References to Other APIs

**Goal:** Identify if any other code references deprecated APIs.

**Steps:**
```bash
cd c:\Users\bhadk\Documents\APS

# Search for imports/references to other API files
grep -r "api_server.py\|api_server_complete\|api_server_aps_concepts\|api_server_legacy" . --include="*.py" --include="*.txt" --include="*.bat" --include="*.sh" --exclude-dir=archive

# Also check for direct invocations
grep -r "python.*api_server" . --include="*.bat" --include="*.sh" --include="*.json"
```

**Expected:** Only references should be in:
- `start_aps.bat` (pointing to xaps)
- `start_aps_V1.bat` (pointing to xaps)
- Possibly in package.json

---

## Phase 1: Documentation (IMMEDIATE)

### 1.1 Create API Route Reference

**File:** `docs/API_REFERENCE.md`

**Content Template:**
```markdown
# X-APS API Reference

## Data Endpoints (Read-only)

GET /api/health
- Purpose: Health check
- Response: {"status": "ok"}

GET /api/data/orders
- Purpose: List all sales orders
- Response: {"items": [...], "total": N}

[... continue for all endpoints ...]
```

**Action:**
```bash
cd c:\Users\bhadk\Documents\APS
# Extract all @app.route lines and create reference
```

### 1.2 Update README.md

**Current:** Typically outdated
**Target:** Add section pointing to xaps_application_api.py

**Example Addition:**
```markdown
## Building on APS

### Running the API Server

```bash
# Set Excel workbook path (optional, uses APS_BF_SMS_RM.xlsx by default)
set WORKBOOK_PATH=C:\path\to\workbook.xlsx

# Start the server
python xaps_application_api.py
```

Server runs on `http://localhost:5000`

See `docs/API_REFERENCE.md` for all available endpoints.
```

### 1.3 Add File Header Deprecation Notices

**For each deprecated file**, add at top:
```python
"""
[ORIGINAL DOCSTRING]

==================== DEPRECATION NOTICE ====================
This file is DEPRECATED and no longer actively maintained.

The primary APS API has been consolidated into:
  → xaps_application_api.py

This file is archived for reference only. Please use the
primary API for all new development.

To help migrate:
1. Review xaps_application_api.py for equivalent endpoints
2. Update imports/references to use xaps_application_api
3. Test thoroughly before deploying

Archive location: archive/api_versions/ (planned)
=====================================================
"""
```

---

## Phase 2: Migration Planning (WEEK 1)

### 2.1 Extract Any Missing Features to xaps (If Needed)

**Scenario:** If UI depends on `/api/sheets/*` generic CRUD

**Steps:**

1. **Copy sheet CRUD handlers from api_server_complete.py** to xaps_application_api.py
   - Find: `/api/meta/sheets` route in api_server_complete.py
   - Find: `/api/sheets/<sheet_name>` route
   - Find: `/api/sheets/<sheet_name>/bulk/replace` route
   - Find: `/api/sheets/<sheet_name>/<key_value>` routes

2. **Place in new section at bottom of xaps_application_api.py:**
   ```python
   # ── Advanced Sheet-Level CRUD (if needed) ─────────────────────
   @app.route("/api/meta/sheets")
   def meta_sheets():
       """List available sheets and their column info."""
       # Implementation from api_server_complete.py
   ```

3. **Test thoroughly** before deploying

4. **Document as optional/advanced feature**

### 2.2 Rename Files for Clarity (OPTIONAL)

**Option A: Keep as xaps_application_api.py**
- Pro: Existing batch scripts work
- Con: Name doesn't indicate it's primary

**Option B: Rename to api.py**
- Pro: Cleaner name, indicates primary API
- Con: Must update batch scripts

**Option C: Rename to api_server.py (remove legacy)**
- Pro: Standard naming convention
- Con: Must update batch scripts, may conflict historically

**Recommendation:** **Keep Option A for now** (xaps_application_api.py)
- Only rename if planning major version bump
- Update docs to clearly identify it as primary

### 2.3 Plan Archive Structure

**Create folder structure:**
```
archive/
  api_versions/
    _DEPRECATED_api_server.py
    _DEPRECATED_api_server_complete.py
    _DEPRECATED_api_server_aps_concepts.py
    _DEPRECATED_api_server_legacy.py
    README.md (explaining deprecation)
```

**OR mark with prefix inline:**
```
api_server_DEPRECATED.py
api_server_complete_DEPRECATED.py
api_server_aps_concepts_DEPRECATED.py
api_server_legacy_DEPRECATED.py
```

### 2.4 Case Study: api_server_complete.py CRUD Routes

**Question to answer:** Do we need `/api/sheets/*` endpoints at all?

**Analysis:**
```python
# From api_server_complete.py

@app.route("/api/sheets/<sheet_name>", methods=["GET", "POST"])
def sheet_collection(sheet_name):
    """Get all rows from sheet or add new row."""
    # Generic read/write for any sheet

@app.route("/api/sheets/<sheet_name>/bulk/replace", methods=["PUT"])
def sheet_bulk_replace(sheet_name):
    """Replace entire sheet contents."""
    # Bulk update capability
```

**Pros of keeping:**
- Admin/power users can directly manipulate sheets
- Useful for data import/export
- Fallback for unforeseen use cases

**Cons of consolidating:**
- Adds attack surface (any sheet can be modified)
- Encourages sheet-level thinking (bad for maintenance)
- Makes API contract unclear

**Recommendation:** **Don't merge into xaps unless proven necessary**
- If needed: Create separate `api_admin_crud.py` for sheet-level operations
- Run both services in production if generic CRUD is essential
- Document clearly as "admin API" vs "application API"

---

## Phase 3: Execution (WEEK 2)

### 3.1 Deprecation Notice Rollout

**Step 1:** Add header notices to all other API files
```bash
# For each file
python -c "
import sys
file_path = 'api_server.py'  # or other file
header = '''\"\"\"
[DEPRECATED - See API_CONSOLIDATION_ANALYSIS.md]
\"\"\"
'''
# Prepend header...
"
```

**Step 2:** Create archive/api_versions/README.md
```markdown
# Deprecated API Versions

This folder contains previous API implementations that have been 
consolidated into the primary `xaps_application_api.py`.

## History

- `_DEPRECATED_api_server.py` - Original/legacy API
- `_DEPRECATED_api_server_complete.py` - Backwards-compatible variant
- `_DEPRECATED_api_server_aps_concepts.py` - Concept-oriented variant
- `_DEPRECATED_api_server_legacy.py` - Legacy variant

## Why Consolidated?

See `API_CONSOLIDATION_ANALYSIS.md` in project root for full analysis.

## Migration Guide

1. Find your current API reference in this file
2. Locate equivalent endpoint in `xaps_application_api.py`
3. Update your client code
4. Test and deploy
```

### 3.2 Batch Script Verification

**Step 1:** Verify start_aps.bat still works
```bash
cd /d c:\Users\bhadk\Documents\APS
call start_aps.bat
# Should start API and UI successfully
```

**Step 2:** Verify no other batch scripts reference old APIs
```bash
# Check all .bat files
grep -r "api_server" *.bat
# Should only show xaps_application_api.py references
```

### 3.3 Create Migration Guide for Developers

**File:** `docs/MIGRATION_GUIDE_API_CONSOLIDATION.md`

**Content:**
```markdown
# Migration Guide: Consolidated API

## Overview
All APS APIs have been consolidated into a single primary API: `xaps_application_api.py`

## For API Consumers

### If using api_server.py
**Old:** `python api_server.py`
**New:** `python xaps_application_api.py`

All routes remain compatible. No code changes needed.

### If using api_server_complete.py
**Old:** `python api_server_complete.py`
**New:** `python xaps_application_api.py`

**Note:** If you were using `/api/sheets/*` generic CRUD routes:
- These have NOT been merged into primary API
- Contact team if you need sheet-level CRUD
- Consider using ExcelStore directly for programmatic access

### If using api_server_aps_concepts.py
**Old:** `python api_server_aps_concepts.py`
**New:** `python xaps_application_api.py`

All concept-oriented routes are functionally identical.

## For Developers

1. Update any hardcoded API references to point to xaps_application_api.py
2. Run full test suite: `pytest tests/ -v`
3. Test client code thoroughly
4. Update deployment scripts (CI/CD, Docker, etc.)

## Support

For issues or questions:
- Review `docs/API_REFERENCE.md` for endpoint details
- Check `API_CONSOLIDATION_ANALYSIS.md` for architectural decisions
- See xaps_application_api.py source code for detailed implementation
```

---

## Phase 4: Testing & Validation (WEEK 2-3)

### 4.1 Full Integration Test

```bash
cd c:\Users\bhadk\Documents\APS

# Start API
start "X-APS API" cmd /c "python xaps_application_api.py"

# Wait for startup
timeout /t 5

# Test all critical endpoints
setlocal enabledelayedexpansion
set "base_url=http://localhost:5000"

REM Data endpoints
for %%E in (health, data/orders, data/campaigns, data/capacity, aps/orders/list) do (
    echo Testing GET /api/%%E
    curl -v !base_url!/api/%%E
)

REM POST endpoints (need data)
echo Testing POST /api/run/bom
curl -X POST -H "Content-Type: application/json" !base_url!/api/run/bom

echo Testing POST /api/orders/assign
curl -X POST -H "Content-Type: application/json" !base_url!/api/orders/assign
```

### 4.2 Compatibility Test Matrix

**Create test script:**
```python
# tests/test_api_endpoints.py

import requests
import json

BASE_URL = "http://localhost:5000"

def test_health():
    r = requests.get(f"{BASE_URL}/api/health")
    assert r.status_code == 200

def test_data_orders():
    r = requests.get(f"{BASE_URL}/api/data/orders")
    assert r.status_code == 200
    assert "items" in r.json()

def test_data_campaigns():
    r = requests.get(f"{BASE_URL}/api/data/campaigns")
    assert r.status_code == 200

# ... continue for all endpoints
```

**Run:**
```bash
cd c:\Users\bhadk\Documents\APS
python -m pytest tests/test_api_endpoints.py -v
```

### 4.3 Performance Baseline

**Document current performance:**
```bash
# Measure response times for critical endpoints
ab -n 100 -c 10 http://localhost:5000/api/health
ab -n 100 -c 10 http://localhost:5000/api/data/orders
```

**Compare if switching between APIs** (if multiple kept for a time)

---

## Phase 5: Cleanup & Final Deprecation

### 5.1 Archive Old Files

```bash
cd c:\Users\bhadk\Documents\APS

# Create archive structure if not done
mkdir -p archive\api_versions

# Move deprecated files (or copy + mark deprecated)
copy api_server.py archive\api_versions\_ARCHIVED_api_server.py
copy api_server_complete.py archive\api_versions\_ARCHIVED_api_server_complete.py
copy api_server_aps_concepts.py archive\api_versions\_ARCHIVED_api_server_aps_concepts.py
copy api_server_legacy.py archive\api_versions\_ARCHIVED_api_server_legacy.py

# Keep originals with DEPRECATED prefix if using back-compat approach
move api_server.py api_server_DEPRECATED.py
move api_server_complete.py api_server_complete_DEPRECATED.py
move api_server_aps_concepts.py api_server_aps_concepts_DEPRECATED.py
move api_server_legacy.py api_server_legacy_DEPRECATED.py
```

### 5.2 Update CI/CD

**Check for:**
- Docker builds (Dockerfile)
- GitHub Actions workflows
- Local development scripts
- Deployment documentation

**Update all to reference only xaps_application_api.py**

### 5.3 Final Documentation

**Create:** `docs/API_INFRASTRUCTURE.md`
```markdown
# API Infrastructure

## Current Setup

- **Primary API:** xaps_application_api.py
- **Port:** 5000 (default)
- **CORS Origins:** localhost:3131, 127.0.0.1:3131, *
- **Workbook:** APS_BF_SMS_RM.xlsx (or env var WORKBOOK_PATH)

## Starting the API

```bash
python xaps_application_api.py
```

Or with custom workbook:
```bash
set WORKBOOK_PATH=C:\Path\To\Custom\Workbook.xlsx
python xaps_application_api.py
```

## Architecture

See xaps_application_api.py header for detailed design.

Key components:
- ExcelStore: Workbook I/O
- Engine modules: BOM, Campaign, Capacity, CTP, Scheduler
- Flask routes: Concept-oriented API contract
- Error handling: Trace IDs, degraded mode, structured errors
```

---

## Rollback Plan

**If consolidation causes issues:**

1. **Immediate Rollback:**
   ```bash
   # Copy deprecated file back
   copy api_server_complete.py api_server.py
   python api_server.py  # use legacy API
   ```

2. **Investigation:**
   - Check logs for specific errors
   - Verify workbook integrity
   - Test with sample data

3. **Gradual Rollout:**
   - Run both APIs in parallel (different ports)
   - Route UI requests to old/new API selectively
   - Monitor both for comparison

---

## Sign-Off Checklist

- [ ] Phase 0 audit complete (API usage verified)
- [ ] Phase 1 documentation created
- [ ] Phase 2 migration plan reviewed with stakeholders
- [ ] All tests pass (Phase 4)
- [ ] API endpoints tested manually
- [ ] UI tested with consolidated API
- [ ] Batch scripts verified
- [ ] Archive structure created
- [ ] Team notified of deprecation
- [ ] Cleanup complete (Phase 5)
- [ ] Monitoring active (production)

---

## Timeline Estimate

| Phase | Duration | Owner |
|-------|----------|-------|
| Phase 0 (Audit) | 2-4 hours | TBD |
| Phase 1 (Docs) | 4-6 hours | TBD |
| Phase 2 (Planning) | 2-3 hours | Team |
| Phase 3 (Execution) | 4-8 hours | TBD |
| Phase 4 (Testing) | 4-6 hours | QA |
| Phase 5 (Cleanup) | 2-3 hours | TBD |
| **Total** | **18-30 hours** | **Spread over 3 weeks** |

---

## Notes

- This can be executed incrementally (don't need to do all at once)
- Early phases are low-risk (documentation only)
- Later phases involve production changes (require testing)
- Rollback is straightforward if issues arise
