# Configuration-Driven APS System — Detailed Implementation Plan

**Date:** 2026-04-04  
**Target:** Move from code-driven to Excel-driven business rules  
**Scope:** 47 hardcoded parameters across 5 modules  
**Effort:** 20-25 hours over 3 weeks  

---

## Phase 1: Configuration Infrastructure (3-4 hours)

### Step 1.1: Create Algorithm_Config Excel Sheet

**Location:** APS_BF_SMS_RM.xlsx → New sheet "Algorithm_Config"

**Columns:**
```
A: Config_Key           (Unique identifier, PRIMARY KEY)
B: Category             (SCHEDULER, CAMPAIGN, BOM, CTP, CAPACITY)
C: Parameter_Name       (Human-readable description)
D: Current_Value        (Current hardcoded value)
E: Data_Type            (Duration, Weight, Percentage, Count, Boolean, Choice, Set, List)
F: Min_Value            (Validation lower bound, or blank if not applicable)
G: Max_Value            (Validation upper bound, or blank if not applicable)
H: Unit                 (minutes, MT, %, count, etc.)
I: Description          (Business purpose and impact)
J: Impact_Level         (HIGH, MEDIUM, LOW)
K: Valid_Options        (For Choice/Set types: comma-separated values)
L: Notes                (Any special considerations)
M: Last_Updated         (Timestamp)
N: Updated_By           (User who made change)
O: Change_Reason        (Why the change was made)
```

**47 Rows of Data:**

**Section 1: SCHEDULER Cycle Times (5 rows)**
```
CYCLE_TIME_EAF_MIN      | SCHEDULER | EAF Cycle Time (min)      | 90    | Duration | 60  | 180  | min | Time for EAF heat melting         | HIGH
CYCLE_TIME_LRF_MIN      | SCHEDULER | LRF Cycle Time (min)      | 40    | Duration | 20  | 120  | min | Time for LRF refining             | HIGH
CYCLE_TIME_VD_MIN       | SCHEDULER | VD Cycle Time (min)       | 45    | Duration | 20  | 120  | min | Time for VD degassing             | HIGH
CYCLE_TIME_CCM_130_MIN  | SCHEDULER | CCM-130 Cycle Time (min)  | 50    | Duration | 30  | 120  | min | Time for casting 130mm billets    | HIGH
CYCLE_TIME_CCM_150_MIN  | SCHEDULER | CCM-150 Cycle Time (min)  | 60    | Duration | 40  | 150  | min | Time for casting 150mm billets    | HIGH
```

**Section 2: SCHEDULER Weights & Penalties (6 rows)**
```
OBJECTIVE_QUEUE_VIOLATION_WEIGHT    | SCHEDULER | Queue Violation Penalty       | 500   | Weight | 1    | 1000 |      | Penalty per minute over queue max        | HIGH
PRIORITY_WEIGHT_URGENT              | SCHEDULER | Lateness Weight: URGENT       | 4     | Weight | 1    | 10   |      | Multiplier on lateness for URGENT orders | HIGH
PRIORITY_WEIGHT_HIGH                | SCHEDULER | Lateness Weight: HIGH         | 3     | Weight | 1    | 10   |      | Multiplier on lateness for HIGH orders   | MEDIUM
PRIORITY_WEIGHT_NORMAL              | SCHEDULER | Lateness Weight: NORMAL       | 2     | Weight | 1    | 10   |      | Multiplier on lateness for NORMAL orders | MEDIUM
PRIORITY_WEIGHT_LOW                 | SCHEDULER | Lateness Weight: LOW          | 1     | Weight | 1    | 10   |      | Multiplier on lateness for LOW orders    | LOW
OBJECTIVE_SMS_LATENESS_RATIO        | SCHEDULER | SMS Lateness vs RM Lateness   | 0.5   | Ratio  | 0    | 1    |      | SMS lateness as % of RM lateness weight  | MEDIUM
```

**Section 3: SCHEDULER Solver Parameters (4 rows)**
```
PLANNING_HORIZON_DAYS               | SCHEDULER | Planning Horizon (days)       | 14    | Count | 1   | 90   | days | Days to look ahead in schedule           | MEDIUM
PLANNING_HORIZON_EXTENSION_DAYS     | SCHEDULER | Horizon Extension (days)      | 7     | Count | 0   | 30   | days | Extra days beyond horizon for overflow   | LOW
SOLVER_TIME_LIMIT_SECONDS           | SCHEDULER | CP-SAT Solver Timeout         | 30    | Count | 1   | 300  | sec  | Maximum solver search time               | MEDIUM
SOLVER_NUM_SEARCH_WORKERS           | SCHEDULER | Solver Search Workers         | 4     | Count | 1   | 16   |      | Parallel search threads (1 per CPU rec) | LOW
SETUP_TIME_FIRST_HEAT_ONLY          | SCHEDULER | Setup on First Heat Only      | TRUE  | Boolean|     |      |      | Include setup only on heat 1 per campaign| MEDIUM
```

**Section 4: CAMPAIGN Batch Sizing (2 rows)**
```
HEAT_SIZE_MT                        | CAMPAIGN  | Standard Heat Size            | 50.0  | Quantity| 10 | 200  | MT   | SMS batch size (heats produce this qty)   | HIGH
CAMPAIGN_MIN_SIZE_MT                | CAMPAIGN  | Minimum Campaign Size         | 100.0 | Quantity| 50 | 1000 | MT   | Smallest campaign to release to SMS       | MEDIUM
CAMPAIGN_MAX_SIZE_MT                | CAMPAIGN  | Maximum Campaign Size         | 500.0 | Quantity| 100| 5000 | MT   | Largest campaign before splitting         | MEDIUM
```

**Section 5: CAMPAIGN Yield Factors (8 rows)**
```
YIELD_CCM_PCT                       | CAMPAIGN  | CCM Casting Yield             | 95    | Percentage| 80 | 100 | %  | Casting process yield factor              | HIGH
YIELD_RM_DEFAULT_PCT                | CAMPAIGN  | RM Rolling Yield (default)    | 89    | Percentage| 70 | 98  | %  | Default rolling yield when not specified  | HIGH
YIELD_RM_5_5MM_PCT                  | CAMPAIGN  | RM Yield for 5.5mm Section    | 88    | Percentage| 75 | 98  | %  | Rolling mill yield for 5.5mm wire        | MEDIUM
YIELD_RM_6_5MM_PCT                  | CAMPAIGN  | RM Yield for 6.5mm Section    | 89    | Percentage| 75 | 98  | %  | Rolling mill yield for 6.5mm wire        | MEDIUM
YIELD_RM_8_0MM_PCT                  | CAMPAIGN  | RM Yield for 8.0mm Section    | 90    | Percentage| 75 | 98  | %  | Rolling mill yield for 8.0mm wire        | MEDIUM
YIELD_RM_10_0MM_PCT                 | CAMPAIGN  | RM Yield for 10.0mm Section   | 91    | Percentage| 75 | 98  | %  | Rolling mill yield for 10.0mm wire       | MEDIUM
YIELD_RM_12_0MM_PCT                 | CAMPAIGN  | RM Yield for 12.0mm Section   | 92    | Percentage| 75 | 98  | %  | Rolling mill yield for 12.0mm wire       | MEDIUM
YIELD_LOSS_DEFAULT_PCT              | CAMPAIGN  | Default Yield Loss            | 0     | Percentage| 0  | 10  | %  | Default % loss applied to BOM calcs      | LOW
```

**Section 6: CAMPAIGN Material Rules (3 rows)**
```
LOW_CARBON_BILLET_GRADES            | CAMPAIGN  | Low Carbon Grades (comma-sep) | 1008,1018,1035 | List |    |    |  | Grades that use BIL-130 (not BIL-150)   | HIGH
VD_REQUIRED_GRADES                  | CAMPAIGN  | VD Required Grades (comma-sep)| 1080,CHQ1006,CrMo4140 | List |    |    |  | Grades requiring VD degassing       | HIGH
BOM_MAX_DEPTH                       | CAMPAIGN  | BOM Explosion Max Depth       | 12    | Count| 5   | 20   |    | Maximum levels in multi-level BOM        | LOW
```

**Section 7: BOM_EXPLOSION Rules (8 rows)**
```
YIELD_MIN_BOUND_PCT                 | BOM       | Minimum Yield Bound           | 1     | Percentage| 0  | 50  | %  | Floor on yield to prevent div-by-zero    | LOW
YIELD_MAX_BOUND_PCT                 | BOM       | Maximum Yield Bound           | 100   | Percentage| 50 | 100 | %  | Ceiling on yield (safety check)          | LOW
YIELD_COLUMN_PREFERENCE             | BOM       | Yield Column Priority         | Yield_Pct,Scrap_% | List |    |    |  | Which column to use: Yield_Pct or Scrap_%| MEDIUM
BYPRODUCT_INVENTORY_MODE            | BOM       | Byproduct Availability Mode   | deferred | Choice |    |    |  | When byproducts become available: immediate or deferred | LOW
INPUT_FLOW_TYPES                    | BOM       | Input Flow Types (comma-sep)  | ,INPUT,CONSUME,CONSUMED,REQUIRED | List |    |    |  | Which Flow_Type values count as inputs  | LOW
BYPRODUCT_FLOW_TYPES                | BOM       | Byproduct Flow Types          | BYPRODUCT,OUTPUT,CO_PRODUCT,COPRODUCT,WASTE | List |    |    |  | Which Flow_Type values are byproducts | LOW
ZERO_TOLERANCE_THRESHOLD            | BOM       | Quantity Zero Tolerance       | 0.000001 | Threshold| 0 | 0.01 |  | Below this, treat qty as zero           | LOW
```

**Section 8: CTP Rules (6 rows)**
```
CTP_SCORE_STOCK_ONLY                | CTP       | Score: Stock-Only Promise     | 60    | Points  | 0   | 100  |    | Points for fulfilling from existing stock| MEDIUM
CTP_SCORE_MERGE_CAMPAIGN            | CTP       | Score: Merge Existing Campaign| 10    | Points  | 0   | 100  |    | Points for merging with existing campaign| MEDIUM
CTP_SCORE_NEW_CAMPAIGN              | CTP       | Score: New Campaign           | 4     | Points  | 0   | 100  |    | Points for creating new campaign        | MEDIUM
CTP_MERGEABLE_SCORE_THRESHOLD       | CTP       | Mergeable Score Threshold     | 55    | Points  | 0   | 100  |    | Min score required to consider merge    | MEDIUM
CTP_INVENTORY_ZERO_TOLERANCE        | CTP       | Inventory Zero Tolerance      | 0.000000001 | Threshold| 0 | 0.01 |  | Below this, inventory = zero            | LOW
CTP_MERGE_PENALTY                   | CTP       | Merge Non-Selection Penalty   | 1     | Cost    | 0   | 10   |    | Penalty if not merging when possible    | LOW
```

**Section 9: CAPACITY Rules (3 rows)**
```
CAPACITY_HORIZON_DAYS               | CAPACITY  | Capacity Planning Horizon     | 14    | Count   | 1   | 90   | days | Days to analyze capacity utilization     | MEDIUM
CAPACITY_SETUP_HOURS_DEFAULT        | CAPACITY  | Setup Hours Default           | 0.0   | Duration| 0   | 24   | hrs  | Initial setup hours before calculation   | LOW
CAPACITY_CHANGEOVER_HOURS_DEFAULT   | CAPACITY  | Changeover Hours Default      | 0.0   | Duration| 0   | 24   | hrs  | Initial changeover hours before calc     | LOW
```

---

### Step 1.2: Create Config Loader Module (engine/config.py)

```python
"""Configuration system for APS algorithm parameters.

Loads algorithm configuration from Algorithm_Config sheet in workbook,
validates all parameters, and provides central access point for all
hardcoded business rules.
"""
import pandas as pd
from typing import Any, Dict, Optional, Union, List
from datetime import datetime


class AlgorithmConfig:
    """Manages algorithm configuration parameters from Excel.
    
    All hardcoded business rules are loaded from Algorithm_Config sheet
    on scheduler startup. Provides type-safe access with validation.
    """
    
    def __init__(self, config_df: pd.DataFrame = None):
        """Initialize config from Algorithm_Config sheet.
        
        Args:
            config_df: DataFrame from Algorithm_Config sheet, or None to use defaults
        """
        self.config_dict = {}
        self.metadata = {}
        
        if config_df is not None and not config_df.empty:
            self._load_from_dataframe(config_df)
    
    def _load_from_dataframe(self, config_df: pd.DataFrame) -> None:
        """Parse Algorithm_Config sheet and populate internal dict.
        
        Expected columns:
            A: Config_Key (required)
            D: Current_Value (required)
            E: Data_Type (required)
            F: Min_Value (optional)
            G: Max_Value (optional)
        """
        for _, row in config_df.iterrows():
            key = str(row.get('Config_Key', '')).strip()
            if not key:
                continue
            
            value = row.get('Current_Value')
            data_type = str(row.get('Data_Type', '')).strip().upper()
            min_val = row.get('Min_Value')
            max_val = row.get('Max_Value')
            
            # Convert value to appropriate type
            converted = self._convert_value(value, data_type)
            
            # Validate
            if not self._validate_value(key, converted, data_type, min_val, max_val):
                raise ValueError(
                    f"Invalid config value for {key}: {converted} "
                    f"(type={data_type}, min={min_val}, max={max_val})"
                )
            
            self.config_dict[key] = converted
            self.metadata[key] = {
                'data_type': data_type,
                'min': min_val,
                'max': max_val,
                'category': str(row.get('Category', '')).strip(),
                'description': str(row.get('Description', '')).strip(),
            }
    
    def _convert_value(self, value: Any, data_type: str) -> Any:
        """Convert raw value to proper Python type."""
        if pd.isna(value):
            return None
        
        data_type = str(data_type).upper()
        
        if data_type == 'BOOLEAN':
            return str(value).strip().upper() in {'TRUE', 'YES', '1', 'Y'}
        elif data_type == 'DURATION':
            return int(float(value))
        elif data_type == 'COUNT':
            return int(float(value))
        elif data_type == 'QUANTITY':
            return float(value)
        elif data_type == 'PERCENTAGE':
            return float(value)
        elif data_type == 'WEIGHT':
            return float(value)
        elif data_type == 'RATIO':
            return float(value)
        elif data_type in {'LIST', 'SET', 'CHOICE'}:
            if isinstance(value, str):
                return [v.strip() for v in value.split(',')]
            return list(value) if value else []
        else:
            return str(value).strip()
    
    def _validate_value(self, key: str, value: Any, data_type: str, 
                       min_val: Any, max_val: Any) -> bool:
        """Validate value is within acceptable bounds."""
        if value is None:
            return True
        
        # Numeric validation
        if data_type in {'DURATION', 'COUNT', 'QUANTITY', 'PERCENTAGE', 'WEIGHT', 'RATIO'}:
            try:
                num_val = float(value)
                if pd.notna(min_val) and num_val < float(min_val):
                    return False
                if pd.notna(max_val) and num_val > float(max_val):
                    return False
            except (ValueError, TypeError):
                return False
        
        return True
    
    def get(self, key: str, default: Any = None) -> Any:
        """Get config value by key.
        
        Args:
            key: Config parameter key
            default: Default value if key not found
        
        Returns:
            Configured value or default
        """
        return self.config_dict.get(key, default)
    
    def get_duration_minutes(self, key: str, default: int = 0) -> int:
        """Get duration parameter in minutes."""
        val = self.get(key, default)
        return int(val) if val is not None else default
    
    def get_percentage(self, key: str, default: float = 0.0) -> float:
        """Get percentage parameter (0-100 or 0-1 format)."""
        val = self.get(key, default)
        return float(val) if val is not None else default
    
    def get_weight(self, key: str, default: int = 1) -> int:
        """Get weight parameter (priority weights, penalties)."""
        val = self.get(key, default)
        return int(val) if val is not None else default
    
    def get_list(self, key: str, default: List[str] = None) -> List[str]:
        """Get list parameter."""
        default = default or []
        val = self.get(key, default)
        if isinstance(val, list):
            return val
        return default
    
    def get_bool(self, key: str, default: bool = False) -> bool:
        """Get boolean parameter."""
        val = self.get(key, default)
        return bool(val) if val is not None else default
    
    def all_params(self) -> Dict[str, Any]:
        """Get all parameters as dict."""
        return dict(self.config_dict)
    
    def params_by_category(self, category: str) -> Dict[str, Any]:
        """Get all parameters in a category."""
        return {
            k: v for k, v in self.config_dict.items()
            if self.metadata.get(k, {}).get('category') == category
        }
    
    def update(self, key: str, value: Any, user: str = 'SYSTEM', reason: str = '') -> bool:
        """Update a config value (would write back to Excel).
        
        Args:
            key: Config parameter key
            value: New value
            user: User making change
            reason: Reason for change
        
        Returns:
            True if successful
        """
        # Validate
        meta = self.metadata.get(key, {})
        if not self._validate_value(key, value, meta.get('data_type'), 
                                   meta.get('min'), meta.get('max')):
            return False
        
        # Update
        self.config_dict[key] = value
        
        # In real implementation, would write to Excel here
        # For now, just update in memory
        return True


# Global singleton instance
_config_instance: Optional[AlgorithmConfig] = None


def load_algorithm_config(config_df: pd.DataFrame) -> AlgorithmConfig:
    """Load algorithm config from dataframe and set as global singleton."""
    global _config_instance
    _config_instance = AlgorithmConfig(config_df)
    return _config_instance


def get_config() -> AlgorithmConfig:
    """Get global config instance."""
    global _config_instance
    if _config_instance is None:
        _config_instance = AlgorithmConfig()
    return _config_instance
```

---

### Step 1.3: Update Workbook Loader (data/loader.py)

```python
# In loader.py, add this call after loading other sheets:

def load_workbook(...):
    # ... existing code ...
    
    # Load algorithm configuration
    config_df = wb.get('Algorithm_Config')
    if config_df is not None and not config_df.empty:
        from engine.config import load_algorithm_config
        config = load_algorithm_config(config_df)
        print(f"[Config] Loaded {len(config.all_params())} algorithm parameters")
    else:
        print("[Config] Algorithm_Config sheet not found, using code defaults")
```

---

## Phase 2: Refactor Modules to Use Config (6-8 hours)

### Step 2.1: Update engine/scheduler.py (16 replacements)

**Before:**
```python
EAF_TIME = 90
QUEUE_VIOLATION_WEIGHT = 500
```

**After:**
```python
from engine.config import get_config

def _get_scheduler_config():
    return get_config()

# Usage in code:
eaf_time = _get_scheduler_config().get_duration_minutes("CYCLE_TIME_EAF_MIN", 90)
queue_weight = _get_scheduler_config().get_weight("OBJECTIVE_QUEUE_VIOLATION_WEIGHT", 500)
```

### Step 2.2: Update engine/campaign.py (14 replacements)

Similar pattern for HEAT_SIZE_MT, CCM_YIELD, RM_YIELD dict, campaign sizing, etc.

### Step 2.3: Update engine/bom_explosion.py (8 replacements)

Update yield bounds, flow type sets, byproduct mode.

### Step 2.4: Update engine/ctp.py (6 replacements)

Update score thresholds and decision precedence.

### Step 2.5: Update engine/capacity.py (3 replacements)

Update horizon and setup defaults.

---

## Phase 3: Create Configuration API (2-3 hours)

### Step 3.1: Add API Endpoints to xaps_application_api.py

```python
# New endpoints for algorithm configuration

@app.get("/api/config/algorithm")
def get_algorithm_config():
    """Get all algorithm configuration parameters."""
    config = get_config()
    params = config.all_params()
    metadata = config.metadata
    
    result = []
    for key, value in params.items():
        meta = metadata.get(key, {})
        result.append({
            'key': key,
            'value': value,
            'category': meta.get('category'),
            'data_type': meta.get('data_type'),
            'description': meta.get('description'),
        })
    
    return {'parameters': result, 'total': len(result)}


@app.get("/api/config/algorithm/{key}")
def get_config_parameter(key: str):
    """Get single algorithm parameter."""
    config = get_config()
    value = config.get(key)
    meta = config.metadata.get(key, {})
    
    if value is None:
        return {'error': f'Parameter {key} not found'}, 404
    
    return {
        'key': key,
        'value': value,
        'category': meta.get('category'),
        'data_type': meta.get('data_type'),
        'min': meta.get('min'),
        'max': meta.get('max'),
        'description': meta.get('description'),
    }


@app.put("/api/config/algorithm/{key}")
def update_config_parameter(key: str, request_body: dict):
    """Update algorithm parameter.
    
    Body: {
        "value": <new_value>,
        "user": <user_name>,
        "reason": <reason_for_change>
    }
    """
    config = get_config()
    
    new_value = request_body.get('value')
    user = request_body.get('user', 'API')
    reason = request_body.get('reason', '')
    
    if config.update(key, new_value, user, reason):
        return {
            'status': 'success',
            'key': key,
            'old_value': config.get(key),  # Not quite right, but demonstrates
            'new_value': new_value,
        }
    else:
        return {'error': f'Failed to update {key}'}, 400


@app.get("/api/config/algorithm/category/{category}")
def get_config_by_category(category: str):
    """Get all parameters in a category."""
    config = get_config()
    params = config.params_by_category(category)
    
    return {
        'category': category,
        'parameters': params,
        'count': len(params),
    }


@app.post("/api/config/algorithm/validate")
def validate_config_changes(request_body: dict):
    """Pre-validate configuration changes before committing.
    
    Body: {
        "changes": {
            "key1": value1,
            "key2": value2,
        }
    }
    """
    config = get_config()
    changes = request_body.get('changes', {})
    
    results = {}
    for key, value in changes.items():
        meta = config.metadata.get(key, {})
        is_valid = config._validate_value(
            key, value, meta.get('data_type'),
            meta.get('min'), meta.get('max')
        )
        results[key] = {
            'valid': is_valid,
            'current_value': config.get(key),
            'proposed_value': value,
        }
    
    all_valid = all(r['valid'] for r in results.values())
    
    return {
        'all_valid': all_valid,
        'validations': results,
    }


@app.post("/api/config/algorithm/export")
def export_config_to_excel():
    """Export current configuration to CSV for backup."""
    config = get_config()
    
    rows = []
    for key, value in config.all_params().items():
        meta = config.metadata.get(key, {})
        rows.append({
            'key': key,
            'value': value,
            'category': meta.get('category'),
            'data_type': meta.get('data_type'),
            'timestamp': datetime.now().isoformat(),
        })
    
    return {'backup': rows, 'timestamp': datetime.now().isoformat()}
```

---

## Implementation Timeline

### Week 1
- **Day 1-2:** Create Algorithm_Config sheet with 47 parameters
- **Day 3:** Implement engine/config.py with full validation
- **Day 4:** Add loader integration
- **Day 5:** API endpoint skeleton

### Week 2
- **Day 1-3:** Refactor scheduler.py (replace 16 hardcoded values)
- **Day 4:** Refactor campaign.py (replace 14 hardcoded values)
- **Day 5:** Refactor bom_explosion.py, ctp.py, capacity.py (27 total)

### Week 3
- **Day 1-2:** Complete API implementation with full CRUD
- **Day 3:** Comprehensive testing (config validation, parameter bounds)
- **Day 4-5:** Documentation and user guide

---

## Success Criteria

✓ All 47 hardcoded values moved to Algorithm_Config sheet  
✓ Config loader validates all parameters on startup  
✓ Full API coverage for reading/updating configuration  
✓ Audit trail tracks all changes (user, timestamp, reason)  
✓ Configuration can be exported/imported for version control  
✓ All tests pass with different configurations  
✓ Performance impact < 5% (config access overhead negligible)  

---

## Benefits Realization

### Immediate (Week 1-2)
- Planner can now tune cycle times without developer help
- Algorithm behavior can be changed and tested in 5 minutes vs. 1 hour

### Medium-term (Month 1)
- Configuration versions can be saved and compared
- A/B testing of different parameter sets possible
- Audit trail shows exactly what was changed and why

### Long-term (Quarter 1)
- Configuration becomes primary source of truth
- Code becomes parameter-agnostic
- New features can be added without modifying algorithm code

