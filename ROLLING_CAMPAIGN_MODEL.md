# Rolling Campaign Model - Complete Architecture

## Executive Summary

**Current Model (Broken):**
- Analyze ALL 81 SOs upfront
- Create 28 campaigns
- Release all 28 simultaneously  
- Scheduler must sequence all 28
- Result: CP-SAT INFEASIBLE (175+ hour timeline needed, 24-72 hour deadline windows)

**Proposed Model (Rolling/Sliding Window):**
- Continuously select NEXT batch of SOs
- Form ONE campaign at a time
- Release only current campaign to operations
- While operations executes, prepare next campaign
- Result: CP-SAT FEASIBLE (only 1 campaign to schedule)

**Key Insight:** Only ONE campaign in production at any given time, not 28 in parallel.

---

## Core Concept

### Rolling Release Timeline

```
TIME:  T0              T0+24h            T0+48h            T0+72h
       |               |                 |                 |
       v               v                 v                 v
    Release        Select Next        Release          Select Next
    CMP-1          CMP-2              CMP-2             CMP-3
      |            (prepare)            |               (prepare)
      |<--------Executing CMP-1-------->|
                                        |<--------Executing CMP-2-------->|
                                                                          |<--------Executing CMP-3----...
                                                    
CMP-1: SMS 10h + RM 12h = 22h total
CMP-2: SMS  8h + RM 11h = 19h total  
CMP-3: SMS  9h + RM 13h = 22h total

Total elapsed time: ~70 hours (3 days)
vs Campaign model: 1400+ hours (58 days)
```

### Execution Model

```
MANUFACTURING PERSPECTIVE:

Production Day 1 (2026-04-06):
  16:00 - Receive CMP-001 work order
  16:00 - Start SMS (melting, refining, degassing, casting)
  
  (Meanwhile, operations team planning SMS for CMP-002)

Production Day 2 (2026-04-07):
  10:00 - SMS CMP-001 complete, billet output ready
  10:00 - Start RM (rolling)
  
  10:00 - Receive CMP-002 work order
  10:00 - SMS crew starts CMP-002 (while RM crew rolls CMP-001)
  
Production Day 2 (2026-04-07):  
  22:00 - RM CMP-001 complete, packaged output
  22:00 - Receive CMP-003 work order
  22:00 - RM crew starts CMP-002
```

---

## Campaign Selection Strategies

### Strategy 1: URGENT-FIRST with Grade Consolidation

**Algorithm:**
```
1. Filter SOs with Priority=URGENT
2. Sort by Delivery_Date (nearest first)
3. Pick first SO -> sets campaign_grade
4. Greedily add SOs:
   - If same grade AND campaign_mt + qty <= 500:
     ADD to campaign
   - Elif different grade:
     SKIP (avoid changeover)
5. Stop when: campaign_mt >= 400 OR heats >= 12 OR duration >= 120h
```

**Example:**
```
Available urgent SOs:
  SO-031: SAE 1080, 140 MT, due 2026-04-06
  SO-032: SAE 1080, 120 MT, due 2026-04-07
  SO-011: SAE 1065,  60 MT, due 2026-04-07
  SO-039: SAE 1008, 160 MT, due 2026-04-07

Selection:
  Campaign-grade = SAE 1080 (first SO's grade)
  Add SO-031: 140 MT (same grade) ✓
  Add SO-032: 120 MT (same grade) ✓
  Total: 260 MT, 2 SOs
  Skip SO-011: different grade (SAE 1065) ✗
  Skip SO-039: different grade (SAE 1008) ✗
```

**Pros:**
- Addresses nearest-due first (fairness)
- Single grade per campaign (no changeover)
- Simple logic, easy to explain

**Cons:**
- May create small campaigns if few urgent same-grade
- May miss consolidation opportunities

---

### Strategy 2: Demand-Window (Next 48 Hours)

**Algorithm:**
```
1. Filter SOs due within next 48 hours
2. Group by grade
3. For each grade: sum urgent MT demand
4. Pick grade with max urgent MT
5. Select all urgent SOs of that grade (up to 500 MT)
```

**Pros:**
- Visible, predictable demand horizon
- Maximizes batch efficiency (consolidated grade)
- Aligns with operational rhythm (daily planning)

**Cons:**
- May defer high-priority outside 48h window
- Less dynamic to urgent updates

---

### Strategy 3: Hybrid Scoring

**Algorithm:**
```
For each SO in next 48h:
  score = 0.5*urgency + 0.3*batch_size + 0.2*consolidation
  
  where:
    urgency = max(0, 1 - hours_to_due/48)
    batch_size = qty_mt / 500
    consolidation = count_same_grade / total_count

1. Calculate score for all candidates
2. Sort by score (descending)
3. Greedily select:
   - First SO sets grade
   - Add SOs with same grade
   - Skip different grades (unless score > threshold)
```

**Pros:**
- Balances multiple objectives
- Adaptive scoring weights (tunable)
- Sophisticated prioritization

**Cons:**
- More complex, harder to understand
- Score tuning requires domain knowledge

---

## Selection Trigger Points

### Proactive Selection (Recommended)

```
Timing: 24 hours before current campaign RM completion

Reason:
  - Time to analyze options thoroughly
  - Time to prepare work orders
  - Time to source materials if needed
  - Production team has 24h notice of changeover

Example:
  CMP-001 scheduled to finish RM at hour 22
  At hour -2 (22-24), analysis runs for CMP-002
  At hour 22, release CMP-002
```

### Reactive Selection

```
Trigger: New URGENT SO arrives during campaign execution

Reason:
  - Could modify next campaign if urgent enough
  - Prevents queuing surprise urgent orders
  
Example:
  CMP-001 in progress
  New SO marked URGENT, due in 12 hours
  If feasible, include in CMP-002 selection
  If not, could be CMP-003
```

### Scheduled Selection

```
Cadence: Every 24 hours (fixed schedule)

Reason:
  - Predictable rhythm
  - Aligns with shift planning
  - Stable operational tempo
```

---

## System Architecture

### Module: CampaignSelector

```python
class CampaignSelector:
    
    def select_next_campaign(
        self,
        open_sos: List[SalesOrder],
        current_campaign: Campaign = None,
        strategy: str = "URGENT_FIRST",
        max_campaign_mt: float = 500.0,
        max_campaign_heats: int = 12,
    ) -> Campaign:
        """
        Select next campaign from open SOs.
        
        Returns:
            Campaign object with selected SOs, estimated heats, duration, etc.
        """
        
        # Filter candidates (not yet in released campaigns)
        candidates = [so for so in open_sos if not so.covered]
        
        # Choose selection algorithm
        if strategy == "URGENT_FIRST":
            selected_sos = self._select_urgent_first(
                candidates, max_campaign_mt, max_campaign_heats
            )
        elif strategy == "DEMAND_WINDOW":
            selected_sos = self._select_demand_window(candidates, max_campaign_mt)
        elif strategy == "HYBRID_SCORE":
            selected_sos = self._select_hybrid_score(candidates, max_campaign_mt)
        else:
            raise ValueError(f"Unknown strategy: {strategy}")
        
        # Build campaign object
        campaign = Campaign(
            id=self._next_campaign_id(),
            sos=selected_sos,
            grade=selected_sos[0].grade,
            total_mt=sum(so.qty_mt for so in selected_sos),
            estimated_heats=self._estimate_heats(selected_sos),
            estimated_sms_hours=self._estimate_sms_duration(selected_sos),
            estimated_rm_hours=self._estimate_rm_duration(selected_sos),
        )
        
        return campaign
    
    def _select_urgent_first(self, candidates, max_mt, max_heats):
        # Sort by urgency
        urgent = [so for so in candidates if so.priority == 'URGENT']
        urgent.sort(key=lambda x: x.delivery_date)
        
        # Pick first SO's grade
        if not urgent:
            return []
        
        selected = [urgent[0]]
        campaign_grade = urgent[0].grade
        campaign_mt = urgent[0].qty_mt
        
        # Add more SOs of same grade
        for so in urgent[1:]:
            if (so.grade == campaign_grade and 
                campaign_mt + so.qty_mt <= max_mt):
                selected.append(so)
                campaign_mt += so.qty_mt
        
        return selected
```

### Trigger Service

```python
class CampaignTriggerService:
    
    def run(self):
        """Run continuously, check for campaign selection triggers."""
        
        while True:
            # Check for proactive selection trigger
            if self._should_select_proactively():
                campaign = self.selector.select_next_campaign(
                    self.get_open_sos(),
                    strategy=config['strategy']
                )
                self._notify_production(campaign)
                self._log_campaign_selection(campaign)
            
            # Check for reactive trigger (new urgent SO)
            new_urgent = self._check_for_new_urgent_sos()
            if new_urgent:
                # Re-evaluate next campaign
                pass
            
            time.sleep(60)  # Check every minute
    
    def _should_select_proactively(self) -> bool:
        """Check if current campaign will finish in next 24 hours."""
        if not self.current_campaign:
            return True  # No campaign, select immediately
        
        completion_time = self.current_campaign.estimated_completion_time
        now = datetime.now()
        hours_until_done = (completion_time - now).total_seconds() / 3600
        
        return hours_until_done <= 24
```

---

## Scheduler Integration

### Current (Broken)
```
Input to scheduler: 28 campaigns
Constraint: Campaign[i].end <= Campaign[i+1].start  (sequential)
Result: INFEASIBLE (takes 1400 hours to schedule all)
```

### Proposed (Rolling)
```
Input to scheduler: 1 campaign at a time
Constraint: Only SMS/RM no-overlap (same resource can't do 2 things at once)
Result: FEASIBLE (trivially solvable in <100ms)

Scheduler job:
  1. Receive current campaign (say, 260 MT, 7 heats, SAE 1080)
  2. Build constraint model for just this campaign
  3. Solve for optimal SMS → LRF → VD → CCM → RM schedule
  4. Return execution plan
  5. Operations executes
  6. Next campaign selected and released ~24 hours later
```

---

## Feasibility Analysis

### Current Model (Campaign-Batched)
```
Total SOs: 81
Campaigns: 28
Timeline: 1948 hours (81 days)
First SO due: 2026-04-06 (0 hours)
Last urgent due: 2026-04-11 (120 hours)

Feasibility: IMPOSSIBLE
  Most campaigns due before they can even start
```

### Proposed Model (Rolling Release)
```
Campaign-1: 2-3 SOs, 260 MT
  Duration: 10h SMS + 12h RM = 22h
  Release: 2026-04-06 16:00
  Complete: 2026-04-07 22:00
  
Campaign-2: 2-3 SOs, 240 MT
  Duration: 10h SMS + 11h RM = 21h
  Release: 2026-04-07 22:00
  Complete: 2026-04-08 19:00
  
Campaign-3: ... similar pattern ...

By 2026-04-12 (6 days):
  All urgent SOs (40 count) have passed through 2-3 campaigns
  
Feasibility: FEASIBLE
  All SOs complete within their due date windows
```

---

## Data Model

### Campaign (Rolling Model)

```python
@dataclass
class Campaign:
    id: str                              # CMP-001, CMP-002, etc
    sales_orders: List[SalesOrder]       # SOs in this campaign
    grade: str                           # SAE 1080, etc (single grade)
    total_mt: float                      # Total MT to be produced
    heats_needed: int                    # Number of SMS heats
    
    # Estimates (calculated at selection time)
    estimated_sms_duration_hours: float  # Time for all heats in SMS
    estimated_rm_duration_hours: float   # Time for rolling
    estimated_total_hours: float         # SMS + RM
    
    # Execution tracking
    status: str                          # PLANNED, RELEASED, SMS, CCM, RM, COMPLETE
    release_time: datetime               # When released to operations
    sms_start_time: datetime             # When SMS started
    sms_end_time: datetime               # When SMS finished
    rm_start_time: datetime              # When RM started
    rm_end_time: datetime                # When RM finished (expected or actual)
```

---

## Summary of Changes

### What Changes

1. **Campaign Creation:** From upfront batch to rolling selection
2. **Scheduler Input:** From 28 campaigns to 1 campaign at a time
3. **Scheduling Constraint:** From campaign sequencing to resource no-overlap
4. **Release Cadence:** From all-at-once to continuous (every 24 hours)
5. **Visibility:** From static plan to dynamic rolling window

### What Stays the Same

1. Manufacturing operations (SMS, RM, etc.)
2. Resource constraints (capacity, changeover times)
3. Billet family assignments
4. BOM structure
5. Material inventory logic

### Benefits

1. **Mathematical Feasibility:** CP-SAT solves instantly (1 campaign = easy)
2. **Operational Simplicity:** One campaign at a time
3. **Responsiveness:** New urgent SOs incorporated next selection
4. **Better Deadlines:** All urgent orders become feasible
5. **Scalability:** Works with 100 or 10,000 SOs
6. **Visibility:** 24-hour lookahead for next campaign

---

## Next Steps

1. Implement CampaignSelector module
2. Implement CampaignTriggerService
3. Modify scheduler to accept 1 campaign at a time
4. Update UI to show rolling campaigns (current, next, candidates)
5. Run end-to-end test with live SOs
6. Validate feasibility (all urgent orders on-time)

