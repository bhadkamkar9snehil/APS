"""
Excel-backed CRUD API for APS workbook.

This is designed to sit alongside the existing Flask APS server and expose
row-level workbook CRUD plus compatibility endpoints that the SPA can use
without changing its current API shape.

Run:
    python api_excel_crud.py
"""
from __future__ import annotations

import json
import os
from datetime import date, datetime
from pathlib import Path
from typing import Any, Dict, Optional

import numpy as np
import pandas as pd
from flask import Flask, request
from flask_cors import CORS

from engine.excel_store import ExcelStore
from engine.workbook_schema import FRONTEND_COMPAT, SHEETS

WORKBOOK = Path(os.getenv("WORKBOOK_PATH", str(Path(__file__).parent / "APS_BF_SMS_RM.xlsx")))
PORT = int(os.getenv("PORT", "5000"))

app = Flask(__name__)
CORS(app, origins=["http://localhost:3131", "http://127.0.0.1:3131", "*"])
store = ExcelStore(WORKBOOK)


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


def _jsonify(data: Any, status: int = 200):
    return app.response_class(json.dumps(data, cls=_Enc, ensure_ascii=False), status=status, mimetype="application/json")


def _sheet_or_404(sheet_name: str):
    cfg = SHEETS.get(sheet_name)
    if not cfg:
        return None, _jsonify({"error": f"Unknown sheet: {sheet_name}"}, 404)
    return cfg, None


@app.route("/api/health")
def health():
    return _jsonify(
        {
            "status": "ok",
            "workbook_path": str(store.workbook_path),
            "workbook_exists": store.workbook_path.exists(),
            "available_sheets": list(SHEETS.keys()),
            "solver_status": "WORKBOOK",
        }
    )


@app.route("/api/meta/sheets")
def meta_sheets():
    return _jsonify({"workbook_path": str(store.workbook_path), "sheets": store.list_sheet_configs()})


@app.route("/api/meta/workbook-snapshot")
def workbook_snapshot():
    return _jsonify(store.workbook_snapshot())


@app.route("/api/sheets/<sheet_name>", methods=["GET", "POST"])
def sheet_collection(sheet_name: str):
    cfg, err = _sheet_or_404(sheet_name)
    if err:
        return err

    if request.method == "GET":
        search = request.args.get("search")
        sort_by = request.args.get("sort_by")
        sort_dir = request.args.get("sort_dir", "asc")
        limit = request.args.get("limit", type=int)
        offset = request.args.get("offset", default=0, type=int)
        return _jsonify(store.list_rows(sheet_name, search=search, sort_by=sort_by, sort_dir=sort_dir, limit=limit, offset=offset))

    payload = request.get_json(silent=True) or {}
    data = payload.get("data", {}) if isinstance(payload, dict) else {}
    try:
        return _jsonify({"item": store.create_row(sheet_name, data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)
    except Exception as e:
        return _jsonify({"error": str(e)}, 500)


@app.route("/api/sheets/<sheet_name>/bulk/replace", methods=["PUT"])
def sheet_bulk_replace(sheet_name: str):
    _, err = _sheet_or_404(sheet_name)
    if err:
        return err
    payload = request.get_json(silent=True) or {}
    items = payload.get("items", []) if isinstance(payload, dict) else []
    try:
        return _jsonify(store.bulk_replace(sheet_name, items))
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)
    except Exception as e:
        return _jsonify({"error": str(e)}, 500)


@app.route("/api/sheets/<sheet_name>/<key_value>", methods=["GET", "PUT", "PATCH", "DELETE"])
def sheet_item(sheet_name: str, key_value: str):
    cfg, err = _sheet_or_404(sheet_name)
    if err:
        return err
    if not cfg.key_field:
        return _jsonify({"error": f"{sheet_name} does not expose a row key"}, 400)

    if request.method == "GET":
        row = store.get_row(sheet_name, key_value)
        return _jsonify({"item": row} if row is not None else {"error": "Row not found"}, 200 if row is not None else 404)

    if request.method == "DELETE":
        try:
            store.delete_row(sheet_name, key_value)
            return _jsonify({"deleted": True, "key": key_value})
        except KeyError as e:
            return _jsonify({"error": str(e)}, 404)
        except ValueError as e:
            return _jsonify({"error": str(e)}, 400)

    payload = request.get_json(silent=True) or {}
    data = payload.get("data", {}) if isinstance(payload, dict) else {}
    partial = request.method == "PATCH"
    try:
        return _jsonify({"item": store.update_row(sheet_name, key_value, data, partial=partial)})
    except KeyError as e:
        return _jsonify({"error": str(e)}, 404)
    except ValueError as e:
        return _jsonify({"error": str(e)}, 400)
    except Exception as e:
        return _jsonify({"error": str(e)}, 500)


@app.route("/api/data/orders")
def data_orders():
    data = store.list_rows(FRONTEND_COMPAT["orders"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int), search=request.args.get("search"))
    return _jsonify({"orders": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/data/skus")
def data_skus():
    data = store.list_rows(FRONTEND_COMPAT["skus"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int), search=request.args.get("search"))
    return _jsonify({"skus": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/data/gantt")
def data_gantt():
    data = store.list_rows(FRONTEND_COMPAT["gantt"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int))
    return _jsonify({"jobs": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/data/config")
def data_config():
    return _jsonify(
        {
            "config": store.list_rows("config")["items"],
            "resources": store.list_rows("resource-master")["items"],
            "routing": store.list_rows("routing")["items"],
            "queue": store.list_rows("queue-times")["items"],
            "skus": store.list_rows("sku-master")["items"],
            "campaigns": store.list_rows("campaign-schedule")["items"],
        }
    )


@app.route("/api/data/campaigns")
def data_campaigns():
    data = store.list_rows(FRONTEND_COMPAT["campaigns"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int))
    return _jsonify({"campaigns": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/data/capacity")
def data_capacity():
    data = store.list_rows(FRONTEND_COMPAT["capacity"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int))
    return _jsonify({"capacity": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/data/material")
def data_material():
    data = store.list_rows(FRONTEND_COMPAT["material"], limit=request.args.get("limit", type=int), offset=request.args.get("offset", default=0, type=int))
    return _jsonify({"material": data["items"], "total": data["total"], "headers": data["headers"]})


@app.route("/api/orders/assign", methods=["POST"])
def assign_orders():
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
    return _jsonify({"updated": updated})


@app.route("/api/run/schedule", methods=["POST"])
def run_schedule():
    campaigns = store.list_rows("campaign-schedule")["items"]
    gantt = store.list_rows("schedule-output")["items"]
    capacity = store.list_rows("capacity-map")["items"]
    material = store.list_rows("material-plan")["items"]
    kpis = {x["KPI"]: x for x in store.list_rows("kpi-dashboard")["items"]}

    total_mt = sum(float(x.get("Total_MT") or 0) for x in campaigns)
    total_heats = sum(float(x.get("Heats") or 0) for x in campaigns)
    released = sum(1 for x in campaigns if str(x.get("Release_Status", "")).upper() == "RELEASED")
    held = sum(1 for x in campaigns if "HOLD" in str(x.get("Release_Status", "")).upper())
    late = sum(1 for x in campaigns if str(x.get("Status", "")).upper() == "LATE")
    bottleneck_row = max(capacity, key=lambda x: float(x.get("Utilisation_%") or 0)) if capacity else None

    return _jsonify(
        {
            "solver_status": "WORKBOOK",
            "solver_detail": "Loaded from workbook outputs",
            "campaigns": campaigns,
            "gantt": gantt,
            "capacity": capacity,
            "material": material,
            "campaigns_total": len(campaigns),
            "campaigns_released": released,
            "campaigns_held": held,
            "campaigns_late": late,
            "total_mt": round(total_mt, 2),
            "total_heats": round(total_heats, 2),
            "throughput_mt_day": kpis.get("Throughput", {}).get("Current") if "Throughput" in kpis else None,
            "on_time_pct": kpis.get("On-Time Delivery", {}).get("Current") if "On-Time Delivery" in kpis else None,
            "bottleneck": bottleneck_row.get("Resource_ID") if bottleneck_row else None,
            "max_utilisation": bottleneck_row.get("Utilisation_%") if bottleneck_row else None,
            "shortage_alerts": [
                {
                    "campaign_id": row.get("Campaign_ID"),
                    "sku_id": row.get("Material_SKU"),
                    "shortage_qty": row.get("Required_Qty"),
                    "severity": "HIGH" if str(row.get("Status", "")).upper() in {"SHORT", "CRITICAL", "BLOCKED"} else "MED",
                }
                for row in material
                if str(row.get("Status", "")).upper() not in {"OK", "AVAILABLE", "COVERED"}
            ],
            "utilisation": [
                {
                    "resource_id": row.get("Resource_ID"),
                    "operation": row.get("Resource_Name"),
                    "utilisation": row.get("Utilisation_%"),
                }
                for row in capacity
            ],
        }
    )


@app.route("/api/run/ctp", methods=["POST"])
def run_ctp():
    payload = request.get_json(silent=True) or {}
    request_id = payload.get("request_id")
    sku_id = payload.get("sku_id")
    rows = store.list_rows("ctp-output")["items"]
    match = None
    for row in rows:
        if request_id and str(row.get("Request_ID")) == str(request_id):
            match = row
            break
        if sku_id and str(row.get("SKU_ID")) == str(sku_id):
            match = row
    if not match:
        return _jsonify(
            {
                "request_id": request_id,
                "sku_id": sku_id,
                "plant_completion_feasible": None,
                "delivery_feasible": None,
                "message": "No matching CTP output row found in workbook",
            }
        )
    return _jsonify(
        {
            "request_id": match.get("Request_ID"),
            "sku_id": match.get("SKU_ID"),
            "qty_mt": match.get("Qty_MT"),
            "requested_date": match.get("Requested_Date"),
            "earliest_completion": match.get("Earliest_Completion"),
            "plant_completion_feasible": match.get("Plant_Completion_Feasible"),
            "earliest_delivery": match.get("Earliest_Delivery"),
            "delivery_feasible": match.get("Delivery_Feasible"),
            "lateness_days": match.get("Lateness_Days"),
            "material_gaps": match.get("Material_Gaps"),
            "campaign_action": match.get("Campaign_Action"),
            "solver_status": match.get("Solver_Status"),
            "feasible": match.get("Delivery_Feasible"),
        }
    )


if __name__ == "__main__":
    print(f"\n  APS Excel CRUD API -> http://localhost:{PORT}")
    print(f"  Workbook           -> {WORKBOOK}\n")
    app.run(host="0.0.0.0", port=PORT, debug=False, use_reloader=False)
