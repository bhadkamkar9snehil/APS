"""
Scenario Runner — reads Scenarios sheet and runs multiple what-if cases
"""
import pandas as pd
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))
from data.loader import load_all
from engine.bom_explosion import consolidate_demand, explode_bom, net_requirements
from engine.capacity import compute_demand_hours, capacity_map
from engine.scheduler import build_jobs, schedule_ortools


SCENARIOS = [
    {"name": "Baseline",              "demand_spike": 0,  "machine_down": None, "down_hrs": 0},
    {"name": "Demand +15%",           "demand_spike": 15, "machine_down": None, "down_hrs": 0},
    {"name": "EAF-01 Down 8hrs",      "demand_spike": 0,  "machine_down": "EAF-01", "down_hrs": 8},
    {"name": "Demand +15% + EAF Down","demand_spike": 15, "machine_down": "EAF-01", "down_hrs": 8},
]


def run_scenario(data: dict, scenario: dict) -> dict:
    so = data["sales_orders"].copy()

    # Apply demand spike
    if scenario["demand_spike"] > 0:
        so["Order_Qty"] = (so["Order_Qty"] * (1 + scenario["demand_spike"] / 100)).round(0)

    # Apply machine downtime — reduce available hours
    resources = data["resources"].copy()
    resources["Avail_Hours_Day"] = resources["Avail_Hours_Day"].astype(float)
    if scenario["machine_down"]:
        mask = resources["Resource_ID"] == scenario["machine_down"]
        resources.loc[mask, "Avail_Hours_Day"] = (
            resources.loc[mask, "Avail_Hours_Day"] - scenario["down_hrs"] / 14
        ).clip(lower=0)

    # BOM + netting
    demand = consolidate_demand(so)
    demand_input = demand[["SKU_ID","Total_Qty"]].rename(columns={"Total_Qty":"Required_Qty"})
    gross = explode_bom(demand_input, data["bom"])
    net = net_requirements(gross, data["inventory"])
    fg_net = net[net["SKU_ID"].str.startswith("SKU-FG")]

    # Capacity
    demand_hrs = compute_demand_hours(fg_net, data["routing"])
    cap = capacity_map(demand_hrs, resources)

    # Schedule
    buffer = float(data["scenarios"].loc["Safety Buffer (Hrs)","Value"])
    jobs = build_jobs(so, data["routing"], resources)
    sched = schedule_ortools(jobs, resources, data["changeover"], buffer_hrs=buffer)

    return {
        "scenario":     scenario["name"],
        "total_jobs":   len(sched),
        "overloaded":   cap[cap["Status"].str.contains("OVERLOAD")]["Resource_ID"].tolist(),
        "utilisation":  cap[["Resource_ID","Utilisation_%"]].set_index("Resource_ID")["Utilisation_%"].to_dict(),
    }


if __name__ == "__main__":
    data = load_all()
    print(f"{'Scenario':<30} {'Jobs':>6} {'Overloaded Resources'}")
    print("-" * 70)
    for sc in SCENARIOS:
        result = run_scenario(data, sc)
        overloaded = result["overloaded"] or ["None"]
        print(f"{result['scenario']:<30} {result['total_jobs']:>6}  {', '.join(overloaded)}")
