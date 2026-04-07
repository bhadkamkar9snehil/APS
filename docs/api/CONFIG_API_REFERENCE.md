# Configuration API Reference

**Complete reference for all configuration management endpoints**

Base URL: `http://localhost:5000/api/config`

---

## Endpoints

### GET /algorithm
Retrieve all algorithm configuration parameters

**Request:**
```bash
curl http://localhost:5000/api/config/algorithm
```

**Response (200 OK):**
```json
{
  "status": "ok",
  "count": 47,
  "parameters": {
    "HEAT_SIZE_MT": {
      "category": "Campaign",
      "description": "Standard EAF heat size",
      "value": 50,
      "type": "Quantity",
      "unit": "MT",
      "min": 10,
      "max": 200
    },
    "OBJECTIVE_QUEUE_VIOLATION_WEIGHT": {
      "category": "Scheduler",
      "description": "Penalty for violating queue time rules",
      "value": 500,
      "type": "Points",
      "unit": "pts",
      "min": 1,
      "max": 10000
    },
    ...
  }
}
```

---

### GET /algorithm/{key}
Retrieve single configuration parameter

**Request:**
```bash
curl http://localhost:5000/api/config/algorithm/HEAT_SIZE_MT
```

**Response (200 OK):**
```json
{
  "status": "ok",
  "parameter": "HEAT_SIZE_MT",
  "category": "Campaign",
  "description": "Standard EAF heat size",
  "value": 50,
  "type": "Quantity",
  "unit": "MT",
  "min": 10,
  "max": 200,
  "default": 50,
  "last_modified": "2026-04-04T10:00:00Z",
  "modified_by": "SYSTEM"
}
```

**Response (404 Not Found):**
```json
{
  "status": "error",
  "message": "Parameter UNKNOWN_PARAM not found"
}
```

---

### GET /algorithm/category/{category}
Retrieve all parameters in a category

**Request:**
```bash
curl http://localhost:5000/api/config/algorithm/category/Scheduler
```

**Valid Categories:**
- `Scheduler` - 16 parameters (cycle times, weights, solver config)
- `Campaign` - 14 parameters (batch sizing, yields, material rules)
- `BOM` - 7 parameters (flow types, yields, tolerance)
- `CTP` - 6 parameters (promise scoring thresholds)
- `Capacity` - 3 parameters (planning horizon, setup/changeover)

**Response (200 OK):**
```json
{
  "status": "ok",
  "category": "Scheduler",
  "count": 16,
  "parameters": {
    "CYCLE_TIME_EAF_MINUTES": {...},
    "CYCLE_TIME_LRF_MINUTES": {...},
    ...
  }
}
```

---

### PUT /algorithm/{key}
Update configuration parameter value

**Request:**
```bash
curl -X PUT http://localhost:5000/api/config/algorithm/HEAT_SIZE_MT \
  -H "Content-Type: application/json" \
  -d '{
    "value": 60,
    "note": "Increased for higher throughput testing"
  }'
```

**Response (200 OK):**
```json
{
  "status": "ok",
  "parameter": "HEAT_SIZE_MT",
  "old_value": 50,
  "new_value": 60,
  "unit": "MT",
  "valid_range": "10-200",
  "note": "Increased for higher throughput testing",
  "updated_at": "2026-04-04T15:30:00Z",
  "updated_by": "api",
  "requires_reload": true
}
```

**Response (400 Bad Request):**
```json
{
  "status": "error",
  "parameter": "HEAT_SIZE_MT",
  "message": "Value 500 out of valid range 10-200",
  "valid_range": "10-200",
  "suggestion": "Maximum allowed: 200 MT"
}
```

**Parameters:**
- `value` (required): New value for parameter
- `note` (optional): Explanation for change
- `require_validation` (optional): Set to true to validate without updating

---

### POST /algorithm/validate
Validate configuration changes without applying them

**Request:**
```bash
curl -X POST http://localhost:5000/api/config/algorithm/validate \
  -H "Content-Type: application/json" \
  -d '{
    "changes": {
      "HEAT_SIZE_MT": 100,
      "CYCLE_TIME_EAF_MINUTES": 120,
      "PRIORITY_WEIGHT_URGENT": 5
    }
  }'
```

**Response (200 OK):**
```json
{
  "status": "ok",
  "valid": true,
  "changes": 3,
  "validation_results": {
    "HEAT_SIZE_MT": {
      "valid": true,
      "message": "Value 100 is within range 10-200",
      "affects": ["campaign_batching", "capacity_planning"]
    },
    "CYCLE_TIME_EAF_MINUTES": {
      "valid": true,
      "message": "Value 120 is within range 30-180",
      "affects": ["schedule_makespan", "queue_times"]
    },
    "PRIORITY_WEIGHT_URGENT": {
      "valid": true,
      "message": "Value 5 is within range 1-10",
      "affects": ["scheduling_priority"]
    }
  }
}
```

**Response (400 Bad Request):**
```json
{
  "status": "error",
  "valid": false,
  "changes": 2,
  "validation_results": {
    "HEAT_SIZE_MT": {
      "valid": true,
      "message": "Value 100 is within range 10-200"
    },
    "INVALID_PARAM": {
      "valid": false,
      "message": "Parameter INVALID_PARAM not found",
      "suggestion": "Did you mean HEAT_SIZE_MT?"
    }
  }
}
```

---

### POST /algorithm/export
Export current configuration to CSV

**Request:**
```bash
curl -X POST http://localhost:5000/api/config/algorithm/export \
  -H "Content-Type: application/json" \
  -d '{
    "format": "csv",
    "include_metadata": true
  }'
```

**Response (200 OK):**
```
Parameter Name,Category,Current Value,Unit,Min,Max,Description,Type
HEAT_SIZE_MT,Campaign,50,MT,10,200,Standard EAF heat size,Quantity
OBJECTIVE_QUEUE_VIOLATION_WEIGHT,Scheduler,500,pts,1,10000,Penalty for queue violations,Points
...
```

**Request Options:**
- `format`: "csv" (default), "json", "yaml"
- `include_metadata`: true/false (include min/max/type info)
- `category_filter`: Restrict to specific category (e.g., "Scheduler")

---

## Usage Examples

### Example 1: Tune System for High Throughput

```bash
# Validate changes
curl -X POST http://localhost:5000/api/config/algorithm/validate \
  -H "Content-Type: application/json" \
  -d '{
    "changes": {
      "HEAT_SIZE_MT": 80,
      "CAMPAIGN_MAX_QUANTITY_MT": 350,
      "SOLVER_RELATIVE_GAP_TOLERANCE": 0.1
    }
  }'

# Apply changes (if validation passes)
curl -X PUT http://localhost:5000/api/config/algorithm/HEAT_SIZE_MT \
  -H "Content-Type: application/json" \
  -d '{"value": 80, "note": "High throughput tuning"}'

curl -X PUT http://localhost:5000/api/config/algorithm/CAMPAIGN_MAX_QUANTITY_MT \
  -H "Content-Type: application/json" \
  -d '{"value": 350, "note": "High throughput tuning"}'

# ... and so on
```

### Example 2: Strict Queue Compliance

```bash
# Increase queue violation penalty
curl -X PUT http://localhost:5000/api/config/algorithm/OBJECTIVE_QUEUE_VIOLATION_WEIGHT \
  -H "Content-Type: application/json" \
  -d '{"value": 1000, "note": "Strict JIT compliance requirement"}'

# Increase priority spread for urgent orders
curl -X PUT http://localhost:5000/api/config/algorithm/PRIORITY_WEIGHT_URGENT \
  -H "Content-Type: application/json" \
  -d '{"value": 5, "note": "Increase urgent order priority"}'

curl -X PUT http://localhost:5000/api/config/algorithm/PRIORITY_WEIGHT_NORMAL \
  -H "Content-Type: application/json" \
  -d '{"value": 1, "note": "Reduce normal order priority"}'
```

### Example 3: Conservative Material Planning

```bash
# Increase safety margins
curl -X PUT http://localhost:5000/api/config/algorithm/YIELD_RM_DEFAULT_PCT \
  -H "Content-Type: application/json" \
  -d '{"value": 0.85, "note": "Conservative yield for shortage prevention"}'

# Reduce batch sizes for flexibility
curl -X PUT http://localhost:5000/api/config/algorithm/CAMPAIGN_MAX_QUANTITY_MT \
  -H "Content-Type: application/json" \
  -d '{"value": 150, "note": "Reduce batch sizes for material flexibility"}'
```

---

## Error Handling

### Common Errors

| Code | Meaning | Example |
|---|---|---|
| 200 | Success | Parameter updated |
| 400 | Bad Request | Invalid value or parameter |
| 404 | Not Found | Parameter doesn't exist |
| 422 | Unprocessable | Validation failed |
| 500 | Server Error | System error |

### Error Response Format

```json
{
  "status": "error",
  "code": 400,
  "message": "User-friendly error message",
  "details": {
    "parameter": "HEAT_SIZE_MT",
    "attempted_value": 500,
    "valid_range": "10-200",
    "reason": "Value exceeds maximum"
  },
  "suggestion": "Decrease to 200 or check if you meant a different parameter"
}
```

---

## Config Reload Behavior

**When does the system use new config values?**

| Component | Reload Timing |
|---|---|
| New API calls | Immediately (singleton reads config each time) |
| Running schedules | Next POST /api/run/aps call |
| Cached results | Only if explicitly recomputed |

**Best Practice:** Always reload by running `POST /api/run/aps` after config changes to see effects.

---

## Authentication

**Current State:** No authentication required (development)

**For Production:** Implement:
- API key validation
- Role-based access (admin can modify, read-only for operators)
- Audit trail (who changed what, when)
- Change notification (alert when config modified)

---

## Rate Limiting

**Current State:** No rate limiting (development)

**For Production:** Consider:
- 100 requests/minute per API key
- Burst allowance for batch operations
- Queuing for large exports

---

## Version History

Config changes are immutable once saved. Excel maintains audit trail:
- Created_By, Created_Date (when parameter added)
- Last_Modified_By, Last_Modified_Date (when value changed)

**Query history:**
```bash
# List all parameters modified in last 7 days
curl "http://localhost:5000/api/config/algorithm?modified_since=7d"

# Export configuration as of specific date
curl -X POST http://localhost:5000/api/config/algorithm/export \
  -H "Content-Type: application/json" \
  -d '{"as_of_date": "2026-03-28"}'
```

---

**Last Updated:** 2026-04-04 | See [PARAMETER_TUNING_GUIDE.md](../reference/PARAMETER_TUNING_GUIDE.md) for parameter details
