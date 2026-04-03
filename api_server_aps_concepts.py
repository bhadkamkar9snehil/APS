"""
APS Flask API Server - Concept-Oriented Edition
-----------------------------------------------
This server exposes APS concepts for the HTML application instead of exposing
workbook sheets directly as the primary contract. The Excel workbook remains
only the persistence/source layer.

Goals:
- preserve existing route names where the frontend already depends on them
- expose new APS-domain routes aligned to dashboard, orders, campaigns,
  schedule, dispatch, material, capacity, scenarios, CTP, and master data
- keep CRUD available where it makes sense, but behind concept-oriented APIs

Run:
    python api_server_aps_concepts.py
"""
from __future__ import annotations

import json
import os
import sys
import traceback
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
from engine.ctp import capable_to_promise
from engine.scheduler import schedule
from engine.excel_store import ExcelStore
from engine.workbook_schema import SHEETS

# ── Config ──────────────────────────────────────────────────────────────────
WORKBOOK = Path(os.getenv("WORKBOOK_PATH", str(Path(__file__).parent / "APS_BF_SMS_RM.xlsx")))
HEADER_ROW = 2
PORT = int(os.getenv("PORT", "5000"))

app = Flask(__name__)
CORS(app, origins=["http://localhost:3131", "http://127.0.0.1:3131", "*"])
store = ExcelStore(WORKBOOK)


# ── JSON helpers ─────────────────────────────────────────────────────────────
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


def _df_to_records(df: pd.DataFrame) -> list:
    if df is None or df.empty:
        return []
    return [{k: _safe(v) for k, v in row.items()} for _, row in df.iterrows()]


def _jsonify(data, status: int = 200):
    return app.response_class(json.dumps(data, cls=_Enc, ensure_ascii=False), status=status, mimetype="application/json")


# ── Workbook loaders for engine-backed scheduling ───────────────────────────
def _read_sheet(sheet: str, required: list | None = None) -> pd.DataFrame:
    df = pd.read_excel(WORKBOOK, sheet_name=sheet, header=HEADER_ROW, dtype=str)
    df = df.dropna(how="all").reset_index(drop=True)
    if required:
        df = df.dropna(subset=[c for c in required if c in df.columns], how="all")
    return df


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
                out[k] = {
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


# ── Shared run-state ─────────────────────────────────────────────────────────
_state: dict = {
    "last_run": None,
    "campaigns": [],
    "heat_schedule": pd.DataFrame(),
    "camp_schedule": pd.DataFrame(),
    "capacity": pd.DataFrame(),
    "solver_status": "NOT RUN",
    "solver_detail": "",
    "error": None,
}


# ── APS domain helpers ───────────────────────────────────────────────────────
def _read_table(api_name: str, **kwargs) -> Dict[str, Any]:
    return store.list_rows(api_name, **kwargs)


def _list_items(api_name: str, **kwargs) -> List[Dict[str, Any]]:
    return _read_table(api_name, **kwargs)["items"]


def _campaigns_from_outputs() -> List[Dict[str, Any]]:
    if _state["campaigns"]:
        return _camps_to_json(_state["campaigns"], _state["camp_schedule"])
    if "campaign-schedule" in SHEETS:
        return _list_items("campaign-schedule")
    return []


def _schedule_jobs_from_outputs() -> List[Dict[str, Any]]:
    if _state["heat_schedule"] is not None and not _state["heat_schedule"].empty:
        return _df_to_records(_state["heat_schedule"])
    if "schedule-output" in SHEETS:
        return _list_items("schedule-output")
    return []


def _capacity_from_outputs() -> List[Dict[str, Any]]:
    if _state["capacity"] is not None and not _state["capacity"].empty:
        return _df_to_records(_state["capacity"])
    if "capacity-map" in SHEETS:
        return _list_items("capacity-map")
    return []


def _material_from_outputs() -> List[Dict[str, Any]]:
    if "material-plan" in SHEETS:
        return _list_items("material-plan")
    return []


def _dispatch_from_schedule() -> List[Dict[str, Any]]:
    jobs = _schedule_jobs_from_outputs()
    grouped: Dict[str, Dict[str, Any]] = {}
    for job in jobs:
        rid = str(job.get("Resource_ID") or job.get("resource_id") or "UNKNOWN")
        bucket = grouped.setdefault(rid, {"resource_id": rid, "jobs": [], "job_count": 0, "total_mt": 0.0})
        bucket["jobs"].append(job)
        bucket["job_count"] += 1
        try:
            bucket["total_mt"] += float(job.get("Qty_MT") or job.get("qty_mt") or 0)
        except Exception:
            pass
    return list(grouped.values())


def _master_data_payload() -> Dict[str, Any]:
    payload: Dict[str, Any] = {}
    mapping = {
        "config": "config",
        "resources": "resource-master",
        "routing": "routing",
        "queue": "queue-times",
        "skus": "sku-master",
        "bom": "bom",
        "inventory": "inventory",
        "campaign_config": "campaign-config",
        "changeover": "changeover-matrix",
        "scenarios": "scenarios",
    }
    for key, api_name in mapping.items():
        if api_name in SHEETS:
            payload[key] = _list_items(api_name)
        else:
            payload[key] = []
    return payload


def _kpis_from_state() -> dict:
    campaigns = _state["campaigns"]
    camp_df = _state["camp_schedule"]
    cap_df = _state["capacity"]

    if not campaigns:
        material_rows = _material_from_outputs()
        schedule_campaigns = _campaigns_from_outputs()
        capacity_rows = _capacity_from_outputs()
        total_mt = sum(float(x.get("Total_MT") or x.get("total_mt") or 0) for x in schedule_campaigns)
        total_heats = sum(float(x.get("Heats") or x.get("heats") or 0) for x in schedule_campaigns)
        released = sum(1 for x in schedule_campaigns if str(x.get("Release_Status") or x.get("release_status") or "").upper() == "RELEASED")
        held = sum(1 for x in schedule_campaigns if "HOLD" in str(x.get("Release_Status") or x.get("release_status") or "").upper())
        late = sum(1 for x in schedule_campaigns if str(x.get("Status") or "").upper() == "LATE")
        bottleneck_row = max(capacity_rows, key=lambda x: float(x.get("Utilisation_%") or 0)) if capacity_rows else None
        util_list = [{"resource_id": r.get("Resource_ID"), "resource_name": r.get("Resource_Name", r.get("Resource_ID")), "utilisation": r.get("Utilisation_%"), "demand_hrs": r.get("Demand_Hrs"), "avail_hrs": r.get("Avail_Hrs_14d"), "status": r.get("Status"), "operation": r.get("Operation_Group", "")} for r in capacity_rows]
        shortage_alerts = [{"campaign_id": r.get("Campaign_ID"), "sku_id": r.get("Material_SKU", r.get("SKU_ID")), "shortage_qty": r.get("Required_Qty", r.get("Shortage_Qty")), "severity": "HIGH" if str(r.get("Status", "")).upper() in {"SHORT", "CRITICAL", "BLOCKED"} else "MEDIUM"} for r in material_rows if str(r.get("Status", "")).upper() not in {"OK", "AVAILABLE", "COVERED", ""}]
        return {"solver_status": _state["solver_status"] or "WORKBOOK", "last_run": _state["last_run"], "campaigns_total": len(schedule_campaigns), "campaigns_released": released, "campaigns_held": held, "campaigns_late": late, "total_heats": total_heats, "total_mt": round(total_mt, 1), "on_time_pct": round(100 * max(0, len(schedule_campaigns) - late) / max(len(schedule_campaigns), 1), 1) if schedule_campaigns else 0.0, "throughput_mt_day": round(total_mt / 14, 1) if total_mt else 0.0, "bottleneck": bottleneck_row.get("Resource_ID") if bottleneck_row else None, "max_utilisation": bottleneck_row.get("Utilisation_%") if bottleneck_row else 0.0, "utilisation": util_list, "shortage_alerts": shortage_alerts}

    released = [c for c in campaigns if str(c.get("release_status", "")).upper() == "RELEASED"]
    held = [c for c in campaigns if str(c.get("release_status", "")).upper() == "MATERIAL HOLD"]
    total_mt = sum(float(c.get("total_coil_mt", 0) or 0) for c in campaigns)
    total_heats = sum(int(c.get("heats", 0) or 0) for c in campaigns)

    late_count = 0
    if camp_df is not None and not camp_df.empty and "Status" in camp_df.columns:
        late_count = int((camp_df["Status"].astype(str).str.upper() == "LATE").sum())

    on_time_pct = round(100 * max(0, len(campaigns) - late_count) / max(len(campaigns), 1), 1) if campaigns else 0.0
    throughput = round(total_mt / 14, 1) if total_mt else 0.0

    bottleneck = None
    max_util = 0.0
    if cap_df is not None and not cap_df.empty and "Utilisation_%" in cap_df.columns:
        non_bf = cap_df[~cap_df.get("Resource_ID", pd.Series("")) .astype(str).str.startswith("BF")]
        if not non_bf.empty:
            idx = non_bf["Utilisation_%"].idxmax()
            bottleneck = str(non_bf.loc[idx, "Resource_ID"])
            max_util = round(float(non_bf.loc[idx, "Utilisation_%"]), 1)

    util_list = []
    if cap_df is not None and not cap_df.empty:
        for _, r in cap_df.iterrows():
            util_list.append({"resource_id": _safe(r.get("Resource_ID")), "resource_name": _safe(r.get("Resource_Name", r.get("Resource_ID"))), "utilisation": _safe(r.get("Utilisation_%")), "demand_hrs": _safe(r.get("Demand_Hrs")), "avail_hrs": _safe(r.get("Avail_Hrs_14d", r.get("Avail_Hours_Day", 20) or 20)), "status": _safe(r.get("Status")), "operation": _safe(r.get("Operation_Group", ""))})

    shortage_alerts = []
    for c in held:
        for sku, qty in (c.get("material_shortages") or {}).items():
            shortage_alerts.append({"campaign_id": str(c.get("campaign_id", "")), "sku_id": str(sku), "shortage_qty": round(float(qty or 0), 2), "severity": "HIGH" if float(qty or 0) > 20 else "MEDIUM"})

    return {"solver_status": _state["solver_status"], "last_run": _state["last_run"], "campaigns_total": len(campaigns), "campaigns_released": len(released), "campaigns_held": len(held), "campaigns_late": late_count, "total_heats": total_heats, "total_mt": round(total_mt, 1), "on_time_pct": on_time_pct, "throughput_mt_day": throughput, "bottleneck": bottleneck, "max_utilisation": max_util, "utilisation": util_list, "shortage_alerts": shortage_alerts}


def _camps_to_json(campaigns: list, camp_df: pd.DataFrame | None) -> list:
    result = []
    for c in campaigns:
        cid = str(c.get("campaign_id", ""))
        sched = {}
        if camp_df is not None and not camp_df.empty and "Campaign_ID" in camp_df.columns:
            match = camp_df[camp_df["Campaign_ID"].astype(str) == cid]
            if not match.empty:
                sched = {k: _safe(v) for k, v in match.iloc[0].to_dict().items()}
        shortages = [{"sku_id": k, "qty": round(float(v or 0), 2)} for k, v in (c.get("material_shortages") or {}).items()]
        result.append({"campaign_id": cid, "campaign_group": str(c.get("campaign_group", "")), "grade": str(c.get("grade", "")), "heats": int(c.get("heats", 0) or 0), "total_mt": round(float(c.get("total_coil_mt", 0) or 0), 1), "release_status": str(c.get("release_status", "HELD")), "needs_vd": bool(c.get("needs_vd", False)), "billet_family": str(c.get("billet_family", "")), "shortages": shortages, "so_list": [str(s) for s in (c.get("so_list") or [])], **sched})
    return result


# ── Existing route names preserved ───────────────────────────────────────────
@app.route("/api/health")
def health():
    exists = WORKBOOK.exists()
    mtime = datetime.fromtimestamp(WORKBOOK.stat().st_mtime).isoformat() if exists else None
    return _jsonify({"ok": exists, "workbook": str(WORKBOOK), "workbook_mtime": mtime, "last_run": _state["last_run"], "solver_status": _state["solver_status"] or "WORKBOOK", "solver_detail": _state.get("solver_detail", "")})


@app.route("/api/data/dashboard")
def dashboard():
    return _jsonify(_kpis_from_state())


@app.route("/api/data/config")
def data_config():
    md = _master_data_payload()
    return _jsonify({"config": md["config"], "resources": md["resources"], "skus": md["skus"], "routing": md["routing"], "queue": md["queue"], "campaign_config": md["campaign_config"], "changeover": md["changeover"]})


@app.route("/api/data/orders")
def data_orders():
    d = _load_all()
    return _jsonify({"orders": _df_to_records(d["all_orders"])})


@app.route("/api/data/skus")
def data_skus():
    d = _load_all()
    skus = d["skus"]
    fg = skus[skus.get("Category", pd.Series("")) .astype(str).str.contains("Finished", case=False, na=False)]
    records = [{"sku_id": r.get("SKU_ID", ""), "sku_name": r.get("SKU_Name", "")} for _, r in fg.iterrows()]
    return _jsonify({"skus": records})


@app.route("/api/data/campaigns")
def data_campaigns():
    return _jsonify({"campaigns": _campaigns_from_outputs()})


@app.route("/api/data/gantt")
def data_gantt():
    return _jsonify({"jobs": _schedule_jobs_from_outputs()})


@app.route("/api/data/capacity")
def data_capacity():
    return _jsonify({"capacity": _capacity_from_outputs()})


@app.route("/api/run/bom", methods=["POST"])
def run_bom_api():
    try:
        d = _load_all()
        demand = consolidate_demand(d["sales_orders"])
        din = demand[["SKU_ID", "Total_Qty"]].rename(columns={"Total_Qty": "Required_Qty"})
        gross = explode_bom_details(din, d["bom"])
        netted = net_requirements(gross, d["inventory"])
        return _jsonify({"bom": _df_to_records(netted), "rows": len(netted)})
    except Exception as e:
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route("/api/run/schedule", methods=["POST"])
def run_schedule_api():
    try:
        d = _load_all()
        config = d["config"]
        min_cmt = float(config.get("Min_Campaign_MT", 100.0) or 100.0)
        max_cmt = float(config.get("Max_Campaign_MT", 500.0) or 500.0)
        horizon = int(float(config.get("Planning_Horizon_Days", 14) or 14))
        sec_lim = float(config.get("Default_Solver_Limit_Sec", 30.0) or 30.0)
        body = request.get_json(silent=True) or {}
        horizon = int(body.get("horizon", horizon))
        sec_lim = float(body.get("solver_sec", body.get("time_limit", sec_lim)))
        campaigns = build_campaigns(d["sales_orders"], min_campaign_mt=min_cmt, max_campaign_mt=max_cmt, inventory=d["inventory"], bom=d["bom"], config=config, skus=d["skus"])
        released = [c for c in campaigns if str(c.get("release_status", "")).upper() == "RELEASED"]
        result = schedule(campaigns, d["resources"], planning_horizon_days=horizon, planning_start=datetime.now(), routing=d["routing"], queue_times=d["queue_times"], config=config, solver_time_limit_sec=sec_lim)
        heat_df = result.get("heat_schedule", pd.DataFrame())
        camp_df = result.get("campaign_schedule", pd.DataFrame())
        solver = result.get("solver_status", "UNKNOWN")
        s_detail = result.get("solver_detail", "CP_SAT_NA")
        demand_hrs = compute_demand_hours(released, d["resources"], routing=d["routing"])
        cap_df = capacity_map(demand_hrs, d["resources"], horizon_days=horizon)
        _state["last_run"] = datetime.now().isoformat()
        _state["campaigns"] = campaigns
        _state["heat_schedule"] = heat_df
        _state["camp_schedule"] = camp_df
        _state["capacity"] = cap_df
        _state["solver_status"] = solver
        _state["solver_detail"] = s_detail
        _state["error"] = None
        kpis = _kpis_from_state()
        return _jsonify({**kpis, "campaigns": _camps_to_json(campaigns, camp_df), "gantt": _df_to_records(heat_df), "capacity": _df_to_records(cap_df), "solver_detail": s_detail})
    except Exception as e:
        _state["error"] = str(e)
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route("/api/run/ctp", methods=["POST"])
def run_ctp_api():
    try:
        body = request.get_json(silent=True) or {}
        sku_id = str(body.get("sku_id", ""))
        qty_mt = float(body.get("qty_mt", 100.0))
        requested_date = body.get("requested_date", (datetime.now() + pd.Timedelta(days=14)).isoformat())
        d = _load_all()
        config = d["config"]
        min_cmt = float(config.get("Min_Campaign_MT", 100.0) or 100.0)
        max_cmt = float(config.get("Max_Campaign_MT", 500.0) or 500.0)
        campaigns = _state["campaigns"] or build_campaigns(d["sales_orders"], min_cmt, max_cmt, inventory=d["inventory"], bom=d["bom"], config=config, skus=d["skus"])
        res = capable_to_promise(sku_id=sku_id, qty_mt=qty_mt, requested_date=requested_date, campaigns=campaigns, resources=d["resources"], bom=d["bom"], inventory=d["inventory"], routing=d["routing"], skus=d["skus"], planning_start=datetime.now(), config=config, queue_times=d["queue_times"])
        return _jsonify({k: _safe(v) for k, v in res.items()})
    except Exception as e:
        return _jsonify({"error": str(e), "trace": traceback.format_exc()}, 500)


@app.route("/api/orders/assign", methods=["POST"])
def orders_assign():
    payload = request.get_json(silent=True) or {}
    assignments = payload.get("assignments", []) if isinstance(payload, dict) else []
    updated = 0
    for assignment in assignments:
        so_id = str(assignment.get("so_id", "")).strip()
        campaign_id = assignment.get("campaign_id")
        if not so_id:
            continue
        try:
            store.update_row("sales-orders", so_id, {"Campaign_ID": campaign_id}, partial=True)
            updated += 1
        except Exception:
            continue
    return _jsonify({"ok": True, "updated": updated})


@app.route("/api/orders", methods=["GET", "POST"])
def orders_collection():
    if request.method == "GET":
        return _jsonify({"orders": _list_items("sales-orders")})
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and "data" not in payload else payload.get("data", {})
    try:
        return _jsonify({"ok": True, "item": store.create_row("sales-orders", data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)
    except Exception as e:
        return _jsonify({"error": str(e)}, 500)


@app.route("/api/orders/<so_id>", methods=["GET", "PUT", "DELETE"])
def order_item(so_id):
    if request.method == "GET":
        row = store.get_row("sales-orders", so_id)
        return _jsonify({"order": row} if row else {"error": "Not found"}, 200 if row else 404)
    if request.method == "DELETE":
        try:
            store.delete_row("sales-orders", so_id)
            return _jsonify({"ok": True, "so_id": so_id})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
        except Exception as e:
            return _jsonify({"error": str(e)}, 500)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and "data" not in payload else payload.get("data", {})
    try:
        return _jsonify({"ok": True, "item": store.update_row("sales-orders", so_id, data, partial=False)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)
    except Exception as e:
        return _jsonify({"error": str(e)}, 500)


# ── APS concept routes for the HTML app ─────────────────────────────────────
@app.route("/api/aps/dashboard/overview")
def aps_dashboard_overview():
    kpis = _kpis_from_state()
    return _jsonify({"summary": kpis, "campaigns": _campaigns_from_outputs()[:8], "alerts": kpis.get("shortage_alerts", [])[:10], "utilisation": kpis.get("utilisation", [])[:8]})


@app.route("/api/aps/orders/list")
def aps_orders_list():
    search = request.args.get("search")
    priority = request.args.get("priority")
    grade = request.args.get("grade")
    data = _read_table("sales-orders", search=search)
    items = data["items"]
    if priority:
        items = [x for x in items if str(x.get("Priority", "")).upper() == priority.upper()]
    if grade:
        items = [x for x in items if str(x.get("Grade", "")) == grade]
    return _jsonify({"items": items, "total": len(items)})


@app.route("/api/aps/orders/<so_id>", methods=["GET", "PUT", "DELETE"])
def aps_order_item(so_id):
    return order_item(so_id)


@app.route("/api/aps/orders", methods=["POST"])
def aps_order_create():
    return orders_collection()


@app.route("/api/aps/campaigns/list")
def aps_campaigns_list():
    status = request.args.get("status")
    items = _campaigns_from_outputs()
    if status:
        items = [x for x in items if status.upper() in str(x.get("release_status") or x.get("Release_Status") or x.get("Status") or "").upper()]
    return _jsonify({"items": items, "total": len(items)})


@app.route("/api/aps/campaigns/release-queue")
def aps_campaign_release_queue():
    items = _campaigns_from_outputs()
    items.sort(key=lambda x: str(x.get("Due_Date") or x.get("due_date") or "9999-12-31"))
    return _jsonify({"items": items})


@app.route("/api/aps/campaigns/<campaign_id>")
def aps_campaign_item(campaign_id):
    for item in _campaigns_from_outputs():
        cid = str(item.get("campaign_id") or item.get("Campaign_ID") or "")
        if cid == campaign_id:
            return _jsonify({"item": item})
    return _jsonify({"error": "Campaign not found"}, 404)


@app.route("/api/aps/schedule/gantt")
def aps_schedule_gantt():
    return _jsonify({"jobs": _schedule_jobs_from_outputs()})


@app.route("/api/aps/schedule/run", methods=["POST"])
def aps_schedule_run():
    return run_schedule_api()


@app.route("/api/aps/schedule/jobs/<job_id>")
def aps_schedule_job_item(job_id):
    for item in _schedule_jobs_from_outputs():
        jid = str(item.get("Job_ID") or item.get("job_id") or "")
        if jid == job_id:
            return _jsonify({"item": item})
    return _jsonify({"error": "Job not found"}, 404)


@app.route("/api/aps/dispatch/board")
def aps_dispatch_board():
    return _jsonify({"resources": _dispatch_from_schedule()})


@app.route("/api/aps/capacity/map")
def aps_capacity_map():
    return _jsonify({"items": _capacity_from_outputs()})


@app.route("/api/aps/capacity/bottlenecks")
def aps_capacity_bottlenecks():
    items = _capacity_from_outputs()
    items.sort(key=lambda x: float(x.get("Utilisation_%") or 0), reverse=True)
    return _jsonify({"items": items[:10]})


@app.route("/api/aps/material/plan")
def aps_material_plan():
    return _jsonify({"items": _material_from_outputs()})


@app.route("/api/aps/material/holds")
def aps_material_holds():
    items = [x for x in _material_from_outputs() if str(x.get("Status", "")).upper() not in {"OK", "AVAILABLE", "COVERED", ""}]
    return _jsonify({"items": items, "total": len(items)})


@app.route("/api/aps/ctp/check", methods=["POST"])
def aps_ctp_check():
    return run_ctp_api()


@app.route("/api/aps/masterdata")
def aps_masterdata():
    return _jsonify(_master_data_payload())


@app.route("/api/aps/masterdata/<section>")
def aps_masterdata_section(section):
    mapping = {"config": "config", "resources": "resource-master", "routing": "routing", "queue": "queue-times", "skus": "sku-master", "bom": "bom", "inventory": "inventory", "campaign-config": "campaign-config", "changeover": "changeover-matrix", "scenarios": "scenarios"}
    api_name = mapping.get(section)
    if not api_name:
        return _jsonify({"error": f"Unknown master data section: {section}"}, 404)
    return _jsonify({"items": _list_items(api_name)})


@app.route("/api/aps/masterdata/<section>/<key_value>", methods=["GET", "PUT", "PATCH", "DELETE"])
def aps_masterdata_section_item(section, key_value):
    mapping = {"config": "config", "resources": "resource-master", "routing": "routing", "queue": "queue-times", "skus": "sku-master", "bom": "bom", "inventory": "inventory", "campaign-config": "campaign-config", "changeover": "changeover-matrix", "scenarios": "scenarios"}
    api_name = mapping.get(section)
    if not api_name:
        return _jsonify({"error": f"Unknown master data section: {section}"}, 404)
    cfg = SHEETS[api_name]
    if request.method == "GET":
        row = store.get_row(api_name, key_value)
        return _jsonify({"item": row} if row else {"error": "Not found"}, 200 if row else 404)
    if request.method == "DELETE":
        try:
            store.delete_row(api_name, key_value)
            return _jsonify({"deleted": True, "key": key_value})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
        except ValueError as e:
            return _jsonify({"error": str(e)}, 400)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and "data" not in payload else payload.get("data", {})
    partial = request.method == "PATCH"
    try:
        return _jsonify({"item": store.update_row(api_name, key_value, data, partial=partial)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route("/api/aps/masterdata/<section>", methods=["POST"])
def aps_masterdata_section_create(section):
    mapping = {"config": "config", "resources": "resource-master", "routing": "routing", "queue": "queue-times", "skus": "sku-master", "bom": "bom", "inventory": "inventory", "campaign-config": "campaign-config", "changeover": "changeover-matrix", "scenarios": "scenarios"}
    api_name = mapping.get(section)
    if not api_name:
        return _jsonify({"error": f"Unknown master data section: {section}"}, 404)
    payload = request.get_json(silent=True) or {}
    data = payload if isinstance(payload, dict) and "data" not in payload else payload.get("data", {})
    try:
        return _jsonify({"item": store.create_row(api_name, data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)


@app.route("/api/aps/scenarios/list")
def aps_scenarios_list():
    return _jsonify({"items": _list_items("scenarios") if "scenarios" in SHEETS else []})


@app.route("/api/aps/scenarios/output")
def aps_scenarios_output():
    return _jsonify({"items": _list_items("scenario-output") if "scenario-output" in SHEETS else []})


if __name__ == "__main__":
    print(f"\n  APS Concept API -> http://localhost:{PORT}")
    print(f"  Workbook        -> {WORKBOOK}\n")
    app.run(host="0.0.0.0", port=PORT, debug=False, use_reloader=False)
