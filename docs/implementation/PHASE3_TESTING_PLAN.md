# Phase 3: System Integration, Testing & Documentation

**Goal:** Validate 100% config-driven system, document parameters, identify simplification opportunities, ensure APS follows standard OR-Tools patterns.

**Timeline:** ~8-10 hours over 2-3 sessions

**Status:** In Progress (April 4, 2026)

---

## 1. System Integration Testing (2-3 hours)

### 1.1 Multi-Module Config Propagation
**Objective:** Verify all 5 modules respond to config changes correctly

**Test Cases:**
- [ ] Change `HEAT_SIZE_MT`: Verify campaign batch sizing changes
- [ ] Change `OBJECTIVE_QUEUE_VIOLATION_WEIGHT`: Verify scheduler objective weights shift
- [ ] Change `YIELD_CCM_PCT`: Verify BOM explosion yield calculations
- [ ] Change `CAPACITY_HORIZON_DAYS`: Verify capacity map horizon expands/contracts
- [ ] Change `CTP_SCORE_STOCK_ONLY`: Verify promise scoring changes

**Test Method:**
```python
# For each config parameter:
1. Load workbook with parameter X = default
2. Run API endpoint (POST /api/run/aps)
3. Capture output (demand, schedule, CTP results)
4. Modify parameter X to 2x or 0.5x default
5. Rerun endpoint
6. Verify metric Y changes proportionally
```

### 1.2 Multi-Scenario Stress Testing
**Objective:** Test system stability under varied config combinations

**Scenarios:**
- [ ] Low capacity (reduce all `Avail_Hours_Day` in resources)
- [ ] Aggressive scheduling (increase `OBJECTIVE_QUEUE_VIOLATION_WEIGHT`)
- [ ] High yield variability (reduce `YIELD_RM_*_PCT`)
- [ ] Tight material planning (reduce `Safety_Stock_MT`)
- [ ] Extreme batch sizing (change `HEAT_SIZE_MT` to 20 vs 100)

**Expected Outcomes:**
- System should not crash
- Results should be logically consistent (no NaN, Inf, negative values)
- Solver status should indicate feasibility or clear blockers

### 1.3 End-to-End Workflow Testing
**Objective:** Verify complete flow from input to output

**Test Flow:**
```
1. POST /api/run/aps → generates campaigns
2. GET /api/material/plan → explodes BOM
3. GET /api/capacity/summary → computes utilization
4. POST /api/ctp/promise → evaluates order feasibility
5. Verify no data gaps or contradictions across outputs
```

---

## 2. Configuration Parameter Documentation (2-3 hours)

### 2.1 Parameter Tuning Guide
**Create:** `docs/reference/PARAMETER_TUNING_GUIDE.md`

**Format for Each Parameter:**
```markdown
### Parameter Name
- **Category:** Scheduler | Campaign | BOM | CTP | Capacity
- **Type:** Points | Hours | Percentage | Threshold | List | Boolean
- **Current Value:** X
- **Valid Range:** [min, max]
- **Units:** minutes | MT | percent | points
- **Effect:** What changes when this parameter changes
- **Tuning Guide:** When to increase/decrease and why
- **Example:** Concrete tuning scenario
```

**Priority (High → Low):**
1. **Scheduler:** OBJECTIVE_QUEUE_VIOLATION_WEIGHT, PRIORITY_WEIGHT_*
2. **Campaign:** HEAT_SIZE_MT, YIELD_*_PCT
3. **CTP:** CTP_SCORE_* parameters
4. **Capacity:** CAPACITY_HORIZON_DAYS
5. **BOM:** YIELD_MIN_BOUND_PCT, ZERO_TOLERANCE_THRESHOLD

### 2.2 Configuration Presets
**Create:** `docs/reference/CONFIG_PRESETS.md`

**Preset Scenarios:**
- [ ] **Conservative** (minimize risk)
  - High safety stocks, loose capacity, high queue penalties
- [ ] **Aggressive** (maximize throughput)
  - Low safety stocks, tight capacity, low queue penalties
- [ ] **Balanced** (default configuration)
- [ ] **MTO** (make-to-order focus)
- [ ] **Capacity-Limited** (resource constraints dominate)

### 2.3 Parameter Dependency Map
**Create:** `docs/reference/PARAMETER_DEPENDENCIES.md`

**Show:**
- Which parameters affect which outputs
- Parameter interactions (if parameter A changes, parameter B's effect changes)
- Constraints (if you change X, Y must also change to maintain consistency)

---

## 3. Simplification Audit (1-2 hours)

### 3.1 Identify Non-Standard Patterns
**Search for:**
- [ ] Remaining hardcoded values (regex: `= [0-9]+\.?[0-9]*` in engine/)
- [ ] Custom optimization logic outside OR-Tools model
- [ ] Duplicate implementations of similar functionality
- [ ] Complex conditional logic that could be parameterized

**Target:** Reduce to <5 non-configurable magic constants

### 3.2 Identify Overconfiguration
**Check:**
- [ ] Parameters that are never read
- [ ] Parameters that always have same value (could be removed)
- [ ] Parameters with mutually exclusive values (could be collapsed)

**Target:** Keep configuration focused on truly variable business rules

### 3.3 OR-Tools Alignment Check
**Verify:**
- [ ] Objective function is standard (linear combination of weighted terms)
- [ ] All constraints follow OR-Tools patterns
- [ ] Solver configuration is standard (no custom hacks)
- [ ] Solution approach is idiomatic (not reinventing wheel)

**Document:** `docs/design/OR_TOOLS_ALIGNMENT.md`

---

## 4. Documentation Consolidation (1-2 hours)

### 4.1 System Architecture Documentation
**Update:** `docs/design/ARCHITECTURE.md`

**Sections:**
- System overview (5 modules, 100% config-driven)
- Data flow diagram (input → config → output)
- Module responsibilities
- Config as system hub

### 4.2 API Documentation
**Verify:** `docs/api/API_REFERENCE.md`

**Ensure:**
- [ ] All endpoints documented
- [ ] Config endpoints fully described
- [ ] Example payloads for config modifications
- [ ] Testing examples

### 4.3 Operations Guide
**Create:** `docs/operations/RUNBOOK.md`

**Topics:**
- [ ] How to run the system
- [ ] How to modify configuration
- [ ] How to interpret results
- [ ] How to troubleshoot common issues
- [ ] How to measure impact of config changes

---

## 5. Acceptance Criteria

### Phase 3 Complete When:
- [ ] All 6 integration test cases pass
- [ ] All 47 parameters documented with tuning guidance
- [ ] <5 non-configurable constants remain in code
- [ ] Architecture documentation updated
- [ ] Runbook created and validated
- [ ] No pre-existing test failures introduced
- [ ] System passes stress test (multi-scenario runs stable)

### Code Quality:
- [ ] All getter functions follow naming pattern `_get_*`
- [ ] All config reads use singleton `get_config()`
- [ ] No circular config dependencies
- [ ] 100% of hardcoded business rules → config OR documented
- [ ] Configuration values have meaningful defaults

---

## 6. Deliverables

| Deliverable | File | Status |
|---|---|---|
| Integration Test Suite | `tests/test_phase3_integration.py` | Pending |
| Parameter Tuning Guide | `docs/reference/PARAMETER_TUNING_GUIDE.md` | Pending |
| Config Presets | `docs/reference/CONFIG_PRESETS.md` | Pending |
| Parameter Dependencies | `docs/reference/PARAMETER_DEPENDENCIES.md` | Pending |
| OR-Tools Alignment | `docs/design/OR_TOOLS_ALIGNMENT.md` | Pending |
| Architecture Update | `docs/design/ARCHITECTURE.md` | Pending |
| Operations Runbook | `docs/operations/RUNBOOK.md` | Pending |
| Simplification Report | `docs/analysis/SIMPLIFICATION_AUDIT.md` | Pending |

---

## 7. Success Metrics

| Metric | Target | Current |
|---|---|---|
| Config Parameters Utilized | 47/47 (100%) | ✅ 47/47 |
| Modules Config-Driven | 5/5 (100%) | ✅ 5/5 |
| Tests Passing | ≥93/96 (97%) | ✅ 93/96 |
| Non-Config Constants | <5 | TBD |
| Parameter Docs Complete | 47/47 | 0/47 |
| Integration Tests Pass | All | 0/6 |
| Zero Config Conflicts | 0 conflicts | TBD |

---

**Next Step:** Begin integration test suite development (Section 1)
