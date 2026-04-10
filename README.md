# X-APS

Version `0.10.0`

Workbook-backed Advanced Planning and Scheduling prototype for steel operations. The app combines a Flask API, a static HTML/JS frontend, and planning engines for scheduling, BOM netting, capacity, material, execution, CTP, and scenarios.

## What Is In This Repo

- `xaps_application_api.py` - main Flask API and workbook-backed application layer
- `ui_design/` - active frontend (`index.html`, `styles.css`, `app.js`)
- `engine/` - planning engines for scheduling, BOM, capacity, campaigns, CTP, and workbook access
- `tests/` and root `test_*.py` files - regression and workflow checks
- `APS_BF_SMS_RM.xlsx` / `APS_BF_SMS_RM.xlsm` - workbook data sources used by the API and helpers
- `docs/` - reference notes and architecture material

## Quick Start

### Windows launcher

Use the bundled launcher if you want the API and UI started together:

```bat
start_aps.bat
```

That starts:

- API: `http://localhost:5000`
- UI: `http://localhost:3131`

### Manual start

1. Create and activate a Python environment.
2. Install the Python dependencies you need for the API.
3. Start the API:

```bash
python xaps_application_api.py
```

4. In a second terminal, serve the UI folder:

```bash
npx serve -s ui_design -p 3131 --no-clipboard
```

5. Open:

```text
http://localhost:3131
```

If you need to point at a different workbook, set `WORKBOOK_PATH` before starting the API.

## How To Use The App

### Main planning flow

1. `Dashboard`
   Review the current order pool, planning status, material health, capacity, and alerts.
2. `Planning`
   Propose planning orders, derive heats, run feasibility checks, and release lots.
3. `BOM`
   Run total BOM netting and inspect overall exploded material requirements.
4. `Material`
   Review campaign-level material readiness and release blockers.
5. `Capacity`
   Inspect resource loading, bottlenecks, and utilisation by equipment.
6. `Execution`
   Review released lots and dispatch-oriented views.
7. `CTP`
   Run capable-to-promise checks for a requested SKU, quantity, and date.

### Key actions

- `Run Schedule`
  Runs the schedule solver for the selected horizon and solver time budget.
- `Run Pipeline`
  Walks through the planning sequence from order selection through feasibility and release preparation.
- `Run BOM Netting`
  Generates total BOM explosion and netting outputs for the active demand pool.
- `Run Feasibility Check`
  Evaluates whether the selected planning orders fit inside the chosen horizon and resource configuration.

## Useful Development Commands

```bash
python xaps_application_api.py
python run_all.py
python run_all.py schedule
python -m pytest tests
node --check ui_design/app.js
```

## Repo Notes

- The workbook is part of the runtime, not just test data.
- The active frontend path is intentionally small: `ui_design/index.html`, `ui_design/styles.css`, and `ui_design/app.js`.
- Legacy one-off UI files and root-level notes have been moved out of the active surface so the working tree is easier to navigate.

## Source Control Hygiene

- Keep generated files, logs, PID files, backups, caches, and archived legacy files out of commits.
- Prefer focused commits by feature area instead of sweeping the full dirty workbook/runtime state into one change.
- Use checkpoint tags before larger cleanup or redesign passes.
