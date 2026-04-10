# APS (Advanced Planning System) - GitHub Setup & Development Guide

## 🎯 Project Overview

**APS** is an integrated steel plant Advanced Planning System combining:
- **Excel-backed workbook** for master data (SKUs, inventory, BOM, routing, resources)
- **Python Flask API** for planning algorithms (OR-Tools CP-SAT solver)
- **HTML/CSS UI** for planning workflow (SO → PO → Heat → Schedule → Release)
- **Finite-capacity scheduler** for resource optimization

---

## 📦 Repository Structure

```
APS/
├── README.md                      # Project overview (you are here)
├── GIT_WORKFLOW.md               # Git branching strategy & best practices
├── CLAUDE.md                      # Claude Code AI configuration
│
├── ui_design/                     # Main planning UI (HTML/CSS)
│   ├── index.html                # Single-page app with 9 tabs
│   ├── styles.css                # CSS design system
│   └── _archive/                 # Previous UI versions
│
├── aps-ui/                        # React/Vite app (for future use)
│   ├── src/
│   ├── public/
│   └── package.json
│
├── engine/                        # Planning algorithms
│   ├── aps_planner.py            # SO→PO→Heat grouping logic
│   ├── scheduler.py              # OR-Tools finite scheduler
│   ├── bom_explosion.py          # Material requirement breakdown
│   └── ...
│
├── xaps_application_api.py        # Flask API server (2000+ lines)
│   ├── GET /api/health
│   ├── POST /api/aps/planning/*
│   ├── POST /api/aps/schedule/*
│   ├── GET /api/aps/dispatch/*
│   └── ... (40+ endpoints)
│
├── aps_functions.py              # Utility functions
├── test_*.py                      # Test suite
├── APS_BF_SMS_RM.xlsx            # Master workbook (Excel)
│
└── archive/                       # Old experiments & prototypes
```

---

## 🚀 Quick Start

### Prerequisites
- Python 3.8+
- Excel (for workbook editing)
- Git
- Flask, OR-Tools, openpyxl

### Setup

```bash
# Clone repository
git clone https://github.com/bhadkamkar9snehil/APS.git
cd APS

# Create Python environment
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate

# Install dependencies
pip install flask openpyxl ortools pandas numpy

# Run Flask server
python xaps_application_api.py
# Server runs on http://localhost:5000

# Open UI in browser
open ui_design/index.html  # or file:///.../ui_design/index.html
```

---

## 📊 APS Planning Workflow

### The 5-Stage Pipeline

```
┌─ STAGE 1: ORDER POOL ─────────────────────────────────────┐
│ Load open Sales Orders (SO) from Excel                     │
│ User filters by grade, priority, customer                  │
│ Checkboxes allow SO selection                              │
│                                                             │
│ API: GET /api/aps/planning/orders/pool                     │
└──────────────────────────────────────────────────────────┘
                            ↓
┌─ STAGE 2: PROPOSE ORDERS (SO → PO) ─────────────────────────┐
│ Auto-group SOs into Planning Orders (PO) by:               │
│ - Same grade family                                         │
│ - Section tolerance (±0.6mm default)                       │
│ - Due date window (±3 days default)                         │
│ - Max lot size (300 MT default)                             │
│                                                             │
│ User can: Merge, Split, Freeze POs                          │
│                                                             │
│ API: POST /api/aps/planning/orders/propose                 │
│      POST /api/aps/planning/orders/update                  │
└──────────────────────────────────────────────────────────┘
                            ↓
┌─ STAGE 3: DERIVE HEATS (PO → Heat) ───────────────────────┐
│ Split each PO into SMS production heats:                   │
│ - Heat size: 50 MT default (user configurable)             │
│ - Max heats per lot: 8                                      │
│ - Partial fill visualization                               │
│                                                             │
│ API: POST /api/aps/planning/heats/derive                   │
└──────────────────────────────────────────────────────────┘
                            ↓
┌─ STAGE 4: FEASIBILITY CHECK ──────────────────────────────┐
│ Run OR-Tools CP-SAT solver:                                 │
│ - Can all heats fit in planning horizon?                    │
│ - Resource availability (SMS, RM lines, equipment)?         │
│ - Setup time & changeover constraints?                      │
│                                                             │
│ If infeasible: Show remediation options                     │
│ - Narrow priority window                                    │
│ - Add parallel resources                                    │
│ - Extend horizon                                            │
│                                                             │
│ API: POST /api/aps/planning/simulate                        │
└──────────────────────────────────────────────────────────┘
                            ↓
┌─ STAGE 5: RELEASE TO EXECUTION ───────────────────────────┐
│ User approves release:                                      │
│ - POs marked as RELEASED                                    │
│ - SOs stamped with Campaign_ID in Excel                     │
│ - Campaign_Group = "APS_RELEASED"                           │
│ - Status = "Planned"                                        │
│                                                             │
│ API: POST /api/aps/planning/release                         │
└──────────────────────────────────────────────────────────┘
```

### Material Constraint Checking

During planning, click **[Material]** button on any SO/PO to view:
- **BOM Explosion**: What raw materials are needed
- **Inventory Status**: Available vs Required
- **Coverage**: COVERED (✓) | PARTIAL (⚠) | SHORT (✗)

Powered by `/api/run/bom` endpoint and real-time inventory tracking.

---

## 🔄 Git Workflow

### Branches

```
main (production)     ← Only releases & hotfixes
  ↑
  └─ release/v1.x.x  ← Prepare releases
  
develop (integration) ← Base for feature development
  ↑
  ├─ feature/*       ← New features
  ├─ bugfix/*        ← Non-critical fixes  
  └─ hotfix/*        ← Critical prod fixes
```

### Release Tagging

```
v1.0.0-planning-bom     ← Current release
v1.0.0                  ← Production release
v1.0.1                  ← Hotfix
```

**See `GIT_WORKFLOW.md` for detailed branching strategy and commit conventions.**

---

## 📋 API Endpoints (40+)

### Planning Workflow
```
GET  /api/aps/planning/orders/pool
POST /api/aps/planning/window/select
POST /api/aps/planning/orders/propose
POST /api/aps/planning/orders/update
POST /api/aps/planning/heats/derive
POST /api/aps/planning/simulate
POST /api/aps/planning/release
```

### Material & BOM
```
POST /api/run/bom                    (BOM Explosion)
GET  /api/aps/material/plan
GET  /api/aps/masterdata/inventory
```

### Execution & Dispatch
```
GET  /api/aps/schedule/gantt
POST /api/aps/schedule/run
GET  /api/aps/dispatch/board
PATCH /api/aps/schedule/jobs/{id}/reschedule
```

### Master Data CRUD
```
GET  /api/aps/masterdata
POST /api/aps/masterdata/{section}
PATCH /api/aps/masterdata/{section}/{key}
DELETE /api/aps/masterdata/{section}/{key}
```

---

## 🧪 Testing

Run the test suite:
```bash
python -m pytest test_*.py -v

# Individual test files:
python test_planning_workflow.py
python test_ui_integration.py
python test_phase1_config.py
```

Current status: **93/96 tests passing** (97%)

---

## 📚 Documentation

| File | Purpose |
|------|---------|
| `GIT_WORKFLOW.md` | Git branching & commit conventions |
| `CLAUDE.md` | Claude Code AI settings & memory |
| `README.md` | Project overview (this file) |
| `docs/` | API documentation & architecture |

---

## 🔐 Master Workbook (APS_BF_SMS_RM.xlsx)

Sheets:
- **Config**: 46 configuration parameters (horizon, tolerance, max sizes, etc.)
- **Sales_Orders**: 300+ open orders for planning
- **Resource_Master**: Equipment (SMS, RM lines, VD, CCM, etc.)
- **Routing**: Production sequences per SKU
- **SKU_Master**: Finished goods catalog
- **BOM**: Material requirements per SKU
- **Inventory**: Current stock levels
- **Campaign_Config**: Legacy campaign grouping
- **Changeover_Matrix**: Setup times between grades

---

## 🎨 UI Tabs (HTML App)

```
1. Dashboard      - Summary metrics & performance
2. Planning       - 5-stage SO→PO→Heat→Schedule→Release pipeline
3. BOM            - Material requirement explosion & coverage tracking
4. Execution      - Released lots, dispatch timeline, Gantt
5. Material       - Inventory plan & shortage resolution
6. Capacity       - Equipment utilization
7. CTP            - Capable-to-promise (available-to-promise)
8. Scenarios      - What-if planning
9. Master Data    - CRUD for all master tables
```

---

## 🚀 Development Workflow

### To Add a Feature

1. **Create feature branch**:
   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b feature/descriptive-name
   ```

2. **Make changes** and commit with proper format:
   ```bash
   git commit -m "feat(planning): add new control"
   ```

3. **Push and create PR**:
   ```bash
   git push origin feature/descriptive-name -u
   # Create PR on GitHub (base: develop, compare: feature/...)
   ```

4. **After review**:
   ```bash
   git checkout develop
   git pull origin develop
   git branch -d feature/descriptive-name
   ```

**See `GIT_WORKFLOW.md` for full workflow details.**

---

## ⚠️ Important Files (Don't Modify Lightly)

| File | Why | Impact |
|------|-----|--------|
| `xaps_application_api.py` | Core API (2000+ lines) | All endpoints broken |
| `APS_BF_SMS_RM.xlsx` | Master data | Planning invalid without data |
| `engine/aps_planner.py` | PO grouping logic | Wrong lot formation |
| `ui_design/index.html` | Main UI (3000+ lines) | Workflow breaks |

---

## 🔧 Configuration

All parameters in `APS_BF_SMS_RM.xlsx` Config sheet:

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `APS_PLANNING_HORIZON_DAYS` | 7 | Days to plan forward |
| `APS_SECTION_TOLERANCE_MM` | 0.6 | Section matching tolerance |
| `APS_MAX_DUE_SPREAD_DAYS` | 3 | Max due date span in PO |
| `APS_MAX_LOT_MT` | 300 | Max MT per PO |
| `APS_MAX_HEATS_PER_LOT` | 8 | Max heats per PO |
| `HEAT_SIZE_MT` | 50 | Default heat batch size |
| `default_heat_duration` | 2.0 | Estimated SMS hours per heat |

Change in Excel, then run `selectPlanningWindow()` to reload.

---

## 📝 Common Tasks

### Debug Planning Logic
```python
# In xaps_application_api.py, add logging:
app.aps_planner.debug = True
# Check console for grouping decisions
```

### Test with Different Config
1. Edit Excel Config sheet parameters
2. Restart Flask: `python xaps_application_api.py`
3. Run planning workflow

### Check API Health
```bash
curl http://localhost:5000/api/health | python -m json.tool
```

### View Full BOM
```bash
curl -X POST http://localhost:5000/api/run/bom | python -m json.tool | head -100
```

---

## 📞 Support

- **Architecture Questions**: See `docs/` folder
- **Git Issues**: See `GIT_WORKFLOW.md`
- **API Endpoints**: See docstrings in `xaps_application_api.py`
- **Bugs**: Create GitHub issue with reproduction steps

---

## 📄 License

[Specify license here - MIT, Apache 2.0, etc.]

---

**Last Updated**: 2026-04-07  
**Repository**: https://github.com/bhadkamkar9snehil/APS  
**Main Branch**: `main` (stable)  
**Development**: `develop` (integration)
