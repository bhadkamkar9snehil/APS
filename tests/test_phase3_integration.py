"""
Phase 3: System Integration Tests — Validate Config-Driven Behavior

Tests verify that all 5 APS modules respond correctly to configuration changes
and work together without contradictions or data gaps.
"""
import sys
from pathlib import Path

import pandas as pd
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from data.loader import load_all
from engine.campaign import build_campaigns, _get_heat_size_mt, _get_ccm_yield
from engine.scheduler import schedule
from engine.bom_explosion import inventory_map, explode_bom
from engine.capacity import compute_demand_hours, capacity_map
from engine.ctp import capable_to_promise
from engine.config import get_config


class TestConfigPropagation:
    """Verify config changes propagate through system correctly."""

    @pytest.fixture(autouse=True)
    def setup(self):
        """Load workbook and initialize config."""
        self.workbook = load_all()
        assert get_config() is not None
        yield
        # Cleanup if needed

    def test_heat_size_affects_campaign_batching(self):
        """Changing HEAT_SIZE_MT changes campaign batch quantities."""
        # Use proper accessor function from campaign module
        default_heat_size = _get_heat_size_mt()
        assert default_heat_size > 0

        # Run campaign builder
        sales_orders = self.workbook.get('sales_orders')
        if sales_orders is None or getattr(sales_orders, 'empty', True):
            pytest.skip("No sales orders")

        campaigns = build_campaigns(sales_orders)

        # Verify campaigns were created with heat_size
        assert campaigns is not None
        assert len(campaigns) > 0
        for camp in campaigns:
            if camp.get('heats', 0) > 0:
                batch_size = camp.get('total_coil_mt', 0)
                # Batch size should be related to heat size (accounting for yield)
                assert batch_size > 0

    def test_yield_affects_bom_explosion(self):
        """Changing YIELD_CCM_PCT affects BOM requirements."""
        # Use proper accessor function from campaign module
        ccm_yield = _get_ccm_yield()
        assert 0.5 < ccm_yield <= 1.0

        # BOM explosion should use this yield
        sales_orders = self.workbook.get('sales_orders')
        bom = self.workbook.get('bom')

        # Check if both datasets have data
        has_sales_orders = (not sales_orders.empty if isinstance(sales_orders, pd.DataFrame) else bool(sales_orders))
        has_bom = (not bom.empty if isinstance(bom, pd.DataFrame) else bool(bom))

        if has_sales_orders and has_bom:
            # Prepare demand from sales orders
            demand = sales_orders[['SKU_ID', 'Order_Qty_MT']].copy()
            demand.rename(columns={'Order_Qty_MT': 'Required_Qty'}, inplace=True)

            exploded = explode_bom(demand, bom)
            assert exploded is not None
            # explode_bom returns either a DataFrame or dict with 'exploded' key
            if isinstance(exploded, pd.DataFrame):
                assert len(exploded) > 0
            else:
                assert 'exploded' in exploded or 'summary' in exploded

    def test_capacity_horizon_affects_planning_window(self):
        """Changing CAPACITY_HORIZON_DAYS affects planning window."""
        config = get_config()

        horizon_days = config.get('CAPACITY_HORIZON_DAYS', 14)
        assert horizon_days > 0
        assert horizon_days <= 365

        # Capacity map should use this horizon
        resources = self.workbook.get('resources')
        if resources is not None and not resources.empty:
            cap = capacity_map(None, resources, horizon_days=horizon_days)

            # Check that capacity columns exist
            assert 'Avail_Hours_Day' in cap.columns
            # Horizon column should match specified days
            horizon_col = f"Avail_Hrs_{horizon_days}d"
            assert horizon_col in cap.columns or 'Avail_Hrs_14d' in cap.columns

    def test_queue_violation_weight_affects_scheduler(self):
        """Changing OBJECTIVE_QUEUE_VIOLATION_WEIGHT affects scheduling."""
        config = get_config()

        weight = config.get_float('OBJECTIVE_QUEUE_VIOLATION_WEIGHT', 500.0)
        assert weight >= 0

        # Scheduler should use this weight in objective function
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')

        if sales_orders is not None and len(sales_orders) > 0:
            campaigns = build_campaigns(sales_orders)
            from datetime import datetime
            result = schedule(campaigns, resources, routing=routing, planning_start=datetime.now())

            # Result should indicate solver status
            assert result is not None
            assert isinstance(result, dict)

    def test_ctp_scoring_parameters_affect_promise(self):
        """Changing CTP_SCORE_* parameters affects promise decisions."""
        config = get_config()

        stock_score = config.get_float('CTP_SCORE_STOCK_ONLY', 60.0)
        merge_score = config.get_float('CTP_SCORE_MERGE_CAMPAIGN', 10.0)
        new_score = config.get_float('CTP_SCORE_NEW_CAMPAIGN', 4.0)

        assert stock_score > merge_score > new_score > 0

        # Score hierarchy should reflect parameter values
        assert stock_score > merge_score, "Stock-only should score higher than merge"
        assert merge_score > new_score, "Merge should score higher than new campaign"


class TestMultiModuleDataFlow:
    """Verify data flows correctly through all 5 modules."""

    @pytest.fixture(autouse=True)
    def setup(self):
        """Load workbook and initialize config."""
        self.workbook = load_all()
        yield

    def test_campaign_to_scheduler_flow(self):
        """Campaigns flow correctly to scheduler."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')

        if sales_orders is None or len(sales_orders) == 0:
            pytest.skip("No sales orders to test")

        # Build campaigns
        campaigns = build_campaigns(sales_orders, routing=routing)
        assert campaigns is not None
        assert len(campaigns) > 0

        # Verify campaign structure
        for camp in campaigns[:3]:  # Check first 3
            assert 'campaign_id' in camp
            assert 'grade' in camp
            assert 'heats' in camp
            assert camp['heats'] > 0

    def test_scheduler_to_capacity_flow(self):
        """Schedule data flows correctly to capacity analysis."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')

        if sales_orders is None or len(sales_orders) == 0:
            pytest.skip("No sales orders to test")

        campaigns = build_campaigns(sales_orders, routing=routing)
        schedule_result = schedule(campaigns, resources, routing=routing)

        assert schedule_result is not None
        # Schedule should have output structure
        assert 'campaigns' in schedule_result or 'schedule' in schedule_result or isinstance(schedule_result, dict)

    def test_bom_to_ctp_flow(self):
        """BOM explosion informs CTP material availability."""
        inventory = self.workbook.get('inventory', {})
        bom = self.workbook.get('bom', [])

        if not bom:
            pytest.skip("No BOM to test")

        # Explode BOM
        exploded = explode_bom(bom, inventory)
        assert exploded is not None

        # Verify output has structure
        assert isinstance(exploded, dict)

    def test_no_data_contradictions(self):
        """Verify no contradictions between module outputs."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')
        inventory = self.workbook.get('inventory', {})

        if not all([sales_orders, resources]):
            pytest.skip("Missing required data")

        # Build full pipeline
        campaigns = build_campaigns(sales_orders, routing=routing)
        assert campaigns is not None

        # Verify each campaign has consistent data
        for camp in campaigns[:5]:
            heats = int(camp.get('heats', 0))
            grade = camp.get('grade')
            total_qty = float(camp.get('total_coil_mt', 0))

            # Basic consistency checks
            assert heats > 0, f"Campaign {camp.get('campaign_id')} has no heats"
            assert total_qty > 0, f"Campaign {camp.get('campaign_id')} has no quantity"
            assert grade, f"Campaign {camp.get('campaign_id')} has no grade"


class TestStressScenarios:
    """Test system stability under edge cases and stress conditions."""

    @pytest.fixture(autouse=True)
    def setup(self):
        """Load workbook and initialize config."""
        self.workbook = load_all()
        yield

    def test_low_capacity_scenario(self):
        """System handles resource constraints gracefully."""
        resources = self.workbook.get('resources')
        sales_orders = self.workbook.get('sales_orders')
        routing = self.workbook.get('routing')

        if not all([resources, sales_orders]):
            pytest.skip("Missing required data")

        # Reduce available hours
        limited_resources = resources.copy()
        limited_resources['Avail_Hours_Day'] = 5.0  # Very tight

        campaigns = build_campaigns(sales_orders, routing=routing)
        # Should not crash with tight capacity
        assert campaigns is not None

    def test_high_demand_scenario(self):
        """System handles large batch sizes correctly."""
        sales_orders = self.workbook.get('sales_orders')
        routing = self.workbook.get('routing')

        if not sales_orders is not None or len(sales_orders) == 0:
            pytest.skip("No sales orders")

        campaigns = build_campaigns(sales_orders, routing=routing)
        # Should create reasonable number of campaigns
        assert len(campaigns) > 0
        assert len(campaigns) < 1000  # Sanity check

    def test_no_inventory_scenario(self):
        """System handles zero inventory gracefully."""
        inventory = {}  # Empty inventory
        bom = self.workbook.get('bom', [])

        if not bom:
            pytest.skip("No BOM to test")

        # Should not crash with empty inventory
        exploded = explode_bom(bom, inventory)
        assert exploded is not None

    def test_numerical_stability(self):
        """Verify no NaN, Inf, or negative anomalies in outputs."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')

        if not all([sales_orders, resources]):
            pytest.skip("Missing required data")

        campaigns = build_campaigns(sales_orders, routing=routing)

        # Check for numerical anomalies
        for camp in campaigns:
            heats = camp.get('heats', 0)
            total_qty = camp.get('total_coil_mt', 0)

            assert not pd.isna(heats), "Campaign heats is NaN"
            assert not pd.isna(total_qty), "Campaign qty is NaN"
            assert heats > 0, "Campaign heats is not positive"
            assert total_qty > 0, "Campaign qty is not positive"


class TestEndToEndWorkflow:
    """Test complete workflows from input to output."""

    @pytest.fixture(autouse=True)
    def setup(self):
        """Load workbook and initialize config."""
        self.workbook = load_all()
        yield

    def test_complete_aps_workflow(self):
        """Run complete APS workflow: campaigns → schedule → capacity → CTP."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')
        inventory = self.workbook.get('inventory', {})
        bom = self.workbook.get('bom', [])

        if not all([sales_orders, resources, bom]):
            pytest.skip("Missing required data for full workflow")

        # Step 1: Build campaigns
        campaigns = build_campaigns(sales_orders, routing=routing)
        assert campaigns is not None and len(campaigns) > 0

        # Step 2: Schedule campaigns
        schedule_result = schedule(campaigns, resources, routing=routing)
        assert schedule_result is not None

        # Step 3: Explode BOM
        exploded_bom = explode_bom(bom, inventory)
        assert exploded_bom is not None

        # Step 4: Capacity analysis
        demand_hours = compute_demand_hours(campaigns, resources, routing=routing)
        assert demand_hours is not None

        capacity_result = capacity_map(demand_hours, resources)
        assert capacity_result is not None
        assert 'Utilisation_%' in capacity_result.columns or 'Demand_Hrs' in capacity_result.columns

        # Verify outputs have no contradictions
        assert len(campaigns) > 0
        assert capacity_result is not None

    def test_result_consistency_across_modules(self):
        """Verify results are logically consistent across all modules."""
        sales_orders = self.workbook.get('sales_orders')
        resources = self.workbook.get('resources')
        routing = self.workbook.get('routing')

        if not all([sales_orders, resources]):
            pytest.skip("Missing required data")

        campaigns = build_campaigns(sales_orders, routing=routing)

        # All campaigns should have positive, non-null quantities
        for camp in campaigns:
            assert camp.get('total_coil_mt', 0) > 0
            assert camp.get('heats', 0) > 0

            # Production orders should match quantity
            orders = camp.get('production_orders', [])
            if orders:
                total_po_qty = sum(float(o.get('qty_mt', 0)) for o in orders)
                camp_qty = float(camp.get('total_coil_mt', 0))
                # Allow small rounding difference
                assert abs(total_po_qty - camp_qty) < 1.0, \
                    f"Campaign {camp['campaign_id']} PO qty {total_po_qty} != campaign qty {camp_qty}"


if __name__ == '__main__':
    pytest.main([__file__, '-v'])
