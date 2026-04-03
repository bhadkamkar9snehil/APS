# APS
Proto Advanced Planning & Scheduling

## Overview

APS is a prototype Advanced Planning and Scheduling (APS) implementation in Python and TypeScript, built around AVEVA-style planning workflows. It includes a REST API server, scheduling engines, scenario runners, and UI scaffolding.

## Repositories

- root: core Python logic, engine modules, scenario runner, tests
- `aps-ui`: front-end React / TypeScript UI
- `docs`: architecture guides, functional concept documents, and process references

## Key components

- `api_server.py`: Flask-based CRUD API and APS service endpoints
- `aps_functions.py`: business logic for APS operations
- `engine/`: core scheduling functions (`bom_explosion`, `capacity`, `ctp`, `scheduler`)
- `scenarios/`: scenario setup and runner tools
- `simulation/`: SimPy-based engine
- `tests/`: unit tests covering scheduling, capacity, campaign config, etc.

## Installation

1. Install Python 3.11+.
2. Create a virtual environment: `python -m venv .venv`.
3. Activate: Windows: `.\.venv\Scripts\Activate`.
4. Install dependencies: `pip install -r requirements.txt` (if exists) or install needed libs manually.

## Running API

```powershell
python api_server.py
```

Then use API endpoints described in code or docs.

## Documentation

See the `docs/` folder for detailed guides:

- `APS_Functional_Concept_Guide.md`
- `APS_Gap_Analysis_vs_AVEVA.md`
- `APS_Generic_Architecture_And_Scenario_Workbench.md`
- `APS_Implementation_Plan_Config_Masters.md`
- `APS_Planning_Views_And_Visibility_Design.md`
- `APS_Roadmap_Industry_Agnostic.md`
- `wiggly-kindling-lynx.md`

## Notes

- `archive/` and generated artifacts are excluded via `.gitignore`.
- Keep the root focused on code; docs live in `docs/`.

