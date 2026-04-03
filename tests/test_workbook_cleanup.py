"""Regression tests for workbook cleanup helpers."""
import sys
import types
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))
sys.modules.setdefault("xlwings", types.SimpleNamespace(Book=types.SimpleNamespace(caller=lambda: None)))

import aps_functions


class _FakeRange:
    def __init__(self, value):
        self.value = value


class _FakeSheet:
    def __init__(self, name, values=None):
        self.name = name
        self._values = values or {}

    def range(self, *args):
        first = args[0]
        if isinstance(first, tuple):
            key = first
        else:
            key = first
        return _FakeRange(self._values.get(key))


class _FakeSheets:
    def __init__(self, sheets):
        self._sheets = list(sheets)
        self._by_name = {sheet.name: sheet for sheet in self._sheets}

    def __iter__(self):
        return iter(self._sheets)

    def __getitem__(self, key):
        if isinstance(key, str):
            return self._by_name[key]
        return self._sheets[key]


class _FakeWorkbook:
    def __init__(self, sheets):
        self.sheets = _FakeSheets(sheets)


def test_remove_inline_sheet_guides_only_clears_real_legacy_panels(monkeypatch):
    bom_sheet = _FakeSheet("BOM", values={(1, 10): None, (3, 10): "Note"})
    bom_output_sheet = _FakeSheet("BOM_Output", values={(1, 15): "Sheet Guide"})
    wb = _FakeWorkbook([bom_sheet, bom_output_sheet])

    cleared = []

    def _capture_clear(ws, start_col, width=6, end_row=20):
        cleared.append((ws.name, start_col, width, end_row))

    monkeypatch.setattr(aps_functions, "_clear_legacy_doc_panel", _capture_clear)

    aps_functions._remove_inline_sheet_guides(wb)

    assert cleared == [("BOM_Output", 15, 6, 24)]


def test_remove_inline_sheet_guides_clears_when_guide_title_exists(monkeypatch):
    bom_sheet = _FakeSheet("BOM", values={(1, 10): " Sheet Guide "})
    wb = _FakeWorkbook([bom_sheet])

    cleared = []

    def _capture_clear(ws, start_col, width=6, end_row=20):
        cleared.append((ws.name, start_col, width, end_row))

    monkeypatch.setattr(aps_functions, "_clear_legacy_doc_panel", _capture_clear)

    aps_functions._remove_inline_sheet_guides(wb)

    assert cleared == [("BOM", 10, 8, 24)]
