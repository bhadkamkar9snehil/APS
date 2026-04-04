"""
Test that material plan is a first-class artifact result.

Tests for Task 2.1 and 2.2:
- Material plan is included in artifact results
- Material plan is computed with proper run_id
- Material plan is not dependent on _state as primary storage
"""

import sys
from pathlib import Path
from datetime import datetime

# Add parent to path for imports
sys.path.insert(0, str(Path(__file__).parent.parent))

from xaps_application_api import (
    _create_run_artifact,
    _calculate_material_plan,
    _run_artifacts,
    _get_active_run_artifact,
)
import pandas as pd


def test_material_plan_in_artifact_results():
    """Test Task 2.1: Material plan is included in artifact results."""
    
    # Setup test data
    config = {"Allow_Default_Masters": False, "Campaign_Serialization_Mode": "STANDARD"}
    campaigns = [
        {
            "campaign_id": "C001",
            "Campaign_ID": "C001",
            "grade": "STEEL_A",
            "release_status": "RELEASED",
            "total_mt": 100.0,
            "material_status": "OK",
            "material_shortages": {},
            "material_consumed": {"SKU_001": 50.0},
            "material_gross_requirements": {"SKU_001": 50.0},
            "material_structure_errors": [],
        }
    ]
    heat_schedule = pd.DataFrame({"job_id": ["J001"]})
    campaign_schedule = pd.DataFrame({"campaign_id": ["C001"]})
    capacity_map = pd.DataFrame({"resource_id": ["R001"]})
    
    # Create artifact
    run_id = _create_run_artifact(
        config=config,
        campaigns=campaigns,
        heat_schedule=heat_schedule,
        campaign_schedule=campaign_schedule,
        capacity_map_df=capacity_map,
        solver_status="OPTIMAL",
        solver_detail="CP_SAT",
        warnings=[],
        degraded_flags={},
        material_plan=None,  # Will be auto-computed
    )
    
    # Verify artifact exists
    assert run_id in _run_artifacts, "Artifact not stored"
    artifact = _run_artifacts[run_id]
    
    # Verify material_plan is in results (Task 2.1)
    assert "results" in artifact, "No results in artifact"
    assert "material_plan" in artifact["results"], "material_plan not in artifact results"
    
    material_plan = artifact["results"]["material_plan"]
    assert material_plan is not None, "material_plan is None"
    assert isinstance(material_plan, dict), "material_plan is not a dict"
    
    # Verify structure
    assert "run_id" in material_plan, "run_id not in material_plan"
    assert material_plan["run_id"] == run_id, "run_id mismatch in material_plan"
    assert "campaigns" in material_plan, "campaigns not in material_plan"
    assert "detail_level" in material_plan, "detail_level not in material_plan"
    assert material_plan["detail_level"] == "campaign", "detail_level should be 'campaign'"
    assert "summary" in material_plan, "summary not in material_plan"
    
    print("✓ Test passed: material_plan is included in artifact results")


def test_material_plan_computed_with_run_id():
    """Test Task 2.2: Material plan is computed with proper run_id."""
    
    config = {"Allow_Default_Masters": False}
    campaigns = [
        {
            "campaign_id": "C002",
            "grade": "STEEL_B",
            "release_status": "RELEASED",
            "total_mt": 50.0,
            "material_status": "OK",
            "material_shortages": {"SKU_002": 10.0},
            "material_consumed": {},
            "material_gross_requirements": {},
            "material_structure_errors": [],
        }
    ]
    
    # Create artifact (material plan will be auto-computed inside _create_run_artifact)
    run_id = _create_run_artifact(
        config=config,
        campaigns=campaigns,
        heat_schedule=pd.DataFrame(),
        campaign_schedule=pd.DataFrame(),
        capacity_map_df=pd.DataFrame(),
        solver_status="OPTIMAL",
        solver_detail="CP_SAT",
        material_plan=None,  # Trigger auto-computation
    )
    
    artifact = _run_artifacts[run_id]
    material_plan = artifact["results"]["material_plan"]
    
    # Verify run_id is set in material_plan (Task 2.2)
    assert material_plan["run_id"] == run_id, "Material plan does not have correct run_id"
    
    # Verify campaigns data is present with shortages
    assert len(material_plan["campaigns"]) > 0, "No campaign data in material_plan"
    camp_data = material_plan["campaigns"][0]
    assert camp_data["campaign_id"] == "C002", "Campaign ID mismatch"
    assert camp_data["shortage_qty"] == 10.0, "Shortage not captured"
    
    print("✓ Test passed: material_plan is computed with proper run_id")


def test_material_plan_not_dependent_on_state():
    """Test Task 2.2: Material plan is primary storage, not dependent on _state."""
    
    config = {}
    campaigns = [{"campaign_id": "C003", "material_shortages": {}}]
    
    # Create artifact without passing material_plan explicitly
    run_id = _create_run_artifact(
        config=config,
        campaigns=campaigns,
        heat_schedule=pd.DataFrame(),
        campaign_schedule=pd.DataFrame(),
        capacity_map_df=pd.DataFrame(),
        solver_status="OPTIMAL",
        solver_detail="CP_SAT",
    )
    
    # Verify artifact has its own material_plan (not dependent on _state)
    artifact = _run_artifacts[run_id]
    artifact_material_plan = artifact["results"]["material_plan"]
    
    # The artifact material_plan should be a complete dict, not None or empty
    assert artifact_material_plan is not None, "Artifact material_plan is None"
    assert isinstance(artifact_material_plan, dict), "Artifact material_plan is not dict"
    assert "run_id" in artifact_material_plan, "Artifact material_plan missing run_id"
    
    # Verify it matches the run_id
    assert artifact_material_plan["run_id"] == run_id, "Material plan run_id doesn't match artifact run_id"
    
    print("✓ Test passed: material_plan is first-class artifact result, not dependent on _state")


if __name__ == "__main__":
    print("Running material plan artifact tests...")
    test_material_plan_in_artifact_results()
    test_material_plan_computed_with_run_id()
    test_material_plan_not_dependent_on_state()
    print("\n✅ All tests passed!")
