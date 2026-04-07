from engine.excel_store import ExcelStore
from engine.workbook_schema import SHEETS
from pathlib import Path

print("Sheets config:")
if 'material-plan' in SHEETS:
    cfg = SHEETS['material-plan']
    print(f"  material-plan: {cfg.excel_name}, header row: {cfg.header_row_1_based}")
else:
    print("  material-plan NOT FOUND")

store = ExcelStore(Path("APS_BF_SMS_RM.xlsx"))
from xaps_application_api import _material_rows, _sheet_items

rows = _material_rows()
print(f"\n_material_rows() returned {len(rows)} rows")
if rows:
    print("First row:", rows[0])
