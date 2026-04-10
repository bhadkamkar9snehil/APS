# X-APS

Version `0.10.1`

X-APS is a workbook-backed Advanced Planning and Scheduling prototype for integrated steel operations. It combines a Flask API, a lightweight HTML/JS frontend, and planning engines for order selection, planning-order formation, heat derivation, finite scheduling, BOM netting, capacity analysis, material readiness, execution handoff, scenarios, and capable-to-promise checks.

## Current State Of The Project

If you are returning to this repo after some time, this is the current intended setup:

- the main backend entry point is `xaps_application_api.py`
- the main static UI is `ui_design/index.html` + `ui_design/styles.css` + `ui_design/app.js`
- the workbook is the operational data store
- `engine/` contains the planning logic used by the API
- `aps-ui/` is a React/TypeScript workstream that exists in the repo, but the actively used browser UI today is the static `ui_design/` app unless you intentionally switch to the React path
- `archive/` contains legacy files that were moved out of the active surface for traceability

If you need a quick mental model, think:

`Workbook data + Flask API + engine logic + static UI = current running APS`

## What The Software Does

At a high level, the application turns open sales demand into a manufacturable plan:

1. Load open Sales Orders from the workbook.
2. Convert them into proposed Planning Orders.
3. Derive Heats from the proposed Planning Orders.
4. Run a finite schedule across SMS/RM resources.
5. Check whether the schedule fits the selected horizon and resource assumptions.
6. Evaluate BOM netting and campaign-level material readiness.
7. Release approved Planning Orders to Execution.

The main planning model is:

`Sales Order -> Planning Order -> Heat Batch -> Finite Schedule -> Execution Release`

The workbook is both input source and persistence layer. The API reads workbook sheets, runs planning logic, and writes planning outputs back into workbook-backed artifacts.

## How The Software Works Internally

The system is workbook-first, not database-first.

### Inputs

The workbook provides:

- Sales Orders
- BOM structure
- inventory
- routing
- resources
- queue / timing rules
- campaign and scenario configuration

### Processing layers

The application then processes that data through several layers:

1. `xaps_application_api.py`
   Reads workbook-backed data and exposes application-facing API routes.
2. `engine/`
   Performs planning logic such as:
   - planning-order formation
   - campaign building
   - BOM explosion and netting
   - capacity loading
   - finite scheduling
   - CTP calculations
3. `ui_design/`
   Calls the API, renders the planning workflow, and shows the operational views.

### Outputs

The system writes or exposes outputs for:

- planning orders
- heat schedule
- campaign schedule
- capacity map
- material plan
- BOM output
- CTP output
- KPI/dashboard state

That is why the workbook, API, and UI can feel tightly coupled: they are.

## Main Features By Tab

### `Dashboard`

The dashboard is the planning cockpit. It summarizes:

- order-pool and planning-order counts
- heats and tonnage planned
- on-time / lateness status
- current feasibility state
- planning-stage progress
- material health
- capacity utilisation
- active planning orders and alerts

Use this page to understand whether the current plan is healthy before diving into details.

### `Planning`

This is the core APS workflow page.

It supports:

- filtering and selecting Sales Orders
- proposing Planning Orders within a planning window
- merge / split / freeze / re-propose operations
- deriving heats
- running feasibility checks with horizon, resource-count, rolling-mode, priority, and grade filters
- viewing planning Gantt/timeline output
- releasing approved Planning Orders to execution

This page answers: "Can we build this demand set as a finite plan, under the selected assumptions?"

### `BOM`

The BOM tab shows the total exploded BOM and netting result for the active demand pool.

It is material-centric and is used to inspect:

- total material demand
- covered / partial / short items
- gross vs net requirements
- plant and material-type grouping
- byproduct / intermediate outputs

This page answers: "What does the total plan require materially?"

### `Material`

The Material tab is campaign-centric rather than total-demand-centric.

It is used to review:

- whether a specific Planning Order / campaign is materially ready
- required quantity
- stock-covered quantity
- make / convert quantity
- shortage quantity
- held lots and release blockers

This page answers: "Is this campaign ready to release, and what is blocking it?"

### `Capacity`

The Capacity tab shows resource loading and bottlenecks from the current schedule / capacity map.

It highlights:

- bottleneck resource
- average utilisation
- overloaded resources
- slack / underutilised resources
- full capacity-map table by resource

This page answers: "Where is the load sitting, and what equipment is constraining the plan?"

### `Execution`

The Execution tab is the handoff layer from planning into release/dispatch views.

It is used to inspect:

- released lots
- dispatch-oriented lists
- production timeline views
- downstream execution-facing status

### `CTP`

CTP stands for Capable To Promise.

It supports:

- checking whether a requested SKU/quantity/date can be promised
- showing the earliest feasible promise date
- reviewing request/output history

This page answers: "If a customer asks for this material by this date, what can we realistically commit to?"

### `Scenarios`

The Scenarios tab is for what-if management and scenario persistence.

It is used to:

- preview saved scenarios
- create and update scenarios
- apply a scenario back into the active model

### `Master Data`

The Master Data tab exposes workbook-backed master sections through the application API.

Typical areas include:

- config
- resources
- routing
- queue times
- SKUs
- BOM
- inventory
- campaign configuration
- changeover matrix
- scenarios

## Important Concepts

### Total BOM vs Material Readiness

- `BOM` tab = total exploded/netted requirement for the active demand pool
- `Material` tab = release-readiness view for individual Planning Orders / campaigns

They are related, but they answer different questions.

### Schedule Span vs Workload

In feasibility:

- `Schedule span` = elapsed time from first scheduled start to last scheduled finish
- `SMS span` / `RM span` = elapsed windows for those resource families
- `Workload` = summed processing hours

Workload can be higher than span when multiple lines run in parallel.

### Rolling Mode

Rolling mode is part of feasibility and scheduling logic.

- `HOT` and `COLD` are not just labels
- they influence how RM timing is scheduled
- they also affect filtered feasibility runs

## Source Of Truth Guide

When files disagree, use this order of trust:

1. `xaps_application_api.py`
   This is the authoritative application layer for the current static UI.
2. `engine/`
   This is where the actual planning logic lives.
3. `ui_design/`
   This is the active browser UI.
4. `README.md` and `docs/`
   Helpful, but not always authoritative if implementation moved faster than documentation.
5. `archive/`
   Historical reference only.

For UI questions, trust the active files in `ui_design/`.

For API behavior, trust `xaps_application_api.py`.

For planning logic, trust the relevant module in `engine/`.

## Why Some Files Look Out Of Sync

This repo accumulated drift over multiple phases, which is why some files can look inconsistent.

The main reasons are:

- the project evolved quickly as a prototype, with many iterations landing directly in the repo
- the workbook acts as a live persistence layer, so implementation changes often touched API, engine, UI, and workbook-facing logic at different times
- there are two UI tracks in the repo:
  - `ui_design/` static UI
  - `aps-ui/` React/TypeScript workstream
- older UI snapshots, patches, and CSS variants existed before the cleanup pass and were archived rather than fully deleted
- docs were written across several implementation phases, so some documents describe earlier states of the product
- some utility scripts at root solve one-off workbook or master-data problems and are not part of the daily runtime path

In short: not every file in the repo exists at the same maturity level or serves the same runtime purpose.

## What To Read First When Catching Up

If you are coming back after a few months, read things in this order:

1. this `README.md`
2. `xaps_application_api.py`
3. the active UI files in `ui_design/`
4. the relevant engine module:
   - BOM issue -> `engine/bom_explosion.py`
   - capacity issue -> `engine/capacity.py`
   - CTP issue -> `engine/ctp.py`
   - scheduling / feasibility issue -> `engine/scheduler.py`
   - planning-order / heat issue -> `engine/aps_planner.py`
5. selected docs in `docs/` only after you know which area you are working on

## Quick Diagnostic Checklist

When something looks wrong, check these first:

- Is the API running from `xaps_application_api.py`?
- Is the UI being served from `ui_design/`?
- Is the workbook path pointing to the intended workbook?
- Was the relevant run actually executed?
  - schedule
  - BOM
  - feasibility
  - CTP
- Are you looking at a total-demand view (`BOM`) or a campaign-level view (`Material`)?
- Are you reading span, workload, and feasibility correctly?
- Are you looking at active files or archived / legacy files?

## Quick Start

### Windows launcher

Use the launcher if you want the API and UI started together:

```bat
start_aps.bat
```

It starts:

- API at `http://localhost:5000`
- UI at `http://localhost:3131`

### Manual start

1. Create and activate a Python environment.
2. Install the Python dependencies required by the API.
3. Start the API:

```bash
python xaps_application_api.py
```

4. In another terminal, serve the static UI:

```bash
npx serve -s ui_design -p 3131 --no-clipboard
```

5. Open:

```text
http://localhost:3131
```

If you need to point to a different workbook:

```bash
export WORKBOOK_PATH=/path/to/APS_BF_SMS_RM.xlsx
python xaps_application_api.py
```

On Windows PowerShell:

```powershell
$env:WORKBOOK_PATH="C:\path\to\APS_BF_SMS_RM.xlsx"
python xaps_application_api.py
```

## Recommended User Flow

1. Open `Dashboard` to understand current plan health.
2. Go to `Planning` and select the Sales Orders you want in scope.
3. `Propose` Planning Orders.
4. `Derive` heats.
5. Run `Feasibility Check`.
6. Review `Capacity`, `BOM`, and `Material` depending on the issue:
   - capacity issue -> inspect `Capacity`
   - total material issue -> inspect `BOM`
   - campaign release blocker -> inspect `Material`
7. `Release` approved Planning Orders to `Execution`.
8. Use `CTP` when answering promise-date questions for new requests.

## Repo Structure

- `xaps_application_api.py` - main Flask API and workbook-backed application layer
- `ui_design/` - active frontend: [index.html](/mnt/c/Users/bhadk/Documents/APS/ui_design/index.html), [styles.css](/mnt/c/Users/bhadk/Documents/APS/ui_design/styles.css), [app.js](/mnt/c/Users/bhadk/Documents/APS/ui_design/app.js)
- `engine/` - scheduling, BOM, capacity, CTP, campaign, and workbook helpers
- `aps-ui/` - React/TypeScript UI workstream now vendored into the repo
- `tests/` and root `test_*.py` files - regression and workflow checks
- `docs/` - architecture, audits, and implementation notes
- `archive/` - archived legacy files kept for traceability

## Key Root-Level Files

- `start_aps.bat` - quickest way to launch API + static UI on Windows
- `run_all.py` - helper for running workbook-oriented APS pipeline steps
- `aps_functions.py` - legacy/helper APS workflow logic still used by some tooling
- `requirements-excel-api.txt` - Python dependency reference for workbook/API work
- `setup_excel.py`, `create_algorithm_config_sheet.py`, `master_data_fixer.py`, `optimize_master_data.py` - support scripts, not the main application entry path
- `VERSION` - lightweight local version marker

## Useful Commands

```bash
python xaps_application_api.py
python run_all.py
python run_all.py schedule
python -m pytest tests
node --check ui_design/app.js
```

## Notes

- The workbook is runtime data, not just sample data.
- The active static frontend path is intentionally small: `ui_design/index.html`, `ui_design/styles.css`, and `ui_design/app.js`.
- Archived legacy files were moved out of the active working surface to make the repo easier to navigate.
- The static UI and the React UI are not the same thing. Unless explicitly stated otherwise, assume `ui_design/` is the active product surface.
