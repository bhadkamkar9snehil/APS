from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from engine.excel_store import ExcelStore
from engine.workbook_schema import SHEETS
from xaps_application_api import _material_rows


def main():
    print("Sheets config:")
    if 'material-plan' in SHEETS:
        cfg = SHEETS['material-plan']
        print(f"  material-plan: {cfg.excel_name}, header row: {cfg.header_row_1_based}")
    else:
        print("  material-plan NOT FOUND")

    ExcelStore(ROOT / "APS_BF_SMS_RM.xlsx")
    rows = _material_rows()
    print(f"\n_material_rows() returned {len(rows)} rows")
    if rows:
        print("First row:", rows[0])


if __name__ == "__main__":
    main()
