"""Regression tests for BOM and capacity engine consistency."""
import sys
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from engine.bom_explosion import (
    OFFICIAL_BOM_EXPLOSION_API,
    consolidate_demand,
    explode_bom,
    explode_bom_details,
    inventory_map,
    net_requirements,
    simulate_material_commit,
)
from engine.capacity import (
    FINITE_SCHEDULE_CAPACITY_BASIS,
    ROUGH_CUT_CAPACITY_BASIS,
    capacity_map,
    capacity_map_from_schedule,
    compute_demand_hours,
)


FIXED_START = datetime(2026, 4, 1, 8, 0)


def _make_resources(include_bf: bool = False, single_rm: bool = False):
    rows = [
        {"Resource_ID": "EAF-01", "Resource_Name": "EAF 1", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "EAF"},
        {"Resource_ID": "EAF-02", "Resource_Name": "EAF 2", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "EAF"},
        {"Resource_ID": "LRF-01", "Resource_Name": "LRF 1", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "LRF"},
        {"Resource_ID": "LRF-02", "Resource_Name": "LRF 2", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "LRF"},
        {"Resource_ID": "VD-01", "Resource_Name": "VD 1", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "VD"},
        {"Resource_ID": "CCM-01", "Resource_Name": "CCM 1", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "CCM"},
        {"Resource_ID": "CCM-02", "Resource_Name": "CCM 2", "Plant": "SMS", "Avail_Hours_Day": 20, "Operation_Group": "CCM"},
        {"Resource_ID": "RM-01", "Resource_Name": "RM 1", "Plant": "RM", "Avail_Hours_Day": 20, "Operation_Group": "RM"},
    ]
    if not single_rm:
        rows.append({"Resource_ID": "RM-02", "Resource_Name": "RM 2", "Plant": "RM", "Avail_Hours_Day": 20, "Operation_Group": "RM"})
    if include_bf:
        rows.append({"Resource_ID": "BF-01", "Resource_Name": "BF 1", "Plant": "Melt", "Avail_Hours_Day": 24, "Operation_Group": "BF"})
    df = pd.DataFrame(rows)
    df["Status"] = "Active"
    df["Type"] = "Machine"
    df["Default_Cycle_Min"] = 0
    df["Default_Setup_Min"] = 0
    return df


def _make_campaign(grade: str, heats: int, qty_mt: float, campaign_id: str) -> dict:
    return {
        "campaign_id": campaign_id,
        "campaign_group": grade,
        "grade": grade,
        "heats": heats,
        "billet_family": "BIL-130" if grade == "SAE 1008" else "BIL-150",
        "needs_vd": False,
        "due_date": FIXED_START + timedelta(days=7),
        "priority_rank": 3,
        "priority": "NORMAL",
        "total_coil_mt": qty_mt,
        "production_orders": [
            {
                "production_order_id": f"{campaign_id}-PO01",
                "sku_id": "WRC-1008-5.5",
                "so_id": f"SO-{campaign_id}",
                "qty_mt": qty_mt,
                "section_mm": 5.5,
                "due_date": FIXED_START + timedelta(days=7),
                "priority_rank": 3,
            }
        ],
        "so_ids": [f"SO-{campaign_id}"],
    }


class TestBomExplosion:
    def test_explode_bom_synthesizes_level_and_preserves_parent_traceability(self):
        demand = pd.DataFrame(
            {
                "SKU_ID": ["FG-A", "FG-B"],
                "Required_Qty": [10.0, 5.0],
            }
        )
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A", "FG-B"],
                "Child_SKU": ["INT-X", "INT-X"],
                "Qty_Per": [1.0, 2.0],
                "Flow_Type": ["INPUT", "INPUT"],
                "Yield_Pct": [100.0, 100.0],
            }
        )

        exploded = explode_bom(demand, bom)
        assert "Parent_SKU" in exploded.columns
        level_one = exploded[(exploded["SKU_ID"] == "INT-X") & (exploded["BOM_Level"] == 1)]
        assert set(level_one["Parent_SKU"]) == {"FG-A", "FG-B"}

    def test_explode_bom_detects_cycles(self):
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["A", "B"],
                "Child_SKU": ["B", "A"],
                "Qty_Per": [1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT"],
            }
        )

        with pytest.raises(ValueError, match="BOM cycle detected"):
            explode_bom(demand, bom)

    def test_explode_bom_can_record_structure_errors(self):
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["A", "B"],
                "Child_SKU": ["B", "A"],
                "Qty_Per": [1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT"],
            }
        )

        exploded = explode_bom(demand, bom, on_structure_error="record")
        assert exploded.attrs["structure_errors"]
        assert exploded.attrs["structure_errors"][0]["type"] == "BOM_CYCLE"
        assert exploded.attrs["feasible"] is False

    def test_explode_bom_marks_official_detail_api_in_attrs(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A"],
                "Child_SKU": ["RM-01"],
                "Qty_Per": [1.0],
                "Flow_Type": ["INPUT"],
            }
        )

        exploded = explode_bom(demand, bom)
        assert exploded.attrs["official_api"] == OFFICIAL_BOM_EXPLOSION_API

    def test_explode_bom_details_is_full_audit_contract(self):
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["A", "B"],
                "Child_SKU": ["B", "A"],
                "Qty_Per": [1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT"],
            }
        )

        details = explode_bom_details(demand, bom, on_structure_error="record")
        assert set(details) == {"exploded", "structure_errors", "feasible"}
        assert details["structure_errors"][0]["type"] == "BOM_CYCLE"

    def test_explode_bom_includes_byproducts(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [10.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A", "FG-A"],
                "Child_SKU": ["RM-01", "SCRAP-01"],
                "Qty_Per": [1.0, 0.1],
                "Flow_Type": ["INPUT", "BYPRODUCT"],
                "Yield_Pct": [100.0, pd.NA],
            }
        )

        exploded = explode_bom(demand, bom)
        scrap_row = exploded[(exploded["SKU_ID"] == "SCRAP-01") & (exploded["Flow_Type"] == "BYPRODUCT")].iloc[0]
        assert scrap_row["Produced_Qty"] == 1.0
        assert scrap_row["Required_Qty"] == 0.0

    def test_simulate_material_commit_raises_on_structure_errors_by_default(self):
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [5.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["A", "B"],
                "Child_SKU": ["B", "A"],
                "Qty_Per": [1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT"],
            }
        )

        with pytest.raises(ValueError, match="BOM cycle detected"):
            simulate_material_commit(demand, bom, inventory={})

    def test_simulate_material_commit_can_record_structure_errors(self):
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [5.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["A", "B"],
                "Child_SKU": ["B", "A"],
                "Qty_Per": [1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT"],
            }
        )

        result = simulate_material_commit(demand, bom, inventory={}, on_structure_error="record")
        assert result["shortages"] == {}
        assert result["structure_errors"]
        assert result["structure_errors"][0]["type"] == "BOM_CYCLE"
        assert result["feasible"] is False

    def test_simulate_material_commit_default_depth_matches_explosion_depth(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A", "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "L9"],
                "Child_SKU": ["L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "L9", "RM-01"],
                "Qty_Per": [1.0] * 10,
                "Flow_Type": ["INPUT"] * 10,
            }
        )

        result = simulate_material_commit(demand, bom, inventory={"RM-01": 1.0})
        assert result["structure_errors"] == []
        assert result["shortages"] == {}
        assert result["feasible"] is True

    def test_simulate_material_commit_defers_byproducts_by_default(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [10.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A", "FG-A"],
                "Child_SKU": ["RM-01", "SCRAP-01"],
                "Qty_Per": [1.0, 0.1],
                "Flow_Type": ["INPUT", "BYPRODUCT"],
                "Yield_Pct": [100.0, pd.NA],
            }
        )

        result = simulate_material_commit(
            demand,
            bom,
            inventory={"RM-01": 10.0, " SCRAP-01 ": 0.5},
            on_structure_error="record",
        )
        assert result["byproducts_produced"]["SCRAP-01"] == 1.0
        assert result["inventory_after"]["SCRAP-01"] == 0.5
        assert result["byproduct_inventory_mode"] == "deferred"
        assert result["feasible"] is True

    def test_simulate_material_commit_can_make_byproducts_immediately_available(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [10.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A", "FG-A"],
                "Child_SKU": ["RM-01", "SCRAP-01"],
                "Qty_Per": [1.0, 0.1],
                "Flow_Type": ["INPUT", "BYPRODUCT"],
                "Yield_Pct": [100.0, pd.NA],
            }
        )

        result = simulate_material_commit(
            demand,
            bom,
            inventory={"RM-01": 10.0, " SCRAP-01 ": 0.5},
            on_structure_error="record",
            byproduct_inventory_mode="IMMEDIATE",
        )
        assert result["byproducts_produced"]["SCRAP-01"] == 1.0
        assert result["inventory_after"]["SCRAP-01"] == 1.5
        assert result["byproduct_inventory_mode"] == "immediate"

    def test_net_requirements_consumes_inventory_once_across_rows(self):
        gross = pd.DataFrame(
            {
                "SKU_ID": ["RM-01", "RM-01"],
                "Required_Qty": [4.0, 4.0],
                "BOM_Level": [1, 2],
                "Parent_SKU": ["A", "B"],
            }
        )
        inventory = pd.DataFrame({"SKU_ID": ["RM-01"], "Available_Qty": [5.0]})

        netted = net_requirements(gross, inventory)
        assert list(netted["Available"]) == [5.0, 1.0]
        assert list(netted["Net_Req"]) == [0.0, 3.0]

    def test_inventory_map_normalizes_dataframe_sku_ids(self):
        inventory = pd.DataFrame(
            {
                "SKU_ID": [" RM-01 ", "RM-01", "   ", None],
                "Available_Qty": [2.0, 3.0, 99.0, 99.0],
            }
        )

        result = inventory_map(inventory)
        assert result == {"RM-01": 5.0}

    def test_net_requirements_preserves_byproduct_rows(self):
        gross = pd.DataFrame(
            {
                "SKU_ID": ["SCRAP-01", "RM-01"],
                "Required_Qty": [0.0, 4.0],
                "Produced_Qty": [2.0, 0.0],
                "BOM_Level": [1, 1],
                "Parent_SKU": ["FG-A", "FG-A"],
                "Flow_Type": ["BYPRODUCT", "INPUT"],
            }
        )
        inventory = pd.DataFrame({"SKU_ID": ["SCRAP-01", "RM-01"], "Available_Qty": [1.0, 1.0]})

        netted = net_requirements(gross, inventory)
        scrap_row = netted[netted["SKU_ID"] == "SCRAP-01"].iloc[0]
        assert scrap_row["Produced_Qty"] == 2.0
        assert scrap_row["Net_Req"] == 0.0
        assert scrap_row["Available"] == 1.0

    def test_net_requirements_only_credits_byproducts_in_immediate_mode(self):
        gross = pd.DataFrame(
            {
                "SKU_ID": ["SCRAP-01", "SCRAP-01"],
                "Required_Qty": [0.0, 2.0],
                "Produced_Qty": [2.0, 0.0],
                "BOM_Level": [1, 1],
                "Parent_SKU": ["FG-A", "FG-B"],
                "Flow_Type": ["BYPRODUCT", "INPUT"],
            }
        )
        inventory = pd.DataFrame({"SKU_ID": ["SCRAP-01"], "Available_Qty": [0.0]})

        deferred = net_requirements(gross, inventory)
        immediate = net_requirements(gross, inventory, byproduct_inventory_mode="IMMEDIATE")

        assert list(deferred["Net_Req"]) == [0.0, 2.0]
        assert list(immediate["Net_Req"]) == [0.0, 0.0]
        assert deferred.attrs["byproduct_inventory_mode"] == "deferred"
        assert immediate.attrs["byproduct_inventory_mode"] == "immediate"

    def test_consolidate_demand_normalizes_open_status(self):
        sales_orders = pd.DataFrame(
            {
                "SO_ID": ["SO-1", "SO-2", "SO-3"],
                "SKU_ID": [" FG-A ", "FG-A", "FG-A"],
                "Order_Qty_MT": [3.0, 2.0, 9.0],
                "Delivery_Date": [FIXED_START] * 3,
                "Status": [" open ", "OPEN", "Closed"],
            }
        )

        consolidated = consolidate_demand(sales_orders)
        assert consolidated.iloc[0]["Total_Qty"] == 5.0
        assert consolidated.iloc[0]["Order_Count"] == 2

    def test_explode_bom_rejects_unknown_structure_error_mode(self):
        demand = pd.DataFrame({"SKU_ID": ["FG-A"], "Required_Qty": [1.0]})
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["FG-A"],
                "Child_SKU": ["RM-01"],
                "Qty_Per": [1.0],
                "Flow_Type": ["INPUT"],
            }
        )

        with pytest.raises(ValueError, match="on_structure_error"):
            explode_bom(demand, bom, on_structure_error="ignore")


class TestCapacityAlignment:
    def test_compute_demand_hours_allocates_load_across_real_machines(self):
        campaigns = [_make_campaign("SAE 1008", heats=4, qty_mt=50.0, campaign_id="CMP-001")]
        demand = compute_demand_hours(campaigns, _make_resources(), allow_defaults=True)

        eaf_rows = demand[demand["Resource_ID"].isin(["EAF-01", "EAF-02"])]
        assert len(eaf_rows[eaf_rows["Demand_Hrs"] > 0]) == 2

    def test_compute_demand_hours_tracks_rm_changeover_separately(self):
        campaigns = [
            _make_campaign("SAE 1008", heats=1, qty_mt=50.0, campaign_id="CMP-001"),
            _make_campaign("Cr-Mo 4140", heats=1, qty_mt=50.0, campaign_id="CMP-002"),
        ]
        changeover = pd.DataFrame(
            {
                "Grade": ["SAE 1008", "Cr-Mo 4140"],
                "SAE 1008": [0, 20],
                "Cr-Mo 4140": [30, 0],
            }
        ).set_index("Grade")

        demand = compute_demand_hours(
            campaigns,
            _make_resources(single_rm=True),
            changeover_matrix=changeover,
            allow_defaults=True,
        )
        rm_row = demand[demand["Resource_ID"] == "RM-01"].iloc[0]
        assert rm_row["Changeover_Hrs"] > 0
        assert rm_row["Demand_Hrs"] == round(
            rm_row["Process_Hrs"] + rm_row["Setup_Hrs"] + rm_row["Changeover_Hrs"],
            2,
        )

    def test_capacity_map_uses_direct_machine_load_not_family_average(self):
        resources = _make_resources()
        demand = pd.DataFrame(
            {
                "Resource_ID": ["EAF-01"],
                "Demand_Hrs": [8.0],
                "Process_Hrs": [8.0],
                "Setup_Hrs": [0.0],
                "Changeover_Hrs": [0.0],
                "Task_Count": [2],
            }
        )

        cap = capacity_map(demand, resources, horizon_days=14)
        eaf_01 = cap[cap["Resource_ID"] == "EAF-01"].iloc[0]
        eaf_02 = cap[cap["Resource_ID"] == "EAF-02"].iloc[0]
        assert eaf_01["Demand_Hrs"] == 8.0
        assert eaf_02["Demand_Hrs"] == 0.0

    def test_capacity_map_no_longer_hardcodes_bf_status(self):
        cap = capacity_map(pd.DataFrame(columns=["Resource_ID", "Demand_Hrs"]), _make_resources(include_bf=True), horizon_days=14)
        bf_row = cap[cap["Resource_ID"] == "BF-01"].iloc[0]
        assert bf_row["Status"] != "CONTINUOUS"

    def test_capacity_map_validates_resource_schema(self):
        bad_resources = pd.DataFrame({"Resource_ID": ["EAF-01"], "Avail_Hours_Day": [20]})
        with pytest.raises(ValueError, match="missing required columns"):
            capacity_map(pd.DataFrame(columns=["Resource_ID", "Demand_Hrs"]), bad_resources, horizon_days=14)

    def test_capacity_map_marks_rough_cut_basis(self):
        cap = capacity_map(pd.DataFrame(columns=["Resource_ID", "Demand_Hrs"]), _make_resources(), horizon_days=14)
        assert set(cap["Capacity_Basis"]) == {ROUGH_CUT_CAPACITY_BASIS}

    def test_compute_demand_hours_can_fail_fast_without_demo_defaults(self):
        with pytest.raises(ValueError, match="Missing SMS routing"):
            compute_demand_hours(
                [_make_campaign("SAE 1008", heats=1, qty_mt=50.0, campaign_id="CMP-001")],
                _make_resources(),
                routing=pd.DataFrame(),
            )

    def test_capacity_map_from_schedule_uses_actual_scheduled_hours(self):
        schedule_df = pd.DataFrame(
            [
                {
                    "Job_ID": "CMP-001-H1-EAF",
                    "Resource_ID": "EAF-01",
                    "Planned_Start": FIXED_START,
                    "Planned_End": FIXED_START + timedelta(hours=2),
                },
                {
                    "Job_ID": "CMP-001-PO01-RM",
                    "Resource_ID": "RM-01",
                    "Planned_Start": FIXED_START + timedelta(hours=3),
                    "Planned_End": FIXED_START + timedelta(hours=5, minutes=30),
                },
            ]
        )

        cap = capacity_map_from_schedule(schedule_df, _make_resources(), horizon_days=14)
        eaf_row = cap[cap["Resource_ID"] == "EAF-01"].iloc[0]
        rm_row = cap[cap["Resource_ID"] == "RM-01"].iloc[0]

        assert eaf_row["Demand_Hrs"] == 2.0
        assert rm_row["Demand_Hrs"] == 2.5
        assert set(cap["Capacity_Basis"]) == {FINITE_SCHEDULE_CAPACITY_BASIS}
