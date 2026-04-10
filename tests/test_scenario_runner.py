"""Regression tests for what-if scenario execution."""
from datetime import datetime

import pandas as pd

from scenarios import scenario_runner


def _base_data():
    return {
        "sales_orders": pd.DataFrame(
            [
                {
                    "SO_ID": "SO-001",
                    "SKU_ID": "FG-001",
                    "Order_Qty_MT": 50.0,
                    "Order_Qty": 50.0,
                    "Order_Date": datetime(2026, 4, 1, 9, 30),
                    "Delivery_Date": datetime(2026, 4, 10, 0, 0),
                    "Priority": "NORMAL",
                    "Status": "Open",
                    "Campaign_Group": "CMP-A",
                }
            ]
        ),
        "resources": pd.DataFrame(
            [
                {
                    "Resource_ID": "RM-01",
                    "Resource_Name": "RM 1",
                    "Plant": "RM",
                    "Avail_Hours_Day": 20,
                }
            ]
        ),
        "inventory": pd.DataFrame(),
        "bom": pd.DataFrame(),
        "config": {},
        "skus": pd.DataFrame(),
        "routing": pd.DataFrame(),
        "queue_times": {},
        "changeover": pd.DataFrame(),
        "scenarios": pd.DataFrame(
            {"Value": [14.0]},
            index=pd.Index(["Planning Horizon (Days)"], name="Parameter"),
        ),
    }


def _baseline_scenario():
    return {
        "name": "Baseline",
        "demand_spike": 0.0,
        "machine_down": None,
        "down_hrs": 0.0,
        "down_start_hr": 0.0,
        "yield_loss": 0.0,
        "rush_order_mt": 0.0,
        "extra_shift_hours": 0.0,
        "solver_time_limit_sec": 30.0,
    }


def test_run_scenario_passes_deterministic_planning_start(monkeypatch):
    data = _base_data()
    captured = {}

    monkeypatch.setattr(
        scenario_runner,
        "build_campaigns",
        lambda *args, **kwargs: [
            {
                "campaign_id": "CAMP-001",
                "release_status": "RELEASED",
                "heats": 1,
                "total_coil_mt": 50.0,
            }
        ],
    )
    monkeypatch.setattr(scenario_runner, "compute_demand_hours", lambda *args, **kwargs: pd.DataFrame())
    monkeypatch.setattr(
        scenario_runner,
        "capacity_map",
        lambda *args, **kwargs: pd.DataFrame(
            [{"Resource_ID": "RM-01", "Utilisation_%": 50.0, "Overload_Hrs": 0.0, "Status": "OK"}]
        ),
    )

    def _fake_schedule(campaigns, resources, **kwargs):
        captured["campaigns"] = campaigns
        captured["kwargs"] = kwargs
        return {
            "campaign_schedule": pd.DataFrame(),
            "weighted_lateness_hours": 0.0,
            "solver_status": "OPTIMAL",
        }

    monkeypatch.setattr(scenario_runner, "schedule", _fake_schedule)

    scenario_runner.run_scenario(data, _baseline_scenario())

    assert captured["kwargs"]["planning_start"] == datetime(2026, 3, 27, 0, 0)
    assert captured["kwargs"]["frozen_jobs"] is None


def test_run_scenario_promotes_frozen_campaigns_to_running_lock(monkeypatch):
    data = _base_data()
    captured = {}

    monkeypatch.setattr(
        scenario_runner,
        "build_campaigns",
        lambda *args, **kwargs: [
            {
                "campaign_id": "CAMP-001",
                "release_status": "MATERIAL HOLD",
                "heats": 1,
                "total_coil_mt": 50.0,
            }
        ],
    )
    monkeypatch.setattr(scenario_runner, "compute_demand_hours", lambda *args, **kwargs: pd.DataFrame())
    monkeypatch.setattr(
        scenario_runner,
        "capacity_map",
        lambda *args, **kwargs: pd.DataFrame(
            [{"Resource_ID": "RM-01", "Utilisation_%": 50.0, "Overload_Hrs": 0.0, "Status": "OK"}]
        ),
    )

    def _fake_schedule(campaigns, resources, **kwargs):
        captured["campaigns"] = campaigns
        captured["kwargs"] = kwargs
        return {
            "campaign_schedule": pd.DataFrame(),
            "weighted_lateness_hours": 0.0,
            "solver_status": "OPTIMAL",
        }

    monkeypatch.setattr(scenario_runner, "schedule", _fake_schedule)

    frozen_jobs = {
        "CAMP-001-PO01": {
            "Resource_ID": "RM-01",
            "Planned_Start": datetime(2026, 4, 1, 8, 0),
            "Planned_End": datetime(2026, 4, 1, 10, 0),
            "Status": "RUNNING",
        }
    }

    scenario_runner.run_scenario(data, _baseline_scenario(), frozen_jobs=frozen_jobs)

    assert len(captured["campaigns"]) == 1
    assert captured["campaigns"][0]["release_status"] == "RUNNING LOCK"
    assert captured["kwargs"]["frozen_jobs"] == frozen_jobs


def test_inject_rush_order_uses_planning_anchor():
    so = _base_data()["sales_orders"]

    out = scenario_runner._inject_rush_order(so, 25.0, planning_start=datetime(2026, 4, 2, 8, 45))

    rush = out.iloc[-1]
    assert rush["SO_ID"] == "RUSH-202604020800"
    assert pd.Timestamp(rush["Order_Date"]) == pd.Timestamp(datetime(2026, 4, 2, 8, 0))
    assert pd.Timestamp(rush["Delivery_Date"]) == pd.Timestamp(datetime(2026, 4, 3, 8, 0))
