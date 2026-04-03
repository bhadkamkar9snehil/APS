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
        """Without BOM, all campaigns should be released."""
        campaigns = build_campaigns(
            _make_sales_orders(qty=100.0),
            min_campaign_mt=50.0,
            max_campaign_mt=500.0,
            bom=None,
        )
        for camp in campaigns:
            assert camp["release_status"] == "RELEASED"

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
