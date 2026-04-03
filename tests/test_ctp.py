"""Tests for CTP engine — committed-capacity filtering, campaign join, and material hold."""
import sys
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from engine.campaign import build_campaigns
from engine.ctp import (
    COMMITTED_STATUSES,
    _ghost_sales_order,
    _join_candidate,
    _merge_into_campaign,
    _net_inventory_after_committed,
    capable_to_promise,
)


# ── Fixtures ─────────────────────────────────────────────────────────────────

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


def _make_inventory(billet_qty=200.0):
    return pd.DataFrame({
        "SKU_ID": ["BIL-130"],
        "Available_Qty": [billet_qty],
    })


def _make_routing():
    return pd.DataFrame(
        [
            {"Operation": "EAF", "Sequence": 1, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 0, "Cycle_Time_Min_Heat": 90, "Setup_Time_Min": 30},
            {"Operation": "LRF", "Sequence": 2, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 10, "Cycle_Time_Min_Heat": 40, "Setup_Time_Min": 10},
            {"Operation": "CCM", "Sequence": 3, "Grade": "SAE 1008", "SKU_ID": "", "Transfer_Time_Min": 0, "Cycle_Time_Min_Heat": 50, "Setup_Time_Min": 20},
            {"Operation": "RM", "Sequence": 4, "Grade": "SAE 1008", "SKU_ID": "WRC-1008-5.5", "Transfer_Time_Min": 10, "Cycle_Time_Min_Heat": 40, "Setup_Time_Min": 40},
        ]
    )


def _make_sales_orders(qty=100.0, due_days=14):
    return pd.DataFrame({
        "SO_ID": ["SO-001"],
        "SKU_ID": ["WRC-1008-5.5"],
        "Grade": ["SAE 1008"],
        "Section_mm": [5.5],
        "Order_Qty_MT": [qty],
        "Order_Qty": [qty],
        "Delivery_Date": [datetime.now() + timedelta(days=due_days)],
        "Order_Date": [datetime.now()],
        "Priority": ["NORMAL"],
        "Status": ["Open"],
        "Campaign_Group": ["SAE 1008"],
    })


def _make_committed_campaigns():
    """Create a released campaign for testing."""
    so = _make_sales_orders(qty=100.0)
    campaigns = build_campaigns(
        so,
        min_campaign_mt=50.0,
        max_campaign_mt=500.0,
        inventory=_make_inventory(500.0),
        bom=_make_bom(),
        skus=_make_skus(),
    )
    for camp in campaigns:
        camp["release_status"] = "RELEASED"
        camp["material_status"] = "READY"
    return campaigns


# ── Tests ────────────────────────────────────────────────────────────────────

class TestCTPCommittedFilter:
    def test_committed_filter_excludes_held_campaigns(self):
        """Finding 1: CTP should only use committed campaigns for capacity."""
        campaigns = _make_committed_campaigns()
        # Add an uncommitted/held campaign
        held = dict(campaigns[0])
        held["campaign_id"] = "CMP-999"
        held["release_status"] = "MATERIAL HOLD"
        all_campaigns = campaigns + [held]

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=datetime.now() + timedelta(days=14),
            campaigns=all_campaigns,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(500.0),
            routing=_make_routing(),
            skus=_make_skus(),
            planning_start=datetime.now(),
        )
        # Should not error out — the held campaign should be excluded
        assert "solver_status" in result
        assert result["sku_id"] == "WRC-1008-5.5"

    def test_committed_statuses_constant(self):
        assert "RELEASED" in COMMITTED_STATUSES
        assert "RUNNING LOCK" in COMMITTED_STATUSES
        assert "MATERIAL HOLD" not in COMMITTED_STATUSES

    def test_inventory_snapshot_uses_sorted_committed_order(self):
        campaigns = [
            {
                "campaign_id": "CMP-010",
                "release_status": "RELEASED",
                "inventory_after": {"BIL-130": 40.0},
            },
            {
                "campaign_id": "CMP-002",
                "release_status": "RELEASED",
                "inventory_after": {"BIL-130": 80.0},
            },
        ]

        result = _net_inventory_after_committed(campaigns, _make_inventory(100.0))
        assert result["BIL-130"] == 40.0

    def test_inventory_snapshot_falls_back_when_snapshot_chain_looks_stale(self):
        campaigns = [
            {
                "campaign_id": "CMP-001",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 100.0},
                "inventory_after": {"BIL-130": 80.0},
                "material_consumed": {"BIL-130": 20.0},
            },
            {
                "campaign_id": "CMP-002",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 60.0},
                "inventory_after": {"BIL-130": 95.0},
                "material_consumed": {"BIL-130": 10.0},
            },
        ]

        result = _net_inventory_after_committed(campaigns, _make_inventory(100.0))
        assert result["BIL-130"] == 70.0


class TestCTPJoinCandidate:
    def test_join_candidate_finds_compatible_campaign(self):
        """Finding 2: _join_candidate should find a matching committed campaign."""
        committed = _make_committed_campaigns()
        ghost = [{
            "campaign_group": committed[0]["campaign_group"],
            "grade": committed[0]["grade"],
            "billet_family": committed[0]["billet_family"],
            "needs_vd": committed[0]["needs_vd"],
        }]
        result = _join_candidate(ghost, committed)
        assert result == committed[0]["campaign_id"]

    def test_join_candidate_returns_none_for_incompatible(self):
        committed = _make_committed_campaigns()
        ghost = [{
            "campaign_group": "DIFFERENT",
            "grade": "Cr-Mo 4140",
            "billet_family": "BIL-150",
            "needs_vd": True,
        }]
        result = _join_candidate(ghost, committed)
        assert result is None

    def test_merge_into_campaign_adds_orders(self):
        """Finding 2: merging should add ghost POs to the target campaign."""
        target = _make_committed_campaigns()[0]
        original_po_count = len(target["production_orders"])
        ghost = {
            "production_orders": [
                {"so_id": "CTP-REQUEST", "sku_id": "WRC-1008-5.5", "qty_mt": 50.0,
                 "section_mm": 5.5, "grade": "SAE 1008", "priority_rank": 1},
            ],
            "total_coil_mt": 50.0,
        }
        merged = _merge_into_campaign(target, ghost)
        assert len(merged["production_orders"]) == original_po_count + 1
        assert merged["total_coil_mt"] > target["total_coil_mt"]

    def test_merged_campaign_targets_merged_request_rows(self, monkeypatch):
        committed = _make_committed_campaigns()
        requested_date = datetime(2026, 4, 20, 12, 0)
        planning_start = datetime(2026, 4, 1, 8, 23)

        def fake_schedule(campaigns, resources, **kwargs):
            assert kwargs["planning_start"] == datetime(2026, 4, 1, 8, 0)
            merged_campaign = next(
                camp for camp in campaigns if camp["campaign_id"] == committed[0]["campaign_id"]
            )
            request_order = next(
                order for order in merged_campaign["production_orders"]
                if str(order.get("so_id", "")).strip() == "CTP-REQUEST"
            )
            return {
                "heat_schedule": pd.DataFrame(
                    [
                        {
                            "Job_ID": f"{request_order['production_order_id']}-RM",
                            "Campaign": merged_campaign["campaign_id"],
                            "Planned_Start": requested_date - timedelta(hours=6),
                            "Planned_End": requested_date - timedelta(hours=2),
                            "Resource_ID": "RM-01",
                        }
                    ]
                ),
                "solver_status": "FEASIBLE",
            }

        monkeypatch.setattr("engine.ctp.schedule", fake_schedule)

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=requested_date,
            campaigns=committed,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(500.0),
            routing=pd.DataFrame(),
            skus=_make_skus(),
            planning_start=planning_start,
        )

        assert result["joins_campaign"] == committed[0]["campaign_id"]
        assert result["earliest_completion"] == pd.Timestamp(requested_date - timedelta(hours=2))
        assert result["earliest_delivery"] is None
        assert result["plant_completion_feasible"] is True
        assert result["delivery_feasible"] is None
        assert result["feasible"] is None
        assert result["lateness_days"] is None
        assert result["completion_gap_days"] == round((-2.0 / 24.0), 2)
        assert result["terminal_resource"] == "RM-01"
        assert result["bottleneck_resource"] is None
        assert result["campaign_action"] == "MERGED_ONLY"
        assert result["merged_campaign_ids"] == [committed[0]["campaign_id"]]
        assert result["new_campaign_ids"] == []
        assert result["partially_merged"] is False

    def test_multi_split_ghost_request_merges_all_campaign_parts(self, monkeypatch):
        committed = _make_committed_campaigns()
        requested_date = datetime(2026, 4, 20, 12, 0)

        split_ghosts = [
            {
                "campaign_group": committed[0]["campaign_group"],
                "grade": committed[0]["grade"],
                "billet_family": committed[0]["billet_family"],
                "needs_vd": committed[0]["needs_vd"],
                "production_orders": [
                    {
                        "so_id": "CTP-REQUEST",
                        "sku_id": "WRC-1008-5.5",
                        "qty_mt": 100.0,
                        "section_mm": 5.5,
                        "grade": "SAE 1008",
                        "priority_rank": 1,
                    }
                ],
                "total_coil_mt": 100.0,
            },
            {
                "campaign_group": committed[0]["campaign_group"],
                "grade": committed[0]["grade"],
                "billet_family": committed[0]["billet_family"],
                "needs_vd": committed[0]["needs_vd"],
                "production_orders": [
                    {
                        "so_id": "CTP-REQUEST",
                        "sku_id": "WRC-1008-5.5",
                        "qty_mt": 80.0,
                        "section_mm": 5.5,
                        "grade": "SAE 1008",
                        "priority_rank": 1,
                    }
                ],
                "total_coil_mt": 80.0,
            },
        ]

        def fake_build_campaigns(*args, **kwargs):
            return split_ghosts

        def fake_schedule(campaigns, resources, **kwargs):
            merged_campaign = next(
                camp for camp in campaigns if camp["campaign_id"] == committed[0]["campaign_id"]
            )
            request_orders = [
                order for order in merged_campaign["production_orders"]
                if str(order.get("so_id", "")).strip() == "CTP-REQUEST"
            ]
            assert len(request_orders) == 2
            assert sum(float(order.get("qty_mt", 0.0) or 0.0) for order in request_orders) == 180.0
            return {
                "heat_schedule": pd.DataFrame(
                    [
                        {
                            "Job_ID": f"{request_orders[0]['production_order_id']}-RM",
                            "Campaign": merged_campaign["campaign_id"],
                            "Planned_Start": requested_date - timedelta(hours=8),
                            "Planned_End": requested_date - timedelta(hours=5),
                            "Resource_ID": "RM-01",
                        },
                        {
                            "Job_ID": f"{request_orders[1]['production_order_id']}-RM",
                            "Campaign": merged_campaign["campaign_id"],
                            "Planned_Start": requested_date - timedelta(hours=4),
                            "Planned_End": requested_date - timedelta(hours=1),
                            "Resource_ID": "RM-02",
                        },
                    ]
                ),
                "solver_status": "FEASIBLE",
            }

        monkeypatch.setattr("engine.ctp.build_campaigns", fake_build_campaigns)
        monkeypatch.setattr("engine.ctp.schedule", fake_schedule)

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=180.0,
            requested_date=requested_date,
            campaigns=committed,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(500.0),
            routing=pd.DataFrame(),
            skus=_make_skus(),
            planning_start=datetime(2026, 4, 1, 8, 0),
        )

        assert result["joins_campaign"] == committed[0]["campaign_id"]
        assert result["earliest_completion"] == pd.Timestamp(requested_date - timedelta(hours=1))
        assert result["plant_completion_feasible"] is True
        assert result["terminal_resource"] == "RM-02"
        assert result["campaign_action"] == "MERGED_ONLY"
        assert result["merged_campaign_ids"] == [committed[0]["campaign_id"]]
        assert result["new_campaign_ids"] == []
        assert result["partially_merged"] is False

    def test_ctp_surfaces_partial_merge_and_new_campaign_ids(self, monkeypatch):
        committed = _make_committed_campaigns()
        requested_date = datetime(2026, 4, 20, 12, 0)

        split_ghosts = [
            {
                "campaign_group": committed[0]["campaign_group"],
                "grade": committed[0]["grade"],
                "billet_family": committed[0]["billet_family"],
                "needs_vd": committed[0]["needs_vd"],
                "production_orders": [
                    {
                        "so_id": "CTP-REQUEST",
                        "sku_id": "WRC-1008-5.5",
                        "qty_mt": 80.0,
                        "section_mm": 5.5,
                        "grade": "SAE 1008",
                        "priority_rank": 1,
                    }
                ],
                "total_coil_mt": 80.0,
            },
            {
                "campaign_group": "OTHER-FAMILY",
                "grade": committed[0]["grade"],
                "billet_family": committed[0]["billet_family"],
                "needs_vd": committed[0]["needs_vd"],
                "production_orders": [
                    {
                        "so_id": "CTP-REQUEST",
                        "sku_id": "WRC-1008-5.5",
                        "qty_mt": 60.0,
                        "section_mm": 5.5,
                        "grade": "SAE 1008",
                        "priority_rank": 1,
                    }
                ],
                "total_coil_mt": 60.0,
            },
        ]

        def fake_build_campaigns(*args, **kwargs):
            return split_ghosts

        def fake_schedule(campaigns, resources, **kwargs):
            merged_campaign = next(
                camp for camp in campaigns if camp["campaign_id"] == committed[0]["campaign_id"]
            )
            new_campaign = next(
                camp for camp in campaigns if str(camp["campaign_id"]).startswith("CTP-")
            )
            merged_order = next(
                order for order in merged_campaign["production_orders"]
                if str(order.get("so_id", "")).strip() == "CTP-REQUEST"
            )
            new_order = new_campaign["production_orders"][0]
            return {
                "heat_schedule": pd.DataFrame(
                    [
                        {
                            "Job_ID": f"{merged_order['production_order_id']}-RM",
                            "Campaign": merged_campaign["campaign_id"],
                            "Planned_Start": requested_date - timedelta(hours=5),
                            "Planned_End": requested_date - timedelta(hours=2),
                            "Resource_ID": "RM-01",
                        },
                        {
                            "Job_ID": f"{new_order['production_order_id']}-RM",
                            "Campaign": new_campaign["campaign_id"],
                            "Planned_Start": requested_date - timedelta(hours=1),
                            "Planned_End": requested_date + timedelta(hours=1),
                            "Resource_ID": "RM-02",
                        },
                    ]
                ),
                "solver_status": "FEASIBLE",
            }

        monkeypatch.setattr("engine.ctp.build_campaigns", fake_build_campaigns)
        monkeypatch.setattr("engine.ctp.schedule", fake_schedule)

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=140.0,
            requested_date=requested_date,
            campaigns=committed,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(500.0),
            routing=pd.DataFrame(),
            skus=_make_skus(),
            planning_start=datetime(2026, 4, 1, 8, 0),
        )

        assert result["campaign_action"] == "PARTIAL_MERGE_AND_NEW"
        assert result["merged_campaign_ids"] == [committed[0]["campaign_id"]]
        assert len(result["new_campaign_ids"]) == 1
        assert result["partially_merged"] is True
        assert result["new_campaign_needed"] is True


class TestCTPCampaignSizing:
    def test_caller_provided_sizing_is_used(self):
        """Finding 3: CTP should respect caller-provided min/max campaign sizing."""
        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=datetime.now() + timedelta(days=14),
            campaigns=[],
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(500.0),
            routing=_make_routing(),
            skus=_make_skus(),
            planning_start=datetime.now(),
            min_campaign_mt=30.0,
            max_campaign_mt=200.0,
        )
        # Should complete without error using the provided sizing
        assert "solver_status" in result

    def test_ctp_blocks_when_inventory_lineage_is_non_authoritative_by_default(self, monkeypatch):
        campaigns = [
            {
                "campaign_id": "CMP-001",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 100.0},
                "inventory_after": {"BIL-130": 80.0},
                "material_consumed": {"BIL-130": 20.0},
                "scheduled_jobs": [],
            },
            {
                "campaign_id": "CMP-002",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 60.0},
                "inventory_after": {"BIL-130": 95.0},
                "material_consumed": {"BIL-130": 10.0},
                "scheduled_jobs": [],
            },
        ]

        def fake_schedule(*args, **kwargs):
            return {"heat_schedule": pd.DataFrame(), "solver_status": "FEASIBLE"}

        monkeypatch.setattr("engine.ctp.schedule", fake_schedule)

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=datetime.now() + timedelta(days=14),
            campaigns=campaigns,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(100.0),
            routing=_make_routing(),
            skus=_make_skus(),
            planning_start=datetime(2026, 4, 1, 8, 0),
        )

        assert result["inventory_lineage_status"] == "CONSERVATIVE_BLEND"
        assert "conservative minimum" in result["inventory_lineage_note"]
        assert result["promise_basis"] == "INVENTORY_LINEAGE_BLOCKED"
        assert result["solver_status"] == "BLOCKED: CONSERVATIVE_BLEND"
        assert result["earliest_completion"] is None
        assert result["campaign_action"] == "INVENTORY_LINEAGE_BLOCKED"

    def test_ctp_can_opt_out_of_authoritative_inventory_block(self, monkeypatch):
        campaigns = [
            {
                "campaign_id": "CMP-001",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 100.0},
                "inventory_after": {"BIL-130": 80.0},
                "material_consumed": {"BIL-130": 20.0},
                "scheduled_jobs": [],
            },
            {
                "campaign_id": "CMP-002",
                "release_status": "RELEASED",
                "inventory_before": {"BIL-130": 60.0},
                "inventory_after": {"BIL-130": 95.0},
                "material_consumed": {"BIL-130": 10.0},
                "scheduled_jobs": [],
            },
        ]

        def fake_schedule(*args, **kwargs):
            return {"heat_schedule": pd.DataFrame(), "solver_status": "FEASIBLE"}

        monkeypatch.setattr("engine.ctp.schedule", fake_schedule)

        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=datetime.now() + timedelta(days=14),
            campaigns=campaigns,
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=_make_inventory(100.0),
            routing=_make_routing(),
            skus=_make_skus(),
            planning_start=datetime(2026, 4, 1, 8, 0),
            config={"Require_Authoritative_CTP_Inventory": "N"},
        )

        assert result["inventory_lineage_status"] == "CONSERVATIVE_BLEND"
        assert result["solver_status"] == "FEASIBLE"


class TestCTPSemantics:
    def test_ghost_sales_order_uses_supplied_order_date(self):
        order_date = datetime(2026, 4, 1, 8, 0)
        ghost = _ghost_sales_order(
            "WRC-1008-5.5",
            50.0,
            datetime(2026, 4, 20, 12, 0),
            _make_skus(),
            order_date=order_date,
        )

        assert pd.Timestamp(ghost.iloc[0]["Order_Date"]) == pd.Timestamp(order_date)

    def test_stock_result_does_not_populate_delivery_when_unmodeled(self):
        stock_inventory = pd.DataFrame(
            {
                "SKU_ID": ["WRC-1008-5.5"],
                "Available_Qty": [100.0],
            }
        )
        result = capable_to_promise(
            sku_id="WRC-1008-5.5",
            qty_mt=50.0,
            requested_date=datetime.now() + timedelta(days=14),
            campaigns=[],
            resources=_make_resources(),
            bom=_make_bom(),
            inventory=stock_inventory,
            routing=pd.DataFrame(),
            skus=_make_skus(),
            planning_start=datetime(2026, 4, 1, 8, 0),
        )

        assert result["delivery_modeled"] is False
        assert result["earliest_completion"] is not None
        assert result["earliest_delivery"] is None
        assert result["plant_completion_feasible"] is True
        assert result["delivery_feasible"] is None
        assert result["feasible"] is None
        assert result["lateness_days"] is None
        assert result["completion_gap_days"] is not None
        assert result["campaign_action"] == "STOCK_ONLY"
        assert result["merged_campaign_ids"] == []
        assert result["new_campaign_ids"] == []
