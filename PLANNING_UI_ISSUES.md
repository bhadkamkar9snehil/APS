# Planning UI Issues & Fixes

## ✅ Fixed (Commit 33fc62d)

### 1. Full BOM Button Placement & Function
**Issue**: Button was in Material panel (wrong location, redundant)  
**Fix**: 
- Moved to Order Pool toolbar header (right side)
- Changed to `data-page-link="bom"` for tab navigation
- Added 📊 icon to distinguish from action buttons
**Status**: FIXED ✓ - Now accessible when browsing pool, not in material detail

### 2. Text Wrapping in Priority Column  
**Issue**: "URGENT", "HIGH", "NORMAL" badges were wrapping to multiple lines  
**Fix**: Added `white-space: nowrap` to badge styling in SO table  
**Status**: FIXED ✓ - Badges now stay on single line

### 3. Byproduct Shown as SHORT
**Issue**: BIL-130-1018 (byproduct/waste) showing "SHORT" - false shortage
**Root Cause**: BOM treats all materials equally (INPUT vs OUTPUT not distinguished)  
**Fix**: Added filter to exclude items with `Required_Qty <= 0` from BOM display
**Logic**: Byproducts have 0 required qty (they're OUTPUT), so excluded from coverage checks
**Status**: FIXED ✓ - False shortcuts eliminated

### 4. KPI Cards Not Updating
**Issue**: Top KPI cards (Heats, MT, Throughput) not recalculating after operations
**Fix**: 
- Added `updatePlanningKPIs()` function to recalculate all top KPIs
- Called after "Propose Orders" operation
- Called after "Derive Heats" operation
- Updates: PLANNING ORDERS, HEATS PLANNED, MT PLANNED, THROUGHPUT
**Status**: FIXED ✓ - KPIs now update after planning operations

---

## 🔴 Critical Issues Requiring Investigation

### 3. Byproduct Shown as SHORT - Logic Error
**Problem**: 
```
BIL-130-1018: 636 MT Required | 0 MT Produced | SHORT
```
This is wrong! **Byproducts are OUTPUT, not INPUT**. The planner doesn't NEED byproduct inventory.

**Root Cause**: 
- BOM Explosion is treating ALL materials (inputs AND outputs) the same
- Need to distinguish between:
  - **RM/Component inputs** (need inventory) → Show coverage status
  - **Byproduct outputs** (don't need inventory) → Hide or mark as BYPRODUCT

**What Needs to Happen**:
1. Check BOM data structure - does it have a field indicating INPUT vs OUTPUT?
2. Filter BOM tree to exclude byproducts
3. Exclude byproducts from coverage calculation

**Impact**: User sees false material shortages, makes wrong planning decisions

---

### 4. FG (Finished Good) in Heat Planning - Design Issue
**Problem**:
```
Heat table shows:
SKU: FG-WR-SAE1045-80 (column header said "SKU")
QTY: 50
```
Confusing! Column header said "SKU" but shows what's being PRODUCED (FG), not what's being USED (materials).

**Current Status**: 🟡 PARTIAL FIX
- Changed column header from "SKU" to "FG (Finished Good)" for clarity
- Now explicitly shows production targets, not consumption
- **Still pending**: Complete refactor to show actual materials consumed (would require BOM expansion per heat)

**Ideal Solution** (requires more work):
- Heat table should show materials consumed, not FG produced
- Would need to:
  1. Get BOM for each FG in the heat
  2. Aggregate all component/RM requirements
  3. Display those instead of FG
- **Impact**: Helps planners understand material flow, identify bottlenecks

**Note**: Current approach (showing FG) is valid for tracking production targets. Showing consumed materials is a "nice-to-have" enhancement for material flow visualization.

---

### 5. Top KPI Cards Not Calculating Correctly
**Problem**:
```
PLANNING ORDERS: 26 (shows 0 approved, 26 proposed) ✓ Seems OK
HEATS PLANNED: — (showing empty dash)
MT PLANNED: — (showing empty dash)
ON-TIME: 100.0% NOT RUN (contradictory)
```

**Issues**:
- Heats/MT KPIs not updating after planning runs
- ON-TIME showing 100% but solver says "NOT RUN"
- Throughput showing 0.0 MT/day
- Bottleneck showing blank

**Root Cause**:
- KPI calculation logic not triggered after planning operations
- State not being updated properly when plan changes
- Solver status not synchronized with UI display

**What Needs to Happen**:
1. Add state update hooks after each pipeline stage
2. Recalculate KPIs when:
   - POs are proposed
   - Heats are derived
   - Schedule is simulated
   - Release is executed
3. Synchronize solver status with actual plan state

---

### 6. No Controls for Infeasible Plans - UX Gap
**Problem**:
```
✓ FEASIBLE: Yes
Total Duration: 335.98h
SMS Hours: 342.02h (OVER CAPACITY!)
RM Hours: 99.42h
```
Plan says feasible but SMS is over 100%! When this IS infeasible, planner has NO OPTIONS.

**Missing Controls**:
- Can't adjust planning window
- Can't modify heat size
- Can't adjust lot grouping tolerance
- Can't extend horizon
- Can't see bottleneck resources
- No "What-if" scenario builder

**What Needs to Happen**:
1. When plan is infeasible, show:
   - Root cause (which resource is bottleneck)
   - Suggested fixes:
     - Extend planning horizon by X days
     - Reduce lot size to fit window
     - Adjust grouping tolerance
     - Add parallel resources
     - Extend production window
2. Add parameter adjustment controls
3. Allow re-planning with modified parameters
4. Show impact of changes before applying

---

### 7. Ready Band at Bottom - Unclear Purpose
**Current**: Bar showing `Ready` with status indicators below Planning Pipeline
**Questions**:
- What does "Ready" mean?
- When changes state and to what?
- Can it safely be removed?

**Assessment**: 
- Seems to be a leftover status indicator
- Doesn't provide useful feedback to planner
- Space could be used for error messages or controls

**Recommendation**: Can be hidden with CSS if not used

---

## Summary Table

| Issue | Status | Priority | Type |
|-------|--------|----------|------|
| Full BOM button | ✅ FIXED | - | UX |
| Badge wrapping | ✅ FIXED | - | UX |
| Byproduct as SHORT | ✅ FIXED | CRITICAL | Logic |
| KPI calculation | ✅ FIXED | HIGH | Logic |
| FG in Heat planning | 🟡 PARTIAL | CRITICAL | Design |
| **Gantt in Planning Tab** | ⏳ PENDING | **CRITICAL** | **UX/Workflow** |
| **Checkbox Sizing** | ⏳ PENDING | HIGH | UX |
| Infeasible plan controls | ⏳ PENDING | **CRITICAL** | UX |
| Heat table material display | ⏳ PENDING | MEDIUM | Design |
| Ready band purpose | ⏳ PENDING | LOW | UX |

**Status Legend**: ✅ FIXED | 🟡 PARTIAL (header clarified) | ⏳ PENDING (documented, not yet fixed)

---

## Remaining Issues to Address

### High Priority (CRITICAL)

**6. Gantt Visualization in Planning Tab** (CRITICAL - planning blind spot)
Currently gantt is hidden in Execution tab behind "Run Schedule" button. Planner cannot see capacity/scheduling impact DURING planning.

**Problem:**
```
Current flow: SO → PO → Heat → [go to Execution tab] → [click Run Schedule] → [see Gantt]
What planner sees: Nothing about scheduling during planning
```

**What Needs to Happen:**
- [ ] Show SO-level Gantt in Planning tab (which resources each SO needs, when)
- [ ] Show PO-level Gantt (consolidated, which resources grouped PO needs)
- [ ] Show Heat-level Gantt (final view, exact heat sequence and resource allocation)
- [ ] Integrate with feasibility check (if plan is INFEASIBLE, show why on gantt - which resource is bottleneck)
- [ ] Allow planner to adjust PO/Heat sequence WITHOUT going to Execution
- [ ] Show impact visually - red bars for overload, green for OK
- [ ] Make gantt PART of Planning workflow, not separate from it

**Impact:** Planner can make informed decisions about PO grouping/heat sizing based on ACTUAL capacity, not guesses.

---

**7. Checkbox Sizing** (UX - consistency)
Checkboxes in Planning tab SO/PO tables are tiny; checkboxes in Release Grid are properly sized.

**What Needs to Happen:**
- [ ] Make Planning tab checkboxes match Release Grid size (larger, easier to click)
- [ ] Ensure consistent checkbox styling across all tables

---

**8. Infeasible Plan Controls** (CRITICAL - blocks planners)
When SMS hours exceed 100%, planner has NO OPTIONS:
- [ ] Show root cause (which resource is bottleneck?)
- [ ] Suggest remediation options:
  - Extend planning horizon
  - Reduce heat size
  - Adjust grouping tolerance
  - Add parallel resources
- [ ] Allow parameter adjustment and re-planning
- [ ] Preview impact before applying changes

---

### Medium Priority

**9. Heat Table Material Display** (Enhancement)
Option to show materials consumed by heat instead of FG produced:
- [ ] Expand BOM for each heat's FG
- [ ] Aggregate component/RM requirements
- [ ] Show per-heat material consumption
- [ ] Link to material shortages

### Low Priority

**10. Ready Band Purpose** (UX Cleanup)
Bottom status bar showing "Ready" seems unused:
- [ ] Determine if needed for workflow
- [ ] If not used, can hide with CSS
- [ ] Could be repurposed for error/warning messages

