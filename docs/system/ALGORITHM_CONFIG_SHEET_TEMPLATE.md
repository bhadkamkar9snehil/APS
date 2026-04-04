# Algorithm_Config Sheet — Complete Data Template

**Purpose:** Central configuration repository for all 47 APS algorithm parameters  
**Location:** APS_BF_SMS_RM.xlsx → Sheet: "Algorithm_Config"  
**Columns:** A-O (15 columns total)  
**Rows:** 1 header + 47 parameters = 48 rows total  

---

## Header Row (Row 1)

```
A                    B           C               D               E           F           G           H           I               J           K                   L           M               N               O
Config_Key           Category    Parameter_Name  Current_Value   Data_Type   Min_Value   Max_Value   Unit        Description     Impact      Valid_Options      Notes       Last_Updated    Updated_By      Change_Reason
```

---

## Data Rows (47 Parameters)

### SCHEDULER — Cycle Times (Rows 2-6)

```
CYCLE_TIME_EAF_MIN                  SCHEDULER   EAF Cycle Time (min)                90      Duration    60          180         min     Time for EAF heat melting                                   HIGH                                                    2026-04-04      SYSTEM          Initial configuration
CYCLE_TIME_LRF_MIN                  SCHEDULER   LRF Cycle Time (min)                40      Duration    20          120         min     Time for LRF refining (heating)                            HIGH                                                    2026-04-04      SYSTEM          Initial configuration
CYCLE_TIME_VD_MIN                   SCHEDULER   VD Cycle Time (min)                 45      Duration    20          120         min     Time for VD vacuum degassing                               HIGH                                                    2026-04-04      SYSTEM          Initial configuration
CYCLE_TIME_CCM_130_MIN              SCHEDULER   CCM-130 Cycle Time (min)            50      Duration    30          120         min     Time to cast 130mm billets on CCM                         HIGH                                                    2026-04-04      SYSTEM          Initial configuration
CYCLE_TIME_CCM_150_MIN              SCHEDULER   CCM-150 Cycle Time (min)            60      Duration    40          150         min     Time to cast 150mm billets on CCM                         HIGH                                                    2026-04-04      SYSTEM          Initial configuration
```

### SCHEDULER — Objective Weights (Rows 7-12)

```
OBJECTIVE_QUEUE_VIOLATION_WEIGHT    SCHEDULER   Queue Violation Penalty             500     Weight      1           1000        points  Penalty per minute over queue max; tuning impacts schedule density  HIGH        1,100,250,500       Used in CP-SAT objective function  2026-04-04  SYSTEM  Initial configuration
PRIORITY_WEIGHT_URGENT              SCHEDULER   Lateness Weight: URGENT             4       Weight      1           10          mult    Multiplier on lateness for URGENT orders (1=PRI, 4=default) HIGH                                                    2026-04-04      SYSTEM          Initial configuration
PRIORITY_WEIGHT_HIGH                SCHEDULER   Lateness Weight: HIGH               3       Weight      1           10          mult    Multiplier on lateness for HIGH orders                    MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
PRIORITY_WEIGHT_NORMAL              SCHEDULER   Lateness Weight: NORMAL             2       Weight      1           10          mult    Multiplier on lateness for NORMAL orders                  MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
PRIORITY_WEIGHT_LOW                 SCHEDULER   Lateness Weight: LOW                1       Weight      1           10          mult    Multiplier on lateness for LOW orders (avoid if possible) LOW                                                     2026-04-04      SYSTEM          Initial configuration
OBJECTIVE_SMS_LATENESS_RATIO        SCHEDULER   SMS Lateness vs RM Lateness         0.5     Ratio       0           1           ratio   SMS lateness as % of RM lateness weight; 0.5=SMS worth 50%  MEDIUM  0.25,0.5,0.75,1.0  Tuning affects SMS vs RM priority balance  2026-04-04  SYSTEM  Initial configuration; Fixed in v1.1 with weight multiplier
```

### SCHEDULER — Solver Parameters (Rows 13-17)

```
PLANNING_HORIZON_DAYS               SCHEDULER   Planning Horizon (days)             14      Count       1           90          days    Days to look ahead in schedule; larger = slower solve   MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
PLANNING_HORIZON_EXTENSION_DAYS     SCHEDULER   Horizon Extension (days)            7       Count       0           30          days    Extra days beyond horizon for overflow handling          LOW                                                     2026-04-04      SYSTEM          Initial configuration
SOLVER_TIME_LIMIT_SECONDS           SCHEDULER   CP-SAT Solver Timeout               30      Count       1           300         sec     Maximum time solver searches; higher = better but slower; typical 30-60 sec  MEDIUM  5,10,20,30,60,120   Tune based on problem size and performance needs  2026-04-04  SYSTEM  Initial configuration
SOLVER_NUM_SEARCH_WORKERS           SCHEDULER   Solver Search Workers               4       Count       1           16          count   Parallel search threads (recommend 1 per CPU core)       LOW                                                     2026-04-04      SYSTEM          Initial configuration
SETUP_TIME_FIRST_HEAT_ONLY          SCHEDULER   Setup on First Heat Only            TRUE    Boolean                         flag    Include setup time only on first heat of each campaign    MEDIUM  TRUE,FALSE          If FALSE, setup added to every heat (impacts duration)  2026-04-04  SYSTEM  Initial configuration
```

### CAMPAIGN — Batch Sizing (Rows 18-20)

```
HEAT_SIZE_MT                        CAMPAIGN    Standard Heat Size                  50.0    Quantity    10          200         MT      Standard SMS batch size (one heat produces this quantity)  HIGH    25,30,40,50,60,75,100  Controls granularity of production; impacts WIP levels  2026-04-04  SYSTEM  Initial configuration
CAMPAIGN_MIN_SIZE_MT                CAMPAIGN    Minimum Campaign Size               100.0   Quantity    50          1000        MT      Smallest campaign released to SMS (must be >= HEAT_SIZE)  MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration; auto-split if below
CAMPAIGN_MAX_SIZE_MT                CAMPAIGN    Maximum Campaign Size               500.0   Quantity    100         5000        MT      Largest campaign before auto-split (split into smaller heats)  MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration; splits to 2-5 heats typically
```

### CAMPAIGN — Yield Factors (Rows 21-28)

```
YIELD_CCM_PCT                       CAMPAIGN    CCM Casting Yield                   95      Percentage  80          100         %       Casting process yield factor; loss = scrap in ingot mold  HIGH    85,90,95,98         Updated annually after process audits  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_DEFAULT_PCT                CAMPAIGN    RM Rolling Yield (default)          89      Percentage  70          98          %       Default rolling yield when section-specific not available  HIGH    85,87,89,91,93      Fallback when YIELD_RM_*MM not specified  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_5_5MM_PCT                  CAMPAIGN    RM Yield for 5.5mm Section          88      Percentage  75          98          %       Rolling mill yield for 5.5mm wire coil                   MEDIUM  86,87,88,89,90      Fine wire; higher scrap due to breakage  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_6_5MM_PCT                  CAMPAIGN    RM Yield for 6.5mm Section          89      Percentage  75          98          %       Rolling mill yield for 6.5mm wire coil                   MEDIUM  87,88,89,90,91      Standard size; benchmark yield  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_8_0MM_PCT                  CAMPAIGN    RM Yield for 8.0mm Section          90      Percentage  75          98          %       Rolling mill yield for 8.0mm wire coil                   MEDIUM  88,89,90,91,92      Medium coil; lower scrap  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_10_0MM_PCT                 CAMPAIGN    RM Yield for 10.0mm Section         91      Percentage  75          98          %       Rolling mill yield for 10.0mm wire coil                  MEDIUM  89,90,91,92,93      Thick coil; lower scrap  2026-04-04  SYSTEM  Initial configuration
YIELD_RM_12_0MM_PCT                 CAMPAIGN    RM Yield for 12.0mm Section         92      Percentage  75          98          %       Rolling mill yield for 12.0mm wire coil                  MEDIUM  90,91,92,93,94      Thickest standard; best yield  2026-04-04  SYSTEM  Initial configuration
YIELD_LOSS_DEFAULT_PCT              CAMPAIGN    Default Yield Loss                  0       Percentage  0           10          %       Default % loss applied to BOM calcs if not specified      LOW                                                     2026-04-04      SYSTEM          Initial configuration
```

### CAMPAIGN — Material Rules (Rows 29-31)

```
LOW_CARBON_BILLET_GRADES            CAMPAIGN    Low Carbon Grades (comma-sep)       1008,1018,1035  List                    list    Grades that use BIL-130 (not BIL-150); affects routing    HIGH    1008,1018,1035      Determines billet family (130 vs 150mm)  2026-04-04  SYSTEM  Initial configuration
VD_REQUIRED_GRADES                  CAMPAIGN    VD Required Grades (comma-sep)      1080,CHQ1006,CrMo4140  List                    list    Grades requiring VD degassing; adds 45-60 min to SMS      HIGH    1080,CHQ1006,CrMo4140  Specialty grades needing vacuum treatment  2026-04-04  SYSTEM  Initial configuration
BOM_MAX_DEPTH                       CAMPAIGN    BOM Explosion Max Depth             12      Count       5           20          levels  Maximum nesting levels in multi-level BOM explosion      LOW                                                     2026-04-04      SYSTEM          Initial configuration
```

### BOM_EXPLOSION — Rules (Rows 32-39)

```
YIELD_MIN_BOUND_PCT                 BOM         Minimum Yield Bound                 1       Percentage  0           50          %       Floor on yield calculation to prevent division by zero   LOW                                                     2026-04-04      SYSTEM          Initial configuration
YIELD_MAX_BOUND_PCT                 BOM         Maximum Yield Bound                 100     Percentage  50          100         %       Ceiling on yield (safety check to catch data errors)     LOW                                                     2026-04-04      SYSTEM          Initial configuration
YIELD_COLUMN_PREFERENCE             BOM         Yield Column Priority               Yield_Pct,Scrap_%  List                    order   Which column to prefer: Yield_Pct or Scrap_%; left=higher priority  MEDIUM  Yield_Pct,Scrap_%   Order matters; first in list wins  2026-04-04  SYSTEM  Initial configuration
BYPRODUCT_INVENTORY_MODE            BOM         Byproduct Availability Mode         deferred    Choice                  mode    When byproducts become available: immediate or deferred   LOW     immediate,deferred  Deferred = after production completion; immediate = now  2026-04-04  SYSTEM  Initial configuration
INPUT_FLOW_TYPES                    BOM         Input Flow Types (comma-sep)        ,INPUT,CONSUME,CONSUMED,REQUIRED  List                    list    Which Flow_Type values count as inputs (consumption)    LOW                                                     2026-04-04      SYSTEM          Initial configuration
BYPRODUCT_FLOW_TYPES                BOM         Byproduct Flow Types (comma-sep)    BYPRODUCT,OUTPUT,CO_PRODUCT,COPRODUCT,WASTE  List                    list    Which Flow_Type values count as byproducts (outputs)    LOW                                                     2026-04-04      SYSTEM          Initial configuration
ZERO_TOLERANCE_THRESHOLD            BOM         Quantity Zero Tolerance             0.000001    Threshold   0           0.01        MT      Below this qty threshold, treat quantity as zero          LOW                                                     2026-04-04      SYSTEM          Initial configuration
```

### CTP — Rules (Rows 40-45)

```
CTP_SCORE_STOCK_ONLY                CTP         Score: Stock-Only Promise           60      Points      0           100         pts     Points for fulfilling entire order from existing stock   MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
CTP_SCORE_MERGE_CAMPAIGN            CTP         Score: Merge Existing Campaign      10      Points      0           100         pts     Points for merging new order with existing campaign       MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
CTP_SCORE_NEW_CAMPAIGN              CTP         Score: New Campaign                 4       Points      0           100         pts     Points for creating entirely new campaign                MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
CTP_MERGEABLE_SCORE_THRESHOLD       CTP         Mergeable Score Threshold           55      Points      0           100         pts     Min score required to consider merge viable vs new campaign  MEDIUM  40,50,55,60,70    Lowering = favor merges; raising = favor new campaigns  2026-04-04  SYSTEM  Initial configuration
CTP_INVENTORY_ZERO_TOLERANCE        CTP         Inventory Zero Tolerance           0.000000001  Threshold   0           0.01        MT      Below this inventory qty, treat as zero                   LOW                                                     2026-04-04      SYSTEM          Initial configuration
CTP_MERGE_PENALTY                   CTP         Merge Non-Selection Penalty         1       Cost        0           10          cost    Penalty applied if merge option is viable but not selected  LOW                                                     2026-04-04      SYSTEM          Initial configuration
```

### CAPACITY — Rules (Rows 46-48)

```
CAPACITY_HORIZON_DAYS               CAPACITY    Capacity Planning Horizon           14      Count       1           90          days    Days to analyze capacity utilization (same as PLANNING)   MEDIUM                                                  2026-04-04      SYSTEM          Initial configuration
CAPACITY_SETUP_HOURS_DEFAULT        CAPACITY    Setup Hours Default                 0.0     Duration    0           24          hrs     Initial setup hours before calculation (placeholder)      LOW                                                     2026-04-04      SYSTEM          Initial configuration
CAPACITY_CHANGEOVER_HOURS_DEFAULT   CAPACITY    Changeover Hours Default            0.0     Duration    0           24          hrs     Initial changeover hours before calculation (placeholder)  LOW                                                     2026-04-04      SYSTEM          Initial configuration
```

---

## Excel Sheet Validation Rules

### Column Validations:

**Column F (Min_Value):** Number or blank  
**Column G (Max_Value):** Number or blank  
**Column E (Data_Type):** List validation = [Duration, Weight, Percentage, Count, Quantity, Boolean, Choice, List, Set, Threshold, Ratio, Cost, Points]  
**Column K (Valid_Options):** Text (comma-separated for multi-select)  

### Data Validations:

- Min_Value < Max_Value (if both populated)
- Config_Key is unique across all rows
- Current_Value must pass validation against Min/Max/Type
- Data_Type must be from allowed list

---

## How to Modify Parameters

### Safe Change Process:

1. **Document:** Update Column O (Change_Reason) with why and impact expected
2. **Update:** Change value in Column D (Current_Value)
3. **Audit:** Note Column M (Last_Updated) and Column N (Updated_By)
4. **Validate:** Run /api/config/algorithm/validate before reloading scheduler
5. **Test:** Run scheduler with new config on small test set
6. **Deploy:** Reload scheduler with new Algorithm_Config to use changes

---

## Example Configuration Scenarios

### Scenario 1: Speed Up Scheduler (High-Speed Production Day)

```
Original                        Change To                       Reason
SOLVER_TIME_LIMIT_SECONDS   30  →  10                          Need result in 10 sec (deadline)
PLANNING_HORIZON_DAYS       14  →  7                           Only 1-week priority orders
PRIORITY_WEIGHT_URGENT      4   →  6                           Make urgent even more urgent
```

### Scenario 2: Improve Schedule Quality (Off-Peak Planning)

```
Original                        Change To                       Reason
SOLVER_TIME_LIMIT_SECONDS   30  →  120                         Can afford 2 min for better solution
PLANNING_HORIZON_DAYS       14  →  30                          Plan full month ahead
OBJECTIVE_SMS_LATENESS_RATIO 0.5 →  1.0                       SMS lateness as important as RM
```

### Scenario 3: Reduce Lead Times (New Product Ramp)

```
Original                        Change To                       Reason
CAMPAIGN_MIN_SIZE_MT       100  →  50                          Smaller batches allowed
CAMPAIGN_MAX_SIZE_MT       500  →  300                         Tighter control on batch size
YIELD_RM_DEFAULT_PCT        89  →  92                          New process has better yield
```

---

## Configuration Change History Log

**File:** Algorithm_Config_Changes.csv (exported after each change)

```
Timestamp           User            Key                             Old_Value   New_Value   Reason
2026-04-04 08:00   SYSTEM          CYCLE_TIME_EAF_MIN              90          90          Initial configuration
2026-04-05 10:30   planner@apm.com SOLVER_TIME_LIMIT_SECONDS       30          60          High-complexity schedule; need better solution
2026-04-05 14:15   planner@apm.com CAMPAIGN_MIN_SIZE_MT            100         75          SAE1008 demand spikes; allow smaller batches
```

---

## API Usage Examples

### Get All Configuration

```bash
curl http://localhost:5000/api/config/algorithm
```

Response:
```json
{
  "parameters": [
    {
      "key": "CYCLE_TIME_EAF_MIN",
      "value": 90,
      "category": "SCHEDULER",
      "data_type": "Duration",
      "description": "Time for EAF heat melting"
    },
    ...
  ],
  "total": 47
}
```

### Update Single Parameter

```bash
curl -X PUT http://localhost:5000/api/config/algorithm/SOLVER_TIME_LIMIT_SECONDS \
  -H "Content-Type: application/json" \
  -d '{
    "value": 60,
    "user": "planner@apm.com",
    "reason": "High-complexity schedule needs more solver time"
  }'
```

Response:
```json
{
  "status": "success",
  "key": "SOLVER_TIME_LIMIT_SECONDS",
  "old_value": 30,
  "new_value": 60
}
```

### Validate Changes Before Committing

```bash
curl -X POST http://localhost:5000/api/config/algorithm/validate \
  -H "Content-Type: application/json" \
  -d '{
    "changes": {
      "SOLVER_TIME_LIMIT_SECONDS": 60,
      "CAMPAIGN_MIN_SIZE_MT": 75
    }
  }'
```

Response:
```json
{
  "all_valid": true,
  "validations": {
    "SOLVER_TIME_LIMIT_SECONDS": {
      "valid": true,
      "current_value": 30,
      "proposed_value": 60
    },
    "CAMPAIGN_MIN_SIZE_MT": {
      "valid": true,
      "current_value": 100,
      "proposed_value": 75
    }
  }
}
```

---

**Status:** Template ready for Excel sheet creation and implementation.

