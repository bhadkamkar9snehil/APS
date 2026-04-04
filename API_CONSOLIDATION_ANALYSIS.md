# API Consolidation Analysis & Recommendations

## Executive Summary

The codebase contains **4 different Flask API implementations** with overlapping functionality. The analysis shows that **`xaps_application_api.py` is the most comprehensive and well-structured**, having integrated best practices from the other versions. A formal consolidation strategy is recommended to eliminate technical debt and confusion.

---

## Current API Landscape

### 1. **api_server.py** (573 lines)
- **Status**: Legacy/unknown state
- **Role**: Unknown purpose, not directly referenced in batch scripts
- **Routes**: Not analyzed in detail
- **Assessment**: Candidate for deprecation

### 2. **api_server_complete.py** (657 lines)
- **Status**: Backwards-compatible edition
- **Key Features**:
  - Serves existing APS endpoints ✓
  - Exposes generic workbook CRUD for all structured sheets ✓
  - Includes metadata endpoints:
    - `/api/meta/sheets`
    - `/api/meta/workbook-snapshot`
    - `/api/sheets/*` (generic CRUD)
    - `/api/sheets/<sheet_name>/bulk/replace` (bulk update)
  - Concept-oriented routes (orders, campaigns, schedule, CTP, BOM, capacity)
- **Assessment**: Good backwards-compatibility, but sheet-level CRUD may not be needed in xaps version

### 3. **api_server_aps_concepts.py** (574 lines)
- **Status**: Concept-oriented edition
- **Key Features**:
  - Concept-oriented design (not sheet-centric)
  - APS domain routes (/api/aps/*)
  - Operational endpoints (campaigns, schedule, dispatch)
  - Order management with assignment logic
  - Health and data endpoints
- **Routes**: ~/20 endpoints similar to xaps version
- **Assessment**: Similar quality to xaps version, but superseded

### 4. **xaps_application_api.py** (1496 lines) ⭐ **RECOMMENDED PRIMARY**
- **Status**: Current, well-developed, concept-oriented
- **Key Features**:
  - Comprehensive error handling with error codes and domains
  - Advanced retries for file-locking issues
  - Better configuration management (envvars)
  - SHEETS mapping for master data and output sections
  - Enhanced response payloads with trace IDs
  - Material planning functions (net requirements, BOM consolidation, commit simulation)
  - Full campaign workflow support (release-queue ordering, status updates)
  - Dispatch board for resource-based scheduling
  - Advanced state management with run_id tracking
- **Routes**: 40+ endpoints including:
  - **Data endpoints** (`/api/data/*`): dashboard, config, orders, SKUs, campaigns, gantt, capacity
  - **Execution endpoints** (`/api/run/*`): BOM, schedule, CTP
  - **Order management** (`/api/orders/`, `/api/orders/<id>`, `/api/orders/assign`)
  - **APS domain endpoints** (`/api/aps/*` for campaigns, schedule, dispatch)
  - **Health check** (`/api/health`)

---

## Detailed Route Comparison

### Data Endpoints (Read-only)
| Endpoint | xaps | concepts | complete |
|----------|------|----------|----------|
| /api/health | ✓ | ✓ | ✓ |
| /api/data/dashboard | ✓ | ✓ | ✓ |
| /api/data/config | ✓ | ✓ | ✓ |
| /api/data/orders | ✓ | ✓ | ✓ |
| /api/data/skus | ✓ | ✓ | ✓ |
| /api/data/campaigns | ✓ | ✓ | ✓ |
| /api/data/gantt | ✓ | ✓ | ✓ |
| /api/data/capacity | ✓ | ✓ | ✓ |

### Execution Endpoints (POST)
| Endpoint | xaps | concepts | complete |
|----------|------|----------|----------|
| /api/run/bom | ✓ | ✓ | ✓ |
| /api/run/schedule | ✓ | ✓ | ✓ |
| /api/run/ctp | ✓ | ✓ | ✓ |

### Order Management
| Endpoint | xaps | concepts | complete |
|----------|------|----------|----------|
| /api/orders (GET/POST) | ✓ | ✓ | ✓ |
| /api/orders/<so_id> (GET/PUT/DELETE) | ✓ | ✓ | ✓ |
| /api/orders/assign | ✓ | ✓ | ✓ |

### APS Domain Endpoints (High-value)
| Endpoint | xaps | concepts | complete |
|----------|------|----------|----------|
| /api/aps/dashboard/overview | ✓ | ✓ | ✗ |
| /api/aps/orders/list | ✓ | ✓ | ✗ |
| /api/aps/orders/{id} | ✓ | ✓ | ✗ |
| /api/aps/orders (POST) | ✓ | ✓ | ✗ |
| /api/aps/orders/assign | ✓ | ✓ | ✗ |
| /api/aps/campaigns/list | ✓ | ✓ | ✗ |
| /api/aps/campaigns/release-queue | ✓ | ✓ | ✗ |
| /api/aps/campaigns/{campaign_id} | ✓ | ✓ | ✗ |
| /api/aps/campaigns/{campaign_id}/status (PATCH) | ✓ | ✗ | ✗ |
| /api/aps/schedule/gantt | ✓ | ✗ | ✗ |
| /api/aps/schedule/run (POST) | ✓ | ✗ | ✗ |
| /api/aps/schedule/jobs/{job_id} | ✓ | ✗ | ✗ |
| /api/aps/schedule/jobs/{job_id}/reschedule (PATCH) | ✓ | ✗ | ✗ |
| /api/aps/dispatch/board | ✓ | ✗ | ✗ |
| /api/aps/dispatch/resources/{resource_id} | ✓ | ✗ | ✗ |

### Generic Workbook CRUD (Sheet-level) - `api_server_complete.py` only
| Endpoint | Purpose |
|----------|---------|
| /api/meta/sheets | List all sheets metadata |
| /api/meta/workbook-snapshot | Get entire workbook data |
| /api/sheets/<sheet_name> (GET/POST) | Read/create on sheet |
| /api/sheets/<sheet_name>/bulk/replace (PUT) | Replace all rows |
| /api/sheets/<sheet_name>/<key_value> (GET/PUT/PATCH/DELETE) | Row-level CRUD |

---

## Code Quality Assessment

### xaps_application_api.py (STRONGEST)
**Strengths:**
- ✓ Stateful error tracking with trace IDs and run_id
- ✓ File locking retry logic (_read_sheet with max_retries)
- ✓ Standard error response format with error_code, error_domain, degraded_mode
- ✓ Comprehensive data aggregation functions (_load_all)
- ✓ Master data decoding/validation
- ✓ Simulation functions (simulate_material_commit)
- ✓ JSON encoding with numpy/datetime handling

**Unique Features:**
- Campaign release queue with due date ordering
- Dispatch board aggregation
- Job rescheduling capability
- Material commit simulation
- More robust state management

### api_server_aps_concepts.py (GOOD)
**Strengths:**
- Clean concept-oriented design
- Similar feature set to xaps
- Well-organized helper functions

**Weaknesses:**
- Lacks advanced error handling
- No file-locking retry logic
- Simpler state management
- Missing some endpoints (reschedule, dispatch details)

### api_server_complete.py (ADEQUATE FOR BACKWARDS-COMPAT)
**Strengths:**
- Generic CRUD for any sheet
- Metadata endpoints
- Backwards-compatible

**Weaknesses:**
- Sheet-level abstraction may be too low-level
- Less advanced error handling
- No material planning functions

---

## Dependency Usage

All three concept-oriented APIs use the same core engine modules:
- `engine.bom_explosion`: consolidate_demand, explode_bom_details, net_requirements
- `engine.campaign`: build_campaigns
- `engine.capacity`: capacity_map, compute_demand_hours
- `engine.ctp`: capable_to_promise
- `engine.scheduler`: schedule
- `engine.excel_store`: ExcelStore (workbook I/O)
- `engine.workbook_schema`: SHEETS (sheet registry)

**Note:** Only xaps_application_api has `simulate_material_commit` imported (advanced material planning).

---

## Recommendations

### PRIMARY: Establish xaps_application_api.py as the Canonical API

**Action Items:**
1. **Rename for clarity**:
   - Rename `xaps_application_api.py` → `api.py` or `api_server.py` (as the primary server)
   - Optionally, make it start with `python api_server.py` or `python -m api`

2. **Document as primary**:
   - Update [README.md](README.md) to reference only xaps_application_api as the current API
   - Add comment in [package.json](package.json) and [start_aps.bat](start_aps.bat) pointing to this file
   - Create [docs/API_ROUTES.md](docs/API_ROUTES.md) with full endpoint documentation

3. **Archive or deprecate other versions**:
   - Move `api_server.py`, `api_server_complete.py`, `api_server_aps_concepts.py`, `api_server_legacy.py` to archive or mark with `_DEPRECATED.py` suffix
   - Add deprecation notice to file headers
   - Option: Create `archive/api_versions/` folder and move them there

### SECONDARY: Consider If Generic CRUD Is Needed

**Decision Point:** Do you need `/api/sheets/*` generic CRUD endpoints?

**If NO** (likely for xaps design):
- xaps_application_api.py is complete as-is
- This is the recommended path (cleaner API contract)

**If YES** (for admin/advanced users):
- Extract `/api/meta/*` and `/api/sheets/*` routes from api_server_complete.py
- Add to xaps_application_api.py in a new "Admin" section
- Document these as "advanced" and less stable

### TERTIARY: Testing & Validation

**Before final consolidation:**
1. Run test suite: `python -m pytest tests/ -v`
2. Verify all xaps routes work with current UI
3. Check which APIs are actually called by aps-ui (grep for API calls)
4. Verify error handling in xaps matches UI expectations

### QUATERNARY: UI Integration

**Questions to resolve:**
1. Does aps-ui only call `/api/data/*`, `/api/run/*`, and `/api/aps/*` routes?
2. Or does it also use `/api/orders/*` naming convention?
3. Any other undocumented routes being used?

**Action:** Create [docs/XAPS_UI_TO_API_MATRIX.md](docs/XAPS_UI_TO_API_MATRIX.md) mapping every UI component to its API calls.

---

## Timeline

**Phase 1 (Immediate)**
- Document findings in this file ✓
- Verify which API is production-preferred
- Get stakeholder agreement on using xaps_application_api.py

**Phase 2 (Week 1)**
- Rename xaps_application_api.py if needed
- Add deprecation notices to other files
- Update documentation
- Run full test suite

**Phase 3 (Week 2-3)**
- Archive old API files
- If needed, extract generic CRUD into xaps version
- Update CI/CD to only test/build primary API

**Phase 4 (Ongoing)**
- Monitor logs/feedback from UI usage
- Ensure no legacy code paths reference old APIs
- Plan gradual migration if generic CRUD endpoints are needed

---

## Files to Create/Update

- [ ] Create: `docs/API_CONSOLIDATION_PLAN.md` (detailed implementation steps)
- [ ] Create: `docs/API_ROUTES.md` (complete endpoint reference)
- [ ] Update: `README.md` (point to primary API)
- [ ] Create: Archive directory structure for deprecated files
- [ ] Update: `start_aps.bat` (point to correct API file)
- [ ] Update: `package.json` (backend reference)

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| UI breaks when switching APIs | Low-Med | High | Comprehensive route testing before switch |
| Missing generic CRUD functionality | Low | Med | Document which endpoints needed, add if required |
| File-locking issues on Windows | Med | Med | xaps_application_api already handles this ✓ |
| State management bugs | Low | High | Verify run_id/trace_id logic with test suite |

---

## Summary Table

| API | Lines | CRUD | Concepts | Error Handling | State Mgmt | Status |
|-----|-------|------|----------|----------------|-----------|--------|
| xaps_application_api.py | 1496 | ✓ | ✓✓ | Advanced | Excellent | **RECOMMENDED** |
| api_server_aps_concepts.py | 574 | ✓ | ✓✓ | Basic | Simple | Archive |
| api_server_complete.py | 657 | ✓✓ | ✓ | Basic | Simple | Archive (or extract CRUD) |
| api_server.py | 573 | ? | ? | ? | ? | Unknown/Deprecated |
| api_server_legacy.py | ? | ? | ? | ? | ? | Unknown/Deprecated |
| xaps_excel_crud.py | ? | ✓ | ✗ | ? | ? | Complementary? |

---

## Next Steps

1. Review this analysis with team
2. Confirm xaps_application_api.py is production-ready
3. Audit aps-ui for actual API endpoint usage
4. Execute Phase 1-2 of the consolidation plan
