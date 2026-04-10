from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, Optional


@dataclass(frozen=True)
class SheetConfig:
    api_name: str
    excel_name: str
    header_row_1_based: int
    key_field: Optional[str]
    read_only: bool = False


SHEETS: Dict[str, SheetConfig] = {
    "config": SheetConfig("config", "Config", 3, "Key"),
    "sku-master": SheetConfig("sku-master", "SKU_Master", 3, "SKU_ID"),
    "bom": SheetConfig("bom", "BOM", 3, "BOM_ID"),
    "inventory": SheetConfig("inventory", "Inventory", 3, "SKU_ID"),
    "sales-orders": SheetConfig("sales-orders", "Sales_Orders", 3, "SO_ID"),
    "resource-master": SheetConfig("resource-master", "Resource_Master", 3, "Resource_ID"),
    "routing": SheetConfig("routing", "Routing", 3, "SKU_ID"),
    "campaign-config": SheetConfig("campaign-config", "Campaign_Config", 3, "Grade"),
    "changeover-matrix": SheetConfig("changeover-matrix", "Changeover_Matrix", 3, "From \\ To"),
    "queue-times": SheetConfig("queue-times", "Queue_Times", 3, "From_Operation"),
    "scenarios": SheetConfig("scenarios", "Scenarios", 3, "Parameter"),
    "ctp-request": SheetConfig("ctp-request", "CTP_Request", 3, "Request_ID"),
    "bom-output": SheetConfig("bom-output", "BOM_Output", 3, "SKU_ID"),
    "capacity-map": SheetConfig("capacity-map", "Capacity_Map", 3, "Resource_ID"),
    "schedule-output": SheetConfig("schedule-output", "Schedule_Output", 3, "Job_ID"),
    "campaign-schedule": SheetConfig("campaign-schedule", "Campaign_Schedule", 3, "Campaign_ID"),
    "material-plan": SheetConfig("material-plan", "Material_Plan", 3, "Campaign_ID"),
    "equipment-schedule": SheetConfig("equipment-schedule", "Equipment_Schedule", 3, "Job_ID"),
    "schedule-gantt": SheetConfig("schedule-gantt", "Schedule_Gantt", 3, "Resource_ID"),
    "scenario-output": SheetConfig("scenario-output", "Scenario_Output", 3, "Scenario"),
    "ctp-output": SheetConfig("ctp-output", "CTP_Output", 3, "Request_ID"),
    "theo-vs-actual": SheetConfig("theo-vs-actual", "Theo_vs_Actual", 3, "Job_ID"),
    "kpi-dashboard": SheetConfig("kpi-dashboard", "KPI_Dashboard", 3, "KPI"),
    "control-panel": SheetConfig("control-panel", "Control_Panel", 3, None, read_only=True),
    "help": SheetConfig("help", "Help", 1, None, read_only=True),
}


FRONTEND_COMPAT = {
    "orders": "sales-orders",
    "skus": "sku-master",
    "gantt": "schedule-output",
    "campaigns": "campaign-schedule",
    "capacity": "capacity-map",
    "material": "material-plan",
}
