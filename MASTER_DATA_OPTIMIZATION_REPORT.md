# Master Data Optimization Report - APS Steel Plant
**Date:** 2026-04-08  
**Status:** Critical Issues Identified

---

## Executive Summary

The master data has **5 critical structural issues** preventing proper APS operation:

| Issue | Impact | Severity |
|-------|--------|----------|
| 3 orphaned SO rows | Cannot be planned | HIGH |
| 26 SKUs missing routing | Cannot schedule production | CRITICAL |
| Missing product-type classification | Cannot identify FG vs intermediate | HIGH |
| Incomplete SO-SKU relationships | Planning uncertainty | MEDIUM |
| Missing safety stock/lead time specs | Inventory planning fails | MEDIUM |

---

## Issue Details & Fixes

### ISSUE #1: Orphaned Sales Orders (3 rows)
**Problem:** 3 rows in Sales_Orders sheet have null SO_ID, Customer, Grade, and SKU_ID.

**Root Cause:**  
Excel template has blank template rows that weren't cleaned up during data load.

**Fix:**
```
DELETE rows 2, 4, 5 from Sales_Orders sheet (the ones with all nulls)
```

**Steel Industry Context:**  
Every SO must have:
- SO_ID (order tracking)
- Grade (SAE/CHQ/CrMo specification)
- Section_mm (wire rod diameter)
- Qty_MT (tonnage)

---

### ISSUE #2: Missing Routing for 26 SKUs (CRITICAL)
**Problem:** 26 of 79 producible SKUs have no routing defined.

**Affected SKUs:**
```
Intermediate Process Outputs (need routing):
  - EAF-OUT-SAE1008, EAF-OUT-SAE1018, EAF-OUT-SAE1035, EAF-OUT-SAE1045,
    EAF-OUT-SAE1065, EAF-OUT-SAE1080, EAF-OUT-CHQ1006, EAF-OUT-CrMo4140
  
  - LRF-OUT-SAE1008, LRF-OUT-SAE1018, LRF-OUT-SAE1035, LRF-OUT-SAE1045,
    LRF-OUT-SAE1065, LRF-OUT-SAE1080, LRF-OUT-CHQ1006, LRF-OUT-CrMo4140
  
  - VD-OUT-SAE1080, VD-OUT-CHQ1006, VD-OUT-CrMo4140
  
Raw Materials (need master record):
  - BF-RAW-MIX, BF-HM (blast furnace raw materials)
```

**Root Cause:**  
Routing sheet was built only for finalized goods and billets.  
Intermediate heat/liquid steel batch tracking missing.

**Fix - Create Routing for Process Outputs:**

Steel plant processes produce intermediate **batches** that must be tracked through equipment:

1. **EAF outputs** → next step is LRF (refining)
   - Route: EAF → LRF → {VD if needed} → CCM
   - Each EAF heat (50 MT) becomes an input to LRF

2. **LRF outputs** → next step is VD (if needed) or CCM (casting)
   - Grade 1080, CHQ1006, CrMo4140 REQUIRE VD
   - Others go direct to CCM

3. **VD outputs** → CCM (casting billet)
   - VD output is liquid steel → CCM casting

4. **CCM outputs** → RM (rolling mill) after cooling as billets
   - CCM outputs are billets (BIL-130 or BIL-150)
   - **These already have routing**

**Recommended Routing Setup:**

| SKU_ID | Operation | Primary_Resource | Duration_Min | Next_Op |
|--------|-----------|-------------------|--------------|---------|
| EAF-OUT-* | REFINING | LRF | 40 | LRF |
| LRF-OUT-* (1080,CHQ,CrMo) | DEGASSING | VD-01 | 45 | VD |
| LRF-OUT-* (others) | CASTING | CCM | 60 | CCM |
| VD-OUT-* | CASTING | CCM | 60 | CCM |

---

### ISSUE #3: Product Type Classification Missing
**Problem:** SKU_Master.Category doesn't distinguish FG vs intermediate vs raw materials.

**Current Categories:**
```
"FG-WR-SAE1008-55"  → Category: "Wire Rod SAE 1008..." (description, not type)
"BIL-130-1008"      → Category: "Steel Billet" (too vague)
"RM-OUT-SAE1008-55" → Category: ??? (missing)
```

**Root Cause:**  
Product type was derived from SKU prefix naming convention only.  
Not explicitly categorized for APS use.

**Fix - Add/Update Product_Type Column:**

```
Product_Type values to use:

RAW_MATERIAL
  - BF-RAW-MIX, BF-HM, RM-FECR (scrap)

PROCESS_INTERMEDIATE (transient batches in SMS/RM)
  - EAF-OUT-* (liquid steel after melting)
  - LRF-OUT-* (liquid steel after refining)
  - VD-OUT-*  (liquid steel after degassing)

PRODUCTION_INTERMEDIATE (stable material between operations)
  - BIL-130-*, BIL-150-* (cast billets, can be stored)
  
FINISHED_GOODS (customer delivery items)
  - FG-WR-SAE*-* (wire rod coils)
```

---

### ISSUE #4: Sales Order → Material Mapping Incomplete
**Problem:** 21 unique SKU_IDs have SO demand, but only 16 are in SKU_Master properly.

**Demanded SKUs:**
```
FG-WR-SAE1008-55/65/80/100/120 (5 variants)
FG-WR-SAE1018-55/65/80/100/120 (5 variants)
FG-WR-SAE1035-30/65/80 (3 variants)
FG-WR-SAE1045-30/40/55 (3 variants)
FG-WR-SAE1065-40/50 (2 variants)
FG-WR-SAE1080-40/50 (2 variants)
```

**Root Cause:**  
SKU_Master has 95 SKUs but only 21 are relevant for current demand.  
Other 74 SKUs are ghost entries or future-planned variants.

**Fix:**
1. Keep SKUs with sales demand active
2. Mark unused SKUs as INACTIVE (new column)
3. Delete truly unused rows to reduce noise

---

### ISSUE #5: Lead Times & Safety Stock Under-Specified
**Problem:** 74 of 95 SKUs have NULL or default values for:
- Lead_Time_Days
- Safety_Stock_MT

**Impact:**  
- Cannot calculate replenishment orders accurately
- Cannot size buffers for supply chain uncertainty

**Fix - Steel Industry Standards:**

By material type:
```
RAW MATERIALS (ferroalloys, scrap):
  Lead_Time_Days: 7-14 (external supply)
  Safety_Stock_MT: 2-3 heats (~100-150 MT)

BILLETS (internal production):
  Lead_Time_Days: 1-2 (CCM capacity)
  Safety_Stock_MT: 1-2 heats (~50-100 MT)

WIRE ROD (FG):
  Lead_Time_Days: 0.5 (RM is bottleneck)
  Safety_Stock_MT: 0.5-1 heat (~25-50 MT)
```

---

## Recommended Master Data Structure

### SKU_Master (enhanced)

```
Columns needed:

SKU_ID              (PK) - BIL-130-1008, FG-WR-SAE1045-30, etc.
SKU_Name            Product name/description
Product_Type        RAW_MATERIAL | PROCESS_INTERMEDIATE | PRODUCTION_INTERMEDIATE | FINISHED_GOODS
Category            Steel grade (1008, 1045, CrMo4140, etc.)
Grade               Full name (SAE 1008, Cr-Mo 4140, etc.)
Section_mm          Wire rod section (for FG)
Density_MT_per_coil Weight per coil (for FG/billets)

Lead_Time_Days      Procurement/production lead time
Safety_Stock_MT     Min buffer to maintain
Reorder_Point_MT    Trigger for replenishment

Route_Primary       Primary resource (e.g., CCM-01)
Billet_Family       For FG: which billet → BIL-130 or BIL-150
Needs_VD            T/F - requires vacuum degassing

Status              ACTIVE | INACTIVE (for clean data)
Attribute_1..N      Flexible fields for future
```

### Sales_Orders (cleaned)

```
SO_ID               (PK) SO-001, SO-002, ...
Customer            Customer name
Region              Geographic region
Grade               Steel grade (SAE 1008, Cr-Mo 4140)
Section_mm          Wire rod section mm
Order_Qty_MT        Tonnage ordered
Coils_Count         Number of coils
SKU_ID              (FK) FG-WR-SAE1008-55 ← MUST NOT BE NULL
Product_Type        (denormalized from SKU_Master for quick access)

Order_Date          When SO was placed
Delivery_Date       When customer wants delivery
Priority            URGENT | HIGH | NORMAL | LOW

Status              OPEN | CONFIRMED | COMPLETED
Campaign_ID         (if assigned) CMP-001
Campaign_Group      (if assigned) CMP-SAE108
```

---

## Implementation Plan

### Phase 1: Data Cleanup (IMMEDIATE)
- [ ] Delete 3 orphaned rows from Sales_Orders
- [ ] Add Product_Type column to SKU_Master
- [ ] Fill Product_Type for all 95 SKUs
- [ ] Add Lead_Time_Days & Safety_Stock_MT (use steel industry defaults)
- [ ] Verify all SO rows have SKU_ID (foreign key constraint)

### Phase 2: Routing Completeness (HIGH PRIORITY)
- [ ] Add routing for 8 EAF-OUT variants (→ LRF)
- [ ] Add routing for 16 LRF-OUT variants (→ VD or CCM)
- [ ] Add routing for 3 VD-OUT variants (→ CCM)
- [ ] Add raw material specs (BF-RAW-MIX, BF-HM)

### Phase 3: Material Relationships (MEDIUM PRIORITY)
- [ ] Complete BOM for all demanded SKUs
- [ ] Add alternative bom routing (e.g., direct EAF→CCM vs EAF→LRF→CCM)
- [ ] Define changeover matrices for grade transitions

### Phase 4: Inventory Consistency (LOW PRIORITY)
- [ ] Mark INACTIVE SKUs in inventory with Status = "INACTIVE"
- [ ] Consolidate duplicate entries
- [ ] Add lot traceability fields

---

## Steel Plant APS Key Principles

1. **Heat & Batch Thinking**
   - SMS produces ~50 MT heats (EAF → LRF → VD? → CCM)
   - RM coils come from CCM billets (50+ coils per heat)
   - Track BOTH heat-level and coil-level demands

2. **Process Sequence Lock**
   - EAF output → MUST go to LRF (no alternatives)
   - LRF output → VD (if grade requires) OR CCM
   - Cannot skip operations for a grade

3. **Yield Impact**
   - Each operation has scrap loss (88-95% yield)
   - BOM must account for cumulative yield
   - 1 MT FG = ~1.1 MT billet = ~1.2 MT liquid steel

4. **Equipment Specialization**
   - EAF: melting, grade mixing
   - LRF: refining, chemistry control
   - VD: ultra-low carbon, inclusion control
   - CCM: casting to billets
   - RM: rolling to final section

5. **Campaign Grouping**
   - Group SOs by grade to minimize RM changeovers
   - Billets from same CCM cast stay together
   - Plan EAF→LRF→CCM as integrated campaign

---

## Validation Queries

After implementing fixes, run these to validate:

```sql
-- No orphaned SOs
SELECT COUNT(*) FROM Sales_Orders WHERE SO_ID IS NULL;  → should be 0

-- All SO SKUs exist
SELECT COUNT(DISTINCT so.SKU_ID) FROM Sales_Orders so
LEFT JOIN SKU_Master sku ON so.SKU_ID = sku.SKU_ID
WHERE sku.SKU_ID IS NULL;  → should be 0

-- All producible SKUs have routing
SELECT COUNT(DISTINCT parent_sku) FROM BOM
WHERE parent_sku NOT IN (SELECT SKU_ID FROM Routing);  → should be 0

-- All input materials have inventory
SELECT COUNT(DISTINCT child_sku) FROM BOM WHERE flow_type = 'INPUT'
AND child_sku NOT IN (SELECT SKU_ID FROM Inventory);  → should be 0
```

---

## Files to Update

1. **APS_BF_SMS_RM.xlsx**
   - Sales_Orders: Delete rows 2, 4, 5
   - SKU_Master: Add Product_Type, Lead_Time_Days, Safety_Stock_MT columns
   - Routing: Add 27 new route entries for intermediate outputs
   - BOM: Verify completeness

2. **data/loader.py**
   - Add validation for Product_Type
   - Add check for SO→SKU foreign key
   - Warn if Lead_Time_Days not set

3. **engine/scheduler.py**
   - Update to use Product_Type (not just SKU prefix parsing)
   - Add process sequence validation

---

## Success Criteria

✓ Zero orphaned SO rows  
✓ 100% routing coverage (all 79 producible SKUs have route)  
✓ All FG SKUs can trace back to raw materials via BOM  
✓ Lead time & safety stock specified for all SKUs  
✓ Campaign grouping logic can assign SOs to campaigns  
✓ Scheduler completes without "missing material" errors  

---

**Next Step:** Implement Phase 1 (cleanup) immediately to unblock planning.
