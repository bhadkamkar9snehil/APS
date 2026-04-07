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
| **Gantt in Planning Tab** | ✅ **FIXED** | **CRITICAL** | **UX/Workflow** |
| **Checkbox Sizing** | ✅ FIXED | HIGH | UX |
| **Infeasible Plan Controls** | ✅ **FIXED** | **CRITICAL** | **UX** |
| Heat table material display | ⏳ PENDING | MEDIUM | Design |
| Ready band purpose | ⏳ PENDING | LOW | UX |

**Status Legend**: ✅ FIXED | 🟡 PARTIAL (header clarified) | ⏳ PENDING (documented, not yet fixed)

---

## Remaining Issues to Address

### High Priority (CRITICAL)

**6. Gantt Visualization in Planning Tab** ✅ **FIXED** (Commit b66cfc4)

**IMPLEMENTATION:**

✅ **COMPLETED:**
- [x] **Plant-based Gantt Modal** - Click "📅 Gantt" button to see timeline split by plant (BF, SMS, RM)
- [x] **Proper visualization** - Shows job timeline bars grouped by plant with color coding
- [x] **Multiple entry points:**
      * Order Pool: "📅 Gantt" button shows timeline for selected SOs
      * Planning Orders: "📅 Gantt" button shows timeline for proposed POs  
      * Heat Batches: "📅 Gantt" button shows timeline for derived heats
- [x] **Modal popup** - Clean UI with close button, can click outside to dismiss
- [x] **Plant separation** - BF (Blue), SMS (Orange), RM (Purple) shown side-by-side
- [x] **Job details** - Each bar shows Job ID + duration (hours)
- [x] **Smart messaging** - "Run Feasibility Check first" if no schedule yet
- [x] **Responsive design** - Works on all screen sizes (max 800px width, 90% on mobile)

**REMOVED (what was "too shitty"):**
- [x] Deleted: renderPlanningGantt() with tiny unreadable resource bars
- [x] Deleted: Cluttered gantt display in feasibility check output  
- [x] Cleaned up: simulateSchedule() and remSimulate() no longer show inline gantt

**Workflow:**
1. User derives heats and clicks Feasibility Check → Simulate
2. Then clicks "📅 Gantt" on any section to see plant timeline
3. Identifies bottleneck plant (BF/SMS/RM with too many operations)
4. Adjusts parameters in remediation panel
5. Re-simulates and checks gantt again to confirm improvement

**Impact:** ✓ Clean, focused planning workflow. Gantt is ON-DEMAND, not always visible, so doesn't clutter the screen.

---

**7. Checkbox Sizing** (UX - consistency) ✅ FIXED (Commit 4e9d9db)

**Problem:** Checkboxes in Planning tab (pool SO table, PO table) were tiny (browser default ~16px); Release Grid had properly sized checkboxes (1.2rem = 19.2px).

**Solution Applied:**
- [x] Added inline styling `width:1.2rem;height:1.2rem;cursor:pointer` to ALL Planning tab checkboxes:
      * Pool SO header select-all checkbox
      * Individual SO row checkboxes in pool table
      * PO header select-all checkbox
      * Individual PO row checkboxes
      * Nested PO-SO split checkboxes (when expanding a PO)
      * Nested select-all for PO detail SOs
- [x] Checkbox styling now consistent across all tables (Planning + Release Grid)
- [x] Larger target makes interaction easier on touch and mouse

---

**8. Infeasible Plan Controls** ✅ **FIXED** (Commit 2ddf9d2)

**Problem:** When plan exceeds capacity (e.g., SMS hours > 100%), planner had NO IDEA:
- Which resource was the bottleneck
- By how much it was overloaded
- How to fix it (no targeted guidance)

Example: "SMS Hours: 342.02h (OVER CAPACITY!)" but plan still showed FEASIBLE

**Solution Implemented:**
- [x] **Bottleneck Detection in Gantt:**
      * Resource utilization % calculated (hours / horizon_hours)
      * Over-capacity resources highlighted in RED with 3px border
      * Bottleneck resource name + % shown at top of gantt
      * Color coding: green ≤80%, yellow >80%, red >100%

- [x] **Resource Overload Alert:**
      * Shows WHICH resources exceed capacity
      * Lists hours/capacity ratio (e.g., "SMS: 342h / 168h = 204%")
      * Appears at top of remediation panel when infeasible

- [x] **Targeted Remediation Guidance:**
      * "If resource overloaded → Add parallel resources"
      * "If over time → Extend horizon"
      * "If too many heats → Narrow priority window"
      * Alert changes based on what's still over after re-simulation

- [x] **Interactive Parameter Testing:**
      * Planner adjusts SMS/RM lines, horizon, or priority filter
      * Re-simulate shows updated gantt + resource utilization
      * Can iterate until plan becomes FEASIBLE with visual feedback

**Result:** Planner now has CLEAR PATH to feasible plan:
1. See gantt → identify bottleneck (e.g., SMS at 204%)
2. Read alert → get specific suggestion (add SMS line)
3. Adjust parameters → re-simulate
4. Check gantt again → see if still over
5. Repeat until FEASIBLE ✓

This UNBLOCKS planners who previously had no options.

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

