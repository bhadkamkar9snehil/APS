# APS

Proto Advanced Planning & Scheduling

## Overview

This repository contains a prototype for Advanced Planning and Scheduling (APS) functionality, inspired by AVEVA MESA patterns.

## Directory structure

- `api_server.py` - API server entry point.
- `aps_functions.py` - APS domain logic helpers.
- `engine/` - core engine modules for BOM explosion, capacity, CTP, scheduler.
- `scenarios/` - scenario runner and orchestrator code.
- `simulation/` - SimPy-based simulation engine.
- `tests/` - unit tests.
- `aps-ui/` - React UI frontend.
- `docs/` - architecture and feature reference documentation.

## Setup

1. Python 3.11+ recommended.
2. Create virtual environment: `python -m venv .venv`.
3. Activate environment:
   - Windows: `.\.venv\Scripts\activate`
4. Install dependencies (if dependency file exists). For example:
   `pip install flask pydantic simpy` etc.

## Usage

Run the API server:

```powershell
python api_server.py
```

Check `docs/` for functional and architectural details.

## Docs

Primary docs are in the `docs/` folder, including:

- `APS_Functional_Concept_Guide.md`
- `APS_Gap_Analysis_vs_AVEVA.md`
- `APS_Generic_Architecture_And_Scenario_Workbench.md`
- `APS_Implementation_Plan_Config_Masters.md`
- `APS_Planning_Views_And_Visibility_Design.md`
- `APS_Roadmap_Industry_Agnostic.md`
- `wiggly-kindling-lynx.md`

## Cleanup policy

- Ignore `.claude`, `.vscode/`, `__pycache__/`, `node_modules/`, and `archive/` via `.gitignore`.
