"""
Capacity Engine — maps demand to machine hours vs available capacity.
Planning horizon and setup defaults are config-driven.
"""
from __future__ import annotations

import pandas as pd

from engine.config import get_config
from engine.scheduler import (
    _changeover_minutes,
    _build_op_lookup,
    _campaign_sms_operations,
    _machine_groups,
    _operation_duration,
    _rm_duration,
    _validate_campaign_master_data,
    build_operation_times,
)

ROUGH_CUT_CAPACITY_BASIS = "ROUGH_CUT_HEURISTIC"
FINITE_SCHEDULE_CAPACITY_BASIS = "FINITE_SCHEDULE"


def _get_capacity_horizon_days() -> int:
    """Get capacity planning horizon in days from Algorithm_Config."""
    return get_config().get('CAPACITY_HORIZON_DAYS', 14)


def _get_capacity_setup_hours_default() -> float:
    """Get default setup hours for capacity planning from Algorithm_Config."""
    return get_config().get_duration_minutes('CAPACITY_SETUP_HOURS_DEFAULT', 0) / 60.0


def _get_capacity_changeover_hours_default() -> float:
    """Get default changeover hours for capacity planning from Algorithm_Config."""
    return get_config().get_duration_minutes('CAPACITY_CHANGEOVER_HOURS_DEFAULT', 0) / 60.0


def _require_resource_columns(resources: pd.DataFrame, required: list[str]) -> None:
    missing = [col for col in required if col not in resources.columns]
    if missing:
        raise ValueError(f"Resources are missing required columns: {', '.join(missing)}")


def _machine_capacity_hours(resources: pd.DataFrame) -> dict[str, float]:
    _require_resource_columns(resources, ["Resource_ID", "Avail_Hours_Day"])
    res = resources[["Resource_ID", "Avail_Hours_Day"]].copy()
    res["Avail_Hours_Day"] = pd.to_numeric(res["Avail_Hours_Day"], errors="coerce").fillna(20.0)
    return {
        str(row["Resource_ID"]).strip(): max(float(row["Avail_Hours_Day"] or 0.0), 1.0)
        for _, row in res.iterrows()
    }


def _initial_machine_state(machine_groups: dict[str, list[str]], resources: pd.DataFrame) -> dict[str, dict]:
    capacity_hours = _machine_capacity_hours(resources)
    states = {}
    for machines in machine_groups.values():
        for machine in machines:
            states[machine] = {
                "Demand_Hrs": 0.0,
                "Process_Hrs": 0.0,
                "Setup_Hrs": 0.0,
                "Changeover_Hrs": 0.0,
                "Task_Count": 0,
                "Avail_Hours_Day": capacity_hours.get(machine, 20.0),
                "Last_Grade": "",
            }
    return states


def _machine_score(state: dict, reserve_hours: float) -> tuple[float, float]:
    avail = max(float(state.get("Avail_Hours_Day", 20.0) or 20.0), 1.0)
    projected = float(state.get("Demand_Hrs", 0.0) or 0.0) + float(reserve_hours or 0.0)
    return (projected / avail, projected)


def _reserve_machine_hours(
    state: dict,
    *,
    process_hours: float,
    setup_hours: float = 0.0,
    changeover_hours: float = 0.0,
) -> None:
    process = round(float(process_hours or 0.0), 6)
    setup = round(float(setup_hours or 0.0), 6)
    changeover = round(float(changeover_hours or 0.0), 6)
    state["Process_Hrs"] = round(float(state.get("Process_Hrs", 0.0) or 0.0) + process, 6)
    state["Setup_Hrs"] = round(float(state.get("Setup_Hrs", 0.0) or 0.0) + setup, 6)
    state["Changeover_Hrs"] = round(float(state.get("Changeover_Hrs", 0.0) or 0.0) + changeover, 6)
    state["Demand_Hrs"] = round(
        float(state.get("Demand_Hrs", 0.0) or 0.0) + process + setup + changeover,
        6,
    )
    state["Task_Count"] = int(state.get("Task_Count", 0) or 0) + 1


def compute_demand_hours(
    campaigns: list,
    resources: pd.DataFrame,
    routing: pd.DataFrame | None = None,
    changeover_matrix: pd.DataFrame | None = None,
    *,
    allow_defaults: bool = False,
) -> pd.DataFrame:
    """
    Given a list of campaign dicts, compute rough-cut machine occupancy hours.

    `Demand_Hrs` reflects the same timing primitives as the scheduler:
    process + setup + explicit RM changeover occupancy. RM changeover is kept
    separate from task processing time so reporting stays aligned with the
    scheduler's non-embedded changeover model.

    Uses the same routing-driven operation list as the scheduler so that
    capacity analysis stays consistent with scheduling behaviour.
    """
    _require_resource_columns(resources, ["Resource_ID", "Avail_Hours_Day"])
    op_lookup = _build_op_lookup(resources)
    machine_groups = _machine_groups(resources, op_lookup=op_lookup, allow_defaults=allow_defaults)
    machine_state = _initial_machine_state(machine_groups, resources)

    for camp in campaigns:
        heats = int(camp["heats"])
        grade = camp["grade"]
        billet_family = camp.get("billet_family")

        # Use the same routing-driven operation list as the scheduler
        sms_ops = _campaign_sms_operations(camp, routing, op_lookup=op_lookup, allow_defaults=allow_defaults)
        op_times = build_operation_times(
            routing,
            grade,
            billet_family=billet_family,
            resources=resources,
            op_lookup=op_lookup,
            allow_defaults=allow_defaults,
        )
        _validate_campaign_master_data(
            camp,
            sms_ops,
            op_times,
            machine_groups,
            allow_defaults=allow_defaults,
        )

        for op in sms_ops:
            profile = op_times.get(op, {})
            cycle_hours = max(float(profile.get("cycle", 0.0) or 0.0), 0.0) / 60.0
            setup_hours = max(float(profile.get("setup", 0.0) or 0.0), 0.0) / 60.0
            if cycle_hours <= 0:
                continue
            for heat_idx in range(heats):
                total_hours = _operation_duration(profile, include_setup=heat_idx == 0) / 60.0
                task_setup = setup_hours if heat_idx == 0 else 0.0
                task_process = max(total_hours - task_setup, 0.0)
                candidates = machine_groups.get(op, [])
                if not candidates:
                    continue
                chosen = min(
                    candidates,
                    key=lambda machine: _machine_score(machine_state[machine], total_hours) + (machine,),
                )
                _reserve_machine_hours(
                    machine_state[chosen],
                    process_hours=task_process,
                    setup_hours=task_setup,
                )

        # RM — per production order (same as before)
        previous_section = None
        production_orders = sorted(
            camp.get("production_orders", []),
            key=lambda line: (
                int(line.get("priority_rank", 9)),
                pd.to_datetime(line.get("due_date")),
                pd.to_numeric(pd.Series([line.get("section_mm")]), errors="coerce").fillna(999).iloc[0],
                str(line.get("production_order_id", "")),
            ),
        )
        if not production_orders:
            production_orders = [
                {
                    "qty_mt": camp["total_coil_mt"],
                    "section_mm": camp.get("section_mm", 6.5),
                    "sku_id": "",
                    "due_date": camp.get("due_date"),
                }
            ]

        for order in production_orders:
            section = pd.to_numeric(pd.Series([order.get("section_mm")]), errors="coerce").fillna(6.5).iloc[0]
            include_setup = previous_section is None or section != previous_section
            process_minutes = _rm_duration(
                order,
                grade,
                routing,
                resources=resources,
                op_lookup=op_lookup,
                include_setup=False,
                allow_defaults=allow_defaults,
            )
            scheduled_minutes = _rm_duration(
                order,
                grade,
                routing,
                resources=resources,
                op_lookup=op_lookup,
                include_setup=include_setup,
                allow_defaults=allow_defaults,
            )
            setup_minutes = max(scheduled_minutes - process_minutes, 0)
            candidates = machine_groups.get("RM", [])
            if candidates:
                chosen = min(
                    candidates,
                    key=lambda machine: _machine_score(
                        machine_state[machine],
                        (
                            process_minutes
                            + setup_minutes
                            + _changeover_minutes(changeover_matrix, machine_state[machine].get("Last_Grade", ""), grade)
                        ) / 60.0,
                    ) + (machine,),
                )
                changeover_minutes = _changeover_minutes(
                    changeover_matrix,
                    machine_state[chosen].get("Last_Grade", ""),
                    grade,
                )
                _reserve_machine_hours(
                    machine_state[chosen],
                    process_hours=process_minutes / 60.0,
                    setup_hours=setup_minutes / 60.0,
                    changeover_hours=changeover_minutes / 60.0,
                )
                machine_state[chosen]["Last_Grade"] = grade
            previous_section = section

    demand_records = []
    for machine, state in machine_state.items():
        demand_hours = round(float(state.get("Demand_Hrs", 0.0) or 0.0), 2)
        if demand_hours <= 1e-9:
            continue
        demand_records.append(
            {
                "Resource_ID": machine,
                "Demand_Hrs": demand_hours,
                "Process_Hrs": round(float(state.get("Process_Hrs", 0.0) or 0.0), 2),
                "Setup_Hrs": round(float(state.get("Setup_Hrs", 0.0) or 0.0), 2),
                "Changeover_Hrs": round(float(state.get("Changeover_Hrs", 0.0) or 0.0), 2),
                "Task_Count": int(state.get("Task_Count", 0) or 0),
            }
        )

    demand = pd.DataFrame(demand_records)
    if demand.empty:
        return pd.DataFrame(columns=["Resource_ID", "Demand_Hrs", "Process_Hrs", "Setup_Hrs", "Changeover_Hrs", "Task_Count"])
    return demand.sort_values("Resource_ID").reset_index(drop=True)


def compute_schedule_demand_hours(schedule_df: pd.DataFrame | None) -> pd.DataFrame:
    """
    Aggregate actual scheduled occupancy by resource from a finite schedule.
    """
    columns = ["Resource_ID", "Demand_Hrs", "Process_Hrs", "Setup_Hrs", "Changeover_Hrs", "Task_Count"]
    if schedule_df is None or getattr(schedule_df, "empty", True):
        return pd.DataFrame(columns=columns)

    df = schedule_df.copy()
    if "Resource_ID" not in df.columns:
        return pd.DataFrame(columns=columns)

    df["Resource_ID"] = df["Resource_ID"].fillna("").astype(str).str.strip()
    df["Job_ID"] = df.get("Job_ID", "").fillna("").astype(str).str.strip()
    df["Planned_Start"] = pd.to_datetime(df.get("Planned_Start"), errors="coerce")
    df["Planned_End"] = pd.to_datetime(df.get("Planned_End"), errors="coerce")
    df = df[
        df["Resource_ID"].ne("")
        & df["Job_ID"].ne("")
        & df["Planned_Start"].notna()
        & df["Planned_End"].notna()
        & (df["Planned_End"] >= df["Planned_Start"])
    ].copy()
    if df.empty:
        return pd.DataFrame(columns=columns)

    df["Demand_Hrs"] = (
        (df["Planned_End"] - df["Planned_Start"]).dt.total_seconds() / 3600.0
    ).clip(lower=0.0)
    demand = (
        df.groupby("Resource_ID", as_index=False)
        .agg(Demand_Hrs=("Demand_Hrs", "sum"), Task_Count=("Job_ID", "count"))
        .sort_values("Resource_ID")
        .reset_index(drop=True)
    )
    demand["Demand_Hrs"] = demand["Demand_Hrs"].round(2)
    demand["Process_Hrs"] = demand["Demand_Hrs"]
    demand["Setup_Hrs"] = 0.0
    demand["Changeover_Hrs"] = 0.0
    return demand[columns]


def capacity_map(
    demand_hrs: pd.DataFrame,
    resources: pd.DataFrame,
    horizon_days: int | None = None,
    *,
    basis: str = ROUGH_CUT_CAPACITY_BASIS,
) -> pd.DataFrame:
    """
    Compare rough-cut machine occupancy hours vs available per resource.
    """
    if horizon_days is None:
        horizon_days = _get_capacity_horizon_days()
    _require_resource_columns(resources, ["Resource_ID", "Resource_Name", "Plant", "Avail_Hours_Day"])
    res = resources[["Resource_ID", "Resource_Name", "Plant", "Avail_Hours_Day"]].copy()
    res["Avail_Hours_Day"] = pd.to_numeric(res["Avail_Hours_Day"], errors="coerce").fillna(20)
    horizon_days = max(int(horizon_days or _get_capacity_horizon_days()), 1)
    horizon_col = f"Avail_Hrs_{horizon_days}d"
    res[horizon_col] = res["Avail_Hours_Day"] * horizon_days

    cap = res.copy()
    demand_cols = ["Demand_Hrs", "Process_Hrs", "Setup_Hrs", "Changeover_Hrs", "Task_Count"]
    if demand_hrs is None or getattr(demand_hrs, "empty", True):
        demand_df = pd.DataFrame(columns=["Resource_ID"] + demand_cols)
    else:
        if not {"Resource_ID", "Demand_Hrs"}.issubset(demand_hrs.columns):
            raise ValueError("Demand hours must include Resource_ID and Demand_Hrs columns.")
        demand_df = demand_hrs.copy()
        for col in demand_cols:
            if col not in demand_df.columns:
                demand_df[col] = 0.0 if col != "Task_Count" else 0
        demand_df = demand_df.groupby("Resource_ID", as_index=False)[demand_cols].sum()
    cap = cap.merge(demand_df, on="Resource_ID", how="left")
    for col in demand_cols:
        cap[col] = pd.to_numeric(cap[col], errors="coerce").fillna(0.0 if col != "Task_Count" else 0)

    cap["Idle_Hrs"] = (cap[horizon_col] - cap["Demand_Hrs"]).clip(lower=0).round(2)
    cap["Overload_Hrs"] = (cap["Demand_Hrs"] - cap[horizon_col]).clip(lower=0).round(2)
    cap["Utilisation_%"] = (cap["Demand_Hrs"] / cap[horizon_col] * 100).round(1).fillna(0)

    cap["Status"] = "OK"
    cap.loc[cap["Overload_Hrs"] > 0, "Status"] = "OVERLOADED"
    cap.loc[cap["Utilisation_%"] < 60, "Status"] = "UNDERUTILISED"
    cap["Capacity_Basis"] = str(basis or ROUGH_CUT_CAPACITY_BASIS).strip() or ROUGH_CUT_CAPACITY_BASIS

    if horizon_col != "Avail_Hrs_14d":
        cap["Avail_Hrs_14d"] = cap[horizon_col]

    return cap


def capacity_map_from_schedule(schedule_df: pd.DataFrame | None, resources: pd.DataFrame, horizon_days: int = 14) -> pd.DataFrame:
    """
    Compare actual finite scheduled occupancy vs available per resource.
    """
    return capacity_map(
        compute_schedule_demand_hours(schedule_df),
        resources,
        horizon_days=horizon_days,
        basis=FINITE_SCHEDULE_CAPACITY_BASIS,
    )
