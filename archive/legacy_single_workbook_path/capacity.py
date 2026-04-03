"""
Layer A/B — Capacity Loader
Maps net demand → machine hours → compare vs available capacity
"""
import pandas as pd
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))
from data.loader import load_all
from engine.bom_explosion import consolidate_demand, explode_bom, net_requirements


def compute_demand_hours(net_req: pd.DataFrame, routing: pd.DataFrame) -> pd.DataFrame:
    """
    For each net requirement, compute machine hours needed.
    Accepts DataFrame with [SKU_ID, Net_Req] or [SKU_ID, Required_Qty]
    """
    df = net_req.copy()
    if "Net_Req" not in df.columns and "Required_Qty" in df.columns:
        df = df.rename(columns={"Required_Qty": "Net_Req"})

    merged = df.merge(
        routing[["SKU_ID","Operation","Resource_ID","Cycle_Time_Hr_MT","Setup_Time_Hr"]],
        on="SKU_ID", how="left"
    ).dropna(subset=["Resource_ID"])

    merged["Machine_Hrs"] = (
        merged["Net_Req"] * merged["Cycle_Time_Hr_MT"] + merged["Setup_Time_Hr"]
    ).round(2)
    return merged[["Resource_ID","Operation","SKU_ID","Net_Req","Machine_Hrs"]]


def capacity_map(demand_hrs: pd.DataFrame, resources: pd.DataFrame) -> pd.DataFrame:
    """
    Compare demand hours vs available hours per resource.
    Flags overloaded resources and idle capacity for MTS fill.
    """
    demand_agg = demand_hrs.groupby("Resource_ID", as_index=False)["Machine_Hrs"].sum()
    demand_agg.rename(columns={"Machine_Hrs": "Demand_Hrs"}, inplace=True)

    res = resources[["Resource_ID","Resource_Name","Avail_Hours_Day"]].copy()
    # Use 14-day planning horizon
    res["Avail_Hrs_14d"] = res["Avail_Hours_Day"] * 14

    cap = res.merge(demand_agg, on="Resource_ID", how="left")
    cap["Demand_Hrs"] = cap["Demand_Hrs"].fillna(0)
    cap["Idle_Hrs"]   = (cap["Avail_Hrs_14d"] - cap["Demand_Hrs"]).clip(lower=0).round(2)
    cap["Overload_Hrs"] = (cap["Demand_Hrs"] - cap["Avail_Hrs_14d"]).clip(lower=0).round(2)
    cap["Utilisation_%"] = (cap["Demand_Hrs"] / cap["Avail_Hrs_14d"] * 100).round(1)

    cap["Status"] = "OK"
    cap.loc[cap["Overload_Hrs"] > 0, "Status"] = "⚠ OVERLOADED"
    cap.loc[cap["Utilisation_%"] < 70, "Status"] = "↓ UNDERUTILISED"

    return cap


if __name__ == "__main__":
    data = load_all()
    demand = consolidate_demand(data["sales_orders"])
    # Routing is defined at FG level — use consolidated FG demand directly
    fg_demand = demand[["SKU_ID","Total_Qty"]].rename(columns={"Total_Qty":"Net_Req"})

    demand_hrs = compute_demand_hours(fg_demand, data["routing"])
    print("Demand Hours by Resource:")
    print(demand_hrs.groupby("Resource_ID")["Machine_Hrs"].sum().reset_index().to_string(index=False))

    cap = capacity_map(demand_hrs, data["resources"])
    print("\nCapacity Map (14-day horizon):")
    print(cap[["Resource_ID","Resource_Name","Avail_Hrs_14d","Demand_Hrs","Idle_Hrs","Overload_Hrs","Utilisation_%","Status"]].to_string(index=False))
