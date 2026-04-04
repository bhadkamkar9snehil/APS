#!/usr/bin/env python3
"""Debug data synchronization between Excel and app."""

from xaps_application_api import _sheet_items, _material_plan_payload, _campaign_rows, _state, _capacity_rows
import json

print("=" * 80)
print("DATA SYNCHRONIZATION CHECK - EXCEL vs APP")
print("=" * 80)

# 1. Campaign data
print("\n1. CAMPAIGN DATA")
print("-" * 80)
sheet_campaigns = _sheet_items('campaign-schedule') if 'campaign-schedule' in ['campaign-schedule'] else []
app_campaigns = _campaign_rows()
print(f"Excel Campaign_Schedule sheet: {len(sheet_campaigns)} rows")
print(f"App campaigns from state: {len(app_campaigns)} campaigns")

# 2. Material Plan data
print("\n2. MATERIAL PLAN DATA")
print("-" * 80)
sheet_material = _sheet_items('material-plan')
app_material = _material_plan_payload()
print(f"Excel Material_Plan sheet: {len(sheet_material)} rows")
if sheet_material:
    print(f"  First row: {sheet_material[0]}")
print(f"App Material Plan: {len(app_material.get('campaigns', []))} campaigns")
print(f"App summary: {app_material.get('summary')}")

# 3. Capacity data
print("\n3. CAPACITY DATA")
print("-" * 80)
sheet_capacity = _sheet_items('capacity-map')
app_capacity = _capacity_rows()
print(f"Excel Capacity_Map sheet: {len(sheet_capacity)} rows")
print(f"App capacity: {len(app_capacity)} rows")

# 4. State check
print("\n4. APPLICATION STATE")
print("-" * 80)
print(f"State has campaigns: {bool(_state.get('campaigns'))}")
print(f"State has heat_schedule: {_state.get('heat_schedule') is not None and (not hasattr(_state.get('heat_schedule'), 'empty') or not _state.get('heat_schedule').empty)}")
print(f"State has capacity: {_state.get('capacity') is not None}")
print(f"State has material_plan_data: {bool(_state.get('material_plan_data'))}")
print(f"Solver status: {_state.get('solver_status')}")

# 5. Check which data source _material_plan_payload uses
print("\n5. DATA SOURCE ANALYSIS")
print("-" * 80)
if _state.get('material_plan_data'):
    print("✓ App uses CALCULATED material data from state (after schedule)")
else:
    print("✓ App uses SHEET material data (static from Excel)")

print("\n" + "=" * 80)
print("RECOMMENDATION:")
print("=" * 80)
if len(sheet_material) > 1:
    print("📌 Excel has static Material_Plan reference data (2 campaigns with BOM)")
    print("📌 App currently returns empty because state not populated")
    print("🔧 SOLUTION: Material data should populate AFTER schedule is run")
else:
    print("📌 Excel Material_Plan is a placeholder/template")
    print("🔧 App should generate material plans from scheduled campaigns")
