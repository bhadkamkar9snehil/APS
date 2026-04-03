"""
Layer A — Data Loader
Reads APS Excel template → validated pandas DataFrames
"""
import pandas as pd
from pathlib import Path

EXCEL_PATH = Path(__file__).parent.parent / "APS_Steel_Template.xlsx"

SHEET_MAP = {
    "skus":       ("SKU_Master",       None),
    "bom":        ("BOM",              None),
    "inventory":  ("Inventory",        None),
    "sales_orders":("Sales_Orders",    None),
    "resources":  ("Resource_Master",  None),
    "routing":    ("Routing",          None),
    "changeover": ("Changeover_Matrix",None),
    "scenarios":  ("Scenarios",        1),   # header row index
}

def load_all(path=EXCEL_PATH) -> dict[str, pd.DataFrame]:
    xls = pd.ExcelFile(path)
    data = {}

    data["skus"]        = xls.parse("SKU_Master")
    data["bom"]         = xls.parse("BOM")
    data["inventory"]   = xls.parse("Inventory")
    data["sales_orders"]= xls.parse("Sales_Orders", parse_dates=["Order_Date","Delivery_Date"])
    data["resources"]   = xls.parse("Resource_Master")
    data["routing"]     = xls.parse("Routing")

    # Changeover matrix — row 0 is header, col 0 is index
    co = xls.parse("Changeover_Matrix", index_col=0)
    data["changeover"] = co

    # Scenarios — skip title row
    sc = xls.parse("Scenarios", header=1)
    sc = sc.dropna(subset=["Parameter"])
    data["scenarios"] = sc.set_index("Parameter")

    return data


def validate(data: dict) -> list[str]:
    """Basic validation checks. Returns list of warnings."""
    warnings = []
    inv = data["inventory"]
    skus = data["skus"]

    # All SKUs in inventory?
    missing = set(skus["SKU_ID"]) - set(inv["SKU_ID"])
    if missing:
        warnings.append(f"SKUs missing from Inventory: {missing}")

    # BOM parent SKUs exist in SKU_Master?
    bom_parents = set(data["bom"]["Parent_SKU"])
    sku_ids = set(skus["SKU_ID"])
    missing_bom = bom_parents - sku_ids
    if missing_bom:
        warnings.append(f"BOM parents not in SKU_Master: {missing_bom}")

    # Open SOs reference valid SKUs?
    so_skus = set(data["sales_orders"]["SKU_ID"])
    invalid_so = so_skus - sku_ids
    if invalid_so:
        warnings.append(f"SO SKUs not in SKU_Master: {invalid_so}")

    return warnings


if __name__ == "__main__":
    data = load_all()
    for name, df in data.items():
        if isinstance(df, pd.DataFrame):
            print(f"  {name:20s} → {df.shape[0]} rows, {df.shape[1]} cols")

    warnings = validate(data)
    if warnings:
        print("\n⚠ Validation warnings:")
        for w in warnings:
            print(f"  - {w}")
    else:
        print("\n✓ All validation checks passed")
