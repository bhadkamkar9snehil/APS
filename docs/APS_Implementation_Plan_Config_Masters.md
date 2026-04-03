# Implementation Plan: Config Sheet, Master Sheet Changes, Queue/Transfer Times & CTP

**Date:** April 2026
**Scope:** Industry-agnostic generalisation + Queue/Transfer time enforcement + Capable-to-Promise

---

## Why These Changes Together

Three problems are solved in one pass:

| Problem | Symptom | Root Cause |
|---------|---------|-----------|
| Plans can be physically infeasible | Scheduler places 4-hour gap between EAF and CCM — liquid steel solidifies | No queue time constraints in CP-SAT model |
| CCM→RM impossible overlaps | RM_Start can equal CCM_End — billet hasn't physically arrived | No transfer time between operations |
| No delivery promising | Planners answer "can I ship X by date Y?" by manually running full schedules | No CTP function |
| System locked to steel | Adding a new machine or grade requires Python changes | Every domain assumption (heat size, VD grades, section yields, op order) is a Python constant |

All four problems share the same fix: **move the domain assumptions from Python constants into the workbook master sheets**, then add the new constraint types that use those same sheets.

---

## Overview of All Changes

| Sheet / File | What Changes | Why |
|-------------|-------------|-----|
| **Config** (new sheet) | 11 key-value system parameters | Replaces Python constants that never needed to be in code |
| **Resource_Master** | +4 columns | Replaces OPERATION_ALIASES, hardcoded cycle/setup times, OPERATION_FILL colors, prefix-based plant detection |
| **Routing** | +4 columns | Replaces OPERATION_ORDER dict, VD_GRADES set, enables transfer times and optional-op logic |
| **SKU_Master** | +3 columns | Replaces billet_family_for_grade(), needs_vd_for_grade(), section_mm as dimension |
| **BOM** | +1 column | Replaces CCM_YIELD and RM_YIELD_BY_SEC constants |
| **Queue_Times** (new sheet) | 6-column constraint table | Enforces max hold time between operations in CP-SAT |
| **CTP_Request** (new sheet) | Input: SKU, Qty, Date | New CTP feature input |
| **CTP_Output** (new sheet) | Output: feasibility result | New CTP feature output |
| `aps_functions.py` | New readers, dynamic headers, plant detection fix | Consumes the new sheet/column data |
| `engine/campaign.py` | Config-driven batch size + groupby; SKU_Master-driven yields | Removes hardcoded constants |
| `engine/scheduler.py` | Data-driven op order + optional ops + transfer + queue constraints | Core engine changes |
| `engine/ctp.py` (new file) | `capable_to_promise()` function | New CTP feature |

---

## PART 1 — Config Sheet (New)

### Sheet Layout

Tab color: `70AD47` (green). Position: first sheet, before Sales_Orders.
Format: three-column table — `Key | Value | Description`.

| Key | Steel Default | Unit | Replaces in Python |
|-----|--------------|------|--------------------|
| `Batch_Unit_Name` | Heat | text | Docstring/label only — "Heat" appears in column headers |
| `Primary_Batch_Resource_Group` | EAF | text | Tells campaign.py which resource group defines batch capacity |
| `Default_Batch_Size_MT` | 50 | MT | `HEAT_SIZE_MT = 50.0` — `campaign.py` line 19 |
| `Campaign_Group_By` | Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant | CSV list | Hardcoded `group_keys` list — `campaign.py` line 252 |
| `Planning_Horizon_Days` | 14 | Days | Fallback in `_read_all()` — `aps_functions.py` line 345 |
| `Default_Solver_Limit_Sec` | 30 | sec | Fallback in `_read_all()` — `aps_functions.py` line 344 |
| `Min_Campaign_MT` | 100 | MT | `_get_params()` — `aps_functions.py` lines 391-394 |
| `Max_Campaign_MT` | 500 | MT | `_get_params()` — `aps_functions.py` lines 391-394 |
| `Queue_Enforcement` | Hard | Hard / Soft | New. Hard = CP-SAT constraint; Soft = penalty term |
| `Default_Section_Fallback` | 6.5 | mm | `fillna(6.5)` — `campaign.py` line 65, `_read_all()` line 378 |
| `Workbook_Name` | aps_bf_sms_rm | text | `SUPPORTED_WORKBOOKS` set — `aps_functions.py` line 36 |

### New Function: `_read_config(wb)` in `aps_functions.py`

```python
def _read_config(wb) -> dict:
    """Read Config sheet key-value table. Returns hardcoded defaults if sheet absent."""
    defaults = {
        "Batch_Unit_Name": "Heat",
        "Primary_Batch_Resource_Group": "EAF",
        "Default_Batch_Size_MT": 50.0,
        "Campaign_Group_By": "Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant",
        "Planning_Horizon_Days": 14,
        "Default_Solver_Limit_Sec": 30.0,
        "Min_Campaign_MT": 100.0,
        "Max_Campaign_MT": 500.0,
        "Queue_Enforcement": "Hard",
        "Default_Section_Fallback": 6.5,
        "Workbook_Name": None,
    }
    try:
        ws = wb.sheets["Config"]
        df = ws.range("A1").expand().options(pd.DataFrame, header=True, index=False).value
        df = df.dropna(subset=["Key"]).set_index("Key")
        for k, v in df["Value"].items():
            if k in defaults and v is not None:
                defaults[k] = v
    except Exception:
        pass   # sheet absent → all defaults apply; existing workbooks not broken
    return defaults
```

**Called at top of `_read_all()`:** `data["config"] = _read_config(wb)`

**Downstream uses of config:**
- `_get_params()` → read `Min_Campaign_MT` / `Max_Campaign_MT` from config first; Scenarios sheet second
- `build_campaigns(..., config=data["config"])` → reads `Default_Batch_Size_MT`, `Campaign_Group_By`
- `schedule(..., config=data["config"])` → reads `Queue_Enforcement`, `Planning_Horizon_Days`
- `_assert_supported_workbook()` → use `config["Workbook_Name"]` if set instead of the hardcoded set
- `_normalize_sales_orders()` → use `config["Default_Section_Fallback"]` instead of literal `6.5`

---

## PART 2 — Resource_Master: 4 New Columns

### New Columns (appended after existing `Status` column)

| Column | Type | Description | Replaces |
|--------|------|-------------|---------|
| `Operation_Group` | text | Canonical operation family name. All machines in the same group compete for the same tasks. | `OPERATION_ALIASES` dict — `scheduler.py` lines 26-36; `startswith("EAF")` plant detection — `aps_functions.py` lines 917-927 |
| `Default_Cycle_Min` | int | Fallback cycle time per batch if Routing sheet has no entry for this resource's group. | `EAF_TIME=90`, `LRF_TIME=40`, `VD_TIME=45`, `CCM_130=50`, `CCM_150=60` — `scheduler.py` lines 19-23 |
| `Default_Setup_Min` | int | Fallback setup time per batch. | Hardcoded setup per op inside `build_operation_times()` — `scheduler.py` lines 212-216 |
| `Operation_Color` | hex | 6-digit hex for Gantt and Schedule_Output cell coloring. | `OPERATION_FILL` dict — `aps_functions.py` lines 29-35 |

### Updated Headers in `build_template_v3.py` (line 577)

```python
# BEFORE (10 columns)
headers = [
    "Resource_ID", "Resource_Name", "Plant", "Type",
    "Avail_Hours_Day", "Max_Capacity_MT_Hr", "Capacity_MT_Day",
    "Heat_Size_MT", "Efficiency_%", "Status"
]

# AFTER (14 columns)
headers = [
    "Resource_ID", "Resource_Name", "Plant", "Type",
    "Avail_Hours_Day", "Max_Capacity_MT_Hr", "Capacity_MT_Day",
    "Heat_Size_MT", "Efficiency_%", "Status",
    "Operation_Group", "Default_Cycle_Min", "Default_Setup_Min", "Operation_Color"
]
```

### Steel Row Data (showing last 4 new fields per row)

| Resource_ID | ... existing ... | Operation_Group | Default_Cycle_Min | Default_Setup_Min | Operation_Color |
|------------|-----------------|----------------|------------------|------------------|----------------|
| EAF-01 | ... | EAF | 90 | 30 | DDEBF7 |
| EAF-02 | ... | EAF | 90 | 30 | DDEBF7 |
| LRF-01 | ... | LRF | 40 | 10 | FFCC99 |
| LRF-02 | ... | LRF | 40 | 10 | FFCC99 |
| LRF-03 | ... | LRF | 40 | 10 | FFCC99 |
| VD-01 | ... | VD | 45 | 15 | D9E1F2 |
| CCM-01 | ... | CCM | 60 | 20 | E2EFDA |
| CCM-02 | ... | CCM | 50 | 20 | E2EFDA |
| RM-01 | ... | RM | *(from Routing)* | 40 | F8CBB5 |
| RM-02 | ... | RM | *(from Routing)* | 40 | F8CBB5 |

### Engine Changes Consuming These Columns

**`scheduler.py` — `build_operation_times()` (lines 204-232):**
Add `resources=None` parameter. After Routing lookup (which already exists), add fallback block:
```python
# Fallback: read Default_Cycle_Min / Default_Setup_Min from Resource_Master
if resources is not None and "Operation_Group" in resources.columns:
    for _, res in resources.drop_duplicates("Operation_Group").iterrows():
        og = str(res.get("Operation_Group", "")).strip().upper()
        if og and og not in op_times:
            cyc = pd.to_numeric(res.get("Default_Cycle_Min"), errors="coerce")
            stp = pd.to_numeric(res.get("Default_Setup_Min"), errors="coerce")
            if pd.notna(cyc):
                op_times[og] = {"cycle": float(cyc), "setup": float(stp or 0)}
```
This means `EAF_TIME`, `LRF_TIME`, `VD_TIME`, `CCM_130`, `CCM_150` constants become dead code — keep them as fallback-of-last-resort but they will never be reached if Resource_Master is populated.

**`scheduler.py` — New helper `_build_op_lookup(resources)`:**
```python
def _build_op_lookup(resources: pd.DataFrame) -> dict:
    """Map every Resource_ID and Operation_Group to its canonical Operation_Group."""
    if resources is None or "Operation_Group" not in resources.columns:
        return OPERATION_ALIASES  # existing hardcoded fallback
    aliases = dict(OPERATION_ALIASES)  # start from existing
    for _, row in resources.iterrows():
        rid = str(row["Resource_ID"]).strip().upper()
        og  = str(row.get("Operation_Group", "")).strip().upper()
        if og:
            aliases[rid] = og
            aliases[og]  = og
    return aliases
```
Called once at top of `schedule()`. Result replaces all lookups currently using the hardcoded `OPERATION_ALIASES` dict.

**`aps_functions.py` — `_build_operation_fill(resources)` (replaces `OPERATION_FILL` at lines 29-35):**
```python
def _build_operation_fill(resources: pd.DataFrame) -> dict:
    fill = {}
    if "Operation_Group" in resources.columns and "Operation_Color" in resources.columns:
        for _, row in resources.drop_duplicates("Operation_Group").iterrows():
            og  = str(row["Operation_Group"]).strip()
            hex_c = str(row.get("Operation_Color", "")).strip().lstrip("#")
            if og and len(hex_c) == 6:
                fill[og] = (int(hex_c[0:2],16), int(hex_c[2:4],16), int(hex_c[4:6],16))
    return fill or OPERATION_FILL  # OPERATION_FILL constant kept as final fallback
```
Called in `_read_all()`: `data["operation_fill"] = _build_operation_fill(data["resources"])`
All downstream formatting functions receive `operation_fill` dict from `data` instead of importing the constant.

**`aps_functions.py` — plant detection (lines 917-927):**
```python
# BEFORE (5 startswith checks per resource_id)
if resource_id.startswith("EAF"): plant = "SMS"
elif resource_id.startswith("LRF"): plant = "SMS"
...

# AFTER (single dict lookup built once)
_res_plant_map = data["resources"].set_index("Resource_ID")["Plant"].to_dict()
plant = _res_plant_map.get(resource_id, "Other")
```

**`aps_functions.py` — `SCENARIO_OUTPUT_HEADERS` (lines 92-116):**
The per-machine utilisation columns (`BF-01`, `EAF-01`, ..., `RM-02`) are currently hardcoded.
Replace with dynamic construction:
```python
SCENARIO_BASE_HEADERS = [
    "Scenario","Heats","Campaigns","Released","Held",
    "On_Time_%","Weighted_Lateness_Hrs","Bottleneck",
    "Throughput_MT_Day","Avg_Margin_Hrs","Solver","Overloaded"
]
# At runtime, after _read_all():
scenario_headers = SCENARIO_BASE_HEADERS + list(data["resources"]["Resource_ID"])
```

---

## PART 3 — Routing Sheet: 4 New Columns

### New Columns (appended after existing `Note` column)

| Column | Type | Steel Default | Replaces |
|--------|------|--------------|---------|
| `Sequence` | int | 10, 20, 25, 30, 40 | `OPERATION_ORDER = {"EAF":1,"LRF":2,"VD":3,"CCM":4,"RM":5}` — `scheduler.py` line 25 |
| `Is_Optional` | Y / N | Y for VD rows; N for all others | `VD_GRADES` set + `needs_vd_for_grade()` — `campaign.py` lines 34 and 43-44 |
| `Optional_Condition` | text | `Needs_VD` (column name in SKU_Master to check) | Same as above — tells engine *which* SKU attribute gates this step |
| `Transfer_Time_Min` | int | 0 for EAF; 5 for LRF/VD/CCM; 30 for RM | **New.** Minimum gap required before the next operation can start at its machine |

### Updated Headers in `build_template_v3.py` (line 619)

```python
# BEFORE (12 columns)
headers = [
    "SKU_ID", "Grade", "Needs_VD", "Op_Seq", "Operation",
    "Resource_Group", "Preferred_Resource", "Cycle_Time_Min_Heat",
    "Setup_Time_Min", "Min_Campaign_MT", "Max_Campaign_MT", "Note"
]

# AFTER (16 columns)
headers = [
    "SKU_ID", "Grade", "Needs_VD", "Op_Seq", "Operation",
    "Resource_Group", "Preferred_Resource", "Cycle_Time_Min_Heat",
    "Setup_Time_Min", "Min_Campaign_MT", "Max_Campaign_MT", "Note",
    "Sequence", "Is_Optional", "Optional_Condition", "Transfer_Time_Min"
]
```

### Steel Row Data (4 new values appended to each existing tuple)

| Operation | ... existing ... | Sequence | Is_Optional | Optional_Condition | Transfer_Time_Min |
|-----------|-----------------|----------|------------|-------------------|-----------------|
| Melting (EAF) | ... | 10 | N | *(blank)* | 0 |
| Refining (LRF) | ... | 20 | N | *(blank)* | 5 |
| Degassing (VD) | ... | 25 | **Y** | **Needs_VD** | 5 |
| Casting (CCM) | ... | 30 | N | *(blank)* | 5 |
| Rolling (RM) | ... | 40 | N | *(blank)* | **30** |

> **`Optional_Condition = "Needs_VD"`** means: at runtime, look up the `Needs_VD` column on the SKU_Master row for this campaign's SKU. If it's "Y", run VD. If "N", skip VD. This makes it a data decision — no Python code changes needed to add a new optional step in any industry.

### Engine Changes Consuming These Columns

**`scheduler.py` — `_build_operation_order(routing)` (replaces `OPERATION_ORDER` dict):**
```python
def _build_operation_order(routing: pd.DataFrame) -> dict:
    """Build {Operation_Group: sequence_int} from Routing Sequence column."""
    if routing is None or "Sequence" not in routing.columns:
        return OPERATION_ORDER  # hardcoded fallback
    seq = routing.dropna(subset=["Sequence", "Operation"]).copy()
    seq["Op_Upper"] = seq["Operation"].str.strip().str.upper()
    return seq.groupby("Op_Upper")["Sequence"].min().to_dict()
```

**`scheduler.py` — Optional operation handling (replaces `needs_vd` flag):**
```python
# BEFORE: campaign carries needs_vd boolean; scheduler checks it
if heat["needs_vd"]:
    make_interval("VD", vd_duration, vd_machines, job_key)

# AFTER: routing row carries Is_Optional + Optional_Condition
for _, route_row in campaign_routing.iterrows():
    op_group   = str(route_row["Operation"]).strip().upper()
    is_optional = str(route_row.get("Is_Optional", "N")).strip().upper() == "Y"
    condition   = str(route_row.get("Optional_Condition", "")).strip()

    if is_optional and condition:
        # Look up the condition column on the SKU_Master row for this campaign's SKU
        flag = sku_master_lookup.get((campaign["sku_id"], condition), "N")
        if str(flag).strip().upper() != "Y":
            continue   # skip this operation for this campaign
    make_interval(op_group, duration, machines, job_key)
```

**`scheduler.py` — Transfer time enforcement (new constraint, replaces `model.Add(next.start >= prev.end)`):**
```python
def _extract_transfer_times(routing: pd.DataFrame) -> dict:
    """Returns {(from_op_group, to_op_group): transfer_minutes}"""
    if routing is None or "Transfer_Time_Min" not in routing.columns:
        return {}
    result = {}
    for sku_id, grp in routing.groupby("SKU_ID"):
        grp_s = grp.sort_values("Sequence")
        rows = grp_s[["Operation","Transfer_Time_Min"]].values.tolist()
        for i in range(len(rows) - 1):
            from_op = str(rows[i][0]).strip().upper()
            to_op   = str(rows[i+1][0]).strip().upper()
            t       = pd.to_numeric(rows[i+1][1], errors="coerce")
            if pd.notna(t) and t > 0:
                result[(from_op, to_op)] = int(t)
    return result

# In schedule(), after all intervals created:
transfer_times = _extract_transfer_times(routing)
for heat_tasks in all_heat_interval_tasks:
    sorted_ops = sorted(heat_tasks.items(), key=lambda kv: op_order.get(kv[0], 99))
    for i in range(len(sorted_ops) - 1):
        from_op, ft = sorted_ops[i]
        to_op,   tt = sorted_ops[i+1]
        transfer = transfer_times.get((from_op, to_op), 0)
        model.Add(tt["start"] >= ft["end"] + transfer)
        # NOTE: transfer=0 → same as existing precedence constraint; no regression
```

---

## PART 4 — SKU_Master: 3 New Columns

### New Columns (appended after existing `Safety_Stock_MT` column)

| Column | Type | Steel Default | Replaces |
|--------|------|--------------|---------|
| `Route_Variant` | Y / N | Same as `Needs_VD` for steel | Used by engine as a generic "variant flag"; in steel it equals Needs_VD |
| `Product_Family` | text | BIL-130 or BIL-150 | `billet_family_for_grade()` — `campaign.py` line 47-48; `LOW_CARBON_BILLET_GRADES` set line 36 |
| `Attribute_1` | float | section_mm value | Generic product dimension; campaign engine uses this instead of `Section_mm` directly |

> **`Needs_VD` column is kept** — it's referenced by name in the Routing sheet's `Optional_Condition` column. `Route_Variant` is the generalised alias that the engine reads in `campaign.py`. For steel, they are the same value. For a pharma deployment, `Route_Variant` might mean "Sterile" and the Routing `Optional_Condition` column would say "Route_Variant" instead of "Needs_VD".

### Updated Headers in `build_template_v3.py` (line 179)

```python
# BEFORE (10 columns)
headers = [
    "SKU_ID", "SKU_Name", "Category", "Grade", "Section_mm",
    "Coil_Wt_MT", "UOM", "Needs_VD", "Lead_Time_Days", "Safety_Stock_MT"
]

# AFTER (13 columns)
headers = [
    "SKU_ID", "SKU_Name", "Category", "Grade", "Section_mm",
    "Coil_Wt_MT", "UOM", "Needs_VD", "Lead_Time_Days", "Safety_Stock_MT",
    "Route_Variant", "Product_Family", "Attribute_1"
]
```

### Steel Row Data (3 new values appended)

| SKU_ID | ... existing ... | Route_Variant | Product_Family | Attribute_1 |
|--------|-----------------|--------------|----------------|-------------|
| FG-WR-SAE1008-55 | ... | N | BIL-130 | 5.5 |
| FG-WR-SAE1080-55 | ... | **Y** | BIL-150 | 5.5 |
| BIL-130-1008 | ... | N | BIL-130 | *(blank)* |
| BIL-150-1080 | ... | **Y** | BIL-150 | *(blank)* |

### Engine Changes Consuming These Columns

**`campaign.py` — `_normalize_sales_orders()` (lines 103-113):**
```python
# BEFORE: hardcoded grade function calls
so["Needs_VD"]     = so["Grade"].map(needs_vd_for_grade)
so["Billet_Family"] = so["Grade"].map(billet_family_for_grade)

# AFTER: join from SKU_Master; fall back to grade functions if columns absent
def _normalize_sales_orders(so, skus=None):
    if skus is not None and "Route_Variant" in skus.columns:
        lkp = skus.set_index("SKU_ID")
        so["Route_Variant"] = so["SKU_ID"].map(lkp["Route_Variant"]).fillna("N")
        # keep Needs_VD as alias for backward compat
        so["Needs_VD"] = so["Route_Variant"]
    else:
        so["Needs_VD"] = so["Grade"].map(needs_vd_for_grade).map({True:"Y",False:"N"})
        so["Route_Variant"] = so["Needs_VD"]

    if skus is not None and "Product_Family" in skus.columns:
        so["Product_Family"] = so["SKU_ID"].map(lkp["Product_Family"]).fillna("")
        # fill blanks from grade function
        mask = so["Product_Family"] == ""
        so.loc[mask, "Product_Family"] = so.loc[mask, "Grade"].map(billet_family_for_grade)
        so["Billet_Family"] = so["Product_Family"]  # alias
    else:
        so["Billet_Family"]  = so["Grade"].map(billet_family_for_grade)
        so["Product_Family"] = so["Billet_Family"]

    so["Route_Family"] = (
        so["Campaign_Group"].astype(str) + "|"
        + so["Grade"].astype(str)         + "|"
        + so["Product_Family"].astype(str) + "|RV:"
        + so["Route_Variant"].astype(str)
    )
```

**`campaign.py` — groupby key (line 252):**
```python
# BEFORE: hardcoded list
group_keys = ["Route_Family","Campaign_Group","Grade","Billet_Family","Needs_VD"]

# AFTER: config-driven
group_by_str = config.get("Campaign_Group_By",
    "Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant")
group_keys = [k.strip() for k in group_by_str.split(",") if k.strip() in so.columns]
```

**`campaign.py` — batch size (line 19 + `_heats_needed_from_lines()`):**
```python
# build_campaigns() signature change
def build_campaigns(so, bom, inventory, config=None, skus=None, ...):
    batch_size = float((config or {}).get("Default_Batch_Size_MT", HEAT_SIZE_MT))
    # pass batch_size into _heats_needed_from_lines()
```

---

## PART 5 — BOM Sheet: 1 New Column

### New Column (inserted after `Scrap_%`)

| Column | Type | Description | Replaces |
|--------|------|-------------|---------|
| `Yield_Pct` | float | Output yield as % of input. E.g., 95.0 means 5% loss. | `CCM_YIELD=0.95` — `campaign.py` line 20; `RM_YIELD_BY_SEC` dict — `campaign.py` line 21 |

`Yield_Pct` is the output-side view of yield; `Scrap_%` is the loss-side view. They are inverses: `Yield_Pct = 100 - Scrap_%`. Both columns are kept. Engine reads `Yield_Pct` first; if blank, falls back to `Scrap_%`; if both blank, assumes 100% yield.

### Updated Headers in `build_template_v3.py` (line 307)

```python
# BEFORE (9 columns)
headers = ["BOM_ID","Parent_SKU","Child_SKU","Flow_Type","Qty_Per","Scrap_%","Level","UOM","Note"]

# AFTER (10 columns)
headers = ["BOM_ID","Parent_SKU","Child_SKU","Flow_Type","Qty_Per","Scrap_%","Yield_Pct","Level","UOM","Note"]
```

### New Helper Function in `engine/bom_explosion.py`

```python
def _effective_yield(bom_row) -> float:
    """Returns decimal yield fraction [0.01, 1.0]."""
    yp = pd.to_numeric(bom_row.get("Yield_Pct"), errors="coerce")
    if pd.notna(yp):
        return max(0.01, min(1.0, yp / 100.0))
    scrap = pd.to_numeric(bom_row.get("Scrap_%"), errors="coerce")
    if pd.notna(scrap):
        return max(0.01, 1.0 - scrap / 100.0)
    return 1.0
```

In `campaign.py` — `_heats_needed_from_lines()`: replace `RM_YIELD_BY_SEC.get(section, DEFAULT_RM_YIELD)` with a BOM lookup for the SKU's RM row `Yield_Pct`. Fall back to `RM_YIELD_BY_SEC` dict if not found.

---

## PART 6 — Queue_Times Sheet (New — Critical Gap Fix)

### Sheet Layout

Tab color: `FF0000` (red — critical constraint sheet). Position: after `Changeover_Matrix`.

| Column | Type | Description |
|--------|------|-------------|
| `From_Operation` | text | Operation_Group of the operation that just finished |
| `To_Operation` | text | Operation_Group of the operation that must start next |
| `Min_Queue_Min` | int | Minimum gap (minutes) between From_end and To_start. 0 = back-to-back OK |
| `Max_Queue_Min` | int | Maximum gap (minutes). If exceeded: Hard = infeasible; Soft = penalised |
| `Enforcement` | Hard / Soft | Overrides Config `Queue_Enforcement` for this specific pair |
| `Note` | text | Physical reason |

### Steel Seed Data

| From | To | Min | Max | Enforcement | Note |
|------|----|-----|-----|-------------|------|
| EAF | LRF | 0 | 120 | Hard | Liquid steel temperature loss limit before refining |
| LRF | VD | 0 | 90 | Hard | Steel temp loss — must reach VD before 90 min |
| LRF | CCM | 0 | 90 | Hard | Steel temp loss — must reach CCM before 90 min |
| VD | CCM | 0 | 60 | Hard | VD to CCM must be near-immediate |
| CCM | RM | 30 | 480 | Soft | Billet cooling + transfer: min 30 min, 8 hr soft max |

### New Reader in `aps_functions.py`

```python
def _read_queue_times(wb) -> dict:
    """Returns {(from_op, to_op): {min, max, enforcement}}. Empty dict if sheet absent."""
    try:
        qt = _read_sheet_table(wb.sheets["Queue_Times"], ("A1",),
                               ("From_Operation","To_Operation"))
        result = {}
        for _, row in qt.iterrows():
            key = (str(row["From_Operation"]).strip().upper(),
                   str(row["To_Operation"]).strip().upper())
            result[key] = {
                "min": int(pd.to_numeric(row.get("Min_Queue_Min", 0),   errors="coerce") or 0),
                "max": int(pd.to_numeric(row.get("Max_Queue_Min", 9999), errors="coerce") or 9999),
                "enforcement": str(row.get("Enforcement","Hard")).strip(),
            }
        return result
    except Exception:
        return {}  # no queue constraints if sheet absent → current behavior preserved
```

Added in `_read_all()`: `data["queue_times"] = _read_queue_times(wb)`

### CP-SAT Constraint Implementation in `scheduler.py`

New parameter added to `schedule()` signature: `queue_times: dict | None = None`

Constraint block inserted after all heat intervals are created, before `model.Solve()`:

```python
QUEUE_VIOLATION_WEIGHT = 500   # penalty weight per minute of violation (Soft mode)

default_enforcement = (config or {}).get("Queue_Enforcement", "Hard")

for heat_tasks in all_heat_interval_tasks:
    sorted_ops = sorted(heat_tasks.items(), key=lambda kv: op_order.get(kv[0], 99))
    for i in range(len(sorted_ops) - 1):
        from_op, ft = sorted_ops[i]
        to_op,   tt = sorted_ops[i+1]
        qt = (queue_times or {}).get((from_op, to_op))
        if qt is None:
            continue

        min_q = qt["min"]
        max_q = qt["max"]
        enf   = qt.get("enforcement", default_enforcement)

        # Min queue (also handles Transfer_Time if > 0 — they stack)
        if min_q > 0:
            model.Add(tt["start"] >= ft["end"] + min_q)

        # Max queue
        if max_q < 9999:
            if enf.upper() == "HARD":
                model.Add(tt["start"] <= ft["end"] + max_q)
            else:  # SOFT — add violation variable to objective
                viol = model.NewIntVar(0, max_q * 10, f"qviol_{from_op}_{to_op}_{i}")
                model.Add(viol >= tt["start"] - (ft["end"] + max_q))
                obj_terms.append(viol * QUEUE_VIOLATION_WEIGHT)
```

**Output flag:** Add `Queue_Violation` column to `Schedule_Output`. For each consecutive operation pair per heat: compute actual gap, compare to max_queue from table. Color cell:
- **OK** (green) — gap ≤ 75% of max
- **WARN** (amber) — gap 75–100% of max
- **CRITICAL** (red) — gap > max (should be 0 if Hard enforcement, may appear with Soft)

---

## PART 7 — CTP: New File + Two Workbook Sheets

### New File: `engine/ctp.py`

```python
def capable_to_promise(
    sku_id: str,
    qty_mt: float,
    requested_date,
    campaigns: list,          # current committed/frozen plan
    resources: pd.DataFrame,
    bom: pd.DataFrame,
    inventory: dict,
    routing: pd.DataFrame,
    skus: pd.DataFrame,
    planning_start,
    config: dict = None,
    *,
    queue_times: dict = None,
    changeover_matrix: pd.DataFrame = None,
) -> dict:
```

**Algorithm (5 steps):**
1. **Build ghost SO** — single-row DataFrame for the requested `sku_id` + `qty_mt` + `requested_date`
2. **Build ghost campaigns** — call `build_campaigns([ghost_so], bom, inventory, config, skus)` — produces ghost campaign(s); note which existing campaign they join (if any)
3. **Check materials** — call `simulate_material_commit()` on ghost demand vs inventory *net of* all already-committed campaigns' `material_consumed` — detect any material gaps
4. **Freeze committed plan** — build `frozen_jobs` dict from all operations in the committed `campaigns` list with their already-scheduled start/end times
5. **Run targeted schedule** — call `schedule(ghost_campaigns, resources, frozen_jobs=frozen_jobs, queue_times=queue_times, ...)` — all existing jobs are frozen; only ghost campaign slots in around them; extract ghost's last operation end time

**Return dict:**
```python
{
    "sku_id":             sku_id,
    "qty_mt":             qty_mt,
    "requested_date":     requested_date,
    "earliest_delivery":  datetime | None,   # last op end of ghost campaign
    "feasible":           bool,              # earliest_delivery <= requested_date
    "lateness_days":      float,             # negative = early, positive = late
    "material_gaps":      list[dict],        # [{sku_id, shortage_qty, impacts_mt}]
    "joins_campaign":     str | None,        # Campaign_ID if ghost merged into existing
    "new_campaign_needed":bool,
    "bottleneck_resource":str | None,        # resource that limits delivery
    "solver_status":      str,               # OPTIMAL / FEASIBLE / INFEASIBLE
}
```

### New Workbook Sheets in `build_template_v3.py`

**`CTP_Request`** — planner fills this and clicks Run CTP:
| Column | Type | Description |
|--------|------|-------------|
| `Request_ID` | text | Reference for this query |
| `SKU_ID` | text | Must match a row in SKU_Master |
| `Qty_MT` | float | Quantity to promise |
| `Requested_Date` | date | Customer's requested delivery date |
| `Notes` | text | Free text |

**`CTP_Output`** — written by `run_ctp()`, one row per request:
| Column | Description |
|--------|-------------|
| `Request_ID` | Echoed from input |
| `SKU_ID` | Echoed |
| `Qty_MT` | Echoed |
| `Requested_Date` | Echoed |
| `Earliest_Delivery` | Computed delivery date from schedule |
| `Feasible` | YES / NO |
| `Lateness_Days` | Negative = early |
| `Material_Gaps` | Comma-separated shortage SKUs (if any) |
| `Joins_Campaign` | Existing campaign ID if merged, else blank |
| `Bottleneck` | Resource ID that limited the delivery |
| `Solver_Status` | OPTIMAL / FEASIBLE / INFEASIBLE |

Row color coding: green = feasible, red = infeasible, amber = feasible but ≤ 2 days margin.

### `run_ctp()` in `aps_functions.py`

```python
def run_ctp(xw_book=None):
    wb    = xw_book or _wb()
    data  = _read_all(wb)
    reqs  = _read_sheet_table(wb.sheets["CTP_Request"], ("A1",),
                               ("Request_ID","SKU_ID","Qty_MT","Requested_Date"))
    committed = _get_committed_campaigns(data)  # reads Campaign_Schedule frozen rows
    results = []
    for _, req in reqs.dropna(subset=["SKU_ID","Qty_MT"]).iterrows():
        r = ctp.capable_to_promise(
            sku_id=req["SKU_ID"], qty_mt=float(req["Qty_MT"]),
            requested_date=req["Requested_Date"],
            campaigns=committed,
            resources=data["resources"], bom=data["bom"],
            inventory=inventory_map(data["inventory"]),
            routing=data["routing"], skus=data["skus"],
            planning_start=datetime.now(),
            config=data["config"],
            queue_times=data.get("queue_times"),
            changeover_matrix=data.get("changeover"),
        )
        results.append(r)
    _render_ctp_output(wb, results)
```

**VBA macro added to workbook:**
```vba
Sub RunCTP()
    RunPython "import aps_functions; aps_functions.run_ctp()"
End Sub
```

---

## Implementation Phases

### Phase 1 — Workbook Schema Only (`build_template_v3.py`)
*No engine changes. Old workbooks still work. Run `python build_template_v3.py` to regenerate.*

| Step | Change |
|------|--------|
| 1a | Add `Config` sheet (11 rows) |
| 1b | Add `Queue_Times` sheet (5 steel rows) |
| 1c | Add 4 columns to `Resource_Master` data rows |
| 1d | Add 4 columns to `Routing` data rows |
| 1e | Add 3 columns to `SKU_Master` data rows |
| 1f | Add `Yield_Pct` column to `BOM` data rows |
| 1g | Add `CTP_Request` and `CTP_Output` shell sheets |

### Phase 2 — Readers & Output Constants (`aps_functions.py`, `campaign.py`)
*Engine still runs; new data is available to downstream code.*

| Step | Change | Replaces |
|------|--------|---------|
| 2a | Add `_read_config(wb)` | Nothing yet — just makes config available |
| 2b | Add `_read_queue_times(wb)` | Nothing yet — just makes queue data available |
| 2c | Add `_build_operation_fill(resources)` | `OPERATION_FILL` constant |
| 2d | Make `SCENARIO_OUTPUT_HEADERS` dynamic | Hardcoded machine ID columns |
| 2e | Replace prefix plant detection with Resource_Master `Plant` column | `startswith("EAF")` etc |
| 2f | Add `skus` param to `_normalize_sales_orders()`; join `Route_Variant`, `Product_Family` | `needs_vd_for_grade()`, `billet_family_for_grade()` |
| 2g | Make campaign groupby key config-driven | Hardcoded `group_keys` list |
| 2h | Make batch size config-driven in `build_campaigns()` | `HEAT_SIZE_MT = 50.0` |

### Phase 3 — Scheduling Engine (`scheduler.py`, new `engine/ctp.py`)

| Step | Change | Replaces / New |
|------|--------|---------------|
| 3a | Add `_build_operation_order(routing)` | `OPERATION_ORDER` dict |
| 3b | Add `_build_op_lookup(resources)` | `OPERATION_ALIASES` dict |
| 3c | Add `resources` param to `build_operation_times()` | `EAF_TIME`, `LRF_TIME` etc constants |
| 3d | Add `_extract_transfer_times(routing)` + enforce in model | **New** transfer time constraints |
| 3e | Add queue time constraint block using `Queue_Times` data | **New** max-hold constraints |
| 3f | Update `schedule()` + `_greedy_fallback()` signatures: add `queue_times`, `config` | Propagate new params |
| 3g | Replace `needs_vd` flag with `Is_Optional`+`Optional_Condition` routing check | `VD_GRADES` set |
| 3h | Add `_effective_yield()` to `bom_explosion.py`; use in `campaign.py` | `CCM_YIELD`, `RM_YIELD_BY_SEC` |
| 3i | Create `engine/ctp.py` | **New file** |
| 3j | Add `run_ctp()` to `aps_functions.py` + `_render_ctp_output()` | **New function** |

---

## Backward Compatibility Rules

Every new column and sheet degrades gracefully if absent:

| New element | Missing behavior |
|-------------|-----------------|
| `Config` sheet | All defaults apply — identical to current hardcoded constants |
| `Queue_Times` sheet | No queue constraints enforced — current behavior |
| `Operation_Group` column in Resource_Master | Falls back to `OPERATION_ALIASES` dict |
| `Sequence` column in Routing | Falls back to `OPERATION_ORDER` dict |
| `Is_Optional` / `Optional_Condition` in Routing | Falls back to `needs_vd` flag on campaign |
| `Transfer_Time_Min` in Routing | No transfer time enforced — current behavior |
| `Route_Variant` / `Product_Family` in SKU_Master | Falls back to `needs_vd_for_grade()` / `billet_family_for_grade()` |
| `Yield_Pct` in BOM | Falls back to `Scrap_%`, then to `CCM_YIELD` / `RM_YIELD_BY_SEC` |

**No existing workbook is broken. Only the regenerated template has the new columns.**

---

## Verification Checklist

1. `python build_template_v3.py` completes without error → verify: Config (11 rows), Queue_Times (5 rows), Resource_Master (14 cols), Routing (16 cols), SKU_Master (13 cols), BOM (10 cols), CTP_Request + CTP_Output sheets present

2. Open workbook → click `Run Schedule` → schedule completes; `Schedule_Output` has `Queue_Violation` column populated

3. Put a test row in `CTP_Request` (valid FG SKU, realistic qty, date 7 days from now) → click `Run CTP` → `CTP_Output` populates with feasibility result and earliest delivery date

4. Remove `Config` sheet from workbook → click `Run Schedule` → identical result to having Config with defaults (no crash, no behavior change)

5. Set `Default_Batch_Size_MT = 40` in Config → click `Run Schedule` → verify heat count increases approximately 25% vs default 50 MT

6. Add a row to `Queue_Times`: `EAF, CCM, 0, 10, Hard` (10-minute max EAF→CCM gap) → click `Run Schedule` → verify solver cannot find feasible solution (expected: switches to greedy fallback or returns INFEASIBLE) — confirms Hard queue constraints are active
