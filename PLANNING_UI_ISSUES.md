# Planning UI Issues & Fixes

## ✅ Fixed

### 1. Full BOM Button Not Working
**Issue**: Clicking "Full BOM →" in Material panel did nothing  
**Fix**: Changed from `onclick="nav('bom')"` to `data-page-link="bom"` to use existing tab switching logic  
**Status**: FIXED - Button now navigates to BOM tab

### 2. Text Wrapping in Priority Column  
**Issue**: "URGENT", "HIGH", "NORMAL" badges were wrapping to multiple lines  
**Fix**: Added `white-space: nowrap` to badge styling in SO table  
**Status**: FIXED - Badges now stay on single line

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

### 4. FG (Finished Good) in Heat Planning - Design Error
**Problem**:
```
Heat table shows:
SKU: FG-WR-SAE1045-80
QTY: 50
```
This is wrong! Heat planning should show **WHAT WE'RE USING** (raw materials), not **WHAT WE'RE PRODUCING** (FG).

**Expected**: Heat should list:
- RM-OUT-SAE1045-80
- RM-ENDCUT  
- RM-SCALE
- etc.

NOT the FG itself.

**Root Cause**:
- Heat derivation logic is including parent SKU (FG) instead of just materials
- Need to show bill-of-materials for the heat, not the finished good

**What Needs to Happen**:
1. Modify Heat table to fetch and display materials required for that heat
2. Show RM/component SKUs, quantities, and routing
3. Link heat execution to material consumption

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

| Issue | Priority | Type | Effort |
|-------|----------|------|--------|
| Full BOM button | FIXED | UX | ✅ |
| Badge wrapping | FIXED | UX | ✅ |
| Byproduct as SHORT | CRITICAL | Logic | High |
| FG in Heat planning | CRITICAL | Design | High |
| KPI calculation | HIGH | Logic | Medium |
| Infeasible plan controls | HIGH | UX | High |
| Ready band purpose | LOW | UX | Low |

---

## Next Steps

1. **Investigate BOM data structure** - Does it have INPUT/OUTPUT flag?
2. **Check heat derivation** - Why is FG being included in heat SKU?
3. **Review KPI calculation functions** - Add state update hooks
4. **Design infeasibility controls** - Show bottleneck and adjustment options
5. **Document Ready band** - Determine if removable

