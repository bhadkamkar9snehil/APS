"""Regression tests for workbook output rendering helpers."""
import sys
import types
from pathlib import Path

import pandas as pd

sys.path.insert(0, str(Path(__file__).parent.parent))
sys.modules.setdefault("xlwings", types.SimpleNamespace(Book=types.SimpleNamespace(caller=lambda: None)))

import aps_functions


class _FakeFont:
    def __init__(self):
        self.bold = False
        self.italic = False
        self.size = None
        self.color = None


class _FakeRange:
    def __init__(self, ws, start, end=None):
        self.ws = ws
        self.start = start
        self.end = end or start
        self.font = _FakeFont()
        self.color = None
        self.number_format = None
        self.column_width = None
        self.row_height = None
        self._value = None

    @property
    def value(self):
        return self._value

    @value.setter
    def value(self, new_value):
        self._value = new_value
        self.ws.writes.append((self.start, self.end, new_value))

    def merge(self):
        self.ws.merges.append((self.start, self.end))


class _FakeSheet:
    def __init__(self, name="Sheet1"):
        self.name = name
        self.writes = []
        self.merges = []

    def range(self, start, end=None):
        return _FakeRange(self, start, end)


def test_schedule_display_rows_keeps_detail_job_ids_clean():
    schedule_df = pd.DataFrame(
        [
            {
                "Job_ID": "CMP-001-H1-EAF",
                "Campaign": "CMP-001",
                "SO_ID": "Pool: SO-001",
                "Grade": "SAE 1008",
                "Section_mm": "Batch",
                "SKU_ID": "BIL-130-SAE1008",
                "Operation": "EAF",
                "Resource_ID": "EAF-01",
                "Planned_Start": pd.Timestamp("2026-03-24 08:00"),
                "Planned_End": pd.Timestamp("2026-03-24 09:00"),
                "Duration_Hrs": 1.0,
                "Heat_No": 1,
                "Qty_MT": 50.0,
                "Queue_Violation": "OK",
                "Status": "Scheduled",
            },
            {
                "Job_ID": "CMP-001-PO01-RM",
                "Campaign": "CMP-001",
                "SO_ID": "SO-001",
                "Grade": "SAE 1008",
                "Section_mm": 5.5,
                "SKU_ID": "FG-WR-SAE1008-55",
                "Operation": "RM",
                "Resource_ID": "RM-01",
                "Planned_Start": pd.Timestamp("2026-03-24 10:00"),
                "Planned_End": pd.Timestamp("2026-03-24 11:00"),
                "Duration_Hrs": 1.0,
                "Heat_No": "",
                "Qty_MT": 20.0,
                "Queue_Violation": "OK",
                "Status": "Scheduled",
            },
        ]
    )
    campaign_df = pd.DataFrame(
        [
            {
                "Campaign_ID": "CMP-001",
                "Grade": "SAE 1008",
                "Heats": 1,
                "Total_MT": 20.0,
                "Status": "On Time",
                "Release_Status": "RELEASED",
                "SOs_Covered": "SO-001",
            }
        ]
    )

    display = aps_functions._schedule_display_rows(schedule_df, campaign_df)

    sms_detail = display.loc[display["__row_kind"] == "detail_sms"].iloc[0]
    rm_detail = display.loc[display["__row_kind"] == "detail_rm"].iloc[0]
    heat_header = display.loc[display["__row_kind"] == "heat_header"].iloc[0]
    rm_header = display.loc[display["__row_kind"] == "rm_header"].iloc[0]

    assert sms_detail["Job_ID"] == "CMP-001-H1-EAF"
    assert rm_detail["Job_ID"] == "CMP-001-PO01-RM"
    assert not str(sms_detail["Job_ID"]).startswith(" ")
    assert not str(rm_detail["Job_ID"]).startswith(" ")
    assert not str(heat_header["Job_ID"]).startswith(" ")
    assert not str(rm_header["Job_ID"]).startswith(" ")


def test_render_material_plan_uses_inventory_covered_labels(monkeypatch):
    ws = _FakeSheet("Material_Plan")
    dashboard_calls = []

    monkeypatch.setattr(aps_functions, "_ensure_sheet", lambda *args, **kwargs: ws)
    monkeypatch.setattr(aps_functions, "_refresh_material_plan_shell", lambda *args, **kwargs: None)
    monkeypatch.setattr(aps_functions, "_ensure_control_panel_links", lambda *args, **kwargs: None)
    monkeypatch.setattr(aps_functions, "_clear_grouped_output_body", lambda *args, **kwargs: None)
    monkeypatch.setattr(aps_functions, "_style_header_row", lambda *args, **kwargs: None)
    monkeypatch.setattr(
        aps_functions,
        "_write_dashboard_block",
        lambda _ws, _cell, _title, _headers, rows: dashboard_calls.append(rows),
    )

    campaigns = [
        {
            "campaign_id": "CMP-001",
            "grade": "SAE 1008",
            "release_status": "RELEASED",
            "material_issue": "",
            "material_gross_requirements": {
                "RM-SCRAP": 20.0,
                "RM-FESI": 10.0,
            },
            "inventory_before": {
                "RM-SCRAP": 15.0,
                "RM-FESI": 10.0,
            },
            "material_consumed": {
                "RM-SCRAP": 15.0,
                "RM-FESI": 10.0,
            },
            "inventory_after": {
                "RM-SCRAP": 0.0,
                "RM-FESI": 0.0,
            },
            "material_shortages": {},
        }
    ]

    aps_functions._render_material_plan(object(), campaigns, skus=None)

    assert dashboard_calls, "expected material summary dashboard write"
    assert ["Inventory Covered Qty", 25.0] in dashboard_calls[0]
    assert all("Drawn From Stock" not in str(value) for _, _, value in ws.writes)
    assert any(
        isinstance(value, str) and "Inventory Covered Qty: 25" in value
        for _, _, value in ws.writes
    )
