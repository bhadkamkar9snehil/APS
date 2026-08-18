# Python Scripts Called by Excel Macros - Analysis

## Excel Macros (from APS_Macros.bas)

The Excel workbook contains the following macros that call Python functions:

```vba
Sub RunBOMExplosion()
    RunPython "import aps_functions; aps_functions.run_bom_explosion()"
End Sub

Sub RunCapacityMap()
    RunPython "import aps_functions; aps_functions.run_capacity_map()"
End Sub

Sub RunSchedule()
    RunPython "import aps_functions; aps_functions.run_schedule()"
End Sub

Sub RunScenarios()
    RunPython "import aps_functions; aps_functions.run_scenario()"
End Sub

Sub RunCTP()
    RunPython "import aps_functions; aps_functions.run_ctp()"
End Sub

Sub ClearOutputs()
    RunPython "import aps_functions; aps_functions.clear_outputs()"
End Sub
```

## Mapping: Excel Macros → aps_functions.py → xaps_application_api.py

### 1. RunBOMExplosion()
- **Macro calls:** `aps_functions.run_bom_explosion()`
- **Function location:** aps_functions.py line 4411
- **Implementation calls:** `run_bom_explosion_for_workbook()` (line 3775)
- **App endpoint:** `/api/run/bom` (line 528 in xaps_application_api.py)
- **Status:** ✓ BOTH IMPLEMENTATIONS AVAILABLE (Legacy + REST API)

### 2. RunCapacityMap()
- **Macro calls:** `aps_functions.run_capacity_map()`
- **Function location:** aps_functions.py line 4415
- **Implementation calls:** `run_capacity_map_for_workbook()` (line 3833)
- **App endpoints:** 
  - `/api/aps/capacity/map` (line 846 in xaps_application_api.py)
  - `/api/aps/capacity/bottlenecks` (line 851)
  - `/api/data/capacity` (line 523)
- **Status:** ✓ BOTH IMPLEMENTATIONS AVAILABLE (Legacy + REST API)

### 3. RunSchedule()
- **Macro calls:** `aps_functions.run_schedule()`
- **Function location:** aps_functions.py line 4419
- **Implementation calls:** `run_schedule_for_workbook()` (line 3914)
- **App endpoint:** `/api/aps/schedule/run` (POST) (handled by run_schedule_api() line 620)
- **Status:** ✓ BOTH IMPLEMENTATIONS AVAILABLE (Legacy + REST API)

### 4. RunScenarios()
- **Macro calls:** `aps_functions.run_scenario()`
- **Function location:** aps_functions.py line 4423
- **Implementation calls:** `run_scenario_for_workbook()` (line 4263)
- **App endpoints:**
  - `/api/aps/scenarios/list` (line 892)
  - `/api/aps/scenarios` (POST) (line 897)
  - `/api/aps/scenarios/apply` (POST) (line 933)
  - `/api/aps/scenarios/output` (line 928)
- **Status:** ✓ BOTH IMPLEMENTATIONS AVAILABLE (Legacy + REST API)

### 5. RunCTP()
- **Macro calls:** `aps_functions.run_ctp()`
- **Function location:** aps_functions.py line 4431
- **Implementation calls:** `run_ctp_for_workbook()` (line 4120)
- **App endpoint:** `/api/run/ctp` (POST) (handled by run_ctp_api() line 659)
- **Status:** ✓ BOTH IMPLEMENTATIONS AVAILABLE (Legacy + REST API)

### 6. ClearOutputs()
- **Macro calls:** `aps_functions.clear_outputs()`
- **Function location:** aps_functions.py line 4427
- **Implementation calls:** `clear_outputs_for_workbook()` (line 4332)
- **App endpoint:** `/api/aps/clear-outputs` (POST) - NEWLY ADDED
- **Status:** ✓ NOW COMPLETE - REST API ENDPOINT ADDED

## Additional App Endpoints

The Flask app also has these supporting endpoints:

- `/api/aps/schedule/gantt` (line 799) - Gantt chart data
- `/api/aps/dispatch/board` (line 831) - Dispatch resources
- `/api/aps/dispatch/resources/<resource_id>` (line 836)
- `/api/aps/material/plan` (line 857) - Material allocation
- `/api/aps/material/holds` (line 862) - Material holds
- `/api/aps/campaigns/list` (line 515)
- `/api/aps/capacity/map` (line 846)
- `/api/aps/campaigns/release-queue` (line 520)
- `/api/data/gantt` (line 518)
- `/api/data/capacity` (line 523)

## Summary

| Function | Legacy Script | Flask Endpoint | Status |
|----------|---------------|----------------|--------|
| BOM Explosion | ✓ aps_functions.py | ✓ /api/run/bom | ✓ COMPLETE |
| Capacity Map | ✓ aps_functions.py | ✓ /api/aps/capacity/map | ✓ COMPLETE |
| Schedule | ✓ aps_functions.py | ✓ /api/aps/schedule/run | ✓ COMPLETE |
| Scenarios | ✓ aps_functions.py | ✓ /api/aps/scenarios/* | ✓ COMPLETE |
| CTP | ✓ aps_functions.py | ✓ /api/run/ctp | ✓ COMPLETE |
| Clear Outputs | ✓ aps_functions.py | ✓ /api/aps/clear-outputs | ✓ COMPLETE |

## Conclusion

**All Excel macro functions are now fully implemented in both:**
1. **Legacy mode:** Direct execution through aps_functions.py (requires Python Excel bridge)
2. **API mode:** REST endpoints through xaps_application_api.py Flask server

**STATUS:** ✓ ALL FUNCTIONS MIGRATED - NO MISSING ENDPOINTS
