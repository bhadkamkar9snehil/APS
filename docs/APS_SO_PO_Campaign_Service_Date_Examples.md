# APS SO → PO → Campaign → Due-Date Examples

Status: **current design examples**

## Example 1 — fully stock covered

```text
SO-100 item 10
100 MT TMT 16 mm
Required 10-Sep
Qualified FG = 100 MT

Manufacturing requirement = 0
MTO PO = none
Campaign = none
Manufacturing WO = none
```

The Sales Order remains visible as demand fulfilled from inventory.

## Example 2 — partial stock coverage

```text
SO-101 item 10
100 MT TMT 16 mm
Required 10-Sep
Qualified FG = 30 MT

MTO PO:
PO-MTO-101-10
70 MT
ProductionRequiredBy = 10-Sep (until a post-production allowance is configured)
SalesOrderId -> SO-101 item 10
```

The PO remains one manufacturing requirement. Its billet/raw-material shortage does not reduce its quantity.

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

If the campaign produces one or more shared heats, heat-to-PO allocation keeps the quantity lineage.

## Example 4 — widely separated dates

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 28-Sep
```

They may be metallurgically compatible, but that does not automatically mean they should be one campaign.

Candidate optimization should compare:
- setup/transition savings from combining;
- early-production/inventory cost for PO-B;
- campaign size/heat utilization;
- available caster/RM capacity;
- due-date/service risk;
- stability.

The final choice is an optimization result.

## Example 5 — one campaign, different PO service dates

Assume one campaign contains:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 18-Sep
```

Wrong simplification:

```text
Campaign due = 10-Sep
therefore all 100 MT treated as due 10-Sep
```

Target behavior:

```text
Campaign:
  EarliestRequired = 10-Sep
  LatestRequired = 18-Sep

Allocations:
  PO-A 40 MT due 10-Sep
  PO-B 60 MT due 18-Sep
```

Physical heats/rolling blocks may be shared, but demand service/tardiness remains measured per PO allocation.

## Example 6 — billet absent today

```text
SO-C -> PO-C 100 MT coil due 25-Sep
FG = 0
Billet = 0 today
```

If the finished material and billet are internally manufacturable:

```text
PO-C remains 100 MT
recursive BOM says billet must be made
SMS/CCM internal production requirement is created
billet planned receipt = 22-Sep
RM scheduled after billet receipt
```

No reduction of PO quantity occurs merely because billet does not exist at plan creation time.

## Example 7 — raw-material shortfall

```text
PO-D finished demand
 -> billet
 -> liquid steel
 -> hot metal
 -> ore requirement 2,000 MT

Available/known ore by need time = 1,500 MT
```

APS shows:

```text
Ore requirement 2,000 MT
Covered 1,500 MT
Shortfall 500 MT
```

The finished manufacturing requirement remains visible. APS does not recommend procurement.

## Example 8 — MTS

MTS is independent of Sales Orders:

```text
Stock target 500 MT
Projected qualified FG 320 MT
Replenishment rule says manufacture 180 MT

MTS PO = 180 MT
```

MTS PO may share a Campaign with MTO POs if configured policy and compatibility rules allow it. Its stock target/service objective remains distinct from customer-due-date service.
