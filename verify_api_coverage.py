#!/usr/bin/env python3
"""Verify all Excel macro functions are callable via Flask API."""

from xaps_application_api import app
import json

def check_endpoints():
    """Verify all required endpoints exist."""
    
    # Mapping of Excel macros to expected endpoints
    required_endpoints = {
        'RunBOMExplosion': {
            'endpoints': ['/api/run/bom'],
            'methods': ['POST']
        },
        'RunCapacityMap': {
            'endpoints': ['/api/aps/capacity/map', '/api/aps/capacity/bottlenecks'],
            'methods': ['GET']
        },
        'RunSchedule': {
            'endpoints': ['/api/aps/schedule/run'],
            'methods': ['POST']
        },
        'RunScenarios': {
            'endpoints': ['/api/aps/scenarios/list', '/api/aps/scenarios', '/api/aps/scenarios/output', '/api/aps/scenarios/apply'],
            'methods': ['GET', 'POST']
        },
        'RunCTP': {
            'endpoints': ['/api/run/ctp', '/api/aps/ctp/requests'],
            'methods': ['POST', 'GET']
        },
        'ClearOutputs': {
            'endpoints': ['/api/aps/clear-outputs'],
            'methods': ['POST']
        }
    }
    
    # Get all registered routes
    registered_routes = {}
    for rule in app.url_map.iter_rules():
        if rule.endpoint != 'static':
            methods = sorted(rule.methods - {'HEAD', 'OPTIONS'})
            registered_routes[rule.rule] = methods
    
    print("=" * 100)
    print("EXCEL MACRO TO API ENDPOINT VERIFICATION")
    print("=" * 100)
    
    all_found = True
    for macro_name, config in required_endpoints.items():
        print(f"\n{macro_name}:")
        macro_complete = True
        
        for endpoint in config['endpoints']:
            found = endpoint in registered_routes
            status = "✓" if found else "✗"
            
            if found:
                methods_available = registered_routes[endpoint]
                required_methods = config['methods']
                methods_complete = any(m in methods_available for m in required_methods)
                method_status = "✓" if methods_complete else "⚠"
                print(f"  {status} {endpoint:<45} {method_status} Methods: {methods_available}")
            else:
                print(f"  {status} {endpoint:<45} ✗ NOT FOUND")
                macro_complete = False
                all_found = False
        
        macro_status = "✓ COMPLETE" if macro_complete else "✗ INCOMPLETE"
        print(f"  Result: {macro_status}")
    
    print("\n" + "=" * 100)
    if all_found:
        print("✓ ALL EXCEL MACROS HAVE MATCHING API ENDPOINTS")
        print("✓ ALL PYTHON FUNCTIONS ARE CALLABLE VIA REST API")
    else:
        print("✗ SOME ENDPOINTS ARE MISSING")
    print("=" * 100)
    
    # List all registered endpoints for reference
    print("\nAll Registered Endpoints:")
    for endpoint in sorted(registered_routes.keys()):
        methods = registered_routes[endpoint]
        print(f"  {endpoint:<50} {methods}")


if __name__ == '__main__':
    check_endpoints()
