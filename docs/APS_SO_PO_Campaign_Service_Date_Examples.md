# APS SO → PO → Campaign → Due-Date Examples

**Status:** current canonical examples  
**Re-baselined:** 23-Aug-2026

These examples illustrate the current demand/manufacturing causality described in [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md). They are examples, not a substitute for the executable acceptance matrix in [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md).

## Example 1 — fully stock covered

```text
SO-100 item 10
100 MT TMT 16 mm
Customer required 10-Sep
Qualified FG = 100 MT

Manufacturing requirement = 0
New MTO PO = none
Campaign = none
Manufacturing WO = none
```

The Sales Order remains visible as customer demand fulfilled from qualified inventory. Stock coverage does not require a fake manufacturing PO simply to preserve a diagram shape.

## Example 2 — partial stock coverage

```text
SO-101 item 10
100 MT TMT 16 mm
Customer required 10-Sep
Qualified FG = 30 MT

MTO PO:
MTO-SO-101-10
70 MT
SalesOrderId -> SO-101 item 10
```

`ProductionRequiredByUtc` is derived from the applicable configured post-production/quality/packing/dispatch allowance. If no effective allowance is configured, the customer/confirmed service date remains the fallback basis.

The PO remains one 70 MT manufacturing requirement. Missing billet/raw material does **not** reduce the PO quantity.

## Example 3 — two SOs can share a campaign but not a PO

```text
SO-A item 10 -> PO-A 40 MT, due 10-Sep
SO-B item 20 -> PO-B 60 MT, due 12-Sep
```

If grade/route/section/customer rules permit:

```text
PO-A 40 MT ---\
               > Campaign C-21 100 MT
PO-B 60 MT ---/
```

Campaign allocations retain:

```text
C-21 -> PO-A 40 MT due 10-Sep
C-21 -> PO-B 60 MT due 12-Sep
```

Shared heats/rolling/route operations retain allocation lineage rather than converting the Campaign into the demand identity.

## Example 4 — widely separated service dates

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 28-Sep
```

Metallurgical compatibility alone does not mean they should share a Campaign.

Candidate optimization can compare:

- setup/transition savings from combining;
- allocation-level service/tardiness;
- early-production/inventory cost for later demand;
- campaign size and heat utilization;
- caster/RM/downstream feasibility;
- due-date spread;
- baseline/replan stability.

The selected Campaign is an optimization result, not a sort-and-fill rule.

## Example 5 — one campaign, different PO service dates

Assume one Campaign contains:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 18-Sep
```

Wrong simplification:

```text
Campaign earliest due = 10-Sep
therefore all 100 MT is treated as customer-due on 10-Sep
```

Current model:

```text
Campaign summary:
  earliest allocation due = 10-Sep
  latest allocation due   = 18-Sep

Service obligations:
  PO-A 40 MT due 10-Sep
  PO-B 60 MT due 18-Sep
```

Physical heats/rolling blocks may be shared, while service/tardiness remains quantity-aware at the allocation grain.

## Example 6 — billet absent today, internally manufacturable later

```text
SO-C -> PO-C 100 MT bar due 25-Sep
FG = 0
Billet = 0 today
```

If the finished product and required billet are internally manufacturable:

```text
PO-C remains 100 MT
recursive material planning derives billet requirement
SMS/CCM internal production is planned
billet planned receipt = 22-Sep
rolling is scheduled after feasible billet availability
```

The plan is not restricted to opening inventory. Future internal production can satisfy future consumption.

If the upstream production cannot arrive by the downstream need time, APS keeps the requirement visible and reports late supply/shortfall rather than deleting the work.

## Example 7 — raw-material shortfall

```text
PO-D finished demand
 -> billet
 -> liquid steel
 -> charge/raw-material requirement
 -> leaf material requirement 2,000 MT

Qualified available/known supply by need time = 1,500 MT
```

APS shows:

```text
Requirement 2,000 MT
Covered     1,500 MT
Shortfall     500 MT
```

The finished manufacturing requirement remains visible. APS does not invent a procurement recommendation.

## Example 8 — future known billet while SMS is unavailable

```text
Rolling need 60 MT on 10-Sep
Opening billet 0 MT
Authoritative incoming billet 65 MT on 09-Sep
SMS unavailable in scenario
```

The 09-Sep receipt may satisfy the 10-Sep rolling need if grade/section/quality/location/thermal/route qualification permits it.

APS does not create an unnecessary replacement heat merely because opening inventory was zero.

## Example 9 — month-long progressive material availability

```text
01-Sep opening billet          0 MT
04-Sep internal cast receipt  65 MT
09-Sep internal cast receipt  65 MT
15-Sep internal cast receipt  65 MT

rolling need:
05-Sep 60 MT
10-Sep 60 MT
16-Sep 60 MT
```

A valid month-long Campaign can consume those receipts progressively. APS must not require all 180 MT to exist at campaign creation time.

## Example 10 — MTS

MTS remains independent from Sales Order lineage:

```text
Stock target 500 MT
Projected qualified FG 320 MT
Replenishment policy requires manufacture 180 MT

MTS PO = 180 MT
```

The MTS PO may share a Campaign with compatible MTO POs when policy allows it, but its stock objective remains distinct from customer-due-date service.

## Example 11 — committed work is not silently resized

```text
SO open quantity was 100 MT
MTO PO 100 MT became firm/released
later authoritative FG increases by 30 MT
```

The existing committed manufacturing is not silently rewritten to 70 MT by a later coverage change. The difference becomes explicit reconciliation/replan evidence according to lifecycle policy.

## Example 12 — resource flexibility does not change demand identity

```text
PO-A -> Campaign C-21 -> Heat H-104
H-104 planned CCM-1
H-104 remains eligible for CCM-2 before commitment
```

A valid redispatch from CCM-1 to CCM-2 changes the resource assignment/Plan Version decision, **not** the Heat, Campaign, PO or SO identity. The complete planned→committed→actual resource lifecycle is the current #16 work area.
