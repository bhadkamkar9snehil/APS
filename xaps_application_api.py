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
from engine.bom_explosion import consolidate_demand, explode_bom_details, net_requirements
from engine.campaign import build_campaigns
from engine.capacity import capacity_map, compute_demand_hours
from engine.config import get_config
from engine.ctp import capable_to_promise, _frozen_jobs_from_schedule_dataframe
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


def _safe(v: Any):
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


# Compatibility cache only. Authoritative reads must come from the active run artifact first.
_state: dict = {
    "last_run": None,
    "run_id": None,
    "campaigns": [],
    "heat_schedule": pd.DataFrame(),
    "camp_schedule": pd.DataFrame(),
    "capacity": pd.DataFrame(),
    "material_plan_data": None,
    "bom_explosion": None,
    "solver_status": "NOT RUN",
    "solver_detail": "",
    "error": None,
}

_run_artifacts: Dict[str, Dict[str, Any]] = {}
_active_run_id: Optional[str] = None

# BOM grouping sort order constants (mirror of aps_functions.py)
_PLANT_SORT_ORDER = {
    "Rolling Mill": 0,
    "SMS": 1,
    "Blast Furnace": 2,
    "Shared Stores": 3,
    "Other": 9,
}

_MAT_TYPE_SORT_ORDER = {
    "Finished Good Demand": 0,
    "RM Output": 1,
    "CCM Output": 2,
    "VD Output": 3,
    "LRF Output": 4,
    "EAF Output": 5,
    "EAF Raw Charge": 6,
    "Hot Metal": 7,
    "BF Raw Mix": 8,
    "BF Raw Material": 9,
    "SMS Raw Material": 10,
    "Shared Flux": 11,
    "BF Byproduct": 12,
    "EAF Byproduct": 13,
    "LRF Byproduct": 14,
    "VD Byproduct": 15,
    "CCM Byproduct": 16,
    "RM Byproduct": 17,
    "Byproduct/Waste": 18,
    "Raw Material": 19,
    "Material": 20,
}


def _current_trace_id() -> str:
    artifact = _get_active_run_artifact()
    if artifact:
        return str(artifact.get("run_id") or "unknown")
    return str(_state.get("run_id") or "unknown")


def _error_response(
    error_message: str,
    error_code: str = "INTERNAL_ERROR",
    error_domain: str = "API",
    trace_id: str | None = None,
    details: str | None = None,
    degraded_mode: bool = False,
    status_code: int = 500,
):
    response = {
        "error": True,
        "error_code": error_code,
        "error_message": error_message,
        "error_domain": error_domain,
        "trace_id": trace_id or _current_trace_id(),
        "degraded_mode": degraded_mode,
        "details": details,
        "timestamp": datetime.now().isoformat(),
    }
    return _jsonify(response, status=status_code), status_code


def _mat_type_for_sku(sku_id: str, category: str = "", sku_name: str = "") -> str:
    """Derive material type from SKU ID, category, or name. Mirrors aps_functions._material_type_for_sku."""
    sku = str(sku_id or "").strip().upper()
    category_text = str(category or "").strip()
    if sku.startswith("FG-WR-"):
        return "Finished Good Demand"
    if sku.startswith("RM-OUT-"):
        return "RM Output"
    if sku.startswith("BIL-"):
        return "CCM Output"
    if sku.startswith("VD-OUT-"):
        return "VD Output"
    if sku.startswith("LRF-OUT-"):
        return "LRF Output"
    if sku.startswith("EAF-OUT-"):
        return "EAF Output"
    if sku.startswith("EAF-RAW-"):
        return "EAF Raw Charge"
    if sku == "BF-HM":
        return "Hot Metal"
    if sku == "BF-RAW-MIX":
        return "BF Raw Mix"
    if sku in {"RM-IRON", "RM-COAL"}:
        return "BF Raw Material"
    if sku in {"RM-SCRAP", "RM-FESI", "RM-FEMN", "RM-FECR", "RM-ELEC"}:
        return "SMS Raw Material"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Flux"
    if sku == "BF-SLAG":
        return "BF Byproduct"
    if sku == "EAF-SLAG":
        return "EAF Byproduct"
    if sku == "LRF-WASTE":
        return "LRF Byproduct"
    if sku == "VD-WASTE":
        return "VD Byproduct"
    if sku == "CCM-CROP":
        return "CCM Byproduct"
    if sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "RM Byproduct"
    if category_text:
        return category_text
    if sku_name:
        return str(sku_name).strip()
    return "Material"


def _plant_for_sku(sku_id: str, category: str = "", material_type: str = "") -> str:
    """Derive plant location from SKU ID, category, or material type. Mirrors aps_functions._plant_for_material."""
    sku = str(sku_id or "").strip().upper()
    material_type = str(material_type or "").strip()
    category_text = str(category or "").strip()
    if sku.startswith("FG-WR-") or sku.startswith("RM-OUT-") or sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "Rolling Mill"
    if (
        sku.startswith("BIL-")
        or sku.startswith("VD-OUT-")
        or sku.startswith("LRF-OUT-")
        or sku.startswith("EAF-OUT-")
        or sku.startswith("EAF-RAW-")
        or sku in {"EAF-SLAG", "LRF-WASTE", "VD-WASTE", "CCM-CROP", "RM-SCRAP", "RM-FESI", "RM-FEMN", "RM-FECR", "RM-ELEC"}
    ):
        return "SMS"
    if sku in {"BF-HM", "BF-RAW-MIX", "BF-SLAG", "RM-IRON", "RM-COAL"}:
        return "Blast Furnace"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Stores"
    if material_type == "Raw Material":
        return "Shared Stores"
    if category_text == "Raw Material":
        return "Shared Stores"
    return "Other"


def _stage_for_sku(sku_id: str, material_type: str = "") -> str:
    """Derive production stage from SKU ID or material type. Mirrors aps_functions._stage_for_material."""
    sku = str(sku_id or "").strip().upper()
    material_type = str(material_type or "").strip()
    if sku.startswith("FG-WR-") or sku.startswith("RM-OUT-") or sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "Rolling"
    if sku.startswith("BIL-") or sku == "CCM-CROP":
        return "CCM"
    if sku.startswith("VD-OUT-") or sku == "VD-WASTE":
        return "VD"
    if sku.startswith("LRF-OUT-") or sku == "LRF-WASTE":
        return "LRF"
    if sku.startswith("EAF-OUT-") or sku.startswith("EAF-RAW-") or sku == "EAF-SLAG":
        return "EAF"
    if sku in {"BF-HM", "BF-RAW-MIX", "BF-SLAG", "RM-IRON", "RM-COAL"}:
        return "BF"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Stores"
    if material_type == "Raw Material":
        return "Stores"
    return "Other"


def _df_to_records(df: pd.DataFrame | None) -> list:
    if df is None or df.empty:
        return []
    return [{k: _safe(v) for k, v in row.items()} for _, row in df.iterrows()]


def _sheet_items(api_name: str, **kwargs) -> List[Dict[str, Any]]:
    if api_name not in SHEETS:
        return []
    return store.list_rows(api_name, **kwargs)["items"]


def _read_sheet(sheet: str, required: list | None = None) -> pd.DataFrame:
    max_retries = 3
    retry_delay = 0.5
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
                time.sleep(retry_delay)
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
            key = (
                str(r.get("From_Operation", "") or "").strip().upper(),
                str(r.get("To_Operation", "") or "").strip().upper(),
            )
            try:
                out[key] = {
                    "min": float(r.get("Min_Queue_Min") or 0),
                    "max": float(r.get("Max_Queue_Min") or 120),
                    "enforcement": str(r.get("Enforcement") or "Hard").strip(),
                }
            except Exception:
                pass
        return out
    except Exception:
        return {}


def _load_all() -> dict:
    config = _read_config()
    so_raw = _read_sheet("Sales_Orders", ["SO_ID", "SKU_ID"])
    # Filter out rows where SO_ID is NaN or 'nan' string (header/description rows)
    so_raw = so_raw[so_raw["SO_ID"].notna() & (so_raw["SO_ID"] != 'nan')].reset_index(drop=True)
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

    so_raw["Delivery_Date"] = pd.to_datetime(so_raw.get("Delivery_Date"), format="mixed", errors="coerce")
    so_raw["Order_Date"] = pd.to_datetime(so_raw.get("Order_Date"), format="mixed", errors="coerce")
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

    return {
        "config": config,
        "sales_orders": open_so,
        "all_orders": so_raw,
        "resources": res,
        "skus": skus,
        "routing": routing,
        "bom": bom,
        "inventory": inv,
        "queue_times": queue,
    }


def _config_flag(config: dict | None, key: str, default: str = "N") -> bool:
    value = str((config or {}).get(key, default) or default).strip().upper()
    return value in {"Y", "YES", "TRUE", "1", "ON"}


# ----- Run artifact model -----

def _create_run_artifact(
    *,
    config: Dict[str, Any],
    campaigns: List[Dict[str, Any]],
    heat_schedule: pd.DataFrame,
    campaign_schedule: pd.DataFrame,
    capacity_map_df: pd.DataFrame,
    solver_status: str,
    solver_detail: str,
    material_plan: Dict[str, Any] | None = None,
    warnings: List[str] | None = None,
    degraded_flags: Dict[str, bool] | None = None,
) -> str:
    run_id = str(uuid.uuid4())
    created_at = datetime.now().isoformat()
    config_snapshot = {k: v for k, v in (config or {}).items()}
    allow_default_masters = _config_flag(config_snapshot, "Allow_Scheduler_Default_Masters", "N")
    campaign_serialization_mode = str(config_snapshot.get("Campaign_Serialization_Mode", "STANDARD") or "STANDARD")

    # Auto-compute material plan if not provided
    if material_plan is None:
        material_plan = _calculate_material_plan(campaigns, detail_level="campaign", run_id=run_id)

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
            },
        },
        "config_snapshot": config_snapshot,
        "results": {
            "campaigns": campaigns,
            "heat_schedule": _df_to_records(heat_schedule),
            "campaign_schedule": _df_to_records(campaign_schedule),
            "capacity": _df_to_records(capacity_map_df),
            "material_plan": material_plan,
        },
        "solver_metadata": {
            "status": solver_status,
            "detail": solver_detail,
            "allow_default_masters": allow_default_masters,
            "campaign_serialization_mode": campaign_serialization_mode,
        },
        "warnings": warnings or [],
        "degraded_flags": degraded_flags or {
            "default_masters_used": allow_default_masters,
            "greedy_fallback": solver_status in {"GREEDY", "GREEDY_FALLBACK", "UNKNOWN"},
            "material_incomplete": False,
            "inventory_lineage_degraded": False,
            "capacity_unavailable": False,
        },
    }
    _run_artifacts[run_id] = artifact
    return run_id


def _get_active_run_artifact() -> Dict[str, Any] | None:
    if _active_run_id and _active_run_id in _run_artifacts:
        return _run_artifacts[_active_run_id]
    return None


def _set_active_run_artifact(run_id: str) -> bool:
    global _active_run_id
    if run_id in _run_artifacts:
        _active_run_id = run_id
        return True
    return False


# ----- Active run reader utilities -----

def _active_run_campaigns() -> List[Dict[str, Any]]:
    artifact = _get_active_run_artifact()
    if artifact:
        return list(artifact.get("results", {}).get("campaigns", []))
    if _state.get("campaigns"):
        return list(_state["campaigns"])
    try:
        if OUTPUT_SECTION_TO_SHEET.get("campaigns", "campaign-schedule") in SHEETS:
            return _sheet_items(OUTPUT_SECTION_TO_SHEET.get("campaigns", "campaign-schedule"))
    except Exception:
        pass
    return []


def _active_run_heat_schedule() -> pd.DataFrame:
    artifact = _get_active_run_artifact()
    if artifact:
        return pd.DataFrame(artifact.get("results", {}).get("heat_schedule", []))
    if isinstance(_state.get("heat_schedule"), pd.DataFrame) and not _state["heat_schedule"].empty:
        return _state["heat_schedule"]
    try:
        if OUTPUT_SECTION_TO_SHEET.get("schedule", "schedule-output") in SHEETS:
            return _read_sheet(OUTPUT_SECTION_TO_SHEET.get("schedule", "schedule-output"))
    except Exception:
        pass
    return pd.DataFrame()


def _active_run_campaign_schedule() -> pd.DataFrame:
    artifact = _get_active_run_artifact()
    if artifact:
        return pd.DataFrame(artifact.get("results", {}).get("campaign_schedule", []))
    if isinstance(_state.get("camp_schedule"), pd.DataFrame) and not _state["camp_schedule"].empty:
        return _state["camp_schedule"]
    return pd.DataFrame()


def _active_run_capacity() -> pd.DataFrame:
    artifact = _get_active_run_artifact()
    if artifact:
        return pd.DataFrame(artifact.get("results", {}).get("capacity", []))
    if isinstance(_state.get("capacity"), pd.DataFrame) and not _state["capacity"].empty:
        return _state["capacity"]
    try:
        if OUTPUT_SECTION_TO_SHEET.get("capacity", "capacity-map") in SHEETS:
            return _read_sheet(OUTPUT_SECTION_TO_SHEET.get("capacity", "capacity-map"))
    except Exception:
        pass
    return pd.DataFrame()


def _workbook_material_plan_payload() -> Dict[str, Any]:
    if OUTPUT_SECTION_TO_SHEET.get("material", "material-plan") not in SHEETS:
        return {"summary": {}, "campaigns": [], "detail_level": "campaign"}

    try:
        rows = _sheet_items(OUTPUT_SECTION_TO_SHEET.get("material", "material-plan"))
        if not rows:
            return {"summary": {}, "campaigns": [], "detail_level": "campaign"}

        campaigns_dict: Dict[str, Dict[str, Any]] = {}
        for row in rows:
            camp_id = str(row.get("Campaign_ID") or row.get("campaign_id") or "").strip()
            if not camp_id or camp_id.startswith("Run"):
                continue

            camp = campaigns_dict.setdefault(
                camp_id,
                {
                    "campaign_id": camp_id,
                    "grade": str(row.get("Grade") or row.get("grade") or ""),
                    "release_status": str(row.get("Release_Status") or row.get("release_status") or "OPEN"),
                    "required_qty": 0.0,
                    "shortage_qty": 0.0,
                    "material_status": "WORKBOOK_FALLBACK",
                    "feasible": True,
                    "structure_errors": [],
                    "material_shortages": {},
                    "material_consumed": {},
                    "material_gross_requirements": {},
                    "detail_rows": [],
                },
            )

            sku = str(row.get("Material_SKU") or row.get("material_sku") or "").strip()
            req_qty = float(pd.to_numeric(pd.Series([row.get("Required_Qty") or row.get("required_qty")]), errors="coerce").fillna(0).iloc[0])
            consumed = float(pd.to_numeric(pd.Series([row.get("Consumed") or row.get("consumed")]), errors="coerce").fillna(0).iloc[0])
            available_before = float(pd.to_numeric(pd.Series([row.get("Available_Before") or row.get("available_before")]), errors="coerce").fillna(0).iloc[0])
            status = str(row.get("Status") or row.get("status") or "OK").strip()

            camp["required_qty"] += req_qty
            if sku and consumed > 0:
                camp["material_consumed"][sku] = round(float(camp["material_consumed"].get(sku, 0.0)) + consumed, 3)
            if sku and req_qty > 0:
                camp["material_gross_requirements"][sku] = round(float(camp["material_gross_requirements"].get(sku, 0.0)) + req_qty, 3)

            shortage_qty = max(req_qty - available_before, 0.0) if "SHORT" in status.upper() else 0.0
            if sku and shortage_qty > 0:
                camp["material_shortages"][sku] = round(float(camp["material_shortages"].get(sku, 0.0)) + shortage_qty, 3)
                camp["shortage_qty"] += shortage_qty
                camp["feasible"] = False
                camp["material_status"] = "SHORTAGE"

            camp["detail_rows"].append(
                {
                    "sku_id": sku,
                    "required_qty": round(req_qty, 3),
                    "consumed_qty": round(consumed, 3),
                    "available_before": round(available_before, 3),
                    "type": status or "WORKBOOK_ROW",
                }
            )

        campaigns = list(campaigns_dict.values())
        summary = {
            "Campaigns": len(campaigns),
            "Released": sum(1 for c in campaigns if "RELEASED" in str(c.get("release_status", "")).upper()),
            "Held": sum(1 for c in campaigns if "HOLD" in str(c.get("release_status", "")).upper()),
            "Shortage Lines": sum(1 for c in campaigns if float(c.get("shortage_qty", 0.0) or 0.0) > 1e-9),
            "BOM Structure Errors": sum(1 for c in campaigns if c.get("structure_errors")),
            "Total Required Qty": round(sum(float(c.get("required_qty", 0.0) or 0.0) for c in campaigns), 3),
            "Inventory Covered Qty": round(
                sum(max(float(c.get("required_qty", 0.0) or 0.0) - float(c.get("shortage_qty", 0.0) or 0.0), 0.0) for c in campaigns),
                3,
            ),
            "Shortage Qty": round(sum(float(c.get("shortage_qty", 0.0) or 0.0) for c in campaigns), 3),
        }
        return {"summary": summary, "campaigns": campaigns, "detail_level": "campaign"}
    except Exception:
        return {"summary": {}, "campaigns": [], "detail_level": "campaign"}


def _active_run_material() -> Dict[str, Any]:
    artifact = _get_active_run_artifact()
    if artifact:
        material_plan = artifact.get("results", {}).get("material_plan")
        if material_plan is not None:
            return material_plan
    if _state.get("material_plan_data"):
        return _state["material_plan_data"]
    return _workbook_material_plan_payload()


def _active_run_bom() -> Optional[Dict[str, Any]]:
    """Read BOM explosion results from artifact first, then compatibility cache."""
    artifact = _get_active_run_artifact()
    if artifact:
        bom = artifact.get("results", {}).get("bom_explosion")
        if bom is not None:
            return bom
    return _state.get("bom_explosion")


def _active_run_solver_metadata() -> Dict[str, Any]:
    artifact = _get_active_run_artifact()
    if artifact:
        meta = artifact.get("solver_metadata", {})
        return {
            "solver_status": meta.get("status", "UNKNOWN"),
            "solver_detail": meta.get("detail", ""),
            "allow_default_masters": bool(meta.get("allow_default_masters", False)),
            "campaign_serialization_mode": meta.get("campaign_serialization_mode", "STANDARD"),
            "degraded_flags": artifact.get("degraded_flags", {}),
            "run_id": artifact.get("run_id"),
            "created_at": artifact.get("created_at"),
            "warnings": artifact.get("warnings", []),
        }
    return {
        "solver_status": _state.get("solver_status", "UNKNOWN"),
        "solver_detail": _state.get("solver_detail", ""),
        "allow_default_masters": False,
        "campaign_serialization_mode": "STANDARD",
        "degraded_flags": {},
        "run_id": _state.get("run_id"),
        "created_at": _state.get("last_run"),
        "warnings": [],
    }


def _campaigns_to_view(campaigns: list, camp_df: pd.DataFrame | None) -> list:
    result = []
    for c in campaigns:
        cid = str(c.get("campaign_id") or c.get("Campaign_ID") or "")
        sched = {}
        if camp_df is not None and not camp_df.empty:
            key_col = "Campaign_ID" if "Campaign_ID" in camp_df.columns else "campaign_id" if "campaign_id" in camp_df.columns else None
            if key_col:
                match = camp_df[camp_df[key_col].astype(str) == cid]
                if not match.empty:
                    sched = {k: _safe(v) for k, v in match.iloc[0].to_dict().items()}
        result.append(
            {
                "campaign_id": cid,
                "campaign_group": str(c.get("campaign_group", "")),
                "grade": str(c.get("grade", "")),
                "heats": int(c.get("heats", 0) or 0),
                "total_mt": round(float(c.get("total_coil_mt", c.get("total_mt", 0)) or 0), 1),
                "release_status": str(c.get("release_status", "HELD")),
                "material_status": str(c.get("material_status", "")),
                "material_issue": str(c.get("material_issue", "")),
                "needs_vd": bool(c.get("needs_vd", False)),
                "billet_family": str(c.get("billet_family", "")),
                "shortages": [{"sku_id": k, "qty": round(float(v or 0), 2)} for k, v in (c.get("material_shortages") or {}).items()],
                "so_list": [str(s) for s in (c.get("so_ids") or c.get("so_list") or [])],
                **sched,
            }
        )
    return result


def _campaign_rows() -> List[Dict[str, Any]]:
    campaigns = _active_run_campaigns()
    if campaigns:
        return _campaigns_to_view(campaigns, _active_run_campaign_schedule())
    return []


def _schedule_rows() -> List[Dict[str, Any]]:
    return _df_to_records(_active_run_heat_schedule())


def _capacity_rows() -> List[Dict[str, Any]]:
    return _df_to_records(_active_run_capacity())


def _material_plan_payload() -> Dict[str, Any]:
    payload = _active_run_material()
    if payload and (payload.get("campaigns") or payload.get("summary")):
        return payload
    if _state.get("material_plan_data"):
        return _state["material_plan_data"]
    return _workbook_material_plan_payload()


def _material_holds_payload() -> List[Dict[str, Any]]:
    payload = _material_plan_payload()
    items: List[Dict[str, Any]] = []
    for camp in payload.get("campaigns", []):
        shortage_qty = float(camp.get("shortage_qty", 0.0) or 0.0)
        material_status = str(camp.get("material_status", "") or "").upper()
        if shortage_qty > 1e-9 or material_status not in {"", "OK", "READY"}:
            items.append(camp)
    return items


def _dispatch_rows() -> List[Dict[str, Any]]:
    if "dispatch" in OUTPUT_SECTION_TO_SHEET and OUTPUT_SECTION_TO_SHEET["dispatch"] in SHEETS:
        artifact = _get_active_run_artifact()
        if artifact:
            jobs = _schedule_rows()
        else:
            try:
                return _sheet_items(OUTPUT_SECTION_TO_SHEET["dispatch"])
            except Exception:
                jobs = _schedule_rows()
        if not artifact:
            jobs = _schedule_rows()
    else:
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
    material_data = _material_plan_payload()
    solver_meta = _active_run_solver_metadata()

    total_mt = sum(float(x.get("total_mt") or 0) for x in campaigns)
    total_heats = sum(float(x.get("heats") or 0) for x in campaigns)
    released = sum(1 for x in campaigns if str(x.get("release_status") or "").upper() == "RELEASED")
    held = sum(1 for x in campaigns if "HOLD" in str(x.get("release_status") or "").upper())
    late = sum(1 for x in campaigns if str(x.get("Status") or "").upper() == "LATE")

    bottleneck = None
    if capacity:
        bottleneck = max(capacity, key=lambda x: float(x.get("Utilisation_%") or 0))

    alerts = []
    for camp in material_data.get("campaigns", []):
        shortage_qty = float(camp.get("shortage_qty", 0.0) or 0.0)
        if shortage_qty > 1e-9:
            alerts.append({
                "campaign_id": camp.get("campaign_id"),
                "shortage_qty": round(shortage_qty, 2),
                "severity": "HIGH",
                "material_status": camp.get("material_status"),
            })
        elif str(camp.get("material_status", "")).upper() not in {"", "OK", "READY"}:
            alerts.append({
                "campaign_id": camp.get("campaign_id"),
                "shortage_qty": 0.0,
                "severity": "MEDIUM",
                "material_status": camp.get("material_status"),
            })

    return {
        "solver_status": solver_meta.get("solver_status", "WORKBOOK"),
        "solver_detail": solver_meta.get("solver_detail", ""),
        "degraded_flags": solver_meta.get("degraded_flags", {}),
        "run_id": solver_meta.get("run_id"),
        "active_run_created_at": solver_meta.get("created_at"),
        "warnings": solver_meta.get("warnings", []),
        "last_run": _state.get("last_run"),
        "campaigns_total": len(campaigns),
        "campaigns_released": released,
        "campaigns_held": held,
        "campaigns_late": late,
        "total_heats": total_heats,
        "total_mt": round(total_mt, 1),
        "on_time_pct": round(100 * max(0, len(campaigns) - late) / max(len(campaigns), 1), 1) if campaigns else 0.0,
        "throughput_mt_day": round(total_mt / 14, 1) if total_mt else 0.0,
        "bottleneck": bottleneck.get("Resource_ID") if bottleneck else None,
        "max_utilisation": bottleneck.get("Utilisation_%") if bottleneck else None,
        "shortage_alerts": alerts,
        "utilisation": [
            {
                "resource_id": r.get("Resource_ID"),
                "resource_name": r.get("Resource_Name", r.get("Resource_ID")),
                "utilisation": r.get("Utilisation_%"),
                "demand_hrs": r.get("Demand_Hrs"),
                "avail_hrs": r.get("Avail_Hrs_14d"),
                "status": r.get("Status"),
                "operation": r.get("Operation_Group", ""),
            }
            for r in capacity
        ],
        "material": material_data.get("summary", {}),
    }


def _masterdata_payload() -> Dict[str, Any]:
    return {section: _sheet_items(api_name) for section, api_name in MASTERDATA_SECTION_TO_SHEET.items() if api_name in SHEETS}


def _section_sheet(section: str) -> Optional[str]:
    return MASTERDATA_SECTION_TO_SHEET.get(section)


def _output_sheet(section: str) -> Optional[str]:
    return OUTPUT_SECTION_TO_SHEET.get(section)


def _validate_workbook_schema() -> Dict[str, Any]:
    result = {
        "valid": True,
        "missing_sheets": [],
        "missing_columns": {},
        "sheet_details": {},
        "required_sheets": list(MASTERDATA_SECTION_TO_SHEET.values()),
    }

    required_columns = {
        "Config": ["Key"],
        "Sales_Orders": ["SO_ID", "SKU_ID"],
        "BOM": ["Parent_SKU", "Child_SKU"],
        "Inventory": ["SKU_ID"],
        "Resource_Master": ["Resource_ID"],
        "Routing": ["SKU_ID", "Operation"],
        "SKU_Master": ["SKU_ID"],
    }

    try:
        import openpyxl
        wb = openpyxl.load_workbook(WORKBOOK, read_only=True, data_only=False)
        sheet_names = set(wb.sheetnames)
        wb.close()
    except Exception as e:
        result["valid"] = False
        result["schema_error"] = f"Failed to read workbook sheets: {str(e)}"
        return result

    for sheet_name in required_columns.keys():
        if sheet_name not in sheet_names:
            result["missing_sheets"].append(sheet_name)
            result["valid"] = False

    for sheet_name, cols in required_columns.items():
        if sheet_name not in sheet_names:
            continue
        try:
            df = _read_sheet(sheet_name)
            result["sheet_details"][sheet_name] = {
                "exists": True,
                "row_count": len(df),
                "columns": list(df.columns),
            }
            missing_cols = [col for col in cols if col not in df.columns]
            if missing_cols:
                result["missing_columns"][sheet_name] = missing_cols
                result["valid"] = False
        except Exception as e:
            result["sheet_details"][sheet_name] = {"exists": False, "error": str(e)}
            result["valid"] = False
    return result


@app.route('/api/health')
def health():
    exists = WORKBOOK.exists()
    mtime = datetime.fromtimestamp(WORKBOOK.stat().st_mtime).isoformat() if exists else None

    workbook_ok = False
    workbook_error = None
    schema_validation = {}
    if exists:
        try:
            import openpyxl
            wb = openpyxl.load_workbook(WORKBOOK, read_only=True, data_only=True)
            wb.close()
            workbook_ok = True
            schema_validation = _validate_workbook_schema()
        except Exception as e:
            workbook_error = str(e)

    active_run = _get_active_run_artifact()
    active_run_campaign_count = 0
    active_run_schedule_row_count = 0
    active_run_capacity_row_count = 0
    artifact_backed_bom_exists = False
    artifact_backed_material_exists = False
    if active_run:
        active_run_campaign_count = len(active_run.get("results", {}).get("campaigns", []))
        active_run_schedule_row_count = len(active_run.get("results", {}).get("heat_schedule", []))
        active_run_capacity_row_count = len(active_run.get("results", {}).get("capacity", []))
        artifact_backed_bom_exists = bool(active_run.get("results", {}).get("bom_explosion") is not None)
        artifact_backed_material_exists = bool(active_run.get("results", {}).get("material_plan") is not None)

    return _jsonify({
        "ok": exists and workbook_ok and schema_validation.get("valid", True),
        "api": "up",
        "workbook": str(WORKBOOK),
        "workbook_exists": exists,
        "workbook_ok": workbook_ok,
        "workbook_mtime": mtime,
        "workbook_error": workbook_error,
        "schema": schema_validation,
        "active_run_exists": bool(active_run),
        "active_run_id": active_run.get("run_id") if active_run else None,
        "active_run_created_at": active_run.get("created_at") if active_run else None,
        "active_run_campaign_count": active_run_campaign_count,
        "active_run_schedule_row_count": active_run_schedule_row_count,
        "active_run_capacity_row_count": active_run_capacity_row_count,
        "artifact_backed_reads": bool(active_run),
        "artifact_backed_bom_exists": artifact_backed_bom_exists,
        "artifact_backed_material_exists": artifact_backed_material_exists,
        "active_run_degraded_flags": active_run.get("degraded_flags") if active_run else None,
        "last_run": _state.get("last_run"),
        "run_id": _current_trace_id(),
        "solver_status": _active_run_solver_metadata().get("solver_status", "NOT RUN"),
        "solver_detail": _active_run_solver_metadata().get("solver_detail", ""),
    }, 200 if exists and workbook_ok else 500)


# ============================================================================
# Configuration Management API
# ============================================================================

@app.route('/api/config/algorithm', methods=['GET'])
def get_algorithm_config():
    """Get all algorithm configuration parameters."""
    config = get_config()
    params = config.all_params()
    metadata = config.metadata

    result = []
    for key, value in params.items():
        meta = metadata.get(key, {})
        result.append({
            'key': key,
            'value': value,
            'category': meta.get('category'),
            'data_type': meta.get('data_type'),
            'description': meta.get('description'),
            'min': meta.get('min'),
            'max': meta.get('max'),
        })

    return _jsonify({
        'status': 'success',
        'parameters': sorted(result, key=lambda x: x['category']),
        'total': len(result),
        'by_category': {
            cat: len([p for p in result if p['category'] == cat])
            for cat in set(p['category'] for p in result)
        }
    })


@app.route('/api/config/algorithm/<key>', methods=['GET'])
def get_algorithm_config_param(key):
    """Get single algorithm parameter."""
    config = get_config()
    value = config.get(key)

    if value is None:
        return _jsonify({
            'status': 'error',
            'error': f'Parameter {key} not found'
        }, 404)

    meta = config.metadata.get(key, {})
    return _jsonify({
        'status': 'success',
        'key': key,
        'value': value,
        'category': meta.get('category'),
        'data_type': meta.get('data_type'),
        'description': meta.get('description'),
        'min': meta.get('min'),
        'max': meta.get('max'),
    })


@app.route('/api/config/algorithm/category/<category>', methods=['GET'])
def get_algorithm_config_by_category(category):
    """Get all parameters in a category."""
    config = get_config()
    params = config.params_by_category(category.upper())

    if not params:
        return _jsonify({
            'status': 'error',
            'error': f'Category {category} not found or has no parameters'
        }, 404)

    metadata = config.metadata
    result = []
    for key, value in params.items():
        meta = metadata.get(key, {})
        result.append({
            'key': key,
            'value': value,
            'data_type': meta.get('data_type'),
            'description': meta.get('description'),
            'min': meta.get('min'),
            'max': meta.get('max'),
        })

    return _jsonify({
        'status': 'success',
        'category': category.upper(),
        'parameters': result,
        'total': len(result),
    })


@app.route('/api/config/algorithm/<key>', methods=['PUT'])
def update_algorithm_config_param(key):
    """Update a single algorithm parameter."""
    try:
        data = request.get_json() or {}
        new_value = data.get('value')
        user = data.get('user', 'API')
        reason = data.get('reason', '')

        if new_value is None:
            return _jsonify({
                'status': 'error',
                'error': 'Missing required field: value'
            }, 400)

        config = get_config()
        old_value = config.get(key)

        if old_value is None:
            return _jsonify({
                'status': 'error',
                'error': f'Parameter {key} not found'
            }, 404)

        # Validate and update
        if not config.update(key, new_value, user, reason):
            return _jsonify({
                'status': 'error',
                'error': f'Invalid value for {key}: {new_value}'
            }, 400)

        # TODO: In real implementation, would write back to Excel here
        return _jsonify({
            'status': 'success',
            'key': key,
            'old_value': old_value,
            'new_value': new_value,
            'user': user,
            'reason': reason,
            'timestamp': datetime.now().isoformat(),
        })

    except Exception as e:
        return _jsonify({
            'status': 'error',
            'error': str(e)
        }, 500)


@app.route('/api/config/algorithm/validate', methods=['POST'])
def validate_algorithm_config():
    """Pre-validate configuration changes before committing."""
    try:
        data = request.get_json() or {}
        changes = data.get('changes', {})

        if not changes:
            return _jsonify({
                'status': 'error',
                'error': 'Missing required field: changes'
            }, 400)

        config = get_config()
        metadata = config.metadata

        validations = {}
        all_valid = True

        for key, new_value in changes.items():
            current = config.get(key)
            meta = metadata.get(key, {})

            if current is None:
                validations[key] = {
                    'valid': False,
                    'error': f'Parameter {key} not found'
                }
                all_valid = False
                continue

            is_valid = config._validate_value(
                key, new_value, meta.get('data_type'),
                meta.get('min'), meta.get('max')
            )

            validations[key] = {
                'valid': is_valid,
                'current_value': current,
                'proposed_value': new_value,
                'data_type': meta.get('data_type'),
                'min': meta.get('min'),
                'max': meta.get('max'),
            }

            if not is_valid:
                all_valid = False

        return _jsonify({
            'status': 'success',
            'all_valid': all_valid,
            'validations': validations,
        })

    except Exception as e:
        return _jsonify({
            'status': 'error',
            'error': str(e)
        }, 500)


@app.route('/api/config/algorithm/export', methods=['POST'])
def export_algorithm_config():
    """Export current configuration to CSV."""
    try:
        config = get_config()
        params = config.all_params()
        metadata = config.metadata

        # Build CSV rows
        csv_lines = ['Config_Key,Category,Current_Value,Data_Type,Description']
        for key, value in params.items():
            meta = metadata.get(key, {})
            cat = meta.get('category', '')
            dtype = meta.get('data_type', '')
            desc = meta.get('description', '').replace(',', ';')  # Escape commas in description

            csv_lines.append(f'{key},{cat},{value},{dtype},"{desc}"')

        csv_content = '\n'.join(csv_lines)

        return _jsonify({
            'status': 'success',
            'timestamp': datetime.now().isoformat(),
            'parameters': len(params),
            'csv': csv_content,
        })

    except Exception as e:
        return _jsonify({
            'status': 'error',
            'error': str(e)
        }, 500)


@app.route('/api/data/dashboard')
def dashboard():
    return _jsonify(_dashboard_payload())


@app.route('/api/data/config')
def data_config():
    md = _masterdata_payload()
    return _jsonify({
        "config": md.get("config", []),
        "resources": md.get("resources", []),
        "routing": md.get("routing", []),
        "queue": md.get("queue", []),
        "skus": md.get("skus", []),
        "bom": md.get("bom", []),
        "inventory": md.get("inventory", []),
        "campaign_config": md.get("campaign-config", []),
        "changeover": md.get("changeover", []),
        "scenarios": md.get("scenarios", []),
    })


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
    return _jsonify({"run_id": _current_trace_id(), "campaigns": _campaign_rows()})


@app.route('/api/data/gantt')
def data_gantt():
    return _jsonify({"run_id": _current_trace_id(), "jobs": _schedule_rows()})


@app.route('/api/data/capacity')
def data_capacity():
    return _jsonify({"run_id": _current_trace_id(), "capacity": _capacity_rows()})


def _build_bom_grouped_view(
    netted_df: pd.DataFrame | None,
    skus_df: pd.DataFrame | None = None,
    bom_df: pd.DataFrame | None = None,
) -> tuple[list, dict]:
    """Build grouped BOM view (Plant → Material_Type hierarchy) from netted DataFrame.

    Returns:
        (grouped_bom: list of plant dicts, summary: dict with overall stats)
    """
    if netted_df is None or netted_df.empty:
        return [], {
            "total_sku_lines": 0,
            "short_lines": 0,
            "partial_lines": 0,
            "covered_lines": 0,
            "byproduct_lines": 0,
            "total_gross_req": 0.0,
            "total_net_req": 0.0,
            "total_covered_by_stock": 0.0,
            "total_produced_qty": 0.0,
        }

    # Build lookup maps for enrichment
    sku_name_lookup = {}
    sku_category_lookup = {}
    if skus_df is not None and not skus_df.empty:
        for _, row in skus_df.iterrows():
            sku_id = str(row.get("SKU_ID", "")).strip()
            if sku_id:
                sku_name_lookup[sku_id] = str(row.get("SKU_Name", sku_id)).strip()
                sku_category_lookup[sku_id] = str(row.get("Category", "")).strip()

    parent_sku_lookup = {}
    if bom_df is not None and not bom_df.empty:
        for _, row in bom_df.iterrows():
            child = str(row.get("Child_SKU", "")).strip()
            parent = str(row.get("Parent_SKU", "")).strip()
            if child and parent:
                if child in parent_sku_lookup:
                    if parent not in parent_sku_lookup[child]:
                        parent_sku_lookup[child] += f", {parent}"
                else:
                    parent_sku_lookup[child] = parent

    # Enrich each row
    enriched_rows = []
    for _, row in netted_df.iterrows():
        sku_id = str(row.get("SKU_ID", "")).strip()
        category = sku_category_lookup.get(sku_id, "")
        material_type = _mat_type_for_sku(sku_id, category)
        stage = _stage_for_sku(sku_id, material_type)
        plant = _plant_for_sku(sku_id, category, material_type)

        gross_req = float(row.get("Gross_Req", row.get("Required_Qty", 0)) or 0)
        net_req = float(row.get("Net_Req", 0) or 0)
        flow_type = str(row.get("Flow_Type", "INPUT")).strip().upper()
        available_before = float(row.get("Available", row.get("Available_Before", 0)) or 0)
        produced_qty = float(row.get("Produced_Qty", 0) or 0)

        # Determine if byproduct
        is_byproduct = flow_type in {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"}
        covered_by_stock = 0.0 if is_byproduct else max(gross_req - net_req, 0.0)

        # Derive status
        if is_byproduct:
            status = "BYPRODUCT"
        elif net_req <= 1e-9:
            status = "COVERED"
        elif available_before > 1e-9:
            status = "PARTIAL SHORT"
        else:
            status = "SHORT"

        enriched_rows.append({
            "plant": plant,
            "stage": stage,
            "material_type": material_type,
            "material_category": category,
            "parent_skus": parent_sku_lookup.get(sku_id, ""),
            "sku_id": sku_id,
            "sku_name": sku_name_lookup.get(sku_id, sku_id),
            "bom_level": int(row.get("BOM_Level", 0) or 0),
            "gross_req": round(gross_req, 3),
            "available_before": round(available_before, 3),
            "covered_by_stock": round(covered_by_stock, 3),
            "produced_qty": round(produced_qty, 3),
            "net_req": round(net_req, 3),
            "status": status,
            "flow_type": flow_type,
        })

    # Sort by plant, material_type, bom_level, sku_id
    enriched_rows.sort(
        key=lambda r: (
            _PLANT_SORT_ORDER.get(r["plant"], 99),
            _MAT_TYPE_SORT_ORDER.get(r["material_type"], 99),
            r["bom_level"],
            r["sku_id"],
        )
    )

    # Group into plant -> material_type -> rows
    grouped_bom = []
    plant_groups = {}
    for row in enriched_rows:
        plant = row["plant"]
        mat_type = row["material_type"]
        if plant not in plant_groups:
            plant_groups[plant] = {}
        if mat_type not in plant_groups[plant]:
            plant_groups[plant][mat_type] = []
        plant_groups[plant][mat_type].append(row)

    # Build grouped output
    for plant in sorted(plant_groups.keys(), key=lambda p: _PLANT_SORT_ORDER.get(p, 99)):
        plant_rows_all = []
        material_types = []

        for mat_type in sorted(
            plant_groups[plant].keys(), key=lambda m: _MAT_TYPE_SORT_ORDER.get(m, 99)
        ):
            rows = plant_groups[plant][mat_type]
            gross = sum(r["gross_req"] for r in rows)
            produced = sum(r["produced_qty"] for r in rows)
            net = sum(r["net_req"] for r in rows)

            material_types.append({
                "material_type": mat_type,
                "gross_req": round(gross, 3),
                "produced_qty": round(produced, 3),
                "net_req": round(net, 3),
                "row_count": len(rows),
                "rows": rows,
            })
            plant_rows_all.extend(rows)

        plant_gross = sum(r["gross_req"] for r in plant_rows_all)
        plant_produced = sum(r["produced_qty"] for r in plant_rows_all)
        plant_net = sum(r["net_req"] for r in plant_rows_all)

        grouped_bom.append({
            "plant": plant,
            "gross_req": round(plant_gross, 3),
            "produced_qty": round(plant_produced, 3),
            "net_req": round(plant_net, 3),
            "row_count": len(plant_rows_all),
            "material_types": material_types,
        })

    # Compute summary
    total_short = sum(1 for r in enriched_rows if r["status"] == "SHORT")
    total_partial = sum(1 for r in enriched_rows if r["status"] == "PARTIAL SHORT")
    total_covered = sum(1 for r in enriched_rows if r["status"] == "COVERED")
    total_byproduct = sum(1 for r in enriched_rows if r["status"] == "BYPRODUCT")
    total_gross = sum(r["gross_req"] for r in enriched_rows)
    total_net = sum(r["net_req"] for r in enriched_rows)
    total_covered_by_stock = sum(r["covered_by_stock"] for r in enriched_rows)
    total_produced = sum(r["produced_qty"] for r in enriched_rows)

    summary = {
        "total_sku_lines": len(enriched_rows),
        "short_lines": total_short,
        "partial_lines": total_partial,
        "covered_lines": total_covered,
        "byproduct_lines": total_byproduct,
        "total_gross_req": round(total_gross, 3),
        "total_net_req": round(total_net, 3),
        "total_covered_by_stock": round(total_covered_by_stock, 3),
        "total_produced_qty": round(total_produced, 3),
    }

    return grouped_bom, summary


@app.route('/api/run/bom', methods=['POST'])
def run_bom_api():
    try:
        d = _load_all()
        demand = consolidate_demand(d['sales_orders'])
        din = demand[['SKU_ID', 'Total_Qty']].rename(columns={'Total_Qty': 'Required_Qty'})
        explosion_result = explode_bom_details(din, d['bom'])
        gross_bom_df = explosion_result.get('exploded', pd.DataFrame())
        structure_errors = explosion_result.get('structure_errors', [])
        feasible = explosion_result.get('feasible', True)

        if not feasible and structure_errors:
            error_summary = "; ".join(f"{e.get('type')}: {e.get('path')}" for e in structure_errors[:3])
            return _error_response(
                error_message="BOM structure validation failed",
                error_code="BOM_STRUCTURE_ERROR",
                error_domain="BOM",
                details=error_summary,
                degraded_mode=False,
                status_code=400,
            )[0]

        netted = net_requirements(gross_bom_df, d['inventory'])

        # Build grouped view
        grouped_bom_data, bom_summary = _build_bom_grouped_view(
            netted, d.get("skus"), d.get("bom")
        )

        # Build BOM payload (additive: includes grouped_bom + summary)
        bom_payload = {
            "gross_bom": _df_to_records(gross_bom_df),
            "net_bom": _df_to_records(netted),
            "grouped_bom": grouped_bom_data,
            "summary": bom_summary,
            "structure_errors": structure_errors,
            "feasible": feasible,
            "rows": len(netted),
            "run_id": _current_trace_id(),
        }

        # Persist to active run artifact
        artifact = _get_active_run_artifact()
        if artifact is not None:
            artifact.setdefault("results", {})["bom_explosion"] = bom_payload

        # Mirror to compatibility cache
        _state["bom_explosion"] = bom_payload

        return _jsonify(bom_payload)
    except Exception as e:
        return _error_response(
            error_message=f"BOM explosion failed: {str(e)}",
            error_code="BOM_EXPLOSION_ERROR",
            error_domain="BOM",
            details=traceback.format_exc(),
            status_code=500,
        )[0]


def _calculate_material_plan(campaigns: List[Dict], detail_level: str = "campaign", run_id: str | None = None, skus: list | None = None) -> Dict[str, Any]:
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
            shortages = camp.get('material_shortages', {}) or {}
            material_consumed = camp.get('material_consumed', {}) or {}
            material_gross_requirements = camp.get('material_gross_requirements', {}) or {}
            structure_errors = camp.get('material_structure_errors', []) or []
            inventory_before = camp.get('inventory_before', {}) or {}
            inventory_after = camp.get('inventory_after', {}) or {}
            feasible = not structure_errors and not shortages

            shortage_qty = sum(float(v or 0) for v in shortages.values())

            # Build detailed material rows grouped by plant
            material_rows_by_plant = {}  # plant -> list of rows
            make_convert_qty = 0.0

            # Union of all SKUs mentioned in any of the material dicts
            all_skus = set(material_gross_requirements.keys()) | set(material_consumed.keys()) | set(shortages.keys())

            for sku_id in sorted(all_skus):
                material_type = _mat_type_for_sku(sku_id)
                plant = _plant_for_sku(sku_id, material_type=material_type)

                required = float(material_gross_requirements.get(sku_id, 0) or 0)
                consumed = float(material_consumed.get(sku_id, 0) or 0)
                shortage = float(shortages.get(sku_id, 0) or 0)
                available_before = float(inventory_before.get(sku_id, 0) or 0)
                remaining_after = float(inventory_after.get(sku_id, 0) or 0)

                # Derive status: SHORTAGE > PARTIAL COVER > DRAWN/MAKE
                if shortage > 1e-6:
                    status = "SHORTAGE"
                elif consumed < required - 1e-6 and shortage < 1e-6:
                    status = "PARTIAL COVER"
                elif available_before > 1e-6 and consumed <= available_before + 1e-6:
                    status = "DRAWN FROM STOCK"
                else:
                    status = "MAKE / CONVERT"
                    make_convert_qty += consumed

                row = {
                    "material_type": material_type,
                    "material_sku": sku_id,
                    "material_name": sku_id,  # Could enhance with SKU lookup if skus param provided
                    "required_qty": round(required, 2),
                    "available_before": round(available_before, 2),
                    "consumed": round(consumed, 2),
                    "remaining_after": round(remaining_after, 2),
                    "status": status,
                }

                if plant not in material_rows_by_plant:
                    material_rows_by_plant[plant] = []
                material_rows_by_plant[plant].append(row)

            # Build plants array sorted by plant name
            plants = []
            for plant_name in sorted(material_rows_by_plant.keys()):
                plant_rows = material_rows_by_plant[plant_name]
                plant_required = sum(r.get('required_qty', 0) for r in plant_rows)
                plant_inventory_covered = sum(min(r.get('required_qty', 0), r.get('available_before', 0)) for r in plant_rows)

                plants.append({
                    "plant": plant_name,
                    "required_qty": round(plant_required, 2),
                    "inventory_covered_qty": round(plant_inventory_covered, 2),
                    "rows": plant_rows,
                })

            # Keep detail_rows for backward compat (simple shape)
            detail_rows = []
            for sku_id, shortage_amount in shortages.items():
                if shortage_amount > 1e-6:
                    detail_rows.append({
                        "sku_id": sku_id,
                        "shortage_qty": round(float(shortage_amount), 2),
                        "type": "SHORTAGE",
                    })
            for sku_id, consumed_amount in material_consumed.items():
                if consumed_amount > 1e-6:
                    detail_rows.append({
                        "sku_id": sku_id,
                        "consumed_qty": round(float(consumed_amount), 2),
                        "type": "CONSUMED",
                    })

            camp_data = {
                "campaign_id": camp_id,
                "grade": grade,
                "release_status": release_status,
                "required_qty": round(req_qty, 2),
                "shortage_qty": round(shortage_qty, 2),
                "material_status": material_status,
                "feasible": feasible,
                "material_structure_errors": structure_errors,
                "material_shortages": shortages,
                "material_consumed": material_consumed,
                "material_gross_requirements": material_gross_requirements,
                "inventory_before": inventory_before,
                "inventory_after": inventory_after,
                "plants": plants,
                "detail_rows": detail_rows,
            }
            campaigns_data.append(camp_data)

        total_required = sum(c.get('required_qty', 0) for c in campaigns_data)
        total_shortages = sum(c.get('shortage_qty', 0) for c in campaigns_data)
        total_covered = total_required - total_shortages
        total_make_convert = float(sum(sum(r.get('consumed', 0) for r in plant.get('rows', []) if plant['rows'] and r.get('status') == 'MAKE / CONVERT') for c in campaigns_data for plant in c.get('plants', [])))
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
            "Make / Convert Qty": round(total_make_convert, 2),
        }

        return {
            "campaigns": campaigns_data,
            "detail_level": detail_level,
            "summary": summary,
            "run_id": run_id,
        }
    except Exception:
        return {"campaigns": [], "detail_level": "campaign", "summary": {}, "run_id": run_id}


def _store_compatibility_cache(
    *,
    run_id: str,
    campaigns: List[Dict[str, Any]],
    heat_df: pd.DataFrame,
    camp_df: pd.DataFrame,
    cap_df: pd.DataFrame,
    material_payload: Dict[str, Any],
    solver: str,
    solver_detail: str,
) -> None:
    _state['last_run'] = datetime.now().isoformat()
    _state['run_id'] = run_id
    _state['campaigns'] = campaigns
    _state['heat_schedule'] = heat_df
    _state['camp_schedule'] = camp_df
    _state['capacity'] = cap_df
    _state['material_plan_data'] = material_payload
    _state['solver_status'] = solver
    _state['solver_detail'] = solver_detail
    _state['error'] = None


@app.route('/api/run/schedule', methods=['POST'])
def run_schedule():
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

        campaigns = build_campaigns(
            d['sales_orders'],
            min_campaign_mt=min_cmt,
            max_campaign_mt=max_cmt,
            inventory=d['inventory'],
            bom=d['bom'],
            config=config,
            skus=d['skus'],
        )
        released = [c for c in campaigns if str(c.get('release_status', '')).upper() == 'RELEASED']

        result = schedule(
            campaigns,
            d['resources'],
            planning_horizon_days=horizon,
            planning_start=datetime.now(),
            routing=d['routing'],
            queue_times=d['queue_times'],
            config=config,
            solver_time_limit_sec=sec_lim,
        )
        heat_df = result.get('heat_schedule', pd.DataFrame())
        camp_df = result.get('campaign_schedule', pd.DataFrame())
        solver = result.get('solver_status', 'UNKNOWN')
        s_detail = result.get('solver_detail', 'CP_SAT_NA')

        allow_defaults = bool(result.get('allow_default_masters', False)) or _config_flag(config, 'Allow_Scheduler_Default_Masters', 'N')
        warnings: List[str] = []
        degraded_flags = {
            "default_masters_used": allow_defaults,
            "greedy_fallback": solver in {"GREEDY", "GREEDY_FALLBACK", "UNKNOWN"},
            "material_incomplete": False,
            "inventory_lineage_degraded": False,
            "capacity_unavailable": False,
        }

        try:
            demand_hrs = compute_demand_hours(
                released,
                d['resources'],
                routing=d['routing'],
                allow_defaults=allow_defaults,
            )
            cap_df = capacity_map(demand_hrs, d['resources'], horizon_days=horizon)
        except Exception as cap_err:
            cap_df = pd.DataFrame()
            degraded_flags["capacity_unavailable"] = True
            warnings.append(f"Capacity map failed: {cap_err}")

        material_payload = _calculate_material_plan(campaigns, detail_level="campaign", run_id=None)
        run_id = _create_run_artifact(
            config=config,
            campaigns=campaigns,
            heat_schedule=heat_df,
            campaign_schedule=camp_df,
            capacity_map_df=cap_df,
            material_plan={**material_payload, "run_id": None},
            solver_status=solver,
            solver_detail=s_detail,
            warnings=warnings,
            degraded_flags=degraded_flags,
        )
        _set_active_run_artifact(run_id)

        material_payload = _calculate_material_plan(campaigns, detail_level="campaign", run_id=run_id)
        _run_artifacts[run_id]["results"]["material_plan"] = material_payload
        _store_compatibility_cache(
            run_id=run_id,
            campaigns=campaigns,
            heat_df=heat_df,
            camp_df=camp_df,
            cap_df=cap_df,
            material_payload=material_payload,
            solver=solver,
            solver_detail=s_detail,
        )

        response_payload = {
            **_dashboard_payload(),
            "campaigns": _campaigns_to_view(campaigns, camp_df),
            "gantt": _df_to_records(heat_df),
            "capacity": _df_to_records(cap_df),
            "material": material_payload,
            "run_id": run_id,
        }
        return _jsonify(response_payload)
    except Exception as e:
        _state['error'] = str(e)
        return _error_response(
            error_message=f"Scheduling failed: {str(e)}",
            error_code="SCHEDULE_ERROR",
            error_domain="SCHEDULER",
            details=traceback.format_exc(),
            degraded_mode=False,
            status_code=500,
        )[0]


@app.route('/api/run/ctp', methods=['POST'])
def run_ctp_api():
    try:
        body = request.get_json(silent=True) or {}
        sku_id = str(body.get('sku_id', '')).strip()
        qty_mt = float(body.get('qty_mt', 100.0))
        requested_date = body.get('requested_date', (datetime.now() + pd.Timedelta(days=14)).isoformat())

        d = _load_all()
        config = d['config']
        min_cmt = float(config.get('Min_Campaign_MT', 100.0) or 100.0)
        max_cmt = float(config.get('Max_Campaign_MT', 500.0) or 500.0)

        campaigns = _active_run_campaigns() or build_campaigns(
            d['sales_orders'],
            min_cmt,
            max_cmt,
            inventory=d['inventory'],
            bom=d['bom'],
            config=config,
            skus=d['skus'],
        )
        schedule_df = _active_run_heat_schedule()
        frozen_jobs = _frozen_jobs_from_schedule_dataframe(schedule_df)

        res = capable_to_promise(
            sku_id=sku_id,
            qty_mt=qty_mt,
            requested_date=requested_date,
            campaigns=campaigns,
            resources=d['resources'],
            bom=d['bom'],
            inventory=d['inventory'],
            routing=d['routing'],
            skus=d['skus'],
            planning_start=datetime.now(),
            config=config,
            queue_times=d['queue_times'],
            frozen_jobs=frozen_jobs,
        )
        return _jsonify({k: _safe(v) for k, v in res.items()})
    except Exception as e:
        return _error_response(
            error_message=f"CTP evaluation failed: {str(e)}",
            error_code="CTP_ERROR",
            error_domain="CTP",
            details=traceback.format_exc(),
            status_code=500,
        )[0]


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


@app.route('/api/aps/dashboard/overview')
def aps_dashboard_overview():
    dashboard = _dashboard_payload()
    campaigns = _campaign_rows()
    return _jsonify({
        "summary": dashboard,
        "campaigns": campaigns[:8],
        "alerts": dashboard.get('shortage_alerts', [])[:10],
        "utilisation": dashboard.get('utilisation', [])[:8],
        "release_queue": sorted(campaigns, key=lambda x: str(x.get('Due_Date') or x.get('due_date') or '9999-12-31'))[:8],
    })


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
    return _jsonify({"run_id": _current_trace_id(), "items": items, "total": len(items)})


@app.route('/api/aps/campaigns/release-queue')
def aps_campaign_release_queue():
    items = sorted(_campaign_rows(), key=lambda x: str(x.get('Due_Date') or x.get('due_date') or '9999-12-31'))
    return _jsonify({"run_id": _current_trace_id(), "items": items})


@app.route('/api/aps/campaigns/<campaign_id>')
def aps_campaign_item(campaign_id):
    for item in _campaign_rows():
        cid = str(item.get('campaign_id') or item.get('Campaign_ID') or '')
        if cid == campaign_id:
            return _jsonify({"run_id": _current_trace_id(), "item": item})
    return _jsonify({"error": 'Campaign not found'}, 404)


@app.route('/api/aps/campaigns/<campaign_id>/status', methods=['PATCH'])
def aps_campaign_status_update(campaign_id):
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
    return _jsonify({"run_id": _current_trace_id(), "jobs": _schedule_rows()})


@app.route('/api/aps/schedule/run', methods=['POST'])
def aps_schedule_run():
    return run_schedule_api()


@app.route('/api/aps/schedule/jobs/<job_id>')
def aps_schedule_job_item(job_id):
    for item in _schedule_rows():
        jid = str(item.get('Job_ID') or item.get('job_id') or '')
        if jid == job_id:
            return _jsonify({"run_id": _current_trace_id(), "item": item})
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
    return _jsonify({"run_id": _current_trace_id(), "resources": _dispatch_rows()})


@app.route('/api/aps/dispatch/resources/<resource_id>')
def aps_dispatch_resource_item(resource_id):
    items = _dispatch_rows()
    for item in items:
        rid = str(item.get('resource_id') or item.get('Resource_ID') or '')
        if rid == resource_id:
            return _jsonify({"run_id": _current_trace_id(), "item": item})
    return _jsonify({"error": 'Resource not found'}, 404)


@app.route('/api/aps/capacity/map')
def aps_capacity_map():
    return _jsonify({"run_id": _current_trace_id(), "items": _capacity_rows()})


@app.route('/api/aps/capacity/bottlenecks')
def aps_capacity_bottlenecks():
    items = sorted(_capacity_rows(), key=lambda x: float(x.get('Utilisation_%') or 0), reverse=True)
    return _jsonify({"run_id": _current_trace_id(), "items": items[:10]})


@app.route('/api/aps/material/plan')
def aps_material_plan():
    return _jsonify(_material_plan_payload())


@app.route('/api/aps/material/holds')
def aps_material_holds():
    items = _material_holds_payload()
    return _jsonify({"run_id": _current_trace_id(), "items": items, "total": len(items)})


@app.route('/api/aps/bom/explosion')
def aps_bom_explosion():
    payload = _active_run_bom()
    if payload is None:
        return _error_response(
            error_message="No BOM explosion data available. Run POST /api/run/bom first.",
            error_code="BOM_NOT_RUN",
            error_domain="BOM",
            status_code=404,
        )[0]
    return _jsonify(payload)


@app.route('/api/aps/clear-outputs', methods=['POST'])
def aps_clear_outputs():
    try:
        _state['campaigns'] = []
        _state['heat_schedule'] = pd.DataFrame()
        _state['camp_schedule'] = pd.DataFrame()
        _state['capacity'] = pd.DataFrame()
        _state['material_plan_data'] = None
        _state['bom_explosion'] = None
        _state['last_run'] = None
        _state['run_id'] = None
        _state['solver_status'] = 'CLEARED'
        _state['solver_detail'] = ''
        _state['error'] = None

        global _active_run_id
        _active_run_id = None
        _run_artifacts.clear()

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
    return _jsonify({
        "ok": True,
        'applied_scenario': scenario,
        'note': 'Workbook-backed scenario selection acknowledged. Use /api/aps/schedule/run to recompute the live plan.',
    })


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
        'legacy': [
            '/api/health', '/api/data/dashboard', '/api/data/config', '/api/data/orders', '/api/data/skus',
            '/api/data/campaigns', '/api/data/gantt', '/api/data/capacity', '/api/run/bom', '/api/run/schedule',
            '/api/run/ctp', '/api/orders', '/api/orders/<so_id>', '/api/orders/assign'
        ],
        'application': [
            '/api/aps/dashboard/overview', '/api/aps/orders/list', '/api/aps/orders', '/api/aps/orders/<so_id>',
            '/api/aps/orders/assign', '/api/aps/campaigns/list', '/api/aps/campaigns/release-queue',
            '/api/aps/campaigns/<campaign_id>', '/api/aps/campaigns/<campaign_id>/status',
            '/api/aps/schedule/gantt', '/api/aps/schedule/run', '/api/aps/schedule/jobs/<job_id>',
            '/api/aps/schedule/jobs/<job_id>/reschedule', '/api/aps/dispatch/board',
            '/api/aps/dispatch/resources/<resource_id>', '/api/aps/capacity/map',
            '/api/aps/capacity/bottlenecks', '/api/aps/material/plan', '/api/aps/material/holds',
            '/api/aps/ctp/check', '/api/aps/ctp/requests', '/api/aps/ctp/output', '/api/aps/scenarios/list',
            '/api/aps/scenarios', '/api/aps/scenarios/<key_value>', '/api/aps/scenarios/output',
            '/api/aps/scenarios/apply', '/api/aps/masterdata', '/api/aps/masterdata/<section>',
            '/api/aps/masterdata/<section>/<key_value>', '/api/aps/masterdata/<section>/bulk-replace'
        ],
    })


if __name__ == '__main__':
    print(f'\n  X-APS Application API -> http://localhost:{PORT}')
    print(f'  Workbook              -> {WORKBOOK}\n')
    app.run(host='0.0.0.0', port=PORT, debug=False, use_reloader=False)
