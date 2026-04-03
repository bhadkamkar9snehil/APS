"""
Integrated APS API server with workbook CRUD routes.

This file keeps the existing `api_server.py` intact and layers workbook CRUD
endpoints onto the same Flask app so the current API surface remains available.

Run:
    python api_server_workbook.py
"""
from __future__ import annotations

import os
from pathlib import Path

from flask import request

from api_server import app, _jsonify  # existing app + response helper
from engine.excel_store import ExcelStore
from engine.workbook_schema import SHEETS

WORKBOOK = Path(os.getenv("WORKBOOK_PATH", str(Path(__file__).parent / "APS_BF_SMS_RM.xlsx")))
store = ExcelStore(WORKBOOK)


def _sheet_or_404(sheet_name: str):
    cfg = SHEETS.get(sheet_name)
    if not cfg:
        return None, _jsonify({"error": f"Unknown sheet: {sheet_name}"}), 404
    return cfg, None, None


@app.route("/api/meta/sheets")
def workbook_meta_sheets():
    return _jsonify({"workbook_path": str(store.workbook_path), "sheets": store.list_sheet_configs()})


@app.route("/api/meta/workbook-snapshot")
def workbook_meta_snapshot():
    return _jsonify(store.workbook_snapshot())


@app.route("/api/sheets/<sheet_name>", methods=["GET", "POST"])
def workbook_sheet_collection(sheet_name: str):
    _, err, status = _sheet_or_404(sheet_name)
    if err:
        return err, status

    if request.method == "GET":
        search = request.args.get("search")
        sort_by = request.args.get("sort_by")
        sort_dir = request.args.get("sort_dir", "asc")
        limit = request.args.get("limit", type=int)
        offset = request.args.get("offset", default=0, type=int)
        filters = {k[2:]: v for k, v in request.args.items() if k.startswith("f_")}
        data = store.list_rows(sheet_name, search=search, sort_by=sort_by, sort_dir=sort_dir, limit=limit, offset=offset, filters=filters or None)
        return _jsonify(data)

    payload = request.get_json(silent=True) or {}
    data = payload.get("data", {}) if isinstance(payload, dict) else {}
    try:
        return _jsonify({"item": store.create_row(sheet_name, data)})
    except ValueError as e:
        return _jsonify({"error": str(e)}), 400
    except Exception as e:
        return _jsonify({"error": str(e)}), 500


@app.route("/api/sheets/<sheet_name>/bulk/replace", methods=["PUT"])
def workbook_sheet_bulk_replace(sheet_name: str):
    _, err, status = _sheet_or_404(sheet_name)
    if err:
        return err, status

    payload = request.get_json(silent=True) or {}
    items = payload.get("items", []) if isinstance(payload, dict) else []
    try:
        return _jsonify(store.bulk_replace(sheet_name, items))
    except ValueError as e:
        return _jsonify({"error": str(e)}), 400
    except Exception as e:
        return _jsonify({"error": str(e)}), 500


@app.route("/api/sheets/<sheet_name>/<key_value>", methods=["GET", "PUT", "PATCH", "DELETE"])
def workbook_sheet_item(sheet_name: str, key_value: str):
    cfg, err, status = _sheet_or_404(sheet_name)
    if err:
        return err, status
    if not cfg.key_field:
        return _jsonify({"error": f"{sheet_name} does not expose a row key"}), 400

    if request.method == "GET":
        row = store.get_row(sheet_name, key_value)
        if row is None:
            return _jsonify({"error": "Row not found"}), 404
        return _jsonify({"item": row})

    if request.method == "DELETE":
        try:
            store.delete_row(sheet_name, key_value)
            return _jsonify({"deleted": True, "key": key_value})
        except KeyError as e:
            return _jsonify({"error": str(e)}), 404
        except ValueError as e:
            return _jsonify({"error": str(e)}), 400
        except Exception as e:
            return _jsonify({"error": str(e)}), 500

    payload = request.get_json(silent=True) or {}
    data = payload.get("data", {}) if isinstance(payload, dict) else {}
    partial = request.method == "PATCH"
    try:
        item = store.update_row(sheet_name, key_value, data, partial=partial)
        return _jsonify({"item": item})
    except KeyError as e:
        return _jsonify({"error": str(e)}), 404
    except ValueError as e:
        return _jsonify({"error": str(e)}), 400
    except Exception as e:
        return _jsonify({"error": str(e)}), 500


if __name__ == "__main__":
    print(f"\n  APS integrated workbook API -> http://localhost:5000")
    print(f"  Workbook                    -> {WORKBOOK}\n")
    app.run(host="0.0.0.0", port=5000, debug=False, use_reloader=False)
