"""
APS Functions Module
--------------------
Provides scheduler and planning functions for Excel macros and direct Python calls.
Wraps engine modules for clean interface.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from engine.excel_store import ExcelStore
from engine.campaign import build_campaigns
from engine.scheduler import schedule
from engine.config import get_config
from engine.capacity import capacity_map
from engine.bom_explosion import explode_bom_details
from engine.ctp import capable_to_promise


def run_schedule(workbook_path: str = None, time_limit_seconds: int = 60) -> dict:
    """Run the APS scheduler (called from Excel macros)."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        config = get_config(store)
        campaigns = build_campaigns(store, config)
        schedule_df = schedule(campaigns, store, config, time_limit_seconds)

        return {
            "success": True,
            "campaigns": len(campaigns),
            "schedule_rows": len(schedule_df) if schedule_df is not None else 0,
            "message": f"Scheduled {len(campaigns)} campaigns"
        }
    except Exception as e:
        return {"success": False, "error": str(e), "message": f"Scheduler failed: {str(e)}"}


def run_bom_explosion(workbook_path: str = None) -> dict:
    """Run BOM explosion analysis."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        bom_details = explode_bom_details(store)
        return {"success": True, "rows": len(bom_details), "message": "BOM explosion complete"}
    except Exception as e:
        return {"success": False, "error": str(e)}


def run_capacity_map(workbook_path: str = None) -> dict:
    """Generate capacity utilization map."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        config = get_config(store)
        cap_map = capacity_map(store, config)
        return {"success": True, "resources": len(cap_map) if cap_map is not None else 0, "message": "Capacity map generated"}
    except Exception as e:
        return {"success": False, "error": str(e)}


def run_ctp(workbook_path: str = None, requested_date: str = None) -> dict:
    """Run capable-to-promise analysis."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        config = get_config(store)
        campaigns = build_campaigns(store, config)

        ctp_result = capable_to_promise(store, config, campaigns, requested_date)
        return {"success": True, "feasible": ctp_result.get("feasible", False), "message": "CTP analysis complete"}
    except Exception as e:
        return {"success": False, "error": str(e)}


def run_scenario(workbook_path: str = None, scenario_name: str = None) -> dict:
    """Apply and run a scenario."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        # Apply scenario if provided
        if scenario_name:
            store.apply_scenario(scenario_name)

        config = get_config(store)
        campaigns = build_campaigns(store, config)
        schedule_df = schedule(campaigns, store, config)

        return {"success": True, "message": f"Scenario '{scenario_name or 'default'}' executed"}
    except Exception as e:
        return {"success": False, "error": str(e)}


def get_campaign_status(workbook_path: str = None) -> dict:
    """Get current campaign status from workbook."""
    try:
        if workbook_path is None:
            import os
            workbook_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")

        store = ExcelStore(workbook_path)
        config = get_config(store)
        campaigns = build_campaigns(store, config)
        return {
            "success": True,
            "total_campaigns": len(campaigns),
            "campaigns": [c.campaign_id for c in campaigns]
        }
    except Exception as e:
        return {"success": False, "error": str(e)}


if __name__ == "__main__":
    # Test call
    import os
    wb_path = os.path.join(os.path.dirname(__file__), "APS_BF_SMS_RM.xlsx")
    result = run_scheduler(wb_path)
    print(result)
