"""Tests for campaign configuration — sizing and config precedence."""
import sys
from datetime import datetime, timedelta
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from engine.campaign import build_campaigns


def _make_sales_orders(qty=200.0):
    return pd.DataFrame({
        "SO_ID": ["SO-001", "SO-002"],
        "SKU_ID": ["WRC-1008-5.5", "WRC-1008-5.5"],
        "Grade": ["SAE 1008", "SAE 1008"],
        "Section_mm": [5.5, 5.5],
        "Order_Qty_MT": [qty, qty],
        "Order_Qty": [qty, qty],
        "Delivery_Date": [datetime.now() + timedelta(days=14)] * 2,
        "Order_Date": [datetime.now()] * 2,
        "Priority": ["NORMAL", "NORMAL"],
        "Status": ["Open", "Open"],
        "Campaign_Group": ["SAE 1008", "SAE 1008"],
    })


def _make_multi_grade_sales_orders():
    now = datetime.now()
    due = now + timedelta(days=14)
    return pd.DataFrame({
        "SO_ID": ["SO-001", "SO-002"],
        "SKU_ID": ["WRC-1008-5.5", "WRC-1080-5.5"],
        "Grade": ["SAE 1008", "SAE 1080"],
        "Section_mm": [5.5, 5.5],
        "Order_Qty_MT": [100.0, 100.0],
        "Order_Qty": [100.0, 100.0],
        "Delivery_Date": [due, due],
        "Order_Date": [now, now],
        "Priority": ["NORMAL", "NORMAL"],
        "Status": ["Open", "Open"],
        "Campaign_Group": ["SAE 1008", "SAE 1080"],
    })


class TestCampaignSizing:
    def test_min_campaign_respected(self):
        """Campaigns below min size should be flagged."""
        campaigns = build_campaigns(
            _make_sales_orders(qty=30.0),
            min_campaign_mt=100.0,
            max_campaign_mt=500.0,
        )
        assert len(campaigns) > 0
        # With 2 orders of 30 MT each = 60 MT, below 100 MT min
        for camp in campaigns:
            if camp["total_coil_mt"] < 100.0:
                assert camp["below_min_campaign"] is True

    def test_max_campaign_splits(self):
        """Orders exceeding max should be split into multiple campaigns."""
        campaigns = build_campaigns(
            _make_sales_orders(qty=400.0),
            min_campaign_mt=50.0,
            max_campaign_mt=300.0,
        )
        # 2 * 400 = 800 MT, max 300 -> should split
        assert len(campaigns) >= 2
        for camp in campaigns:
            assert camp["total_coil_mt"] <= 300.0 + 1e-6

    def test_sku_attributes_propagated(self):
        """Finding 4: Campaign should carry sku_attributes from order lines."""
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
        )
        assert len(campaigns) > 0
        for camp in campaigns:
            assert "sku_attributes" in camp
            assert isinstance(camp["sku_attributes"], dict)

    def test_release_status_without_bom(self):
        """Without BOM, all campaigns should be held for material verification."""
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            bom=None,
        )
        for camp in campaigns:
            assert camp["release_status"] == "MATERIAL HOLD"

    def test_primary_batch_trace_failure_holds_campaign_by_default(self):
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["WRC-1008-5.5"],
                "Child_SKU": ["BIL-130"],
                "Qty_Per": [1.1],
                "Flow_Type": ["INPUT"],
                "Yield_Pct": [95.0],
            }
        )
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            bom=bom,
            inventory=pd.DataFrame({"SKU_ID": ["BIL-130"], "Available_Qty": [1000.0]}),
        )
        assert campaigns
        assert campaigns[0]["heats_calc_method"] == "LEGACY_BLOCKED"
        assert campaigns[0]["heats_trace_valid"] is False
        assert campaigns[0]["heats_calc_warnings"]
        assert campaigns[0]["heats_calc_errors"]
        assert campaigns[0]["release_status"] == "MATERIAL HOLD"
        assert campaigns[0]["material_status"] == "BOM ERROR"
        assert "PRIMARY_BATCH_TRACE" in campaigns[0]["material_issue"]

    def test_primary_batch_trace_legacy_estimate_is_diagnostic_only(self):
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["WRC-1008-5.5"],
                "Child_SKU": ["BIL-130"],
                "Qty_Per": [1.1],
                "Flow_Type": ["INPUT"],
                "Yield_Pct": [95.0],
            }
        )
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            bom=bom,
            inventory=pd.DataFrame({"SKU_ID": ["BIL-130"], "Available_Qty": [1000.0]}),
            config={"Allow_Legacy_Primary_Batch_Fallback": "Y"},
        )
        assert campaigns
        assert campaigns[0]["heats_calc_method"] == "LEGACY_DIAGNOSTIC_ONLY"
        assert campaigns[0]["heats_trace_valid"] is False
        assert campaigns[0]["release_status"] == "MATERIAL HOLD"
        assert campaigns[0]["material_status"] == "BOM ERROR"
        assert campaigns[0]["heats_calc_warnings"]

    def test_bom_structure_error_hard_fails_by_default(self):
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["WRC-1008-5.5", "BIL-130", "INT-LOOP"],
                "Child_SKU": ["BIL-130", "INT-LOOP", "BIL-130"],
                "Qty_Per": [1.0, 1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT", "INPUT"],
            }
        )
        with pytest.raises(ValueError, match="BOM cycle detected"):
            build_campaigns(
                _make_sales_orders(qty=100.0),
                min_campaign_mt=50.0,
                max_campaign_mt=500.0,
                bom=bom,
                inventory=pd.DataFrame({"SKU_ID": ["BIL-130"], "Available_Qty": [0.0]}),
                config={"Primary_Batch_Resource_Group": "CCM"},
            )

    def test_bom_structure_error_can_be_recorded_into_campaign_hold(self):
        bom = pd.DataFrame(
            {
                "Parent_SKU": ["WRC-1008-5.5", "BIL-130", "INT-LOOP"],
                "Child_SKU": ["BIL-130", "INT-LOOP", "BIL-130"],
                "Qty_Per": [1.0, 1.0, 1.0],
                "Flow_Type": ["INPUT", "INPUT", "INPUT"],
            }
        )
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            bom=bom,
            inventory=pd.DataFrame({"SKU_ID": ["BIL-130"], "Available_Qty": [0.0]}),
            config={
                "BOM_Structure_Error_Mode": "RECORD",
                "Primary_Batch_Resource_Group": "CCM",
            },
        )
        assert campaigns
        assert campaigns[0]["release_status"] == "MATERIAL HOLD"
        assert campaigns[0]["material_status"] == "BOM ERROR"
        assert campaigns[0]["material_structure_errors"]

    def test_manual_campaign_preserves_exact_grouping_by_default(self):
        sales_orders = _make_sales_orders(qty=400.0)
        sales_orders["Campaign_ID"] = ["MAN-001", "MAN-001"]
        campaigns = build_campaigns(
            sales_orders,
            min_campaign_mt=50.0,
            max_campaign_mt=300.0,
        )
        manual_campaigns = [camp for camp in campaigns if camp.get("manual_campaign_id") == "MAN-001"]
        assert len(manual_campaigns) == 1
        assert manual_campaigns[0]["manual_campaign_grouping_mode"] == "PRESERVE_EXACT"
        assert manual_campaigns[0]["manual_campaign_split"] is False
        assert manual_campaigns[0]["manual_campaign_over_max"] is True

    def test_manual_campaign_can_opt_into_split_to_max(self):
        sales_orders = _make_sales_orders(qty=400.0)
        sales_orders["Campaign_ID"] = ["MAN-001", "MAN-001"]
        campaigns = build_campaigns(
            sales_orders,
            min_campaign_mt=50.0,
            max_campaign_mt=300.0,
            config={"Manual_Campaign_Grouping_Mode": "SPLIT_TO_MAX"},
        )
        manual_campaigns = [camp for camp in campaigns if camp.get("manual_campaign_id") == "MAN-001"]
        assert len(manual_campaigns) >= 2
        assert all(camp["manual_campaign_grouping_mode"] == "SPLIT_TO_MAX" for camp in manual_campaigns)
        assert all(camp["manual_campaign_split"] is True for camp in manual_campaigns)

    def test_campaign_grade_order_comes_from_campaign_config(self):
        campaign_config = pd.DataFrame(
            {
                "Grade": ["SAE 1008", "SAE 1080"],
                "Grade_Seq_Order": [2, 1],
            }
        )

        campaigns = build_campaigns(
            _make_multi_grade_sales_orders(),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            campaign_config=campaign_config,
        )

        assert [camp["grade"] for camp in campaigns] == ["SAE 1080", "SAE 1008"]
        assert [camp["grade_order"] for camp in campaigns] == [1, 2]

    def test_priority_sequence_config_controls_campaign_priority_rank(self):
        sales_orders = _make_sales_orders(qty=100.0)
        sales_orders["Priority"] = ["LOW", "URGENT"]

        campaigns = build_campaigns(
            sales_orders,
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            config={"PRIORITY_SEQUENCE": "LOW,URGENT,HIGH,NORMAL"},
        )

        assert campaigns
        assert campaigns[0]["priority"] == "LOW"
        assert campaigns[0]["priority_rank"] == 1

    def test_reserved_inventory_is_not_treated_as_usable_stock(self):
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            inventory=pd.DataFrame(
                {
                    "SKU_ID": ["WRC-1008-5.5"],
                    "Available_Qty": [200.0],
                    "Reserved_Qty": [200.0],
                }
            ),
        )

        assert campaigns
        assert sum(camp["total_coil_mt"] for camp in campaigns) == pytest.approx(200.0)

    def test_sku_master_section_and_vd_flags_drive_campaign_lines(self):
        sales_orders = _make_sales_orders(qty=100.0)
        sales_orders["Section_mm"] = [None, None]
        sku_master = pd.DataFrame(
            {
                "SKU_ID": ["WRC-1008-5.5"],
                "Section_mm": [8.0],
                "Needs_VD": ["Y"],
                "Product_Family": ["WIRE_ROD"],
            }
        )

        campaigns = build_campaigns(
            sales_orders,
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            skus=sku_master,
        )

        assert campaigns
        assert campaigns[0]["section_mm"] == pytest.approx(8.0)
        assert campaigns[0]["sections_covered"] == "8"
        assert all(po["section_mm"] == pytest.approx(8.0) for po in campaigns[0]["production_orders"])
        assert all(po["needs_vd"] is True for po in campaigns[0]["production_orders"])

    def test_routing_campaign_limits_override_global_campaign_limits(self):
        routing = pd.DataFrame(
            {
                "Grade": ["SAE 1008"],
                "Min_Campaign_MT": [120.0],
                "Max_Campaign_MT": [250.0],
            }
        )

        campaigns = build_campaigns(
            _make_sales_orders(qty=220.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            routing=routing,
        )

        assert len(campaigns) == 2
        assert all(camp["total_coil_mt"] <= 250.0 + 1e-6 for camp in campaigns)
        assert all(camp["total_coil_mt"] >= 120.0 - 1e-6 for camp in campaigns)
