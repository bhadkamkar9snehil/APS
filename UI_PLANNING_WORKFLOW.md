# Planning UI - Complete Workflow

## Vision

**User Control First:** The planner (user) makes all business decisions. System recommends, simulates, and executes.

```
Open SOs → Selection Algorithm → Recommended SOs → User Reviews/Tweaks
                                                        ↓
                                                    SO → PO Conversion
                                                        ↓
                                                    POs List (Editable)
                                                        ↓
                                                    User Tweaks PO Details
                                                        ↓
                                                    Simulate Manufacturing
                                                        ↓
                                                    User Reviews Results
                                                        ↓
                                                    APPROVED? → Release to Operations
                                                        ↓ (No, iterate)
                                                    TWEAK → Go back to POs
```

---

## UI Layout

### Page: "Plan Next Campaign"

**3-Column Layout:**

```
LEFT PANEL              CENTER PANEL            RIGHT PANEL
(Sales Orders)          (Campaign Editor)       (POs & Simulation)
─────────────           ─────────────           ──────────────

[SO Selection]          [Campaign Summary]      [Generated POs]
- Filter/Sort           - Grade: SAE 1080       - PO-001 (Details)
- Checkbox Select       - Count: 4 SOs          - PO-002 (Details)
- Urgency/Due Date      - MT: 260               - PO-003 (Details)
- Add/Remove buttons    - Heats: 7              
                        - Est Duration: 22h     [Manufacturing Plan]
[Selected SOs List]                             - SMS Timeline
- SO-031 ✓              [Conversion Settings]   - RM Timeline
- SO-032 ✓              - PO size: Auto/Custom  - Resource Schedule
- SO-011 ✓              - Heats per PO: ?       - Changeover times
- SO-039 ✓              - Section grouping: ?   
                        - Apply Settings        [Simulation Results]
[Campaign Stats]                                - All on-time? YES/NO
- Total MT: 260         [Recommended SOs]       - Critical path
- Est. heats: 7         (Auto-selected) vs      - Resource util %
- Due: 2026-04-07       [Manual Changes]        - Bottlenecks
                        (User tweaks)
```

---

## Panel 1: SO Selection (Left)

### Component: SO Filter & Selection

```html
[TAB: RECOMMENDED] [TAB: ALL] [TAB: URGENT ONLY] [TAB: BY GRADE]

[FILTER BAR]
Priority: [All ▼] | Grade: [All ▼] | Due: [All ▼]
Hours to Due: [0 - 200 ▼]

[URGENCY INDICATOR]
┌─────────────────────────────────────────┐
│ URGENT (Due within 24h)          [3 SOs] │
│ HIGH (Due 24-72h)               [12 SOs] │
│ NORMAL (Due >72h)               [66 SOs] │
└─────────────────────────────────────────┘

[SO LIST - Interactive Table]
┌──────────────────────────────────────────────────────────────────┐
│ ☐ │ SO_ID   │ Grade      │ MT    │ Due      │ Hours │ Priority  │
├──────────────────────────────────────────────────────────────────┤
│ ☑ │ SO-031  │ SAE 1080   │ 140   │ 04-06    │ 0     │ URGENT    │ ← Selected
│ ☑ │ SO-032  │ SAE 1080   │ 120   │ 04-07    │ 24    │ URGENT    │ ← Selected
│ ☑ │ SO-011  │ SAE 1065   │  60   │ 04-07    │ 24    │ URGENT    │ ← Selected (diff grade)
│ ☑ │ SO-039  │ SAE 1008   │ 160   │ 04-07    │ 24    │ URGENT    │ ← Selected
│ ☐ │ SO-040  │ SAE 1008   │ 140   │ 04-08    │ 36    │ URGENT    │
│ ☐ │ SO-050  │ SAE 1018   │ 140   │ 04-08    │ 36    │ URGENT    │
│ ☐ │ SO-007  │ SAE 1018   │  60   │ 04-08    │ 36    │ URGENT    │
│ ☐ │ SO-043  │ SAE 1065   │ 120   │ 04-09    │ 60    │ HIGH      │
└──────────────────────────────────────────────────────────────────┘

[ACTIONS]
[+ Add to Campaign] [- Remove] [Clear All] [Load Recommended]
```

### Component: SO Details (Hover/Click)

```
SO-031 Details
────────────────
SKU:      FG-WR-SAE1080-55
Qty:      140 MT
Grade:    SAE 1080
Section:  5.5mm
Customer: XYZ Corp
Due:      2026-04-06 00:00 (NOW - OVERDUE!)
Priority: URGENT
Status:   Open

BOM Requirements:
  └─ BIL-150-1080 (billet input needed)
     Current Inventory: 100 MT (Partial coverage)
     Required: 146.7 MT (with 5% yield loss)
     Status: SHORTAGE 46.7 MT (need SMS heat)
     
Estimated Heats: 2 (140 MT / 40 MT per heat for BIL-150)

Notes: High-priority, already overdue, needs immediate manufacturing
```

---

## Panel 2: Campaign Editor (Center)

### Component: Campaign Summary & Control

```
┌──────────────────────────────────────────────────────────┐
│  CAMPAIGN-001 PLANNING                                   │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  Grade:              [SAE 1080 ▼]  ← User can change   │
│  Campaign Strategy:  [URGENT-FIRST ▼] [DEMAND-WINDOW]  │
│                      [HYBRID-SCORE]                     │
│                                                          │
│  Selected SOs:       [4] ← Count                         │
│  Total MT:           260 MT                              │
│  Est. Heats:         7                                   │
│                                                          │
│  SMS Duration:       10h (+ 2h setup) = 12h             │
│  RM Duration:        104 MT × 0.4h/MT = 41.6h           │
│  Total:              ~54 hours                           │
│                                                          │
│  Est. Release Time:  [2026-04-06 16:00 ▼]              │
│  Est. SMS Complete:  2026-04-07 04:00                   │
│  Est. RM Complete:   2026-04-08 12:00                   │
│                                                          │
│  Material Status:    [Check] ✓ All covered              │
│  Inventory Locks:    [Lock Selected Materials]          │
│                                                          │
└──────────────────────────────────────────────────────────┘

[KEY METRICS]
┌─────────────────────────────────────────┐
│ SOs in Campaign:      4                 │
│ Different Grades:     3 (Mix!)          │
│ Changeover Cost:      30 minutes        │
│ On-Time Feasibility:  95% (all due 04-08 or later) │
│ Critical Path:        RM (41.6h)        │
└─────────────────────────────────────────┘
```

### Component: SO Conversion Settings

```
┌──────────────────────────────────────────────────────────┐
│  PO GENERATION SETTINGS                                  │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  Production Order Logic:                                │
│                                                          │
│  ☑ One PO per Sales Order                              │
│    └─ Each SO becomes separate production run          │
│                                                          │
│  ☐ Consolidate Same Grade                              │
│    └─ Group all SAE 1080 SOs into 1 PO                 │
│                                                          │
│  ☐ Smart Consolidation (Configurable)                  │
│    └─ Group SOs if:                                    │
│       • Same grade AND                                 │
│       • Due within ±24 hours of each other AND         │
│       • Total MT ≤ [500 ▼] MT per group               │
│                                                          │
│  Section Grouping:                                     │
│  ☑ Separate PO per section (e.g., 5.5mm vs 6.5mm)     │
│    └─ Cost: More POs, more changeovers, better accuracy │
│                                                          │
│  ☐ Group sections by material length                   │
│    └─ Cost: Fewer POs, more complex rolling            │
│                                                          │
│  Batch Size (Heat Planning):                           │
│  Heat Size: [40 ▼] MT per heat                         │
│  Min Heats: [1]     Max Heats: [10]                    │
│  Target Heats per Campaign: [6-8]                      │
│                                                          │
│  [RESET TO DEFAULTS] [APPLY SETTINGS]                  │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

### Component: Recommended vs Manual

```
┌────────────────────────────────────────────────────┐
│  AUTO-RECOMMENDED vs MANUAL SELECTION              │
├────────────────────────────────────────────────────┤
│                                                    │
│  [RECOMMENDED]              [YOUR SELECTION]       │
│  ────────────────           ──────────────────     │
│  (Algo picked)              (You picked)           │
│                                                    │
│  SO-031 (URGENT, 0h)   ✓   SO-031 (URGENT, 0h)  │
│  SO-032 (URGENT, 24h)  ✓   SO-032 (URGENT, 24h) │
│  SO-039 (URGENT, 24h)  ✓   SO-011 (URGENT, 24h) │
│                             SO-039 (URGENT, 24h) │
│  (Skipped SO-011:                                 │
│   Different grade,     ✗   Status: 4 SOs, 260 MT │
│   can do CMP-2)             Different from algo   │
│                                                    │
│  [ACCEPT RECOMMENDED]  [KEEP MANUAL]  [RESET]    │
│                                                    │
└────────────────────────────────────────────────────┘
```

---

## Panel 3: PO List & Simulation (Right)

### Component: Generated Production Orders

```
┌─────────────────────────────────────────────────────────────┐
│  GENERATED PRODUCTION ORDERS (4 POs from 4 SOs)             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  [PO-001]  ▾ SAE 1080-55 | 140 MT | 2 heats | Due 04-06   │
│  ├─ SO-031 (140 MT FG-WR-SAE1080-55)                       │
│  ├─ Billet Needed: BIL-150-1080 (146.7 MT)                 │
│  ├─ Heats: 2 (70 MT each)                                  │
│  ├─ SMS Start: 16:00 (1st heat)                            │
│  ├─ SMS Duration: 4.5h total (2 heats × 1.5h + 1.5h setup)│
│  ├─ RM Start: 20:30 (1st heat ready)                       │
│  ├─ RM Duration: 56h (140 MT × 0.4h/MT)                    │
│  ├─ Est. Complete: 2026-04-08 08:30                        │
│  ├─ Status: ON-TIME (due 04-06, but 2 days late ok)       │
│  └─ [EDIT] [SPLIT] [MERGE WITH NEXT]                       │
│                                                             │
│  [PO-002]  ▾ SAE 1080-65 | 120 MT | 2 heats | Due 04-07   │
│  ├─ SO-032 (120 MT FG-WR-SAE1080-65)                       │
│  ├─ Billet: BIL-150-1080 (126 MT) - Using PO-001's stock  │
│  ├─ Heats: 2                                               │
│  ├─ SMS Start: 20:30 (waits for PO-001 SMS done)           │
│  ├─ RM Start: 01:00 next day                               │
│  ├─ Est. Complete: 2026-04-08 16:00                        │
│  ├─ Status: ON-TIME ✓                                      │
│  └─ [EDIT] [SPLIT] [MERGE WITH NEXT]                       │
│                                                             │
│  [PO-003]  ▾ SAE 1065-55 | 60 MT | 1 heat | Due 04-07     │
│  [PO-004]  ▾ SAE 1008-55 | 160 MT | 4 heats | Due 04-07   │
│                                                             │
│  [+] Add Manual PO  [↑↓] Reorder  [RESET POs]              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Component: PO Detail Edit (Click PO-001)

```
┌─────────────────────────────────────────────────────────────┐
│  EDIT PRODUCTION ORDER: PO-001                              │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Source SO: [SO-031 ▼]  (Can change)                       │
│  Qty MT: [140] ◄► 100-200 MT valid range                  │
│  Grade: SAE 1080 (locked)                                  │
│  Section: [5.5mm ▼]                                         │
│                                                             │
│  Heats Configuration:                                      │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Heats per PO: [2]    (1-4 heats valid)              │   │
│  │ ├─ Heat-1: 70 MT (@ 40 MT/heat)  [✓]              │   │
│  │ ├─ Heat-2: 70 MT (@ 40 MT/heat)  [✓]              │   │
│  │ └─ Total: 140 MT (matches SO qty) [✓]              │   │
│  │                                                     │   │
│  │ Or split into:                                      │   │
│  │ ☑ 3 heats: 50+50+40 MT each  [Preview duration]    │   │
│  │ ☑ 4 heats: 35+35+35+35 MT    [Preview duration]    │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  Manufacturing Sequence:                                   │
│  SMS Order: [1st ▼]  (relative to other POs)              │
│  RM Order: [1st ▼]                                         │
│  ├─ (SMS and RM can run in different orders)              │
│  ├─ (But each PO must finish SMS before starting RM)      │
│                                                             │
│  Resource Preferences:                                     │
│  SMS Resource: [EAF-02 ▼] [Auto-assign] [Any]             │
│  RM Resource: [RM-02 ▼]   [Auto-assign] [Any]             │
│                                                             │
│  Constraints & Warnings:                                   │
│  ⚠ Different grade from PO-002 (SAE 1065)                │
│    → Changeover needed: 30 minutes between RM-1 and RM-2  │
│                                                             │
│  ✓ Material: BIL-150-1080 available (100 MT on hand)      │
│             + SMS will produce 146.7 MT             │
│             = Sufficient for this PO                       │
│                                                             │
│  [SAVE CHANGES] [CANCEL] [DELETE PO] [DUPLICATE]          │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Component: Manufacturing Timeline (Gantt)

```
CAMPAIGN MANUFACTURING PLAN
═══════════════════════════════════════════════════════════════

Time:      16:00    20:00    00:00    04:00    08:00    12:00
Day:       2026-04-06       2026-04-07       2026-04-08

SMS (EAF/LRF/VD/CCM):
├─ PO-001 ███████░░  (4.5h SMS, then RM)
├─ PO-002       ███████░░░  (waits for EAF, 4.5h SMS)
├─ PO-003              ██░░░  (2h SMS)
└─ PO-004                   ███████░░░  (6h SMS)

RM (Rolling Mill):
├─ PO-001          ████████████████████░░░░  (56h rolling)
├─ PO-002                                    ███████████  (48h)
├─ PO-003 ██░░░                (6h rolling, done early)
└─ PO-004                              █████████████  (64h)

Critical Path: PO-004 RM (ends 2026-04-09 at 12:00)
Makespan: 44 hours total (2026-04-06 16:00 to 2026-04-08 12:00)

Resource Utilization:
─────────────────────
EAF-02:   █████░░░░░░  16.5 / 168 hours (9.8%)
RM-02:    ███████░░░░  48 / 168 hours (28.6%)
```

### Component: Simulation Results

```
┌─────────────────────────────────────────────────────────────┐
│  SIMULATION RESULTS                                         │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  [FEASIBILITY CHECK]                                        │
│  ✓ All POs fit within manufacturing capacity               │
│  ✓ All due dates can be met (on-time or acceptable late)  │
│  ✓ No material shortages (inventory sufficient)            │
│  ✓ No resource conflicts (no two POs on same RM)          │
│                                                             │
│  [DELIVERY STATUS]                                         │
│  PO-001 (SO-031): Due 2026-04-06, Mfg done 2026-04-08 12:30│
│                   Status: 2 DAYS LATE (was due today!)    │
│                   │[████████████████────────] LATE        │
│                                                             │
│  PO-002 (SO-032): Due 2026-04-07, Mfg done 2026-04-08 16:00│
│                   Status: 1 DAY LATE                        │
│                   │[████████████────────────] LATE        │
│                                                             │
│  PO-003 (SO-011): Due 2026-04-07, Mfg done 2026-04-08 02:00│
│                   Status: EARLY (19 hours ahead!)          │
│                   │[████░░░░░░░░░░░░░░░░░░░] ON-TIME    │
│                                                             │
│  PO-004 (SO-039): Due 2026-04-07, Mfg done 2026-04-09 12:00│
│                   Status: 5 DAYS LATE ❌                    │
│                   │[█████████████████────────] CRITICAL   │
│                                                             │
│  [SUMMARY]                                                 │
│  Avg Lateness: 2.0 days                                    │
│  On-Time Orders: 1/4 (25%)                                 │
│  Late Orders: 3/4 (75%)                                    │
│  Critical (>3 days late): 1/4 (25%)                        │
│                                                             │
│  [SUGGESTIONS]                                              │
│  • PO-004 (SO-039 SAE 1008) is too large for this campaign │
│  • Move PO-004 to CAMPAIGN-002 (would be on-time)         │
│  • Current plan: 3 POs (PO-001/002/003) would be better   │
│                                                             │
│  [AUTO-OPTIMIZE] [ACCEPT] [MODIFY MANUALLY]               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## Workflow Actions

### Action 1: Load Recommended Campaign

**User clicks:** "Load Recommended"

**System does:**
1. Run selection algorithm (URGENT-FIRST by default)
2. Populate SO list with recommended SOs
3. Generate initial POs
4. Show simulation results
5. Highlight any issues

### Action 2: Manually Tweak Campaign

**User can:**
- Add/remove SOs from campaign
- Change PO split logic (1 PO per SO vs consolidate)
- Adjust heat configuration per PO
- Change manufacturing sequence
- Assign specific resources
- Change PO order

**System responds:**
- Updates campaign summary instantly
- Recalculates manufacturing timeline
- Updates simulation results (delivery dates, resource util)
- Highlights new issues/warnings

### Action 3: Re-Simulate After Changes

**User clicks:** "Apply Settings" or "Recalculate"

**System:**
1. Regenerates POs with new settings
2. Re-runs simulation
3. Updates timeline (Gantt chart)
4. Updates feasibility check
5. Suggests optimizations if issues found

### Action 4: Auto-Optimize

**User clicks:** "Auto-Optimize"

**System:**
1. Takes current configuration
2. Tries different SO combinations
3. Tries different PO groupings
4. Tries different sequencing
5. Returns best option (on-time, resource util, etc)
6. Presents to user for approval

### Action 5: Release to Operations

**User clicks:** "Release This Campaign"

**System:**
1. Locks this campaign (can't modify)
2. Creates work orders for each PO
3. Sends to manufacturing floor
4. Starts "Plan Next Campaign" automatically
5. Shows released campaign in read-only view

---

## Information Flow

### Before Release (Planning Phase)

```
┌──────────────────────────────────────┐
│ Open Sales Orders (81 total)         │
│ - URGENT: 40 SOs                     │
│ - HIGH: 20 SOs                       │
│ - NORMAL: 21 SOs                     │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ Campaign Selection Algorithm         │
│ (URGENT-FIRST / DEMAND-WINDOW / etc) │
│ Input: open_sos, strategy, params    │
│ Output: recommended SOs              │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ Planning UI                          │
│ - Show recommended campaign          │
│ - User reviews/tweaks                │
│ - User confirms or modifies          │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ PO Generation                        │
│ Input: selected SOs, conversion logic│
│ Output: list of production orders    │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ Manufacturing Simulation             │
│ - Simulate SMS → RM flow             │
│ - Check due dates                    │
│ - Check resources                    │
│ - Generate timeline (Gantt)          │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ User Decision                        │
│ [Approve] → Release to Mfg           │
│ [Modify] → Back to Planning UI       │
│ [Optimize] → Auto-search for better  │
└──────────────────────────────────────┘
           ↓
┌──────────────────────────────────────┐
│ Release to Operations                │
│ - Lock campaign                      │
│ - Send work orders to floor          │
│ - Manufacturing begins               │
│ - Start next campaign planning       │
└──────────────────────────────────────┘
```

### After Release (Execution Phase)

```
CAMPAIGN-001 (In Manufacturing)
├─ PO-001: SMS 16:00-20:30, RM 20:30-02:30
├─ PO-002: SMS 20:30-01:00, RM 01:00-09:00
├─ PO-003: SMS 01:00-03:00, RM 03:00-09:00
└─ PO-004: SMS 03:00-09:00, RM 09:00-17:00

CAMPAIGN-002 (Being Planned)
├─ [Review Recommended SOs]
├─ [Plan POs]
├─ [Simulate]
├─ [User Approves]
└─ [Ready to Release at hour 24]

CAMPAIGN-003+ (Candidates, not yet planned)
└─ [Will be recommended when CMP-2 nears completion]
```

---

## Configuration Options

### Campaign Selection Strategy

**In settings/admin panel:**

```
Default Campaign Selection Strategy:
☑ URGENT-FIRST
  └─ Sort by delivery_date, consolidate by grade
  
☐ DEMAND-WINDOW  
  └─ Next 48h demand, largest grade first
  
☐ HYBRID-SCORE
  └─ Weighted: 0.5*urgency + 0.3*size + 0.2*consolidation

Campaign Size Constraints:
  Min MT: [100 ▼]
  Max MT: [500 ▼]
  Max Heats: [12 ▼]
  Max Duration: [120 ▼] hours

PO Generation Logic:
☑ One PO per Sales Order (most granular)
☐ Consolidate same grade (fewer POs)
☐ Smart consolidation (threshold-based)

Section Grouping:
☑ Separate PO per section (5.5mm, 6.5mm, etc.)
☐ Group sections by material type
```

---

## Use Cases

### Use Case 1: "Expedite Urgent Order"

**Scenario:** New URGENT SO arrives at 10:00 AM, due 04-07 noon

**User flow:**
1. Opens "Plan Next Campaign" page
2. Sees current CAMPAIGN-002 plan (3 POs, due 04-08)
3. Checks "New URGENT" filter
4. Sees new SO-999 (SAE 1065, 80 MT, due 04-07 noon)
5. Clicks "Add to Current Plan"
6. System re-simulates:
   - Total MT: 350 → 430 MT
   - Heats: 8 → 11
   - New critical PO due 04-07 noon
7. Simulation shows: PO-999 would complete 04-08 14:00 (LATE by 38 hours)
8. User tweaks: "Remove PO-004 from CAMPAIGN-002"
9. System suggests: "Move PO-004 to CAMPAIGN-003"
10. New plan: CAMPAIGN-002 has 4 POs, all on-time or minor late
11. User clicks "Release Modified Campaign-002"

### Use Case 2: "Machine Breakdown Recovery"

**Scenario:** RM-01 breaks down during CAMPAIGN-001

**Current situation:**
- CAMPAIGN-001: In RM, 50% done, expected to finish today
- CAMPAIGN-002: Planned for tomorrow

**User flow:**
1. Opens "Plan Next Campaign"
2. Notes: "RM-01 down until 2026-04-09"
3. Modifies CAMPAIGN-002 PO resource assignments:
   - All PO RM operations: [RM-02 ▼] (instead of RM-01/2 auto)
4. System recalculates: RM-02 will be overloaded
5. Simulation shows: CMP-002 completion delayed 12 hours
6. User clicks "Auto-Optimize"
7. System suggests: Split CMP-002 into CMP-2A and CMP-2B
   - CMP-2A: smaller campaign, goes to RM-02, on-time
   - CMP-2B: remainder, can go to RM-01 after repair
8. User approves, releases CMP-2A

### Use Case 3: "Grade Consolidation for Efficiency"

**Scenario:** Planner wants to minimize changeovers

**User flow:**
1. In settings: Change strategy to "DEMAND-WINDOW"
2. Set consolidation weight: 0.5 (was 0.2)
3. Opens "Plan Next Campaign"
4. System recommends: All SAE 1008 SOs (160+140+160 = 460 MT)
5. Result: Single grade, no changeovers
6. Simulation shows: RM efficiency at 85% (vs 40% with mixed grades)
7. But: Some URGENT but non-1008 orders deferred to CMP-003
8. User reviews: Is efficiency gain worth deferring 2 due-dates?
9. Decision: Accept (customer agreed to 24h extension)
10. Clicks "Release"

---

## UI Implementation Priorities

### Phase 1: Basic Planning (Week 1)
- SO selection panel with filters
- Campaign summary display
- Basic PO generation (1:1 SO to PO)
- Simple timeline view
- Release button

### Phase 2: Advanced Planning (Week 2)
- Editable PO details (heat split, sequencing)
- Gantt chart manufacturing timeline
- Simulation results (due dates, resources)
- Auto-optimize suggestions
- Configuration options

### Phase 3: Intelligence & Control (Week 3)
- Multiple selection strategies
- Scenario comparison ("What if?" analysis)
- Resource conflict visualization
- Material shortage highlighting
- Changeover optimization

---

## API Endpoints Needed

```
POST   /api/campaigns/select-next
  Input: strategy, constraints, current_campaign_id
  Output: list of recommended SOs

POST   /api/campaigns/generate-pos
  Input: selected_so_ids, conversion_logic
  Output: list of production orders with details

POST   /api/campaigns/simulate
  Input: list of POs, resource constraints
  Output: manufacturing timeline, feasibility, warnings

POST   /api/campaigns/optimize
  Input: current campaign config, optimization criteria
  Output: alternative configurations with scores

POST   /api/campaigns/release
  Input: campaign_id, PO list
  Output: work orders, manufacturing schedule

GET    /api/campaigns/current
  Output: currently executing campaign details

GET    /api/campaigns/next
  Output: next planned campaign (read-only view)
```

