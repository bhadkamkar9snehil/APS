"""
Layer A — BOM Explosion + Inventory Netting
Multi-level BOM: explode demand → net against inventory → gross requirement
"""
import pandas as pd
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))
from data.loader import load_all


def explode_bom(demand: pd.DataFrame, bom: pd.DataFrame, max_levels: int = 5) -> pd.DataFrame:
    """
    Explode finished good demand through multi-level BOM.
    demand: DataFrame with [SKU_ID, Required_Qty]
    Returns: DataFrame with [SKU_ID, Level, Required_Qty, Source_SKU]
    """
    all_requirements = []
    queue = demand[["SKU_ID","Required_Qty"]].copy()
    queue["Level"] = 0

    for _ in range(max_levels):
        if queue.empty:
            break

        bom_sub = bom[["Parent_SKU","Child_SKU","Qty_Per","Scrap_%","Level"]].rename(
            columns={"Level": "BOM_Level", "Child_SKU": "Next_SKU"}
        )
        exploded = queue.merge(bom_sub, left_on="SKU_ID", right_on="Parent_SKU", how="inner")
        if exploded.empty:
            break

        exploded["Required_Qty"] = (
            exploded["Required_Qty"] * exploded["Qty_Per"] * (1 + exploded["Scrap_%"] / 100)
        ).round(3)

        all_requirements.append(
            exploded[["Next_SKU","Required_Qty","BOM_Level","Parent_SKU"]]
            .rename(columns={"Next_SKU":"SKU_ID"})
        )
        queue = exploded[["Next_SKU","Required_Qty","BOM_Level"]].rename(
            columns={"Next_SKU":"SKU_ID","BOM_Level":"Level"}
        )

    if not all_requirements:
        return pd.DataFrame(columns=["SKU_ID","Required_Qty","BOM_Level","Parent_SKU"])

    result = pd.concat(all_requirements, ignore_index=True)
    result = result.groupby(["SKU_ID","BOM_Level"], as_index=False)["Required_Qty"].sum()
    return result.sort_values("BOM_Level")


def net_requirements(gross: pd.DataFrame, inventory: pd.DataFrame) -> pd.DataFrame:
    """
    Net gross requirements against available inventory.
    Returns: DataFrame with [SKU_ID, Gross_Req, Available, Net_Req]
    """
    inv = inventory[["SKU_ID","Available_Qty"]].copy()
    netted = gross.merge(inv, on="SKU_ID", how="left")
    netted["Available_Qty"] = netted["Available_Qty"].fillna(0)
    netted["Net_Req"] = (netted["Required_Qty"] - netted["Available_Qty"]).clip(lower=0).round(3)
    return netted.rename(columns={"Required_Qty": "Gross_Req", "Available_Qty": "Available"})


def consolidate_demand(sales_orders: pd.DataFrame) -> pd.DataFrame:
    """Consolidate open SOs by SKU — respecting earliest delivery date."""
    open_so = sales_orders[sales_orders["Status"] == "Open"]
    consolidated = open_so.groupby("SKU_ID").agg(
        Total_Qty=("Order_Qty", "sum"),
        Earliest_Delivery=("Delivery_Date", "min"),
        Order_Count=("SO_ID", "count")
    ).reset_index()
    return consolidated


if __name__ == "__main__":
    data = load_all()
    demand_consolidated = consolidate_demand(data["sales_orders"])
    print("Consolidated Demand:")
    print(demand_consolidated.to_string(index=False))

    demand_input = demand_consolidated[["SKU_ID","Total_Qty"]].rename(columns={"Total_Qty":"Required_Qty"})
    gross = explode_bom(demand_input, data["bom"])
    print("\nBOM Explosion (Gross Requirements):")
    print(gross.to_string(index=False))

    netted = net_requirements(gross, data["inventory"])
    print("\nNet Requirements (after inventory):")
    print(netted.to_string(index=False))
