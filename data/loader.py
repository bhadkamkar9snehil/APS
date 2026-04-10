"""
Data Loader — reads APS_BF_SMS_RM.xlsm into pandas DataFrames
"""
import pandas as pd
from pathlib import Path

from engine.config import load_workbook_config_snapshot

EXCEL_PATH = Path(__file__).parent.parent / "APS_BF_SMS_RM.xlsm"


def load_all(path=None) -> dict:
    if path is None:
        path = EXCEL_PATH
    path = Path(path)
    xls  = pd.ExcelFile(path)
    data = {}

    # All sheets have 2 title rows before the actual header row (header=2)
    data["skus"]         = xls.parse("SKU_Master",       header=2)
    data["bom"]          = xls.parse("BOM",              header=2)
    data["inventory"]    = xls.parse("Inventory",        header=2)
    data["sales_orders"] = xls.parse("Sales_Orders",     header=2)
    data["resources"]    = xls.parse("Resource_Master",  header=2)
    data["routing"]      = xls.parse("Routing",          header=2)
    data["campaign_cfg"] = xls.parse("Campaign_Config",  header=2)
    data["changeover"]   = xls.parse("Changeover_Matrix",header=2, index_col=0)

    sc = xls.parse("Scenarios", header=2)
    sc.columns = sc.columns.str.strip()
    if "Parameter" in sc.columns:
        sc = sc.dropna(subset=["Parameter"])
        data["scenarios"] = sc.set_index("Parameter")
    else:
        data["scenarios"] = __import__('pandas').DataFrame(
            {"Value": [2.0, 15.0, 8.0, "EAF-01", 14, 100.0, 500.0]},
            index=__import__('pandas').Index(
                ["Safety Buffer (Hrs)","Demand Spike (%)","Machine Down (Hrs)",
                 "Machine Down Resource","Planning Horizon (Days)",
                 "Min Campaign MT","Max Campaign MT"], name="Parameter"))

    # New plan-surface sheets (may not exist in older workbooks)
    for sheet_name, key in [
        ("Config", "config_raw"),
        ("Queue_Times", "queue_times"),
        ("CTP_Request", "ctp_request"),
        ("CTP_Output", "ctp_output"),
    ]:
        try:
            data[key] = xls.parse(sheet_name, header=2)
        except Exception:
            data[key] = pd.DataFrame()

    snapshot = load_workbook_config_snapshot(path)
    data["algorithm_config"] = snapshot.algorithm_config.all_params()
    data["config"] = snapshot.runtime_config

    if "Status" in data["sales_orders"].columns:
        data["sales_orders"]["Status"] = data["sales_orders"]["Status"].fillna("Open")
    else:
        data["sales_orders"]["Status"] = "Open"

    # Ensure Section_mm numeric
    data["sales_orders"]["Section_mm"] = pd.to_numeric(
        data["sales_orders"]["Section_mm"], errors="coerce").fillna(6.5)

    return data


def validate(data: dict) -> list:
    warnings = []
    sku_ids = set(data["skus"]["SKU_ID"])

    so_skus = set(data["sales_orders"]["SKU_ID"])
    invalid = so_skus - sku_ids
    if invalid:
        warnings.append(f"SO SKUs not in SKU_Master: {invalid}")

    bom_parents = set(data["bom"]["Parent_SKU"])
    missing = bom_parents - sku_ids
    if missing:
        warnings.append(f"BOM parents not in SKU_Master: {missing}")

    return warnings


if __name__ == "__main__":
    data = load_all()
    for name, df in data.items():
        if isinstance(df, pd.DataFrame):
            print(f"  {name:20s} -> {df.shape[0]:>4} rows, {df.shape[1]:>3} cols")
    warnings = validate(data)
    if warnings:
        print("\nWarnings:")
        for w in warnings:
            print(f"  - {w}")
    else:
        print("\nAll validation checks passed")
