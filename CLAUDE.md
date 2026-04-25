# APS — CLAUDE.md

## Project overview
Advanced Planning & Scheduling (APS / X-APS) system with a Python/Flask backend
persisting data through an Excel workbook and a plain HTML/CSS/JS frontend.

## UI stack — single source of truth
**Active UI: `/home/user/APS/ui_design/`**

| File | Role |
|------|------|
| `ui_design/index.html` | Full single-page application — all screens in one file |
| `ui_design/app.js` | All JavaScript — state, API calls, rendering |
| `ui_design/styles.css` | All CSS — layout, components, themes |

**Do NOT modify or build from:**
- `aps-ui/` — abandoned React/Vite prototype (not deployed, not the active UI)
- `archive/` — legacy iterations, kept for reference only

## Backend
- Entry point: `xaps_application_api.py` (Flask)
- Data layer: Excel workbook via `openpyxl` (path configured in environment)
- API base: `/api/aps/*` (modern routes) — see `docs/XAPS_UI_TO_API_MATRIX.md`

## Key conventions
- All page sections live as `<section class="page" id="page-{name}">` in `index.html`
- Navigation tabs are defined in `TABS` array at top of `app.js`; overflow items in `#tabOverflowMenu`
- Global state object: `state` in `app.js` — single mutable store
- API helper: `apiFetch(url, init?)` — returns parsed JSON, throws on error
- DOM lookup helper: `qs(id)` — shorthand for `document.getElementById`
- Status tokens use CSS classes: `status-success`, `status-danger`, `status-warning`, `status-neutral`, `status-info`
- Metric cards use: `<div class="metric"><div class="metric-label">…</div><div class="metric-value">…</div><div class="metric-sub">…</div></div>`

## Planning pipeline (5-stage workflow)
Stage tabs: Pool → Propose → Heats → Feasibility → Release
- Pool: `loadPlanningOrderPool()` → `/api/aps/planning/orders/pool`
- Window + Propose: `selectPlanningWindow()` + `proposePlanningOrders()` → `/api/aps/planning/window/select` + `/api/aps/planning/orders/propose`
- Heats: `deriveHeatBatches()` → `/api/aps/planning/heats/derive`
- Feasibility: `simulateSchedule()` → `/api/aps/planning/simulate`
- Release: `releaseSelectedPOs()` → `/api/aps/planning/release`

## Running locally
```bash
# Backend
python xaps_application_api.py

# Frontend — just open in browser (no build step required)
open ui_design/index.html
# Or serve with any static file server:
cd ui_design && python -m http.server 5173
```
