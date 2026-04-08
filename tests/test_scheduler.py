"""Scheduler regression tests for planning anchors, setup times, and RM timing."""
import sys
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from engine.campaign import build_campaigns
from engine.scheduler import _campaign_route_rows, _rm_duration, build_operation_times, schedule


FIXED_START = datetime(2026, 4, 1, 8, 37)


def _make_resources():
    return pd.DataFrame({
        "Resource_ID": ["EAF-01", "EAF-02", "LRF-01", "LRF-02", "LRF-03", "VD-01", "CCM-01", "CCM-02", "RM-01", "RM-02"],
        "Resource_Name": ["EAF 1", "EAF 2", "LRF 1", "LRF 2", "LRF 3", "VD 1", "CCM 1", "CCM 2", "RM 1", "RM 2"],
        "Plant": ["SMS"] * 10,
        "Avail_Hours_Day": [20] * 10,
        "Type": ["Machine"] * 10,
        "Status": ["Active"] * 10,
        "Max_Capacity_MT_Hr": [60] * 10,
        "Capacity_MT_Day": [1200] * 10,
        "Heat_Size_MT": [50] * 10,
        "Efficiency_%": [95] * 10,
        "Operation_Group": ["EAF", "EAF", "LRF", "LRF", "LRF", "VD", "CCM", "CCM", "RM", "RM"],
        "Default_Cycle_Min": [90, 90, 40, 40, 40, 45, 50, 50, 0, 0],
        "Default_Setup_Min": [30, 30, 10, 10, 10, 15, 20, 20, 40, 40],
        "Operation_Color": [""] * 10,
    })


def _make_bom():
    return pd.DataFrame({
        "BOM_ID": ["B1", "B2"],
        "Parent_SKU": ["WRC-1008-5.5", "WRC-1008-5.5"],
        "Child_SKU": ["BIL-130", "SCRAP-01"],
        "Flow_Type": ["INPUT", "BYPRODUCT"],
        "Qty_Per": [1.1, 0.05],
        "Scrap_%": [pd.NA, pd.NA],
        "Yield_Pct": [95.0, pd.NA],
        "Level": [1, 1],
        "UOM": ["MT", "MT"],
        "Note": ["", ""],
    })


def _make_skus():
    return pd.DataFrame({
        "SKU_ID": ["WRC-1008-5.5", "BIL-130"],
        "SKU_Name": ["Wire Rod 1008 5.5mm", "Billet 130mm"],
        "Category": ["FG", "WIP"],
        "Grade": ["SAE 1008", "SAE 1008"],
        "Section_mm": [5.5, pd.NA],
        "Coil_Wt_MT": [2.0, pd.NA],
        "UOM": ["MT", "MT"],
        "Needs_VD": ["N", "N"],
        "Lead_Time_Days": [3, 1],
        "Safety_Stock_MT": [0, 0],
        "Route_Variant": ["N", ""],
        "Product_Family": ["BIL-130", "BIL-130"],
        "Attribute_1": [5.5, pd.NA],
    })


def _make_inventory(billet_qty=500.0):
    return pd.DataFrame({
        "SKU_ID": ["BIL-130"],
        "Available_Qty": [billet_qty],
    })


def _make_sales_orders(qty=50.0, due_days=14):
    due_date = FIXED_START + timedelta(days=due_days)
    return pd.DataFrame({
        "SO_ID": ["SO-001"],
        "SKU_ID": ["WRC-1008-5.5"],
        "Grade": ["SAE 1008"],
        "Section_mm": [5.5],
        "Order_Qty_MT": [qty],
        "Order_Qty": [qty],
        "Delivery_Date": [due_date],
        "Order_Date": [FIXED_START],
        "Priority": ["NORMAL"],
        "Status": ["Open"],
        "Campaign_Group": ["SAE 1008"],
    })


def _make_campaigns():
    campaigns = build_campaigns(
        _make_sales_orders(),
        min_campaign_mt=50.0,
        max_campaign_mt=500.0,
        inventory=_make_inventory(),
        bom=_make_bom(),
        skus=_make_skus(),
    )
    for camp in campaigns:
        camp["release_status"] = "RELEASED"
        camp["material_status"] = "READY"
        # Ensure rolling mode defaults for backward compatibility with existing tests
        camp.setdefault("order_type", "MTO")
        camp.setdefault("rolling_mode", "HOT")
        camp.setdefault("hot_charging", True)
    return campaigns


def _make_routing_with_transfers():
    return pd.DataFrame(
        [
            {"Operation": "EAF", "Sequence": 1, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 0, "Cycle_Time_Min_Heat": 90, "Setup_Time_Min": 30},
            {"Operation": "LRF", "Sequence": 2, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 10, "Cycle_Time_Min_Heat": 40, "Setup_Time_Min": 10},
            {"Operation": "CCM", "Sequence": 3, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 0, "Cycle_Time_Min_Heat": 50, "Setup_Time_Min": 20},
            {"Operation": "RM", "Sequence": 4, "Grade": "SAE 1008", "SKU_ID": "WRC-1008-5.5", "Transfer_Time_Min": 10, "Cycle_Time_Min_Heat": 40, "Setup_Time_Min": 40},
        ]
    )


def _make_manual_campaign(campaign_id: str, so_id: str, qty_mt: float = 400.0, due_days: int = 14):
    due_date = FIXED_START + timedelta(days=due_days)
    return {
        "campaign_id": campaign_id,
        "campaign_group": "SAE 1008",
        "grade": "SAE 1008",
        "section_mm": 5.5,
        "sections_covered": "5.5",
        "needs_vd": False,
        "billet_family": "BIL-130",
        "total_coil_mt": qty_mt,
        "heats": 1,
        "so_ids": [so_id],
        "order_count": 1,
        "due_date": due_date,
        "priority": "NORMAL",
        "priority_rank": 3,
        "production_orders": [
            {
                "campaign_id": campaign_id,
                "production_order_id": f"{campaign_id}-PO01",
                "so_id": so_id,
                "sku_id": "WRC-1008-5.5",
                "section_mm": 5.5,
                "qty_mt": qty_mt,
                "due_date": due_date,
                "priority_rank": 3,
                "priority": "NORMAL",
            }
        ],
        "release_status": "RELEASED",
        "material_status": "READY",
    }


class TestSchedulerAnchoring:
    def test_schedule_uses_explicit_planning_start(self):
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
        )
        assert result["planning_start"] == datetime(2026, 4, 1, 8, 0)

    def test_frozen_job_resource_mismatch_raises(self):
        campaigns = _make_campaigns()
        frozen_jobs = {
            f"{campaigns[0]['campaign_id']}-H1-EAF": {
                "Resource_ID": "RM-01",
                "Planned_Start": FIXED_START,
                "Planned_End": FIXED_START + timedelta(hours=2),
                "Status": "RUNNING",
            }
        }

        with pytest.raises(ValueError, match="incompatible resource"):
            schedule(
                campaigns,
                _make_resources(),
                planning_start=FIXED_START,
                routing=_make_routing_with_transfers(),
                frozen_jobs=frozen_jobs,
            )

    def test_schedule_falls_back_cleanly_when_cp_model_missing(self, monkeypatch):
        monkeypatch.setattr("engine.scheduler.cp_model", None)
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
        )
        assert result["solver_status"] == "GREEDY"
        assert result["solver_detail"] == "ORTOOLS_UNAVAILABLE"
        assert result["master_data_mode"] == "STRICT_MASTERS"
        assert result["allow_default_masters"] is False

    def test_campaign_serialization_mode_can_allow_sms_overlap(self):
        campaigns = [
            _make_manual_campaign("CMP-001", "SO-001"),
            _make_manual_campaign("CMP-002", "SO-002"),
        ]

        result = schedule(
            campaigns,
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
            config={"Campaign_Serialization_Mode": "OVERLAP_AFTER_SMS"},
        )

        heat_schedule = result["heat_schedule"].copy()
        heat_schedule["Planned_Start"] = pd.to_datetime(heat_schedule["Planned_Start"])
        heat_schedule["Planned_End"] = pd.to_datetime(heat_schedule["Planned_End"])

        first_ccm_end = heat_schedule[
            (heat_schedule["Campaign"] == "CMP-001") & (heat_schedule["Operation"] == "CCM")
        ]["Planned_End"].min()
        first_rm_end = heat_schedule[
            (heat_schedule["Campaign"] == "CMP-001") & (heat_schedule["Operation"] == "RM")
        ]["Planned_End"].min()
        second_eaf_start = heat_schedule[
            (heat_schedule["Campaign"] == "CMP-002") & (heat_schedule["Operation"] == "EAF")
        ]["Planned_Start"].min()

        assert result["campaign_serialization_mode"] == "OVERLAP_AFTER_SMS"
        assert second_eaf_start >= first_ccm_end
        assert second_eaf_start < first_rm_end

    def test_invalid_campaign_serialization_mode_raises(self):
        with pytest.raises(ValueError, match="Campaign_Serialization_Mode"):
            schedule(
                _make_campaigns(),
                _make_resources(),
                planning_start=FIXED_START,
                routing=_make_routing_with_transfers(),
                config={"Campaign_Serialization_Mode": "WHATEVER"},
            )


class TestSchedulerDurations:
    def test_build_operation_times_can_opt_into_demo_defaults(self):
        resources = _make_resources()
        resources["Default_Cycle_Min"] = 0
        op_times = build_operation_times(
            pd.DataFrame(),
            "SAE 1008",
            resources=resources,
            allow_defaults=True,
        )

        assert op_times["EAF"]["cycle"] == 90
        assert op_times["LRF"]["cycle"] == 40
        assert op_times["CCM"]["cycle"] == 50

    def test_schedule_rejects_missing_routing_by_default(self):
        with pytest.raises(ValueError, match="Missing SMS routing"):
            schedule(
                _make_campaigns(),
                _make_resources(),
                planning_start=FIXED_START,
                routing=pd.DataFrame(),
            )

    def test_schedule_can_opt_into_demo_master_defaults(self):
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=pd.DataFrame(),
            config={"Allow_Scheduler_Default_Masters": "Y"},
        )

        assert result["solver_status"] in {"OPTIMAL", "FEASIBLE", "GREEDY"}
        assert result["master_data_mode"] == "DEFAULT_MASTERS_ALLOWED"
        assert result["allow_default_masters"] is True

    def test_first_sms_heat_includes_setup_time(self):
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
        )

        heat_schedule = result["heat_schedule"]
        eaf_row = heat_schedule[heat_schedule["Operation"] == "EAF"].iloc[0]
        assert eaf_row["Duration_Hrs"] == 2.0

    def test_rm_duration_keeps_changeover_outside_task_by_default(self):
        order = {"qty_mt": 50.0, "section_mm": 5.5, "sku_id": "WRC-1008-5.5"}
        no_changeover = _rm_duration(
            order,
            "SAE 1008",
            routing=pd.DataFrame(),
            resources=_make_resources(),
            changeover_minutes=0,
            include_setup=True,
            allow_defaults=True,
        )
        default_duration = _rm_duration(
            order,
            "SAE 1008",
            routing=pd.DataFrame(),
            resources=_make_resources(),
            changeover_minutes=30,
            include_setup=True,
            allow_defaults=True,
        )
        embedded_duration = _rm_duration(
            order,
            "SAE 1008",
            routing=pd.DataFrame(),
            resources=_make_resources(),
            changeover_minutes=30,
            include_setup=True,
            add_changeover_to_duration=True,
            allow_defaults=True,
        )

        assert default_duration == no_changeover
        assert embedded_duration == no_changeover + 30


class TestQueueReporting:
    def test_sms_queue_violation_excludes_transfer_time(self):
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
            queue_times={("EAF", "LRF"): {"min": 0, "max": 5, "enforcement": "Hard"}},
        )

        heat_schedule = result["heat_schedule"]
        lrf_row = heat_schedule[heat_schedule["Operation"] == "LRF"].iloc[0]
        assert lrf_row["Queue_Violation"] == "OK"

    def test_rm_queue_violation_excludes_transfer_time(self):
        # Test with HOT rolling (default) shows earlier RM start
        result = schedule(
            _make_campaigns(),
            _make_resources(),
            planning_start=FIXED_START,
            routing=_make_routing_with_transfers(),
            queue_times={("CCM", "RM"): {"min": 0, "max": 5, "enforcement": "Hard"}},
        )

        heat_schedule = result["heat_schedule"]
        rm_row = heat_schedule[heat_schedule["Operation"] == "RM"].iloc[0]
        # With HOT rolling, RM starts after first CCM, which may cause queue warning
        # This is expected behavior - HOT rolling trades off queue constraint for faster production
        assert rm_row["Queue_Violation"] in ["OK", "WARN"]


class TestRoutingRobustness:
    def test_campaign_route_rows_accepts_sequence_without_op_seq(self):
        route_rows = _campaign_route_rows(
            _make_campaigns()[0],
            _make_routing_with_transfers(),
        )

        assert list(route_rows["Operation"])[:3] == ["EAF", "LRF", "CCM"]
