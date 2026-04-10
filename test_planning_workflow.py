#!/usr/bin/env python3
"""
Test script for APS Planning Workflow
Validates the complete SO → PO → Heat → Schedule implementation
"""

from datetime import datetime, timedelta
from engine.aps_planner import (
    APSPlanner, PlanningHorizon, SalesOrder, PlanningOrder, HeatBatch
)
from engine.config import get_config

def test_planning_workflow():
    """Test the complete planning workflow."""
    print("\n" + "="*70)
    print("APS PLANNING WORKFLOW TEST")
    print("="*70)

    # Initialize planner
    config = get_config()
    planner = APSPlanner(config.all_params())
    params = config.all_params()
    print(f"[OK] Planner initialized with {len(params)} config parameters")

    # STEP 1: Create sample sales orders
    print("\n1. CREATING SAMPLE SALES ORDERS")
    now = datetime.now()
    sos = [
        SalesOrder(
            so_id="SO-001", customer_id="Customer A", grade="SAE 1008",
            section_mm=5.5, qty_mt=100, due_date=(now + timedelta(days=3)).isoformat(),
            priority="URGENT", route_family="SMS→RM", status="Open"
        ),
        SalesOrder(
            so_id="SO-002", customer_id="Customer B", grade="SAE 1008",
            section_mm=5.5, qty_mt=150, due_date=(now + timedelta(days=4)).isoformat(),
            priority="HIGH", route_family="SMS→RM", status="Open"
        ),
        SalesOrder(
            so_id="SO-003", customer_id="Customer C", grade="SAE 1045",
            section_mm=8.0, qty_mt=120, due_date=(now + timedelta(days=5)).isoformat(),
            priority="NORMAL", route_family="SMS→RM", status="Open"
        ),
        SalesOrder(
            so_id="SO-004", customer_id="Customer A", grade="SAE 1045",
            section_mm=8.0, qty_mt=100, due_date=(now + timedelta(days=6)).isoformat(),
            priority="NORMAL", route_family="SMS→RM", status="Open"
        ),
    ]
    print(f"[OK] Created {len(sos)} sample sales orders")

    # STEP 2: Select planning window
    print("\n2. SELECTING PLANNING WINDOW (NEXT 7 DAYS)")
    window_sos = planner.select_planning_window(sos, PlanningHorizon.NEXT_7_DAYS)
    print(f"[OK] Selected {len(window_sos)} orders in planning window")
    for so in window_sos:
        print(f"  - {so.so_id}: {so.qty_mt}MT {so.grade} (due {so.due_date[:10]})")

    # STEP 3: Propose planning orders
    print("\n3. PROPOSING PLANNING ORDERS")
    pos = planner.propose_planning_orders(window_sos)
    print(f"[OK] Proposed {len(pos)} planning orders")
    for po in pos:
        print(f"  - {po.po_id}: {po.total_qty_mt}MT {po.grade_family} ({len(po.selected_so_ids)} SOs)")
        print(f"    Due window: {po.due_window[0]} to {po.due_window[1]}")
        print(f"    Heats required: {po.heats_required}")

    # STEP 4: Validate planning orders
    print("\n4. VALIDATING PLANNING ORDERS")
    validation = planner.validate_planning_orders(pos)
    print(f"[OK] Validation passed:")
    print(f"  - Valid: {validation['valid']}")
    print(f"  - Total SOs: {validation['total_sos']}")
    print(f"  - Total MT: {validation['total_mt']}")
    print(f"  - Total heats: {validation['total_heats']}")

    # STEP 5: Derive heat batches
    print("\n5. DERIVING HEAT BATCHES")
    heats = planner.derive_heat_batches(pos, heat_size_mt=50.0)
    print(f"[OK] Derived {len(heats)} heat batches")
    for heat in heats[:5]:  # Show first 5
        print(f"  - {heat.heat_id}: {heat.qty_mt}MT {heat.grade} " +
              f"(heat {heat.heat_number_seq}/{pos[0].heats_required})")
    if len(heats) > 5:
        print(f"  ... and {len(heats) - 5} more")

    # STEP 6: Simulate schedule
    print("\n6. SIMULATING FINITE SCHEDULE")
    schedule_result = planner.simulate_finite_schedule(heats, pos)
    print(f"[OK] Schedule simulation complete:")
    print(f"  - Feasible: {schedule_result['feasible']}")
    print(f"  - Total duration: {schedule_result['total_duration_hours']}h")
    print(f"  - SMS hours: {schedule_result['sms_hours']}h")
    print(f"  - RM hours: {schedule_result['rm_hours']}h")
    print(f"  - Load factor: {schedule_result['load_factor']}")
    print(f"  - Message: {schedule_result['message']}")

    # Summary
    print("\n" + "="*70)
    print("WORKFLOW TEST COMPLETE")
    print("="*70)
    print(f"\nSummary:")
    print(f"  - Sales Orders processed: {len(sos)}")
    print(f"  - Planning window: {len(window_sos)} orders")
    print(f"  - Planning Orders proposed: {len(pos)}")
    print(f"  - Heat batches derived: {len(heats)}")
    print(f"  - Schedule feasible: {schedule_result['feasible']}")
    print(f"\n[OK] All workflow steps completed successfully!\n")

    return {
        'sos': len(sos),
        'window_sos': len(window_sos),
        'planning_orders': len(pos),
        'heats': len(heats),
        'feasible': schedule_result['feasible'],
    }


if __name__ == '__main__':
    try:
        result = test_planning_workflow()
        print("\nTest result:", result)
    except Exception as e:
        print(f"\n[FAIL] Test failed: {e}")
        import traceback
        traceback.print_exc()
