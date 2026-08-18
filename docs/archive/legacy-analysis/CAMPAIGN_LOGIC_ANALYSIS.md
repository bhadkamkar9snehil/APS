# Campaign Creation Logic Analysis & Fixes

**Date:** April 4, 2026  
**Status:** Issues Identified & Solutions Provided

---

## Critical Issues Found

### Issue 1: No Deduplication of Sales Orders (CRITICAL)
**Location:** `engine/campaign.py`, line 612 in `build_campaigns()`  
**Severity:** CRITICAL - Direct cause of material double-counting

**Problem:**
- The function accepts `sales_orders` DataFrame without deduplicating by SO_ID
- If duplicate SO_IDs exist (like the SO-306 duplicate we found), they are processed multiple times
- This causes material requirements to be multiplied incorrectly
- Example: SO-306 appeared twice with different SKUs → double material shortage

**Root Cause:**
- Excel data entry can produce duplicate rows
- Data loader validates but doesn't enforce uniqueness
- Campaign logic assumes all rows are distinct orders

**Solution 1a - Add deduplication at data load (BEST FIX):**
```python
# In xaps_application_api.py, _load_all() function, line ~378:
so_raw = so_raw[so_raw["SO_ID"].notna() & (so_raw["SO_ID"] != 'nan')].reset_index(drop=True)

# ADD: Deduplicate by SO_ID, keeping first occurrence
so_raw = so_raw.drop_duplicates(subset=["SO_ID"], keep="first").reset_index(drop=True)
```

**Solution 1b - Add deduplication in campaign logic (DEFENSIVE FIX):**
```python
# In engine/campaign.py, line 612, in build_campaigns():
open_so = _normalize_sales_orders(sales_orders, skus=skus, config=config)

# ADD after normalization:
if not open_so.empty and "SO_ID" in open_so.columns:
    dupe_sos = open_so[open_so.duplicated(subset=["SO_ID"], keep=False)]["SO_ID"].unique()
    if len(dupe_sos) > 0:
        print(f"WARNING: Duplicate SO_IDs found: {dupe_sos}. Keeping first occurrence only.")
        open_so = open_so.drop_duplicates(subset=["SO_ID"], keep="first")
```

---

### Issue 2: Silent NaN Handling in Make_Qty_MT Filter (MODERATE)
**Location:** `engine/campaign.py`, line 618  
**Severity:** MODERATE - Could silently drop valid orders

**Problem:**
```python
make_so = open_so[open_so["Make_Qty_MT"] > 1e-6].copy()
```

- This comparison with NaN produces False, dropping the row silently
- No warning that orders with invalid quantities are being ignored
- Operator won't know some orders failed to process

**Solution:**
```python
# Check for NaN before filtering
invalid_qty = open_so[open_so["Make_Qty_MT"].isna()]
if not invalid_qty.empty:
    print(f"WARNING: {len(invalid_qty)} orders have invalid Make_Qty_MT (NaN). Skipping.")

make_so = open_so[open_so["Make_Qty_MT"] > 1e-6].copy()
```

---

### Issue 3: Unvalidated Delivery Dates Cause Scheduler NaN Errors (CRITICAL)
**Location:** `engine/campaign.py`, lines 389, 663, 811  
**Severity:** CRITICAL - Crashes scheduler

**Problem:**
- `pd.to_datetime(date_value)` without explicit error handling produces NaT (Not a Time) for invalid dates
- NaT values in delivery dates cause scheduler to fail with "cannot convert float NaN to integer"
- Example: If a column has mixed text/dates and coercion fails, result is NaT

**Root Cause:**
In `_normalize_sales_orders()` at line 389:
```python
so["Delivery_Date"] = pd.to_datetime(so["Delivery_Date"])  # Can produce NaT for invalid values
```

Then later at line 663 and 811:
```python
"due_date": pd.to_datetime(order["Delivery_Date"]),  # Using NaT value here
```

Then in scheduler at `engine/scheduler.py`, line 1202:
```python
due_min = max(0, int((rm_due - t0).total_seconds() / 60))  # NaT - datetime = NaN, can't convert to int
```

**Solution:**
```python
# In engine/campaign.py, _normalize_sales_orders(), after line 389:

# Validate dates - replace NaT with sensible default (e.g., today + 30 days)
so["Delivery_Date"] = pd.to_datetime(so["Delivery_Date"], errors="coerce")
invalid_dates = so[so["Delivery_Date"].isna()]
if not invalid_dates.empty:
    print(f"WARNING: {len(invalid_dates)} orders have invalid Delivery_Date. Using default (30 days out).")
    default_delivery = pd.Timestamp.now() + pd.Timedelta(days=30)
    so.loc[so["Delivery_Date"].isna(), "Delivery_Date"] = default_delivery

so["Order_Date"] = pd.to_datetime(so["Order_Date"], errors="coerce")
so.loc[so["Order_Date"].isna(), "Order_Date"] = pd.Timestamp.now()
```

---

### Issue 4: Missing Column Handling in Campaign Grouping (MODERATE)
**Location:** `engine/campaign.py`, lines 759-765  
**Severity:** MODERATE - Could cause KeyError at runtime

**Problem:**
```python
group_keys = [key.strip() for key in group_by_str.split(",") if key.strip() in make_so.columns]
if not group_keys:
    group_keys = [
        key
        for key in ["Route_Family", "Campaign_Group", "Grade", "Billet_Family", "Needs_VD"]
        if key in make_so.columns  # This fallback may not match normalization
    ]
grouped = make_so.groupby(group_keys, sort=False)  # Could fail if group_keys is empty
```

If all columns are missing, `group_keys` could be empty, causing:
- Empty groupby (creates single empty group)
- Or ambiguous behavior

**Solution:**
```python
group_by_str = str(
    (config or {}).get(
        "Campaign_Group_By",
        "Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant",
    )
    or ""
)
group_keys = [key.strip() for key in group_by_str.split(",") if key.strip() in make_so.columns]

if not group_keys:
    # Fallback to most reliable grouping: Grade always exists after normalization
    group_keys = ["Grade"]
    print(f"WARNING: Campaign_Group_By columns not found. Falling back to grouping by Grade only.")

grouped = make_so.groupby(group_keys, sort=False)
```

---

### Issue 5: No Validation of Group Keys Existence (LOW)
**Location:** `engine/campaign.py`, line 764  
**Severity:** LOW - Edge case, but could fail silently

**Problem:**
The fallback list includes columns that may not exist in all datasets:
```python
group_keys = [
    key
    for key in ["Route_Family", "Campaign_Group", "Grade", "Billet_Family", "Needs_VD"]
    if key in make_so.columns
]
```

If `_normalize_sales_orders()` doesn't create all these columns (due to missing input columns), grouping might behave unexpectedly.

**Solution:**
Ensure `_normalize_sales_orders()` always creates all expected columns explicitly:
```python
# In _normalize_sales_orders(), add at end before return:
required_columns = ["Route_Family", "Campaign_Group", "Grade", "Billet_Family", "Needs_VD", "Route_Variant"]
for col in required_columns:
    if col not in so.columns:
        if col == "Route_Family":
            so[col] = so.get("Grade", "") + "|" + so.get("Campaign_Group", "")
        elif col == "Campaign_Group":
            so[col] = so.get("Grade", "")
        # ... etc
```

---

### Issue 6: Silent Float Coercion in Qty Calculations (LOW)
**Location:** `engine/campaign.py`, lines 720, 796  
**Severity:** LOW - Defensive but good practice

**Problem:**
```python
remaining_qty = float(order["Make_Qty_MT"] or 0.0)
```

If `Make_Qty_MT` is already coerced to float at line 387, this is redundant. But if somehow a NaN slips through, this silently converts it to 0.0.

**Solution:**
```python
# Use explicit NaN check
remaining_qty = float(order["Make_Qty_MT"] or 0.0)
if remaining_qty <= 1e-6:
    continue  # Skip zero or invalid quantities
```

---

## Summary of Fixes (Priority Order)

| Priority | Issue | Location | Fix Type | Impact |
|----------|-------|----------|----------|--------|
| CRITICAL | Duplicate SO_IDs | data loader | Deduplication | Prevents material double-counting |
| CRITICAL | NaT dates cause NaN in scheduler | campaign logic | Default dates | Prevents scheduler crash |
| MODERATE | Silent NaN qty drops | campaign logic | Warning log | Operator visibility |
| MODERATE | Missing group keys | campaign logic | Fallback + log | Prevents silent failures |
| LOW | Group key existence | normalization | Column creation | Edge case hardening |
| LOW | Silent float coercion | campaign logic | Skip check | Defensive programming |

---

## Implementation Priority

### Phase 1 (Immediate - blocking issues):
1. Add SO_ID deduplication in `_load_all()` (Solution 1a)
2. Add NaT date validation (Solution 3)

### Phase 2 (Soon - data quality):
3. Add NaN qty warning (Solution 2)
4. Add group_keys fallback with logging (Solution 4)

### Phase 3 (Nice to have - robustness):
5. Ensure all expected columns exist (Solution 5)
6. Add explicit NaN checks in qty loops (Solution 6)

---

## Testing Recommendations

1. **Duplicate SO detection test:** Insert duplicate SO_IDs, verify deduplication
2. **Invalid date test:** Insert dates with NaT values, verify default assignment
3. **Zero quantity test:** Create SO with 0 qty, verify it's dropped with warning
4. **Missing columns test:** Create DataFrame without Campaign_Group, verify fallback works
5. **Material audit:** Verify material requirements match expected values (no double-counting)

