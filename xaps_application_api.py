"""
X-APS Application API
---------------------
Concept-oriented Flask API for the X-APS HTML application.

This server treats the Excel workbook as the persistence layer while exposing
APS application concepts as the API contract:
- dashboard
- orders
- campaigns
- schedule
- dispatch
- capacity
- material
- CTP
- scenarios
- master data

It also preserves the legacy route names already used by the existing UI.

Run:
    python xaps_application_api.py
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import time
import traceback
import uuid
from datetime import date, datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

import numpy as np
import pandas as pd
from flask import Flask, request
from flask_cors import CORS

sys.path.insert(0, str(Path(__file__).parent))

from engine.bom_explosion import consolidate_demand, explode_bom_details, net_requirements, simulate_material_commit
from engine.campaign import build_campaigns
from engine.capacity import capacity_map, compute_demand_hours
from engine.ctp import capable_to_promise
from engine.excel_store import ExcelStore
from engine.scheduler import schedule
from engine.workbook_schema import SHEETS

raw_workbook_path = os.getenv("WORKBOOK_PATH", str(Path(__file__).parent / "APS_BF_SMS_RM.xlsx"))
raw_workbook_path = raw_workbook_path.strip().strip('"').strip("'")
WORKBOOK = Path(raw_workbook_path)
HEADER_ROW = 2
PORT = int(os.getenv("PORT", "5000"))

app = Flask(__name__)
CORS(app, origins=["http://localhost:3131", "http://127.0.0.1:3131", "*"])
store = ExcelStore(WORKBOOK)

MASTERDATA_SECTION_TO_SHEET = {
    "config": "config",
    "resources": "resource-master",
    "routing": "routing",
    "queue": "queue-times",
    "skus": "sku-master",
    "bom": "bom",
    "inventory": "inventory",
    "campaign-config": "campaign-config",
    "changeover": "changeover-matrix",
    "scenarios": "scenarios",
}

OUTPUT_SECTION_TO_SHEET = {
    "capacity": "capacity-map",
    "campaigns": "campaign-schedule",
    "material": "material-plan",
    "dispatch": "equipment-schedule",
    "schedule": "schedule-output",
    "gantt": "schedule-gantt",
    "kpis": "kpi-dashboard",
    "ctp-output": "ctp-output",
    "scenario-output": "scenario-output",
}


class _Enc(json.JSONEncoder):
    def default(self, obj: Any):
        if isinstance(obj, (np.integer,)):
            return int(obj)
        if isinstance(obj, (np.floating,)):
            return None if np.isnan(obj) else float(obj)
        if isinstance(obj, np.ndarray):
            return obj.tolist()
        if isinstance(obj, (datetime, date, pd.Timestamp)):
            try:
                return obj.isoformat()
            except Exception:
                return str(obj)
        try:
            if pd.isna(obj):
                return None
        except Exception:
            pass
        return super().default(obj)


def _safe(v):
    if v is None:
        return None
    if isinstance(v, float) and (np.isnan(v) or np.isinf(v)):
        return None
    if isinstance(v, (np.integer,)):
        return int(v)
    if isinstance(v, (np.floating,)):
        return float(v)
    if isinstance(v, (datetime, date, pd.Timestamp)):
        try:
            return v.isoformat()
        except Exception:
            return str(v)
    try:
        if pd.isna(v):
            return None
    except Exception:
        pass
    return v


def _jsonify(data, status: int = 200):
    return app.response_class(json.dumps(data, cls=_Enc, ensure_ascii=False), status=status, mimetype="application/json")


def _df_to_records(df: pd.DataFrame) -> list:
    if df is None or df.empty:
        return []
    return [{k: _safe(v) for k, v in row.items()} for _, row in df.iterrows()]


def _sheet_items(api_name: str, **kwargs) -> List[Dict[str, Any]]:
    if api_name not in SHEETS:
        return []
    return store.list_rows(api_name, **kwargs)["items"]


def _read_sheet(sheet: str, required: list | None = None) -> pd.DataFrame:
    """Read Excel sheet with retry logic for file locking issues."""
    max_retries = 3
    retry_delay = 0.5  # seconds
    last_error: Exception | None = None
    
    for attempt in range(max_retries):
        try:
            df = pd.read_excel(WORKBOOK, sheet_name=sheet, header=HEADER_ROW, dtype=str)
            df = df.dropna(how="all").reset_index(drop=True)
            if required:
                df = df.dropna(subset=[c for c in required if c in df.columns], how="all")
            return df
        except Exception as e:
            last_error = e
            if attempt < max_retries - 1:
                # File might be locked, wait and retry
                time.sleep(retry_delay)
    
    # If we get here, all retries failed
    if last_error:
        raise last_error
    raise RuntimeError("Failed to read Excel sheet after multiple retries")
def _read_config() -> dict:
    try:
        df = _read_sheet("Config", ["Key"])
        return {str(r["Key"]).strip(): r.get("Value") for _, r in df.iterrows() if str(r.get("Key", "")).strip()}
    except Exception:
        return {}


def _read_queue_times() -> dict:
    try:
        df = _read_sheet("Queue_Times", ["From_Operation"])
        out = {}
        for _, r in df.iterrows():
            k = (str(r.get("From_Operation", "") or "").strip().upper(), str(r.get("To_Operation", "") or "").strip().upper())
            try:
                out[k] = {"min": float(r.get("Min_Queue_Min") or 0), "max": float(r.get("Max_Queue_Min") or 120), "enforcement": str(r.get("Enforcement") or "Hard").strip()}
            except Exception:
                pass
        return out
    except Exception:
        return {}


def _load_all() -> dict:
    config = _read_config()
    so_raw = _read_sheet("Sales_Orders", ["SO_ID", "SKU_ID"])
    res = _read_sheet("Resource_Master", ["Resource_ID"])
    skus = _read_sheet("SKU_Master", ["SKU_ID"])
    routing = _read_sheet("Routing", ["SKU_ID", "Operation"])
    bom = _read_sheet("BOM", ["Parent_SKU", "Child_SKU"])
    inv = _read_sheet("Inventory", ["SKU_ID"])
    queue = _read_queue_times()

    def _num(df, col, default=0.0):
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors="coerce").fillna(default)
        return df

    if "Available_Qty" not in inv.columns and "Stock_Qty" in inv.columns:
        inv = inv.rename(columns={"Stock_Qty": "Available_Qty"})
    inv = _num(inv, "Available_Qty", 0.0)

    res = _num(res, "Avail_Hours_Day", 20.0)
    res = _num(res, "Max_Capacity_MT_Hr", 33.3)
    if "Plant" not in res.columns:
        res["Plant"] = "Plant"

    bom = _num(bom, "Qty_Per", 1.0)
    bom = _num(bom, "Yield_Pct", 100.0)
    bom = _num(bom, "Level", 1.0)

    so_raw["Delivery_Date"] = pd.to_datetime(so_raw.get("Delivery_Date"), errors="coerce")
    so_raw["Order_Date"] = pd.to_datetime(so_raw.get("Order_Date"), errors="coerce")
    so_raw["Status"] = so_raw.get("Status", "Open").fillna("Open")
    so_raw = _num(so_raw, "Order_Qty_MT", 0.0)
    so_raw = _num(so_raw, "Section_mm", 6.5)
    if "Order_Qty_MT" not in so_raw.columns and "Order_Qty" in so_raw.columns:
        so_raw["Order_Qty_MT"] = pd.to_numeric(so_raw["Order_Qty"], errors="coerce").fillna(0)
    if "Order_Qty" not in so_raw.columns:
        so_raw["Order_Qty"] = so_raw.get("Order_Qty_MT", 0)

    open_mask = so_raw["Status"].astype(str).str.strip().str.upper().isin({"OPEN", "CONFIRMED", "PLANNED", ""})
    open_so = so_raw[open_mask].copy()
    if open_so.empty:
        open_so = so_raw.copy()

    routing = _num(routing, "Cycle_Time_Min_Heat", 60.0)
    routing = _num(routing, "Setup_Time_Min", 0.0)
    routing = _num(routing, "Transfer_Time_Min", 0.0)
    routing = _num(routing, "Op_Seq", 10.0)
    if "Sequence" not in routing.columns and "Op_Seq" in routing.columns:
        routing["Sequence"] = routing["Op_Seq"]

    return {"config": config, "sales_orders": open_so, "all_orders": so_raw, "resources": res, "skus": skus, "routing": routing, "bom": bom, "inventory": inv, "queue_times": queue}


_state: dict = {"last_run": None, "campaigns": [], "heat_schedule": pd.DataFrame(), "camp_schedule": pd.DataFrame(), "capacity": pd.DataFrame(), "solver_status": "NOT RUN", "solver_detail": "", "error": None}

# Run artifact storage - tracks all planning runs with full context
_run_artifacts: Dict[str, Dict[str, Any]] = {}  # run_id -> artifact
_active_run_id: Optional[str] = None  # Currently active run


def _create_run_artifact(
    config: Dict,
    campaigns: List[Dict],
    heat_schedule: pd.DataFrame,
    campaign_schedule: pd.DataFrame,
    capacity_map_df: pd.DataFrame,
    solver_status: str,
    solver_detail: str,
    warnings: List[str] | None = None,
    degraded_flags: Dict[str, bool] | None = None,
) -> str:
    """Create a canonical planning run artifact with full context.
    
    Returns the run_id for reference and storage.
    """
    run_id = str(uuid.uuid4())
    created_at = datetime.now().isoformat()
    
    # Compute config snapshot for reproducibility
    config_snapshot = {k: v for k, v in (config or {}).items()}
    
    # Extract key planning parameters
    allow_default_masters = config_snapshot.get('Allow_Default_Masters', False)
    campaign_serialization_mode = config_snapshot.get('Campaign_Serialization_Mode', 'STANDARD')
    
    # Build artifact
    artifact = {
        "run_id": run_id,
        "created_at": created_at,
        "workbook_path": str(WORKBOOK),
        "input_snapshot": {
            "campaign_count": len(campaigns),
            "solver_config": {
                "solver_status": solver_status,
                "solver_detail": solver_detail,
                "allow_default_masters": allow_default_masters,
                "campaign_serialization_mode": campaign_serialization_mode,
            }
        },
        "config_snapshot": config_snapshot,
        "results": {
            "campaigns": campaigns,
            "heat_schedule": heat_schedule.to_dict('records') if not heat_schedule.empty else [],
            "campaign_schedule": campaign_schedule.to_dict('records') if not campaign_schedule.empty else [],
            "capacity": capacity_map_df.to_dict('records') if not capacity_map_df.empty else [],
        },
        "solver_metadata": {
            "status": solver_status,
            "detail": solver_detail,
        },
        "warnings": warnings or [],
        "degraded_flags": degraded_flags or {
            "default_masters_used": allow_default_masters,
            "greedy_fallback": solver_status in {"GREEDY", "GREEDY_FALLBACK"},
            "material_incomplete": False,
            "inventory_lineage_degraded": False,
        },
    }
    
    # Store artifact
    _run_artifacts[run_id] = artifact
    
    return run_id


def _get_active_run_artifact() -> Dict[str, Any] | None:
    """Get the currently active planning run artifact."""
    if _active_run_id and _active_run_id in _run_artifacts:
        return _run_artifacts[_active_run_id]
    return None


def _set_active_run_artifact(run_id: str) -> bool:
    """Set the active run artifact by ID."""
    global _active_run_id
    if run_id in _run_artifacts:
        _active_run_id = run_id
        return True
    return False


def _campaigns_to_view(campaigns: list, camp_df: pd.DataFrame | None) -> list:
    result = []
    for c in campaigns:
        cid = str(c.get("campaign_id", ""))
        sched = {}
        if camp_df is not None and not camp_df.empty and "Campaign_ID" in camp_df.columns:
            match = camp_df[camp_df["Campaign_ID"].astype(str) == cid]
            if not match.empty:
                sched = {k: _safe(v) for k, v in match.iloc[0].to_dict().items()}
        result.append({
            "campaign_id": cid,
            "campaign_group": str(c.get("campaign_group", "")),
            "grade": str(c.get("grade", "")),
            "heats": int(c.get("heats", 0) or 0),
            "total_mt": round(float(c.get("total_coil_mt", 0) or 0), 1),
            "release_status": str(c.get("release_status", "HELD")),
            "needs_vd": bool(c.get("needs_vd", False)),
            "billet_family": str(c.get("billet_family", "")),
            "shortages": [{"sku_id": k, "qty": round(float(v or 0), 2)} for k, v in (c.get("material_shortages") or {}).items()],
            "so_list": [str(s) for s in (c.get("so_list") or [])],
            **sched,
        })
    return result


def _campaign_rows() -> List[Dict[str, Any]]:
    if _state["campaigns"]:
        return _campaigns_to_view(_state["campaigns"], _state["camp_schedule"])
    if "campaign-schedule" in SHEETS:
        return _sheet_items("campaign-schedule")
    return []


def _schedule_rows() -> List[Dict[str, Any]]:
    if _state["heat_schedule"] is not None and not _state["heat_schedule"].empty:
        return _df_to_records(_state["heat_schedule"])
    if "schedule-output" in SHEETS:
        return _sheet_items("schedule-output")
    return []


def _capacity_rows() -> List[Dict[str, Any]]:
    if _state["capacity"] is not None and not _state["capacity"].empty:
        return _df_to_records(_state["capacity"])
    if "capacity-map" in SHEETS:
        return _sheet_items("capacity-map")
    return []


def _material_plan_payload() -> Dict[str, Any]:
    """Parse Material Plan sheet and return structured campaign/plant hierarchy."""
    # First priority: use calculated material plan from last schedule run
    if _state.get("material_plan_data"):
        return _state["material_plan_data"]
    
    if "material-plan" not in SHEETS:
        return {"summary": {}, "campaigns": []}
    
    try:
        rows = _sheet_items("material-plan")
        if not rows:
            return {"summary": {}, "campaigns": []}
        
        # For now, derive a basic structure from flat rows
        # Group by campaign
        campaigns_dict = {}
        
        for row in rows:
            camp_id = str(row.get('Campaign_ID') or row.get('campaign_id') or 'UNKNOWN').strip()
            
            # Skip placeholder/instruction rows
            if camp_id.startswith('Run') or camp_id == 'UNKNOWN' or not camp_id:
                continue
            
            if camp_id not in campaigns_dict:
                campaigns_dict[camp_id] = {
                    "campaign_id": camp_id,
                    "grade": str(row.get('Grade') or row.get('grade') or ''),
                    "release_status": str(row.get('Release_Status') or row.get('release_status') or 'OPEN'),
                    "required_qty": 0,
                    "plants": {}
                }
            
            # Get plant info
            plant = str(row.get('Plant') or row.get('plant') or 'UNKNOWN')
            if plant not in campaigns_dict[camp_id]["plants"]:
                campaigns_dict[camp_id]["plants"][plant] = {
                    "plant": plant,
                    "required_qty": 0,
                    "inventory_covered_qty": 0,
                    "rows": []
                }
            
            # Parse numeric values
            try:
                req_qty = float(row.get('Required_Qty') or row.get('required_qty') or 0)
            except (ValueError, TypeError):
                req_qty = 0
            
            try:
                avail_qty = float(row.get('Available_Before') or row.get('available_before') or 0)
            except (ValueError, TypeError):
                avail_qty = 0
            
            # Build detail row
            detail_row = {
                "material_type": str(row.get('Material_Type') or row.get('material_type') or ''),
                "material_sku": str(row.get('Material_SKU') or row.get('material_sku') or ''),
                "material_name": str(row.get('Material_Name') or row.get('material_name') or ''),
                "required_qty": req_qty,
                "available_before": avail_qty,
                "consumed": _safe(row.get('Consumed') or row.get('consumed')),
                "remaining_after": _safe(row.get('Remaining_After') or row.get('remaining_after')),
                "status": str(row.get('Status') or row.get('status') or 'OK')
            }
            
            campaigns_dict[camp_id]["plants"][plant]["rows"].append(detail_row)
            campaigns_dict[camp_id]["plants"][plant]["required_qty"] += req_qty
            campaigns_dict[camp_id]["plants"][plant]["inventory_covered_qty"] += avail_qty
            campaigns_dict[camp_id]["required_qty"] += req_qty
        
        # Convert plants dict to list
        campaigns_list = []
        for camp_id, camp_data in campaigns_dict.items():
            camp_data["plants"] = list(camp_data["plants"].values())
            campaigns_list.append(camp_data)
        
        # Build summary
        total_required = sum(c.get('required_qty', 0) for c in campaigns_list)
        total_inventory = sum(
            sum(p.get('inventory_covered_qty', 0) for p in c.get('plants', []))
            for c in campaigns_list
        )
        make_convert = total_required - total_inventory
        released_count = sum(1 for c in campaigns_list if 'RELEASED' in str(c.get('release_status', '')).upper())
        held_count = sum(1 for c in campaigns_list if 'HOLD' in str(c.get('release_status', '')).upper())
        
        summary = {
            "Campaigns": len(campaigns_list),
            "Released": released_count,
            "Held": held_count,
            "Shortage Lines": sum(1 for c in campaigns_list for p in c.get('plants', []) for r in p.get('rows', []) if 'SHORT' in str(r.get('status', '')).upper()),
            "Total Required Qty": total_required,
            "Inventory Covered Qty": total_inventory,
            "Make / Convert Qty": max(0, make_convert)
        }
        
        return {
            "summary": summary,
            "campaigns": campaigns_list
        }
    except Exception as e:
        print(f"Error parsing material plan: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return {"summary": {}, "campaigns": []}


def _material_rows() -> List[Dict[str, Any]]:
    return _sheet_items("material-plan") if "material-plan" in SHEETS else []


def _dispatch_rows() -> List[Dict[str, Any]]:
    if "dispatch" in OUTPUT_SECTION_TO_SHEET and OUTPUT_SECTION_TO_SHEET["dispatch"] in SHEETS:
        return _sheet_items(OUTPUT_SECTION_TO_SHEET["dispatch"])
    # fallback built from schedule output
    jobs = _schedule_rows()
    grouped: Dict[str, Dict[str, Any]] = {}
    for job in jobs:
        rid = str(job.get("Resource_ID") or job.get("resource_id") or "UNKNOWN")
        bucket = grouped.setdefault(rid, {"resource_id": rid, "jobs": [], "job_count": 0, "total_mt": 0.0})
        bucket["jobs"].append(job)
        bucket["job_count"] += 1
        try:
            bucket["total_mt"] += float(job.get("Qty_MT") or 0)
        except Exception:
            pass
    return list(grouped.values())


def _dashboard_payload() -> Dict[str, Any]:
    campaigns = _campaign_rows()
    capacity = _capacity_rows()
    material = _material_rows()
    total_mt = sum(float(x.get("Total_MT") or x.get("total_mt") or 0) for x in campaigns)
    total_heats = sum(float(x.get("Heats") or x.get("heats") or 0) for x in campaigns)
    released = sum(1 for x in campaigns if str(x.get("Release_Status") or x.get("release_status") or "").upper() == "RELEASED")
    held = sum(1 for x in campaigns if "HOLD" in str(x.get("Release_Status") or x.get("release_status") or x.get("Status") or "").upper())
    late = sum(1 for x in campaigns if str(x.get("Status") or "").upper() == "LATE")
    bottleneck = max(capacity, key=lambda x: float(x.get("Utilisation_%") or 0)) if capacity else None
    alerts = []
    for row in material:
        status = str(row.get("Status") or "").upper()
        if status not in {"", "OK", "AVAILABLE", "COVERED"}:
            alerts.append({"campaign_id": row.get("Campaign_ID"), "sku_id": row.get("Material_SKU", row.get("SKU_ID")), "shortage_qty": row.get("Required_Qty", row.get("Shortage_Qty")), "severity": "HIGH" if status in {"CRITICAL", "BLOCKED", "SHORT"} else "MEDIUM"})
    return {"solver_status": _state["solver_status"] or "WORKBOOK", "solver_detail": _state.get("solver_detail", ""), "last_run": _state["last_run"], "campaigns_total": len(campaigns), "campaigns_released": released, "campaigns_held": held, "campaigns_late": late, "total_heats": total_heats, "total_mt": round(total_mt, 1), "on_time_pct": round(100 * max(0, len(campaigns) - late) / max(len(campaigns), 1), 1) if campaigns else 0.0, "throughput_mt_day": round(total_mt / 14, 1) if total_mt else 0.0, "bottleneck": bottleneck.get("Resource_ID") if bottleneck else None, "max_utilisation": bottleneck.get("Utilisation_%") if bottleneck else None, "shortage_alerts": alerts, "utilisation": [{"resource_id": r.get("Resource_ID"), "resource_name": r.get("Resource_Name", r.get("Resource_ID")), "utilisation": r.get("Utilisation_%"), "demand_hrs": r.get("Demand_Hrs"), "avail_hrs": r.get("Avail_Hrs_14d"), "status": r.get("Status"), "operation": r.get("Operation_Group", "")} for r in capacity]}


def _masterdata_payload() -> Dict[str, Any]:
    return {section: _sheet_items(api_name) for section, api_name in MASTERDATA_SECTION_TO_SHEET.items() if api_name in SHEETS}


def _section_sheet(section: str) -> Optional[str]:
    return MASTERDATA_SECTION_TO_SHEET.get(section)


def _output_sheet(section: str) -> Optional[str]:
    return OUTPUT_SECTION_TO_SHEET.get(section)


# legacy-compatible routes
@app.route('/api/health')
def health():
    exists = WORKBOOK.exists()
    mtime = datetime.fromtimestamp(WORKBOOK.stat().st_mtime).isoformat() if exists else None

    workbook_ok = False
    workbook_error = None

    if exists:
        try:
            import openpyxl
            wb = openpyxl.load_workbook(WORKBOOK, read_only=True, data_only=True)
            wb.close()
            workbook_ok = True
        except Exception as e:
            workbook_error = str(e)

    return _jsonify({
        "ok": exists and workbook_ok,
        "api": "up",
        "workbook": str(WORKBOOK),
        "workbook_exists": exists,
        "workbook_ok": workbook_ok,
        "workbook_mtime": mtime,
        "workbook_error": workbook_error,
        "last_run": _state.get("last_run"),
        "solver_status": _state.get("solver_status", "NOT RUN"),
        "solver_detail": _state.get("solver_detail", ""),
    }, 200 if exists else 500)


@app.route('/api/data/dashboard')
def dashboard():
    return _jsonify(_dashboard_payload())


@app.route('/api/data/config')
def data_config():
    md = _masterdata_payload()
    return _jsonify({"config": md.get("config", []), "resources": md.get("resources", []), "routing": md.get("routing", []), "queue": md.get("queue", []), "skus": md.get("skus", []), "bom": md.get("bom", []), "inventory": md.get("inventory", []), "campaign_config": md.get("campaign-config", []), "changeover": md.get("changeover", []), "scenarios": md.get("scenarios", [])})


@app.route('/api/data/orders')
def data_orders():
    return _jsonify({"orders": _sheet_items('sales-orders')})


@app.route('/api/data/skus')
def data_skus():
    d = _load_all()
    skus = d['skus']
    fg = skus[skus.get('Category', pd.Series('')).astype(str).str.contains('Finished', case=False, na=False)]
    return _jsonify({"skus": [{"sku_id": r.get('SKU_ID', ''), "sku_name": r.get('SKU_Name', '')} for _, r in fg.iterrows()]})


@app.route('/api/data/campaigns')
def data_campaigns():
    return _jsonify({"campaigns": _campaign_rows()})


@app.route('/api/data/gantt')
def data_gantt():
    return _jsonify({"jobs": _schedule_rows()})


@app.route('/api/data/capacity')
def data_capacity():
    return _jsonify({"capacity": _capacity_rows()})


@app.route('/api/run/bom', methods=['POST'])
def run_bom_api():
    """Explode BOM through multi-level structure and net against inventory.
    
    Returns:
    - gross_bom: Exploded BOM before netting
    - net_bom: Exploded and netted against inventory
    - structure_errors: List of BOM structure problems (cycles, max depth)
    - feasible: Whether BOM structure is valid
    - rows: Count of net rows
    """
    try:
        d = _load_all()
        demand = consolidate_demand(d['sales_orders'])
        din = demand[['SKU_ID', 'Total_Qty']].rename(columns={'Total_Qty': 'Required_Qty'})
        
        # Explode BOM - returns dict with structured result
        explosion_result = explode_bom_details(din, d['bom'])
        gross_bom_df = explosion_result.get('exploded', pd.DataFrame())
        structure_errors = explosion_result.get('structure_errors', [])
        feasible = explosion_result.get('feasible', True)
        
        # Net against inventory using the exploded DataFrame
        netted = net_requirements(gross_bom_df, d['inventory'])
        
        return _jsonify({
            "gross_bom": _df_to_records(gross_bom_df),
            "net_bom": _df_to_records(netted),
            "structure_errors": structure_errors,
            "feasible": feasible,
            "rows": len(netted)
        })
    except Exception as e:
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


def _enrich_campaigns_with_material_data(campaigns: List[Dict], bom: pd.DataFrame, inventory: pd.DataFrame | dict) -> List[Dict]:
    """Enrich campaigns with actual material simulation results.
    
    For each campaign with production orders, simulates material commit to populate:
    - inventory_before, inventory_after
    - material_consumed, material_gross_requirements
    - material_shortages, material_structure_errors
    - material_feasible flag
    
    This replaces placeholder/fake data with real BOM explosion results.
    """
    enriched = []
    
    for campaign in campaigns:
        enriched_camp = dict(campaign)
        material_status = str(campaign.get('material_status', 'UNREVIEWED')).strip()
        
        # Extract production demand from campaign's production orders
        production_orders = campaign.get('production_orders', [])
        if not production_orders:
            enriched.append(enriched_camp)
            continue
        
        # Build demand DataFrame from production orders
        demand_rows = []
        for po in production_orders:
            sku_id = str(po.get('sku_id') or po.get('SKU_ID') or '')
            qty_mt = float(po.get('qty_mt') or po.get('Qty_MT') or 0.0)
            if sku_id and qty_mt > 1e-6:
                demand_rows.append({'SKU_ID': sku_id, 'Required_Qty': qty_mt})
        
        if not demand_rows:
            enriched.append(enriched_camp)
            continue
        
        # Simulate material commit for this campaign's demand
        try:
            demand_df = pd.DataFrame(demand_rows)
            result = simulate_material_commit(
                demand=demand_df,
                bom=bom,
                inventory=inventory,
                max_levels=10,
                on_structure_error='log'  # Don't hard-fail on BOM issues; log them instead
            )
            
            # Extract results
            enriched_camp['inventory_before'] = campaign.get('inventory_before', {})
            enriched_camp['inventory_after'] = result.get('inventory_after', {})
            enriched_camp['material_consumed'] = result.get('consumed', {})
            enriched_camp['material_gross_requirements'] = result.get('gross_requirements', {})
            enriched_camp['material_shortages'] = result.get('shortages', {})
            enriched_camp['material_structure_errors'] = result.get('structure_errors', [])
            enriched_camp['material_feasible'] = result.get('feasible', True)
            
            # Update material status based on simulation results
            if not result.get('feasible', True):
                enriched_camp['material_status'] = 'STRUCTURE_ERROR'
            elif result.get('shortages', {}):
                enriched_camp['material_status'] = 'SHORTAGE'
            elif material_status == 'UNREVIEWED':
                enriched_camp['material_status'] = 'OK'
            
        except Exception as e:
            # If simulation fails, mark campaign as degraded but don't crash
            enriched_camp['material_status'] = 'SIMULATION_ERROR'
            enriched_camp['material_structure_errors'] = [{'type': 'SIMULATION_ERROR', 'message': str(e)}]
            enriched_camp['material_feasible'] = False
            print(f"Error simulating material for campaign {campaign.get('campaign_id')}: {e}", file=sys.stderr)
        
        enriched.append(enriched_camp)
    
    return enriched


def _calculate_material_plan(campaigns: List[Dict], bom: pd.DataFrame, inventory: pd.DataFrame, skus: pd.DataFrame) -> Dict[str, Any]:
    """Build material plan response from enriched campaign data.
    
    NOTE: This function now uses material data enriched during scheduling.
    For each campaign, it structures the material results for API consumption.
    """
    try:
        campaigns_data = []
        
        for camp in campaigns:
            camp_id = str(camp.get('campaign_id') or camp.get('Campaign_ID') or '')
            if not camp_id:
                continue
            
            grade = str(camp.get('grade') or camp.get('Grade') or '')
            release_status = str(camp.get('release_status') or camp.get('Release_Status') or 'OPEN')
            req_qty = float(camp.get('total_coil_mt') or camp.get('total_mt') or camp.get('Total_MT') or 0)
            material_status = str(camp.get('material_status') or 'UNREVIEWED')
            shortages = camp.get('material_shortages', {})
            consumed = camp.get('material_consumed', {})
            structure_errors = camp.get('material_structure_errors', [])
            feasible = camp.get('material_feasible', True)
            
            # Build summary for this campaign
            shortage_qty = sum(float(v or 0) for v in shortages.values())
            has_shortage = shortage_qty > 1e-6
            
            # Determine status based on material check results
            if not feasible:
                display_status = 'STRUCTURE_ERROR'
            elif has_shortage:
                display_status = 'SHORTAGE'
            else:
                display_status = 'MAKE / CONVERT' if req_qty > 1e-6 else 'OK'
            
            # Build plants view (for now, aggregate as single plant)
            plants_dict = {}
            plant_name = 'Production RM'
            
            plants_dict[plant_name] = {
                "plant": plant_name,
                "required_qty": req_qty,
                "shortage_qty": shortage_qty,
                "inventory_covered_qty": req_qty - shortage_qty,
                "material_status": material_status,
                "structure_errors": structure_errors,
                "feasible": feasible,
                "rows": []
            }
            
            # Add shortage detail rows
            for sku_id, shortage_amount in shortages.items():
                if shortage_amount > 1e-6:
                    shortage_row = {
                        "material_type": "Raw Material",
                        "material_sku": sku_id,
                        "material_name": f"Shortage: {sku_id}",
                        "required_qty": shortage_amount,
                        "available_before": 0,
                        "consumed": 0,
                        "remaining_after": -shortage_amount,
                        "status": f"SHORT {shortage_amount:.2f}MT"
                    }
                    plants_dict[plant_name]["rows"].append(shortage_row)
            
            # If there are no shortages but there are gross requirements, show consumed items
            if not shortages and consumed:
                for sku_id, consumed_amount in consumed.items():
                    if consumed_amount > 1e-6:
                        consumed_row = {
                            "material_type": "Raw Material",
                            "material_sku": sku_id,
                            "material_name": f"Consumed: {sku_id}",
                            "required_qty": consumed_amount,
                            "available_before": consumed_amount,
                            "consumed": consumed_amount,
                            "remaining_after": 0,
                            "status": "CONSUMED"
                        }
                        plants_dict[plant_name]["rows"].append(consumed_row)
            
            camp_data = {
                "campaign_id": camp_id,
                "grade": grade,
                "release_status": release_status,
                "material_status": material_status,
                "required_qty": req_qty,
                "shortage_qty": shortage_qty,
                "feasible": feasible,
                "structure_errors": structure_errors,
                "plants": list(plants_dict.values())
            }
            campaigns_data.append(camp_data)
        
        # Build summary
        total_required = sum(c.get('required_qty', 0) for c in campaigns_data)
        total_shortages = sum(c.get('shortage_qty', 0) for c in campaigns_data)
        total_covered = total_required - total_shortages
        released_count = sum(1 for c in campaigns_data if 'RELEASED' in str(c.get('release_status', '')).upper())
        held_count = sum(1 for c in campaigns_data if 'HOLD' in str(c.get('release_status', '')).upper())
        shortage_count = sum(1 for c in campaigns_data if c.get('shortage_qty', 0) > 1e-6)
        error_count = sum(1 for c in campaigns_data if not c.get('feasible', True))
        
        summary = {
            "Campaigns": len(campaigns_data),
            "Released": released_count,
            "Held": held_count,
            "Shortage Lines": shortage_count,
            "BOM Structure Errors": error_count,
            "Total Required Qty": round(total_required, 2),
            "Inventory Covered Qty": round(total_covered, 2),
            "Shortage Qty": round(total_shortages, 2),
            "Make / Convert Qty": max(0, round(total_required - total_covered, 2))
        }
        
        return {
            "summary": summary,
            "campaigns": campaigns_data,
            "detail_level": "campaign"  # Indicates this is campaign-level, not plant-level detail
        }
    except Exception as e:
        print(f"Error calculating material plan: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return {"summary": {"error": str(e)}, "campaigns": []}


@app.route('/api/run/schedule', methods=['POST'])
def run_schedule():
    """Schedule API endpoint - executes scheduling and returns complete planning result.
    
    This endpoint is identical to /api/aps/schedule/run and should remain in sync.
    Accepts optional body parameters: horizon (days), solver_sec (seconds).
    """
    return run_schedule_api()


def run_schedule_api():
    try:
        d = _load_all()
        config = d['config']
        min_cmt = float(config.get('Min_Campaign_MT', 100.0) or 100.0)
        max_cmt = float(config.get('Max_Campaign_MT', 500.0) or 500.0)
        horizon = int(float(config.get('Planning_Horizon_Days', 14) or 14))
        sec_lim = float(config.get('Default_Solver_Limit_Sec', 30.0) or 30.0)
        body = request.get_json(silent=True) or {}
        horizon = int(body.get('horizon', horizon))
        sec_lim = float(body.get('solver_sec', body.get('time_limit', sec_lim)))
        campaigns = build_campaigns(d['sales_orders'], min_campaign_mt=min_cmt, max_campaign_mt=max_cmt, inventory=d['inventory'], bom=d['bom'], config=config, skus=d['skus'])
        released = [c for c in campaigns if str(c.get('release_status', '')).upper() == 'RELEASED']
        result = schedule(campaigns, d['resources'], planning_horizon_days=horizon, planning_start=datetime.now(), routing=d['routing'], queue_times=d['queue_times'], config=config, solver_time_limit_sec=sec_lim)
        heat_df = result.get('heat_schedule', pd.DataFrame())
        camp_df = result.get('campaign_schedule', pd.DataFrame())
        solver = result.get('solver_status', 'UNKNOWN')
        s_detail = result.get('solver_detail', 'CP_SAT_NA')
        demand_hrs = compute_demand_hours(released, d['resources'], routing=d['routing'])
        cap_df = capacity_map(demand_hrs, d['resources'], horizon_days=horizon)
        
        # Extract scheduler metadata for degraded-mode tracking
        allow_defaults = result.get('allow_default_masters', False)
        scheduler_mode = result.get('campaign_serialization_mode', 'STANDARD')
        
        # Enrich campaigns with actual material simulation results
        enriched_campaigns = _enrich_campaigns_with_material_data(campaigns, d['bom'], d['inventory'])
        
        # Calculate material plan from enriched campaigns with real material data
        _state['material_plan_data'] = _calculate_material_plan(enriched_campaigns, d['bom'], d['inventory'], d['skus'])
        
        # Build degraded-mode flags based on scheduler output and config
        degraded_flags = {
            "default_masters_used": allow_defaults,
            "greedy_fallback": solver in {"GREEDY", "GREEDY_FALLBACK"},
            "material_incomplete": False,  # Will be updated if material checks fail
            "inventory_lineage_degraded": False,  # Will be updated if lineage was recomputed
        }
        
        # Create canonical run artifact with full planning context
        run_id = _create_run_artifact(
            config=config,
            campaigns=enriched_campaigns,
            heat_schedule=heat_df,
            campaign_schedule=camp_df,
            capacity_map_df=cap_df,
            solver_status=solver,
            solver_detail=s_detail,
            warnings=[],
            degraded_flags=degraded_flags
        )
        _set_active_run_artifact(run_id)
        
        _state['last_run'] = datetime.now().isoformat()
        _state['run_id'] = run_id  # Track active run in legacy _state
        _state['campaigns'] = enriched_campaigns  # Store enriched campaigns with material data
        _state['heat_schedule'] = heat_df
        _state['camp_schedule'] = camp_df
        _state['capacity'] = cap_df
        _state['solver_status'] = solver
        _state['solver_detail'] = s_detail
        _state['error'] = None
        return _jsonify({
            **_dashboard_payload(), 
            'campaigns': _campaigns_to_view(enriched_campaigns, camp_df), 
            'gantt': _df_to_records(heat_df), 
            'capacity': _df_to_records(cap_df),
            'run_id': run_id  # Include run_id in response for client tracking
        })
    except Exception as e:
        _state['error'] = str(e)
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route('/api/run/ctp', methods=['POST'])
def run_ctp_api():
    try:
        body = request.get_json(silent=True) or {}
        sku_id = str(body.get('sku_id', ''))
        qty_mt = float(body.get('qty_mt', 100.0))
        requested_date = body.get('requested_date', (datetime.now() + pd.Timedelta(days=14)).isoformat())
        d = _load_all()
        config = d['config']
        min_cmt = float(config.get('Min_Campaign_MT', 100.0) or 100.0)
        max_cmt = float(config.get('Max_Campaign_MT', 500.0) or 500.0)
        campaigns = _state['campaigns'] or build_campaigns(d['sales_orders'], min_cmt, max_cmt, inventory=d['inventory'], bom=d['bom'], config=config, skus=d['skus'])
        res = capable_to_promise(sku_id=sku_id, qty_mt=qty_mt, requested_date=requested_date, campaigns=campaigns, resources=d['resources'], bom=d['bom'], inventory=d['inventory'], routing=d['routing'], skus=d['skus'], planning_start=datetime.now(), config=config, queue_times=d['queue_times'])
        return _jsonify({k: _safe(v) for k, v in res.items()})
    except Exception as e:
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route('/api/orders/assign', methods=['POST'])
def orders_assign():
    payload = request.get_json(silent=True) or {}
    assignments = payload.get('assignments', []) if isinstance(payload, dict) else []
    updated = 0
    for assignment in assignments:
        so_id = str(assignment.get('so_id', '')).strip()
        campaign_id = assignment.get('campaign_id')
        if not so_id:
            continue
        try:
            store.update_row('sales-orders', so_id, {'Campaign_ID': campaign_id}, partial=True)
            updated += 1
        except Exception:
            continue
    return _jsonify({"ok": True, "updated": updated})


@app.route('/api/orders', methods=['GET', 'POST'])
def orders_collection():
    if request.method == 'GET':
        return _jsonify({"orders": _sheet_items('sales-orders')})
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        return _jsonify({"ok": True, "item": store.create_row('sales-orders', data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/orders/<so_id>', methods=['GET', 'PUT', 'DELETE'])
def order_item(so_id):
    if request.method == 'GET':
        row = store.get_row('sales-orders', so_id)
        return _jsonify({"order": row} if row else {"error": 'Not found'}, 200 if row else 404)
    if request.method == 'DELETE':
        try:
            store.delete_row('sales-orders', so_id)
            return _jsonify({"ok": True, 'so_id': so_id})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        return _jsonify({"ok": True, 'item': store.update_row('sales-orders', so_id, data, partial=False)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)


# application/domain routes
@app.route('/api/aps/dashboard/overview')
def aps_dashboard_overview():
    dashboard = _dashboard_payload()
    return _jsonify({"summary": dashboard, "campaigns": _campaign_rows()[:8], "alerts": dashboard.get('shortage_alerts', [])[:10], "utilisation": dashboard.get('utilisation', [])[:8], "release_queue": sorted(_campaign_rows(), key=lambda x: str(x.get('Due_Date') or x.get('due_date') or '9999-12-31'))[:8]})


@app.route('/api/aps/orders/list')
def aps_orders_list():
    items = _sheet_items('sales-orders', search=request.args.get('search'))
    priority = request.args.get('priority')
    grade = request.args.get('grade')
    if priority:
        items = [x for x in items if str(x.get('Priority', '')).upper() == priority.upper()]
    if grade:
        items = [x for x in items if str(x.get('Grade', '')) == grade]
    return _jsonify({"items": items, "total": len(items)})


@app.route('/api/aps/orders/<so_id>', methods=['GET', 'PUT', 'DELETE'])
def aps_order_item(so_id):
    return order_item(so_id)


@app.route('/api/aps/orders', methods=['POST'])
def aps_order_create():
    return orders_collection()


@app.route('/api/aps/orders/assign', methods=['POST'])
def aps_orders_assign():
    return orders_assign()


@app.route('/api/aps/campaigns/list')
def aps_campaigns_list():
    items = _campaign_rows()
    status = request.args.get('status')
    if status:
        items = [x for x in items if status.upper() in str(x.get('release_status') or x.get('Release_Status') or x.get('Status') or '').upper()]
    return _jsonify({"items": items, "total": len(items)})


@app.route('/api/aps/campaigns/release-queue')
def aps_campaign_release_queue():
    items = sorted(_campaign_rows(), key=lambda x: str(x.get('Due_Date') or x.get('due_date') or '9999-12-31'))
    return _jsonify({"items": items})


@app.route('/api/aps/campaigns/<campaign_id>')
def aps_campaign_item(campaign_id):
    for item in _campaign_rows():
        cid = str(item.get('campaign_id') or item.get('Campaign_ID') or '')
        if cid == campaign_id:
            return _jsonify({"item": item})
    return _jsonify({"error": 'Campaign not found'}, 404)


@app.route('/api/aps/campaigns/<campaign_id>/status', methods=['PATCH'])
def aps_campaign_status_update(campaign_id):
    # workbook-backed when Campaign_Schedule exists with Campaign_ID key
    if 'campaign-schedule' not in SHEETS:
        return _jsonify({"error": 'Campaign_Schedule sheet not available for updates'}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        item = store.update_row('campaign-schedule', campaign_id, data, partial=True)
        return _jsonify({"item": item})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/aps/schedule/gantt')
def aps_schedule_gantt():
    return _jsonify({"jobs": _schedule_rows()})


@app.route('/api/aps/schedule/run', methods=['POST'])
def aps_schedule_run():
    return run_schedule_api()


@app.route('/api/aps/schedule/jobs/<job_id>')
def aps_schedule_job_item(job_id):
    for item in _schedule_rows():
        jid = str(item.get('Job_ID') or item.get('job_id') or '')
        if jid == job_id:
            return _jsonify({"item": item})
    return _jsonify({"error": 'Job not found'}, 404)


@app.route('/api/aps/schedule/jobs/<job_id>/reschedule', methods=['PATCH'])
def aps_schedule_job_reschedule(job_id):
    if 'schedule-output' not in SHEETS:
        return _jsonify({"error": 'Schedule_Output sheet not available for updates'}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        item = store.update_row('schedule-output', job_id, data, partial=True)
        return _jsonify({"item": item})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)


@app.route('/api/aps/dispatch/board')
def aps_dispatch_board():
    return _jsonify({"resources": _dispatch_rows()})


@app.route('/api/aps/dispatch/resources/<resource_id>')
def aps_dispatch_resource_item(resource_id):
    items = _dispatch_rows()
    for item in items:
        rid = str(item.get('resource_id') or item.get('Resource_ID') or '')
        if rid == resource_id:
            return _jsonify({"item": item})
    return _jsonify({"error": 'Resource not found'}, 404)


@app.route('/api/aps/capacity/map')
def aps_capacity_map():
    return _jsonify({"items": _capacity_rows()})


@app.route('/api/aps/capacity/bottlenecks')
def aps_capacity_bottlenecks():
    items = sorted(_capacity_rows(), key=lambda x: float(x.get('Utilisation_%') or 0), reverse=True)
    return _jsonify({"items": items[:10]})


@app.route('/api/aps/material/plan')
def aps_material_plan():
    return _jsonify(_material_plan_payload())


@app.route('/api/aps/material/holds')
def aps_material_holds():
    items = [x for x in _material_rows() if str(x.get('Status', '')).upper() not in {'', 'OK', 'AVAILABLE', 'COVERED'}]
    return _jsonify({"items": items, "total": len(items)})


@app.route('/api/aps/clear-outputs', methods=['POST'])
def aps_clear_outputs():
    """Clear all calculated output data from state."""
    try:
        # Reset all calculated state variables
        _state['campaigns'] = []
        _state['heat_schedule'] = None
        _state['camp_schedule'] = None
        _state['capacity'] = None
        _state['material_plan_data'] = None
        _state['last_run'] = None
        _state['solver_status'] = 'CLEARED'
        _state['solver_detail'] = ''
        _state['error'] = None
        return _jsonify({"ok": True, "message": "All outputs cleared"})
    except Exception as e:
        _state['error'] = str(e)
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route('/api/aps/ctp/check', methods=['POST'])
def aps_ctp_check():
    return run_ctp_api()


@app.route('/api/aps/ctp/requests', methods=['GET', 'POST'])
def aps_ctp_requests():
    if request.method == 'GET':
        return _jsonify({"items": _sheet_items('ctp-request') if 'ctp-request' in SHEETS else []})
    if 'ctp-request' not in SHEETS:
        return _jsonify({"error": 'CTP_Request sheet not available'}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        return _jsonify({"item": store.create_row('ctp-request', data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/aps/ctp/output')
def aps_ctp_output():
    return _jsonify({"items": _sheet_items('ctp-output') if 'ctp-output' in SHEETS else []})


@app.route('/api/aps/scenarios/list')
def aps_scenarios_list():
    return _jsonify({"items": _sheet_items('scenarios') if 'scenarios' in SHEETS else []})


@app.route('/api/aps/scenarios', methods=['POST'])
def aps_scenario_create():
    if 'scenarios' not in SHEETS:
        return _jsonify({"error": 'Scenarios sheet not available'}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        return _jsonify({"item": store.create_row('scenarios', data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/aps/scenarios/<key_value>', methods=['PUT', 'PATCH', 'DELETE'])
def aps_scenario_item(key_value):
    if 'scenarios' not in SHEETS:
        return _jsonify({"error": 'Scenarios sheet not available'}, 400)
    if request.method == 'DELETE':
        try:
            store.delete_row('scenarios', key_value)
            return _jsonify({"deleted": True, 'key': key_value})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    partial = request.method == 'PATCH'
    try:
        return _jsonify({"item": store.update_row('scenarios', key_value, data, partial=partial)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)


@app.route('/api/aps/scenarios/output')
def aps_scenarios_output():
    return _jsonify({"items": _sheet_items('scenario-output') if 'scenario-output' in SHEETS else []})


@app.route('/api/aps/scenarios/apply', methods=['POST'])
def aps_scenarios_apply():
    payload = request.get_json(silent=True) or {}
    scenario = payload.get('scenario')
    return _jsonify({"ok": True, 'applied_scenario': scenario, 'note': 'Workbook-backed scenario selection acknowledged. Use /api/aps/schedule/run to recompute the live plan.'})


@app.route('/api/aps/masterdata')
def aps_masterdata():
    return _jsonify(_masterdata_payload())


@app.route('/api/aps/masterdata/<section>')
def aps_masterdata_section(section):
    api_name = _section_sheet(section)
    if not api_name:
        return _jsonify({"error": f'Unknown master data section: {section}'}, 404)
    return _jsonify({"items": _sheet_items(api_name)})


@app.route('/api/aps/masterdata/<section>', methods=['POST'])
def aps_masterdata_section_create(section):
    api_name = _section_sheet(section)
    if not api_name:
        return _jsonify({"error": f'Unknown master data section: {section}'}, 404)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    try:
        return _jsonify({"item": store.create_row(api_name, data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/aps/masterdata/<section>/<key_value>', methods=['GET', 'PUT', 'PATCH', 'DELETE'])
def aps_masterdata_section_item(section, key_value):
    api_name = _section_sheet(section)
    if not api_name:
        return _jsonify({"error": f'Unknown master data section: {section}'}, 404)
    if request.method == 'GET':
        row = store.get_row(api_name, key_value)
        return _jsonify({"item": row} if row else {"error": 'Not found'}, 200 if row else 404)
    if request.method == 'DELETE':
        try:
            store.delete_row(api_name, key_value)
            return _jsonify({"deleted": True, 'key': key_value})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
        except ValueError as e:
            return _jsonify({"error": str(e)}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and 'data' not in payload else payload.get('data', {})
    partial = request.method == 'PATCH'
    try:
        return _jsonify({"item": store.update_row(api_name, key_value, data, partial=partial)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)
        

@app.route('/api/aps/masterdata/<section>/bulk-replace', methods=['PUT'])
def aps_masterdata_bulk_replace(section):
    api_name = _section_sheet(section)
    if not api_name:
        return _jsonify({"error": f'Unknown master data section: {section}'}, 404)
    payload = request.get_json(silent=True) or {}
    items = payload.get('items', []) if isinstance(payload, dict) else []
    try:
        return _jsonify(store.bulk_replace(api_name, items))
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route('/api/meta/xaps/routes')
def xaps_route_manifest():
    return _jsonify({
        'legacy': ['/api/health','/api/data/dashboard','/api/data/config','/api/data/orders','/api/data/skus','/api/data/campaigns','/api/data/gantt','/api/data/capacity','/api/run/bom','/api/run/schedule','/api/run/ctp','/api/orders','/api/orders/<so_id>','/api/orders/assign'],
        'application': ['/api/aps/dashboard/overview','/api/aps/orders/list','/api/aps/orders','/api/aps/orders/<so_id>','/api/aps/orders/assign','/api/aps/campaigns/list','/api/aps/campaigns/release-queue','/api/aps/campaigns/<campaign_id>','/api/aps/campaigns/<campaign_id>/status','/api/aps/schedule/gantt','/api/aps/schedule/run','/api/aps/schedule/jobs/<job_id>','/api/aps/schedule/jobs/<job_id>/reschedule','/api/aps/dispatch/board','/api/aps/dispatch/resources/<resource_id>','/api/aps/capacity/map','/api/aps/capacity/bottlenecks','/api/aps/material/plan','/api/aps/material/holds','/api/aps/ctp/check','/api/aps/ctp/requests','/api/aps/ctp/output','/api/aps/scenarios/list','/api/aps/scenarios','/api/aps/scenarios/<key_value>','/api/aps/scenarios/output','/api/aps/scenarios/apply','/api/aps/masterdata','/api/aps/masterdata/<section>','/api/aps/masterdata/<section>/<key_value>','/api/aps/masterdata/<section>/bulk-replace']
    })


if __name__ == '__main__':
    print(f'\n  X-APS Application API -> http://localhost:{PORT}')
    print(f'  Workbook              -> {WORKBOOK}\n')
    app.run(host='0.0.0.0', port=PORT, debug=False, use_reloader=False)
