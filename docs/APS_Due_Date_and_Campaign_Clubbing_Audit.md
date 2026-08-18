# APS Due-Date and Campaign Clubbing Audit

Status: **current implementation audit + target behavior**

## Current implementation

Campaign planning currently:

1. orders MTO before MTS;
2. sorts higher priority first;
3. sorts earlier `ProductionOrder.RequiredDate` first;
4. groups by campaign compatibility key;
5. fills campaigns up to `MaximumCampaignQuantityMt`;
6. sets Campaign `RequiredDate` to the earliest PO required date in the campaign.

Current compatibility key includes:
- grade sequence class;
- caster section;
- route;
- exact grade partition depending mixed-grade policy;
- demand-source partition depending MTO/MTS mixing policy;
- segregation partition for dedicated campaign / same SO / same customer.

Current hot-rolling grouping uses:
- grade;
- input/caster section;
- final/output section;
- route;
- product family;
- fresh-vs-existing feed;
- optionally Campaign depending cross-campaign policy.

The resulting hot-rolling task uses the earliest PO required date in the group as `DueUtc` and maximum PO priority as task priority.

Current casting heat task similarly uses the earliest PO required date among exact heat allocations and maximum priority.

CP-SAT creates tardiness variables for tasks with `DueUtc` and weights lateness by priority.

## Problem

This correctly causes earlier/high-priority demand to influence schedule order, but it conflates:

```text
shared physical task due date
```

with:

```text
individual customer demand due dates
```

when one heat/rolling block serves multiple POs.

Example:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 20-Sep
shared 100 MT campaign/rolling block
```

Assigning `DueUtc = 10-Sep` to all 100 MT is conservative but not an accurate service model.

## Target behavior

### Preserve dates at demand-allocation grain

Every PO allocation to Campaign, Heat, Rolling Plan and WO retains:
- allocated quantity;
- customer required date;
- production-required-by date;
- priority/service class.

### Physical task timing

Physical tasks retain one scheduled start/end, but their service consequence is evaluated against all demand allocations they satisfy.

### Campaign dates

Campaign exposes summary dates only:
- EarliestRequiredDate;
- LatestRequiredDate;
- optional weighted/service-critical date.

These are explanations/anchors, not replacements for allocation dates.

### Candidate campaign optimization

Clubbing POs with different dates is allowed when compatible, but candidate scoring includes:
- service/tardiness by PO quantity;
- due-date spread/early-production cost;
- transition/setup savings;
- campaign/heat utilization;
- downstream feasibility;
- inventory/stability consequences.

### Solver objective

Preferred long-term approach:
- terminal/output fulfilment variables or allocation completion times;
- tardiness weighted by PO priority and allocated quantity;
- hard date only where customer/business policy explicitly requires it;
- upstream tasks derive latest useful times through precedence/material flow rather than all being assigned the same customer due date.

## Current assessment

Current code is a usable baseline for earliest-date-first planning, but due-date treatment is **not complete enough** for mixed-date aggregated campaigns.

This must be corrected as part of #15 campaign optimization and the demand-orchestration issue, not patched only in UI.
