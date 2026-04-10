"""
Run the APS pipeline and write results into the workbook.

Usage:
    python run_all.py
    python run_all.py bom
    python run_all.py capacity
    python run_all.py schedule
    python run_all.py scenarios
    python run_all.py ctp
    python run_all.py clear
"""
import sys
from datetime import datetime
from pathlib import Path

import xlwings as xw

import aps_functions as aps

BASE_DIR = Path(__file__).resolve().parent
WORKBOOK_CANDIDATES = [
    "APS_BF_SMS_RM.xlsm",
    "APS_BF_SMS_RM.xlsx",
]


def _iter_open_books():
    for app in xw.apps:
        for wb in app.books:
            yield wb


def _get_workbook():
    candidate_names = {name.lower() for name in WORKBOOK_CANDIDATES}

    for wb in _iter_open_books():
        if wb.name.lower() in candidate_names:
            print(f"Using open workbook: {wb.name}")
            return wb

    for name in WORKBOOK_CANDIDATES:
        path = BASE_DIR / name
        if path.exists():
            print(f"Opening workbook: {path.name}")
            return xw.Book(str(path))

    raise FileNotFoundError(
        "Could not find a supported APS workbook. Expected one of: "
        + ", ".join(WORKBOOK_CANDIDATES)
    )


def _resolve_actions(action):
    action_map = {
        "bom": ("BOM Explosion", aps.run_bom_explosion_for_workbook),
        "capacity": ("Capacity Map", aps.run_capacity_map_for_workbook),
        "schedule": ("Schedule", aps.run_schedule_for_workbook),
        "scenarios": ("Scenarios", aps.run_scenario_for_workbook),
        "ctp": ("CTP", aps.run_ctp_for_workbook),
        "clear": ("Clear Outputs", aps.clear_outputs_for_workbook),
    }
    if action == "all":
        return [
            action_map["bom"],
            action_map["capacity"],
            action_map["schedule"],
            action_map["scenarios"],
        ]
    if action not in action_map:
        raise ValueError(
            f"Unknown action '{action}'. Use one of: all, bom, capacity, schedule, scenarios, ctp, clear"
        )
    return [action_map[action]]


def run(action="all"):
    wb = _get_workbook()
    steps = _resolve_actions(action)

    for idx, (label, func) in enumerate(steps, start=1):
        print(f"\n[{idx}] {label}...")
        result = func(wb)

        if label == "BOM Explosion":
            print(f"   -> {len(result)} material requirement rows")
        elif label == "Capacity Map":
            overloaded = len(result[result["Status"] == "OVERLOADED"])
            print(f"   -> {len(result)} resources | Overloaded: {overloaded}")
        elif label == "Schedule":
            heat_rows = len(result["heat_schedule"])
            campaign_rows = len(result["campaign_schedule"])
            late = len(result["campaign_schedule"][result["campaign_schedule"]["Status"] == "LATE"])
            held = len(result["campaign_schedule"][result["campaign_schedule"]["Status"] == "MATERIAL HOLD"])
            released = len(result.get("released_campaigns", []))
            print(
                f"   -> Solver: {result['solver_status']} | "
                f"{heat_rows} operations | {released} released / {campaign_rows} total campaigns | "
                f"{late} late | {held} held"
            )
        elif label == "Scenarios":
            print(f"   -> {len(result)} scenario rows")
        elif label == "CTP":
            print("   -> CTP requests processed")
        elif label == "Clear Outputs":
            print("   -> output ranges cleared")

    wb.save()
    print(f"\nDone. ({datetime.now().strftime('%d-%b %H:%M')})")


if __name__ == "__main__":
    run(sys.argv[1] if len(sys.argv) > 1 else "all")
