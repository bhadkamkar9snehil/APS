"""
APS xlwings bridge for the canonical APS workbook.
Called from Excel via RunPython VBA macros.
"""
import math
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd
import xlwings as xw

sys.path.insert(0, str(Path(__file__).parent))

from engine.bom_explosion import consolidate_demand, explode_bom_details, net_requirements
from engine.campaign import build_campaigns
from engine.capacity import (
    ROUGH_CUT_CAPACITY_BASIS,
    capacity_map,
    capacity_map_from_schedule,
    compute_demand_hours,
)
from engine.ctp import capable_to_promise
from engine.scheduler import schedule
from scenarios.scenario_runner import build_scenarios, run_scenario as _run_scenario

LATE_COLOR = (255, 200, 200)
ON_TIME_CLR = (226, 239, 218)
AMBER_COLOR = (255, 235, 156)
RUNNING_COLOR = (189, 215, 238)
HEADER_COLOR = (47, 84, 150)
TITLE_COLOR = (31, 56, 100)
LIGHT_BLUE = (221, 235, 247)
DOC_FILL = (234, 242, 248)
OPERATION_FILL = {
    "EAF": (221, 235, 247),
    "LRF": (255, 242, 204),
    "VD": (217, 225, 242),
    "CCM": (226, 239, 218),
    "RM": (248, 203, 173),
}
SUPPORTED_WORKBOOKS = {"aps_bf_sms_rm.xlsm", "aps_bf_sms_rm.xlsx"}
CONFIG_DEFAULTS = {
    "Batch_Unit_Name": "Heat",
    "Primary_Batch_Resource_Group": "EAF",
    "Default_Batch_Size_MT": 50.0,
    "Campaign_Group_By": "Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant",
    "Planning_Horizon_Days": 14,
    "Default_Solver_Limit_Sec": 30.0,
    "Min_Campaign_MT": 100.0,
    "Max_Campaign_MT": 500.0,
    "Queue_Enforcement": "Hard",
    "Campaign_Serialization_Mode": "STRICT_END_TO_END",
    "Allow_Scheduler_Default_Masters": "N",
    "BOM_Structure_Error_Mode": "RAISE",
    "Allow_Legacy_Primary_Batch_Fallback": "N",
    "Manual_Campaign_Grouping_Mode": "PRESERVE_EXACT",
    "Byproduct_Inventory_Mode": "DEFERRED",
    "Require_Authoritative_CTP_Inventory": "Y",
    "Default_Section_Fallback": 6.5,
    "Workbook_Name": None,
}
BOM_OUTPUT_SHEET = "BOM_Output"
SCENARIO_OUTPUT_SHEET = "Scenario_Output"
EQUIPMENT_SCHEDULE_SHEET = "Equipment_Schedule"
GANTT_SHEET = "Schedule_Gantt"
MATERIAL_PLAN_SHEET = "Material_Plan"
HELP_SHEET = "Help"
CTP_REQUEST_SHEET = "CTP_Request"
CTP_OUTPUT_SHEET = "CTP_Output"
RUNNING_STATUSES = {"RUNNING", "IN PROCESS", "IN_PROGRESS", "LOCKED", "FROZEN"}
OUTPUT_PROMPTS = {
    BOM_OUTPUT_SHEET: "Run 'Run BOM Explosion' to populate",
    "Capacity_Map": "Run 'Run Capacity Map' to populate",
    "Schedule_Output": "Run 'Run Schedule' to populate",
    "Campaign_Schedule": "Run 'Run Schedule' to populate",
    MATERIAL_PLAN_SHEET: "Run 'Run Schedule' to populate campaign material consumption",
    EQUIPMENT_SCHEDULE_SHEET: "Run 'Run Schedule' to populate grouped equipment tables",
    GANTT_SHEET: "Run 'Run Schedule' to populate the timeline view",
    SCENARIO_OUTPUT_SHEET: "Run 'Run Scenarios' to populate",
    CTP_OUTPUT_SHEET: "Enter requests in 'CTP_Request' and run 'Run CTP' to populate",
}
KPI_DASHBOARD_SHEET = "KPI_Dashboard"
LEGACY_GUIDE_LAYOUTS = {
    "Config": (5, 6),
    "Control_Panel": (10, 6),
    "SKU_Master": (15, 6),
    "BOM": (10, 8),
    "Inventory": (10, 6),
    "Sales_Orders": (14, 6),
    "Resource_Master": (16, 6),
    "Routing": (18, 6),
    "Campaign_Config": (12, 6),
    "Changeover_Matrix": (12, 6),
    "Queue_Times": (8, 6),
    "Scenarios": (8, 6),
    "CTP_Request": (8, 6),
    BOM_OUTPUT_SHEET: (15, 6),
    "Capacity_Map": (11, 6),
    "Schedule_Output": (19, 8),
    "Campaign_Schedule": (22, 6),
    MATERIAL_PLAN_SHEET: (12, 6),
    EQUIPMENT_SCHEDULE_SHEET: (16, 6),
    SCENARIO_OUTPUT_SHEET: (25, 6),
    "CTP_Output": (13, 6),
    "Theo_vs_Actual": (15, 6),
    KPI_DASHBOARD_SHEET: (8, 6),
}
HELP_SHEET_ROWS = [
    ("Control_Panel", "Control", "Main launch sheet for BOM, capacity, schedule, scenarios, and cleanup.", "Start here; use this as the workbook landing page."),
    ("Help", "Reference", "Central workbook instructions. Inline sheet guides have been removed on purpose.", "Use this tab instead of looking for notes on each individual sheet."),
    ("Config", "Input Master", "Workbook defaults such as batch size, grouping keys, horizon, solver limit, queue policy, and master-data strictness.", "Change structural defaults here, not in Python."),
    ("SKU_Master", "Input Master", "All finished goods, intermediates, byproducts, and raw materials.", "Use when adding grades, WIP stages, or tracked waste streams."),
    ("BOM", "Input Master", "Stagewise material relationships, yields, scraps, and byproducts.", "Workbook production runs use deferred byproduct availability instead of assuming immediate reuse."),
    ("Inventory", "Input Master", "Available stock by SKU and location.", "Drives netting before the plant is asked to make more material."),
    ("Sales_Orders", "Input Master", "Open demand waiting to be planned.", "Priority, due dates, and quantities drive campaigns."),
    ("Resource_Master", "Input Master", "Plant assets, capacities, hours, and operation families.", "Workbook planning runs fail fast on missing required resources; demo fallback masters are not used."),
    ("Routing", "Input Master", "Operation order and standard times by SKU or family.", "Workbook planning runs fail fast on missing routes/times; demo fallback masters are not used."),
    ("Campaign_Config", "Input Master", "Campaign sizing and sequencing assumptions.", "Use when PPC wants different campaign clustering rules."),
    ("Changeover_Matrix", "Input Master", "RM changeover reference minutes between products.", "Used for rolling changeover gaps; steel sequencing is still partial."),
    ("Queue_Times", "Input Master", "Queue and hold-time constraints between operations.", "Used to limit unrealistic waits between linked stages."),
    ("Scenarios", "Input Control", "Active scenario and planning override sheet.", "Demand spike, downtime, solver limit, yield loss, rush orders, and overtime affect the live plan."),
    ("CTP_Request", "Input Control", "Capable-to-promise request input sheet.", "Enter requested SKU, quantity, and date, then run CTP."),
    (BOM_OUTPUT_SHEET, "Output", "Total plant material requirements after inventory netting.", "Network view; not campaign-sequenced."),
    ("Capacity_Map", "Output", "Rough-cut heuristic demand hours vs available hours by resource.", "Use for quick bottleneck screening, not as a finite-schedule equivalent."),
    ("Schedule_Output", "Output", "Master planner table across SMS and RM.", "Campaign sequencing follows Config > Campaign_Serialization_Mode; this is the detailed operation view."),
    ("Campaign_Schedule", "Output", "One row per campaign with release status and stage milestones.", "Best management-summary view."),
    (MATERIAL_PLAN_SHEET, "Output", "Campaign-by-campaign material allocation and shortage trace.", "Use with Campaign_Schedule when debugging holds."),
    (EQUIPMENT_SCHEDULE_SHEET, "Output", "Dispatch packets grouped by plant and equipment.", "Best for shift handover and machine-level release."),
    (GANTT_SHEET, "Output", "Resource swim-lane timeline of the dispatch plan.", "Shows campaign occupancy by machine in release order."),
    (SCENARIO_OUTPUT_SHEET, "Output", "Scenario comparison sheet.", "Use to compare service, lateness, bottlenecks, throughput, and utilisation."),
    ("CTP_Output", "Output", "Capable-to-promise result sheet.", "Shows plant-completion surrogate separately from delivery feasibility, and blocks promises when inventory lineage is non-authoritative."),
    ("Theo_vs_Actual", "Output", "Placeholder for closed-loop plan-vs-actual review.", "Reserved for future execution feedback."),
    (KPI_DASHBOARD_SHEET, "Output", "Executive KPI and chart sheet.", "Use for the top-line summary after each run."),
]
SCHEDULE_OUTPUT_HEADERS = [
    "Job_ID",
    "Campaign",
    "SO_ID",
    "Grade",
    "Section_mm",
    "SKU_ID",
    "Operation",
    "Resource_ID",
    "Planned_Start",
    "Planned_End",
    "Duration_Hrs",
    "Heat_No",
    "Qty_MT",
    "Queue_Violation",
    "Status",
]
CAMPAIGN_SCHEDULE_HEADERS = [
    "Campaign_ID",
    "Campaign_Group",
    "Grade",
    "Section_mm",
    "Sections_Covered",
    "Total_MT",
    "Heats",
    "Heats_Calc_Method",
    "Heats_Calc_Warnings",
    "Order_Count",
    "Priority",
    "Release_Status",
    "Material_Issue",
    "EAF_Start",
    "CCM_Start",
    "RM_Start",
    "RM_End",
    "Duration_Hrs",
    "Due_Date",
    "Margin_Hrs",
    "Status",
    "SOs_Covered",
]
CAMPAIGN_SCHEDULE_SAFE_HEADERS = [
    "Campaign_ID",
    "Grade",
    "Section_mm",
    "Total_MT",
    "Heats",
    "Heats_Calc_Method",
    "EAF_Start",
    "CCM_Start",
    "RM_Start",
    "RM_End",
    "Duration_Hrs",
    "Due_Date",
    "Margin_Hrs",
    "Status",
    "SOs_Covered",
]
SCENARIO_BASE_HEADERS = [
    "Scenario",
    "Heats",
    "Campaigns",
    "Released",
    "Held",
    "On_Time_%",
    "Weighted_Lateness_Hrs",
    "Bottleneck",
    "Throughput_MT_Day",
    "Avg_Margin_Hrs",
    "Solver",
    "Overloaded",
]
CTP_REQUEST_HEADERS = [
    "Request_ID",
    "SKU_ID",
    "Qty_MT",
    "Requested_Date",
    "Notes",
]
CTP_OUTPUT_HEADERS = [
    "Request_ID",
    "SKU_ID",
    "Qty_MT",
    "Requested_Date",
    "Earliest_Completion",
    "Plant_Completion_Feasible",
    "Earliest_Delivery",
    "Delivery_Feasible",
    "Lateness_Days",
    "Inventory_Lineage",
    "Material_Gaps",
    "Campaign_Action",
    "Merged_Campaigns",
    "New_Campaigns",
    "Solver_Status",
]
SCENARIO_DEFAULT_RESOURCE_HEADERS = [
    "BF-01",
    "EAF-01",
    "EAF-02",
    "LRF-01",
    "LRF-02",
    "LRF-03",
    "VD-01",
    "CCM-01",
    "CCM-02",
    "RM-01",
    "RM-02",
]
SCENARIO_OUTPUT_HEADERS = SCENARIO_BASE_HEADERS + SCENARIO_DEFAULT_RESOURCE_HEADERS
SCENARIO_OUTPUT_SAFE_HEADERS = list(SCENARIO_OUTPUT_HEADERS)
BOM_OUTPUT_HEADERS = [
    "Plant",
    "Stage",
    "Material_Type",
    "Material_Category",
    "Parent_SKUs",
    "SKU_ID",
    "SKU_Name",
    "BOM_Level",
    "Gross_Req",
    "Available_Before",
    "Covered_By_Stock",
    "Produced_Qty",
    "Net_Req",
    "Status",
]
MATERIAL_PLAN_HEADERS = [
    "Campaign_ID",
    "Plant",
    "Material_Type",
    "Material_SKU",
    "Material_Name",
    "Required_Qty",
    "Available_Before",
    "Consumed",
    "Remaining_After",
    "Status",
]
RELEASED_STATUSES = {"RELEASED", "RUNNING LOCK"}
XL_CHART_COLUMN_CLUSTERED = 51
XL_CHART_PIE = 5
XL_CHART_LINE = 4
PLANT_SORT_ORDER = {
    "Rolling Mill": 0,
    "SMS": 1,
    "Blast Furnace": 2,
    "Shared Stores": 3,
    "Other": 9,
}
MATERIAL_TYPE_SORT_ORDER = {
    "Finished Good Demand": 0,
    "RM Output": 1,
    "CCM Output": 2,
    "VD Output": 3,
    "LRF Output": 4,
    "EAF Output": 5,
    "EAF Raw Charge": 6,
    "Hot Metal": 7,
    "BF Raw Mix": 8,
    "BF Raw Material": 9,
    "SMS Raw Material": 10,
    "Shared Flux": 11,
    "BF Byproduct": 12,
    "EAF Byproduct": 13,
    "LRF Byproduct": 14,
    "VD Byproduct": 15,
    "CCM Byproduct": 16,
    "RM Byproduct": 17,
    "Byproduct/Waste": 18,
    "Raw Material": 19,
    "Material": 20,
}


def _wb():
    return xw.Book.caller()


def _sheet_names(wb):
    return [sheet.name for sheet in wb.sheets]


def _assert_supported_workbook(wb, config=None):
    wb_name = wb.name.lower()
    configured_name = str((config or {}).get("Workbook_Name") or "").strip().lower()
    if configured_name:
        wb_stem = Path(wb.name).stem.lower()
        if wb_name == configured_name or wb_stem == configured_name:
            return
    if wb_name not in SUPPORTED_WORKBOOKS:
        raise RuntimeError(
            f"This APS build only supports APS_BF_SMS_RM.xlsm. "
            f"You are running it from '{wb.name}'. Open APS_BF_SMS_RM.xlsm and run the buttons there."
        )


def _clean_dataframe(df):
    if df is None:
        return pd.DataFrame()

    df = df.copy()
    df = df.dropna(axis=1, how="all")
    df.columns = [
        f"Unnamed_{idx}" if col is None or pd.isna(col) else str(col).strip()
        for idx, col in enumerate(df.columns)
    ]
    keep_cols = [col for col in df.columns if col and not col.startswith("Unnamed_")]
    if keep_cols:
        df = df[keep_cols]
    return df


def _read_sheet_table(ws, starts, required_columns):
    last_columns = []
    for start in starts:
        try:
            df = ws.range(start).options(pd.DataFrame, header=True, index=False, expand="table").value
        except Exception:
            continue

        df = _clean_dataframe(df)
        cols = list(df.columns)
        last_columns = cols
        if all(col in cols for col in required_columns):
            return df

    raise KeyError(
        f"Could not find columns {required_columns} on sheet '{ws.name}'. Found columns: {last_columns}"
    )


def _read_changeover_matrix(ws):
    values = ws.used_range.value
    if not values:
        return pd.DataFrame()
    if not isinstance(values, list):
        values = [values]
    if values and not isinstance(values[0], list):
        values = [values]

    header_idx = None
    headers = []
    for idx, row in enumerate(values):
        row = row if isinstance(row, list) else [row]
        first = str(row[0] or "").strip()
        if first.startswith("From"):
            headers = [str(val).strip() for val in row[1:] if val not in (None, "")]
            header_idx = idx
            break
    if header_idx is None or not headers:
        return pd.DataFrame()

    records = []
    for row in values[header_idx + 1 :]:
        row = row if isinstance(row, list) else [row]
        first = str(row[0] or "").strip()
        if not first or first.startswith("Legend"):
            break
        if first not in headers:
            continue
        row_values = list(row[1 : 1 + len(headers)])
        record = {"Grade": first}
        for col_name, val in zip(headers, row_values):
            record[col_name] = pd.to_numeric(pd.Series([val]), errors="coerce").fillna(0).iloc[0]
        records.append(record)

    if not records:
        return pd.DataFrame()
    return pd.DataFrame(records).set_index("Grade")


def _read_config(wb) -> dict:
    config = dict(CONFIG_DEFAULTS)
    if "Config" not in _sheet_names(wb):
        return config
    try:
        df = _read_sheet_table(wb.sheets["Config"], ("A3", "A1"), ("Key", "Value"))
    except Exception:
        return config
    df = df.dropna(subset=["Key"])
    for _, row in df.iterrows():
        key = str(row.get("Key", "")).strip()
        value = row.get("Value")
        if key in config and value is not None and not pd.isna(value):
            config[key] = value
    return config


def _config_flag(config: dict | None, key: str, default: str = "N") -> bool:
    value = str((config or {}).get(key, default) or default).strip().upper()
    return value in {"Y", "YES", "TRUE", "1", "ON"}


def _config_choice(config: dict | None, key: str, default: str, aliases: dict[str, str]) -> str:
    raw_value = str((config or {}).get(key, default) or default).strip().upper()
    normalized = aliases.get(raw_value)
    if normalized is None:
        allowed = ", ".join(sorted(dict.fromkeys(aliases.values())))
        raise ValueError(f"Config > {key} must be one of: {allowed}.")
    return normalized


def _assert_workbook_policy(
    config: dict | None,
    *,
    require_strict_masters: bool = False,
    require_deferred_byproducts: bool = False,
    require_strict_bom_structure: bool = False,
    require_no_legacy_heat_fallback: bool = False,
    require_strict_campaign_serialization: bool = False,
    require_preserve_exact_manual_groups: bool = False,
) -> None:
    violations = []
    if require_strict_masters and _config_flag(config, "Allow_Scheduler_Default_Masters", "N"):
        violations.append(
            "Config > Allow_Scheduler_Default_Masters must remain N for workbook production runs."
        )
    if require_strict_bom_structure:
        bom_mode = _config_choice(
            config,
            "BOM_Structure_Error_Mode",
            "RAISE",
            {
                "RAISE": "RAISE",
                "HARD_FAIL": "RAISE",
                "FAIL": "RAISE",
                "RECORD": "RECORD",
                "HOLD": "RECORD",
            },
        )
        if bom_mode != "RAISE":
            violations.append(
                "Config > BOM_Structure_Error_Mode must remain RAISE for workbook production runs."
            )
    if require_no_legacy_heat_fallback and _config_flag(config, "Allow_Legacy_Primary_Batch_Fallback", "N"):
        violations.append(
            "Config > Allow_Legacy_Primary_Batch_Fallback must remain N for workbook production runs."
        )
    if require_strict_campaign_serialization:
        serialization_mode = _config_choice(
            config,
            "Campaign_Serialization_Mode",
            "STRICT_END_TO_END",
            {
                "STRICT": "STRICT_END_TO_END",
                "END_TO_END": "STRICT_END_TO_END",
                "STRICT_END_TO_END": "STRICT_END_TO_END",
                "SMS": "OVERLAP_AFTER_SMS",
                "SMS_ONLY": "OVERLAP_AFTER_SMS",
                "PRIMARY_BATCH": "OVERLAP_AFTER_SMS",
                "OVERLAP_AFTER_SMS": "OVERLAP_AFTER_SMS",
            },
        )
        if serialization_mode != "STRICT_END_TO_END":
            violations.append(
                "Config > Campaign_Serialization_Mode must remain STRICT_END_TO_END for workbook production runs."
            )
    if require_preserve_exact_manual_groups:
        manual_group_mode = _config_choice(
            config,
            "Manual_Campaign_Grouping_Mode",
            "PRESERVE_EXACT",
            {
                "PRESERVE_EXACT": "PRESERVE_EXACT",
                "PRESERVE": "PRESERVE_EXACT",
                "EXACT": "PRESERVE_EXACT",
                "NO_SPLIT": "PRESERVE_EXACT",
                "SPLIT_TO_MAX": "SPLIT_TO_MAX",
                "SPLIT": "SPLIT_TO_MAX",
                "RESPECT_MAX": "SPLIT_TO_MAX",
            },
        )
        if manual_group_mode != "PRESERVE_EXACT":
            violations.append(
                "Config > Manual_Campaign_Grouping_Mode must remain PRESERVE_EXACT for workbook production runs."
            )
    if require_deferred_byproducts:
        byproduct_mode = str((config or {}).get("Byproduct_Inventory_Mode", "DEFERRED") or "DEFERRED").strip().upper()
        if byproduct_mode != "DEFERRED":
            violations.append(
                "Config > Byproduct_Inventory_Mode must remain DEFERRED for workbook production runs."
            )
    if violations:
        raise ValueError(" ".join(violations))


def _read_queue_times(wb) -> dict:
    if "Queue_Times" not in _sheet_names(wb):
        return {}
    try:
        qt = _read_sheet_table(
            wb.sheets["Queue_Times"],
            ("A3", "A1"),
            ("From_Operation", "To_Operation"),
        )
    except Exception:
        return {}
    result = {}
    for _, row in qt.iterrows():
        from_op = str(row.get("From_Operation", "")).strip().upper()
        to_op = str(row.get("To_Operation", "")).strip().upper()
        if not from_op or not to_op:
            continue
        raw_min = pd.to_numeric(row.get("Min_Queue_Min", 0), errors="coerce")
        raw_max = pd.to_numeric(row.get("Max_Queue_Min", 9999), errors="coerce")
        result[(from_op, to_op)] = {
            "min": int(raw_min) if pd.notna(raw_min) else 0,
            "max": int(raw_max) if pd.notna(raw_max) else 9999,
            "enforcement": str(row.get("Enforcement", "Hard")).strip() or "Hard",
        }
    return result


def _build_operation_fill(resources: pd.DataFrame) -> dict:
    if resources is None or getattr(resources, "empty", True):
        return dict(OPERATION_FILL)
    if "Operation_Group" not in resources.columns or "Operation_Color" not in resources.columns:
        return dict(OPERATION_FILL)

    fill = {}
    for _, row in resources.drop_duplicates(subset=["Operation_Group"]).iterrows():
        op_group = str(row.get("Operation_Group", "")).strip().upper()
        hex_color = str(row.get("Operation_Color", "")).strip().lstrip("#")
        if not op_group or len(hex_color) != 6:
            continue
        try:
            fill[op_group] = (
                int(hex_color[0:2], 16),
                int(hex_color[2:4], 16),
                int(hex_color[4:6], 16),
            )
        except ValueError:
            continue
    return fill or dict(OPERATION_FILL)


def _scenario_output_headers(resources: pd.DataFrame | None = None) -> list[str]:
    if resources is None or getattr(resources, "empty", True) or "Resource_ID" not in resources.columns:
        return list(SCENARIO_OUTPUT_HEADERS)
    resource_ids = (
        resources["Resource_ID"]
        .dropna()
        .astype(str)
        .str.strip()
        .loc[lambda s: s.ne("")]
        .drop_duplicates()
        .tolist()
    )
    return list(SCENARIO_BASE_HEADERS) + resource_ids if resource_ids else list(SCENARIO_OUTPUT_HEADERS)


def _resource_plant_map(resources: pd.DataFrame | None = None) -> dict:
    if resources is None or getattr(resources, "empty", True):
        return {}
    if not {"Resource_ID", "Plant"}.issubset(set(resources.columns)):
        return {}
    resource_df = (
        resources[["Resource_ID", "Plant"]]
        .copy()
        .dropna(subset=["Resource_ID"])
        .drop_duplicates(subset=["Resource_ID"])
    )
    return {
        str(row["Resource_ID"]).strip(): str(row.get("Plant", "")).strip() or "Other"
        for _, row in resource_df.iterrows()
    }


def _resource_plant(resource_id, plant_map=None):
    resource_text = str(resource_id or "").strip()
    if plant_map and resource_text in plant_map:
        return plant_map[resource_text]
    resource_upper = resource_text.upper()
    if resource_upper.startswith(("EAF", "LRF", "VD", "CCM")):
        return "SMS"
    if resource_upper.startswith("RM"):
        return "Rolling Mill"
    if resource_upper.startswith("BF"):
        return "Blast Furnace"
    return "Other"


def _inject_rush_order(so: pd.DataFrame, rush_order_mt: float):
    if so.empty or rush_order_mt <= 0:
        return so

    candidates = so.copy()
    candidates["Delivery_Date"] = pd.to_datetime(candidates.get("Delivery_Date"), errors="coerce")
    candidates = candidates.sort_values(
        ["Delivery_Date", "Order_Date", "SO_ID"],
        kind="stable",
    ).reset_index(drop=True)
    template = candidates.iloc[0].copy()
    now = datetime.now()
    template["SO_ID"] = f"RUSH-{now.strftime('%d%H%M')}"
    template["Order_Qty_MT"] = round(float(rush_order_mt), 3)
    if "Order_Qty" in template.index:
        template["Order_Qty"] = round(float(rush_order_mt), 3)
    template["Order_Date"] = now
    template["Delivery_Date"] = now + timedelta(days=1)
    template["Priority"] = "URGENT"
    template["Status"] = "Open"
    template["Customer"] = template.get("Customer", "Rush Customer")
    template["Region"] = template.get("Region", "Rush")
    template["Campaign_Group"] = template.get("Campaign_Group") or template.get("Grade", "Rush")
    return pd.concat([so, pd.DataFrame([template])], ignore_index=True)


def _read_all(wb):
    data = {}
    data["config"] = _read_config(wb)
    _assert_supported_workbook(wb, config=data["config"])
    main_starts = ("A3", "A1")
    scenario_starts = ("A3", "A2")

    data["sales_orders"] = _read_sheet_table(
        wb.sheets["Sales_Orders"], main_starts, ("SO_ID", "SKU_ID", "Delivery_Date")
    )
    data["resources"] = _read_sheet_table(
        wb.sheets["Resource_Master"], main_starts, ("Resource_ID", "Resource_Name", "Avail_Hours_Day")
    )
    data["skus"] = _read_sheet_table(wb.sheets["SKU_Master"], main_starts, ("SKU_ID", "SKU_Name"))
    data["routing"] = _read_sheet_table(
        wb.sheets["Routing"], main_starts, ("SKU_ID", "Operation")
    )
    data["changeover"] = _read_changeover_matrix(wb.sheets["Changeover_Matrix"])
    data["queue_times"] = _read_queue_times(wb)
    data["bom"] = _read_sheet_table(
        wb.sheets["BOM"], main_starts, ("Parent_SKU", "Child_SKU", "Qty_Per")
    )
    data["inventory"] = _read_sheet_table(
        wb.sheets["Inventory"], main_starts, ("SKU_ID", "Available_Qty")
    )

    try:
        sc = _read_sheet_table(wb.sheets["Scenarios"], scenario_starts, ("Parameter", "Value"))
        sc = sc.dropna(subset=["Parameter"])
        data["scenarios"] = sc.set_index("Parameter")
    except KeyError:
        data["scenarios"] = pd.DataFrame(
            {
                "Value": [
                    15.0,
                    8.0,
                    "EAF-01",
                    0.0,
                    data["config"].get("Default_Solver_Limit_Sec", 30.0),
                    data["config"].get("Planning_Horizon_Days", 14),
                    0.0,
                    0.0,
                    0.0,
                    data["config"].get("Min_Campaign_MT", 100.0),
                    data["config"].get("Max_Campaign_MT", 500.0),
                ]
            },
            index=pd.Index(
                [
                    "Demand Spike (%)",
                    "Machine Down (Hrs)",
                    "Machine Down Resource",
                    "Machine Down Start (Hr)",
                    "Solver Time Limit (sec)",
                    "Planning Horizon (Days)",
                    "Yield Loss (%)",
                    "Rush Order MT",
                    "Extra Shift Hours",
                    "Min Campaign MT",
                    "Max Campaign MT",
                ],
                name="Parameter",
            ),
        )

    so = data["sales_orders"]
    so["Delivery_Date"] = pd.to_datetime(so["Delivery_Date"])
    so["Order_Date"] = pd.to_datetime(so["Order_Date"])
    so["Status"] = so["Status"].fillna("Open") if "Status" in so.columns else "Open"
    if "Order_Qty_MT" not in so.columns and "Order_Qty" in so.columns:
        so["Order_Qty_MT"] = pd.to_numeric(so["Order_Qty"], errors="coerce").fillna(0)
    if "Order_Qty" not in so.columns and "Order_Qty_MT" in so.columns:
        so["Order_Qty"] = pd.to_numeric(so["Order_Qty_MT"], errors="coerce").fillna(0)
    default_section = float(data["config"].get("Default_Section_Fallback", 6.5) or 6.5)
    so["Section_mm"] = pd.to_numeric(so.get("Section_mm", default_section), errors="coerce").fillna(default_section)
    so["Order_Qty_MT"] = pd.to_numeric(so.get("Order_Qty_MT", 0), errors="coerce").fillna(0)
    data["sales_orders"] = so

    resources = data["resources"].copy()
    if "Plant" not in resources.columns:
        resources["Plant"] = resources.get("Location", resources.get("Resource_Type", "Plant"))
    data["resources"] = resources
    data["operation_fill"] = _build_operation_fill(resources)
    data["scenario_output_headers"] = _scenario_output_headers(resources)
    data["resource_plant_map"] = _resource_plant_map(resources)

    return data


def _get_params(data):
    config = data.get("config", {})
    try:
        min_cmt = float(data["scenarios"].loc["Min Campaign MT", "Value"])
        max_cmt = float(data["scenarios"].loc["Max Campaign MT", "Value"])
    except (KeyError, TypeError):
        min_cmt = float(config.get("Min_Campaign_MT", 100.0) or 100.0)
        max_cmt = float(config.get("Max_Campaign_MT", 500.0) or 500.0)
    return min_cmt, max_cmt


def _config_value(data, key, default=None, cast=None):
    value = (data.get("config") or {}).get(key, default)
    if value in ("", None) or pd.isna(value):
        return default
    if cast is None:
        return value
    try:
        return cast(value)
    except Exception:
        return default


def _scenario_value(data, key, default=None, cast=float):
    try:
        value = data["scenarios"].loc[key, "Value"]
    except (KeyError, TypeError):
        return default
    if value in ("", None) or pd.isna(value):
        return default
    if cast is None:
        return value
    try:
        return cast(value)
    except Exception:
        return default


def _planning_horizon_days(data):
    default = _config_value(data, "Planning_Horizon_Days", 14, float)
    return max(int(float(_scenario_value(data, "Planning Horizon (Days)", default, float))), 1)


def _deterministic_planning_start(horizon_days, frozen_jobs=None, anchor_dates=None):
    candidates = []
    for frozen in (frozen_jobs or {}).values():
        ts = pd.to_datetime((frozen or {}).get("Planned_Start"), errors="coerce")
        if pd.notna(ts):
            candidates.append(ts.to_pydatetime())

    raw_anchor_dates = [] if anchor_dates is None else list(anchor_dates)
    anchor_series = pd.to_datetime(pd.Series(raw_anchor_dates, dtype=object), errors="coerce").dropna()
    if not anchor_series.empty:
        anchor_dt = anchor_series.min() - pd.Timedelta(days=max(int(horizon_days or 14), 1))
        candidates.append(anchor_dt.to_pydatetime())

    if not candidates:
        return datetime(2000, 1, 1)
    anchor = min(candidates)
    return anchor.replace(minute=0, second=0, microsecond=0)


def _apply_planning_overrides(data):
    adjusted = dict(data)
    so = data["sales_orders"].copy()
    resources = data["resources"].copy()

    demand_spike = float(_scenario_value(data, "Demand Spike (%)", 0.0, float) or 0.0)
    if demand_spike:
        factor = 1 + demand_spike / 100.0
        so["Order_Qty_MT"] = (pd.to_numeric(so["Order_Qty_MT"], errors="coerce").fillna(0) * factor).round(1)
        if "Order_Qty" in so.columns:
            so["Order_Qty"] = (pd.to_numeric(so["Order_Qty"], errors="coerce").fillna(0) * factor).round(1)

    horizon_days = _planning_horizon_days(data)
    down_hrs = max(float(_scenario_value(data, "Machine Down (Hrs)", 0.0, float) or 0.0), 0.0)
    down_resource = str(_scenario_value(data, "Machine Down Resource", "", lambda v: str(v).strip()) or "").strip()
    down_start_hr = max(float(_scenario_value(data, "Machine Down Start (Hr)", 0.0, float) or 0.0), 0.0)
    solver_default = float(_config_value(data, "Default_Solver_Limit_Sec", 30.0, float) or 30.0)
    solver_time_limit_sec = max(float(_scenario_value(data, "Solver Time Limit (sec)", solver_default, float) or solver_default), 1.0)
    yield_loss_pct = max(float(_scenario_value(data, "Yield Loss (%)", 0.0, float) or 0.0), 0.0)
    rush_order_mt = max(float(_scenario_value(data, "Rush Order MT", 0.0, float) or 0.0), 0.0)
    extra_shift_hours = max(float(_scenario_value(data, "Extra Shift Hours", 0.0, float) or 0.0), 0.0)

    if rush_order_mt > 0:
        so = _inject_rush_order(so, rush_order_mt)

    resources["Avail_Hours_Day"] = pd.to_numeric(resources["Avail_Hours_Day"], errors="coerce").fillna(20)
    if extra_shift_hours > 0:
        resources["Avail_Hours_Day"] = resources["Avail_Hours_Day"] + extra_shift_hours
    if down_hrs and down_resource:
        mask = resources["Resource_ID"].astype(str).str.strip() == down_resource
        if mask.any():
            resources.loc[mask, "Avail_Hours_Day"] = (
                resources.loc[mask, "Avail_Hours_Day"] - down_hrs / horizon_days
            ).clip(lower=0)

    adjusted["sales_orders"] = so
    adjusted["resources"] = resources
    adjusted["_planning"] = {
        "demand_spike_pct": demand_spike,
        "machine_down_hours": down_hrs,
        "machine_down_resource": down_resource or None,
        "machine_down_start_hour": down_start_hr,
        "planning_horizon_days": horizon_days,
        "solver_time_limit_sec": solver_time_limit_sec,
        "yield_loss_pct": yield_loss_pct,
        "rush_order_mt": rush_order_mt,
        "extra_shift_hours": extra_shift_hours,
    }
    return adjusted


def _write_df(ws, df, start_cell="A2", clear_range=None):
    if clear_range:
        ws.range(clear_range).clear_contents()
    if df is not None and not df.empty:
        ws.range(start_cell).options(pd.DataFrame, header=False, index=False).value = df


def _status(wb, msg):
    try:
        wb.sheets["KPI_Dashboard"].range("A2").value = msg
    except Exception:
        pass


def _campaigns_from_data(data):
    min_cmt, max_cmt = _get_params(data)
    return build_campaigns(
        data["sales_orders"],
        min_cmt,
        max_cmt,
        inventory=data.get("inventory"),
        bom=data.get("bom"),
        config=data.get("config"),
        skus=data.get("skus"),
        yield_loss_pct=float(data.get("_planning", {}).get("yield_loss_pct", 0.0) or 0.0),
    )


def _sheet_headers(ws, width=20, row=None, required_headers=None):
    row = row or _find_header_row(ws, required_headers=required_headers, max_cols=width)
    if row is None:
        return []
    values = ws.range((row, 1), (row, width)).value
    headers = []
    for val in values:
        if val in (None, ""):
            if headers:
                break
            continue
        headers.append(str(val).strip())
    return headers


def _find_header_row(ws, required_headers=None, max_rows=12, max_cols=30):
    required_headers = set(required_headers or [])
    for row_idx in range(1, max_rows + 1):
        values = ws.range((row_idx, 1), (row_idx, max_cols)).value
        if not isinstance(values, list):
            values = [values]

        headers = []
        for val in values:
            if val in (None, ""):
                if headers:
                    break
                continue
            headers.append(str(val).strip())

        if headers and (not required_headers or required_headers.issubset(headers)):
            return row_idx

    return None


def _clear_table_data(ws, header_row, width, max_rows):
    if not header_row:
        return
    try:
        last_row = min(max(int(ws.used_range.last_cell.row), header_row + 1), max_rows)
    except Exception:
        last_row = max_rows
    if last_row < header_row + 1:
        last_row = header_row + 1
    ws.range((header_row + 1, 1), (last_row, width)).clear_contents()


def _restore_output_prompt(ws, header_row):
    prompt = OUTPUT_PROMPTS.get(ws.name)
    if prompt:
        ws.range((header_row + 1, 1)).value = prompt


def _sheet_header_df(ws, required_headers, width=30):
    header_row = _find_header_row(ws, required_headers=required_headers, max_cols=width)
    headers = _sheet_headers(ws, width=width, row=header_row)
    if not header_row or not headers:
        return pd.DataFrame(), header_row, headers

    last_row = ws.used_range.last_cell.row
    if last_row <= header_row:
        return pd.DataFrame(columns=headers), header_row, headers

    try:
        df = ws.range((header_row, 1), (last_row, len(headers))).options(
            pd.DataFrame, header=True, index=False
        ).value
    except Exception:
        return pd.DataFrame(columns=headers), header_row, headers

    df = _clean_dataframe(df)
    return df, header_row, headers


def _campaign_sheet_headers(ws, header_row):
    actual_headers = _sheet_headers(ws, width=25, row=header_row)
    if actual_headers and all(header in CAMPAIGN_SCHEDULE_HEADERS for header in actual_headers):
        return actual_headers
    return CAMPAIGN_SCHEDULE_SAFE_HEADERS


def _read_running_jobs(wb):
    if "Schedule_Output" not in _sheet_names(wb):
        return {}

    ws = wb.sheets["Schedule_Output"]
    df, _, _ = _sheet_header_df(
        ws,
        required_headers={"Job_ID", "Resource_ID", "Planned_Start", "Planned_End", "Status"},
        width=25,
    )
    if df.empty:
        return {}

    running = {}
    status_series = df["Status"] if "Status" in df.columns else pd.Series("", index=df.index)
    if isinstance(status_series, pd.DataFrame):
        status_series = status_series.iloc[:, 0] if not status_series.empty else pd.Series("", index=df.index)
    resolved_status = status_series.fillna("").astype(str).str.strip()
    df = df[resolved_status.str.upper().isin(RUNNING_STATUSES)].copy()
    resolved_status = resolved_status.loc[df.index]
    for idx, row in df.iterrows():
        job_id = str(row.get("Job_ID", "")).strip()
        resource_id = str(row.get("Resource_ID", "")).strip()
        if not job_id or not resource_id:
            continue
        try:
            start_ts = pd.to_datetime(row["Planned_Start"])
            end_ts = pd.to_datetime(row["Planned_End"])
        except Exception:
            continue
        if pd.isna(start_ts) or pd.isna(end_ts):
            continue
        running[job_id] = {
            "Resource_ID": resource_id,
            "Planned_Start": start_ts.to_pydatetime(),
            "Planned_End": end_ts.to_pydatetime(),
            "Status": resolved_status.loc[idx],
        }
    return running


def _campaign_id_from_job(job_id: str) -> str:
    job = str(job_id or "").strip()
    if not job:
        return ""
    if "-PO" in job:
        return job.split("-PO", 1)[0]
    if "-H" in job:
        return job.split("-H", 1)[0]
    if job.endswith("-RM"):
        return job.rsplit("-RM", 1)[0]
    return ""


def _held_campaign_rows(campaigns: list) -> pd.DataFrame:
    held_rows = []
    for camp in campaigns:
        held_rows.append(
            {
                "Campaign_ID": camp["campaign_id"],
                "Campaign_Group": camp.get("campaign_group", ""),
                "Grade": camp["grade"],
                "Section_mm": camp.get("section_mm", ""),
                "Sections_Covered": camp.get("sections_covered", ""),
                "Total_MT": camp["total_coil_mt"],
                "Heats": camp["heats"],
                "Heats_Calc_Method": camp.get("heats_calc_method", ""),
                "Heats_Calc_Warnings": _format_heats_calc_warnings(camp.get("heats_calc_warnings")),
                "Order_Count": camp.get("order_count", len(camp.get("so_ids", []))),
                "Priority": camp.get("priority", ""),
                "Release_Status": camp.get("release_status", "MATERIAL HOLD"),
                "Material_Issue": camp.get("material_issue", ""),
                "EAF_Start": "",
                "CCM_Start": "",
                "Resource_ID": "",
                "RM_Start": "",
                "RM_End": "",
                "Due_Date": pd.to_datetime(camp["due_date"]).strftime("%Y-%m-%d"),
                "Status": "MATERIAL HOLD",
                "SOs_Covered": ", ".join(camp.get("so_ids", [])),
            }
        )
    return pd.DataFrame(held_rows)


def _ensure_sheet(wb, name, after_sheet=None):
    if name in _sheet_names(wb):
        return wb.sheets[name]

    if after_sheet and after_sheet in _sheet_names(wb):
        return wb.sheets.add(name, after=wb.sheets[after_sheet])
    return wb.sheets.add(name, after=wb.sheets[-1])


def _control_panel_formula():
    return '=HYPERLINK("#\'Control_Panel\'!A1","Back to Control Panel")'


def _safe_set_alignment(rng, horizontal=None, vertical=None, attempts=6, delay_sec=0.05):
    for attempt in range(attempts):
        try:
            if horizontal is not None:
                rng.api.HorizontalAlignment = horizontal
            if vertical is not None:
                rng.api.VerticalAlignment = vertical
            return True
        except Exception:
            if attempt == attempts - 1:
                return False
            time.sleep(delay_sec)
    return False


def _clear_legacy_doc_panel(ws, start_col, width=6, end_row=20):
    end_col = start_col + width - 1
    try:
        ws.range((1, start_col), (end_row, end_col)).api.UnMerge()
    except Exception:
        pass
    try:
        ws.range((1, start_col), (end_row, end_col)).clear_contents()
        ws.range((1, start_col), (end_row, end_col)).color = None
    except Exception:
        pass


def _has_legacy_doc_panel(ws, start_col):
    """Only clear the old inline guide when the actual guide title is present."""
    try:
        title = ws.range((1, start_col)).value
    except Exception:
        return False
    return isinstance(title, str) and title.strip().lower() == "sheet guide"


def _remove_inline_sheet_guides(wb):
    for sheet_name, (start_col, width) in LEGACY_GUIDE_LAYOUTS.items():
        if sheet_name not in _sheet_names(wb):
            continue
        ws = wb.sheets[sheet_name]
        if not _has_legacy_doc_panel(ws, start_col):
            continue
        _clear_legacy_doc_panel(ws, start_col, width=width, end_row=24)


def _render_help_sheet(wb):
    ws = _ensure_sheet(wb, HELP_SHEET, after_sheet="Control_Panel")
    ws.api.Cells.Clear()
    _style_title_block(
        ws,
        "APS Help",
        "Central workbook reference. Inline sheet guides have been removed so the working sheets stay clean.",
    )
    ws.range("A4").value = (
        "Use Control_Panel to run the APS. Use this Help tab for sheet meanings, what to edit, "
        "and what each output is for."
    )
    ws.range("A4").font.italic = True
    ws.range("A4").font.color = (128, 128, 128)
    headers = ["Sheet", "Type", "What It Is For", "How To Use It"]
    _style_header_row(ws, 6, headers)
    ws.range((7, 1)).value = HELP_SHEET_ROWS
    ws.range((7, 1), (6 + len(HELP_SHEET_ROWS), 4)).api.WrapText = True
    ws.range((7, 1), (6 + len(HELP_SHEET_ROWS), 4)).api.VerticalAlignment = -4160
    for col_idx, width in enumerate((20, 16, 42, 72), start=1):
        ws.range((1, col_idx)).column_width = width
    for row_idx in range(7, 7 + len(HELP_SHEET_ROWS)):
        try:
            ws.range((row_idx, 1), (row_idx, 4)).api.Borders.Weight = 2
        except Exception:
            pass
    try:
        ws.activate()
        ws.range("A7").select()
        window = ws.book.app.api.ActiveWindow
        try:
            window.FreezePanes = False
        except Exception:
            pass
        window.SplitRow = 6
        window.SplitColumn = 0
        window.FreezePanes = True
        window.Zoom = 90
    except Exception:
        pass


def _prepare_help_environment(wb):
    _remove_inline_sheet_guides(wb)
    # Only render the Help sheet if it is missing or empty — it is static content
    try:
        help_ws = wb.sheets[HELP_SHEET]
        has_content = help_ws.range("A6").value is not None
    except Exception:
        has_content = False
    if not has_content:
        _render_help_sheet(wb)
    _ensure_control_panel_links(wb)


def _safe_sheet_token(sheet_name):
    return "".join(ch if ch.isalnum() else "_" for ch in str(sheet_name or ""))


def _has_nav_button(ws):
    safe_sheet = _safe_sheet_token(ws.name)
    expected_prefix = f"btn_{safe_sheet}_GoToControlPanel_"
    try:
        for idx in range(1, ws.api.Shapes.Count + 1):
            shp = ws.api.Shapes.Item(idx)
            if str(shp.Name).startswith(expected_prefix):
                return True
    except Exception:
        pass
    return False


def _ensure_control_panel_links(wb):
    for ws in wb.sheets:
        if ws.name == "Control_Panel":
            continue
        for cell_ref in ("X1", "AA1"):
            try:
                ws.range(cell_ref).clear_contents()
            except Exception:
                pass
        if _has_nav_button(ws):
            continue
        link_cell = "AA1" if ws.name == "Schedule_Output" else "X1"
        link = ws.range(link_cell)
        link.formula = _control_panel_formula()
        link.font.bold = True
        link.font.color = TITLE_COLOR
        link.color = LIGHT_BLUE


def _style_header_row(ws, row, headers):
    ws.range((row, 1)).value = headers
    hdr_rng = ws.range((row, 1), (row, len(headers)))
    hdr_rng.color = HEADER_COLOR
    hdr_rng.font.color = (255, 255, 255)
    hdr_rng.font.bold = True


def _ensure_header_layout(ws, header_row, headers):
    try:
        ws.range((header_row, 1), (header_row, 18)).clear_contents()
    except Exception:
        pass
    _style_header_row(ws, header_row, headers)
    for idx in range(1, len(headers) + 1):
        try:
            ws.range((header_row, idx)).column_width = max(ws.range((header_row, idx)).column_width, 16)
        except Exception:
            ws.range((header_row, idx)).column_width = 16


def _style_title_block(ws, title, subtitle):
    ws.range("A1").value = title
    ws.range("A1").font.bold = True
    ws.range("A1").font.size = 14
    ws.range("A1").font.color = TITLE_COLOR
    ws.range("A2").value = subtitle
    ws.range("A2").font.italic = True
    ws.range("A2").font.color = (128, 128, 128)


def _write_doc_panel(ws, start_col, lines, width=6):
    end_col = start_col + width - 1
    panel_end_row = max(len(lines) + 2, 12)
    for col_idx in range(start_col, end_col + 1):
        try:
            ws.range((1, col_idx)).column_width = max(ws.range((1, col_idx)).column_width, 13)
        except Exception:
            pass
    try:
        ws.range((1, start_col), (panel_end_row, end_col)).api.UnMerge()
    except Exception:
        pass
    try:
        ws.range((1, start_col), (panel_end_row, end_col)).clear_contents()
        ws.range((1, start_col), (panel_end_row, end_col)).color = None
    except Exception:
        pass
    head = ws.range((1, start_col), (1, end_col))
    head.merge()
    head.value = "Sheet Guide"
    head.color = LIGHT_BLUE
    head.font.bold = True
    head.font.size = 11
    head.font.color = TITLE_COLOR
    try:
        head.api.HorizontalAlignment = -4131
    except Exception:
        pass

    for idx, line in enumerate(lines, start=2):
        rng = ws.range((idx, start_col), (idx, end_col))
        rng.merge()
        rng.value = line
        rng.color = DOC_FILL
        rng.font.color = (79, 79, 79)
        rng.font.size = 10
        rng.api.WrapText = True
        try:
            rng.api.HorizontalAlignment = -4131
            rng.api.VerticalAlignment = -4160
        except Exception:
            pass


def _apply_schedule_window_layout(ws):
    try:
        ws.activate()
        ws.range("A4").select()
        window = ws.book.app.api.ActiveWindow
        try:
            window.FreezePanes = False
        except Exception:
            pass
        window.SplitRow = 3
        window.SplitColumn = 0
        window.FreezePanes = True
        window.Zoom = 90
    except Exception:
        pass


def _prepare_custom_output_sheet(ws, title, subtitle, headers, prompt, doc_lines=None, doc_start_col=12):
    ws.api.Cells.Clear()
    _style_title_block(ws, title, subtitle)
    _style_header_row(ws, 3, headers)
    ws.range((4, 1)).value = prompt
    ws.range((4, 1)).font.italic = True
    ws.range((4, 1)).font.color = (128, 128, 128)
    if doc_lines:
        _clear_legacy_doc_panel(ws, doc_start_col, width=6, end_row=20)


def _clear_grouped_output_body(ws, start_row, end_row, end_col):
    rng = ws.range((start_row, 1), (end_row, end_col))
    try:
        rng.api.UnMerge()
    except Exception:
        pass
    try:
        rng.clear_contents()
    except Exception:
        try:
            rng.api.Clear()
        except Exception:
            pass
    try:
        rng.color = None
        rng.font.bold = False
        rng.font.italic = False
        rng.font.color = (0, 0, 0)
    except Exception:
        pass


def _schedule_output_guide_lines(schedule_df, solver_status=""):
    if schedule_df is None or schedule_df.empty:
        summary_line = "Run 'Run Schedule' to populate the master view, equipment packets, and Gantt timeline."
        assignment_line = "Machine assignment: not available until a schedule is generated."
    else:
        sched = schedule_df.copy()
        sched["Planned_Start"] = pd.to_datetime(sched.get("Planned_Start"), errors="coerce")
        sched["Planned_End"] = pd.to_datetime(sched.get("Planned_End"), errors="coerce")
        start_dt = sched["Planned_Start"].min()
        end_dt = sched["Planned_End"].max()
        campaign_count = int(sched.get("Campaign", pd.Series(dtype=object)).nunique())
        resource_count = int(sched.get("Resource_ID", pd.Series(dtype=object)).nunique())
        late_count = int(
            sched.get("Status", pd.Series(dtype=object)).astype(str).str.strip().str.upper().eq("LATE").sum()
        )
        summary_bits = [solver_status or "Scheduled", f"{len(sched)} ops", f"{campaign_count} campaigns", f"{resource_count} resources"]
        if not pd.isna(start_dt) and not pd.isna(end_dt):
            summary_bits.append(f"{start_dt:%d-%b %H:%M} to {end_dt:%d-%b %H:%M}")
        if late_count:
            summary_bits.append(f"{late_count} late")
        summary_line = "Latest run: " + " | ".join(summary_bits)
        family_counts = (
            sched.assign(Resource_Family=sched.get("Resource_ID", pd.Series(dtype=object)).map(_resource_family))
            .groupby("Resource_Family")
            .size()
            .to_dict()
        )
        family_order = ["BF", "EAF", "LRF", "VD", "CCM", "RM"]
        assignment_bits = [f"{fam} {int(family_counts.get(fam, 0))}" for fam in family_order if fam in family_counts]
        assignment_line = "Machine assignment: " + (" | ".join(assignment_bits) if assignment_bits else "not available")

    return [
        "Role: Master planner table across SMS and RM.",
        summary_line,
        assignment_line,
        "Rows are grouped hierarchically as campaign headers, SMS heat sections, and RM dispatch sections.",
        "Campaigns are executed as strict PPC batches: campaign n+1 cannot start until campaign n is fully complete.",
        "Heat_No is the true SMS execution order inside the campaign, starting from the first EAF heat as Heat 1.",
        "SMS rows show pooled SO coverage and downstream size mix. RM rows show the exact finished-order dispatch.",
        "Use Equipment_Schedule for machine packets and Schedule_Gantt for the visual time view.",
    ]


def _refresh_schedule_output_shell(ws, schedule_df=None, solver_status=""):
    _style_title_block(
        ws,
        "Schedule Output",
        "Output: Hierarchical planner view  —  CAMPAIGN header  >  Heat rows (with resource route)  >  Operation detail  |  RM section: Rolling Orders  >  individual dispatch lines.",
    )
    _ensure_header_layout(ws, 3, SCHEDULE_OUTPUT_HEADERS)
    try:
        ws.range((1, 17), (6, 23)).clear_contents()
        ws.range((1, 17), (6, 23)).color = None
    except Exception:
        pass
    _clear_legacy_doc_panel(ws, 20, width=6, end_row=20)
    _apply_schedule_window_layout(ws)


def _refresh_campaign_schedule_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "Campaign Schedule",
        "Output: One row per released or held campaign. A campaign here means one PPC release batch of SOs executed end-to-end before the next campaign starts.",
        CAMPAIGN_SCHEDULE_HEADERS,
        OUTPUT_PROMPTS["Campaign_Schedule"],
        doc_lines=[
            "Role: Campaign-release summary, not a machine dispatch table.",
            "One row here = one PPC-released batch of sales orders that the plant executes as one continuous campaign.",
            "Campaign_Group is the planning family used to assemble the batch; once released, the campaign runs before the next campaign can start.",
            "Sections_Covered lists the downstream RM sizes served by that one campaign.",
            "Duration_Hrs shows end-to-end planned campaign span; Margin_Hrs shows slack to due date.",
            "Release_Status shows whether the campaign was released or held for material shortage.",
            "Heats_Calc_Method and Heats_Calc_Warnings make degraded or blocked heat logic explicit instead of hiding it in background metadata.",
        ],
        doc_start_col=22,
    )
    _apply_schedule_window_layout(ws)


def _refresh_bom_output_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "BOM Explosion Output",
        "Output: End-to-end material requirements grouped by plant, process stage, and material type. Updated by 'Run BOM Explosion'.",
        BOM_OUTPUT_HEADERS,
        OUTPUT_PROMPTS[BOM_OUTPUT_SHEET],
        doc_lines=[
            "Role: Total requirement view across the whole demand pool after inventory netting.",
            "BOM_Output answers: what does the plant network need in total, by plant and material type?",
            "It is aggregated across all open demand; it does not respect campaign release order.",
            "Do not expect line-for-line equality with Material_Plan; that sheet is campaign-sequenced and inventory-state-aware.",
            "Use Material_Plan when you need campaign-by-campaign commitment and shortage logic.",
        ],
        doc_start_col=15,
    )


def _refresh_capacity_map_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "Capacity Map",
        "Output: rough-cut heuristic load vs available hours by machine. This is not a finite-schedule mirror. Updated by 'Run Capacity Map'.",
        ["Resource_ID", "Resource_Name", "Plant", "Avail_Hrs_14d", "Demand_Hrs", "Idle_Hrs", "Overload_Hrs", "Utilisation_%", "Status", "Capacity_Basis"],
        OUTPUT_PROMPTS["Capacity_Map"],
        doc_lines=[
            "Role: Fast rough-cut load screen, not the exact finite-schedule answer.",
            "Demand_Hrs is allocated heuristically from campaign demand using routing and standard times.",
            "Queue constraints, frozen jobs, downtime interactions, and detailed sequencing can make this differ from the finite scheduler.",
            "Use Schedule_Output and the KPI dashboard when you need scheduler-aligned resource occupancy.",
        ],
        doc_start_col=11,
    )


def _refresh_material_plan_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "Material Plan",
        "Output: Campaign-by-campaign material allocation and shortage trace, grouped by plant. Updated by 'Run Schedule'.",
        MATERIAL_PLAN_HEADERS,
        OUTPUT_PROMPTS[MATERIAL_PLAN_SHEET],
        doc_lines=[
            "Role: Campaign release and material-commit trace.",
            "Material_Plan answers: what did each campaign actually consume, and what caused any hold?",
            "Unlike BOM_Output, this sheet is sequence-aware: later campaigns see inventory after earlier campaigns consume stock.",
            "Do not expect line-for-line equality with BOM_Output; this sheet reflects release sequence and current inventory state.",
            "Use this sheet with Campaign_Schedule when debugging material holds or release order.",
        ],
        doc_start_col=13,
    )


def _refresh_scenario_output_shell(ws, headers=None):
    headers = headers or list(SCENARIO_OUTPUT_HEADERS)
    _prepare_custom_output_sheet(
        ws,
        "Scenario Output",
        "Output: Comparison of baseline and what-if scenarios. Updated by 'Run Scenarios'.",
        headers,
        OUTPUT_PROMPTS[SCENARIO_OUTPUT_SHEET],
        doc_lines=[
            "Role: Scenario comparison sheet for what-if planning.",
            "Rows compare released/held campaigns, on-time %, weighted lateness, bottleneck, throughput, and resource utilisation.",
            "The best scenario row is highlighted automatically and utilisation columns are color-scaled for quick bottleneck reading.",
            "Use this to compare stress cases before accepting a plan change.",
            "The editable controls stay on the Scenarios input sheet.",
        ],
        doc_start_col=max(len(headers) + 2, 27),
    )


def _refresh_ctp_request_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "CTP Request",
        "Input: capable-to-promise request queue. Enter requested SKU, quantity, and delivery date, then run 'Run CTP'.",
        CTP_REQUEST_HEADERS,
        "Enter one request per row, then run 'Run CTP'.",
        doc_lines=[
            "Role: Planner request queue for capable-to-promise checks.",
            "Each row asks: if this extra demand is requested now, when can the plant realistically deliver it?",
            "The check respects committed campaign consumption, current inventory, routing, and the frozen current schedule.",
            "Use CTP_Output to review modeled delivery feasibility when available, material blockers, and whether the request can join an existing campaign.",
        ],
        doc_start_col=8,
    )


def _refresh_ctp_output_shell(ws):
    _prepare_custom_output_sheet(
        ws,
        "CTP Output",
        "Output: capable-to-promise response per request, including delivery feasibility when modeled, plus blockers and completion surrogate.",
        CTP_OUTPUT_HEADERS,
        OUTPUT_PROMPTS[CTP_OUTPUT_SHEET],
        doc_lines=[
            "Role: Promise-date response for ad hoc demand requests.",
            "Earliest_Completion and Plant_Completion_Feasible show the internal plant-finish surrogate explicitly.",
            "Delivery_Feasible is YES/NO only when customer-delivery timing is actually modeled; otherwise it shows UNMODELED.",
            "Campaign_Action, Merged_Campaigns, and New_Campaigns show whether the request rides an existing campaign, creates a new one, or does both.",
            "Earliest_Delivery stays blank when delivery is not modeled; earliest plant completion is kept internally as a surrogate only.",
            "Material_Gaps explains blockers when the request cannot be released cleanly.",
            "Inventory_Lineage shows whether CTP relied on authoritative committed inventory or a degraded fallback.",
            "Non-authoritative inventory lineage blocks workbook CTP promises instead of being hidden as background metadata.",
        ],
        doc_start_col=15,
    )


def _prepare_kpi_dashboard_shell(ws):
    try:
        chart_objects = ws.api.ChartObjects()
        for idx in range(chart_objects.Count, 0, -1):
            chart_objects.Item(idx).Delete()
    except Exception:
        pass

    ws.api.Cells.Clear()
    _style_title_block(
        ws,
        "KPI Dashboard",
        "Output: APS summary, charts, and run diagnostics. Refreshed by Capacity, Schedule, and Scenario runs.",
    )
    _clear_legacy_doc_panel(ws, 15, width=6, end_row=20)

    ws.range("A4").value = "Run Capacity, Schedule, or Scenarios to populate dashboard charts."
    ws.range("A4").font.italic = True
    ws.range("A4").font.color = (128, 128, 128)
    _ensure_control_panel_links(ws.book)


def _clean_dashboard_df(df, key_col):
    if df is None or getattr(df, "empty", True):
        return pd.DataFrame()
    clean = df.copy()
    if key_col not in clean.columns:
        return pd.DataFrame()
    key_series = clean[key_col].fillna("").astype(str).str.strip()
    mask = key_series.ne("") & ~key_series.str.startswith("Run '", na=False)
    clean = clean[mask]
    return clean.reset_index(drop=True)


def _read_output_df(wb, sheet_name, required_headers, width):
    if sheet_name not in _sheet_names(wb):
        return pd.DataFrame()
    ws = wb.sheets[sheet_name]
    df, _, _ = _sheet_header_df(ws, required_headers=required_headers, width=width)
    return df


def _read_committed_jobs(wb):
    if "Schedule_Output" not in _sheet_names(wb):
        return {}

    ws = wb.sheets["Schedule_Output"]
    df, _, _ = _sheet_header_df(
        ws,
        required_headers={"Job_ID", "Resource_ID", "Planned_Start", "Planned_End"},
        width=25,
    )
    if df.empty:
        return {}

    committed = {}
    for _, row in df.iterrows():
        job_id = str(row.get("Job_ID", "")).strip()
        resource_id = str(row.get("Resource_ID", "")).strip()
        if not job_id or not resource_id:
            continue
        try:
            start_ts = pd.to_datetime(row.get("Planned_Start"), errors="coerce")
            end_ts = pd.to_datetime(row.get("Planned_End"), errors="coerce")
        except Exception:
            continue
        if pd.isna(start_ts) or pd.isna(end_ts):
            continue
        committed[job_id] = {
            "Resource_ID": resource_id,
            "Planned_Start": start_ts.to_pydatetime(),
            "Planned_End": end_ts.to_pydatetime(),
            "Status": str(row.get("Status", "SCHEDULED") or "SCHEDULED").strip(),
        }
    return committed


def _read_ctp_requests(wb):
    if CTP_REQUEST_SHEET not in _sheet_names(wb):
        return pd.DataFrame(columns=CTP_REQUEST_HEADERS)

    ws = wb.sheets[CTP_REQUEST_SHEET]
    df, _, _ = _sheet_header_df(
        ws,
        required_headers={"SKU_ID", "Qty_MT", "Requested_Date"},
        width=max(len(CTP_REQUEST_HEADERS) + 2, 8),
    )
    if df.empty:
        return pd.DataFrame(columns=CTP_REQUEST_HEADERS)

    for header in CTP_REQUEST_HEADERS:
        if header not in df.columns:
            df[header] = ""
    df = df[CTP_REQUEST_HEADERS].copy()
    df["SKU_ID"] = df["SKU_ID"].fillna("").astype(str).str.strip()
    df = df[df["SKU_ID"].ne("")].copy()
    if df.empty:
        return pd.DataFrame(columns=CTP_REQUEST_HEADERS)

    df["Qty_MT"] = pd.to_numeric(df["Qty_MT"], errors="coerce")
    df["Requested_Date"] = pd.to_datetime(df["Requested_Date"], errors="coerce")
    df["Request_ID"] = df["Request_ID"].fillna("").astype(str).str.strip()
    df["Notes"] = df["Notes"].fillna("").astype(str).str.strip()
    df = df.dropna(subset=["Qty_MT", "Requested_Date"])
    df = df[df["Qty_MT"] > 0].copy()
    if df.empty:
        return pd.DataFrame(columns=CTP_REQUEST_HEADERS)

    generated_ids = []
    for idx, request_id in enumerate(df["Request_ID"].tolist(), start=1):
        generated_ids.append(request_id or f"REQ-{idx:03d}")
    df["Request_ID"] = generated_ids
    return df.reset_index(drop=True)


def _format_material_gaps(material_gaps):
    if not material_gaps:
        return ""
    parts = []
    for gap in material_gaps:
        sku_id = str(gap.get("sku_id", "")).strip()
        qty = round(float(gap.get("shortage_qty", 0.0) or 0.0), 3)
        if sku_id:
            parts.append(f"{sku_id} {qty:g}")
    return ", ".join(parts)


def _format_heats_calc_warnings(value) -> str:
    if value in (None, "", []):
        return ""
    if isinstance(value, str):
        return value.strip()
    if not isinstance(value, list):
        return str(value)
    parts = []
    for warning in value:
        if not isinstance(warning, dict):
            text = str(warning).strip()
            if text:
                parts.append(text)
            continue
        issue_type = str(warning.get("type", "") or "").strip()
        reason = str(warning.get("reason", "") or "").strip()
        path = str(warning.get("path", "") or "").strip()
        sku_id = str(warning.get("sku_id", "") or "").strip()
        detail = reason or path or sku_id
        parts.append(f"{issue_type}: {detail}" if issue_type else detail)
    return " | ".join(part for part in parts if part)


def _ctp_feasible_text(result: dict) -> str:
    feasible = result.get("delivery_feasible", result.get("feasible"))
    if feasible is True:
        return "YES"
    if feasible is False:
        return "NO"
    if result.get("delivery_modeled") is False:
        return "UNMODELED"
    return ""


def _ctp_inventory_lineage_text(result: dict) -> str:
    status = str(result.get("inventory_lineage_status", "") or "").strip().upper()
    if not status:
        return ""
    return {
        "AUTHORITATIVE_SNAPSHOT_CHAIN": "AUTHORITATIVE",
        "NO_COMMITTED_CAMPAIGNS": "CURRENT_INVENTORY_ONLY",
        "RECOMPUTED_FROM_CONSUMPTION": "RECOMPUTED_FROM_CONSUMPTION",
        "CONSERVATIVE_BLEND": "CONSERVATIVE_BLEND",
    }.get(status, status)


def _ctp_campaign_list_text(value) -> str:
    if not value:
        return ""
    if isinstance(value, str):
        return value.strip()
    if isinstance(value, (list, tuple, set)):
        return ", ".join(str(item).strip() for item in value if str(item).strip())
    return str(value).strip()


def _ctp_plant_completion_feasible_text(result: dict) -> str:
    feasible = result.get("plant_completion_feasible")
    if feasible is True:
        return "YES"
    if feasible is False:
        return "NO"
    return ""


def _ctp_solver_status_text(result: dict) -> str:
    solver_status = str(result.get("solver_status", "") or "").strip()
    if solver_status.upper().startswith("BLOCKED:"):
        return solver_status
    lineage_status = str(result.get("inventory_lineage_status", "") or "").strip().upper()
    if lineage_status in {"", "AUTHORITATIVE_SNAPSHOT_CHAIN", "NO_COMMITTED_CAMPAIGNS"}:
        return solver_status
    lineage_label = {
        "RECOMPUTED_FROM_CONSUMPTION": "INV=RECOMPUTED",
        "CONSERVATIVE_BLEND": "INV=CONSERVATIVE_BLEND",
    }.get(lineage_status, f"INV={lineage_status}")
    if not solver_status:
        return lineage_label
    return f"{solver_status} | {lineage_label}"


def _render_ctp_output(wb, ctp_df):
    ws = _ensure_sheet(wb, CTP_OUTPUT_SHEET, after_sheet=CTP_REQUEST_SHEET)
    _refresh_ctp_output_shell(ws)
    _ensure_control_panel_links(wb)
    header_row = 3
    _clear_table_data(ws, header_row, len(CTP_OUTPUT_HEADERS), 500)

    for idx, header in enumerate(CTP_OUTPUT_HEADERS, start=1):
        try:
            ws.range((header_row, idx)).column_width = {
                "Request_ID": 14,
                "SKU_ID": 20,
                "Qty_MT": 10,
                "Requested_Date": 18,
                "Earliest_Completion": 18,
                "Plant_Completion_Feasible": 14,
                "Earliest_Delivery": 18,
                "Delivery_Feasible": 12,
                "Lateness_Days": 12,
                "Inventory_Lineage": 22,
                "Material_Gaps": 34,
                "Campaign_Action": 22,
                "Merged_Campaigns": 22,
                "New_Campaigns": 22,
                "Solver_Status": 30,
            }.get(header, 14)
        except Exception:
            pass

    if ctp_df is None or ctp_df.empty:
        _restore_output_prompt(ws, header_row)
        return ws

    output_df = ctp_df.copy()
    for header in CTP_OUTPUT_HEADERS:
        if header not in output_df.columns:
            output_df[header] = ""
    output_df = output_df[CTP_OUTPUT_HEADERS]
    ws.range((header_row + 1, 1)).value = output_df.values.tolist()
    last_row = header_row + len(output_df)

    for col_name in ["Requested_Date", "Earliest_Completion", "Earliest_Delivery"]:
        col_idx = CTP_OUTPUT_HEADERS.index(col_name) + 1
        ws.range((header_row + 1, col_idx), (last_row, col_idx)).number_format = "dd-mmm-yyyy hh:mm"
    for col_name in ["Qty_MT", "Lateness_Days"]:
        col_idx = CTP_OUTPUT_HEADERS.index(col_name) + 1
        ws.range((header_row + 1, col_idx), (last_row, col_idx)).number_format = "0.###"

    completion_feasible_col = CTP_OUTPUT_HEADERS.index("Plant_Completion_Feasible") + 1
    feasible_col = CTP_OUTPUT_HEADERS.index("Delivery_Feasible") + 1
    solver_col = CTP_OUTPUT_HEADERS.index("Solver_Status") + 1
    for row_idx, (_, row) in enumerate(output_df.iterrows(), start=header_row + 1):
        completion_feasible_text = str(row.get("Plant_Completion_Feasible", "")).strip().upper()
        feasible_text = str(row.get("Delivery_Feasible", "")).strip().upper()
        solver_text = str(row.get("Solver_Status", "")).strip().upper()
        if completion_feasible_text == "YES":
            ws.range((row_idx, completion_feasible_col)).color = ON_TIME_CLR
        elif completion_feasible_text == "NO":
            ws.range((row_idx, completion_feasible_col)).color = LATE_COLOR
        if feasible_text == "YES":
            ws.range((row_idx, feasible_col)).color = ON_TIME_CLR
        elif feasible_text == "NO":
            ws.range((row_idx, feasible_col)).color = LATE_COLOR
        elif feasible_text == "UNMODELED":
            ws.range((row_idx, feasible_col)).color = AMBER_COLOR
        if solver_text in {"MATERIAL HOLD", "INVALID REQUEST"} or solver_text.startswith("BLOCKED:"):
            ws.range((row_idx, solver_col)).color = AMBER_COLOR
    return ws


def _resource_family(resource_id):
    resource_text = str(resource_id or "").strip().upper()
    if resource_text.startswith("EAF"):
        return "EAF"
    if resource_text.startswith("LRF"):
        return "LRF"
    if resource_text.startswith("VD"):
        return "VD"
    if resource_text.startswith("CCM"):
        return "CCM"
    if resource_text.startswith("RM"):
        return "RM"
    if resource_text.startswith("BF"):
        return "BF"
    return resource_text or "Other"


def _sku_meta_lookup(skus):
    if skus is None or getattr(skus, "empty", True):
        return {}
    cols = [col for col in ["SKU_ID", "SKU_Name", "Category"] if col in skus.columns]
    if "SKU_ID" not in cols:
        return {}
    meta_df = skus[cols].copy().drop_duplicates(subset=["SKU_ID"])
    meta_df = meta_df.fillna("")
    lookup = {}
    for _, row in meta_df.iterrows():
        sku_id = str(row.get("SKU_ID", "")).strip()
        if not sku_id:
            continue
        lookup[sku_id] = {col: row.get(col, "") for col in cols}
    return lookup


def _parent_sku_lookup(bom):
    if bom is None or getattr(bom, "empty", True):
        return {}
    if not {"Parent_SKU", "Child_SKU"}.issubset(set(bom.columns)):
        return {}
    pairs = bom[["Parent_SKU", "Child_SKU"]].copy().fillna("")
    pairs["Parent_SKU"] = pairs["Parent_SKU"].astype(str).str.strip()
    pairs["Child_SKU"] = pairs["Child_SKU"].astype(str).str.strip()
    pairs = pairs[(pairs["Parent_SKU"] != "") & (pairs["Child_SKU"] != "")]
    lookup = (
        pairs.groupby("Child_SKU")["Parent_SKU"]
        .apply(lambda vals: ", ".join(sorted(dict.fromkeys([str(v).strip() for v in vals if str(v).strip()]))))
        .to_dict()
    )
    return lookup


def _material_type_for_sku(sku_id, category="", sku_name=""):
    sku = str(sku_id or "").strip().upper()
    category_text = str(category or "").strip()
    if sku.startswith("FG-WR-"):
        return "Finished Good Demand"
    if sku.startswith("RM-OUT-"):
        return "RM Output"
    if sku.startswith("BIL-"):
        return "CCM Output"
    if sku.startswith("VD-OUT-"):
        return "VD Output"
    if sku.startswith("LRF-OUT-"):
        return "LRF Output"
    if sku.startswith("EAF-OUT-"):
        return "EAF Output"
    if sku.startswith("EAF-RAW-"):
        return "EAF Raw Charge"
    if sku == "BF-HM":
        return "Hot Metal"
    if sku == "BF-RAW-MIX":
        return "BF Raw Mix"
    if sku in {"RM-IRON", "RM-COAL"}:
        return "BF Raw Material"
    if sku in {"RM-SCRAP", "RM-FESI", "RM-FEMN", "RM-FECR", "RM-ELEC"}:
        return "SMS Raw Material"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Flux"
    if sku == "BF-SLAG":
        return "BF Byproduct"
    if sku == "EAF-SLAG":
        return "EAF Byproduct"
    if sku == "LRF-WASTE":
        return "LRF Byproduct"
    if sku == "VD-WASTE":
        return "VD Byproduct"
    if sku == "CCM-CROP":
        return "CCM Byproduct"
    if sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "RM Byproduct"
    if category_text:
        return category_text
    if sku_name:
        return str(sku_name).strip()
    return "Material"


def _stage_for_material(sku_id, material_type=""):
    sku = str(sku_id or "").strip().upper()
    material_type = str(material_type or "").strip()
    if sku.startswith("FG-WR-") or sku.startswith("RM-OUT-") or sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "Rolling"
    if sku.startswith("BIL-") or sku == "CCM-CROP":
        return "CCM"
    if sku.startswith("VD-OUT-") or sku == "VD-WASTE":
        return "VD"
    if sku.startswith("LRF-OUT-") or sku == "LRF-WASTE":
        return "LRF"
    if sku.startswith("EAF-OUT-") or sku.startswith("EAF-RAW-") or sku == "EAF-SLAG":
        return "EAF"
    if sku in {"BF-HM", "BF-RAW-MIX", "BF-SLAG", "RM-IRON", "RM-COAL"}:
        return "BF"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Stores"
    if material_type == "Raw Material":
        return "Stores"
    return "Other"


def _plant_for_material(sku_id, category="", material_type=""):
    sku = str(sku_id or "").strip().upper()
    material_type = str(material_type or "").strip()
    category_text = str(category or "").strip()
    if sku.startswith("FG-WR-") or sku.startswith("RM-OUT-") or sku in {"RM-ENDCUT", "RM-SCALE"}:
        return "Rolling Mill"
    if (
        sku.startswith("BIL-")
        or sku.startswith("VD-OUT-")
        or sku.startswith("LRF-OUT-")
        or sku.startswith("EAF-OUT-")
        or sku.startswith("EAF-RAW-")
        or sku in {"EAF-SLAG", "LRF-WASTE", "VD-WASTE", "CCM-CROP", "RM-SCRAP", "RM-FESI", "RM-FEMN", "RM-FECR", "RM-ELEC"}
    ):
        return "SMS"
    if sku in {"BF-HM", "BF-RAW-MIX", "BF-SLAG", "RM-IRON", "RM-COAL"}:
        return "Blast Furnace"
    if sku in {"RM-LIME", "RM-DOLO"}:
        return "Shared Stores"
    if material_type == "Raw Material":
        return "Shared Stores"
    if category_text == "Raw Material":
        return "Shared Stores"
    return "Other"


def _plant_sort_key(plant_name):
    return (PLANT_SORT_ORDER.get(str(plant_name or "").strip(), 99), str(plant_name or ""))


def _material_type_sort_key(material_type):
    return (MATERIAL_TYPE_SORT_ORDER.get(str(material_type or "").strip(), 99), str(material_type or ""))


def _material_status(status_text):
    status_upper = str(status_text or "").strip().upper()
    if status_upper in {"SHORT", "SHORTAGE", "MATERIAL HOLD"}:
        return "SHORTAGE"
    if status_upper in {"PARTIAL SHORT", "PARTIAL COVER"}:
        return "PARTIAL"
    if status_upper in {"DRAWN FROM STOCK"}:
        return "DRAWN FROM STOCK"
    if status_upper in {"COVERED", "MAKE / CONVERT", "RELEASED", "READY"}:
        return "OK"
    return status_upper or "INFO"


def _write_dashboard_block(ws, start_cell, title, headers, rows):
    start_rng = ws.range(start_cell)
    start_row, start_col = start_rng.row, start_rng.column
    ws.range((start_row, start_col)).value = title
    ws.range((start_row, start_col)).font.bold = True
    ws.range((start_row, start_col)).font.color = TITLE_COLOR
    ws.range((start_row + 1, start_col)).value = headers
    hdr_rng = ws.range((start_row + 1, start_col), (start_row + 1, start_col + len(headers) - 1))
    hdr_rng.color = HEADER_COLOR
    hdr_rng.font.color = (255, 255, 255)
    hdr_rng.font.bold = True
    if rows:
        ws.range((start_row + 2, start_col)).value = rows


def _add_excel_chart(ws, top_left_cell, bottom_right_cell, data_range, chart_type, title, has_legend=True):
    try:
        top_left = ws.range(top_left_cell)
        bottom_right = ws.range(bottom_right_cell)
        left = top_left.left
        top = top_left.top
        width = max(bottom_right.left + bottom_right.width - left, 240)
        height = max(bottom_right.top + bottom_right.height - top, 180)
        chart_obj = ws.api.ChartObjects().Add(left, top, width, height)
        chart = chart_obj.Chart
        chart.ChartType = chart_type
        chart.SetSourceData(data_range.api)
        chart.HasTitle = True
        chart.ChartTitle.Text = title
        chart.HasLegend = has_legend
        if not has_legend:
            chart.Legend.Delete()
        try:
            chart.ChartArea.Format.Line.Visible = False
        except Exception:
            pass
        return chart
    except Exception:
        return None


def _refresh_kpi_dashboard(
    wb,
    capacity_df=None,
    schedule_df=None,
    campaign_df=None,
    scenario_df=None,
    solver_status="",
    planning=None,
):
    ws = _ensure_sheet(wb, KPI_DASHBOARD_SHEET, after_sheet="Theo_vs_Actual")
    _prepare_kpi_dashboard_shell(ws)

    if capacity_df is None:
        capacity_df = _read_output_df(wb, "Capacity_Map", {"Resource_ID", "Status"}, 20)
    if schedule_df is None:
        schedule_df = _read_output_df(wb, "Schedule_Output", {"Job_ID", "Operation", "Resource_ID"}, 25)
    if campaign_df is None:
        campaign_df = _read_output_df(wb, "Campaign_Schedule", {"Campaign_ID", "Grade", "Status"}, 25)
    if scenario_df is None:
        scenario_df = _read_output_df(wb, SCENARIO_OUTPUT_SHEET, {"Scenario", "Heats", "Campaigns"}, 30)

    capacity_df = _clean_dashboard_df(capacity_df, "Resource_ID")
    schedule_df = _clean_dashboard_df(schedule_df, "Job_ID")
    campaign_df = _clean_dashboard_df(campaign_df, "Campaign_ID")
    scenario_df = _clean_dashboard_df(scenario_df, "Scenario")

    if capacity_df.empty and schedule_df.empty and campaign_df.empty and scenario_df.empty:
        return ws

    if not capacity_df.empty:
        for col in ["Utilisation_%", "Overload_Hrs", "Demand_Hrs", "Avail_Hrs_14d"]:
            capacity_df[col] = pd.to_numeric(capacity_df.get(col, 0), errors="coerce").fillna(0)

    if not schedule_df.empty:
        schedule_df["Qty_MT"] = pd.to_numeric(schedule_df.get("Qty_MT", 0), errors="coerce").fillna(0)

    if not campaign_df.empty:
        campaign_df["Total_MT"] = pd.to_numeric(campaign_df.get("Total_MT", 0), errors="coerce").fillna(0)
        campaign_df["Heats"] = pd.to_numeric(campaign_df.get("Heats", 0), errors="coerce").fillna(0)
        campaign_df["Release_Status"] = campaign_df.get("Release_Status", "").fillna("").astype(str).str.strip()
        campaign_df["Status"] = campaign_df.get("Status", "").fillna("").astype(str).str.strip()
        campaign_df["RM_End"] = pd.to_datetime(campaign_df.get("RM_End"), errors="coerce")
        campaign_df["Due_Date"] = pd.to_datetime(campaign_df.get("Due_Date"), errors="coerce")

    if not scenario_df.empty:
        for col in [
            "Heats",
            "Campaigns",
            "Released",
            "Held",
            "On_Time_%",
            "Weighted_Lateness_Hrs",
            "Throughput_MT_Day",
            "Avg_Margin_Hrs",
        ]:
            if col in scenario_df.columns:
                scenario_df[col] = pd.to_numeric(scenario_df.get(col, 0), errors="coerce").fillna(0)

    released_mask = (
        campaign_df.get("Release_Status", pd.Series(dtype=object)).astype(str).str.upper().isin(RELEASED_STATUSES)
        if not campaign_df.empty
        else pd.Series(dtype=bool)
    )
    released_campaigns = int(released_mask.sum()) if not campaign_df.empty else 0
    held_campaigns = int(
        campaign_df.get("Release_Status", pd.Series(dtype=object)).astype(str).str.upper().eq("MATERIAL HOLD").sum()
    ) if not campaign_df.empty else 0
    late_campaigns = int(
        campaign_df.get("Status", pd.Series(dtype=object)).astype(str).str.upper().eq("LATE").sum()
    ) if not campaign_df.empty else 0
    on_time_pct = round(((released_campaigns - late_campaigns) / released_campaigns) * 100, 1) if released_campaigns else 100.0
    total_heats = int(campaign_df.loc[released_mask, "Heats"].sum()) if not campaign_df.empty and released_campaigns else 0
    total_ops = int(len(schedule_df)) if not schedule_df.empty else 0
    total_mt = round(float(campaign_df.loc[released_mask, "Total_MT"].sum()), 1) if not campaign_df.empty and released_campaigns else 0.0
    total_mt_all = round(float(campaign_df.get("Total_MT", pd.Series(dtype=float)).sum()), 1) if not campaign_df.empty else 0.0
    fulfillment_rate = round((total_mt / total_mt_all) * 100.0, 1) if total_mt_all else 100.0

    released_campaign_df = campaign_df.loc[released_mask].copy() if not campaign_df.empty else pd.DataFrame()
    if not released_campaign_df.empty:
        released_campaign_df["Margin_Hrs"] = (
            (released_campaign_df["Due_Date"] - released_campaign_df["RM_End"]).dt.total_seconds() / 3600.0
        ).fillna(0.0)
        lead_min = round(float(released_campaign_df["Margin_Hrs"].min()), 2)
        lead_avg = round(float(released_campaign_df["Margin_Hrs"].mean()), 2)
        lead_max = round(float(released_campaign_df["Margin_Hrs"].max()), 2)
    else:
        lead_min = lead_avg = lead_max = 0.0

    non_bf_capacity = capacity_df[capacity_df["Resource_ID"].astype(str) != "BF-01"].copy() if not capacity_df.empty else pd.DataFrame()
    max_util = round(float(non_bf_capacity["Utilisation_%"].max()), 1) if not non_bf_capacity.empty else 0.0
    avg_util = round(float(non_bf_capacity["Utilisation_%"].mean()), 1) if not non_bf_capacity.empty else 0.0
    bottleneck = (
        str(non_bf_capacity.sort_values(["Utilisation_%", "Overload_Hrs"], ascending=[False, False]).iloc[0]["Resource_ID"])
        if not non_bf_capacity.empty
        else "-"
    )

    planning_bits = []
    if planning:
        planning_bits.append(f"Horizon {int(planning.get('planning_horizon_days', 0) or 0)}d")
        down_resource = planning.get("machine_down_resource")
        down_hours = float(planning.get("machine_down_hours", 0) or 0)
        if down_resource and down_hours > 0:
            planning_bits.append(f"{down_resource} down {down_hours:g}h")
        spike = float(planning.get("demand_spike_pct", 0) or 0)
        if spike:
            planning_bits.append(f"Demand +{spike:g}%")
        yield_loss = float(planning.get("yield_loss_pct", 0) or 0)
        if yield_loss:
            planning_bits.append(f"Yield loss {yield_loss:g}%")

    ws.range("A2").value = "Last run: " + " | ".join(
        [datetime.now().strftime("%d-%b %H:%M"), solver_status or "Workbook refresh"] + planning_bits
    )
    ws.range("A2").font.italic = True
    ws.range("A2").font.color = (128, 128, 128)

    kpi_rows = [
        ["Released Campaigns", released_campaigns, ">= 1", "No", "OK" if released_campaigns else "CHECK"],
        ["Held Campaigns", held_campaigns, "0", "No", "OK" if held_campaigns == 0 else "WATCH"],
        ["On-Time Campaign %", on_time_pct, ">= 95", "%", "OK" if on_time_pct >= 95 else "WATCH"],
        ["Late Campaigns", late_campaigns, "0", "No", "OK" if late_campaigns == 0 else "ALERT"],
        ["Total Heats", total_heats, "-", "Heats", "OK"],
        ["Total Operations", total_ops, "-", "Ops", "OK"],
        ["Total MT Released", total_mt, "-", "MT", "OK"],
        ["Fulfillment Rate", fulfillment_rate, ">= 95", "%", "OK" if fulfillment_rate >= 95 else "WATCH"],
        ["Avg Lead Margin", lead_avg, "> 0", "Hrs", "OK" if lead_avg >= 0 else "WATCH"],
        ["Avg Utilisation", avg_util, ">= 75", "%", "OK" if avg_util >= 75 else "WATCH"],
        ["Max Utilisation", max_util, "<= 100", "%", "OK" if max_util <= 100 else "ALERT"],
        ["Bottleneck Resource", bottleneck, "-", "", "INFO"],
    ]
    _write_dashboard_block(ws, "A4", "APS Scorecard", ["KPI", "Current", "Target", "Unit", "Status"], kpi_rows)

    for row_idx in range(6, 6 + len(kpi_rows)):
        status_text = str(ws.range((row_idx, 5)).value or "").strip().upper()
        fill = {
            "OK": ON_TIME_CLR,
            "WATCH": AMBER_COLOR,
            "ALERT": LATE_COLOR,
            "CHECK": AMBER_COLOR,
            "INFO": LIGHT_BLUE,
        }.get(status_text)
        if fill:
            ws.range((row_idx, 5)).color = fill

    run_info_rows = [
        ["Solver", solver_status or "-"],
        ["Planning Horizon (Days)", planning.get("planning_horizon_days", "-") if planning else "-"],
        ["Demand Spike (%)", planning.get("demand_spike_pct", 0) if planning else 0],
        ["Machine Down Resource", planning.get("machine_down_resource", "-") if planning else "-"],
        ["Machine Down (Hrs)", planning.get("machine_down_hours", 0) if planning else 0],
        ["Machine Down Start (Hr)", planning.get("machine_down_start_hour", 0) if planning else 0],
        ["Solver Time Limit (sec)", planning.get("solver_time_limit_sec", 30) if planning else 30],
        ["Yield Loss (%)", planning.get("yield_loss_pct", 0) if planning else 0],
        ["Rush Order MT", planning.get("rush_order_mt", 0) if planning else 0],
        ["Extra Shift Hours", planning.get("extra_shift_hours", 0) if planning else 0],
    ]
    _write_dashboard_block(ws, "G4", "Run Settings", ["Setting", "Value"], run_info_rows)
    _write_dashboard_block(
        ws,
        "G16",
        "Lead-Time Margin",
        ["Metric", "Value", "Unit"],
        [["Min Margin", lead_min, "Hrs"], ["Avg Margin", lead_avg, "Hrs"], ["Max Margin", lead_max, "Hrs"]],
    )

    bottleneck_rows = []
    if not non_bf_capacity.empty:
        bottleneck_rows = (
            non_bf_capacity.sort_values(["Utilisation_%", "Overload_Hrs"], ascending=[False, False])
            .head(3)[["Resource_ID", "Utilisation_%", "Overload_Hrs"]]
            .values.tolist()
        )
    _write_dashboard_block(ws, "G23", "Bottleneck Ranking", ["Resource", "Util_%", "Overload_Hrs"], bottleneck_rows)

    util_chart_df = pd.DataFrame(columns=["Resource_ID", "Utilisation_%"])
    if not capacity_df.empty:
        util_chart_df = capacity_df[["Resource_ID", "Utilisation_%"]].copy().sort_values("Resource_ID").reset_index(drop=True)
    _write_dashboard_block(
        ws,
        "M4",
        "Utilisation Data",
        list(util_chart_df.columns) if not util_chart_df.empty else ["Resource_ID", "Utilisation_%"],
        util_chart_df.values.tolist(),
    )

    campaign_status_df = pd.DataFrame(columns=["Bucket", "Count"])
    if not campaign_df.empty:
        campaign_status_df = pd.DataFrame(
            [
                [
                    "Released On Time",
                    int(
                        (
                            campaign_df["Release_Status"].astype(str).str.upper().isin(RELEASED_STATUSES)
                            & (campaign_df["Status"].astype(str).str.upper() != "LATE")
                        ).sum()
                    ),
                ],
                [
                    "Released Late",
                    int(
                        (
                            campaign_df["Release_Status"].astype(str).str.upper().isin(RELEASED_STATUSES)
                            & (campaign_df["Status"].astype(str).str.upper() == "LATE")
                        ).sum()
                    ),
                ],
                ["Material Hold", int(campaign_df["Release_Status"].astype(str).str.upper().eq("MATERIAL HOLD").sum())],
            ]
        )
    _write_dashboard_block(ws, "P4", "Campaign Status Data", ["Bucket", "Count"], campaign_status_df.values.tolist())

    operation_mix_df = pd.DataFrame(columns=["Operation", "Ops"])
    if not schedule_df.empty:
        operation_mix_df = (
            schedule_df.groupby("Operation", as_index=False).size().rename(columns={"size": "Ops"}).sort_values("Operation").reset_index(drop=True)
        )
    _write_dashboard_block(
        ws,
        "S4",
        "Operation Mix Data",
        list(operation_mix_df.columns) if not operation_mix_df.empty else ["Operation", "Ops"],
        operation_mix_df.values.tolist(),
    )

    throughput_df = pd.DataFrame(columns=["RM_End", "Cumulative_MT"])
    if not released_campaign_df.empty:
        throughput_df = released_campaign_df[["RM_End", "Total_MT"]].dropna().sort_values("RM_End").reset_index(drop=True)
        throughput_df["Cumulative_MT"] = throughput_df["Total_MT"].cumsum().round(1)
        throughput_df["RM_End"] = throughput_df["RM_End"].dt.strftime("%d-%b %H:%M")
    _write_dashboard_block(
        ws,
        "V4",
        "Throughput Data",
        list(throughput_df.columns) if not throughput_df.empty else ["RM_End", "Cumulative_MT"],
        throughput_df.values.tolist(),
    )

    scenario_volume_df = pd.DataFrame(columns=["Scenario", "Released", "Held", "Heats"])
    if not scenario_df.empty:
        cols = [col for col in ["Scenario", "Released", "Held", "Heats"] if col in scenario_df.columns]
        scenario_volume_df = scenario_df[cols].copy()
    _write_dashboard_block(
        ws,
        "Y4",
        "Scenario Volume Data",
        list(scenario_volume_df.columns) if not scenario_volume_df.empty else ["Scenario", "Released", "Held", "Heats"],
        scenario_volume_df.values.tolist(),
    )

    scenario_kpi_df = pd.DataFrame(columns=["Scenario", "On_Time_%", "Throughput_MT_Day", "Avg_Margin_Hrs"])
    if not scenario_df.empty:
        cols = [col for col in ["Scenario", "On_Time_%", "Throughput_MT_Day", "Avg_Margin_Hrs"] if col in scenario_df.columns]
        scenario_kpi_df = scenario_df[cols].copy()
    _write_dashboard_block(
        ws,
        "AC4",
        "Scenario KPI Data",
        list(scenario_kpi_df.columns) if not scenario_kpi_df.empty else ["Scenario", "On_Time_%", "Throughput_MT_Day", "Avg_Margin_Hrs"],
        scenario_kpi_df.values.tolist(),
    )

    if not util_chart_df.empty:
        _add_excel_chart(
            ws,
            "A18",
            "F31",
            ws.range((5, 13), (5 + len(util_chart_df), 14)),
            XL_CHART_COLUMN_CLUSTERED,
            "Resource Utilisation %",
            has_legend=False,
        )
    if not campaign_status_df.empty:
        _add_excel_chart(
            ws,
            "G18",
            "L31",
            ws.range((5, 16), (5 + len(campaign_status_df), 17)),
            XL_CHART_PIE,
            "Campaign Outcome Split",
            has_legend=True,
        )
    if not operation_mix_df.empty:
        _add_excel_chart(
            ws,
            "A33",
            "F46",
            ws.range((5, 19), (5 + len(operation_mix_df), 20)),
            XL_CHART_COLUMN_CLUSTERED,
            "Operation Mix",
            has_legend=False,
        )
    if not throughput_df.empty:
        _add_excel_chart(
            ws,
            "G33",
            "L46",
            ws.range((5, 22), (5 + len(throughput_df), 23)),
            XL_CHART_LINE,
            "Cumulative Throughput",
            has_legend=False,
        )
    if not scenario_volume_df.empty:
        _add_excel_chart(
            ws,
            "A48",
            "F61",
            ws.range((5, 25), (5 + len(scenario_volume_df), 24 + len(scenario_volume_df.columns))),
            XL_CHART_COLUMN_CLUSTERED,
            "Scenario Volumes",
            has_legend=True,
        )
    if not scenario_kpi_df.empty:
        _add_excel_chart(
            ws,
            "G48",
            "L61",
            ws.range((5, 29), (5 + len(scenario_kpi_df), 28 + len(scenario_kpi_df.columns))),
            XL_CHART_COLUMN_CLUSTERED,
            "Scenario KPI Comparison",
            has_legend=True,
        )

    try:
        ws.api.Range("M:AI").EntireColumn.Hidden = True
    except Exception:
        pass
    try:
        for col in range(1, 35):
            ws.range((1, col)).column_width = max(ws.range((1, col)).column_width, 12)
    except Exception:
        pass
    return ws


def _status_fill(status_text):
    status_upper = str(status_text or "").strip().upper()
    if status_upper == "LATE":
        return LATE_COLOR
    if status_upper in {"MATERIAL HOLD", "SHORTAGE", "HELD"}:
        return AMBER_COLOR
    if status_upper in {"PARTIAL", "PARTIAL SHORT", "PARTIAL COVER"}:
        return AMBER_COLOR
    if status_upper in RUNNING_STATUSES:
        return RUNNING_COLOR
    if status_upper in {"SCHEDULED", "ON TIME", "COVERED", "DRAWN FROM STOCK", "OK", "BYPRODUCT"}:
        return ON_TIME_CLR
    return None


def _operation_fill(operation_text, operation_fill=None):
    fill_map = operation_fill or OPERATION_FILL
    return fill_map.get(str(operation_text or "").strip().upper())


def _schedule_detail_rows(heat_sched, camp_sched):
    detail = heat_sched.copy()
    if detail.empty:
        return detail

    detail["Planned_Start"] = pd.to_datetime(detail["Planned_Start"])
    detail["Planned_End"] = pd.to_datetime(detail["Planned_End"])
    detail = detail.sort_values(["Planned_Start", "Resource_ID", "Campaign", "Operation"]).reset_index(drop=True)
    return detail


def _render_bom_output(wb, bom_df, skus=None, bom_master=None):
    ws = _ensure_sheet(wb, BOM_OUTPUT_SHEET, after_sheet="Scenarios")
    _refresh_bom_output_shell(ws)
    _ensure_control_panel_links(wb)
    if bom_df is None or bom_df.empty:
        return ws

    sku_meta = _sku_meta_lookup(skus)
    parent_lookup = _parent_sku_lookup(bom_master)
    display = bom_df.copy()
    if "SKU_Name" not in display.columns:
        display["SKU_Name"] = display["SKU_ID"].map(lambda sku: sku_meta.get(str(sku).strip(), {}).get("SKU_Name", ""))

    display["Category"] = display["SKU_ID"].map(lambda sku: sku_meta.get(str(sku).strip(), {}).get("Category", ""))
    display["Material_Type"] = display.apply(
        lambda row: _material_type_for_sku(row.get("SKU_ID"), row.get("Category", ""), row.get("SKU_Name", "")),
        axis=1,
    )
    display["Stage"] = display.apply(
        lambda row: _stage_for_material(row.get("SKU_ID"), row.get("Material_Type", "")),
        axis=1,
    )
    display["Plant"] = display.apply(
        lambda row: _plant_for_material(row.get("SKU_ID"), row.get("Category", ""), row.get("Material_Type", "")),
        axis=1,
    )
    display["Material_Category"] = display["Category"].fillna("").astype(str)
    display["Parent_SKUs"] = display["SKU_ID"].map(lambda sku: parent_lookup.get(str(sku).strip(), ""))
    display["Available_Before"] = pd.to_numeric(
        display.get("Available_Before", display.get("Available", 0)), errors="coerce"
    ).fillna(0.0)
    display["Produced_Qty"] = pd.to_numeric(display.get("Produced_Qty", 0), errors="coerce").fillna(0.0).round(3)
    if "Flow_Type" not in display.columns:
        display["Flow_Type"] = "INPUT"
    display["Flow_Type"] = display["Flow_Type"].fillna("INPUT").astype(str).str.strip().str.upper()
    display["Covered_By_Stock"] = display.apply(
        lambda row: 0.0
        if row.get("Flow_Type") in {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"}
        else max(float(row.get("Gross_Req", 0) or 0.0) - float(row.get("Net_Req", 0) or 0.0), 0.0),
        axis=1,
    ).round(3)
    display["Status"] = display.apply(
        lambda row: (
            "BYPRODUCT"
            if row.get("Flow_Type") in {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"}
            else (
                "COVERED"
                if float(row.get("Net_Req", 0) or 0) <= 1e-9
                else ("PARTIAL SHORT" if float(row.get("Available_Before", 0) or 0) > 1e-9 else "SHORT")
            )
        ),
        axis=1,
    )
    display = display[
        [
            "Plant",
            "Stage",
            "Material_Type",
            "Material_Category",
            "Parent_SKUs",
            "SKU_ID",
            "SKU_Name",
            "BOM_Level",
            "Gross_Req",
            "Available_Before",
            "Covered_By_Stock",
            "Produced_Qty",
            "Net_Req",
            "Status",
        ]
    ].copy()
    display["__plant_sort"] = display["Plant"].map(lambda val: _plant_sort_key(val))
    display["__type_sort"] = display["Material_Type"].map(lambda val: _material_type_sort_key(val))
    display = display.sort_values(
        ["__plant_sort", "__type_sort", "BOM_Level", "SKU_ID"],
        kind="stable",
    ).drop(columns=["__plant_sort", "__type_sort"])

    _clear_grouped_output_body(ws, 4, 4000, len(BOM_OUTPUT_HEADERS))

    width_map = {
        "Plant": 16,
        "Stage": 10,
        "Material_Type": 18,
        "Material_Category": 18,
        "Parent_SKUs": 22,
        "SKU_ID": 20,
        "SKU_Name": 32,
        "BOM_Level": 10,
        "Gross_Req": 12,
        "Available_Before": 14,
        "Covered_By_Stock": 14,
        "Produced_Qty": 12,
        "Net_Req": 12,
        "Status": 14,
    }
    for idx, header in enumerate(BOM_OUTPUT_HEADERS, start=1):
        try:
            ws.range((3, idx)).column_width = width_map.get(header, 14)
        except Exception:
            pass

    row = 5
    for plant, plant_values in display.groupby("Plant", sort=False):
        gross_total = round(float(plant_values["Gross_Req"].sum()), 3)
        produced_total = round(float(plant_values["Produced_Qty"].sum()), 3)
        net_total = round(float(plant_values["Net_Req"].sum()), 3)
        ws.range((row, 1), (row, len(BOM_OUTPUT_HEADERS))).merge()
        ws.range((row, 1)).value = (
            f"Plant: {plant} | Gross Req: {gross_total:g} | Produced: {produced_total:g} | Net Req: {net_total:g} | "
            f"{len(plant_values)} material lines"
        )
        ws.range((row, 1), (row, len(BOM_OUTPUT_HEADERS))).color = LIGHT_BLUE
        ws.range((row, 1)).font.bold = True
        row += 1

        for material_type, mat_values in plant_values.groupby("Material_Type", sort=False):
            mat_gross = round(float(mat_values["Gross_Req"].sum()), 3)
            mat_produced = round(float(mat_values["Produced_Qty"].sum()), 3)
            mat_net = round(float(mat_values["Net_Req"].sum()), 3)
            ws.range((row, 1), (row, len(BOM_OUTPUT_HEADERS))).merge()
            ws.range((row, 1)).value = (
                f"Material Type: {material_type} | Gross Req: {mat_gross:g} | Produced: {mat_produced:g} | Net Req: {mat_net:g}"
            )
            ws.range((row, 1), (row, len(BOM_OUTPUT_HEADERS))).color = AMBER_COLOR
            ws.range((row, 1)).font.bold = True
            row += 1

            _style_header_row(ws, row, BOM_OUTPUT_HEADERS)
            row += 1

            values = mat_values[BOM_OUTPUT_HEADERS].values.tolist()
            ws.range((row, 1)).value = values
            group_end = row + len(values) - 1

            for num_col in ["BOM_Level", "Gross_Req", "Available_Before", "Covered_By_Stock", "Produced_Qty", "Net_Req"]:
                col_idx = BOM_OUTPUT_HEADERS.index(num_col) + 1
                fmt = "0" if num_col == "BOM_Level" else "0.###"
                ws.range((row, col_idx), (group_end, col_idx)).number_format = fmt

            for row_idx, (_, rec) in enumerate(mat_values.iterrows(), start=row):
                fill = _status_fill(rec.get("Status"))
                if fill:
                    ws.range((row_idx, 1), (row_idx, len(BOM_OUTPUT_HEADERS))).color = fill
                ws.range((row_idx, 1)).font.bold = False
                ws.range((row_idx, 2)).font.bold = True
                ws.range((row_idx, len(BOM_OUTPUT_HEADERS))).font.bold = True
            row = group_end + 2

        row += 1

    return ws


def _render_material_plan(wb, campaigns, skus=None):
    ws = _ensure_sheet(wb, MATERIAL_PLAN_SHEET, after_sheet="Campaign_Schedule")
    _refresh_material_plan_shell(ws)
    _ensure_control_panel_links(wb)
    if not campaigns:
        return ws

    sku_meta = _sku_meta_lookup(skus)

    rows = []
    for camp in campaigns:
        gross_map = camp.get("material_gross_requirements", {}) or {}
        before_map = camp.get("inventory_before", {}) or {}
        consumed_map = camp.get("material_consumed", {}) or {}
        after_map = camp.get("inventory_after", {}) or {}
        shortage_map = camp.get("material_shortages", {}) or {}
        material_ids = sorted(set(gross_map) | set(consumed_map) | set(shortage_map))
        for material_sku in material_ids:
            required = float(gross_map.get(material_sku, shortage_map.get(material_sku, 0.0)) or 0.0)
            available_before = float(before_map.get(material_sku, 0.0) or 0.0)
            consumed = float(consumed_map.get(material_sku, 0.0) or 0.0)
            remaining_after = float(after_map.get(material_sku, available_before - consumed) or 0.0)
            if material_sku in shortage_map:
                status = "SHORTAGE"
            elif consumed > 1e-9 and consumed + 1e-9 < required:
                status = "PARTIAL COVER"
            elif consumed > 1e-9:
                status = "DRAWN FROM STOCK"
            else:
                status = "MAKE / CONVERT"
            sku_name = sku_meta.get(material_sku, {}).get("SKU_Name", "")
            category = sku_meta.get(material_sku, {}).get("Category", "")
            material_type = _material_type_for_sku(material_sku, category, sku_name)
            plant = _plant_for_material(material_sku, category, material_type)
            rows.append(
                {
                    "Campaign_ID": camp.get("campaign_id", ""),
                    "Campaign_Grade": camp.get("grade", ""),
                    "Campaign_Release_Status": camp.get("release_status", ""),
                    "Campaign_Material_Issue": camp.get("material_issue", ""),
                    "Plant": plant,
                    "Material_Type": material_type,
                    "Material_SKU": material_sku,
                    "Material_Name": sku_name,
                    "Required_Qty": round(required, 3),
                    "Available_Before": round(available_before, 3),
                    "Consumed": round(consumed, 3),
                    "Remaining_After": round(remaining_after, 3),
                    "Status": status,
                }
            )

    _clear_grouped_output_body(ws, 4, 4000, len(MATERIAL_PLAN_HEADERS))
    if not rows:
        return ws

    plan_df = pd.DataFrame(rows)
    plan_df["__plant_sort"] = plan_df["Plant"].map(lambda val: _plant_sort_key(val))
    plan_df["__type_sort"] = plan_df["Material_Type"].map(lambda val: _material_type_sort_key(val))

    released_count = sum(1 for camp in campaigns if str(camp.get("release_status", "")).upper() == "RELEASED")
    held_count = sum(1 for camp in campaigns if str(camp.get("release_status", "")).upper() == "MATERIAL HOLD")
    shortage_lines = int(plan_df["Status"].astype(str).str.upper().eq("SHORTAGE").sum())
    make_qty = round(float(plan_df.loc[plan_df["Status"].astype(str).str.upper().eq("MAKE / CONVERT"), "Required_Qty"].sum()), 3)
    inventory_covered_qty = round(float(plan_df["Consumed"].sum()), 3)
    total_required = round(float(plan_df["Required_Qty"].sum()), 3)
    _write_dashboard_block(
        ws,
        "M8",
        "Material Summary",
        ["Metric", "Value"],
        [
            ["Campaigns", len(campaigns)],
            ["Released", released_count],
            ["Held", held_count],
            ["Shortage Lines", shortage_lines],
            ["Total Required Qty", total_required],
            ["Inventory Covered Qty", inventory_covered_qty],
            ["Make / Convert Qty", make_qty],
        ],
    )

    width_map = {
        "Campaign_ID": 14,
        "Plant": 16,
        "Material_Type": 18,
        "Material_SKU": 20,
        "Material_Name": 30,
        "Required_Qty": 12,
        "Available_Before": 14,
        "Consumed": 12,
        "Remaining_After": 14,
        "Status": 16,
    }
    for idx, header in enumerate(MATERIAL_PLAN_HEADERS, start=1):
        try:
            ws.range((3, idx)).column_width = width_map.get(header, 14)
        except Exception:
            pass

    campaign_order = {camp.get("campaign_id", ""): idx for idx, camp in enumerate(campaigns)}
    row = 5
    for campaign_id, camp_values in sorted(
        plan_df.groupby("Campaign_ID", sort=False),
        key=lambda item: campaign_order.get(item[0], 9999),
    ):
        camp_values = camp_values.sort_values(["__plant_sort", "__type_sort", "Material_SKU"], kind="stable")
        first = camp_values.iloc[0]
        required_total = round(float(camp_values["Required_Qty"].sum()), 3)
        shortage_count = int(camp_values["Status"].astype(str).str.upper().eq("SHORTAGE").sum())
        header_text = (
            f"Campaign: {campaign_id} | Grade: {first['Campaign_Grade']} | "
            f"Release: {first['Campaign_Release_Status'] or '-'} | Required Qty: {required_total:g}"
        )
        if first["Campaign_Material_Issue"]:
            header_text += f" | Issue: {first['Campaign_Material_Issue']}"
        if shortage_count:
            header_text += f" | Shortage Lines: {shortage_count}"
        ws.range((row, 1), (row, len(MATERIAL_PLAN_HEADERS))).merge()
        ws.range((row, 1)).value = header_text
        ws.range((row, 1), (row, len(MATERIAL_PLAN_HEADERS))).color = (
            AMBER_COLOR if str(first["Campaign_Release_Status"]).upper() == "MATERIAL HOLD" else LIGHT_BLUE
        )
        ws.range((row, 1)).font.bold = True
        row += 1

        for plant, plant_values in camp_values.groupby("Plant", sort=False):
            plant_required = round(float(plant_values["Required_Qty"].sum()), 3)
            plant_inventory_covered = round(float(plant_values["Consumed"].sum()), 3)
            ws.range((row, 1), (row, len(MATERIAL_PLAN_HEADERS))).merge()
            ws.range((row, 1)).value = (
                f"Plant: {plant} | Required Qty: {plant_required:g} | Inventory Covered Qty: {plant_inventory_covered:g}"
            )
            ws.range((row, 1), (row, len(MATERIAL_PLAN_HEADERS))).color = AMBER_COLOR
            ws.range((row, 1)).font.bold = True
            row += 1

            _style_header_row(ws, row, MATERIAL_PLAN_HEADERS)
            row += 1

            write_df = plant_values[MATERIAL_PLAN_HEADERS].copy()
            ws.range((row, 1)).value = write_df.values.tolist()
            group_end = row + len(write_df) - 1

            for num_col in ["Required_Qty", "Available_Before", "Consumed", "Remaining_After"]:
                col_idx = MATERIAL_PLAN_HEADERS.index(num_col) + 1
                ws.range((row, col_idx), (group_end, col_idx)).number_format = "0.###"

            for row_idx, (_, rec) in enumerate(write_df.iterrows(), start=row):
                fill = _status_fill(rec.get("Status"))
                if fill:
                    ws.range((row_idx, 1), (row_idx, len(MATERIAL_PLAN_HEADERS))).color = fill
                ws.range((row_idx, 1)).font.bold = True
                ws.range((row_idx, 3)).font.bold = True
                ws.range((row_idx, 10)).font.bold = True
            row = group_end + 2

        row += 1

    return ws


def _render_equipment_schedule(wb, schedule_df, resources):
    ws = _ensure_sheet(wb, EQUIPMENT_SCHEDULE_SHEET, after_sheet="Campaign_Schedule")
    headers = [
        "Job_ID",
        "Campaign",
        "SO_ID",
        "Grade",
        "Section_mm",
        "SKU_ID",
        "Operation",
        "Planned_Start",
        "Planned_End",
        "Duration_Hrs",
        "Qty_MT",
        "Status",
    ]
    _prepare_custom_output_sheet(
        ws,
        "Equipment Schedule",
        "Output: Separate dispatch tables by plant and equipment. Updated by 'Run Schedule'.",
        headers,
        OUTPUT_PROMPTS[EQUIPMENT_SCHEDULE_SHEET],
        doc_lines=[
            "Role: Dispatch view grouped by equipment, with one table per machine.",
            "Use this when supervisors want a clean handoff for a specific unit instead of a single long master list.",
            "Blank rows separate equipment sections, and campaign separator rows split the machine queue into visible campaign blocks.",
            "Running rows stay pinned if you mark them as RUNNING.",
            "Return to Control_Panel anytime using the workbook link in X1.",
        ],
        doc_start_col=16,
    )
    _ensure_control_panel_links(wb)

    if schedule_df.empty:
        return ws
    ws.range((4, 1), (4, len(headers))).clear_contents()

    resource_lookup = resources[["Resource_ID", "Resource_Name", "Plant"]].drop_duplicates()
    sched = schedule_df.merge(resource_lookup, on="Resource_ID", how="left")
    sched["Plant"] = sched["Plant"].fillna("Unassigned")
    sched["Resource_Name"] = sched["Resource_Name"].fillna(sched["Resource_ID"])
    sched["Planned_Start"] = pd.to_datetime(sched["Planned_Start"])
    sched["Planned_End"] = pd.to_datetime(sched["Planned_End"])
    sched = sched.sort_values(["Plant", "Resource_ID", "Planned_Start", "Operation"]).reset_index(drop=True)

    row = 5
    current_plant = None
    for (plant, resource_id, resource_name), grp in sched.groupby(["Plant", "Resource_ID", "Resource_Name"], sort=False):
        if plant != current_plant:
            ws.range((row, 1), (row, len(headers))).merge()
            ws.range((row, 1)).value = f"Plant: {plant}"
            current_plant = plant
            row += 1

        ws.range((row, 1), (row, len(headers))).merge()
        ws.range((row, 1)).value = f"Equipment: {resource_id} | {resource_name}"
        row += 1

        _style_header_row(ws, row, headers)
        row += 1

        values = grp[headers].copy()
        values["Planned_Start"] = values["Planned_Start"].dt.strftime("%d-%b %H:%M")
        values["Planned_End"] = values["Planned_End"].dt.strftime("%d-%b %H:%M")
        values = values.sort_values(["Planned_Start", "Planned_End", "Campaign", "Job_ID"]).reset_index(drop=True)

        for campaign_id, camp_values in values.groupby("Campaign", sort=False):
            first = camp_values.iloc[0]
            campaign_label = (
                f"Campaign: {campaign_id} | Grade: {first['Grade']} | "
                f"Section: {first['Section_mm']} | SOs: {first['SO_ID']}"
            )
            ws.range((row, 1), (row, len(headers))).merge()
            ws.range((row, 1)).value = campaign_label
            row += 1

            ws.range((row, 1)).value = camp_values.values.tolist()
            row += len(camp_values) + 1

        row += 1

    return ws


def _gantt_bucket_hours(horizon_days):
    horizon = max(int(horizon_days or 14), 1)
    if horizon <= 5:
        return 1
    if horizon <= 10:
        return 2
    return 4


def _contrast_font(fill):
    try:
        red, green, blue = fill
    except Exception:
        return (255, 255, 255)
    luminance = (red * 299 + green * 587 + blue * 114) / 1000
    return (255, 255, 255) if luminance < 150 else TITLE_COLOR


def _utilisation_fill(utilisation_pct):
    try:
        util = float(utilisation_pct)
    except Exception:
        return None
    if util >= 85:
        return LATE_COLOR
    if util >= 60:
        return AMBER_COLOR
    if util >= 25:
        return ON_TIME_CLR
    return LIGHT_BLUE


def _build_gantt_segments(gantt_df):
    if gantt_df is None or gantt_df.empty:
        return pd.DataFrame(columns=["Resource_ID", "Campaign", "Planned_Start", "Planned_End", "Status"])

    rows = []
    gap_tolerance = timedelta(minutes=15)
    for resource_id, resource_rows in gantt_df.groupby("Resource_ID", sort=False):
        current = None
        for _, rec in resource_rows.sort_values(["Planned_Start", "Planned_End", "Operation"]).iterrows():
            campaign_id = str(rec.get("Campaign", "")).strip()
            start_dt = pd.to_datetime(rec.get("Planned_Start"), errors="coerce")
            end_dt = pd.to_datetime(rec.get("Planned_End"), errors="coerce")
            if not campaign_id or pd.isna(start_dt) or pd.isna(end_dt):
                continue
            status_text = str(rec.get("Status", "")).strip()
            operation_text = str(rec.get("Operation", "")).strip().upper()
            if (
                current
                and campaign_id == current["Campaign"]
                and start_dt <= current["Planned_End"] + gap_tolerance
            ):
                current["Planned_End"] = max(current["Planned_End"], end_dt)
                current["Status"] = status_text or current["Status"]
                current["Operations"].add(operation_text)
            else:
                if current:
                    rows.append(current)
                current = {
                    "Resource_ID": str(resource_id).strip(),
                    "Campaign": campaign_id,
                    "Planned_Start": start_dt.to_pydatetime() if hasattr(start_dt, "to_pydatetime") else start_dt,
                    "Planned_End": end_dt.to_pydatetime() if hasattr(end_dt, "to_pydatetime") else end_dt,
                    "Status": status_text,
                    "Operations": {operation_text} if operation_text else set(),
                }
        if current:
            rows.append(current)

    if not rows:
        return pd.DataFrame(columns=["Resource_ID", "Campaign", "Planned_Start", "Planned_End", "Status"])

    seg_df = pd.DataFrame(rows)
    seg_df["Duration_Hrs"] = (
        (pd.to_datetime(seg_df["Planned_End"]) - pd.to_datetime(seg_df["Planned_Start"])).dt.total_seconds() / 3600.0
    ).round(2)
    seg_df["Operation_Label"] = seg_df["Operations"].map(
        lambda ops: " / ".join(sorted(op for op in ops if op)) if ops else ""
    )
    return seg_df


def _gantt_guide_lines(segments_df, bucket_hours, timeline_start, timeline_end):
    if segments_df is None or segments_df.empty:
        return [
            "Role: Resource swim-lane view for campaign timing.",
            "Run 'Run Schedule' to populate the timeline.",
            "Each lane represents one resource; colored bars show campaign occupancy windows.",
        ]

    busiest = (
        segments_df.groupby("Resource_ID")["Duration_Hrs"].sum().sort_values(ascending=False).head(1)
    )
    busiest_line = "Busiest lane: not available"
    if not busiest.empty:
        busiest_line = f"Busiest lane: {busiest.index[0]} | {busiest.iloc[0]:.1f} scheduled hrs"
    return [
        "Role: Planner timeline by resource lane and campaign occupancy.",
        f"View: {bucket_hours}-hour buckets | {timeline_start:%d-%b %H:%M} to {timeline_end:%d-%b %H:%M}.",
        f"Lanes: {segments_df['Resource_ID'].nunique()} | Campaigns shown: {segments_df['Campaign'].nunique()}",
        busiest_line,
        "Plant bands split Rolling Mill and SMS lanes. Darker bars show campaign windows on each resource.",
        "Campaign release gating follows Config > Campaign_Serialization_Mode so PPC can choose strict end-to-end or SMS-overlap behaviour.",
        "Use Schedule_Output for exact operation timestamps and Equipment_Schedule for dispatch packets.",
    ]


def _apply_gantt_window_layout(ws, timeline_start_col):
    try:
        ws.activate()
        ws.range((6, timeline_start_col)).select()
        window = ws.book.app.api.ActiveWindow
        try:
            window.FreezePanes = False
        except Exception:
            pass
        window.SplitRow = 5
        window.SplitColumn = timeline_start_col - 1
        window.FreezePanes = True
        window.Zoom = 90
    except Exception:
        pass


def _render_schedule_gantt(wb, schedule_df, planning_start, horizon_days, resources=None):
    ws = _ensure_sheet(wb, GANTT_SHEET, after_sheet=EQUIPMENT_SCHEDULE_SHEET)
    ws.api.Cells.Clear()
    _style_title_block(
        ws,
        "Schedule Gantt",
        "Output: Resource swim-lane Gantt by campaign, with adaptive time buckets and plant-separated lanes. Campaigns are shown in strict release order.",
    )
    _ensure_control_panel_links(wb)
    horizon_days = max(int(horizon_days or 14), 1)
    bucket_hours = _gantt_bucket_hours(horizon_days)
    buckets_per_day = int(24 / bucket_hours)
    bucket_count = horizon_days * buckets_per_day
    timeline_start_col = 6
    meta_headers = ["Plant", "Resource_ID", "Utilisation_%", "Campaigns", "Ops"]
    timeline_start = pd.to_datetime(planning_start or datetime.now()).to_pydatetime().replace(
        hour=0, minute=0, second=0, microsecond=0
    )
    timeline_end = timeline_start + timedelta(days=horizon_days)

    if schedule_df is None or schedule_df.empty:
        ws.range("A3").value = OUTPUT_PROMPTS[GANTT_SHEET]
        ws.range("A3").font.italic = True
        ws.range("A3").font.color = (128, 128, 128)
        return ws

    gantt_df = schedule_df.copy()
    gantt_df["Planned_Start"] = pd.to_datetime(gantt_df["Planned_Start"], errors="coerce")
    gantt_df["Planned_End"] = pd.to_datetime(gantt_df["Planned_End"], errors="coerce")
    gantt_df = gantt_df.dropna(subset=["Planned_Start", "Planned_End"]).copy()
    gantt_df = gantt_df.sort_values(["Resource_ID", "Planned_Start", "Planned_End", "Operation"]).reset_index(drop=True)
    if gantt_df.empty:
        ws.range("A3").value = OUTPUT_PROMPTS[GANTT_SHEET]
        ws.range("A3").font.italic = True
        ws.range("A3").font.color = (128, 128, 128)
        return ws

    segments = _build_gantt_segments(gantt_df)
    duration_hrs = pd.to_numeric(gantt_df.get("Duration_Hrs", 0), errors="coerce").fillna(0)
    utilisation = (
        gantt_df.assign(_Duration_Hrs=duration_hrs)
        .groupby("Resource_ID", as_index=False)["_Duration_Hrs"]
        .sum()
        .rename(columns={"_Duration_Hrs": "Scheduled_Hrs"})
    )
    utilisation["Utilisation_%"] = (
        utilisation["Scheduled_Hrs"] / max(horizon_days * 24, 1) * 100.0
    ).round(1)
    plant_lookup = _resource_plant_map(resources)
    plant_map = (
        gantt_df[["Resource_ID"]]
        .assign(
            Plant=gantt_df["Resource_ID"].astype(str).map(lambda rid: _resource_plant(rid, plant_lookup))
        )
        .drop_duplicates()
    )
    resource_rows = utilisation.merge(plant_map, on="Resource_ID", how="left")
    resource_rows["Campaigns"] = resource_rows["Resource_ID"].map(
        gantt_df.groupby("Resource_ID")["Campaign"].nunique().to_dict()
    ).fillna(0).astype(int)
    resource_rows["Ops"] = resource_rows["Resource_ID"].map(
        gantt_df.groupby("Resource_ID").size().to_dict()
    ).fillna(0).astype(int)
    resource_rows = resource_rows.sort_values(
        by=["Plant", "Resource_ID"],
        key=lambda s: s.map(lambda value: _plant_sort_key(value) if s.name == "Plant" else (0, str(value)))
    ).reset_index(drop=True)

    # ── Row 3: one-line summary ──────────────────────────────────────────
    summary_bits = [
        f"{bucket_hours}-hour buckets",
        f"{timeline_start:%d-%b %H:%M} to {timeline_end:%d-%b %H:%M}",
        f"{resource_rows['Resource_ID'].nunique()} resources",
        f"{gantt_df['Campaign'].nunique()} campaigns",
    ]
    ws.range("A3").value = " | ".join(summary_bits)
    ws.range("A3").font.color = (79, 79, 79)
    ws.range("A3").font.italic = True

    # ── Row 4: day date headers (spanning bucket columns) ────────────────
    for day_idx in range(horizon_days):
        start_col = timeline_start_col + day_idx * buckets_per_day
        end_col = start_col + buckets_per_day - 1
        header_rng = ws.range((4, start_col), (4, end_col))
        header_rng.merge()
        day_dt = timeline_start + timedelta(days=day_idx)
        header_rng.value = day_dt.strftime("%a %d-%b")
        header_rng.color = LIGHT_BLUE if day_idx % 2 == 0 else DOC_FILL
        header_rng.font.bold = True
        header_rng.font.color = TITLE_COLOR
        header_rng.font.size = 10
        try:
            header_rng.api.HorizontalAlignment = -4108
            header_rng.row_height = 22
        except Exception:
            pass

    # ── Row 4: meta area label ───────────────────────────────────────────
    try:
        meta_label_rng = ws.range((4, 1), (4, 5))
        meta_label_rng.merge()
        meta_label_rng.value = f"Planning: {timeline_start:%d-%b} to {timeline_end:%d-%b} ({horizon_days}d)"
        meta_label_rng.font.bold = True
        meta_label_rng.font.color = TITLE_COLOR
        meta_label_rng.color = (247, 249, 252)
        meta_label_rng.api.HorizontalAlignment = -4108
    except Exception:
        pass

    # ── Row 5: meta column headers + timeline bucket headers ─────────────
    _style_header_row(ws, 5, meta_headers)
    meta_widths = {"Plant": 14, "Resource_ID": 14, "Utilisation_%": 13, "Campaigns": 10, "Ops": 8}
    for idx, header in enumerate(meta_headers, start=1):
        try:
            ws.range((5, idx)).column_width = meta_widths.get(header, 12)
            ws.range((5, idx)).api.HorizontalAlignment = -4108
        except Exception:
            pass

    timeline_end_col = timeline_start_col + bucket_count - 1
    bucket_col_width = 8 if bucket_hours == 1 else (9 if bucket_hours == 2 else 10)
    for bucket_idx in range(bucket_count):
        col = timeline_start_col + bucket_idx
        bucket_start = timeline_start + timedelta(hours=bucket_idx * bucket_hours)
        # Show date label on first bucket of each day, else just time
        if bucket_idx % buckets_per_day == 0:
            ws.range((5, col)).value = bucket_start.strftime("%d-%b %H:%M")
        else:
            ws.range((5, col)).value = bucket_start.strftime("%H:%M")
        ws.range((5, col)).color = HEADER_COLOR
        ws.range((5, col)).font.color = (255, 255, 255)
        ws.range((5, col)).font.bold = True
        ws.range((5, col)).font.size = 8
        try:
            ws.range((5, col)).column_width = bucket_col_width
            ws.range((5, col)).api.HorizontalAlignment = -4108
        except Exception:
            pass

    # ── Data rows: plant bands + resource lanes ──────────────────────────
    resource_row_lookup = {}
    row_cursor = 6
    for plant_name, plant_rows in resource_rows.groupby("Plant", sort=False):
        plant_label_rng = ws.range((row_cursor, 1), (row_cursor, timeline_end_col))
        plant_label_rng.merge()
        plant_label_rng.value = f"▶ {plant_name}"
        plant_label_rng.color = HEADER_COLOR
        plant_label_rng.font.bold = True
        plant_label_rng.font.color = (255, 255, 255)
        plant_label_rng.font.size = 10
        try:
            plant_label_rng.api.HorizontalAlignment = -4131
            plant_label_rng.row_height = 24
        except Exception:
            pass
        row_cursor += 1

        for _, row in plant_rows.iterrows():
            resource_row_lookup[str(row["Resource_ID"]).strip()] = row_cursor
            ws.range((row_cursor, 1)).value = row["Plant"]
            ws.range((row_cursor, 2)).value = row["Resource_ID"]
            ws.range((row_cursor, 3)).value = row["Utilisation_%"]
            ws.range((row_cursor, 4)).value = row["Campaigns"]
            ws.range((row_cursor, 5)).value = row["Ops"]

            # Base lane fill — alternating banding
            base_fill = (255, 255, 255) if row_cursor % 2 == 1 else (248, 250, 252)
            ws.range((row_cursor, 1), (row_cursor, 5)).color = base_fill

            # Utilisation conditional coloring
            util_fill = _utilisation_fill(row["Utilisation_%"])
            if util_fill:
                ws.range((row_cursor, 3)).color = util_fill

            try:
                ws.range((row_cursor, 1), (row_cursor, 5)).api.HorizontalAlignment = -4108
                ws.range((row_cursor, 2)).api.HorizontalAlignment = -4131
                ws.range((row_cursor, 2)).font.bold = True
                ws.range((row_cursor, 3)).number_format = "0.0\"%\""
                ws.range((row_cursor, 1), (row_cursor, timeline_end_col)).row_height = 22
            except Exception:
                pass

            # Alternating day columns for visual lanes
            for day_idx in range(horizon_days):
                start_col = timeline_start_col + day_idx * buckets_per_day
                end_col = start_col + buckets_per_day - 1
                day_fill = (250, 252, 255) if day_idx % 2 == 0 else (244, 247, 251)
                ws.range((row_cursor, start_col), (row_cursor, end_col)).color = day_fill
            row_cursor += 1

        row_cursor += 1  # gap between plant bands

    # ── Campaign bar rendering ───────────────────────────────────────────
    palette = [
        (68, 114, 196),
        (112, 173, 71),
        (237, 125, 49),
        (91, 155, 213),
        (165, 165, 165),
        (255, 192, 0),
        (38, 68, 120),
        (158, 72, 14),
        (99, 37, 35),
        (67, 104, 43),
    ]

    # Build a grade lookup from segments
    campaign_grades = {}
    if not gantt_df.empty and "Grade" in gantt_df.columns:
        for cid, grp in gantt_df.groupby("Campaign"):
            grades = grp["Grade"].dropna().unique()
            campaign_grades[str(cid)] = str(grades[0]) if len(grades) else ""

    campaign_colors = {}
    for idx, campaign_id in enumerate(sorted(segments["Campaign"].dropna().astype(str).unique())):
        campaign_colors[campaign_id] = palette[idx % len(palette)]

    for _, rec in segments.iterrows():
        row_offset = resource_row_lookup.get(str(rec["Resource_ID"]).strip())
        if row_offset is None:
            continue
        start_idx = max(int(math.floor((rec["Planned_Start"] - timeline_start).total_seconds() / 3600 / bucket_hours)), 0)
        end_idx = min(int(math.ceil((rec["Planned_End"] - timeline_start).total_seconds() / 3600 / bucket_hours)) - 1, bucket_count - 1)
        if end_idx < start_idx:
            continue
        start_col = timeline_start_col + start_idx
        end_col = timeline_start_col + end_idx
        fill = campaign_colors.get(str(rec["Campaign"]), HEADER_COLOR)
        font_color = _contrast_font(fill)
        bar_rng = ws.range((row_offset, start_col), (row_offset, end_col))
        bar_rng.color = fill
        bar_rng.font.color = font_color
        bar_rng.font.bold = True
        bar_rng.font.size = 8
        try:
            bar_rng.api.Borders.Weight = 2
        except Exception:
            pass
        status_upper = str(rec.get("Status", "")).strip().upper()
        if status_upper == "LATE":
            try:
                bar_rng.api.Borders.Color = 0x0000FF
            except Exception:
                pass
        elif status_upper in RUNNING_STATUSES:
            try:
                bar_rng.api.Borders.Color = 0xC08000
            except Exception:
                pass

        # Bar label: campaign ID + grade if there's enough room
        label_col = start_col + max((end_col - start_col) // 2, 0)
        campaign_id_str = str(rec["Campaign"]).strip()
        grade_str = campaign_grades.get(campaign_id_str, "")
        op_label = str(rec.get("Operation_Label", "")).strip()
        if end_col - start_col >= 4 and grade_str:
            label = f"{campaign_id_str} ({grade_str})"
        elif end_col - start_col >= 2:
            label = campaign_id_str
        else:
            label = campaign_id_str[:6]
        label_rng = ws.range((row_offset, label_col))
        label_rng.value = label
        _safe_set_alignment(label_rng, horizontal=-4108)

    # ── Day boundary vertical lines ──────────────────────────────────────
    last_data_row = max(resource_row_lookup.values()) if resource_row_lookup else 6
    for day_idx in range(horizon_days + 1):
        boundary_col = timeline_start_col + day_idx * buckets_per_day
        if boundary_col > timeline_end_col:
            boundary_col = timeline_end_col
        try:
            ws.range((4, boundary_col), (last_data_row, boundary_col)).api.Borders(7).Weight = 2
            ws.range((4, boundary_col), (last_data_row, boundary_col)).api.Borders(7).Color = 0x808080
        except Exception:
            pass

    # ── Legend panel ──────────────────────────────────────────────────────
    legend_col = timeline_end_col + 3
    legend_row = 4
    legend_title_rng = ws.range((legend_row, legend_col), (legend_row, legend_col + 3))
    legend_title_rng.merge()
    legend_title_rng.value = "Campaign Colors"
    legend_title_rng.font.bold = True
    legend_title_rng.color = (226, 239, 218)
    _safe_set_alignment(legend_title_rng, horizontal=-4131)
    for idx, campaign_id in enumerate(sorted(campaign_colors), start=legend_row + 1):
        label_rng = ws.range((idx, legend_col))
        swatch_rng = ws.range((idx, legend_col + 1))
        count_rng = ws.range((idx, legend_col + 2))
        unit_rng = ws.range((idx, legend_col + 3))
        label_rng.value = campaign_id
        grade = campaign_grades.get(campaign_id, "")
        swatch_rng.value = grade if grade else " "
        swatch_rng.color = campaign_colors[campaign_id]
        swatch_rng.font.color = _contrast_font(campaign_colors[campaign_id])
        swatch_rng.font.bold = True
        swatch_rng.font.size = 8
        count_rng.value = (
            segments[segments["Campaign"].astype(str) == campaign_id]["Resource_ID"].nunique()
        )
        unit_rng.value = "lanes"
        _safe_set_alignment(label_rng, horizontal=-4131)
        _safe_set_alignment(swatch_rng, horizontal=-4108)
        _safe_set_alignment(count_rng, horizontal=-4108)

    # ── Guide lines (doc panel below legend) ─────────────────────────────
    guide_lines = _gantt_guide_lines(segments, bucket_hours, timeline_start, timeline_end)
    guide_start_row = legend_row + len(campaign_colors) + 2
    guide_title = ws.range((guide_start_row, legend_col), (guide_start_row, legend_col + 3))
    guide_title.merge()
    guide_title.value = "Reading Guide"
    guide_title.font.bold = True
    guide_title.color = DOC_FILL
    guide_title.font.color = TITLE_COLOR
    for g_idx, line in enumerate(guide_lines, start=guide_start_row + 1):
        g_rng = ws.range((g_idx, legend_col), (g_idx, legend_col + 3))
        g_rng.merge()
        g_rng.value = line
        g_rng.font.size = 9
        g_rng.font.color = (79, 79, 79)

    _apply_gantt_window_layout(ws, timeline_start_col)
    return ws


def _schedule_display_rows(schedule_df, campaign_df):
    if schedule_df is None or schedule_df.empty:
        return pd.DataFrame()

    detail = schedule_df.copy()
    detail["Planned_Start"] = pd.to_datetime(detail.get("Planned_Start"), errors="coerce")
    detail["Planned_End"] = pd.to_datetime(detail.get("Planned_End"), errors="coerce")
    detail["Heat_No"] = pd.to_numeric(detail.get("Heat_No"), errors="coerce")
    operation_order = {"EAF": 1, "LRF": 2, "VD": 3, "CCM": 4, "RM": 5}
    detail["__op_sort"] = detail.get("Operation", pd.Series(dtype=object)).astype(str).str.upper().map(
        lambda op: operation_order.get(op, 99)
    )

    campaign_order = []
    if campaign_df is not None and not getattr(campaign_df, "empty", True) and "Campaign_ID" in campaign_df.columns:
        campaign_order = [str(cid).strip() for cid in campaign_df["Campaign_ID"].tolist() if str(cid).strip()]
    campaign_order_map = {cid: idx for idx, cid in enumerate(campaign_order)}
    campaign_meta = {}
    if campaign_df is not None and not getattr(campaign_df, "empty", True):
        meta_cols = [col for col in ["Campaign_ID", "Grade", "Heats", "Total_MT", "Status", "Release_Status", "SOs_Covered"] if col in campaign_df.columns]
        for _, row in campaign_df[meta_cols].iterrows():
            cid = str(row.get("Campaign_ID", "")).strip()
            if cid:
                campaign_meta[cid] = row.to_dict()

    visible_cols = list(schedule_df.columns)
    display_rows = []
    for campaign_id, camp_rows in sorted(
        detail.groupby("Campaign", sort=False),
        key=lambda item: campaign_order_map.get(str(item[0]).strip(), 9999),
    ):
        camp_key = str(campaign_id).strip()
        meta = campaign_meta.get(camp_key, {})
        release_status = str(meta.get("Release_Status", meta.get("Status", ""))).strip()
        total_mt = meta.get("Total_MT", "")
        heats_count = meta.get("Heats", "")
        grade_val = meta.get("Grade", "")
        sos_val = str(meta.get("SOs_Covered", "") or "")

        # Gather campaign-wide time span from detail rows
        camp_starts = pd.to_datetime(camp_rows["Planned_Start"], errors="coerce").dropna()
        camp_ends   = pd.to_datetime(camp_rows["Planned_End"],   errors="coerce").dropna()

        campaign_header = {col: "" for col in visible_cols}
        campaign_header["Job_ID"] = f"CAMPAIGN  {camp_key}"
        if "Campaign" in campaign_header:
            campaign_header["Campaign"] = camp_key
        if "Grade" in campaign_header:
            campaign_header["Grade"] = grade_val
        if "Qty_MT" in campaign_header:
            campaign_header["Qty_MT"] = total_mt
        if "Heat_No" in campaign_header:
            campaign_header["Heat_No"] = heats_count
        if "Status" in campaign_header:
            campaign_header["Status"] = release_status
        if "Planned_Start" in campaign_header and not camp_starts.empty:
            campaign_header["Planned_Start"] = camp_starts.min()
        if "Planned_End" in campaign_header and not camp_ends.empty:
            campaign_header["Planned_End"] = camp_ends.max()
        if "Duration_Hrs" in campaign_header and not camp_starts.empty and not camp_ends.empty:
            try:
                camp_span = round((camp_ends.max() - camp_starts.min()).total_seconds() / 3600.0, 1)
                campaign_header["Duration_Hrs"] = camp_span
            except Exception:
                pass
        if "SO_ID" in campaign_header:
            so_parts = [s.strip() for s in sos_val.split(",") if s.strip()]
            if len(so_parts) <= 3:
                campaign_header["SO_ID"] = ", ".join(so_parts)
            else:
                campaign_header["SO_ID"] = f"{', '.join(so_parts[:3])} (+{len(so_parts) - 3} more)"
        campaign_header["__row_kind"] = "campaign_header"
        display_rows.append(campaign_header)

        sms_rows = camp_rows[camp_rows["Operation"].astype(str).str.upper().isin({"EAF", "LRF", "VD", "CCM"})].copy()
        rm_rows = camp_rows[camp_rows["Operation"].astype(str).str.upper().eq("RM")].copy()

        if not sms_rows.empty:
            sms_rows = sms_rows.sort_values(["Heat_No", "__op_sort", "Planned_Start", "Job_ID"], kind="stable")
            total_heats = int(pd.to_numeric(sms_rows["Heat_No"], errors="coerce").max() or meta.get("Heats") or 0)
            heat_groups = []
            for heat_no, heat_values in sms_rows.groupby("Heat_No", dropna=True, sort=False):
                heat_groups.append(
                    (
                        heat_no,
                        pd.to_datetime(heat_values.get("Planned_Start"), errors="coerce").min(),
                        heat_values.sort_values(["__op_sort", "Planned_Start", "Job_ID"], kind="stable"),
                    )
                )
            heat_groups.sort(key=lambda item: (pd.isna(item[1]), item[1], item[0]))
            for heat_no, _, heat_values in heat_groups:
                heat_number = int(heat_no)
                heat_header = {col: "" for col in visible_cols}
                # Build resource chain: EAF-01 -> LRF-02 -> CCM-01
                res_chain_parts = []
                for _, hrow in heat_values.sort_values("__op_sort").iterrows():
                    res = str(hrow.get("Resource_ID", "")).strip()
                    op = str(hrow.get("Operation", "")).strip()
                    res_chain_parts.append(res if res else op)
                res_chain = " -> ".join(p for p in res_chain_parts if p)

                h_start = heat_values["Planned_Start"].min()
                h_end = heat_values["Planned_End"].max()
                try:
                    h_dur = round((h_end - h_start).total_seconds() / 3600.0, 1)
                    dur_str = h_dur
                except Exception:
                    dur_str = ""

                # Combine heat label and resource chain into Job_ID for at-a-glance reading
                heat_label = f"Heat {heat_number} / {total_heats}"
                if res_chain:
                    heat_header["Job_ID"] = f"{heat_label}    {res_chain}"
                else:
                    heat_header["Job_ID"] = heat_label
                if "Campaign" in heat_header:
                    heat_header["Campaign"] = camp_key
                # Distinct SOs contributing to this heat
                if "SO_ID" in heat_header:
                    heat_so_list = (
                        heat_values["SO_ID"].dropna().astype(str)
                        .str.strip().unique().tolist()
                    )
                    heat_so_list = [s for s in heat_so_list if s and s not in ("nan", "")]
                    if len(heat_so_list) <= 2:
                        heat_header["SO_ID"] = ", ".join(heat_so_list)
                    else:
                        heat_header["SO_ID"] = f"{heat_so_list[0]}, {heat_so_list[1]} (+{len(heat_so_list)-2})"
                if "Heat_No" in heat_header:
                    heat_header["Heat_No"] = heat_number
                if "Planned_Start" in heat_header:
                    heat_header["Planned_Start"] = h_start
                if "Planned_End" in heat_header:
                    heat_header["Planned_End"] = h_end
                if "Duration_Hrs" in heat_header:
                    heat_header["Duration_Hrs"] = dur_str
                # Qty_MT: sum of mass for this heat (SMS batch weight)
                if "Qty_MT" in heat_header:
                    h_mt = pd.to_numeric(heat_values.get("Qty_MT"), errors="coerce").sum()
                    heat_header["Qty_MT"] = round(h_mt, 1) if h_mt > 0 else ""
                # Leave Status blank — chain is already visible in Job_ID
                if "Operation" in heat_header:
                    heat_header["Operation"] = "SMS Batch"
                heat_header["__row_kind"] = "heat_header"
                display_rows.append(heat_header)

                _OP_DISPLAY = {
                    "EAF": "Melting",
                    "LRF": "Refining",
                    "VD": "Degassing",
                    "CCM": "Casting",
                }
                for _, row in heat_values.iterrows():
                    rec = {col: row.get(col, "") for col in visible_cols}
                    op_raw = str(rec.get("Operation", "")).strip().upper()
                    if op_raw in _OP_DISPLAY:
                        rec["Operation"] = _OP_DISPLAY[op_raw]
                    sec_val = str(rec.get("Section_mm", "")).strip()
                    if sec_val in ("-", "", "nan", "None"):
                        rec["Section_mm"] = "Batch"
                    # Keep Job_ID clean for cross-sheet joins; visual hierarchy is applied via Excel indentation.
                    rec["Job_ID"] = str(rec.get("Job_ID", "") or "").strip()
                    rec["__row_kind"] = "detail_sms"
                    display_rows.append(rec)

        if not rm_rows.empty:
            rm_rows = rm_rows.sort_values(["Planned_Start", "Planned_End", "Job_ID"], kind="stable")
            rm_total_mt = pd.to_numeric(rm_rows.get("Qty_MT"), errors="coerce").sum()
            sections = (
                rm_rows.get("Section_mm", pd.Series(dtype=object))
                .dropna()
                .astype(str)
                .unique()
                .tolist()
            )
            sec_str = ", ".join(s for s in sections if s not in ("nan", "-", "", "None"))[:40]
            rm_header = {col: "" for col in visible_cols}
            rm_header["Job_ID"] = f"Rolling Orders ({len(rm_rows)} lines)"
            if "Campaign" in rm_header:
                rm_header["Campaign"] = camp_key
            if "Grade" in rm_header:
                rm_header["Grade"] = meta.get("Grade", "")
            if "Section_mm" in rm_header:
                rm_header["Section_mm"] = sec_str
            if "Qty_MT" in rm_header:
                rm_header["Qty_MT"] = round(rm_total_mt, 1) if rm_total_mt > 0 else ""
            if "Planned_Start" in rm_header:
                rm_header["Planned_Start"] = rm_rows["Planned_Start"].min()
            if "Planned_End" in rm_header:
                rm_header["Planned_End"] = rm_rows["Planned_End"].max()
            if "Status" in rm_header:
                rm_header["Status"] = "Rolling Dispatch"
            rm_header["__row_kind"] = "rm_header"
            display_rows.append(rm_header)
            for _, row in rm_rows.iterrows():
                rec = {col: row.get(col, "") for col in visible_cols}
                op_raw = str(rec.get("Operation", "")).strip().upper()
                if op_raw == "RM":
                    rec["Operation"] = "Rolling"
                so_raw = str(rec.get("SO_ID", "") or "")
                if "," in so_raw:
                    so_parts = [s.strip() for s in so_raw.split(",") if s.strip()]
                    if len(so_parts) > 2:
                        rec["SO_ID"] = f"{so_parts[0]}, {so_parts[1]}  +{len(so_parts) - 2} more"
                # Keep Job_ID clean for cross-sheet joins; visual hierarchy is applied via Excel indentation.
                rec["Job_ID"] = str(rec.get("Job_ID", "") or "").strip()
                rec["__row_kind"] = "detail_rm"
                display_rows.append(rec)

        blank_row = {col: "" for col in visible_cols}
        blank_row["__row_kind"] = "blank"
        display_rows.append(blank_row)

    return pd.DataFrame(display_rows)


def _margin_fill(margin_hrs):
    try:
        margin = float(margin_hrs)
    except Exception:
        return None
    if pd.isna(margin):
        return None
    if margin < 0:
        return LATE_COLOR
    if margin < 24:
        return AMBER_COLOR
    return ON_TIME_CLR


def _parse_percentish(value):
    text = str(value or "").strip().replace("%", "")
    if text == "":
        return None
    try:
        return float(text)
    except Exception:
        return None


def _format_campaign_schedule(ws, header_row, headers, campaign_df):
    if not headers:
        return
    width = len(headers)

    # ── Column widths ─────────────────────────────────────────────────────────
    width_map = {
        "Campaign_ID": 16, "Campaign_Group": 16, "Grade": 14, "Section_mm": 12,
        "Sections_Covered": 20, "Total_MT": 11, "Heats": 8, "Heats_Calc_Method": 18,
        "Heats_Calc_Warnings": 34, "Order_Count": 11,
        "Priority": 10, "Release_Status": 15, "Material_Issue": 24,
        "EAF_Start": 18, "CCM_Start": 18, "RM_Start": 18, "RM_End": 18,
        "Duration_Hrs": 12, "Due_Date": 16, "Margin_Hrs": 12, "Status": 13,
        "SOs_Covered": 34,
    }
    for idx, header in enumerate(headers, start=1):
        try:
            ws.range((header_row, idx)).column_width = width_map.get(header, 14)
        except Exception:
            pass

    # ── Header row height ─────────────────────────────────────────────────────
    try:
        ws.range((header_row, 1)).row_height = 24
    except Exception:
        pass

    if campaign_df is None or campaign_df.empty:
        return
    last_row = header_row + len(campaign_df)

    # ── AutoFilter ────────────────────────────────────────────────────────────
    try:
        ws.api.AutoFilterMode = False
    except Exception:
        pass
    try:
        ws.range((header_row, 1), (last_row, width)).api.AutoFilter()
    except Exception:
        pass

    # ── Date formats  (weekday prefix) ────────────────────────────────────────
    for date_col in ["EAF_Start", "CCM_Start", "RM_Start", "RM_End", "Due_Date"]:
        if date_col in headers:
            col = headers.index(date_col) + 1
            ws.range((header_row + 1, col), (last_row, col)).number_format = "ddd dd-mmm hh:mm"

    # ── Number formats ────────────────────────────────────────────────────────
    for num_col in ["Total_MT", "Duration_Hrs", "Margin_Hrs"]:
        if num_col in headers:
            col = headers.index(num_col) + 1
            ws.range((header_row + 1, col), (last_row, col)).number_format = "0.00"

    # ── SOs_Covered: wrap + shrink font ───────────────────────────────────────
    for text_col in ["SOs_Covered", "Heats_Calc_Warnings"]:
        if text_col in headers:
            col = headers.index(text_col) + 1
            text_rng = ws.range((header_row + 1, col), (last_row, col))
            text_rng.api.WrapText = True
            if text_col == "SOs_Covered":
                text_rng.font.size = 8

    # ── Center-align numeric summary columns ──────────────────────────────────
    center_cols = {"Heats", "Order_Count", "Total_MT", "Duration_Hrs", "Margin_Hrs",
                   "Priority", "Status", "Section_mm"}
    for idx, header in enumerate(headers, start=1):
        if header in center_cols:
            try:
                ws.range((header_row + 1, idx), (last_row, idx)).api.HorizontalAlignment = -4108
            except Exception:
                pass

    # ── Pre-compute column indices ────────────────────────────────────────────
    margin_col = (headers.index("Margin_Hrs") + 1) if "Margin_Hrs" in headers else None
    status_col = (headers.index("Status") + 1) if "Status" in headers else None
    release_col = (headers.index("Release_Status") + 1) if "Release_Status" in headers else None
    priority_col = (headers.index("Priority") + 1) if "Priority" in headers else None

    # ── Alternating band colours ──────────────────────────────────────────────
    _BAND = ((255, 255, 255), (248, 248, 248))  # white / very light grey
    _PRIORITY_FILL = {
        "URGENT": LATE_COLOR,
        "HIGH": AMBER_COLOR,
    }

    # ── Per-row formatting ────────────────────────────────────────────────────
    for row_idx, (_, row) in enumerate(campaign_df.iterrows(), start=header_row + 1):
        row_rng = ws.range((row_idx, 1), (row_idx, width))

        # 1. Base: alternating band
        band = _BAND[(row_idx - header_row - 1) % 2]
        try:
            row_rng.color = band
            row_rng.row_height = 18
        except Exception:
            pass

        # 2. Release_Status full-row tint override
        release_text = str(row.get("Release_Status", "") or "").strip().upper()
        if release_text in {"MATERIAL HOLD", "SHORTAGE", "HELD"}:
            try:
                row_rng.color = AMBER_COLOR
            except Exception:
                pass

        # 3. Status column — bold + fill + accent border
        status_text = str(row.get("Status", "") or "").strip().upper()
        fill = _status_fill(row.get("Status"))
        if fill and status_col:
            try:
                ws.range((row_idx, status_col)).color = fill
                ws.range((row_idx, status_col)).font.bold = True
                # Left accent border matching the status color
                ws.range((row_idx, status_col)).api.Borders(7).Weight = 3
            except Exception:
                pass
        elif status_col:
            try:
                ws.range((row_idx, status_col)).font.bold = True
            except Exception:
                pass

        # 4. If overall status is LATE, tint the whole row
        if status_text == "LATE":
            try:
                row_rng.color = LATE_COLOR
            except Exception:
                pass

        # 5. Release_Status cell colour
        if release_col:
            release_fill = _status_fill(row.get("Release_Status"))
            if release_fill:
                try:
                    ws.range((row_idx, release_col)).color = release_fill
                except Exception:
                    pass

        # 6. Priority column colour
        if priority_col:
            pri_text = str(row.get("Priority", "") or "").strip().upper()
            pri_fill = _PRIORITY_FILL.get(pri_text)
            if pri_fill:
                try:
                    ws.range((row_idx, priority_col)).color = pri_fill
                    ws.range((row_idx, priority_col)).font.bold = True
                except Exception:
                    pass

        # 7. Margin_Hrs — gradient fill + bold/italic when negative
        if margin_col:
            margin_fill = _margin_fill(row.get("Margin_Hrs"))
            if margin_fill:
                try:
                    margin_cell = ws.range((row_idx, margin_col))
                    margin_cell.color = margin_fill
                    margin_cell.font.bold = True
                    # Italic when negative (late) for extra emphasis
                    try:
                        margin_val = float(row.get("Margin_Hrs"))
                        if margin_val < 0:
                            margin_cell.font.italic = True
                    except Exception:
                        pass
                except Exception:
                    pass


def _format_scenario_output(ws, header_row, headers, scenario_df):
    if not headers:
        return
    width_map = {
        "Scenario": 18,
        "Heats": 8,
        "Campaigns": 10,
        "Released": 9,
        "Held": 8,
        "On_Time_%": 10,
        "Weighted_Lateness_Hrs": 18,
        "Bottleneck": 12,
        "Throughput_MT_Day": 16,
        "Avg_Margin_Hrs": 14,
        "Solver": 12,
        "Overloaded": 20,
    }
    for idx, header in enumerate(headers, start=1):
        try:
            ws.range((header_row, idx)).column_width = width_map.get(header, 12)
        except Exception:
            pass
    if scenario_df is None or scenario_df.empty:
        return
    last_row = header_row + len(scenario_df)
    try:
        ws.api.AutoFilterMode = False
    except Exception:
        pass
    try:
        ws.range((header_row, 1), (last_row, len(headers))).api.AutoFilter()
    except Exception:
        pass

    on_time_col = headers.index("On_Time_%") + 1 if "On_Time_%" in headers else None
    util_cols = [idx + 1 for idx, header in enumerate(headers) if header in {"BF-01", "EAF-01", "EAF-02", "LRF-01", "LRF-02", "LRF-03", "VD-01", "CCM-01", "CCM-02", "RM-01", "RM-02"}]
    for pct_col_name in ["On_Time_%", "BF-01", "EAF-01", "EAF-02", "LRF-01", "LRF-02", "LRF-03", "VD-01", "CCM-01", "CCM-02", "RM-01", "RM-02"]:
        if pct_col_name in headers:
            col_idx = headers.index(pct_col_name) + 1
            ws.range((header_row + 1, col_idx), (last_row, col_idx)).number_format = '0.0"%"'
    for num_col_name in ["Weighted_Lateness_Hrs", "Throughput_MT_Day", "Avg_Margin_Hrs"]:
        if num_col_name in headers:
            col_idx = headers.index(num_col_name) + 1
            ws.range((header_row + 1, col_idx), (last_row, col_idx)).number_format = "0.00"

    best_row_idx = None
    best_key = None
    for row_idx, (_, row) in enumerate(scenario_df.iterrows(), start=header_row + 1):
        on_time = float(pd.to_numeric(row.get("On_Time_%"), errors="coerce") or 0)
        lateness = float(pd.to_numeric(row.get("Weighted_Lateness_Hrs"), errors="coerce") or 0)
        throughput = float(pd.to_numeric(row.get("Throughput_MT_Day"), errors="coerce") or 0)
        held = float(pd.to_numeric(row.get("Held"), errors="coerce") or 0)
        key = (-on_time, held, lateness, -throughput)
        if best_key is None or key < best_key:
            best_key = key
            best_row_idx = row_idx

        if on_time_col:
            on_time_fill = ON_TIME_CLR if on_time >= 95 else (AMBER_COLOR if on_time >= 85 else LATE_COLOR)
            ws.range((row_idx, on_time_col)).color = on_time_fill
            ws.range((row_idx, on_time_col)).font.bold = True

        for col_idx in util_cols:
            val = _parse_percentish(ws.range((row_idx, col_idx)).value)
            if val is None:
                continue
            fill = LATE_COLOR if val >= 95 else (AMBER_COLOR if val >= 80 else ON_TIME_CLR)
            ws.range((row_idx, col_idx)).color = fill

    if best_row_idx:
        ws.range((best_row_idx, 1), (best_row_idx, len(headers))).api.Borders(7).Weight = 3
        ws.range((best_row_idx, 1), (best_row_idx, len(headers))).api.Borders(8).Weight = 3
        ws.range((best_row_idx, 1), (best_row_idx, len(headers))).font.bold = True
        ws.range((best_row_idx, 1)).value = f"BEST | {ws.range((best_row_idx, 1)).value}"


def _format_schedule_output(ws, header_row, headers, schedule_df, operation_fill=None):
    if not headers:
        return

    width = len(headers)
    last_row = header_row + max(len(schedule_df), 1)
    body_start = header_row + 1

    # ── Column widths ────────────────────────────────────────────────────────
    width_map = {
        "Job_ID": 44,        # wider: holds "  Heat N/T    EAF-01 -> LRF-01 -> CCM-01"
        "Campaign": 11,
        "SO_ID": 28,
        "Grade": 12,
        "Section_mm": 11,
        "SKU_ID": 20,
        "Operation": 13,
        "Resource_ID": 11,
        "Planned_Start": 17,
        "Planned_End": 17,
        "Duration_Hrs": 9,
        "Heat_No": 8,
        "Qty_MT": 9,
        "Queue_Violation": 13,
        "Status": 18,        # narrower: chain moved to Job_ID
    }
    for idx, header in enumerate(headers, start=1):
        try:
            ws.range((header_row, idx)).column_width = width_map.get(header, 14)
        except Exception:
            pass

    if schedule_df.empty:
        return

    # ── Bulk reset body area ──────────────────────────────────────────────────
    try:
        body_rng = ws.range((body_start, 1), (last_row, width))
        body_rng.api.ClearFormats()
    except Exception:
        pass

    try:
        ws.api.AutoFilterMode = False
    except Exception:
        pass
    try:
        ws.range((header_row, 1), (last_row, width)).api.AutoFilter()
    except Exception:
        pass

    # ── Number formats ────────────────────────────────────────────────────────
    if body_start <= last_row:
        fmt_map = {
            "Planned_Start": "ddd dd-mmm hh:mm",
            "Planned_End": "ddd dd-mmm hh:mm",
            "Duration_Hrs": '0.0"h"',
            "Qty_MT": "0.##",
        }
        for col_name, fmt in fmt_map.items():
            if col_name in headers:
                col = headers.index(col_name) + 1
                try:
                    ws.range((body_start, col), (last_row, col)).number_format = fmt
                except Exception:
                    # COM may reject call if Excel is busy; format individual cells
                    for r in range(body_start, min(last_row + 1, body_start + 500)):
                        try:
                            ws.range((r, col)).number_format = fmt
                        except Exception:
                            break

    try:
        ws.range((header_row, 1), (header_row, width)).row_height = 22
    except Exception:
        pass


def run_bom_explosion_for_workbook(wb):
    """Explode the BOM, net against inventory, and write the material summary to the BOM sheet."""
    _prepare_help_environment(wb)
    data = _apply_planning_overrides(_read_all(wb))
    _assert_workbook_policy(
        data.get("config"),
        require_deferred_byproducts=True,
        require_strict_bom_structure=True,
    )
    demand = consolidate_demand(data["sales_orders"])
    demand_input = demand[["SKU_ID", "Total_Qty"]].rename(columns={"Total_Qty": "Required_Qty"})
    gross_details = explode_bom_details(
        demand_input,
        data["bom"],
        on_structure_error=str(
            (data.get("config") or {}).get("BOM_Structure_Error_Mode", "RAISE") or "RAISE"
        ).strip().lower(),
    )
    gross = gross_details["exploded"]
    byproduct_inventory_mode = str(
        (data.get("config") or {}).get("Byproduct_Inventory_Mode", "DEFERRED") or "DEFERRED"
    ).strip().lower()
    netted = net_requirements(
        gross,
        data["inventory"],
        byproduct_inventory_mode=byproduct_inventory_mode,
    )

    sku_lookup = data["skus"][["SKU_ID", "SKU_Name"]].drop_duplicates()
    netted = netted.merge(sku_lookup, on="SKU_ID", how="left")
    for col in ["Gross_Req", "Produced_Qty", "Available", "Net_Req"]:
        if col not in netted.columns:
            netted[col] = 0.0
    netted[["Gross_Req", "Produced_Qty", "Available", "Net_Req"]] = netted[
        ["Gross_Req", "Produced_Qty", "Available", "Net_Req"]
    ].round(3)
    netted = netted.rename(columns={"Available": "Available_Before"})
    if "Flow_Type" not in netted.columns:
        netted["Flow_Type"] = "INPUT"
    netted["_flow_sort"] = netted["Flow_Type"].fillna("INPUT").astype(str).str.strip().str.upper().map(
        lambda flow: 0 if flow in {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"} else 1
    )
    netted = netted[
        ["SKU_ID", "SKU_Name", "BOM_Level", "Gross_Req", "Produced_Qty", "Available_Before", "Net_Req", "Flow_Type", "_flow_sort"]
    ]
    netted = netted.sort_values(["BOM_Level", "_flow_sort", "SKU_ID"]).drop(columns=["_flow_sort"]).reset_index(drop=True)

    _render_bom_output(wb, netted, data.get("skus"), data.get("bom"))

    _status(
        wb,
        f"BOM run: {datetime.now().strftime('%d-%b %H:%M')} | {len(netted)} material rows"
        f" | BYPRODUCT={byproduct_inventory_mode.upper()} | BOM={str((data.get('config') or {}).get('BOM_Structure_Error_Mode', 'RAISE')).strip().upper()}",
    )
    wb.sheets[BOM_OUTPUT_SHEET].activate()
    return netted


def run_capacity_map_for_workbook(wb):
    _prepare_help_environment(wb)
    data = _apply_planning_overrides(_read_all(wb))
    planning = data["_planning"]
    _assert_workbook_policy(
        data.get("config"),
        require_strict_masters=True,
        require_deferred_byproducts=True,
        require_strict_bom_structure=True,
        require_no_legacy_heat_fallback=True,
        require_strict_campaign_serialization=True,
        require_preserve_exact_manual_groups=True,
    )
    campaigns = _campaigns_from_data(data)
    releasable_campaigns = [camp for camp in campaigns if camp.get("release_status") == "RELEASED"]
    demand_hrs = compute_demand_hours(
        releasable_campaigns,
        data["resources"],
        routing=data.get("routing"),
        changeover_matrix=data.get("changeover"),
        allow_defaults=False,
    )
    cap = capacity_map(demand_hrs, data["resources"], horizon_days=planning["planning_horizon_days"])

    ws = wb.sheets["Capacity_Map"]
    _refresh_capacity_map_shell(ws)
    header_row = _find_header_row(ws, required_headers={"Resource_ID", "Resource_Name", "Status"}, max_cols=20) or 3
    headers = _sheet_headers(ws, width=20, row=header_row)
    if "Plant" in headers:
        out_cols = [
            "Resource_ID",
            "Resource_Name",
            "Plant",
            "Avail_Hrs_14d",
            "Demand_Hrs",
            "Idle_Hrs",
            "Overload_Hrs",
            "Utilisation_%",
            "Status",
            "Capacity_Basis",
        ]
        max_width = 10
    else:
        out_cols = [
            "Resource_ID",
            "Resource_Name",
            "Avail_Hrs_14d",
            "Demand_Hrs",
            "Idle_Hrs",
            "Overload_Hrs",
            "Utilisation_%",
            "Status",
            "Capacity_Basis",
        ]
        max_width = 9

    _clear_table_data(ws, header_row, max_width, 200)
    ws.range((header_row + 1, 1)).value = cap[out_cols].values.tolist()

    status_col = out_cols.index("Status") + 1
    for row_idx, (_, row) in enumerate(cap.iterrows(), start=header_row + 1):
        clr = {
            "OVERLOADED": LATE_COLOR,
            "OK": ON_TIME_CLR,
            "UNDERUTILISED": AMBER_COLOR,
            "CONTINUOUS": (189, 215, 238),
        }.get(row["Status"])
        if clr:
            ws.range((row_idx, status_col)).color = clr

    _status(
        wb,
        f"Capacity Map ({ROUGH_CUT_CAPACITY_BASIS}): {datetime.now().strftime('%d-%b %H:%M')} | "
        f"{len(releasable_campaigns)} released / {len(campaigns)} total campaigns | "
        f"{sum(c['heats'] for c in releasable_campaigns)} released heats",
    )
    _refresh_kpi_dashboard(wb, capacity_df=cap, planning=planning)
    ws.activate()
    return cap


def run_schedule_for_workbook(wb):
    _prepare_help_environment(wb)
    data = _apply_planning_overrides(_read_all(wb))
    planning = data["_planning"]
    _assert_workbook_policy(
        data.get("config"),
        require_strict_masters=True,
        require_deferred_byproducts=True,
        require_strict_bom_structure=True,
        require_no_legacy_heat_fallback=True,
        require_strict_campaign_serialization=True,
        require_preserve_exact_manual_groups=True,
    )
    frozen_jobs = _read_running_jobs(wb)
    campaigns = _campaigns_from_data(data)
    frozen_campaign_ids = {
        campaign_id
        for campaign_id in (_campaign_id_from_job(job_id) for job_id in frozen_jobs)
        if campaign_id
    }
    releasable_campaigns = []
    held_campaigns = []
    for camp in campaigns:
        if camp.get("release_status") == "RELEASED" or camp["campaign_id"] in frozen_campaign_ids:
            if camp["campaign_id"] in frozen_campaign_ids and camp.get("release_status") != "RELEASED":
                camp = dict(camp)
                camp["release_status"] = "RUNNING LOCK"
                camp["material_status"] = "SHORTAGE"
            releasable_campaigns.append(camp)
        else:
            held_campaigns.append(camp)

    result = schedule(
        releasable_campaigns,
        data["resources"],
        planning_start=_deterministic_planning_start(
            planning["planning_horizon_days"],
            frozen_jobs=frozen_jobs,
            anchor_dates=data["sales_orders"].get("Delivery_Date", []),
        ),
        planning_horizon_days=planning["planning_horizon_days"],
        machine_down_resource=planning["machine_down_resource"],
        machine_down_hours=planning["machine_down_hours"],
        machine_down_start_hour=planning.get("machine_down_start_hour", 0),
        frozen_jobs=frozen_jobs,
        routing=data.get("routing"),
        queue_times=data.get("queue_times"),
        changeover_matrix=data.get("changeover"),
        config=data.get("config"),
        solver_time_limit_sec=planning.get("solver_time_limit_sec", 30),
    )
    heat_sched = result["heat_schedule"].copy()
    camp_sched = result["campaign_schedule"].copy()
    solver_status = result["solver_status"]
    cap_for_dashboard = capacity_map_from_schedule(
        heat_sched,
        data["resources"],
        horizon_days=planning["planning_horizon_days"],
    )
    if cap_for_dashboard.empty:
        cap_for_dashboard = capacity_map(
            compute_demand_hours(
                releasable_campaigns,
                data["resources"],
                routing=data.get("routing"),
                changeover_matrix=data.get("changeover"),
                allow_defaults=False,
            ),
            data["resources"],
            horizon_days=planning["planning_horizon_days"],
        )
    detail_sched = _schedule_detail_rows(heat_sched, camp_sched)
    held_sched = _held_campaign_rows(held_campaigns)
    if not held_sched.empty:
        camp_sched = pd.concat([camp_sched, held_sched], ignore_index=True, sort=False)
    if not camp_sched.empty and "Campaign_ID" in camp_sched.columns:
        campaign_num = pd.to_numeric(
            camp_sched["Campaign_ID"].astype(str).str.extract(r"(\d+)")[0],
            errors="coerce",
        ).fillna(9999)
        camp_sched = camp_sched.assign(_campaign_num=campaign_num).sort_values(
            ["_campaign_num", "Campaign_ID"]
        ).drop(columns=["_campaign_num"]).reset_index(drop=True)

    if not camp_sched.empty:
        for dt_col in ["EAF_Start", "CCM_Start", "RM_Start", "RM_End", "Due_Date"]:
            if dt_col in camp_sched.columns:
                camp_sched[dt_col] = pd.to_datetime(camp_sched[dt_col], errors="coerce")
        duration_end = (
            camp_sched.get("RM_End")
            .fillna(camp_sched.get("CCM_Start"))
            .fillna(camp_sched.get("EAF_Start"))
        )
        camp_sched["Duration_Hrs"] = (
            (duration_end - camp_sched.get("EAF_Start")).dt.total_seconds() / 3600.0
        ).round(2)
        camp_sched["Duration_Hrs"] = camp_sched["Duration_Hrs"].where(
            camp_sched.get("EAF_Start").notna() & duration_end.notna(),
            "",
        )
        camp_sched["Margin_Hrs"] = (
            (camp_sched.get("Due_Date") - camp_sched.get("RM_End")).dt.total_seconds() / 3600.0
        ).round(2)
        camp_sched["Margin_Hrs"] = camp_sched["Margin_Hrs"].where(
            camp_sched.get("Due_Date").notna() & camp_sched.get("RM_End").notna(),
            "",
        )

    sku_lookup = data["skus"][["SKU_ID", "SKU_Name"]].drop_duplicates()
    if not detail_sched.empty and "SKU_Name" not in detail_sched.columns:
        detail_sched = detail_sched.merge(sku_lookup, on="SKU_ID", how="left")
    display_sched = _schedule_display_rows(detail_sched, camp_sched)

    ws_h = wb.sheets["Schedule_Output"]
    _refresh_schedule_output_shell(ws_h)
    heat_header_row = 3
    heat_headers = list(SCHEDULE_OUTPUT_HEADERS)
    if not display_sched.empty and heat_headers:
        for header in heat_headers:
            if header not in display_sched.columns:
                display_sched[header] = ""
        heat_out = display_sched[heat_headers]
    else:
        heat_out = display_sched

    try:
        ws_h.range((heat_header_row, len(heat_headers) + 1), (2000, 18)).clear_contents()
        ws_h.range((heat_header_row, len(heat_headers) + 1), (2000, 18)).color = None
    except Exception:
        pass
    _clear_table_data(ws_h, heat_header_row, len(heat_headers), 2000)
    try:
        ws_h.range((heat_header_row + 1, 1), (2000, len(heat_headers))).api.ClearFormats()
    except Exception:
        pass
    if not heat_out.empty:
        ws_h.range((heat_header_row + 1, 1)).value = heat_out.values.tolist()
    if heat_headers:
        _refresh_schedule_output_shell(ws_h, detail_sched, solver_status)
        _format_schedule_output(
            ws_h,
            heat_header_row,
            heat_headers,
            display_sched,
            operation_fill=data.get("operation_fill"),
        )

    if "Campaign_Schedule" in [sheet.name for sheet in wb.sheets]:
        ws_c = wb.sheets["Campaign_Schedule"]
        _refresh_campaign_schedule_shell(ws_c)
        camp_header_row = 3
        camp_headers = CAMPAIGN_SCHEDULE_HEADERS
        if not camp_sched.empty and camp_headers:
            for header in camp_headers:
                if header not in camp_sched.columns:
                    camp_sched[header] = ""
            camp_out = camp_sched[camp_headers]
        else:
            camp_out = camp_sched

        _clear_table_data(ws_c, camp_header_row, len(camp_headers), 500)
        if not camp_out.empty:
            ws_c.range((camp_header_row + 1, 1)).value = camp_out.values.tolist()
        if not camp_out.empty:
            _format_campaign_schedule(ws_c, camp_header_row, camp_headers, camp_out)

    _render_material_plan(wb, releasable_campaigns + held_campaigns, data.get("skus"))
    _render_equipment_schedule(wb, detail_sched, data["resources"])
    _render_schedule_gantt(
        wb,
        detail_sched,
        result.get("planning_start", datetime.now().replace(minute=0, second=0, microsecond=0)),
        result.get("planning_horizon_days", planning["planning_horizon_days"]),
        resources=data.get("resources"),
    )

    late = len(camp_sched[camp_sched["Status"] == "LATE"]) if not camp_sched.empty else 0
    held = len(camp_sched[camp_sched["Status"] == "MATERIAL HOLD"]) if not camp_sched.empty else 0
    _status(
        wb,
        f"Schedule: {solver_status} | {len(releasable_campaigns)} released / {len(campaigns)} total campaigns | "
        f"{sum(c['heats'] for c in releasable_campaigns)} released heats | "
        f"{result.get('campaign_serialization_mode', 'STRICT_END_TO_END')} | "
        f"{result.get('master_data_mode', 'STRICT_MASTERS')} | "
        f"{late} late | {held} held | "
        f"{datetime.now().strftime('%d-%b %H:%M')}",
    )
    _refresh_kpi_dashboard(
        wb,
        capacity_df=cap_for_dashboard,
        schedule_df=detail_sched,
        campaign_df=camp_sched,
        solver_status=solver_status,
        planning=planning,
    )
    if "Campaign_Schedule" in [sheet.name for sheet in wb.sheets]:
        wb.sheets["Campaign_Schedule"].activate()
    else:
        ws_h.activate()
    result["campaign_schedule"] = camp_sched
    result["heat_schedule"] = detail_sched
    result["released_campaigns"] = releasable_campaigns
    result["held_campaigns"] = held_campaigns
    return result


def run_ctp_for_workbook(wb):
    """Run Capable-to-Promise for all requests in CTP_Request sheet."""
    _prepare_help_environment(wb)
    data = _read_all(wb)
    _assert_workbook_policy(
        data.get("config"),
        require_strict_masters=True,
        require_deferred_byproducts=True,
        require_strict_bom_structure=True,
        require_no_legacy_heat_fallback=True,
        require_strict_campaign_serialization=True,
        require_preserve_exact_manual_groups=True,
    )

    if CTP_REQUEST_SHEET not in _sheet_names(wb):
        _status(wb, "CTP_Request sheet not found. Regenerate the workbook template.")
        return

    ws_req = wb.sheets[CTP_REQUEST_SHEET]
    try:
        req_df = _read_sheet_table(
            ws_req, ("A3", "A1"), ("SKU_ID", "Qty_MT", "Requested_Date")
        )
    except Exception:
        _status(wb, "CTP_Request sheet missing required columns: SKU_ID, Qty_MT, Requested_Date.")
        return

    req_df = req_df.dropna(subset=["SKU_ID", "Qty_MT"])
    req_df = req_df[req_df["SKU_ID"].astype(str).str.strip().ne("")]
    if req_df.empty:
        _status(wb, "No CTP requests found. Fill in CTP_Request sheet and retry.")
        return

    campaigns = _campaigns_from_data(data)
    frozen_jobs = _read_running_jobs(wb)
    planning_horizon_days = _planning_horizon_days(data)
    planning_start = _deterministic_planning_start(
        planning_horizon_days,
        frozen_jobs=frozen_jobs,
        anchor_dates=list(data["sales_orders"].get("Delivery_Date", [])) + list(req_df.get("Requested_Date", [])),
    )

    results = []
    for _, req in req_df.iterrows():
        sku_id = str(req["SKU_ID"]).strip()
        try:
            qty_mt = float(req["Qty_MT"] or 0.0)
        except Exception:
            qty_mt = 0.0
        requested_date = pd.to_datetime(req.get("Requested_Date"), errors="coerce")
        request_id = str(req.get("Request_ID", "") or "").strip() or sku_id

        if qty_mt <= 0:
            continue

        try:
            min_cmt, max_cmt = _get_params(data)
            result = capable_to_promise(
                sku_id=sku_id,
                qty_mt=qty_mt,
                requested_date=requested_date,
                campaigns=campaigns,
                resources=data["resources"],
                bom=data["bom"],
                inventory=data["inventory"],
                routing=data.get("routing"),
                skus=data.get("skus"),
                planning_start=planning_start,
                config=data.get("config"),
                min_campaign_mt=min_cmt,
                max_campaign_mt=max_cmt,
                frozen_jobs=frozen_jobs,
                queue_times=data.get("queue_times"),
                changeover_matrix=data.get("changeover"),
            )
        except Exception as exc:
            result = {
                "sku_id": sku_id,
                "qty_mt": qty_mt,
                "requested_date": requested_date,
                "earliest_completion": None,
                "earliest_delivery": None,
                "plant_completion_feasible": None,
                "delivery_feasible": None,
                "feasible": False,
                "lateness_days": None,
                "material_gaps": [],
                "campaign_action": "ERROR",
                "merged_campaign_ids": [],
                "new_campaign_ids": [],
                "inventory_lineage_status": "",
                "solver_status": f"ERROR: {exc}",
            }
        result["request_id"] = request_id
        results.append(result)

    # Convert list-of-dicts to DataFrame with the columns _render_ctp_output expects
    ctp_rows = []
    for r in results:
        gaps = r.get("material_gaps", [])
        gap_str = ", ".join(str(g.get("sku_id", "")) for g in gaps) if gaps else ""
        ctp_rows.append({
            "Request_ID":       str(r.get("request_id", r.get("sku_id", ""))),
            "SKU_ID":           str(r.get("sku_id", "")),
            "Qty_MT":           r.get("qty_mt", ""),
            "Requested_Date":   r.get("requested_date", ""),
            "Earliest_Completion": r.get("earliest_completion", ""),
            "Plant_Completion_Feasible": _ctp_plant_completion_feasible_text(r),
            "Earliest_Delivery":r.get("earliest_delivery", ""),
            "Delivery_Feasible": _ctp_feasible_text(r),
            "Lateness_Days":    r.get("lateness_days", ""),
            "Inventory_Lineage": _ctp_inventory_lineage_text(r),
            "Material_Gaps":    gap_str,
            "Campaign_Action":  str(r.get("campaign_action", "") or ""),
            "Merged_Campaigns": _ctp_campaign_list_text(r.get("merged_campaign_ids")),
            "New_Campaigns":    _ctp_campaign_list_text(r.get("new_campaign_ids")),
            "Solver_Status":    _ctp_solver_status_text(r),
        })
    ctp_df = pd.DataFrame(ctp_rows, columns=CTP_OUTPUT_HEADERS) if ctp_rows else pd.DataFrame(columns=CTP_OUTPUT_HEADERS)
    _render_ctp_output(wb, ctp_df)
    feasible_count = sum(1 for r in results if r.get("delivery_feasible", r.get("feasible")) is True)
    plant_completion_count = sum(1 for r in results if r.get("plant_completion_feasible") is True)
    blocked_count = sum(1 for r in results if str(r.get("solver_status", "")).upper().startswith("BLOCKED:"))
    unmodeled_count = sum(
        1
        for r in results
        if r.get("delivery_modeled") is False
        and r.get("delivery_feasible", r.get("feasible")) is None
        and not str(r.get("solver_status", "")).upper().startswith("BLOCKED:")
    )
    _status(
        wb,
        f"CTP: {len(results)} request(s) processed | "
        f"{plant_completion_count} plant-completion-feasible | "
        f"{feasible_count} delivery-feasible | "
        f"{unmodeled_count} delivery-unmodeled | "
        f"{blocked_count} blocked | "
        f"{datetime.now().strftime('%d-%b %H:%M')}",
    )
    if CTP_OUTPUT_SHEET in _sheet_names(wb):
        wb.sheets[CTP_OUTPUT_SHEET].activate()


def run_scenario_for_workbook(wb):
    _prepare_help_environment(wb)
    data = _read_all(wb)
    _assert_workbook_policy(
        data.get("config"),
        require_strict_masters=True,
        require_deferred_byproducts=True,
        require_strict_bom_structure=True,
        require_no_legacy_heat_fallback=True,
        require_strict_campaign_serialization=True,
        require_preserve_exact_manual_groups=True,
    )
    frozen_jobs = _read_running_jobs(wb)
    planning_horizon_days = _planning_horizon_days(data)
    planning_start = _deterministic_planning_start(
        planning_horizon_days,
        frozen_jobs=frozen_jobs,
        anchor_dates=data["sales_orders"].get("Delivery_Date", []),
    )
    scenario_headers = data.get("scenario_output_headers", list(SCENARIO_OUTPUT_HEADERS))
    results = []
    for scenario in build_scenarios(data):
        scenario_result = _run_scenario(
            data,
            scenario,
            planning_start=planning_start,
            frozen_jobs=frozen_jobs,
        )
        row = {
            "Scenario": scenario_result["scenario"],
            "Heats": scenario_result["total_heats"],
            "Campaigns": scenario_result["campaigns"],
            "Released": scenario_result.get("released_campaigns", scenario_result["campaigns"]),
            "Held": scenario_result.get("held_campaigns", 0),
            "On_Time_%": scenario_result.get("on_time_pct", 0),
            "Weighted_Lateness_Hrs": scenario_result.get("weighted_lateness_hours", 0),
            "Bottleneck": scenario_result.get("bottleneck", "-"),
            "Throughput_MT_Day": scenario_result.get("throughput_mt_day", 0),
            "Avg_Margin_Hrs": scenario_result.get("avg_margin_hrs", 0),
            "Solver": scenario_result["solver_status"],
            "Overloaded": ", ".join(scenario_result["overloaded"]) or "None",
        }
        row.update({k: round(float(v), 1) for k, v in scenario_result["utilisation"].items()})
        results.append(row)

    df = pd.DataFrame(results)
    ws = wb.sheets[SCENARIO_OUTPUT_SHEET]
    _refresh_scenario_output_shell(ws, headers=scenario_headers)
    header_row = _find_header_row(
        ws,
        required_headers={"Scenario", "Heats", "Campaigns"},
        max_cols=max(len(scenario_headers) + 2, 30),
    ) or 3
    headers = list(scenario_headers)
    _clear_table_data(ws, header_row, len(headers), 200)
    if not df.empty:
        for header in headers:
            if header not in df.columns:
                df[header] = ""
        df = df[headers]
        ws.range((header_row + 1, 1)).value = df.values.tolist()
        _format_scenario_output(ws, header_row, headers, df)

    _status(wb, f"Scenarios: {datetime.now().strftime('%d-%b %H:%M')}")
    _refresh_kpi_dashboard(wb, scenario_df=df, solver_status="Scenario Comparison")
    ws.activate()
    return df


def clear_outputs_for_workbook(wb):
    _prepare_help_environment(wb)
    schedule_ws = wb.sheets["Schedule_Output"]
    _refresh_schedule_output_shell(schedule_ws)
    schedule_header_row = _find_header_row(schedule_ws, required_headers={"Job_ID", "Resource_ID", "Status"}, max_cols=25) or 3
    _clear_table_data(schedule_ws, schedule_header_row, len(_sheet_headers(schedule_ws, width=25, row=schedule_header_row)), 2000)
    _restore_output_prompt(schedule_ws, schedule_header_row)
    if "Campaign_Schedule" in [sheet.name for sheet in wb.sheets]:
        campaign_ws = wb.sheets["Campaign_Schedule"]
        _refresh_campaign_schedule_shell(campaign_ws)
    if MATERIAL_PLAN_SHEET in _sheet_names(wb):
        material_ws = wb.sheets[MATERIAL_PLAN_SHEET]
        _refresh_material_plan_shell(material_ws)
    cap_ws = wb.sheets["Capacity_Map"]
    _refresh_capacity_map_shell(cap_ws)
    cap_header_row = _find_header_row(cap_ws, required_headers={"Resource_ID", "Resource_Name", "Status"}, max_cols=20) or 3
    _clear_table_data(cap_ws, cap_header_row, len(_sheet_headers(cap_ws, width=20, row=cap_header_row)), 200)
    _restore_output_prompt(cap_ws, cap_header_row)
    bom_ws = wb.sheets[BOM_OUTPUT_SHEET]
    _refresh_bom_output_shell(bom_ws)
    scenario_ws = wb.sheets[SCENARIO_OUTPUT_SHEET]
    try:
        scenario_headers = _scenario_output_headers(
            _read_sheet_table(wb.sheets["Resource_Master"], ("A3", "A1"), ("Resource_ID",))
        )
    except Exception:
        scenario_headers = list(SCENARIO_OUTPUT_HEADERS)
    _refresh_scenario_output_shell(scenario_ws, headers=scenario_headers)
    scenario_header_row = _find_header_row(
        scenario_ws,
        required_headers={"Scenario", "Heats", "Campaigns"},
        max_cols=max(len(scenario_headers) + 2, 30),
    ) or 3
    _clear_table_data(scenario_ws, scenario_header_row, len(scenario_headers), 200)
    _restore_output_prompt(scenario_ws, scenario_header_row)
    if EQUIPMENT_SCHEDULE_SHEET in _sheet_names(wb):
        _prepare_custom_output_sheet(
            wb.sheets[EQUIPMENT_SCHEDULE_SHEET],
            "Equipment Schedule",
            "Output: Separate dispatch tables by plant and equipment. Updated by 'Run Schedule'.",
            [
                "Job_ID",
                "Campaign",
                "SO_ID",
                "Grade",
                "Section_mm",
                "SKU_ID",
                "Operation",
                "Planned_Start",
                "Planned_End",
                "Duration_Hrs",
                "Qty_MT",
                "Status",
            ],
            OUTPUT_PROMPTS[EQUIPMENT_SCHEDULE_SHEET],
            doc_lines=[
                "Role: Dispatch view grouped by equipment, with one table per machine.",
                "Use this when supervisors want a clean handoff for a specific unit instead of a single long master list.",
                "Blank rows separate equipment sections; running rows stay pinned if you mark them as RUNNING.",
                "Return to Control_Panel anytime using the workbook link in X1.",
            ],
            doc_start_col=16,
        )
    if GANTT_SHEET in _sheet_names(wb):
        wb.sheets[GANTT_SHEET].api.Cells.Clear()
        _style_title_block(
            wb.sheets[GANTT_SHEET],
            "Schedule Gantt",
            "Output: Resource swim-lane Gantt by campaign in strict release order. Updated by 'Run Schedule'.",
        )
        wb.sheets[GANTT_SHEET].range("A3").value = OUTPUT_PROMPTS[GANTT_SHEET]
        wb.sheets[GANTT_SHEET].range("A3").font.italic = True
        wb.sheets[GANTT_SHEET].range("A3").font.color = (128, 128, 128)
    if KPI_DASHBOARD_SHEET in _sheet_names(wb):
        _prepare_kpi_dashboard_shell(wb.sheets[KPI_DASHBOARD_SHEET])
    _ensure_control_panel_links(wb)
    _status(wb, f"Cleared: {datetime.now().strftime('%d-%b %H:%M')}")


def run_bom_explosion():
    return run_bom_explosion_for_workbook(_wb())


def run_capacity_map():
    return run_capacity_map_for_workbook(_wb())


def run_schedule():
    return run_schedule_for_workbook(_wb())


def run_scenario():
    return run_scenario_for_workbook(_wb())


def clear_outputs():
    return clear_outputs_for_workbook(_wb())


def run_ctp():
    return run_ctp_for_workbook(_wb())
