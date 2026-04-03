from __future__ import annotations

from pathlib import Path
from typing import Callable

from flask import request

from .excel_store import ExcelStore
from .workbook_schema import SHEETS


JsonifyFn = Callable[..., object]



def register_workbook_routes(app, jsonify_fn: JsonifyFn, workbook_path: str | Path):
    store = ExcelStore(workbook_path)

    def _sheet_or_404(sheet_name: str):
        cfg = SHEETS.get(sheet_name)
        if not cfg:
            return None, jsonify_fn({"error": f"Unknown sheet: {sheet_name}"}), 404
        return cfg, None, None

    @app.route("/api/meta/sheets")
    def workbook_meta_sheets():
        return jsonify_fn({"workbook_path": str(store.workbook_path), "sheets": store.list_sheet_configs()})

    @app.route("/api/meta/workbook-snapshot")
    def workbook_meta_snapshot():
        return jsonify_fn(store.workbook_snapshot())

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
            return jsonify_fn(data)

        payload = request.get_json(silent=True) or {}
        data = payload.get("data", {}) if isinstance(payload, dict) else {}
        try:
            return jsonify_fn({"item": store.create_row(sheet_name, data)})
        except ValueError as e:
            return jsonify_fn({"error": str(e)}), 400
        except Exception as e:
            return jsonify_fn({"error": str(e)}), 500

    @app.route("/api/sheets/<sheet_name>/bulk/replace", methods=["PUT"])
    def workbook_sheet_bulk_replace(sheet_name: str):
        _, err, status = _sheet_or_404(sheet_name)
        if err:
            return err, status

        payload = request.get_json(silent=True) or {}
        items = payload.get("items", []) if isinstance(payload, dict) else []
        try:
            return jsonify_fn(store.bulk_replace(sheet_name, items))
        except ValueError as e:
            return jsonify_fn({"error": str(e)}), 400
        except Exception as e:
            return jsonify_fn({"error": str(e)}), 500

    @app.route("/api/sheets/<sheet_name>/<key_value>", methods=["GET", "PUT", "PATCH", "DELETE"])
    def workbook_sheet_item(sheet_name: str, key_value: str):
        cfg, err, status = _sheet_or_404(sheet_name)
        if err:
            return err, status
        if not cfg.key_field:
            return jsonify_fn({"error": f"{sheet_name} does not expose a row key"}), 400

        if request.method == "GET":
            row = store.get_row(sheet_name, key_value)
            if row is None:
                return jsonify_fn({"error": "Row not found"}), 404
            return jsonify_fn({"item": row})

        if request.method == "DELETE":
            try:
                store.delete_row(sheet_name, key_value)
                return jsonify_fn({"deleted": True, "key": key_value})
            except KeyError as e:
                return jsonify_fn({"error": str(e)}), 404
            except ValueError as e:
                return jsonify_fn({"error": str(e)}), 400
            except Exception as e:
                return jsonify_fn({"error": str(e)}), 500

        payload = request.get_json(silent=True) or {}
        data = payload.get("data", {}) if isinstance(payload, dict) else {}
        partial = request.method == "PATCH"
        try:
            item = store.update_row(sheet_name, key_value, data, partial=partial)
            return jsonify_fn({"item": item})
        except KeyError as e:
            return jsonify_fn({"error": str(e)}), 404
        except ValueError as e:
            return jsonify_fn({"error": str(e)}), 400
        except Exception as e:
            return jsonify_fn({"error": str(e)}), 500

    return store
