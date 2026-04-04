# API Consolidation - File Status & Lineage

## Current Files in Workspace

| File | Lines | Status | Purpose | In Use By | Notes |
|------|-------|--------|---------|-----------|-------|
| **xaps_application_api.py** | 1496 | ✅ ACTIVE | Primary production API | <ul><li>start_aps.bat</li><li>start_aps_V1.bat</li><li>aps-ui (likely)</li></ul> | **KEEP** - Recommended primary |
| **api_server_aps_concepts.py** | 574 | ⚠️ LEGACY | Concept-oriented design | Unknown | Consider deprecated |
| **api_server_complete.py** | 657 | ⚠️ LEGACY | Backwards-compatible + CRUD | Unknown | Keep only if sheet CRUD needed |
| **api_server.py** | 573 | ⚠️ LEGACY | Original/unknown | Unknown | Likely obsolete |
| **api_server_legacy.py** | ? | 🚫 UNKNOWN | Legacy variant | Unknown | Unclear purpose |
| **api_excel_crud.py** | ? | ❓ UNCLEAR | Excel CRUD operations | ? | Check if needed |
| **xaps_excel_crud.py** | ? | ❓ UNCLEAR | Excel CRUD operations (xaps variant) | ? | Check if needed |

---

## Architectural Lineage (Hypothesized)

```
                Initial API Implementation
                        ↓
            ┌───────────┴────────────┐
            ↓                        ↓
    api_server.py          api_server_legacy.py
        (base)                  (branch)
            ↓
    api_server_complete.py
        (added CRUD)
            ↓
    api_server_aps_concepts.py
        (concept focus)
            ↓
    xaps_application_api.py  ← CURRENT PRODUCTION
        (refined, best practices)
```

**Timeline (estimated):**
1. Initial `api_server.py` created
2. Variants created for testing (complete, concepts, legacy)
3. Converged on concept design as primary approach
4. `xaps_application_api.py` developed as refined version - NOW PRODUCTION
5. Other variants kept for backwards-compatibility or reference

---

## Related Files (Unclear Relationship)

### Excel CRUD Files
- **api_excel_crud.py** - Purpose unclear
- **xaps_excel_crud.py** - Purpose unclear

**Questions:**
- Are these used by other parts of the system?
- Are they standalone utilities or part of the API?
- Should they be consolidated/integrated with API or kept separate?

**Action:** Need investigation

```bash
# Find references to these files
grep -r "api_excel_crud\|xaps_excel_crud" . --include="*.py" --include="*.bat" --include="*.json"
```

### Excel-Related Scripts
- **setup_excel.py** - Excel environment setup
- **inspect_workbook.py** - Workbook inspection
- **build_template_v3.py** - Template builder

**Status:** Appear to be utilities, not part of consolidation scope

---

## Batch File Configuration

### start_aps.bat (MODERN)
```batch
set "API_PORT=5000"
set "UI_PORT=3131"
set "WORKBOOK_PATH=%CD%\APS_BF_SMS_RM.xlsx"

:: Start API server (background)
start "X-APS API" /B cmd /c "... python xaps_application_api.py"

:: Check API is alive with retry logic
curl -f -s http://localhost:5000/api/health >nul
```

**Status:** Well-structured, references xaps_application_api.py ✓

### start_aps_V1.bat (LEGACY)
```batch
:: Kill existing servers
for /f "tokens=5" %%a in ('netstat -ano...') do taskkill /PID %%a /F

:: Start API
start /B "X-APS API" python xaps_application_api.py

:: Check API
curl -s http://localhost:5000/api/health
```

**Status:** Also references xaps_application_api.py ✓

**Observation:** Both files do the same thing; v1 is simpler. Consider consolidating to single start script.

---

## Dependencies & Engine Modules

### Core Engine Used by All APIs
```
engine/
├── __init__.py
├── bom_explosion.py       ← Used by all APIs
├── campaign.py            ← Used by all APIs
├── capacity.py            ← Used by all APIs
├── ctp.py                 ← Used by all APIs
├── ctp_V1.py              ← ? (unclear if used)
├── scheduler.py           ← Used by all APIs
├── excel_store.py         ← Used by all APIs
├── workbook_routes.py     ← ? (unclear purpose)
├── workbook_schema.py     ← Used by all APIs
└── simpy_engine.py        ← ? (simulation engine?)
```

**Status:** APIs are tightly coupled to engine layer (good), but engine has some unclear modules

---

## Data Flow Analysis

### How xaps_application_api.py Works

```
HTTP Request
    ↓
Flask route handler
    ↓
┌─ Data retrieval ──→ ExcelStore reads Excel
│                        ↓
│                    Excel processing (pd.read_excel)
│                        ↓
│                    JSON response (_Enc encoder)
│
├─ Computation ────→ ExcelStore reads master data
                        ↓
                    Engine modules process data (BOM, Campaign, Schedule, CTP)
                        ↓
                    Results written back to Excel (ExcelStore)
                        ↓
                    JSON response with trace_id
│
└─ Operations ────→ CRUD operations (orders, campaigns)
                        ↓
                    ExcelStore update_row / list_rows
                        ↓
                    Excel updated
                        ↓
                    JSON response
```

### File I/O Pattern

**Excel file accessed via:**
- ExcelStore class (wrapper around pd.read_excel / openpyxl)
- Handles: read, write, row updates, bulk operations
- Retry logic: _read_sheet function with 3 retries

**Typical flow:**
```
API request
  → Acquire file lock (with retries)
  → Read sheet(s) from Excel
  → Process data
  → Write results (if needed)
  → Release lock
  → Return JSON
```

---

## Decision Tree

Use this to decide what to do with each file:

### For xaps_application_api.py
```
xaps_application_api.py
├─ Is it actually being used in production? YES
├─ Is it working correctly? YES
└─ Keep as primary API ✓
   └─ Action: Make official, document as such
```

### For api_server_aps_concepts.py
```
api_server_aps_concepts.py
├─ Is it being used anywhere? NO (likely)
├─ Do we need concept-oriented reference code? MAYBE
└─ If MAYBE → Keep for now; if NO → Archive
   └─ Decision: If aps-ui only uses xaps → Archive
```

### For api_server_complete.py
```
api_server_complete.py
├─ Do we need /api/sheets/* generic CRUD? ?
├─ If YES → Decide: Extract to new API or keep both
├─ If NO → Archive
└─ Action: Depends on audit findings
   └─ Likely: Archive (generic CRUD not aligned with concept design)
```

### For api_server.py
```
api_server.py
├─ Is anyone using this? NO (likely)
├─ Do we have a newer version? YES (xaps)
└─ Archive immediately
   └─ Action: Move to archive/api_versions/
```

### For api_server_legacy.py
```
api_server_legacy.py
├─ What was this for? UNKNOWN
├─ Do we need legacy support? ?
└─ If unsure → Archive (keep for reference)
   └─ Action: Move to archive/api_versions/ with note
```

### For api_excel_crud.py & xaps_excel_crud.py
```
api_excel_crud.py
├─ Is this used anywhere? UNKNOWN
├─ Is it complementary to APIs? UNCLEAR
└─ Action: Search for usage, then decide
   └─ If not used → Archive
   └─ If used by admin tools → Keep separate
   └─ If integral to API → Might need integration review
```

---

## Proposed File Organization (After Consolidation)

```
c:\Users\bhadk\Documents\APS\
├── xaps_application_api.py          ← PRIMARY API (rename? optional)
│
├── archive/
│   └── api_versions/
│       ├── README.md (explaining all)
│       ├── _DEPRECATED_api_server.py
│       ├── _DEPRECATED_api_server_complete.py
│       ├── _DEPRECATED_api_server_aps_concepts.py
│       ├── _DEPRECATED_api_server_legacy.py
│       └── _UNCLEAR_api_excel_crud.py
│
├── docs/
│   ├── README.md
│   ├── API_REFERENCE.md          ← NEW: Endpoint reference
│   ├── API_CONSOLIDATION_ANALYSIS.md      ← NEW
│   ├── API_CONSOLIDATION_VERIFICATION_CHECKLIST.md ← NEW
│   └── ... (existing docs)
│
├── start_aps.bat                 ← MODERN (keep)
├── start_aps_V1.bat              ← LEGACY (consider removing)
└── ... (other files)
```

---

## Immediate Actions (DO THESE FIRST)

1. **Confirm xaps_application_api.py is production**
   ```bash
   # Check batch files
   type start_aps.bat | find "xaps_application_api"
   type start_aps_V1.bat | find "xaps_application_api"
   ```
   ✅ CONFIRMED in this analysis

2. **Find usage of other APIs**
   ```bash
   grep -r "api_server" . --include="*.txt" --include="*.json" --include="*.bat" --include="*.py"
   grep -r "api_excel_crud" . --include="*.txt" --include="*.json" --include="*.bat" --include="*.py"
   ```

3. **Document findings**
   - Create [docs/API_FILE_INVENTORY.md](docs/API_FILE_INVENTORY.md) with findings
   - Add to repository memory for future reference

4. **Get team input**
   - Confirm xaps is the one to keep
   - Decide on generic CRUD routes
   - Decide on archival strategy

---

## Summary

| Item | Status | Action |
|------|--------|--------|
| **Primary API identified** | ✅ | xaps_application_api.py |
| **Other APIs assessed** | ✅ | See table above |
| **File organization proposed** | ✅ | Archive to api_versions/ |
| **Dependencies mapped** | ✅ | Engine modules identified |
| **Consolidation plan** | ✅ | See API_CONSOLIDATION_ANALYSIS.md |
| **Verification steps** | ✅ | See API_CONSOLIDATION_VERIFICATION_CHECKLIST.md |
| **Next step** | ⏳ | Execute Phase 0 (audit UI usage) |

---

## References

- [API Consolidation Analysis](API_CONSOLIDATION_ANALYSIS.md)
- [Verification Checklist](API_CONSOLIDATION_VERIFICATION_CHECKLIST.md)
- [Quick Reference](API_COMPARISON_QUICK_REFERENCE.md)
- Source files: All in c:\Users\bhadk\Documents\APS\
