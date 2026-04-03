"""
Scenario Runner — runs what-if scenarios.
"""
from __future__ import annotations

from datetime import datetime, timedelta

import pandas as pd

from engine.campaign import build_campaigns
from engine.capacity import capacity_map, capacity_map_from_schedule, compute_demand_hours
from engine.scheduler import schedule

DEFAULT_SCENARIOS = [
    {
        "name": "Baseline",
        "demand_spike": 0.0,
        "machine_down": None,
        "down_hrs": 0.0,
        "down_start_hr": 0.0,
        "yield_loss": 0.0,
        "rush_order_mt": 0.0,
        "extra_shift_hours": 0.0,
        "solver_time_limit_sec": 30.0,
    },
    {
        "name": "Demand +15%",
        "demand_spike": 15.0,
        "machine_down": None,
        "down_hrs": 0.0,
        "down_start_hr": 0.0,
        "yield_loss": 0.0,
        "rush_order_mt": 0.0,
        "extra_shift_hours": 0.0,
        "solver_time_limit_sec": 30.0,
    },
    {
        "name": "EAF-01 Down 8hrs",
        "demand_spike": 0.0,
        "machine_down": "EAF-01",
        "down_hrs": 8.0,
        "down_start_hr": 0.0,
        "yield_loss": 0.0,
        "rush_order_mt": 0.0,
        "extra_shift_hours": 0.0,
        "solver_time_limit_sec": 30.0,
    },
    {
        "name": "Demand +15% + EAF Down",
        "demand_spike": 15.0,
        "machine_down": "EAF-01",
        "down_hrs": 8.0,
        "down_start_hr": 0.0,
        "yield_loss": 0.0,
        "rush_order_mt": 0.0,
        "extra_shift_hours": 0.0,
        "solver_time_limit_sec": 30.0,
    },
]


RELEASED_STATUSES = {"RELEASED", "RUNNING LOCK"}


def _floor_hour(ts: datetime) -> datetime:
    return ts.replace(minute=0, second=0, microsecond=0)


def _coerce_datetime(value) -> datetime | None:
    ts = pd.to_datetime(value, errors="coerce")
    if pd.isna(ts):
        return None
    return ts.to_pydatetime()


def _deterministic_planning_start(
    planning_start,
    horizon_days: int,
    *,
    frozen_jobs: dict | None = None,
    anchor_dates=None,
) -> datetime:
    explicit_start = _coerce_datetime(planning_start)
    if explicit_start is not None:
        return _floor_hour(explicit_start)

    candidates = []
    for frozen in (frozen_jobs or {}).values():
        frozen_start = _coerce_datetime((frozen or {}).get("Planned_Start"))
        if frozen_start is not None:
            candidates.append(_floor_hour(frozen_start))

    raw_anchor_dates = [] if anchor_dates is None else list(anchor_dates)
    anchor_series = pd.to_datetime(pd.Series(raw_anchor_dates, dtype=object), errors="coerce").dropna()
    if not anchor_series.empty:
        anchor_dt = anchor_series.min() - pd.Timedelta(days=max(int(horizon_days or 14), 1))
        candidates.append(_floor_hour(anchor_dt.to_pydatetime()))

    if not candidates:
        return datetime(2000, 1, 1)
    return min(candidates)


def _scenario_value(data: dict, key: str, default=None, cast=float):
    try:
        value = data["scenarios"].loc[key, "Value"]
    except Exception:
        return default
    if value in ("", None) or pd.isna(value):
        return default
    if cast is None:
        return value
    try:
        return cast(value)
    except Exception:
        return default


def _inject_rush_order(so: pd.DataFrame, rush_order_mt: float, planning_start=None):
    if so.empty or rush_order_mt <= 0:
        return so
    base = so.copy()
    base["Delivery_Date"] = pd.to_datetime(base.get("Delivery_Date"), errors="coerce")
    base["Order_Date"] = pd.to_datetime(base.get("Order_Date"), errors="coerce")
    base = base.sort_values(["Delivery_Date", "Order_Date", "SO_ID"], kind="stable").reset_index(drop=True)
    template = base.iloc[0].copy()
    anchor = _coerce_datetime(planning_start) or datetime(2000, 1, 1)
    order_ts = _floor_hour(anchor)
    template["SO_ID"] = f"RUSH-{order_ts.strftime('%Y%m%d%H%M')}"
    template["Order_Qty_MT"] = round(float(rush_order_mt), 3)
    if "Order_Qty" in template.index:
        template["Order_Qty"] = round(float(rush_order_mt), 3)
    template["Priority"] = "URGENT"
    template["Status"] = "Open"
    template["Order_Date"] = order_ts
    template["Delivery_Date"] = order_ts + timedelta(days=1)
    template["Campaign_Group"] = template.get("Campaign_Group") or template.get("Grade", "Rush")
    return pd.concat([so, pd.DataFrame([template])], ignore_index=True)


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


def build_scenarios(data: dict) -> list:
    demand_spike = float(_scenario_value(data, "Demand Spike (%)", 15.0, float) or 0.0)
    down_hrs = float(_scenario_value(data, "Machine Down (Hrs)", 8.0, float) or 0.0)
    machine_down = str(_scenario_value(data, "Machine Down Resource", "EAF-01", lambda v: str(v).strip()) or "").strip()
    down_start_hr = float(_scenario_value(data, "Machine Down Start (Hr)", 0.0, float) or 0.0)
    solver_time_limit_sec = float(_scenario_value(data, "Solver Time Limit (sec)", 30.0, float) or 30.0)
    yield_loss = float(_scenario_value(data, "Yield Loss (%)", 0.0, float) or 0.0)
    rush_order_mt = float(_scenario_value(data, "Rush Order MT", 0.0, float) or 0.0)
    extra_shift_hours = float(_scenario_value(data, "Extra Shift Hours", 0.0, float) or 0.0)

    machine_down = machine_down or None
    demand_label = f"Demand +{demand_spike:g}%"
    down_label = f"{machine_down or 'Machine'} Down {down_hrs:g}hrs"
    scenarios = [
        {
            "name": "Baseline",
            "demand_spike": 0.0,
            "machine_down": None,
            "down_hrs": 0.0,
            "down_start_hr": 0.0,
            "yield_loss": 0.0,
            "rush_order_mt": 0.0,
            "extra_shift_hours": 0.0,
            "solver_time_limit_sec": solver_time_limit_sec,
        },
        {
            "name": demand_label,
            "demand_spike": demand_spike,
            "machine_down": None,
            "down_hrs": 0.0,
            "down_start_hr": 0.0,
            "yield_loss": 0.0,
            "rush_order_mt": 0.0,
            "extra_shift_hours": 0.0,
            "solver_time_limit_sec": solver_time_limit_sec,
        },
        {
            "name": down_label,
            "demand_spike": 0.0,
            "machine_down": machine_down,
            "down_hrs": down_hrs,
            "down_start_hr": down_start_hr,
            "yield_loss": 0.0,
            "rush_order_mt": 0.0,
            "extra_shift_hours": 0.0,
            "solver_time_limit_sec": solver_time_limit_sec,
        },
        {
            "name": f"{demand_label} + {down_label}",
            "demand_spike": demand_spike,
            "machine_down": machine_down,
            "down_hrs": down_hrs,
            "down_start_hr": down_start_hr,
            "yield_loss": 0.0,
            "rush_order_mt": 0.0,
            "extra_shift_hours": 0.0,
            "solver_time_limit_sec": solver_time_limit_sec,
        },
    ]

    if yield_loss > 0:
        scenarios.append(
            {
                "name": f"Yield Loss {yield_loss:g}%",
                "demand_spike": 0.0,
                "machine_down": None,
                "down_hrs": 0.0,
                "down_start_hr": 0.0,
                "yield_loss": yield_loss,
                "rush_order_mt": 0.0,
                "extra_shift_hours": 0.0,
                "solver_time_limit_sec": solver_time_limit_sec,
            }
        )
    if rush_order_mt > 0:
        scenarios.append(
            {
                "name": f"Rush Order +{rush_order_mt:g} MT",
                "demand_spike": 0.0,
                "machine_down": None,
                "down_hrs": 0.0,
                "down_start_hr": 0.0,
                "yield_loss": 0.0,
                "rush_order_mt": rush_order_mt,
                "extra_shift_hours": 0.0,
                "solver_time_limit_sec": solver_time_limit_sec,
            }
        )
    if extra_shift_hours > 0:
        scenarios.append(
            {
                "name": f"Extra Shift +{extra_shift_hours:g}h",
                "demand_spike": 0.0,
                "machine_down": None,
                "down_hrs": 0.0,
                "down_start_hr": 0.0,
                "yield_loss": 0.0,
                "rush_order_mt": 0.0,
                "extra_shift_hours": extra_shift_hours,
                "solver_time_limit_sec": solver_time_limit_sec,
            }
        )
    if yield_loss > 0 or rush_order_mt > 0 or extra_shift_hours > 0:
        scenarios.append(
            {
                "name": "Combined Stress",
                "demand_spike": demand_spike,
                "machine_down": machine_down,
                "down_hrs": down_hrs,
                "down_start_hr": down_start_hr,
                "yield_loss": yield_loss,
                "rush_order_mt": rush_order_mt,
                "extra_shift_hours": extra_shift_hours,
                "solver_time_limit_sec": solver_time_limit_sec,
            }
        )
    return scenarios


def run_scenario(data: dict, scenario: dict, *, planning_start=None, frozen_jobs: dict | None = None) -> dict:
    so = data["sales_orders"].copy()
    horizon_days = max(int(float(_scenario_value(data, "Planning Horizon (Days)", 14, float) or 14)), 1)
    planning_anchor = _deterministic_planning_start(
        planning_start,
        horizon_days,
        frozen_jobs=frozen_jobs,
        anchor_dates=so.get("Delivery_Date", []),
    )
    if scenario.get("demand_spike", 0) > 0:
        factor = 1 + float(scenario["demand_spike"]) / 100.0
        so["Order_Qty_MT"] = (pd.to_numeric(so["Order_Qty_MT"], errors="coerce").fillna(0) * factor).round(1)
        if "Order_Qty" in so.columns:
            so["Order_Qty"] = (pd.to_numeric(so["Order_Qty"], errors="coerce").fillna(0) * factor).round(1)
    if float(scenario.get("rush_order_mt", 0) or 0) > 0:
        so = _inject_rush_order(so, float(scenario["rush_order_mt"]), planning_start=planning_anchor)

    resources = data["resources"].copy()
    resources["Avail_Hours_Day"] = pd.to_numeric(resources["Avail_Hours_Day"], errors="coerce").fillna(20)
    extra_shift_hours = max(float(scenario.get("extra_shift_hours", 0) or 0), 0.0)
    if extra_shift_hours > 0:
        resources["Avail_Hours_Day"] = resources["Avail_Hours_Day"] + extra_shift_hours

    if scenario.get("machine_down"):
        mask = resources["Resource_ID"].astype(str).str.strip() == str(scenario["machine_down"]).strip()
        resources.loc[mask, "Avail_Hours_Day"] = (
            resources.loc[mask, "Avail_Hours_Day"] - float(scenario.get("down_hrs", 0) or 0) / horizon_days
        ).clip(lower=0)

    min_cmt = float(_scenario_value(data, "Min Campaign MT", 100.0, float) or 100.0)
    max_cmt = float(_scenario_value(data, "Max Campaign MT", 500.0, float) or 500.0)

    campaigns = build_campaigns(
        so,
        min_campaign_mt=min_cmt,
        max_campaign_mt=max_cmt,
        inventory=data.get("inventory"),
        bom=data.get("bom"),
        config=data.get("config"),
        skus=data.get("skus"),
        yield_loss_pct=float(scenario.get("yield_loss", 0.0) or 0.0),
    )
    frozen_campaign_ids = {
        campaign_id
        for campaign_id in (_campaign_id_from_job(job_id) for job_id in (frozen_jobs or {}))
        if campaign_id
    }
    releasable_campaigns = []
    for camp in campaigns:
        if camp.get("release_status") == "RELEASED" or camp.get("campaign_id") in frozen_campaign_ids:
            if camp.get("campaign_id") in frozen_campaign_ids and camp.get("release_status") != "RELEASED":
                camp = dict(camp)
                camp["release_status"] = "RUNNING LOCK"
                camp["material_status"] = "SHORTAGE"
            releasable_campaigns.append(camp)
    demand_hrs = compute_demand_hours(
        releasable_campaigns,
        resources,
        routing=data.get("routing"),
        changeover_matrix=data.get("changeover"),
        allow_defaults=False,
    )
    rough_cap = capacity_map(demand_hrs, resources, horizon_days=horizon_days)
    result = schedule(
        releasable_campaigns,
        resources,
        planning_start=planning_anchor,
        planning_horizon_days=horizon_days,
        machine_down_resource=scenario.get("machine_down"),
        machine_down_hours=float(scenario.get("down_hrs", 0) or 0.0),
        machine_down_start_hour=float(scenario.get("down_start_hr", 0) or 0.0),
        frozen_jobs=frozen_jobs,
        routing=data.get("routing"),
        queue_times=data.get("queue_times"),
        changeover_matrix=data.get("changeover"),
        config=data.get("config"),
        solver_time_limit_sec=float(scenario.get("solver_time_limit_sec", 30.0) or 30.0),
    )
    cap = capacity_map_from_schedule(result.get("heat_schedule"), resources, horizon_days=horizon_days)
    if cap.empty:
        cap = rough_cap

    campaign_df = result.get("campaign_schedule", pd.DataFrame()).copy()
    if not campaign_df.empty:
        campaign_df["Release_Status"] = campaign_df.get("Release_Status", "").fillna("").astype(str).str.upper()
        campaign_df["Status"] = campaign_df.get("Status", "").fillna("").astype(str).str.upper()
        campaign_df["RM_End"] = pd.to_datetime(campaign_df.get("RM_End"), errors="coerce")
        campaign_df["Due_Date"] = pd.to_datetime(campaign_df.get("Due_Date"), errors="coerce")
    released_campaigns = len(releasable_campaigns)
    held_campaigns = len(campaigns) - released_campaigns
    total_heats = int(sum(int(camp.get("heats", 0) or 0) for camp in releasable_campaigns))
    total_mt = float(sum(float(camp.get("total_coil_mt", 0) or 0.0) for camp in releasable_campaigns))
    on_time_pct = 100.0
    avg_margin = 0.0
    if not campaign_df.empty and released_campaigns:
        released_df = campaign_df[campaign_df["Release_Status"].isin(RELEASED_STATUSES)].copy()
        on_time_count = int((released_df["Status"] != "LATE").sum())
        on_time_pct = round((on_time_count / max(len(released_df), 1)) * 100.0, 1)
        if not released_df.empty:
            released_df["Margin_Hrs"] = (
                (released_df["Due_Date"] - released_df["RM_End"]).dt.total_seconds() / 3600.0
            ).fillna(0.0)
            avg_margin = round(float(released_df["Margin_Hrs"].mean()), 2)

    non_bf = cap[cap["Resource_ID"].astype(str) != "BF-01"].copy()
    bottleneck = "-"
    if not non_bf.empty:
        bottleneck = str(non_bf.sort_values(["Utilisation_%", "Overload_Hrs"], ascending=[False, False]).iloc[0]["Resource_ID"])

    return {
        "scenario": scenario["name"],
        "total_heats": total_heats,
        "campaigns": len(campaigns),
        "released_campaigns": released_campaigns,
        "held_campaigns": held_campaigns,
        "on_time_pct": on_time_pct,
        "weighted_lateness_hours": float(result.get("weighted_lateness_hours", 0.0) or 0.0),
        "bottleneck": bottleneck,
        "throughput_mt_day": round(total_mt / max(horizon_days, 1), 2),
        "avg_margin_hrs": avg_margin,
        "overloaded": cap[cap["Status"] == "OVERLOADED"]["Resource_ID"].tolist(),
        "utilisation": cap[["Resource_ID", "Utilisation_%"]].set_index("Resource_ID")["Utilisation_%"].to_dict(),
        "solver_status": result["solver_status"],
    }
