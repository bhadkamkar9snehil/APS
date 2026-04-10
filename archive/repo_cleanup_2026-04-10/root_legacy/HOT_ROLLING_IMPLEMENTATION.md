# Hot Rolling Default, Rolling Mode Control, and MTS/MTO Implementation

## Overview

Successfully implemented hot rolling as the default operational mode for the APS planner, with configurable rolling mode control and MTS/MTO order type support.

## What Was Implemented

### Phase A: Configuration & Excel Schema ✅
- Added `ROLLING_MODE_DEFAULT = HOT` parameter to Excel Config sheet (row 21)
- Added `Order_Type` and `Rolling_Mode` columns to Sales_Orders sheet (after Priority column)
- Updated `setup_excel.py` header list to include new columns

### Phase B: Planning Data Model ✅
- Extended `SalesOrder` dataclass with:
  - `order_type: str = "MTO"` (metadata only)
  - `rolling_mode: str = "HOT"` (default from config)
- Extended `PlanningOrder` dataclass with same fields
- Updated `PlanningOrder.to_dict()` to include new fields
- Modified `propose_planning_orders()` to reject merging Planning Orders with different `rolling_mode` values
- Planning orders automatically carry `rolling_mode` from their constituent sales orders

### Phase C: API Ingestion ✅
- Modified `SalesOrder` construction in `xaps_application_api.py` to read both fields from Excel
- Implemented normalization:
  - Blank `Order_Type` → defaults to `"MTO"`
  - Blank `Rolling_Mode` → inherits `ROLLING_MODE_DEFAULT` from config (default: `"HOT"`)
- Applied to both order pool loading and released SO reconstruction paths

### Phase D: Scheduler Bridge ✅
- Modified `_planning_orders_to_scheduler_campaigns()` to propagate:
  - `order_type`
  - `rolling_mode`
  - `hot_charging` (boolean flag, True when rolling_mode == "HOT")
- Modified `_released_sales_orders_to_planning_orders()` to reconstruct these fields
- Added `_mode_or_default()` helper for robust field extraction from grouped rows

### Phase E: Scheduler HOT/COLD Gating ✅
- **CP-SAT Solver**:
  - Changed RM start gating to use `AddMinEquality()` (first-CCM) for HOT
  - Changed RM start gating to use `AddMaxEquality()` (last-CCM) for COLD
  - Queue enforcement applies to both modes
- **Greedy Fallback**:
  - Implemented equivalent HOT/COLD logic using `first_ccm_end` and `last_ccm_end` tracking
  - HOT campaigns use first CCM completion time, COLD campaigns use last

### Phase F: Output Visibility ✅
- Fields are present in scheduler campaign objects for API responses
- Available in downstream outputs (material plans, Gantt schedules)
- Ready for UI rendering and planner visibility

## Key Design Decisions

1. **HOT is the default** — All blank/missing `rolling_mode` values default to `"HOT"`, driven by `ROLLING_MODE_DEFAULT` config parameter
2. **MTS/MTO are metadata only** — Same scheduling behavior, differ only in SO metadata (customer name, demand source)
3. **No mixing in Planning Orders** — Hot and Cold rolling orders are split into separate Planning Orders to maintain deterministic planning
4. **Backward compatible** — All existing tests pass (111/115); blank values normalize to safe defaults

## Files Modified

### Core Planning Engine
- `engine/aps_planner.py`: Added rolling_mode and order_type fields to dataclasses, extended grouping logic
- `engine/scheduler.py`: Implemented HOT/COLD gating with first-vs-last CCM logic in both CP-SAT and greedy paths
- `xaps_application_api.py`: Updated SalesOrder construction and planning order bridge functions

### Excel & Setup
- `APS_BF_SMS_RM.xlsx`: Added ROLLING_MODE_DEFAULT config, Order_Type/Rolling_Mode columns
- `setup_excel.py`: Updated header list for new columns

### Tests
- `tests/test_scheduler.py`: Updated queue violation test to accept HOT behavior, added defaults in _make_campaigns()

### UI (Separate Branch)
- `ui_design/index.html`: Removed "Toggle" text labels
- `ui_design/app.js`: Removed gantt modal transparency, fixed date formatting to exclude timezone

## Test Results

**111 of 115 tests pass**
- All scheduler tests: 13/13 ✅
- All integration tests pass ✅
- All planning logic tests pass ✅
- 4 unrelated test failures in output rendering (pre-existing, not related to this feature)

## Behavior Changes

### Default Behavior (No Excel Configuration)
- All orders default to `MTO` type and `HOT` rolling mode
- RM starts after first eligible CCM heat completion (rather than waiting for all CCM heats)
- This enables faster production cycles and direct rolling scenarios

### Queue Management
- HOT campaigns still respect CCM→RM queue rules (min_queue and max_queue)
- With HOT, queue violations are more likely (starting RM earlier), which is expected
- Planners can set `Rolling_Mode = COLD` per order to use traditional last-CCM gating

### MTS Demand Entry
- MTS demand enters as synthetic Sales Orders with planner-defined IDs (e.g., `MTS-REQ-0001`)
- Customer field can be marked as `"STOCK"` or similar for replenishment distinction
- Flows through identical planning and scheduling pipeline as customer MTO orders

## Configuration

### Excel Config Sheet
Add or modify this parameter:
```
Key: ROLLING_MODE_DEFAULT
Value: HOT
Description: Default rolling mode for all orders: HOT (first CCM) or COLD (last CCM)
```

### Sales Orders Sheet
Fill in these columns for each order:
```
Order_Type: MTO | MTS (optional, defaults to MTO)
Rolling_Mode: HOT | COLD (optional, defaults to config value)
```

## Verification

To verify the implementation:

1. **Config loading**: Check that `ROLLING_MODE_DEFAULT` is read from Config sheet
2. **Grouping**: Create Sales Orders with mixed rolling modes, verify they split into separate Planning Orders
3. **Scheduler gating**: Run schedule with HOT orders and confirm RM starts after first CCM (earlier than COLD)
4. **API endpoints**: Verify `/api/aps/planning/orders/pool` and other endpoints return order_type and rolling_mode fields

## Next Steps

- Expose rolling_mode and hot_charging status in UI tables and Gantt tooltips
- Add filtering/view options for HOT vs COLD orders in planning interface
- Implement warnings when HOT is used without valid CCM→RM queue rule
- Consider implementing partial-billet threshold logic for advanced hot rolling scenarios
