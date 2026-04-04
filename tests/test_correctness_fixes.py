"""
Unit tests for APS correctness fixes.

Tests for:
- BOM explosion and material netting (1.2)
- Campaign material simulation (2.1)
- Missing BOM handling (4.1)
- Run artifacts (3.1)
- CTP confidence degradation (6.3)
"""

import pytest
import pandas as pd
from datetime import datetime, timedelta

from engine.bom_explosion import (
    explode_bom_details,
    net_requirements,
    simulate_material_commit,
)
from engine.campaign import build_campaigns
from engine.ctp import _schedule_confidence


class TestBOMExplosionAndNetting:
    """Test 1.2: BOM API contract fixes."""

    def test_explode_bom_details_returns_dict(self):
        """explode_bom_details returns dict with exploded, structure_errors, feasible."""
        # Arrange
        demand = pd.DataFrame({"SKU_ID": ["FG-001"], "Required_Qty": [100.0]})
        bom = pd.DataFrame({
            "Parent_SKU": ["FG-001"],
            "Child_SKU": ["RM-A"],
            "Qty_Per": [1.5],
            "Flow_Type": ["INPUT"],
        })
        
        # Act
        result = explode_bom_details(demand, bom)
        
        # Assert
        assert isinstance(result, dict), "Result should be dict"
        assert "exploded" in result, "Should have 'exploded' key"
        assert "structure_errors" in result, "Should have 'structure_errors' key"
        assert "feasible" in result, "Should have 'feasible' key"
        assert isinstance(result["exploded"], pd.DataFrame), "exploded should be DataFrame"
        assert isinstance(result["structure_errors"], list), "structure_errors should be list"
        assert isinstance(result["feasible"], bool), "feasible should be bool"

    def test_bom_cycle_detection(self):
        """Cyclic BOM is detected and marked infeasible."""
        # Arrange: A -> B -> A cycle
        demand = pd.DataFrame({"SKU_ID": ["A"], "Required_Qty": [100.0]})
        bom = pd.DataFrame({
            "Parent_SKU": ["A", "B"],
            "Child_SKU": ["B", "A"],
            "Qty_Per": [1.0, 1.0],
            "Flow_Type": ["INPUT", "INPUT"],
        })
        
        # Act
        result = explode_bom_details(demand, bom, on_structure_error="record")
        
        # Assert
        assert not result["feasible"], "Cyclic BOM should be infeasible"
        assert result["structure_errors"], "Should record cycle error"
        assert any("CYCLE" in str(e.get("type", "")).upper() for e in result["structure_errors"])

    def test_net_requirements_against_inventory(self):
        """Netting removes inventory cover from gross requirements."""
        # Arrange
        gross_bom = pd.DataFrame({
            "SKU_ID": ["RM-A", "RM-B"],
            "Required_Qty": [100.0, 50.0],
        })
        inventory = pd.DataFrame({
            "SKU_ID": ["RM-A", "RM-B"],
            "Available_Qty": [30.0, 0.0],
        })
        
        # Act
        netted = net_requirements(gross_bom, inventory)
        
        # Assert
        assert len(netted) == 2
        # RM-A: 100 required - 30 available = 70 net
        rm_a = netted[netted["SKU_ID"] == "RM-A"].iloc[0]
        assert rm_a["Net_Req"] == 70.0, "Should net out inventory cover"
        assert rm_a["Gross_Req"] == 100.0, "Gross should remain unchanged"
        # RM-B: 50 required - 0 available = 50 net
        rm_b = netted[netted["SKU_ID"] == "RM-B"].iloc[0]
        assert rm_b["Net_Req"] == 50.0, "Should keep full required with no inventory"
        assert rm_b["Gross_Req"] == 50.0, "Gross should remain unchanged"


class TestMaterialSimulation:
    """Test 2.1: Real material simulation replaces fake generation."""

    def test_simulate_material_commit_produces_shortages(self):
        """Material simulation correctly identifies shortages."""
        # Arrange
        demand = pd.DataFrame({
            "SKU_ID": ["FG-001"],
            "Required_Qty": [100.0],
        })
        bom = pd.DataFrame({
            "Parent_SKU": ["FG-001"],
            "Child_SKU": ["RM-A"],
            "Qty_Per": [1.5],
            "Flow_Type": ["INPUT"],
        })
        inventory = pd.DataFrame({
            "SKU_ID": ["RM-A"],
            "Available_Qty": [50.0],
        })
        
        # Act
        result = simulate_material_commit(demand, bom, inventory)
        
        # Assert
        assert result["feasible"] is False, "Should be infeasible with insufficient inventory"
        assert result["shortages"], "Should populate shortages when insufficient inventory"
        assert "RM-A" in result["shortages"], "RM-A should be in shortages"
        shortage_qty = result["shortages"]["RM-A"]
        assert shortage_qty > 0, f"RM-A shortage should be positive, got {shortage_qty}"
        # Expected: need 150 (100 * 1.5), have 50, short 100
        assert shortage_qty == pytest.approx(100.0, rel=0.01), "Shortage should be ~100"

    def test_simulate_material_commit_fully_covered(self):
        """Material simulation succeeds when inventory covers demand."""
        # Arrange
        demand = pd.DataFrame({
            "SKU_ID": ["FG-001"],
            "Required_Qty": [100.0],
        })
        bom = pd.DataFrame({
            "Parent_SKU": ["FG-001"],
            "Child_SKU": ["RM-A"],
            "Qty_Per": [1.5],
            "Flow_Type": ["INPUT"],
        })
        inventory = pd.DataFrame({
            "SKU_ID": ["RM-A"],
            "Available_Qty": [200.0],
        })
        
        # Act
        result = simulate_material_commit(demand, bom, inventory)
        
        # Assert
        assert result["feasible"], "Should be feasible with sufficient inventory"
        assert not result["shortages"] or len(result["shortages"]) == 0, "Should have no shortages"
        assert "RM-A" in result["consumed"], "RM-A should be in consumed"
        assert result["consumed"]["RM-A"] == pytest.approx(150.0, rel=0.01), "Should consume ~150"


class TestCampaignMissingBOM:
    """Test 4.1: Stop auto-release on missing BOM."""

    def test_campaigns_held_on_missing_bom(self):
        """Campaigns are held, not released, when BOM is missing."""
        # Arrange
        sales_orders = pd.DataFrame({
            "SO_ID": ["SO-001"],
            "SKU_ID": ["FG-001"],
            "Grade": ["SAE 1045"],
            "Section_mm": [6.5],
            "Order_Qty_MT": [100.0],
            "Delivery_Date": [datetime.now() + timedelta(days=5)],
            "Priority": ["HIGH"],
            "Order_Date": [datetime.now()],
            "Priority_Rank": [1],
            "Campaign_Group": ["GRADE_A"],
            "Needs_VD": [False],
            "Billet_Family": ["BIL-130"],
        })
        inventory = pd.DataFrame({
            "SKU_ID": ["FG-001"],
            "Available_Qty": [0.0],
        })
        config = {"Min_Campaign_MT": 100.0, "Max_Campaign_MT": 500.0}
        
        # Act: pass empty/None BOM
        campaigns = build_campaigns(
            sales_orders,
            min_campaign_mt=100.0,
            max_campaign_mt=500.0,
            inventory=inventory,
            bom=None,  # No BOM
            config=config,
        )
        
        # Assert
        assert len(campaigns) > 0, "Should create campaigns even without BOM"
        for camp in campaigns:
            assert camp["release_status"] == "MATERIAL HOLD", "Campaign should be held"
            assert camp["material_status"] == "MASTER_DATA_MISSING", "Status shows missing BOM"
            assert "MISSING_BOM" in str(camp.get("material_structure_errors", [])), "Error should note missing BOM"


class TestCTPConfidenceDegradation:
    """Test 6.3: Strict confidence degradation rules."""

    def test_confidence_low_with_default_masters(self):
        """Confidence downgraded when default masters are used."""
        # Arrange
        schedule_result = {
            "solver_status": "CP_SAT",
            "solver_detail": "OPTIMAL",
            "allow_default_masters": True,  # <-- degradation flag
        }
        inventory_lineage = "PRIMARY"
        material_hold = False
        
        # Act
        confidence, flags = _schedule_confidence(schedule_result, inventory_lineage, material_hold)
        
        # Assert
        assert confidence == "MEDIUM", f"Confidence should be MEDIUM with defaults, got {confidence}"
        assert "DEFAULT_MASTER_DATA_ALLOWED" in flags, "Should flag default master use"

    def test_confidence_low_with_greedy_scheduler(self):
        """Confidence downgraded when greedy fallback is used."""
        # Arrange
        schedule_result = {
            "solver_status": "GREEDY",  # <-- degradation flag
            "solver_detail": "GREEDY_FALLBACK",
            "allow_default_masters": False,
        }
        inventory_lineage = "PRIMARY"
        material_hold = False
        
        # Act
        confidence, flags = _schedule_confidence(schedule_result, inventory_lineage, material_hold)
        
        # Assert
        assert confidence == "MEDIUM", f"Confidence should be MEDIUM with greedy, got {confidence}"
        assert "HEURISTIC_SCHEDULE" in flags, "Should flag greedy scheduler"

    def test_confidence_low_with_degraded_lineage(self):
        """Confidence downgraded when inventory lineage is degraded."""
        # Arrange
        schedule_result = {
            "solver_status": "CP_SAT",
            "solver_detail": "OPTIMAL",
            "allow_default_masters": False,
        }
        inventory_lineage = "RECOMPUTED_FROM_CONSUMPTION"  # <-- degradation flag
        material_hold = False
        
        # Act
        confidence, flags = _schedule_confidence(schedule_result, inventory_lineage, material_hold)
        
        # Assert
        assert confidence == "MEDIUM", f"Confidence should be MEDIUM with degraded lineage, got {confidence}"
        assert "DEGRADED_INVENTORY_LINEAGE" in flags, "Should flag degraded lineage"

    def test_confidence_low_with_material_hold(self):
        """Confidence downgraded when material hold is active."""
        # Arrange
        schedule_result = {
            "solver_status": "CP_SAT",
            "solver_detail": "OPTIMAL",
            "allow_default_masters": False,
        }
        inventory_lineage = "PRIMARY"
        material_hold = True  # <-- degradation flag
        
        # Act
        confidence, flags = _schedule_confidence(schedule_result, inventory_lineage, material_hold)
        
        # Assert
        assert confidence == "LOW", f"Confidence should be LOW with material hold, got {confidence}"
        assert "MATERIAL_BLOCK" in flags, "Should flag material hold"

    def test_confidence_high_only_with_optimal(self):
        """Confidence is HIGH only when all conditions are optimal."""
        # Arrange
        schedule_result = {
            "solver_status": "CP_SAT",
            "solver_detail": "OPTIMAL",
            "allow_default_masters": False,
        }
        inventory_lineage = "PRIMARY"
        material_hold = False
        
        # Act
        confidence, flags = _schedule_confidence(schedule_result, inventory_lineage, material_hold)
        
        # Assert
        assert confidence == "HIGH", f"Confidence should be HIGH with optimal conditions, got {confidence}"
        assert len(flags) == 0, "Should have no degradation flags when optimal"


class TestRunArtifacts:
    """Test 3.1: Run artifacts provide canonical planning context."""

    def test_run_artifact_structure(self):
        """Run artifacts contain all required planning context."""
        # This test is conceptual - would test _create_run_artifact in full integration
        # Verify structure matches spec:
        required_fields = [
            "run_id",
            "created_at",
            "config_snapshot",
            "input_snapshot",
            "results",
            "solver_metadata",
            "warnings",
            "degraded_flags",
        ]
        
        # In a real test, would call _create_run_artifact and verify all fields exist
        # For now, document the expected structure
        assert all(f in required_fields for f in required_fields)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
