"""
Regression tests for BOM persistence and Material plan grouping.

Tests cover:
1. BOM explosion results are persisted to artifact and cache
2. BOM GET endpoint returns persisted results
3. BOM and Material return semantically different payloads
4. Material plan uses canonical campaign material fields
5. Clear outputs removes both BOM and Material payloads
"""

import pytest
import sys
from pathlib import Path

# Import the API module directly
sys.path.insert(0, str(Path(__file__).parent.parent))
import xaps_application_api as api


@pytest.fixture
def client():
    """Flask test client with TESTING mode enabled."""
    api.app.config["TESTING"] = True
    with api.app.test_client() as c:
        yield c


@pytest.fixture(autouse=True)
def reset_state():
    """Reset API state before and after each test."""
    api._state["bom_explosion"] = None
    api._state["material_plan_data"] = None
    api._state["bom_explosion"] = None
    api._run_artifacts.clear()
    api._active_run_id = None
    yield
    api._run_artifacts.clear()
    api._active_run_id = None
    api._state["bom_explosion"] = None
    api._state["material_plan_data"] = None


def _make_minimal_load_all():
    """Create synthetic load_all() output for testing BOM."""
    import pandas as pd
    return {
        "sales_orders": pd.DataFrame({
            "SO_ID": ["SO-001", "SO-002"],
            "SKU_ID": ["FG-WR-001", "BIL-100"],
            "Customer": ["Cust-A", "Cust-B"],
            "Order_Qty_MT": [10.0, 20.0],
            "Delivery_Date": ["2026-04-10", "2026-04-15"],
            "Status": ["OPEN", "OPEN"],
        }),
        "bom": pd.DataFrame({
            "Parent_SKU": ["FG-WR-001", "BIL-100", "BIL-100"],
            "Child_SKU": ["BIL-100", "RM-SCRAP", "RM-LIME"],
            "Qty_Per": [1.0, 0.85, 0.05],
            "Effect_Yield": [1.0, 1.0, 1.0],
            "BOM_Level": [1, 2, 2],
        }),
        "inventory": pd.DataFrame({
            "SKU_ID": ["RM-SCRAP", "RM-LIME"],
            "Available_Qty": [5.0, 2.0],
        }),
        "config": {},
        "resources": pd.DataFrame(),
    }


class TestBomPersistence:
    """Test BOM explosion POST/GET persistence."""

    def test_get_bom_returns_404_before_run(self, client):
        """GET BOM should return 404 if no BOM has been run."""
        resp = client.get("/api/aps/bom/explosion")
        assert resp.status_code == 404
        data = resp.get_json()
        assert data.get("error") is True
        assert "BOM_NOT_RUN" in data.get("error_code", "")

    def test_post_bom_persists_to_state(self, client, monkeypatch):
        """POST BOM should persist payload to _state."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        assert resp.status_code == 200
        data = resp.get_json()

        # Verify response has expected keys
        assert "gross_bom" in data
        assert "net_bom" in data
        assert "structure_errors" in data
        assert "feasible" in data
        assert "rows" in data
        assert "run_id" in data

        # Verify persisted to _state
        assert api._state["bom_explosion"] is not None
        assert api._state["bom_explosion"]["gross_bom"] == data["gross_bom"]
        assert api._state["bom_explosion"]["net_bom"] == data["net_bom"]

    def test_post_bom_persists_to_artifact_when_active_run(self, client, monkeypatch):
        """POST BOM should persist to artifact if an active run exists."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # Create a synthetic active run artifact
        run_id = "test-run-001"
        api._run_artifacts[run_id] = {
            "run_id": run_id,
            "created_at": "2026-04-03T10:00:00",
            "results": {},
        }
        api._active_run_id = run_id

        resp = client.post("/api/run/bom")
        assert resp.status_code == 200

        # Verify persisted to artifact
        artifact = api._get_active_run_artifact()
        assert artifact is not None
        assert "bom_explosion" in artifact.get("results", {})
        assert artifact["results"]["bom_explosion"]["gross_bom"] is not None

    def test_get_bom_returns_persisted_after_post(self, client, monkeypatch):
        """GET BOM should return the persisted payload after POST."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # First POST the BOM
        post_resp = client.post("/api/run/bom")
        assert post_resp.status_code == 200
        post_data = post_resp.get_json()

        # Then GET it
        get_resp = client.get("/api/aps/bom/explosion")
        assert get_resp.status_code == 200
        get_data = get_resp.get_json()

        # Verify they match (except possibly run_id)
        assert get_data["gross_bom"] == post_data["gross_bom"]
        assert get_data["net_bom"] == post_data["net_bom"]
        assert get_data["structure_errors"] == post_data["structure_errors"]
        assert get_data["feasible"] == post_data["feasible"]

    def test_post_and_get_use_same_payload_shape(self, client, monkeypatch):
        """POST and GET BOM should use identical payload shape."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        post_data = resp.get_json()

        # Verify payload has required fields
        required_keys = {"gross_bom", "net_bom", "structure_errors", "feasible", "rows", "run_id"}
        assert set(post_data.keys()) >= required_keys

        # GET should have same structure
        get_resp = client.get("/api/aps/bom/explosion")
        get_data = get_resp.get_json()
        assert set(get_data.keys()) >= required_keys


class TestMaterialPlanPayload:
    """Test Material plan response includes grouped plant structure."""

    def test_material_plan_includes_inventory_before_after(self, client):
        """Material plan should include inventory_before and inventory_after per campaign."""
        # Build a synthetic campaign with inventory data
        campaign = {
            "campaign_id": "CMP-001",
            "grade": "GR-A",
            "release_status": "RELEASED",
            "total_coil_mt": 100.0,
            "material_status": "READY",
            "material_shortages": {},
            "material_consumed": {"BIL-100": 85.0, "RM-SCRAP": 72.25},
            "material_gross_requirements": {"BIL-100": 100.0, "RM-SCRAP": 85.0},
            "material_structure_errors": [],
            "inventory_before": {"RM-SCRAP": 10.0},
            "inventory_after": {"RM-SCRAP": 0.0},
        }

        result = api._calculate_material_plan([campaign])
        assert result is not None
        assert len(result["campaigns"]) == 1

        camp_data = result["campaigns"][0]
        assert "inventory_before" in camp_data
        assert "inventory_after" in camp_data
        assert camp_data["inventory_before"] == {"RM-SCRAP": 10.0}
        assert camp_data["inventory_after"] == {"RM-SCRAP": 0.0}

    def test_material_plan_returns_grouped_plants(self, client):
        """Material plan should return plants array grouped by plant location."""
        campaign = {
            "campaign_id": "CMP-002",
            "grade": "GR-B",
            "release_status": "RELEASED",
            "total_coil_mt": 50.0,
            "material_status": "READY",
            "material_shortages": {},
            "material_consumed": {"BIL-100": 42.5, "RM-SCRAP": 36.0, "RM-LIME": 2.5},
            "material_gross_requirements": {"BIL-100": 50.0, "RM-SCRAP": 42.5, "RM-LIME": 2.5},
            "material_structure_errors": [],
            "inventory_before": {},
            "inventory_after": {},
        }

        result = api._calculate_material_plan([campaign])
        camp_data = result["campaigns"][0]

        # Should have plants array
        assert "plants" in camp_data
        assert isinstance(camp_data["plants"], list)
        assert len(camp_data["plants"]) > 0

        # Each plant should have required structure
        for plant in camp_data["plants"]:
            assert "plant" in plant
            assert "required_qty" in plant
            assert "inventory_covered_qty" in plant
            assert "rows" in plant
            assert isinstance(plant["rows"], list)

            # Each row should have required fields
            for row in plant["rows"]:
                assert "material_sku" in row
                assert "material_type" in row
                assert "required_qty" in row
                assert "consumed" in row
                assert "status" in row

    def test_material_plan_uses_canonical_campaign_fields(self, client):
        """Material plan should read and preserve canonical campaign material fields."""
        campaign = {
            "campaign_id": "CMP-003",
            "grade": "GR-C",
            "release_status": "MATERIAL HOLD",
            "total_coil_mt": 75.0,
            "material_status": "SHORTAGE",
            "material_shortages": {"RM-SCRAP": 5.0},
            "material_consumed": {"BIL-100": 63.75, "RM-SCRAP": 54.0},
            "material_gross_requirements": {"BIL-100": 75.0, "RM-SCRAP": 63.75, "RM-LIME": 3.75},
            "material_structure_errors": [{"type": "CYCLE", "path": ["A", "B"]}],
            "inventory_before": {"RM-SCRAP": 10.0},
            "inventory_after": {"RM-SCRAP": 10.0},
        }

        result = api._calculate_material_plan([campaign])
        camp_data = result["campaigns"][0]

        # All canonical fields should be preserved
        assert camp_data["material_shortages"] == {"RM-SCRAP": 5.0}
        assert camp_data["material_consumed"] == {"BIL-100": 63.75, "RM-SCRAP": 54.0}
        assert camp_data["material_gross_requirements"]["RM-LIME"] == 3.75
        assert len(camp_data["material_structure_errors"]) == 1
        assert camp_data["inventory_before"]["RM-SCRAP"] == 10.0

    def test_material_plan_summary_includes_make_convert_qty(self, client):
        """Material plan summary should include Make / Convert Qty field."""
        campaign = {
            "campaign_id": "CMP-004",
            "grade": "GR-D",
            "release_status": "RELEASED",
            "total_coil_mt": 100.0,
            "material_status": "READY",
            "material_shortages": {},
            "material_consumed": {"BIL-100": 85.0, "RM-SCRAP": 72.25},
            "material_gross_requirements": {"BIL-100": 100.0, "RM-SCRAP": 85.0},
            "material_structure_errors": [],
            "inventory_before": {},
            "inventory_after": {},
        }

        result = api._calculate_material_plan([campaign])

        assert "summary" in result
        summary = result["summary"]
        assert "Make / Convert Qty" in summary
        assert isinstance(summary["Make / Convert Qty"], float)


class TestBomMaterialSeparation:
    """Test that BOM and Material payloads remain semantically separate."""

    def test_bom_and_material_return_different_payloads(self, client, monkeypatch):
        """BOM and Material should not return identical payloads for same planning state."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # Run BOM
        bom_resp = client.post("/api/run/bom")
        bom_data = bom_resp.get_json()

        # Run schedule (which generates material plan)
        api._state["material_plan_data"] = {
            "summary": {"Campaigns": 1},
            "campaigns": [
                {
                    "campaign_id": "CMP-005",
                    "plants": [{"plant": "SMS", "rows": []}],
                }
            ],
        }

        # Get material plan
        material_resp = client.get("/api/aps/material/plan")
        material_data = material_resp.get_json()

        # They should be structurally different
        assert "campaigns" in material_data  # Material plan has campaigns
        assert "gross_bom" in bom_data  # BOM has gross_bom
        assert "gross_bom" not in material_data  # Material plan doesn't have bom keys
        assert "campaigns" not in bom_data  # BOM doesn't have campaigns key

    def test_bom_payload_has_no_campaign_sequencing(self, client, monkeypatch):
        """BOM payload should be demand-network-level, not campaign-sequenced."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()

        # BOM should not have campaign_id or release_status
        for row in data.get("net_bom", []):
            assert "campaign_id" not in row
            assert "release_status" not in row

    def test_material_payload_has_campaign_sequencing(self, client):
        """Material payload should be campaign-sequenced, not demand-network-level."""
        campaign = {
            "campaign_id": "CMP-006",
            "release_status": "RELEASED",
            "grade": "GR-E",
            "total_coil_mt": 100.0,
            "material_status": "READY",
            "material_shortages": {},
            "material_consumed": {"BIL-100": 85.0},
            "material_gross_requirements": {"BIL-100": 100.0},
            "material_structure_errors": [],
            "inventory_before": {},
            "inventory_after": {},
        }

        result = api._calculate_material_plan([campaign])
        camp_data = result["campaigns"][0]

        # Material should have campaign info
        assert "campaign_id" in camp_data
        assert "release_status" in camp_data
        assert camp_data["campaign_id"] == "CMP-006"


class TestClearOutputs:
    """Test that clear-outputs removes both BOM and Material payloads."""

    def test_clear_removes_bom_from_state(self, client, monkeypatch):
        """POST clear-outputs should clear _state["bom_explosion"]."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # First populate BOM
        client.post("/api/run/bom")
        assert api._state["bom_explosion"] is not None

        # Then clear
        client.post("/api/aps/clear-outputs")
        assert api._state["bom_explosion"] is None

    def test_clear_removes_material_from_state(self, client):
        """POST clear-outputs should clear _state["material_plan_data"]."""
        # Populate material plan data
        api._state["material_plan_data"] = {"summary": {"Campaigns": 1}, "campaigns": []}
        assert api._state["material_plan_data"] is not None

        # Clear
        client.post("/api/aps/clear-outputs")
        assert api._state["material_plan_data"] is None

    def test_clear_removes_material_from_artifact(self, client):
        """POST clear-outputs should clear artifact material_plan."""
        run_id = "test-run-002"
        api._run_artifacts[run_id] = {
            "run_id": run_id,
            "results": {
                "material_plan": {"summary": {}, "campaigns": []},
            },
        }
        api._active_run_id = run_id

        # Verify it's there
        artifact = api._get_active_run_artifact()
        assert artifact["results"]["material_plan"] is not None

        # Clear
        client.post("/api/aps/clear-outputs")

        # Verify artifact is gone
        assert api._active_run_id is None
        assert len(api._run_artifacts) == 0

    def test_clear_removes_bom_from_artifact(self, client):
        """POST clear-outputs should clear artifact bom_explosion."""
        run_id = "test-run-003"
        api._run_artifacts[run_id] = {
            "run_id": run_id,
            "results": {
                "bom_explosion": {"gross_bom": [], "net_bom": []},
            },
        }
        api._active_run_id = run_id

        # Clear
        client.post("/api/aps/clear-outputs")

        # Verify artifact is gone
        assert api._active_run_id is None
        assert len(api._run_artifacts) == 0

    def test_get_bom_returns_404_after_clear(self, client, monkeypatch):
        """After clear-outputs, GET BOM should return 404."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # POST BOM
        client.post("/api/run/bom")
        assert client.get("/api/aps/bom/explosion").status_code == 200

        # Clear
        client.post("/api/aps/clear-outputs")

        # GET should now 404
        resp = client.get("/api/aps/bom/explosion")
        assert resp.status_code == 404


class TestBomGroupedView:
    """Test BOM explosion grouped view (Plant → Material_Type hierarchy)."""

    def test_post_bom_returns_grouped_bom(self, client, monkeypatch):
        """POST /api/run/bom should return grouped_bom in response."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        assert resp.status_code == 200
        data = resp.get_json()

        assert "grouped_bom" in data
        assert isinstance(data["grouped_bom"], list)
        assert "summary" in data
        assert isinstance(data["summary"], dict)

    def test_grouped_bom_has_plant_structure(self, client, monkeypatch):
        """Grouped BOM should have Plant → Material_Type → rows structure."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()
        grouped_bom = data["grouped_bom"]

        assert len(grouped_bom) > 0
        for plant_group in grouped_bom:
            assert "plant" in plant_group
            assert "material_types" in plant_group
            assert isinstance(plant_group["material_types"], list)
            assert "gross_req" in plant_group
            assert "net_req" in plant_group
            assert "row_count" in plant_group

    def test_grouped_bom_has_material_type_subgroups(self, client, monkeypatch):
        """Each plant should have Material_Type subgroups with rows."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()

        # Check at least one plant has material_types
        for plant_group in data["grouped_bom"]:
            if plant_group["material_types"]:
                for mat_type in plant_group["material_types"]:
                    assert "material_type" in mat_type
                    assert "rows" in mat_type
                    assert isinstance(mat_type["rows"], list)
                    if mat_type["rows"]:
                        # Check row fields
                        row = mat_type["rows"][0]
                        required_fields = {
                            "plant", "stage", "material_type", "material_category",
                            "parent_skus", "sku_id", "sku_name", "bom_level",
                            "gross_req", "available_before", "covered_by_stock",
                            "produced_qty", "net_req", "status", "flow_type",
                        }
                        assert required_fields.issubset(set(row.keys()))
                break

    def test_grouped_bom_has_summary(self, client, monkeypatch):
        """BOM response should include summary with line counts and totals."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()
        summary = data["summary"]

        required_keys = {
            "total_sku_lines", "short_lines", "partial_lines", "covered_lines",
            "byproduct_lines", "total_gross_req", "total_net_req",
            "total_covered_by_stock", "total_produced_qty",
        }
        assert required_keys.issubset(set(summary.keys()))

        # Verify counts are non-negative
        assert summary["total_sku_lines"] >= 0
        assert summary["short_lines"] >= 0
        assert summary["partial_lines"] >= 0

    def test_grouped_bom_status_values(self, client, monkeypatch):
        """BOM rows should have valid status values."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()

        valid_statuses = {"COVERED", "PARTIAL SHORT", "SHORT", "BYPRODUCT"}
        for plant_group in data["grouped_bom"]:
            for mat_type in plant_group["material_types"]:
                for row in mat_type["rows"]:
                    assert row["status"] in valid_statuses, f"Invalid status: {row['status']}"

    def test_grouped_bom_summary_counts_match_rows(self, client, monkeypatch):
        """Summary line counts should match actual grouped row counts."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        resp = client.post("/api/run/bom")
        data = resp.get_json()
        summary = data["summary"]
        grouped = data["grouped_bom"]

        # Count all rows from grouped structure
        total_rows = 0
        short_rows = 0
        partial_rows = 0
        covered_rows = 0
        byproduct_rows = 0

        for plant_group in grouped:
            for mat_type in plant_group["material_types"]:
                for row in mat_type["rows"]:
                    total_rows += 1
                    if row["status"] == "SHORT":
                        short_rows += 1
                    elif row["status"] == "PARTIAL SHORT":
                        partial_rows += 1
                    elif row["status"] == "COVERED":
                        covered_rows += 1
                    elif row["status"] == "BYPRODUCT":
                        byproduct_rows += 1

        assert summary["total_sku_lines"] == total_rows
        assert summary["short_lines"] == short_rows
        assert summary["partial_lines"] == partial_rows
        assert summary["covered_lines"] == covered_rows
        assert summary["byproduct_lines"] == byproduct_rows

    def test_get_bom_explosion_includes_grouped_bom(self, client, monkeypatch):
        """GET /api/aps/bom/explosion should return grouped_bom from artifact."""
        monkeypatch.setattr(api, "_load_all", lambda: _make_minimal_load_all())

        # POST first
        post_resp = client.post("/api/run/bom")
        post_data = post_resp.get_json()

        # GET should return same grouped_bom
        get_resp = client.get("/api/aps/bom/explosion")
        assert get_resp.status_code == 200
        get_data = get_resp.get_json()

        assert "grouped_bom" in get_data
        assert get_data["grouped_bom"] == post_data["grouped_bom"]
        assert get_data["summary"] == post_data["summary"]
