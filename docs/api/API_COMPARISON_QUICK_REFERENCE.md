# API Comparison Quick Reference

A concise lookup table for understanding the different API implementations and their relationship to the consolidated strategy.

---

## TL;DR - What to Use

| Use Case | API | Why |
|----------|-----|-----|
| **New development** | xaps_application_api.py | Primary, most complete, best practices |
| **Existing UI (aps-ui)** | xaps_application_api.py | Already configured in start_aps.bat |
| **Legacy systems** | api_server_complete.py | If you need `/api/sheets/*` generic CRUD |
| **Testing concepts** | api_server_aps_concepts.py | Simpler, good reference for learning |
| **Admin/advanced** | Create separate service | Don't merge sheet-level CRUD into main API |

---

## Route Inventory

### Core Concept Routes (All Have These)

#### Data Retrieval (GET)
| Route | xaps | concepts | complete | Purpose |
|-------|------|----------|----------|---------|
| `/api/health` | ✓ | ✓ | ✓ | Health check |
| `/api/data/dashboard` | ✓ | ✓ | ✓ | Dashboard KPIs |
| `/api/data/config` | ✓ | ✓ | ✓ | Config master data |
| `/api/data/orders` | ✓ | ✓ | ✓ | Sales orders |
| `/api/data/skus` | ✓ | ✓ | ✓ | Product master |
| `/api/data/campaigns` | ✓ | ✓ | ✓ | Campaign schedule |
| `/api/data/gantt` | ✓ | ✓ | ✓ | Gantt data for schedule |
| `/api/data/capacity` | ✓ | ✓ | ✓ | Capacity availability |

**Status:** All concept-oriented APIs have feature parity here ✓

#### Execution Routes (POST)
| Route | xaps | concepts | complete | Purpose |
|-------|------|----------|----------|---------|
| `/api/run/bom` | ✓ | ✓ | ✓ | Execute BOM explosion |
| `/api/run/schedule` | ✓ | ✓ | ✓ | Execute scheduler |
| `/api/run/ctp` | ✓ | ✓ | ✓ | Execute CTP analysis |

**Status:** All APIs have feature parity here ✓

---

### Order Management Routes (All Have These)

| Route | xaps | concepts | complete | Purpose |
|-------|------|----------|----------|---------|
| `GET /api/orders` | ✓ | ✓ | ✓ | List orders |
| `POST /api/orders` | ✓ | ✓ | ✓ | Create order |
| `GET /api/orders/<id>` | ✓ | ✓ | ✓ | Get order details |
| `PUT /api/orders/<id>` | ✓ | ✓ | ✓ | Update order |
| `DELETE /api/orders/<id>` | ✓ | ✓ | ✓ | Delete order |
| `POST /api/orders/assign` | ✓ | ✓ | ✓ | Assign to campaign |

**Status:** All APIs have feature parity here ✓

---

### Advanced APS Domain Routes

#### Dashboard & Orders (most APIs have these)
| Route | xaps | concepts | complete | Purpose |
|-------|------|----------|----------|---------|
| `/api/aps/dashboard/overview` | ✓ | ✓ | ✗ | Aggregated KPIs |
| `/api/aps/orders/list` | ✓ | ✓ | ✗ | Orders with status |
| `GET /api/aps/orders/<id>` | ✓ | ✓ | ✗ | Order detail view |
| `PUT /api/aps/orders/<id>` | ✓ | ✓ | ✗ | Update order |
| `DELETE /api/aps/orders/<id>` | ✓ | ✓ | ✗ | Delete order |
| `POST /api/aps/orders` | ✓ | ✓ | ✗ | Create order |
| `POST /api/aps/orders/assign` | ✓ | ✓ | ✗ | Assign to campaign |

**Status:** xaps + concepts have these; complete doesn't (sheet-centric approach)

#### Campaign Management  
| Route | xaps | concepts | Purpose |
|-------|------|----------|---------|
| `GET /api/aps/campaigns/list` | ✓ | ✓ | List campaigns |
| `GET /api/aps/campaigns/release-queue` | ✓ | ✓ | Campaigns sorted by due date |
| `GET /api/aps/campaigns/<id>` | ✓ | ✓ | Campaign details |
| `PATCH /api/aps/campaigns/<id>/status` | ✓ | ✗ | **[xaps only]** Update campaign status |

**Status:** xaps is more complete

#### Schedule Management
| Route | xaps | concepts | Purpose |
|-------|------|----------|---------|
| `GET /api/aps/schedule/gantt` | ✓ | ✗ | **[xaps only]** Gantt data |
| `POST /api/aps/schedule/run` | ✓ | ✗ | **[xaps only]** Execute schedule |
| `GET /api/aps/schedule/jobs/<id>` | ✓ | ✗ | **[xaps only]** Job details |
| `PATCH /api/aps/schedule/jobs/<id>/reschedule` | ✓ | ✗ | **[xaps only]** Reschedule job |

**Status:** xaps is EXCLUSIVE

#### Dispatch Board
| Route | xaps | concepts | Purpose |
|-------|------|----------|---------|
| `GET /api/aps/dispatch/board` | ✓ | ✗ | **[xaps only]** Grid of resources |
| `GET /api/aps/dispatch/resources/<id>` | ✓ | ✗ | **[xaps only]** Resource details |

**Status:** xaps is EXCLUSIVE

---

### Sheet-Level Generic CRUD (api_server_complete ONLY)

| Route | complete | Purpose |
|-------|----------|---------|
| `GET /api/meta/sheets` | ✓ | List sheet names and schemas |
| `GET /api/meta/workbook-snapshot` | ✓ | Download entire workbook as JSON |
| `GET /api/sheets/<name>` | ✓ | Get all rows from sheet |
| `POST /api/sheets/<name>` | ✓ | Add row to sheet |
| `PUT /api/sheets/<name>/bulk/replace` | ✓ | Replace all sheet rows |
| `GET /api/sheets/<name>/<key>` | ✓ | Get specific row |
| `PUT /api/sheets/<name>/<key>` | ✓ | Update row |
| `PATCH /api/sheets/<name>/<key>` | ✓ | Partial update row |
| `DELETE /api/sheets/<name>/<key>` | ✓ | Delete row |

**Status:** ONLY in api_server_complete (not in xaps)

**Consolidation Note:** If needed, can be extracted and added to xaps OR kept in separate admin API service

---

## Code Quality Comparison

### Error Handling

| Feature | xaps | concepts | complete |
|---------|------|----------|----------|
| **Error codes** (MASTER_DATA_ERROR, BOM_CYCLE, etc.) | ✓ Advanced | ✗ Basic | ✗ Basic |
| **Error domains** (API, MASTER_DATA, BOM, SCHEDULER, etc.) | ✓ Yes | ✗ No | ✗ No |
| **Trace IDs** for request correlation | ✓ Yes | ✗ No | ✗ No |
| **Degraded mode flag** | ✓ Yes | ✗ No | ✗ No |
| **File-lock retry logic** | ✓ 3x retry | ✗ 1 attempt | ✗ 1 attempt |
| **Timeout handling** | ✓ Yes | ✗ No | ✗ No |

**Winner:** xaps has enterprise-grade error handling

### Data Processing

| Feature | xaps | concepts | complete |
|---------|------|----------|----------|
| **Material commit simulation** | ✓ Yes | ✗ No | ✗ No |
| **Net requirements calculation** | ✓ Yes | ✓ Yes | ✓ Yes |
| **Demand consolidation** | ✓ Yes | ✓ Yes | ✓ Yes |
| **Campaign building** | ✓ Yes | ✓ Yes | ✓ Yes |

**Winner:** Tie (xaps has additional material simulation)

### Configuration Management

| Feature | xaps | concepts | complete |
|---------|------|----------|----------|
| **Config section mapping** | ✓ Yes | ✓ Yes | ✓ Yes |
| **Sheet mapping** (MASTERDATA_SECTION_TO_SHEET) | ✓ Explicit | ✓ Implicit | ✓ Implicit |
| **State tracking** (run_id, traces) | ✓ Advanced | ✗ Basic | ✗ Basic |

**Winner:** xaps (more explicit, better state tracking)

---

## Feature-by-Feature Decision Matrix

### Do you need these features?

| Feature | Yes → Use | No → Use |
|---------|-----------|---------|
| **Advanced error codes & domains** | xaps | any |
| **File locking resilience** | xaps | any |
| **Trace ID correlation** | xaps | any |
| **Job rescheduling** | xaps | (not available elsewhere) |
| **Dispatch board** | xaps | (not available elsewhere) |
| **Generic `/api/sheets/*` CRUD** | api_server_complete | xaps + others |
| **Learning/reference code** | api_server_aps_concepts | xaps (more complex) |
| **Sheet metadata endpoints** | api_server_complete | xaps |

---

## Deprecation Status Timeline

### NOW (v1.0)
- ✅ **xaps_application_api.py** - PRODUCTION
- ⚠️ **api_server_aps_concepts.py** - ACCEPTABLE (but considered legacy)
- ⚠️ **api_server_complete.py** - ACCEPTABLE (if generic CRUD needed)
- ❌ **api_server.py** - UNKNOWN/LEGACY

### NEXT (v1.1 - ~2 weeks)
- ✅ **xaps_application_api.py** - PRODUCTION
- 🗑️ **api_server_aps_concepts.py** - MARKED FOR DEPRECATION
- 🗑️ **api_server_complete.py** - MARKED FOR DEPRECATION (extract if needed)
- 🗑️ **api_server.py** - ARCHIVED

### FUTURE (v2.0 - ~3 months)
- ✅ **xaps_application_api.py** - PRODUCTION
- 🗑️ All others removed (archived versions available)
- 📋 If generic CRUD needed: new separate `api_admin.py` service

---

## Migration Path by Use Case

### I'm using api_server.py
```
api_server.py → xaps_application_api.py
(1 file rename effectively, or just start using xaps)
```
**Risk:** Low (both are concept-oriented, similar routes)
**Effort:** 2 hours (test + verify)

### I'm using api_server_complete.py for basic routes
```
api_server_complete.py (basic routes) → xaps_application_api.py
(all basic routes are identical)
```
**Risk:** Low
**Effort:** 2 hours (test + verify)

### I'm using api_server_complete.py for sheet CRUD
```
OPTION A: api_server_complete.py → xaps_application_api.py + api_admin.py
(extract sheet CRUD to separate admin API)

OPTION B: Keep both running
(xaps_application_api.py on port 5000, api_server_complete.py on port 5001)
```
**Risk:** Medium (depends on which CRUD routes you use)
**Effort:** 4-8 hours (extract, test, deploy dual service)

### I'm using api_server_aps_concepts.py
```
api_server_aps_concepts.py → xaps_application_api.py
(routes are nearly identical, xaps is superset)
```
**Risk:** Low (xaps is strictly better)
**Effort:** 1 hour (test + verify)

---

## Import Compatibility

All three concept-oriented APIs import from the same engine:

```python
# All use these (identical imports):
from engine.bom_explosion import consolidate_demand, explode_bom_details, net_requirements
from engine.campaign import build_campaigns
from engine.capacity import capacity_map, compute_demand_hours
from engine.ctp import capable_to_promise
from engine.scheduler import schedule
from engine.excel_store import ExcelStore
from engine.workbook_schema import SHEETS

# xaps ADDITIONALLY imports:
from engine.bom_explosion import simulate_material_commit  # Advanced feature
```

**Implication:** All are compatible with same engine version, swappable at API layer only

---

## Next Steps

1. **Decision needed:** Do you need `/api/sheets/*` generic CRUD?
   - **YES:** Plan extraction or dual-service approach
   - **NO:** Mark api_server_complete.py as deprecated now

2. **Audit needed:** Check aps-ui for actual API usage
   ```bash
   grep -r "/api/" aps-ui/src/ --include="*.tsx" --include="*.ts"
   ```

3. **Testing needed:** Verify xaps_application_api.py with full workload
   ```bash
   python -m pytest tests/ -v
   ```

4. **Rollout:** Follow phases in `API_CONSOLIDATION_VERIFICATION_CHECKLIST.md`

---

## Reference Docs

- **Detailed Analysis:** `API_CONSOLIDATION_ANALYSIS.md`
- **Execution Steps:** `API_CONSOLIDATION_VERIFICATION_CHECKLIST.md`
- **API Reference:** `docs/API_REFERENCE.md` (to be created)
- **Source:** [xaps_application_api.py](xaps_application_api.py)
