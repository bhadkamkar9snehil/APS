#!/usr/bin/env python3
"""
Integration test for APS Planning UI
Tests the complete workflow: Order Pool -> Planning Board -> Heat Builder -> Scheduler -> Release
"""
import requests
import json

API_BASE = "http://localhost:5000"

def test_workflow():
    """Test complete planning workflow via API."""
    print("\n" + "="*70)
    print("APS PLANNING WORKFLOW — UI INTEGRATION TEST")
    print("="*70)

    # STEP 1: Get Order Pool
    print("\n[1/5] Order Pool - Fetch all open sales orders")
    resp = requests.get(f"{API_BASE}/api/aps/planning/orders/pool")
    assert resp.status_code == 200, f"Pool failed: {resp.status_code}"
    pool = resp.json()
    total_orders = pool['total_orders']
    print(f"  SUCCESS: {total_orders} open orders in pool")

    # STEP 2: Select Planning Window
    print("\n[2/5] Planning Board - Select 7-day window")
    resp = requests.post(
        f"{API_BASE}/api/aps/planning/window/select",
        json={"days": 7}
    )
    assert resp.status_code == 200, f"Window select failed: {resp.status_code}"
    window = resp.json()
    candidates = window['candidate_count']
    print(f"  SUCCESS: {candidates} SOs selected in 7-day window")

    # STEP 3: Propose Planning Orders
    print("\n[3/5] Planning Board - Propose manufacturing lots")
    resp = requests.post(
        f"{API_BASE}/api/aps/planning/orders/propose",
        json={"days": 7}
    )
    assert resp.status_code == 200, f"Propose failed: {resp.status_code}"
    propose = resp.json()
    po_count = propose['po_count']
    total_mt = propose['validation']['total_mt']
    planning_orders = propose['planning_orders']
    print(f"  SUCCESS: {po_count} POs proposed, {total_mt:.0f} MT total")
    print(f"  POs created:")
    for i, po in enumerate(planning_orders[:3]):
        print(f"    - {po['po_id']}: {po['total_qty_mt']:.0f}MT {po['grade_family']} ({len(po['selected_so_ids'])} SOs, {po['heats_required']} heats)")
    if po_count > 3:
        print(f"    ... and {po_count - 3} more")

    # STEP 4: Derive Heat Batches
    print("\n[4/5] Heat Builder - Derive heat requirements")
    resp = requests.post(
        f"{API_BASE}/api/aps/planning/heats/derive",
        json={"planning_orders": planning_orders}
    )
    assert resp.status_code == 200, f"Derive heats failed: {resp.status_code}"
    heats_resp = resp.json()
    heat_count = heats_resp['total_heats']
    heats_mt = heats_resp['total_mt']
    heats = heats_resp['heats']
    print(f"  SUCCESS: {heat_count} heats derived, {heats_mt:.0f} MT total")
    print(f"  Heats created:")
    for i, heat in enumerate(heats[:3]):
        print(f"    - {heat['heat_id']}: {heat['qty_mt']:.1f}MT {heat['grade']} (seq {heat['heat_number_seq']})")
    if heat_count > 3:
        print(f"    ... and {heat_count - 3} more")

    # STEP 5: Simulate Schedule
    print("\n[5/5] Finite Scheduler - Check feasibility")
    resp = requests.post(
        f"{API_BASE}/api/aps/planning/simulate",
        json={"heat_batches": heats}
    )
    assert resp.status_code == 200, f"Simulate failed: {resp.status_code}"
    schedule = resp.json()
    feasible = "FEASIBLE" if schedule['feasible'] else "INFEASIBLE"
    duration = schedule['total_duration_hours']
    sms_h = schedule['sms_hours']
    rm_h = schedule['rm_hours']
    load_factor = schedule['load_factor']
    print(f"  SUCCESS: Schedule {feasible}")
    print(f"    - Duration: {duration}h")
    print(f"    - SMS: {sms_h}h, RM: {rm_h}h")
    print(f"    - Load Factor: {load_factor}")
    print(f"    - Message: {schedule['message']}")

    # Summary
    print("\n" + "="*70)
    print("WORKFLOW TEST COMPLETE ✓")
    print("="*70)
    print(f"\nMetrics:")
    print(f"  Total orders in pool: {total_orders}")
    print(f"  Orders in 7-day window: {candidates}")
    print(f"  Planning orders created: {po_count}")
    print(f"  Heat batches derived: {heat_count}")
    print(f"  Schedule feasible: {schedule['feasible']}")
    print(f"\n✓ All 5 workflow steps completed successfully!\n")

    return {
        'pool_orders': total_orders,
        'window_candidates': candidates,
        'planning_orders': po_count,
        'heats': heat_count,
        'feasible': schedule['feasible'],
        'duration_hours': duration,
    }


if __name__ == '__main__':
    try:
        result = test_workflow()
        print(f"Result: {json.dumps(result, indent=2)}")
    except AssertionError as e:
        print(f"\n[FAIL] {e}")
        exit(1)
    except Exception as e:
        print(f"\n[ERROR] {e}")
        import traceback
        traceback.print_exc()
        exit(1)
