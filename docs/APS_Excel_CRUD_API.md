# APS Excel CRUD API

This document defines the Excel-backed API layer added for the APS workbook.

## Purpose

The objective is to let the existing single-page APS UI interact directly with workbook-backed data for read and write operations without changing the established endpoint style.

## New files

- `api_excel_crud.py`
- `engine/excel_store.py`
- `engine/workbook_schema.py`

## Workbook path

The API reads the workbook from the `WORKBOOK_PATH` environment variable.

Example:

```powershell
$env:WORKBOOK_PATH = ".\APS_BF_SMS_RM.xlsx"
python api_excel_crud.py
```

## Generic CRUD endpoints

### List rows
- `GET /api/sheets/{sheet_name}`

Query params:
- `search`
- `sort_by`
- `sort_dir=asc|desc`
- `limit`
- `offset`

### Get single row
- `GET /api/sheets/{sheet_name}/{key_value}`

### Create row
- `POST /api/sheets/{sheet_name}`

Body:
```json
{
  "data": {
    "SO_ID": "SO-999",
    "Customer": "Example",
    "SKU_ID": "FG-WR-SAE1008-55"
  }
}
```

### Replace row
- `PUT /api/sheets/{sheet_name}/{key_value}`

### Patch row
- `PATCH /api/sheets/{sheet_name}/{key_value}`

### Delete row
- `DELETE /api/sheets/{sheet_name}/{key_value}`

### Bulk replace sheet data
- `PUT /api/sheets/{sheet_name}/bulk/replace`

Body:
```json
{
  "items": [
    {"Key": "Planning_Horizon_Days", "Value": 14, "Description": "Default horizon"}
  ]
}
```

## SPA compatibility endpoints

These preserve the existing UI contract style:

- `GET /api/health`
- `GET /api/meta/sheets`
- `GET /api/meta/workbook-snapshot`
- `GET /api/data/orders`
- `GET /api/data/skus`
- `GET /api/data/gantt`
- `GET /api/data/config`
- `GET /api/data/campaigns`
- `GET /api/data/capacity`
- `GET /api/data/material`
- `POST /api/orders/assign`
- `POST /api/run/schedule`
- `POST /api/run/ctp`

## Workbook to API mapping

| API name | Sheet | Key |
|---|---|---|
| config | Config | Key |
| sku-master | SKU_Master | SKU_ID |
| bom | BOM | BOM_ID |
| inventory | Inventory | SKU_ID |
| sales-orders | Sales_Orders | SO_ID |
| resource-master | Resource_Master | Resource_ID |
| routing | Routing | SKU_ID |
| campaign-config | Campaign_Config | Grade |
| changeover-matrix | Changeover_Matrix | From \\ To |
| queue-times | Queue_Times | From_Operation |
| scenarios | Scenarios | Parameter |
| ctp-request | CTP_Request | Request_ID |
| bom-output | BOM_Output | SKU_ID |
| capacity-map | Capacity_Map | Resource_ID |
| schedule-output | Schedule_Output | Job_ID |
| campaign-schedule | Campaign_Schedule | Campaign_ID |
| material-plan | Material_Plan | Campaign_ID |
| equipment-schedule | Equipment_Schedule | Job_ID |
| schedule-gantt | Schedule_Gantt | Resource_ID |
| scenario-output | Scenario_Output | Scenario |
| ctp-output | CTP_Output | Request_ID |
| theo-vs-actual | Theo_vs_Actual | Job_ID |
| kpi-dashboard | KPI_Dashboard | KPI |

## Read-only sheets

- `control-panel`
- `help`

These are intentionally not exposed for row mutation because they are narrative or layout-like workbook sheets.
