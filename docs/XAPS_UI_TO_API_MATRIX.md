# X-APS UI to API Coverage Matrix

This document maps the HTML application concepts and actions to the API routes and workbook-backed persistence model.

## Design principle

The API is not sheet-first. The workbook is only the backing store. The public contract is application-first and aligned to APS screens and actions.

Primary runtime file:
- `xaps_application_api.py`

## Screen coverage

### Dashboard
- summary KPIs → `GET /api/aps/dashboard/overview`
- release queue preview → `GET /api/aps/campaigns/release-queue`
- alerts → `GET /api/aps/dashboard/overview`
- utilisation preview → `GET /api/aps/dashboard/overview`
- run schedule button → `POST /api/aps/schedule/run`

### Sales Orders
- list/search/filter → `GET /api/aps/orders/list`
- create → `POST /api/aps/orders`
- read single → `GET /api/aps/orders/{so_id}`
- update → `PUT /api/aps/orders/{so_id}`
- delete → `DELETE /api/aps/orders/{so_id}`
- assign to campaign → `POST /api/aps/orders/assign`

### Campaigns
- list/filter → `GET /api/aps/campaigns/list`
- release queue → `GET /api/aps/campaigns/release-queue`
- single campaign → `GET /api/aps/campaigns/{campaign_id}`
- status/release/hold updates → `PATCH /api/aps/campaigns/{campaign_id}/status`

### Schedule / Gantt
- gantt jobs → `GET /api/aps/schedule/gantt`
- run schedule → `POST /api/aps/schedule/run`
- get job → `GET /api/aps/schedule/jobs/{job_id}`
- reschedule/edit job → `PATCH /api/aps/schedule/jobs/{job_id}/reschedule`

### Dispatch
- board → `GET /api/aps/dispatch/board`
- single resource dispatch → `GET /api/aps/dispatch/resources/{resource_id}`

### Capacity
- map → `GET /api/aps/capacity/map`
- bottlenecks → `GET /api/aps/capacity/bottlenecks`

### Material
- full plan → `GET /api/aps/material/plan`
- shortages / holds → `GET /api/aps/material/holds`

### CTP
- check from UI → `POST /api/aps/ctp/check`
- create request row → `POST /api/aps/ctp/requests`
- request history → `GET /api/aps/ctp/requests`
- result history → `GET /api/aps/ctp/output`

### Scenarios
- list → `GET /api/aps/scenarios/list`
- create → `POST /api/aps/scenarios`
- update → `PUT/PATCH /api/aps/scenarios/{key_value}`
- delete → `DELETE /api/aps/scenarios/{key_value}`
- output → `GET /api/aps/scenarios/output`
- apply scenario button → `POST /api/aps/scenarios/apply`

### Master Data
- full payload → `GET /api/aps/masterdata`
- section read → `GET /api/aps/masterdata/{section}`
- create → `POST /api/aps/masterdata/{section}`
- read item → `GET /api/aps/masterdata/{section}/{key_value}`
- update item → `PUT/PATCH /api/aps/masterdata/{section}/{key_value}`
- delete item → `DELETE /api/aps/masterdata/{section}/{key_value}`
- bulk replace section → `PUT /api/aps/masterdata/{section}/bulk-replace`

## Legacy route compatibility

The server also preserves the existing route family:
- `/api/health`
- `/api/data/dashboard`
- `/api/data/config`
- `/api/data/orders`
- `/api/data/skus`
- `/api/data/campaigns`
- `/api/data/gantt`
- `/api/data/capacity`
- `/api/run/bom`
- `/api/run/schedule`
- `/api/run/ctp`
- `/api/orders`
- `/api/orders/{so_id}`
- `/api/orders/assign`

## Workbook-backed sections

### Master / input sections
- Config
- SKU_Master
- BOM
- Inventory
- Sales_Orders
- Resource_Master
- Routing
- Campaign_Config
- Changeover_Matrix
- Queue_Times
- Scenarios
- CTP_Request

### Output / runtime sections
- BOM_Output
- Capacity_Map
- Schedule_Output
- Campaign_Schedule
- Material_Plan
- Equipment_Schedule
- Schedule_Gantt
- Scenario_Output
- CTP_Output
- Theo_vs_Actual
- KPI_Dashboard

## Gaps still intentionally left lightweight

The following buttons/actions can be represented through update endpoints, but the exact business workflow may later need stronger domain logic:
- campaign release vs hold resolution semantics
- scenario application persistence semantics
- schedule drag-and-drop conflict validation beyond row update
- dispatch acknowledgement or execution feedback if the UI grows into MES-style execution

The current API is sufficient for two-way Excel-backed communication between the HTML UI and workbook for all major screens and actions.
