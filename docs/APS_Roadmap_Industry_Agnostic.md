# APS Roadmap: Priority Fixes + Industry-Agnostic Architecture

**Date:** April 2026
**Context:** Based on AVEVA gap analysis, confirmed user priorities, and full codebase audit of steel-specific hardcoding.

---

## Part A — Priority Fixes (Queue Times, Transfer Times, CTP)

These three are confirmed critical. All three are additions to existing engine files with no architectural change required.

---

### A1. Queue Times (Max Hold Time Between Operations)

**Why critical for steel:** Liquid steel loses temperature. EAF tap → CCM casting has a physical maximum hold time (typically 90–120 min). If the scheduler leaves a gap beyond this, the heat is scrapped in reality. The current system can schedule a 4-hour gap silently.

**Where to change:** `engine/scheduler.py`

**What to add:**

1. **New sheet in workbook:** `Queue_Times` with columns:
   - `From_Operation` | `To_Operation` | `Min_Queue_Min` | `Max_Queue_Min` | `Resource_Filter` (optional — e.g., only enforce for EAF machines)

2. **New parameter in `schedule()` signature:**
   ```python
   def schedule(..., queue_times=None, ...):
   ```
   `queue_times` = dict keyed by `(from_op, to_op)` → `{"min": int, "max": int}`

3. **New constraint block in CP-SAT model** (after intervals are created, before solve):
   ```
   For each heat h, for each (from_op, to_op) pair in queue_times:
       end_time[h][from_op] + min_queue <= start_time[h][to_op]
       start_time[h][to_op] <= end_time[h][from_op] + max_queue
   ```
   For `max_queue`: use `model.Add(start[to_op] <= end[from_op] + max_q)` — this enforces the hard deadline.

4. **Greedy fallback:** In `_greedy_fallback()`, after scheduling `from_op`, check if the proposed `to_op` start violates `max_queue`. If it does, mark the heat as infeasible and flag it.

5. **Read in `aps_functions.py`:**
   ```python
   data["queue_times"] = _read_queue_times(wb)  # new reader
   ```
   Pass to `schedule()` call.

6. **Output flag:** In Schedule_Output, add a `Queue_Violation` column. Flag "WARN" if a gap exceeds 75% of max_queue, "CRITICAL" if it exceeds max_queue. Highlight red.

**Steel defaults to seed the Queue_Times sheet:**
| From | To  | Min_Queue_Min | Max_Queue_Min |
|------|-----|--------------|--------------|
| EAF  | LRF | 0            | 120           |
| LRF  | VD  | 0            | 90            |
| LRF  | CCM | 0            | 90            |
| VD   | CCM | 0            | 60            |
| CCM  | RM  | 30           | 480           |

---

### A2. Transfer Times (Logistics Lag Between Operations)

**Why critical:** CCM is physically separated from the Rolling Mill. A billet cast at time T cannot start rolling before T + transfer time. Currently the scheduler can assign RM_Start = CCM_End + 0 minutes, which is impossible.

**Where to change:** `engine/scheduler.py`, `engine/capacity.py`

**What to add:**

1. **New column in Routing sheet:** `Transfer_Time_Min` — time required after this operation ends before the next can start at its resource. Add alongside existing `Cycle_Time_Min_Heat`.

2. **New parameter in `schedule()`:**
   ```python
   def schedule(..., transfer_times=None, ...):
   ```
   `transfer_times` = dict keyed by `(from_op, to_op)` → `int` (minutes)

3. **Constraint implementation:** Transfer time is a **minimum gap** constraint — a looser version of min_queue. In the CP-SAT model:
   ```
   start[to_op] >= end[from_op] + transfer_time[(from_op, to_op)]
   ```
   This is already possible with existing `NewIntervalVar` precedence constraints; just add the offset.

4. **Read from Routing sheet** in `aps_functions.py` — extract `Transfer_Time_Min` per `(From_Op, To_Op)` row, build dict, pass to scheduler.

5. **Capacity map:** `capacity.py` should account for transfer time when computing available hours — a machine sitting idle during transfer is still occupied from a scheduling perspective.

**Steel defaults:**
| From | To  | Transfer_Min |
|------|-----|-------------|
| CCM  | RM  | 30          |
| EAF  | LRF | 5           |
| LRF  | CCM | 5           |

---

### A3. CTP — Capable-to-Promise

**Why critical:** Planners need to answer "Can I commit delivery of 100 MT SAE 1008 5.5mm by April 15?" without running a full production schedule manually.

**New file:** `engine/ctp.py`

**Function signature:**
```python
def capable_to_promise(
    sku_id: str,
    qty_mt: float,
    requested_date: datetime,
    campaigns: list,       # current committed plan
    resources: dict,       # resource master
    bom: pd.DataFrame,
    inventory: dict,
    routing: pd.DataFrame,
    planning_start: datetime,
    *,
    queue_times: dict = None,
    transfer_times: dict = None,
    changeover_matrix: pd.DataFrame = None,
) -> dict:
```

**Algorithm:**
1. Build a "ghost" SO with the requested SKU + qty
2. Run campaign grouping for this ghost SO against existing committed campaigns (to check if it joins an existing campaign or needs a new one)
3. Run `explode_bom()` for the ghost demand — check if materials are available (net of already-committed campaigns)
4. If materials OK: run a targeted `schedule()` with `frozen_jobs` = all currently scheduled operations, adding only the ghost campaign
5. Extract the ghost campaign's RM_End from the schedule result
6. Return:
   ```python
   {
       "sku_id": sku_id,
       "qty_mt": qty_mt,
       "requested_date": requested_date,
       "earliest_delivery": datetime,
       "feasible": bool,               # can deliver by requested_date
       "lateness_days": float,          # negative = early
       "material_gaps": list[dict],     # shortages if any
       "joins_campaign": str | None,    # existing campaign it would join
       "new_campaign_needed": bool,
       "bottleneck_resource": str | None,
   }
   ```

**Excel exposure in `aps_functions.py`:**
```python
def run_ctp(xw_book=None):
    # Read CTP_Request sheet: SKU_ID, Qty_MT, Requested_Date
    # For each row, call ctp.capable_to_promise()
    # Write results to CTP_Output sheet
```

**New sheets needed in workbook:**
- `CTP_Request`: SKU_ID | Qty_MT | Requested_Date
- `CTP_Output`: all fields from return dict above + color coding (green = on time, red = late)

**VBA macro:** `Sub RunCTP() : RunPython "import aps_functions; aps_functions.run_ctp()" : End Sub`

---

## Part B — Industry-Agnostic Architecture

The codebase has **12 categories of steel-specific hardcoding** (identified by full audit). Making it industry-agnostic means moving every domain assumption into configuration — the engine should know nothing about steel, EAF, billets, or grades.

---

### B1. The Core Problem: What Is Hardcoded vs What Should Be Data-Driven

| Layer | Steel-Specific Assumption | Industry-Agnostic Replacement |
|-------|--------------------------|-------------------------------|
| Batch unit | Heat = 50 MT EAF capacity | `Batch_Size` in Resource_Master per production unit |
| Operation sequence | EAF→LRF→VD→CCM→RM fixed | Route defined per SKU in Routing sheet (DAG, any depth) |
| Optional operations | VD only for 3 grades | `Optional: Yes/No` flag per Routing row |
| Process time | CCM=50/60 based on billet family | `Cycle_Time` from Routing per (SKU, Resource) |
| Yield rates | CCM 95%, RM 88-92% by section | `Yield_Pct` in BOM per row |
| Changeover | Grade-to-grade matrix on RM | Changeover_Matrix, any resource |
| Material naming | FG-WR-, BIL-, EAF-OUT- prefixes | Category + Stage from SKU_Master; no prefix parsing |
| Grade system | 8 fixed grades with VD flags | Grades are just attribute values in SKU_Master |
| Section mm | 5.5/6.5/8.0/10.0/12.0mm fixed | `Attribute_1` in SKU_Master (generic dimension attribute) |
| Plant structure | BF + SMS + RM | Plants defined in Resource_Master |
| EAF charge recipe | Per-grade ingredient ratios | BOM rows of type INPUT per FG/intermediate |
| Loss byproducts | Slag/crop/scale per process | BOM rows of type BYPRODUCT per operation |

---

### B2. Changes Required by File

#### `engine/campaign.py` — Most Hardcoded File

**Remove:**
- `HEAT_SIZE_MT = 50.0` → Read from Resource_Master column `Batch_Size_MT` per primary production resource
- `GRADE_ORDER` dict → Replace with `Priority_Order` column in a Grade_Master sheet or SKU_Master
- `VD_GRADES` set → Replace with `Needs_Secondary_Op` boolean flag per SKU or grade in SKU_Master
- `LOW_CARBON_BILLET_GRADES` → Replace with `Billet_Family` column in SKU_Master
- `RM_YIELD_BY_SEC` dict → Read yield from BOM `Yield_Pct` column per BOM row
- `CCM_YIELD = 0.95` → BOM row for CCM operation
- `rm_minutes_for_qty()` → Replace with Routing-driven lookup; this function encodes rolling mill physics
- `billet_family_for_grade()` → Read from SKU_Master `Billet_Family` column
- `needs_vd_for_grade()` → Read from SKU_Master `Needs_VD` or generalized `Route_Variant` flag

**Campaign grouping key** (line 252): Currently `[Route_Family, Campaign_Group, Grade, Billet_Family, Needs_VD]`. Make generic:
- Read grouping key columns from a `Config` sheet row: `Campaign_Group_By = Route_Family, Product_Family, Variant`
- `Billet_Family` and `Needs_VD` become generic `Product_Family` and `Route_Variant`

**Rename fields:**
- `heats` → `batches` (a "heat" is steel-specific; "batch" is universal — pharma, food, chemicals all use batches)
- `billet_family` → `product_family`
- `needs_vd` → `route_variant`
- `section_mm` → `attribute_1` or keep as a generic product dimension attribute

#### `engine/scheduler.py`

**Remove:**
- `DEFAULT_MACHINE_GROUPS` dict — resource pools must come 100% from Resource_Master
- `EAF_TIME, LRF_TIME, VD_TIME, CCM_130, CCM_150` constants → all times from Routing
- `OPERATION_ORDER` dict — operation sequence must come from Routing `Sequence` column
- `OPERATION_ALIASES` dict — canonicalization should be in Routing sheet `Operation_Group` column
- `_ccm_time()` function — billet-family-based time logic; replace with `build_operation_times()`
- Default setup times per operation in `build_operation_times()` — make fallbacks configurable in Resource_Master `Default_Setup_Min` column

**Rename:**
- All references to "heat" → "batch"
- "campaign" can stay (campaigns are industry-agnostic enough — pharmaceutical batches, food runs, etc.)

**Make `OPERATION_ORDER` data-driven:**
```python
# Current
OPERATION_ORDER = {"EAF": 1, "LRF": 2, "VD": 3, "CCM": 4, "RM": 5}

# Future
operation_order = routing.set_index("Operation_Group")["Sequence"].to_dict()
```

#### `engine/capacity.py`

**Remove:**
- Hardcoded EAF/LRF/VD/CCM/RM operation handling
- Replace with: iterate over all unique `Operation_Group` values in Routing, sum demand hours from `build_operation_times()`

**New approach:**
```python
for op_group in routing["Operation_Group"].unique():
    op_campaigns = [c for c in campaigns if op_group in c["operation_times"]]
    demand_hrs[op_group] = sum(c["operation_times"][op_group] for c in op_campaigns) / 60
```

#### `engine/bom_explosion.py`

**Already largely generic.** Minor changes:
- Remove any steel-specific prefix assumptions in `simulate_material_commit()`
- Ensure `Flow_Type` parsing is case-insensitive and extensible (e.g., "CONSUME", "CONSUMED" all map to INPUT)
- Add support for `Yield_Pct` column in BOM (currently yield is computed separately in campaign.py; it should come from BOM)

#### `aps_functions.py`

**Remove:**
- `SUPPORTED_WORKBOOKS` set with steel-specific filenames — or make it a config value
- All `startswith("EAF")`, `startswith("LRF")` etc. prefix-based plant detection → replace with `Plant` column in Resource_Master and SKU_Master
- `PLANT_SORT_ORDER` dict with steel plant names → read plant ordering from Resource_Master
- `MATERIAL_TYPE_SORT_ORDER` with steel material type names → read from a `Material_Type_Master` sheet or Config
- `CAMPAIGN_SCHEDULE_HEADERS` steel-specific columns: `EAF_Start`, `CCM_Start` → make dynamic based on first/last operation stages from Routing
- `OPERATION_ALIASES` usage in output formatting → use Operation_Group from Routing

**Rename:**
- `run_bom_explosion` → keep (universal term)
- `run_capacity_map` → keep
- `run_schedule` → keep
- `run_scenario` → keep

**Key output fix — `_format_schedule_output()`:**
- Operation color coding currently hardcodes EAF=blue, LRF=amber, VD=lavender, CCM=green, RM=orange
- Move to a `Operation_Color` column in Resource_Master; read per resource

#### `build_template_v3.py`

This file is the **most domain-coupled** — it hardcodes the entire steel product catalog, charge recipes, and plant structure. For industry-agnostic use, this file should be:
1. Split into: `build_template_core.py` (generic workbook structure) + `steel_data_seed.py` (steel-specific example data)
2. The core template creates the sheet structure; the seed data populates example rows that any user replaces with their own data
3. All hardcoded SKUs, grades, sections, recipes → move to `steel_data_seed.py` as a reference implementation

---

### B3. New Configuration Architecture

**New sheet: `Config`** (key-value pairs for system-level settings)
| Key | Current Value | Description |
|-----|--------------|-------------|
| `Batch_Unit_Name` | Heat | What a production batch is called (Heat / Batch / Run / Lot) |
| `Primary_Batch_Resource_Group` | EAF | Which resource group defines batch size |
| `Default_Batch_Size_MT` | 50 | Fallback batch size |
| `Campaign_Group_By` | Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant | Comma-separated grouping keys |
| `Planning_Horizon_Days` | 14 | Default planning window |
| `Default_Solver_Limit_Sec` | 30 | CP-SAT time limit |
| `Queue_Enforcement` | Hard | Hard = infeasible if violated; Soft = penalize |
| `Min_Campaign_MT` | 100 | Minimum campaign size |
| `Max_Campaign_MT` | 500 | Maximum campaign size |

**Resource_Master new columns:**
| Column | Purpose |
|--------|---------|
| `Batch_Size_MT` | How many MT per batch on this resource |
| `Default_Cycle_Min` | Fallback cycle time if Routing has no entry |
| `Default_Setup_Min` | Fallback setup time |
| `Operation_Color` | Hex color for Gantt and output formatting |
| `Plant` | Which plant/area this resource belongs to |

**Routing sheet new columns:**
| Column | Purpose |
|--------|---------|
| `Sequence` | Integer operation order within the route (replaces OPERATION_ORDER dict) |
| `Is_Optional` | Boolean — can be skipped based on product variant |
| `Optional_Condition` | Column name in SKU_Master that determines if this step is needed |
| `Transfer_Time_Min` | Lag before next operation can start |
| `Operation_Group` | Canonical operation name (replaces OPERATION_ALIASES) |

**SKU_Master new columns:**
| Column | Purpose |
|--------|---------|
| `Route_Variant` | Which routing variant applies (replaces needs_vd) |
| `Product_Family` | Groups SKUs sharing same intermediate (replaces billet_family) |
| `Attribute_1` | First product dimension (replaces section_mm) |
| `Attribute_2` | Second product dimension (reserved) |
| `Batch_Yield_Pct` | Primary yield for this SKU at its production step |

---

### B4. What Industries Would This Then Support

After these changes, the same engine would handle:

| Industry | Batch Unit | Operation Sequence | Scheduling Driver |
|----------|-----------|-------------------|------------------|
| **Steel** (current) | Heat (50 MT) | EAF→LRF→VD→CCM→RM | Grade + billet family + section |
| **Pharmaceuticals** | Batch (kg/L) | Dispensing→Mixing→Granulation→Coating→Packaging | Product + batch record + stability |
| **Food/Beverage** | Run | Preparation→Cooking→Filling→Packaging | SKU + allergen family + CIP requirements |
| **Glass** | Melt (tonnes) | Furnace→Forming→Annealing→Inspection→Packing | Color + composition (no mixing between runs) |
| **Chemicals** | Campaign (reactor batch) | Reaction→Distillation→Drying→Packaging | Grade/spec + reactor chemistry |
| **Foundry** | Heat (melt) | Melting→Alloying→Pouring→Shakeout→Fettling | Alloy spec + pattern |
| **Paper/Pulp** | Reel/Order | Pulping→Refining→Paper Machine→Coating→Cutting | Grade + width + basis weight |
| **Aluminium Smelting** | Tap (pot line) | Reduction→Casting→Rolling/Extrusion | Alloy + shape + temper |

The minimal changes to enable any of these are: populate the Config, Routing, Resource_Master, SKU_Master, and BOM sheets with domain data. The engine runs identically.

---

### B5. What Stays Steel-Specific by Design

Some things should remain as a steel reference implementation, not deleted:
- `steel_data_seed.py` — the example data file
- EAF charge recipe logic in BOM (the chemistry ratios) — steel-specific but expressed as standard BOM rows
- Wire rod section yield table — becomes a lookup in Routing or BOM `Yield_Pct` column
- Grade priority sequence — becomes `Priority_Order` in a product master

---

## Part C — Implementation Order

| Phase | Change | Files | Effort | Value |
|-------|--------|-------|--------|-------|
| **P1** | Queue times enforcement | scheduler.py, aps_functions.py, workbook template | Medium | Critical — prevents infeasible plans |
| **P1** | Transfer times | scheduler.py, routing sheet | Low | Critical — removes impossible overlaps |
| **P2** | CTP engine + sheets | ctp.py (new), aps_functions.py, workbook template | Medium | High — enables customer promising |
| **P3** | Remove OPERATION_ORDER hardcoding | scheduler.py, capacity.py | Low | Medium — needed for generalization |
| **P3** | Resource_Master: add Batch_Size, Color, Default times | build_template_v3.py, scheduler.py, aps_functions.py | Low | High |
| **P3** | Routing: add Sequence, Is_Optional, Transfer_Time columns | build_template_v3.py, scheduler.py | Low | High |
| **P4** | Config sheet + Campaign_Group_By parameter | build_template_v3.py, campaign.py, aps_functions.py | Medium | Medium |
| **P4** | campaign.py: heats→batches, billet_family→product_family, needs_vd→route_variant | campaign.py | Low | High |
| **P4** | Remove prefix-based plant detection from aps_functions.py | aps_functions.py | Low | Medium |
| **P5** | Split build_template_v3.py into core + steel_data_seed | build_template_v3.py | Medium | Medium |
| **P5** | Operation colors from Resource_Master | aps_functions.py, build_template_v3.py | Low | Low |

---

## Appendix: Full Hardcoding Inventory (Quick Reference)

| File | Hardcoded Item | Line Range | Industry-Agnostic Fix |
|------|---------------|-----------|----------------------|
| campaign.py | HEAT_SIZE_MT = 50 | L19 | Resource_Master Batch_Size_MT |
| campaign.py | GRADE_ORDER dict | L24-33 | SKU_Master Priority_Order column |
| campaign.py | VD_GRADES set | L34 | SKU_Master Route_Variant column |
| campaign.py | LOW_CARBON_BILLET_GRADES | L36 | SKU_Master Product_Family column |
| campaign.py | RM_YIELD_BY_SEC dict | L21 | BOM Yield_Pct column |
| campaign.py | CCM_YIELD = 0.95 | L20 | BOM Yield_Pct column |
| campaign.py | rm_minutes_for_qty() | L51-58 | Routing Cycle_Time per SKU |
| scheduler.py | DEFAULT_MACHINE_GROUPS | L38-44 | Resource_Master only |
| scheduler.py | EAF_TIME, LRF_TIME etc | L19-23 | Routing Cycle_Time |
| scheduler.py | OPERATION_ORDER dict | L25 | Routing Sequence column |
| scheduler.py | OPERATION_ALIASES dict | L26-37 | Routing Operation_Group column |
| scheduler.py | _ccm_time() billet logic | L103 | Routing + Product_Family |
| scheduler.py | Default setup times | L212-216 | Resource_Master Default_Setup_Min |
| aps_functions.py | SUPPORTED_WORKBOOKS | L36 | Config sheet |
| aps_functions.py | startswith("EAF") plant logic | L917-927 | Resource_Master Plant column |
| aps_functions.py | PLANT_SORT_ORDER dict | L167-171 | Resource_Master sort order |
| aps_functions.py | MATERIAL_TYPE_SORT_ORDER | L173-195 | SKU_Master Category ordering |
| aps_functions.py | CAMPAIGN_SCHEDULE_HEADERS EAF_Start/CCM_Start | L54-73 | Dynamic from Routing first/last op |
| aps_functions.py | Operation color coding | ~L1100+ | Resource_Master Operation_Color |
| build_template_v3.py | Full grade/section product catalog | L194-204 | External seed data |
| build_template_v3.py | EAF charge recipes per grade | L332-340 | BOM rows only |
| build_template_v3.py | RM loss % by section | L328-329 | BOM Yield_Pct |
| build_template_v3.py | BF burden rates | L382-385 | BOM rows |
| build_template_v3.py | Initial inventory quantities | L404-471 | External seed data |
| build_template_v3.py | Plant color map | L603 | Resource_Master Operation_Color |
