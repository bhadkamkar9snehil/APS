# APS Audit Findings

Date: 2026-04-02
Scope: Codebase audit against `APS_Implementation_Plan_Config_Masters.md`
Method: Source audit of the active runtime codepaths in `aps_functions.py`, `engine/`, `scenarios/`, `setup_excel.py`, `build_template_v3.py`, `run_all.py`, and workbook integration files.

## Summary

The plan is partially implemented.

Implemented in code:
- New schema markers for `Config`, `Queue_Times`, `CTP_Request`, `CTP_Output`
- Config and queue-time readers
- Resource-driven operation colors and dynamic scenario headers
- Transfer-time and queue-time handling in the scheduler
- `Yield_Pct` support in BOM logic
- Initial CTP engine implementation in `engine/ctp.py`

Not fully complete:
- CTP workbook integration
- Correct CTP committed-capacity behavior
- True generic optional-step behavior
- Consistent config ownership of campaign sizing
- Reliable fallback behavior in a few reader paths

## Findings

### 1. High: CTP reserves capacity for work that is not actually committed

`run_ctp_for_workbook()` passes all campaigns from `_campaigns_from_data()` into CTP, not only committed/released work.

Relevant code:
- [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L3807)
- [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L723)
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L127)
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L205)

Why this is a problem:
- Inventory netting inside CTP filters to `RELEASED` and `RUNNING LOCK`
- Capacity scheduling inside CTP uses `committed_campaigns + ghost_campaigns` without the same filter
- That can make CTP later or infeasible because uncommitted work is still consuming solver capacity

Impact:
- CTP answers can be materially more pessimistic than the actual committed plan
- Material and capacity assumptions are inconsistent inside the same CTP result

### 2. High: CTP reports campaign joining, but does not actually merge into an existing campaign

`_join_candidate()` detects a compatible existing campaign, but the ghost campaign is still renamed and scheduled as a new independent campaign.

Relevant code:
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L103)
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L177)
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L206)

Why this is a problem:
- `joins_campaign` can claim the request would merge
- `new_campaign_needed` can say no new campaign is needed
- But the schedule simulation still evaluates a separate `CTP-###` campaign

Impact:
- The reported result and the simulated scheduling path do not match
- Earliest delivery and bottleneck answers can be misleading

### 3. Medium-High: Campaign-size control is inconsistent between live scheduling and CTP

Normal planning reads min/max campaign size from `Scenarios` first and only falls back to `Config`, while CTP reads directly from `Config`.

Relevant code:
- [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L615)
- [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L723)
- [engine/ctp.py](/c:/Users/bhadk/Documents/APS/engine/ctp.py#L149)

Why this is a problem:
- The implementation plan says these defaults moved into `Config`
- In the actual runtime, populated `Scenarios` values override normal scheduling behavior
- CTP can therefore build different campaign sizes than the operational schedule

Impact:
- CTP and live planning can disagree on the same request
- Changing `Config` may not affect normal runs the way the plan promises

### 4. Medium-High: Generic optional-operation support is only partial

The routing logic supports `Is_Optional` and `Optional_Condition`, but campaign objects do not carry arbitrary SKU attributes through to the scheduler. In practice, the logic still mainly supports VD-style behavior.

Relevant code:
- [engine/scheduler.py](/c:/Users/bhadk/Documents/APS/engine/scheduler.py#L394)
- [engine/campaign.py](/c:/Users/bhadk/Documents/APS/engine/campaign.py#L342)
- [engine/campaign.py](/c:/Users/bhadk/Documents/APS/engine/campaign.py#L553)

Why this is a problem:
- The plan describes a generic, data-driven condition model
- The current scheduler explicitly recognizes `NEEDS_VD` and `ROUTE_VARIANT`
- Other conditions only work if the matching attribute is already copied onto the campaign dict

Impact:
- Adding new optional operations still risks requiring Python changes
- The industry-agnostic goal is not fully achieved

### 5. Medium: CTP exists in Python but is not fully integrated into the workbook UX

The engine and rendering code exist, but the workbook action path is incomplete.

Missing or incomplete pieces:
- No `RunCTP` VBA macro in [APS_Macros.bas](/c:/Users/bhadk/Documents/APS/APS_Macros.bas#L1)
- No simple `run_ctp()` xlwings entrypoint beside the other exported functions in [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L4041)
- No CTP button wiring in [setup_excel.py](/c:/Users/bhadk/Documents/APS/setup_excel.py#L78)
- Template text still describes CTP as a future action in [build_template_v3.py](/c:/Users/bhadk/Documents/APS/build_template_v3.py#L906) and [build_template_v3.py](/c:/Users/bhadk/Documents/APS/build_template_v3.py#L1092)

Impact:
- The feature is not fully reachable from the workbook flow promised by the implementation plan

### 6. Medium: Queue-time and BOM fallback paths contain fragile or incorrect fallback logic

Relevant code:
- [aps_functions.py](/c:/Users/bhadk/Documents/APS/aps_functions.py#L406)
- [engine/scheduler.py](/c:/Users/bhadk/Documents/APS/engine/scheduler.py#L145)
- [engine/bom_explosion.py](/c:/Users/bhadk/Documents/APS/engine/bom_explosion.py#L22)

Issues:
- Queue-time readers use `int(pd.to_numeric(..., errors="coerce") or default)`, which is unsafe around blank/NaN values
- `_input_bom_rows()` checks for `"Scrap_%"` but populates `"Scrap_%"`? No: it writes `rows["Scrap_%"]`, so the absence check is against the wrong column name

Impact:
- Missing or blank workbook values can produce brittle behavior
- The BOM scrap fallback is not as robust as intended

### 7. Medium: Capacity analysis remains steel-specific and can diverge from the generalized scheduler

The scheduler now consumes routing sequence, optional steps, transfer times, and queue times, but `compute_demand_hours()` still hardcodes the EAF/LRF/VD/CCM/RM flow and uses `needs_vd`.

Relevant code:
- [engine/capacity.py](/c:/Users/bhadk/Documents/APS/engine/capacity.py#L12)

Why this matters:
- The implementation plan moved toward data-driven operation behavior
- Capacity analysis is still using a fixed steel route

Impact:
- Capacity results can drift from scheduling behavior
- This limits the industry-agnostic generalization promised in the plan

### 8. Medium-Low: CLI/runtime support does not expose CTP

The command runner only supports `bom`, `capacity`, `schedule`, `scenarios`, `clear`.

Relevant code:
- [run_all.py](/c:/Users/bhadk/Documents/APS/run_all.py#L53)

Impact:
- Even outside Excel, CTP is not part of the supported execution surface

### 9. Medium-Low: The standalone data loader has not been updated to the new plan surface

`data/loader.py` still reads the older core sheets only and does not load `Config`, `Queue_Times`, or CTP-related sheets.

Relevant code:
- [data/loader.py](/c:/Users/bhadk/Documents/APS/data/loader.py#L10)

Impact:
- Any workflows that rely on `data.loader.load_all()` do not see the full generalized workbook model
- This creates drift between the standalone loader and the xlwings runtime path

### 10. Low: There is no automated test coverage for the new plan features

There are no repo tests covering queue constraints, transfer times, config-driven campaign sizing, dynamic optional operations, or CTP behavior.

Relevant references:
- [README.md](/c:/Users/bhadk/Documents/APS/README.md#L312)

Impact:
- Regression risk is high, especially for workbook-schema-dependent logic
- The newer features are difficult to verify confidently after future changes

## Overall Assessment

The codebase is not in a “plan complete” state.

Best description:
- Schema work: mostly present
- Core scheduler changes: largely present
- CTP: partially implemented, but not reliable enough to treat as complete
- Industry-agnostic generalization: directionally improved, not fully achieved
- Workbook integration: incomplete for CTP

## Verification Limits

This audit was primarily source-based.

I was not able to run the APS end-to-end in the current shell session because:
- `python`/`py` were not available on PATH
- A local Python runtime was present, but it did not have required packages such as `pandas`

That means the findings above were derived from code inspection rather than full execution verification.

## Recommended Next Fixes

1. Fix CTP to use only committed campaigns for capacity reservation.
2. Either implement true campaign-join simulation in CTP or remove the join claim from the output.
3. Unify campaign-size precedence so `Config` and live scheduling behave consistently.
4. Finish workbook integration for CTP: VBA macro, button wiring, exported entrypoint, and template copy.
5. Make optional-operation conditions truly data-driven by carrying required SKU attributes into campaign objects.
6. Harden queue-time numeric parsing and fix the BOM scrap fallback typo.
7. Add basic automated tests for CTP, queue constraints, transfer times, and config-driven campaign sizing.
