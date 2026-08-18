# SO → PO → Campaign Summary

Canonical summary:

- SO item = customer demand.
- MTO PO = internally required finished-product manufacturing quantity for that SO item after qualified FG coverage.
- MTS PO = stock replenishment manufacturing requirement, no fake SO.
- PO is not an aggregation container for several unrelated SOs.
- Campaign is the first intentional aggregation/clubbing layer.
- Campaign/Heat/Rolling/WO allocations preserve PO/SO quantities and individual required-by dates.
- Material shortages do not reduce PO quantity; recursive BOM/material planning exposes upstream manufacturing needs and shortfalls.
- Each PO retains its own due date even when physical production is shared.
