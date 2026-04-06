# Planning Page Implementation - Rolling Campaign Selection

## Overview

Implemented a complete rolling campaign planning interface that gives users full control to:
- Select sales orders for the next campaign
- Configure production order generation strategy
- Simulate manufacturing timelines and feasibility
- Release campaigns to operations
- Auto-load recommended campaigns based on urgency and delivery dates

## Architecture

### Frontend (ui_design/index.html)

**New Tab:** "Planning" - Added after Campaigns tab

**3-Panel Layout:**

1. **Left Panel: SO Selection** (1 column)
   - Search by SO ID / Grade / Customer
   - Filter by Priority (URGENT, HIGH, NORMAL)
   - Filter by Grade
   - Bulk select/deselect buttons
   - Visual selection feedback with checkbox
   - Display selected SO count
   - Shows: Grade, MT, Due Date, Priority for each SO

2. **Center Panel: Campaign Editor** (1.2 columns)
   - Campaign Summary (SOs, MT, Grade, Est. Heats)
   - PO Generation Settings:
     * 1 PO per SO (individual)
     * Consolidated (same grade)
     * Heat-optimized (minimize heats)
   - Estimated Timeline:
     * SMS Duration
     * RM Duration
     * Total Duration
   - Simulate button
   - Auto-Optimize button (placeholder)

3. **Right Panel: PO List & Simulation** (1.2 columns)
   - Production Orders list with:
     * PO ID
     * SOs included
     * MT and heats
     * Duration
     * Edit Heats button (placeholder)
   - Manufacturing Timeline (Gantt placeholder)
   - Feasibility Check:
     * Status (FEASIBLE / INFEASIBLE)
     * On-time SOs count
     * Late SOs count
   - Release Campaign button (enabled only if feasible)
   - Tweak & Re-simulate button

### Backend (engine/rolling_campaign.py)

**CampaignSelector** class - Campaign selection logic
- `recommend_next_campaign()` - Main entry point
- `_select_urgent_first()` - Strategy: URGENT priority, sort by due date, consolidate by grade
- `_select_demand_window()` - Strategy: Next 48 hours, largest grade demand
- `_select_hybrid_score()` - Strategy: Weighted scoring (urgency 50%, batch size 30%, priority boost)
- Estimation functions: heats, SMS duration, RM duration

**POGenerator** class - Production order generation
- `generate_pos()` - Generate POs based on strategy
- `_generate_1to1()` - One PO per SO
- `_generate_consolidated()` - One PO per grade
- `_generate_heat_optimized()` - Minimize heats, distribute SOs across heats

**RollingCampaign** and **ProductionOrder** dataclasses - Data structures

### API Endpoints (xaps_application_api.py)

1. **GET /api/aps/planning/recommend-campaign**
   - Returns recommended campaign based on current open SOs
   - Uses URGENT_FIRST strategy by default
   - Response: campaign_id, recommended_sos, grade, total_mt, heats, duration

2. **POST /api/aps/planning/simulate-campaign**
   - Input: sales_orders list, po_strategy
   - Simulates campaign execution
   - Generates POs using specified strategy
   - Performs feasibility check (all SOs due after campaign completion time)
   - Response: campaign, production_orders, feasible flag, on-time/late counts, gantt_data

3. **POST /api/aps/planning/release-campaign**
   - Input: sales_orders list, po_strategy
   - Creates campaign and generates POs
   - Updates SO Campaign_ID in workbook
   - Response: campaign_id, status, production_order count

## User Workflow

1. **Load Recommended Campaign**
   - Click "Load Recommended" button
   - System automatically selects URGENT SOs from same grade (nearest due date first)
   - Campaign summary updates in real-time

2. **Manually Adjust Selection**
   - Use search and filter controls on left panel
   - Click SOs to toggle selection
   - Use Select All / Clear buttons
   - Campaign summary updates as selection changes

3. **Configure PO Generation**
   - Select strategy (1to1, consolidated, or heat-optimized)
   - Heat-optimized minimizes number of heats while consolidating same-grade SOs

4. **Simulate**
   - Click "Simulate" button
   - System:
     * Calculates SMS and RM durations
     * Generates POs based on selected strategy
     * Checks feasibility (all SOs due after campaign completion)
     * Displays timeline and feasibility results
   - Simulation results shown in right panel

5. **Release Campaign**
   - Click "Release Campaign" button (enabled only if feasible)
   - System:
     * Assigns unique campaign ID
     * Updates SO Campaign_ID in workbook
     * Returns campaign details
   - Campaign moves to operations
   - UI clears for next campaign selection

## Key Features

### Real-time Calculation
- Campaign summary updates instantly as SOs are selected
- MT total, heat count, and duration estimates recalculated on each change

### Selection Strategies
- **URGENT-FIRST**: Nearest due date, single grade, greedily add same-grade SOs
- **DEMAND-WINDOW**: Next 48 hours, largest urgent grade demand
- **HYBRID-SCORE**: Weighted scoring balancing urgency (50%), batch size (30%), priority boost

### PO Generation Strategies
- **1to1**: Individual POs per SO - maximum visibility, more scheduling decisions
- **Consolidated**: One PO per grade - simpler scheduling, batch efficiency
- **Heat-Optimized**: Distribute same-grade SOs across heats - minimize heats while consolidating

### Feasibility Checking
- All SOs in campaign must have due date > (now + campaign_duration)
- Shows on-time count and late count
- Release button only enabled for feasible campaigns

### Column Name Mapping
- SO column "Order_Qty_MT" (not "Qty_MT")
- SO column "Delivery_Date" (not "Due_Date")
- SO column "Campaign_ID" (not "Campaign")

## Estimation Logic (Rough approximations for MVP)

- **Heats**: ceil(total_MT / 50) - assumes 50 MT per heat on average
- **SMS Duration**: heats * 2 + 1 hour (2h per heat + 1h setup)
- **RM Duration**: ceil(total_MT / 30) + 1 hour (30 MT/hour rolling speed + 1h setup)

These can be refined with actual process parameters from Campaign_Config sheet.

## Testing

The planning page is ready for testing at:
- **Tab**: Planning (in top navigation)
- **URL**: http://localhost:3131

### Quick Test
1. Click "Load Recommended" - Should show 2 URGENT SAE 1080 SOs (SO-031, SO-032)
2. Click "Simulate" - Should show feasibility status
3. Adjust selection manually - Campaign summary updates
4. Try different PO strategies - Results recalculate
5. When feasible, click "Release Campaign"

## Future Enhancements

1. **Advanced Sorting** - Sort SOs by due date, MT, customer, etc.
2. **Campaign History** - Show released campaigns and their status
3. **What-if Analysis** - Save/compare multiple campaign scenarios
4. **Gantt Chart** - Detailed manufacturing timeline visualization
5. **Edit PO Heats** - Allow manual adjustment of heat distribution
6. **Auto-Optimize** - Genetic algorithm or constraint solver optimization
7. **Predictive Feasibility** - Show likelihood of on-time delivery with confidence
8. **Material Checking** - Verify BOM coverage and material hold flags
9. **Resource Availability** - Check resource utilization and conflicts
10. **Integration with Scheduler** - Direct integration with CP-SAT solver

## Files Modified/Created

### Created
- `engine/rolling_campaign.py` - Core rolling campaign logic (530 lines)
- `PLANNING_PAGE_IMPLEMENTATION.md` - This file

### Modified
- `ui_design/index.html` - Added Planning tab and 3-panel interface (500+ lines of HTML/JS)
- `xaps_application_api.py` - Added 3 new API endpoints

### Column Names Used
- SO_ID, Customer, Grade, Order_Qty_MT, Delivery_Date, Priority, Campaign_ID, Status

## Notes

- Planning page state is NOT persisted - refreshing the page clears selections
- Recommendations use current open SOs from workbook
- Released campaigns update Campaign_ID column in Sales_Orders sheet
- Feasibility check is simple (due_date > now + duration) - can be enhanced with scheduler simulation
