"""
Layer B — Finite Scheduler
Uses OR-Tools CP-SAT to assign jobs to machines respecting:
- Machine capacity (no overlap)
- Precedence (operation sequence)
- Due date adherence
- Changeover penalties
- Customer/region grouping
- Buffer times between jobs

Run:  python engine/scheduler.py
"""
# from ortools.sat.python import cp_model  # uncomment when ortools installed
import pandas as pd
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))
from data.loader import load_all
from engine.bom_explosion import consolidate_demand, net_requirements, explode_bom
from engine.capacity import compute_demand_hours


def build_jobs(sales_orders: pd.DataFrame, routing: pd.DataFrame, resources: pd.DataFrame) -> list[dict]:
    """
    Build job list from open SOs + routing.
    Each job = one SO × one routing operation.
    """
    open_so = sales_orders[sales_orders["Status"] == "Open"].copy()
    jobs = open_so.merge(
        routing[["SKU_ID","Operation_Seq","Operation","Resource_ID","Cycle_Time_Hr_MT","Setup_Time_Hr","Min_Batch_MT","Max_Batch_MT"]],
        on="SKU_ID", how="inner"
    )
    jobs["Duration_Hrs"] = (
        jobs["Order_Qty"] * jobs["Cycle_Time_Hr_MT"] + jobs["Setup_Time_Hr"]
    ).round(2)
    jobs["Job_ID"] = jobs["SO_ID"] + "-OP" + jobs["Operation_Seq"].astype(str).str.zfill(2)
    return jobs.sort_values(["SO_ID","Operation_Seq"]).to_dict("records")


def schedule_ortools(jobs: list[dict], resources: pd.DataFrame,
                     changeover: pd.DataFrame, buffer_hrs: float = 2.0) -> pd.DataFrame:
    """
    Finite scheduling using OR-Tools CP-SAT.
    Returns schedule DataFrame with planned start/end per job.

    TODO: Replace stub with actual CP-SAT model in Phase 3.
    """
    # ── STUB: Simple EDD greedy dispatch (replace with CP-SAT) ──────────────
    # Earliest Due Date first, per resource
    from datetime import datetime, timedelta

    resource_clocks = {}  # tracks current free time per resource
    schedule = []

    for job in sorted(jobs, key=lambda j: (j["Delivery_Date"], j["Operation_Seq"])):
        res_id = job["Resource_ID"]
        now = resource_clocks.get(res_id, datetime.now().replace(minute=0, second=0, microsecond=0))

        start = now + timedelta(hours=buffer_hrs)
        end   = start + timedelta(hours=job["Duration_Hrs"])

        schedule.append({
            "Job_ID":        job["Job_ID"],
            "SO_ID":         job["SO_ID"],
            "SKU_ID":        job["SKU_ID"],
            "SKU_Name":      job.get("SKU_Name",""),
            "Operation":     job["Operation"],
            "Resource_ID":   res_id,
            "Planned_Start": start.strftime("%Y-%m-%d %H:%M"),
            "Planned_End":   end.strftime("%Y-%m-%d %H:%M"),
            "Duration_Hrs":  job["Duration_Hrs"],
            "Qty_MT":        job["Order_Qty"],
            "Status":        "Scheduled",
        })
        resource_clocks[res_id] = end

    return pd.DataFrame(schedule)


if __name__ == "__main__":
    data = load_all()
    jobs = build_jobs(data["sales_orders"], data["routing"], data["resources"])
    print(f"Jobs to schedule: {len(jobs)}")

    buffer = float(data["scenarios"].loc["Safety Buffer (Hrs)","Value"])
    schedule_df = schedule_ortools(jobs, data["resources"], data["changeover"], buffer_hrs=buffer)

    print("\nSchedule (first 10 jobs):")
    print(schedule_df.head(10).to_string(index=False))
    print(f"\nTotal jobs scheduled: {len(schedule_df)}")

    # Write back to Excel (Schedule_Output sheet)
    # TODO: use openpyxl to write schedule_df → APS_Steel_Template.xlsx / Schedule_Output
