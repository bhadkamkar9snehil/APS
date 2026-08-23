# SO → PO → Campaign Summary

**Status:** current canonical summary  
**Re-baselined:** 23-Aug-2026

- SO item = customer demand.
- Qualified finished-goods coverage is evaluated before deriving new MTO manufacturing quantity.
- MTO PO = internally required finished-product manufacturing quantity for one SO-item manufacturing requirement after qualified FG coverage.
- MTS PO = stock-replenishment manufacturing requirement; it does not require a fake SO.
- PO is not the aggregation container for unrelated customer demand.
- Campaign is the first intentional manufacturing aggregation/clubbing layer.
- Campaign/Heat/Rolling/route-operation/WO allocations preserve PO/SO quantity, customer and service-date lineage.
- Material shortages do **not** reduce the manufacturing requirement. Recursive BOM/time-phased material planning exposes upstream manufacturing need, late supply or explicit shortfall.
- Planning is not restricted to material in inventory now: authoritative future incoming, committed/released WIP and APS-planned internal receipts may satisfy later operations.
- Each PO retains its own quantity-aware service obligation even when physical production is shared.
- Resource assignment is not demand identity. A valid alternate-resource redispatch changes the schedule/Plan Version assignment, not the SO/PO/Campaign/Heat identity.

See [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md) for the complete semantic model and [`APS_SO_PO_Campaign_Service_Date_Examples.md`](APS_SO_PO_Campaign_Service_Date_Examples.md) for worked examples.
