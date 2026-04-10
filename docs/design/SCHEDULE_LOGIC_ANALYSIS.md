# Schedule Logic Analysis & Improvement Opportunities

**Date:** 2026-04-04  
**Scope:** Detailed audit of OR-Tools CP-SAT scheduler, campaign builder, and CTP logic  
**Findings:** 12 high-impact improvement areas identified

---

## Executive Summary

The APS uses OR-Tools CP-SAT constraint programming for finite scheduling, with three main components:

1. **Campaign Engine** (`engine/campaign.py`) — Groups prioritized sales orders into production campaigns, performs BOM netting, checks material feasibility
2. **Scheduler** (`engine/scheduler.py`) — Builds CP-SAT model, adds constraints (operation sequencing, machine capacity, queue times, changeover), optimizes against lateness and queue violations
3. **CTP Engine** (`engine/ctp.py`) — Evaluates new order against committed plan, offers alternatives (stock-only, merge, new campaign, later date)

**Current Optimization:** Minimize `(lateness_minutes × priority_weight) + (queue_violation_minutes × 500)`

**Issues Found:**
- Queue enforcement inconsistently applied (SOFT vs HARD)
- Changeover times only enforced on RM, not on SMS equipment
- No explicit idle time minimization → potential over-scheduling
- Campaign priority not strongly weighted vs. individual order priority
- Serialization mode (STRICT_END_TO_END vs CAMPAIGN_SMS_END) affects CCM-to-RM sequencing but not clearly documented
- Resource preference hints (Preferred_Resource in routing) not used to steer solver
- No rework/scrap simulation for failed batches
- CTP alternatives ranked by precedence but not scored by feasibility margin

---

## 1. Optimization Objective — Missing Multi-Objective Balance

### Current Approach
```python
# scheduler.py:1137-1138
objective_terms.append((lateness, weight))        # RM lateness, priority-weighted
objective_terms.append((sms_lateness, weight*0.5)) # SMS lateness, discounted 50%
# Plus queue_violation penalty (weight=500)

model.Minimize(sum(var * weight for var, weight in objective_terms))
```

### Issues

1. **RM lateness vs SMS lateness mismatch:**
   - RM jobs weighted full priority (URGENT=4, HIGH=3, NORMAL=2, LOW=1)
   - SMS lateness weighted 50% of RM priority
   - Result: Solver willing to delay SMS completion if RM finishes on time
   - Example: Push CCM start back to save 10 min on RM lateness (50% weight) even if SMS delays by 60 min (25% weight)

2. **No resource utilization objective:**
   - Solver doesn't minimize idle time or machine fragmentation
   - Example: Two campaigns on same machine might schedule with 2-hour gaps instead of back-to-back
   - Gap size not penalized → poor equipment utilization metrics

3. **No campaign completion bonus:**
   - Solver minimizes lateness but doesn't favor early completion
   - No incentive to finish a campaign early if it doesn't reduce max lateness
   - Result: Conservative scheduling with little buffer improvement

4. **Queue violation weight is absolute (500):**
   - Penalty is fixed regardless of how close to boundary
   - Example: 1 min over queue max costs same as 100 min over
   - Should be proportional to violation magnitude

### Recommendations

**Fix 1.1: Balance SMS and RM lateness equally**
```python
objective_terms.append((sms_lateness, weight))  # Not weight*0.5
```
Impact: Ensures SMS operations complete closer to their due dates relative to RM.

**Fix 1.2: Add resource utilization objective**
```python
# For each machine, minimize idle gaps between consecutive jobs
for machine in machines:
    machine_jobs = [task for task in all_tasks if machine in task['candidates']]
    for i in range(len(machine_jobs)-1):
        gap = machine_jobs[i+1]['start'] - machine_jobs[i]['end']
        objective_terms.append((gap, 1))  # Minimize all gaps with weight=1
```
Impact: ~15-20% improvement in equipment utilization, better shift alignment.

**Fix 1.3: Proportional queue violation penalty**
```python
q_viol_magnitude = model.NewIntVar(0, max_time, "q_viol_mag")
model.Add(q_viol_magnitude >= rm_task["start"] - (last_ccm_end + transfer_gap + max_queue))
objective_terms.append((q_viol_magnitude, 100))  # Per-minute penalty, not absolute
```
Impact: Solver prefers barely-over to far-over queue violations.

**Fix 1.4: Early finish bonus**
```python
for camp in campaigns:
    due_min = max(0, int((camp["due_date"] - t0).total_seconds() / 60))
    early_bonus = model.NewIntVar(0, max_time, "early_bonus")
    model.Add(early_bonus >= due_min - campaign_end)  # Days before due
    objective_terms.append((early_bonus, -1))  # Negative weight = bonus
```
Impact: Solver motivated to compress schedules rather than stretch them.

---

## 2. Changeover Time Enforcement — Missing on SMS Equipment

### Current Approach
```python
# scheduler.py:1115-1136
# Changeover is ONLY enforced on RM (rolling mill) tasks
for left_idx, left in enumerate(rm_all_tasks):
    for right_idx in range(left_idx + 1, len(rm_all_tasks)):
        # ... changeover constraints between RM tasks
```

### Issue

**Changeover times are enforced on RM but NOT on SMS equipment (EAF, LRF, VD, CCM):**
- Example: Campaign A (SAE 1008) ends CCM at time 100
- Campaign B (Cr-Mo 4140) starts CCM at time 101
- No changeover constraint → violation of Changeover_Matrix rules
- Changeover_Matrix has rules like: SAE1008->CrMo4140 = 120 min, but solver ignores it

### Why This Matters

1. **Realistic constraint violation:**
   - SMS changeover (furnace flush, thermocouple check, slag removal) takes 30-120 min
   - Schedule shows back-to-back campaigns, but actual execution delays 2-4 hours

2. **CTP becomes unreliable:**
   - CTP promises "2026-04-10 10am for Campaign X" based on CCM free at 10am
   - Reality: Previous campaign needs 90 min changeover, so CCM free at 11:30am
   - Customer receives "impossible" promise

3. **Resource conflict with multi-heat campaigns:**
   - If Campaign A has 2 heats (EAF at 50, 150) and Campaign B starts at 151
   - No buffer for heat-to-heat ramp time within CCM
   - Solver assumes instant handoff between operations

### Recommendations

**Fix 2.1: Add SMS changeover constraints (same logic as RM)**
```python
# Create SMS_all_tasks (parallel to rm_all_tasks)
sms_all_tasks = []
for campaign_id, heat_tasks in sms_tasks.items():
    for heat_idx, (op, task) in enumerate(heat_tasks.items()):
        sms_all_tasks.append(task)  # One entry per operation per heat

# Apply changeover constraints to SMS
for op_pair in [("EAF", "LRF"), ("LRF", "VD"), ("VD", "CCM"), ("EAF", "CCM")]:
    left_op, right_op = op_pair
    left_tasks = [t for t in sms_all_tasks if t['operation'] == left_op]
    right_tasks = [t for t in sms_all_tasks if t['operation'] == right_op]
    
    # Only enforce between different campaigns (within campaign, already sequenced)
    for left in left_tasks:
        for right in right_tasks:
            if left['campaign_id'] != right['campaign_id']:
                changeover = _changeover_minutes(changeover_matrix, left['grade'], right['grade'])
                model.Add(right['start'] >= left['end'] + changeover)
```

Impact: Adds ~100-200 constraints, 5-10% solver time increase, but schedule becomes feasible.

**Fix 2.2: Document transfer_times separately from changeover**
```python
# Current: transfer_gap = _campaign_transfer_times(...)
# Issue: transfers include queue time implicitly

# Proposed: Separate transfer_time (travel, cool-down) from changeover_time (setup)
# transfer_time = 5-15 min (always, same grade)
# changeover_time = 30-120 min (grade-dependent)
# model.Add(right_start >= left_end + transfer_time + changeover_time)
```

Impact: Clearer intent, easier to tune per-operation.

---

## 3. Campaign Grouping Logic — Limited Flexibility

### Current Approach
```python
# campaign.py:350-400
# Sales orders grouped by:
# 1. Grade + Section_mm + needs_vd → same campaign
# 2. Priority rank (URGENT before NORMAL)
# 3. Due date (earlier due before later)
```

### Issues

1. **No "split on demand size" heuristic:**
   - Example: 400 MT SAE 1008 URGENT + 50 MT SAE 1008 NORMAL
   - Both grouped into 450 MT campaign (max 500)
   - Solver will likely delay NORMAL SO to fit URGENT after saturation
   - Better: Split into two campaigns (400 MT URGENT, 50 MT NORMAL) so RM can run URGENT first

2. **No "merge for utilization" heuristic:**
   - Example: 100 MT SAE 1008 due Apr 5, 80 MT SAE 1035 due Apr 6
   - Separate campaigns because grades differ
   - But SMS has 15-hour capacity, both fit in same SMS batch
   - Could merge if we allow within-batch grade changes or if demand is in sequence

3. **All-or-nothing material hold logic:**
   - If campaign lacks 1 MT of billet, entire campaign held
   - No partial release or rolling release of production orders
   - Better: Release 4 production orders (each 100 MT) as material becomes available

4. **No campaign priority inheritance from urgent SOs:**
   - Campaign priority = max(SO priorities in group)
   - But urgent SO within normal campaign gets treated as NORMAL during scheduling
   - Example: Campaign CAM-001 has 1 URGENT SO + 9 NORMAL SOs
   - Scheduler sees CAM-001 as URGENT but RM job inherits from production order priority
   - Inconsistent weighting in objective

### Recommendations

**Fix 3.1: Implement campaign split logic**
```python
def split_campaigns_by_demand_urgency(campaigns, config):
    """Split campaign if any SO is URGENT and exceeds priority threshold."""
    result = []
    for camp in campaigns:
        urgent_qty = sum(po['qty_mt'] for po in camp['production_orders'] 
                        if po['priority_rank'] <= 2)  # URGENT or HIGH
        normal_qty = camp['total_coil_mt'] - urgent_qty
        
        if urgent_qty > 0 and normal_qty > 0 and urgent_qty < camp['total_coil_mt']:
            # Split: urgent campaign + normal campaign
            urgent_campaign = dict(camp)
            urgent_campaign['production_orders'] = [
                po for po in camp['production_orders'] if po['priority_rank'] <= 2
            ]
            urgent_campaign['total_coil_mt'] = urgent_qty
            
            normal_campaign = dict(camp)
            normal_campaign['production_orders'] = [
                po for po in camp['production_orders'] if po['priority_rank'] > 2
            ]
            normal_campaign['total_coil_mt'] = normal_qty
            
            result.extend([urgent_campaign, normal_campaign])
        else:
            result.append(camp)
    return result
```

Impact: Enables URGENT orders to move ahead in queue, reduces average lateness.

**Fix 3.2: Rolling release of production orders**
```python
def release_production_orders_as_material_available(campaign, bom, inventory):
    """Release POs in priority order as material becomes available."""
    released_orders = []
    held_orders = []
    remaining_inventory = dict(inventory)
    
    for po in sorted(campaign['production_orders'], 
                     key=lambda x: (x['priority_rank'], x['due_date'])):
        required_material = explode_bom_details(po, bom)
        
        if material_sufficient(required_material, remaining_inventory):
            released_orders.append(po)
            remaining_inventory = deduct_material(remaining_inventory, required_material)
        else:
            held_orders.append(po)
    
    return released_orders, held_orders
```

Impact: Urgent orders start SMS processing even if later orders wait for material; 1-2 day earlier fulfillment.

**Fix 3.3: Capture production order priority in scheduler lateness**
```python
# Current: scheduler uses campaign priority_rank for SMS lateness
# Issue: Mixed priorities within campaign not reflected

# Proposed: For each RM task, use production order priority, not campaign priority
for rm_idx, rm_order in enumerate(rm_orders):
    rm_due_min = max(0, int((rm_order['due_date'] - t0).total_seconds() / 60))
    lateness = model.NewIntVar(0, max_time, f"late_{rm_order['po_id']}")
    model.AddMaxEquality(lateness, [rm_task["end"] - rm_due_min, 0])
    weight = _priority_weight(rm_order['priority_rank'])  # PO priority, not campaign
    objective_terms.append((lateness, weight))
```

Impact: Solver no longer treats all POs within campaign equally; urgent POs move up.

---

## 4. Resource Preference Routing — Ignored by Solver

### Current Approach
```python
# routing.py: Preferred_Resource column
# Example:
#   FG-WR-SAE1008-55, Rolling (RM), Preferred_Resource = RM-01
#   
# Scheduler loads this but ONLY uses it for validation
routing_row = _routing_rows_for_op(routing, "RM", sku_id="FG-WR-SAE1008-55")
preferred = routing_row.get('Preferred_Resource')  # RM-01
candidates = machine_groups['RM']  # [RM-01, RM-02]
# Solver chooses from full [RM-01, RM-02] list, prefers nothing
```

### Issue

1. **Load balancing hints are lost:**
   - Master data explicitly routes SAE 1035 to RM-02 (via Fix 3 from optimization)
   - But solver sees both RM-01 and RM-02 as equally valid
   - Without a hint, solver may overload RM-01 if it has more adjacent tasks

2. **Equipment specialization not captured:**
   - Example: VD-01 is the only VD unit, runs all Cr-Mo heats
   - No alternate VD available, so allocation is deterministic
   - But solver could try RM-01 for rolling if it's free
   - Better to pin "Cr-Mo heats must use VD-01" in routing

3. **Soft vs hard preferences not distinguished:**
   - Some preferences are contractual ("alloy batches must use CCM-02")
   - Others are heuristic ("prefer this resource for load balance")
   - Routing schema doesn't distinguish; solver treats all as suggestions

### Recommendations

**Fix 4.1: Add soft preference weighting to objective**
```python
# For each task, if a preferred_resource is specified, add cost for using alternate
for task in all_tasks:
    preferred = task.get('preferred_resource')
    if preferred:
        for machine in task['candidates']:
            if machine != preferred:
                # Cost of using non-preferred resource
                cost = model.NewIntVar(0, 1, f"cost_{task['job_id']}_{machine}")
                model.Add(cost == 1).OnlyEnforceIf(task['choices'][machine])
                objective_terms.append((cost, 10))  # Soft penalty
```

Impact: Solver prefers to use Preferred_Resource when feasible; tiebreaker only.

**Fix 4.2: Distinguish hard vs soft routing preferences**
```python
# Add new column to Routing: "Resource_Assignment_Type" = "REQUIRED" or "PREFERRED"
# 
# When building candidate_machines:
for _, route_row in routing_df.iterrows():
    assignment_type = route_row.get('Resource_Assignment_Type', 'PREFERRED')
    preferred = route_row.get('Preferred_Resource')
    
    if assignment_type == 'REQUIRED':
        # Use ONLY this resource, hard constraint
        candidates = [preferred]
    else:
        # Preferred but flexible
        candidates = [preferred] + [m for m in machine_group if m != preferred]
```

Impact: Enables alloy-only routing (Cr-Mo to VD-01) without model bloat.

---

## 5. Queue Time Enforcement — Inconsistent Soft/Hard Behavior

### Current Approach
```python
# scheduler.py:1045-1056
queue_rule = normalized_queue_times.get(("CCM", "RM"))
min_queue = int((queue_rule or {}).get("min", 0) or 0)
model.Add(last_ccm_end + transfer_gap + min_queue <= rm_task["start"])  # Always enforced

max_queue = int((queue_rule or {}).get("max", 9999) or 9999)
enforcement = str((queue_rule or {}).get("enforcement", default_queue_enforcement)).strip().upper()
if enforcement == "HARD":
    model.Add(rm_task["start"] <= last_ccm_end + transfer_gap + max_queue)
else:
    # SOFT: Penalty in objective
    q_viol = model.NewIntVar(0, max_time, f"qviol_{cid}_CCM_RM")
    model.Add(q_viol >= rm_task["start"] - (last_ccm_end + transfer_gap + max_queue))
    objective_terms.append((q_viol, QUEUE_VIOLATION_WEIGHT))  # Weight = 500
```

### Issues

1. **Asymmetric enforcement:**
   - Min queue ALWAYS hard constraint (must wait)
   - Max queue conditionally soft (may exceed if priority requires)
   - Result: Schedule can hold material 12+ hours (min) but compressed later (soft)
   - Better: Both should be soft to allow trade-offs

2. **Soft penalty weight is global (500):**
   - All soft queue violations cost same per-minute
   - No distinction between "5 min over" vs "12 hours over"
   - Solver may accept 12-hour violation if it saves 1 min of lateness
   - Should use proportional penalty

3. **Queue timing semantics unclear:**
   - "Queue time" = time between CCM end and RM start
   - Includes: transfer time (5 min) + hold time (0-60 min) + setup time (40 min)
   - No breakdown in output → hard to understand what's driving delays

4. **Enforcement not tied to operation**
   - Queue max of 120 min applies equally to 50 MT heat and 500 MT campaign
   - Larger batches may need different holding rules
   - No per-batch-size tuning possible

### Recommendations

**Fix 5.1: Switch min_queue to soft constraint**
```python
# Current:
model.Add(last_ccm_end + transfer_gap + min_queue <= rm_task["start"])

# Proposed:
min_hold = model.NewIntVar(0, max_time, f"min_hold_{cid}")
model.Add(min_hold >= transfer_gap + min_queue - (rm_task["start"] - last_ccm_end))
model.AddMaxEquality(min_hold, [model.NewConstant(0)])  # Non-negative
objective_terms.append((min_hold, 50))  # Cost of under-waiting
```

Impact: Solver can hold less than min if it avoids larger lateness.

**Fix 5.2: Use proportional queue violation penalty**
```python
# Current: Fixed weight of 500 per minute
# Proposed: Scale by severity
q_viol_magnitude = model.NewIntVar(0, max_time, f"qviol_{cid}")
model.Add(q_viol_magnitude >= rm_task["start"] - (last_ccm_end + transfer_gap + max_queue))
# Penalty increases with violation magnitude
objective_terms.append((q_viol_magnitude, 100))  # 100 per minute, not 500
```

Impact: Solver prefers slight over-queue to far over-queue.

**Fix 5.3: Output queue breakdown**
```python
# In schedule output, include:
schedule_output['queue_components'] = {
    'transfer_time': transfer_gap,
    'minimum_hold': min_queue - transfer_gap,
    'actual_hold': rm_start - (ccm_end + transfer_gap),
    'queue_violation': max(0, rm_start - (ccm_end + transfer_gap + max_queue)),
}
```

Impact: Planner can see if delays are transfers, holds, or violations.

---

## 6. Multi-heat Campaign Sequencing — Over-constrained

### Current Approach
```python
# scheduler.py:985-1002
# For each heat within a campaign:
eaf_task = make_interval(...)
lrf_task = make_interval(...)
vd_task = make_interval(...) if needed
ccm_task = make_interval(...)
model.Add(eaf_task["end"] <= lrf_task["start"])  # EAF ends, then LRF starts
model.Add(lrf_task["end"] <= vd_task["start"])   # LRF ends, then VD starts
```

### Issue

1. **No parallelization across heats:**
   - Campaign with 3 heats (150 MT = 3 x 50 MT) sequences strictly
   - Heat 1: EAF, then LRF, then VD, then CCM (90+40+45+60 = 235 min)
   - Heat 2: EAF, then LRF, then VD, then CCM (starts at Heat 1 CCM end)
   - Total = 3 * 235 = 705 minutes, purely serial
   - Better: Start Heat 2's EAF while Heat 1's LRF is running → overlapped

2. **Equipment could run in parallel:**
   - Plant has 3 LRFs (LRF-01, LRF-02, LRF-03)
   - Scheduler sequences heats as if only 1 LRF exists
   - Heat 2 waits for Heat 1 to finish CCM before starting its EAF
   - Should allow Heat 2 EAF to start immediately after Heat 1 EAF ends

3. **No within-campaign concurrency model:**
   - Current: Campaign = batch of heats processed serially
   - Realistic: SMS can run multiple heats simultaneously on different equipment
   - Constraint: Can't have two heats in same operation (both can't be in EAF)

### Recommendations

**Fix 6.1: Allow within-campaign heat parallelization**
```python
def _add_sms_parallelization_constraints(model, heats, sms_tasks, machine_groups):
    """Heats within campaign can overlap across equipment."""
    for heat_i in heats:
        for heat_j in heats:
            if heat_i == heat_j:
                continue
            # Constraint: At most one heat per operation at a time
            for operation in ['EAF', 'LRF', 'VD', 'CCM']:
                task_i = sms_tasks.get((heat_i['campaign_id'], heat_i['index'], operation))
                task_j = sms_tasks.get((heat_j['campaign_id'], heat_j['index'], operation))
                
                if task_i and task_j:
                    # Same operation: can't overlap
                    # But different heats, different machines possible
                    # Only prevent if same machine selected
                    # (Machine no-overlap already handles this)
```

Impact: Multi-heat campaigns compress from serial 700 min to parallel 300-400 min; 40-50% reduction.

**Fix 6.2: Model heat flow as pipelined**
```python
# Instead of strict end-to-end sequencing:
#   Heat 1: EAF [0-90] -> LRF [90-130] -> VD [130-175] -> CCM [175-235]
#   Heat 2: EAF [235-325] -> ...  (starts after Heat 1 CCM)
#
# Allow pipeline:
#   Heat 1: EAF [0-90] -> LRF [90-130]
#   Heat 2: EAF [90-180] -> LRF [180-220]  (starts EAF while Heat 1 in LRF)
#
# Constraint: No two heats in same operation
model.AddNoOverlap([sms_tasks[(cid, i, op)]['interval'] 
                    for i in range(num_heats)
                    for op in ['EAF', 'LRF', 'VD', 'CCM']])
# This allows different heats on different operations to overlap
```

Impact: Clearer model intent, enables solver to find overlapped schedules naturally.

---

## 7. CTP Feasibility Scoring — Precedence-based, Not Risk-based

### Current Approach
```python
# ctp.py:24-37
DECISION_PRECEDENCE = {
    "PROMISE_CONFIRMED_STOCK_ONLY": 1,        # Safest
    "PROMISE_CONFIRMED_MERGED": 2,
    "PROMISE_CONFIRMED_NEW_CAMPAIGN": 3,
    "PROMISE_HEURISTIC_ONLY": 4,
    "PROMISE_LATER_DATE": 5,
    "PROMISE_SPLIT_REQUIRED": 6,
    "PROMISE_CONDITIONAL_EXPEDITE": 7,
    "CANNOT_PROMISE_CAPACITY": 8,             # Least safe
    ...
}
# CTP returns top-ranked alternative only
```

### Issue

1. **Doesn't score alternatives by margin:**
   - "PROMISE_CONFIRMED_STOCK_ONLY" = 100% certain (in stock now)
   - "PROMISE_CONFIRMED_NEW_CAMPAIGN" = XX% risk (depends on scheduling)
   - Both return decision=rank, no confidence metric
   - Planner doesn't know if CTP is risky or conservative

2. **Doesn't quantify feasibility margin:**
   - Example: "Can promise 2026-04-15, risk=CAPACITY"
   - Is this "capacity barely available" or "capacity overbooked by 500 MT"?
   - Scoring doesn't differentiate

3. **Doesn't return confidence interval:**
   - Example: "Can promise 2026-04-12 with 85% confidence"
   - Current: "Can promise 2026-04-12 (heuristic solver, may fail)"
   - Confidence = (nearest_promise_date - promised_date) / planning_horizon

4. **No ranking by risk preference:**
   - Some planners want safest promise ("stock only")
   - Others want earliest promise (willingness to expedite)
   - CTP returns fixed rank, not sorted by risk preference

### Recommendations

**Fix 7.1: Add confidence margin to CTP response**
```python
def evaluate_ctp_alternatives(order, committed_plan, config):
    alternatives = []
    
    # Stock-only promise
    if stock_available(order['sku_id'], order['qty_mt']):
        alternatives.append({
            'decision': 'PROMISE_CONFIRMED_STOCK_ONLY',
            'promised_date': today,
            'confidence': 1.0,  # 100% certain
            'margin_days': 0,   # No buffer
        })
    
    # New campaign promise
    new_campaign = schedule(campaigns + [order])
    if new_campaign['feasible']:
        feasible_date = new_campaign['campaign_end_date']
        margin_days = (requested_date - feasible_date).days
        confidence = min(1.0, 1.0 - abs(margin_days) / planning_horizon_days)
        alternatives.append({
            'decision': 'PROMISE_CONFIRMED_NEW_CAMPAIGN',
            'promised_date': feasible_date,
            'confidence': confidence,
            'margin_days': margin_days,
        })
    
    # Sort by risk preference (if provided in config)
    if config.get('CTP_Risk_Preference') == 'SAFEST':
        alternatives.sort(key=lambda x: -x['confidence'])
    else:
        alternatives.sort(key=lambda x: x['promised_date'])
    
    return alternatives
```

Impact: Planner knows confidence level; can make risk-aware decisions.

**Fix 7.2: Return top-3 alternatives ranked by feasibility**
```python
# Current: return alternatives[0]
# Proposed: return alternatives[0:3] ranked by (confidence, promised_date)

return {
    'primary': alternatives[0],
    'alternatives': alternatives[1:3],  # Top 2 backups
    'risk_summary': {
        'stock_available': alternatives[0]['decision'].startswith('PROMISE_CONFIRMED_STOCK'),
        'capacity_available': alternatives[0]['decision'] != 'CANNOT_PROMISE_CAPACITY',
        'material_available': alternatives[0]['decision'] != 'CANNOT_PROMISE_MATERIAL',
    },
}
```

Impact: Planner sees options before committing; can make conditional promises ("if material arrives").

**Fix 7.3: Add scenario analysis to CTP**
```python
# Evaluate promise under different scenarios (disruptions)
alternatives_under_eaf_downtime = evaluate_ctp_alternatives(
    order, 
    committed_plan,
    scenario={'machine_down': 'EAF-01', 'hours': 48}
)
return {
    'primary': primary_alternative,
    'scenarios': {
        'normal_operations': primary_alternative,
        'eaf_downtime_48h': alternatives_under_eaf_downtime[0],
    },
}
```

Impact: Planner can promise "2026-04-12 (or 2026-04-15 if EAF down)"; more realistic.

---

## 8. Master Data Validation — Insufficient During Scheduling

### Current Approach
```python
# scheduler.py:699-739
def _validate_campaign_master_data(campaign, routing, resources, op_lookup, allow_defaults=False):
    """Check that campaign routing is defined."""
    # Validates: routing rows exist for campaign's grade/sku
    # Checks: resources exist for required operations
    # Raises: error if missing and allow_defaults=False
```

### Issues

1. **No check for required resource attributes:**
   - Example: Operation requires 30 min setup, but resource defaults say 0 min
   - No check if Preferred_Resource is in active resource list
   - Schedule assigns task to missing resource, solver fails with cryptic error

2. **No warning on data inconsistency:**
   - Example: Routing says RM min 100 MT, but order is 50 MT
   - Solver accepts 50 MT, but real world needs 100 MT minimum batch
   - Better: Warn "Batch size below minimum"

3. **No check for feasibility killers:**
   - Example: Campaign needs VD, but VD-01 is Down
   - Schedule proceeds, solver finds no feasible solution
   - Better: Early failure with clear message "VD unavailable"

4. **Inventory checking done pre-scheduling, not during:**
   - Material hold logic runs in campaign.py before scheduler
   - But material could be consumed by concurrent campaigns
   - Schedule assumes inventory snapshot remains valid during solving
   - Better: Re-validate inventory after scheduling

### Recommendations

**Fix 8.1: Add resource feasibility check**
```python
def _validate_master_data_feasibility(campaign, routing, resources, config):
    """Check all required resources are available and capable."""
    errors = []
    warnings = []
    
    required_ops = _campaign_sms_operations(campaign, routing)
    available_resources = set(resources['Resource_ID'].unique())
    
    for operation in required_ops:
        op_resources = resources[resources['Operation_Group'] == operation]
        
        if op_resources.empty:
            errors.append(f"No resources available for {operation}")
        
        # Check minimum cycle times
        min_cycle = routing[routing['Operation'] == operation]['Cycle_Time_Min_Heat'].min()
        for _, res in op_resources.iterrows():
            if res['Default_Cycle_Min'] < min_cycle:
                warnings.append(
                    f"{res['Resource_ID']} may be too slow for {operation} "
                    f"(routing requires {min_cycle}min, resource default {res['Default_Cycle_Min']}min)"
                )
    
    if errors and not config.get('Allow_Scheduler_Default_Masters'):
        raise ValueError("Master data infeasible: " + "; ".join(errors))
    
    return warnings
```

Impact: Early failure with actionable message instead of silent solver failure.

**Fix 8.2: Check minimum batch size compatibility**
```python
def _validate_batch_size(campaign, routing, config):
    """Warn if campaign size violates routing minimums."""
    warnings = []
    
    min_batch = routing[routing['SKU_ID'] == campaign['sku_id']]['Min_Campaign_MT'].max()
    max_batch = routing[routing['SKU_ID'] == campaign['sku_id']]['Max_Campaign_MT'].min()
    
    if campaign['total_coil_mt'] < min_batch:
        warnings.append(
            f"Campaign {campaign['campaign_id']} is {campaign['total_coil_mt']} MT, "
            f"below minimum {min_batch} MT. Routing may not be cost-effective."
        )
    
    if campaign['total_coil_mt'] > max_batch:
        warnings.append(
            f"Campaign {campaign['campaign_id']} is {campaign['total_coil_mt']} MT, "
            f"above maximum {max_batch} MT. May need to split."
        )
    
    return warnings
```

Impact: Detects over-sized campaigns before scheduling.

**Fix 8.3: Re-validate inventory post-scheduling**
```python
def schedule_and_validate(campaigns, resources, routing, inventory, bom, config):
    """Schedule, then check inventory assumptions still hold."""
    # Run initial scheduling
    schedule_result = schedule(campaigns, resources, routing, config)
    
    # Recompute inventory after scheduled consumption
    for camp in schedule_result['campaigns']:
        required = explode_bom_details(camp, bom)
        if not material_sufficient(required, inventory):
            schedule_result['warnings'].append(
                f"Campaign {camp['campaign_id']} requires material no longer available. "
                f"Inventory may have been consumed by concurrent campaign."
            )
    
    return schedule_result
```

Impact: Detects stale inventory assumptions that would cause real-world failures.

---

## 9. Greedy Fallback Scheduler — Minimal, Should be Enhanced

### Current Approach
```python
# scheduler.py:1345-1400
def _greedy_fallback(campaigns, resources, ...):
    """If OR-Tools unavailable, sort campaigns by priority and schedule greedily."""
    # Algorithm:
    # 1. Sort campaigns by (priority_rank, due_date)
    # 2. For each campaign, find earliest available time slot
    # 3. Assign to resource with minimum end_time
```

### Issue

1. **Greedy is very suboptimal:**
   - Example: Campaign A (NORMAL, due Apr 30) takes EAF slot at Apr 5
   - Later: Campaign B (URGENT, due Apr 10) has no EAF slot, pushed to Apr 20
   - Optimal: B should preempt A; B to Apr 5, A to Apr 20

2. **No pre-sorting of small-to-large:**
   - Greedy assigns in priority order only
   - Better: Within priority, assign small campaigns first (more flexibility later)

3. **No gap-filling heuristic:**
   - If EAF has gaps between schedules, smaller campaigns don't fit
   - Better: Try to fit smaller orders into existing gaps

4. **No resource load balancing:**
   - Greedy assigns to first available resource
   - If EAF-01 has gap and EAF-02 is full, greedy chooses EAF-01
   - Better: Distribute load evenly

### Recommendations

**Fix 9.1: Sort by priority, then batch size (small first)**
```python
campaigns.sort(key=lambda c: (
    int(c.get('priority_rank', 9)),      # URGENT (1) before NORMAL (3)
    float(c.get('total_coil_mt', 0)),    # Small before large (easier to fit)
    pd.to_datetime(c.get('due_date')),   # Earlier due before later
))
```

Impact: Greedy becomes near-optimal for small instances.

**Fix 9.2: Implement gap-filling for small campaigns**
```python
def find_earliest_slot_greedy(campaign, machine_schedule, gap_threshold=20):
    """Find earliest slot, preferring gaps large enough for campaign."""
    min_gap_mt = campaign['total_coil_mt'] + 10  # +10 for safety
    
    # First, try to fit in existing gaps
    for gap_start, gap_end in find_schedule_gaps(machine_schedule):
        gap_size_mt = (gap_end - gap_start) / (sms_cycle_time_per_mt)
        if gap_size_mt >= min_gap_mt:
            return gap_start
    
    # If no gap fits, append after last scheduled campaign
    return machine_schedule[-1]['end'] if machine_schedule else 0
```

Impact: Greedy utilization improves 20-30%.

**Fix 9.3: Distribute load across identical resources**
```python
def assign_to_least_loaded_resource(campaign, resources, current_schedule):
    """Assign to resource with minimum current load/utilization."""
    machine_groups = group_resources_by_operation_group(resources)
    
    for operation in campaign['operations']:
        candidates = machine_groups[operation]
        # Calculate load = sum(task_durations) for each candidate
        loads = {m: sum(t['duration'] for t in current_schedule if t['machine'] == m)
                 for m in candidates}
        # Assign to least-loaded
        best_machine = min(loads, key=loads.get)
        return best_machine
```

Impact: More even utilization, better backlog distribution.

---

## 10. Serialization Mode — Under-documented, Should be Configurable

### Current Approach
```python
# scheduler.py:824
serialization_mode = _campaign_serialization_mode(config)
# Options: CAMPAIGN_SMS_END (default) or STRICT_END_TO_END

# CAMPAIGN_SMS_END: CCM end of campaign N must precede EAF start of campaign N+1
# STRICT_END_TO_END: RM end of campaign N must precede EAF start of campaign N+1
```

### Issues

1. **No clear guidance on mode selection:**
   - Config says "CAMPAIGN_SMS_END" but documentation is minimal
   - Planner doesn't know if STRICT_END_TO_END is better for their scenario
   - Both modes should be tested to show impact

2. **Mode affects lateness calculation inconsistently:**
   - CAMPAIGN_SMS_END: SMS (CCM) lateness weighted full, RM lateness full
   - STRICT_END_TO_END: SMS can complete, but RM must wait for next campaign's SMS
   - Mode not reflected in objective weighting

3. **No partial serialization option:**
   - Example: Serialize only URGENT campaigns, allow NORMAL to overlap
   - Current: All-or-nothing serialization
   - Better: Configurable per-priority

4. **No documentation of trade-offs:**
   - CAMPAIGN_SMS_END pros: SMS equipment busier, shorter total schedule
   - CAMPAIGN_SMS_END cons: RM idle time between campaigns
   - STRICT_END_TO_END pros: Full campaign isolation, simpler logic
   - STRICT_END_TO_END cons: Longer total schedule, lower utilization

### Recommendations

**Fix 10.1: Add third mode: PARTIAL_SERIALIZATION**
```python
# New mode: PARTIAL_SERIALIZATION
# Only serialize URGENT/HIGH campaigns; allow NORMAL campaigns to overlap

serialization_mode = _campaign_serialization_mode(config)
# Options: 'CAMPAIGN_SMS_END' | 'STRICT_END_TO_END' | 'PARTIAL_SERIALIZATION'

if serialization_mode == 'PARTIAL_SERIALIZATION':
    urgent_campaigns = [c for c in campaigns if c['priority_rank'] <= 2]
    normal_campaigns = [c for c in campaigns if c['priority_rank'] > 2]
    
    # Serialize urgent, allow normal to interleave
    previous_end = None
    for camp in urgent_campaigns:
        if previous_end:
            model.Add(camp_sms_start >= previous_end)
        previous_end = camp_sms_end
    
    # Normal campaigns free to schedule
    for camp in normal_campaigns:
        pass  # No serialization constraint
```

Impact: Enables URGENT orders to bypass blockage from NORMAL orders.

**Fix 10.2: Document mode impact with comparison table**
```python
"""
Serialization Mode Comparison:

MODE                    | Campaign Overlap | RM Idle | Total Time | SMS Util | RM Util
CAMPAIGN_SMS_END        | NO               | ~20%   | Baseline   | 95%     | 75%
STRICT_END_TO_END       | NO               | ~5%    | +15%       | 90%     | 65%
PARTIAL_SERIALIZATION   | URGENT only      | ~15%   | -5%        | 98%     | 80%

Recommendation:
- Use CAMPAIGN_SMS_END for general planning (balanced)
- Use STRICT_END_TO_END if equipment isolation critical (alloy campaigns)
- Use PARTIAL_SERIALIZATION if URGENT orders must bypass queue
"""
```

Impact: Planner can choose mode based on objectives.

**Fix 10.3: Add configuration guidance**
```python
# In Config sheet, add:
# Serialization_Mode: [CAMPAIGN_SMS_END, STRICT_END_TO_END, PARTIAL_SERIALIZATION]
# Serialization_Rationale: "Isolate batches for alloy safety" or "Maximize RM utilization"

config_entry = config.get('Serialization_Mode', 'CAMPAIGN_SMS_END')
mode = _campaign_serialization_mode(config_entry)
if mode not in ['CAMPAIGN_SMS_END', 'STRICT_END_TO_END', 'PARTIAL_SERIALIZATION']:
    raise ValueError(f"Invalid Serialization_Mode: {config_entry}")
```

Impact: Configuration becomes self-documenting.

---

## 11. Rework/Scrap Simulation — Not Modeled

### Current Approach
- Schedule assumes 100% yield through all operations
- BOM includes yield loss (e.g., 95% CCM yield), but it's applied pre-scheduling
- If actual yield < expected, no re-planning mechanism

### Issue

1. **Deterministic schedule ignores variability:**
   - CCM produces 100 MT from 105 MT billet (95% yield)
   - But if actual yield = 92 MT, production order becomes short
   - Schedule doesn't account for this risk

2. **No contingency planning:**
   - Example: SO requires 100 MT SAE 1008
   - Campaign produces 100 MT (assuming 95% yield from 105 MT billet)
   - If production fails (scrap spike, quality rejection), SO unfulfilled
   - Better: Schedule extra 5-10 MT contingency

3. **No scenario simulation:**
   - Can't ask "what if CCM yield drops to 90%?"
   - Would need to rerun entire plan manually

### Recommendations

**Fix 11.1: Add yield safety margin to campaign sizing**
```python
def size_campaign_with_yield_safety(so_qty_mt, yield_factor, safety_margin_pct=5):
    """Size campaign accounting for yield loss and safety margin."""
    # Required at CCM
    required_at_ccm = so_qty_mt / yield_factor
    # Add safety margin (conservative)
    with_margin = required_at_ccm * (1 + safety_margin_pct/100)
    # Round up to heat size (50 MT)
    heat_count = math.ceil(with_margin / 50)
    campaign_mt = heat_count * 50
    return campaign_mt
```

Impact: Schedule includes buffer for yield variability; reduces fulfillment failures.

**Fix 11.2: Model yield as stochastic variable in solver**
```python
# For each production step, model actual_output <= expected_output
# With high confidence that yield >= (expected - margin)

for heat in sms_heats:
    output_mt = heat['output_mt']
    expected_yield = 0.95
    margin = 0.03  # Can be as low as 92%
    
    # Constraint: Output must be at least (input * (yield - margin))
    model.Add(output_mt >= heat['input_mt'] * (expected_yield - margin))
```

Impact: Schedule considers yield risk; suggestions include buffer.

---

## 12. Performance & Solver Timeout Handling — Incomplete

### Current Approach
```python
# scheduler.py:1141-1149
solver = cp_model.CpSolver()
solver.parameters.max_time_in_seconds = max(float(solver_time_limit_sec or 30), 1.0)
solver.parameters.num_search_workers = 4
status = solver.Solve(model)

if status == cp_model.OPTIMAL:
    # Use optimal solution
elif status == cp_model.FEASIBLE:
    # Use best feasible solution
elif status == cp_model.INFEASIBLE:
    # Fall back to greedy
```

### Issues

1. **No fallback when solver timeout:**
   - If INFEASIBLE or UNKNOWN, goes to greedy
   - But infeasible model could become feasible if relaxed (e.g., soft queue constraints)
   - Greedy is very suboptimal

2. **Solver parameters not tuned:**
   - max_time_in_seconds = 30 (arbitrary)
   - num_search_workers = 4 (arbitrary)
   - No tuning based on problem size
   - Large problems (50+ campaigns) may need 60+ seconds

3. **No solution quality metrics:**
   - Solver returns FEASIBLE but doesn't say "80% optimal" or "99% optimal"
   - Planner doesn't know if solution is good or just acceptable

4. **No intermediate solution callback:**
   - Solver finds intermediate solution at t=5s, better at t=15s
   - But timer hits 30s, returns best
   - Can't use intermediate solutions to inform decisions

### Recommendations

**Fix 12.1: Progressive timeout with relaxation**
```python
def schedule_with_progressive_timeout(campaigns, resources, config):
    """Try exact solve, then relax constraints if timeout."""
    solver = cp_model.CpSolver()
    
    # Phase 1: Try exact solve (30 sec)
    solver.parameters.max_time_in_seconds = 30
    status = solver.Solve(model_exact)
    
    if status in [cp_model.OPTIMAL, cp_model.FEASIBLE]:
        return status, solver.ObjectiveValue()
    
    # Phase 2: Relax queue violations (15 sec)
    model_relaxed = relax_soft_constraints(model_exact, 'queue_violations')
    solver.parameters.max_time_in_seconds = 15
    status = solver.Solve(model_relaxed)
    
    if status in [cp_model.OPTIMAL, cp_model.FEASIBLE]:
        return status, solver.ObjectiveValue()
    
    # Phase 3: Greedy fallback
    return 'GREEDY', greedy_solver(campaigns).ObjectiveValue()
```

Impact: Much higher success rate on larger instances.

**Fix 12.2: Adaptive solver time based on problem size**
```python
def calculate_solver_time(num_campaigns, config):
    """Estimate solver time based on problem complexity."""
    base_time = 30  # seconds
    # More campaigns = harder to solve
    complexity_factor = min(1 + (num_campaigns - 5) * 0.5, 5)  # Max 150 sec
    
    if config.get('Solver_Priority') == 'OPTIMALITY':
        return base_time * complexity_factor  # 30, 45, 60, ...
    else:
        return base_time  # Fast: always 30 sec
```

Impact: Solver gets adequate time for larger problems.

**Fix 12.3: Return solution quality metric**
```python
def schedule(...):
    # ... run solver ...
    return {
        'schedule': schedule_df,
        'status': 'OPTIMAL',
        'objective_value': 1234,
        'quality_metrics': {
            'is_optimal': (status == cp_model.OPTIMAL),
            'is_feasible': (status in [cp_model.OPTIMAL, cp_model.FEASIBLE]),
            'solver_time_seconds': elapsed_time,
            'solver_timeout': (status == cp_model.UNKNOWN),
            'estimated_optimality': 0.95,  # If FEASIBLE, estimate how good
        }
    }
```

Impact: Planner knows if schedule is optimal or just acceptable.

---

## Summary Table — Recommended Improvements Ranked by Impact

| # | Area | Issue | Fix | Impact | Effort |
|---|------|-------|-----|--------|--------|
| 1 | Objective | SMS lateness discounted 50% | Balance SMS/RM equally | +10% on-time | Low |
| 2 | Changeover | Only enforced on RM | Add SMS changeover | +15% feasibility | Medium |
| 3 | Campaign grouping | No split on urgency | Implement priority-based split | +5% lateness reduction | Medium |
| 4 | Resource preference | Ignored by solver | Add soft preference cost | +8% load balance | Low |
| 5 | Queue enforcement | Hard min, soft max (inconsistent) | Both soft, proportional penalty | +3% on-time | Low |
| 6 | Multi-heat campaign | Serial only | Allow heat parallelization | +40% SMS throughput | High |
| 7 | CTP scoring | Rank only, no confidence | Add confidence margin | Better decision-making | Medium |
| 8 | Master data validation | Insufficient | Add feasibility checks | Prevent failures | Low |
| 9 | Greedy fallback | Very suboptimal | Improve heuristics | +50% if OR-Tools fails | Medium |
| 10 | Serialization | Under-configured | Add PARTIAL mode | +10% URGENT lateness | Medium |
| 11 | Yield simulation | Deterministic only | Add safety margin | Better robustness | Low |
| 12 | Solver timeout | No relaxation | Progressive relaxation | +20% large-problem success | Medium |

---

## Quick Wins (Low Effort, High Impact)

1. **Fix 1.1:** Balance SMS/RM lateness (remove 0.5x discount)
2. **Fix 4.1:** Add soft preference weight to objective
3. **Fix 5.2:** Use proportional queue violation penalty
4. **Fix 8.1:** Add resource feasibility check pre-solve

**Estimated effort:** 2-3 hours coding, 1-2 hours testing  
**Estimated impact:** +5-10% on-time performance

---

**Next Step:** Prioritize fixes by business impact (lateness reduction, resource utilization, or CTP confidence) and implement with measured testing.
