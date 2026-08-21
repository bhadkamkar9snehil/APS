# APS Documentation

This directory is intentionally split by **authority**, not by file age.

## 1. Current authority

Start at [`current/README.md`](current/README.md).

Canonical/current documents remain at the `docs/` root so existing GitHub issue and external links stay valid. The current index identifies which root documents are authoritative and which are current supporting specifications.

The most important backend documents are:

1. [`APS_Backend_Acceptance_Audit_2026-08-18.md`](APS_Backend_Acceptance_Audit_2026-08-18.md)
2. [`APS_End_to_End_Manufacturing_Planning_Flow.md`](APS_End_to_End_Manufacturing_Planning_Flow.md)
3. [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md)
4. [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md) — canonical production planning/query/release/execution path established by #38.
5. [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md)
6. [`APS_Backend_Visibility_Contract.md`](APS_Backend_Visibility_Contract.md)
7. [`APS_Backend_Audit_Remediation_Map.md`](APS_Backend_Audit_Remediation_Map.md)
8. [`APS_Steel_Domain_Architecture_Roadmap.md`](APS_Steel_Domain_Architecture_Roadmap.md)
9. [`dotnet-planning-core.md`](dotnet-planning-core.md) — current implementation note, not the architecture authority.

Repository/governance evidence is recorded in [`APS_Repository_Cleanup_Manifest_2026-08-18.md`](APS_Repository_Cleanup_Manifest_2026-08-18.md).

## 2. Reference

[`reference/`](reference/) contains useful but **non-authoritative** material:

- earlier product/function architecture thinking;
- legacy workbook/Flask API references;
- configuration/master-data design material;
- parameter tuning and migration/parity references.

Reference documents may contain old terminology or behavior. Current documents win on conflict.

## 3. Archive

[`archive/`](archive/) contains **superseded historical material**:

- old audits and codebase analyses;
- old API consolidation work;
- old UI/layout/API mapping documents;
- old implementation phase/completion reports;
- old optimization/fix summaries;
- historical fix plans and orphan notes.

A filename containing words such as `COMPLETE`, `FINAL` or `SUMMARY` inside `docs/archive/` is historical only and is **not current acceptance evidence**.

## Product/backend authority rule

A capability is not complete merely because a class, workbook sheet, Python function, API route or historical completion report exists.

For production .NET work the applicable chain must be coherent:

```text
Domain/master
 -> SQL persistence/provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version audit
 -> execution/replan where relevant
 -> read model/API
```

For production lifecycle ownership specifically, use [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md). Demo/component algorithms are not production authority merely because they call the same solver.

## Historical source

The Python/workbook prototype and earlier UI implementations were retired from the active tree. Their final snapshot is preserved by Git tag `v0.2.5`; current implementation and acceptance claims must come from the .NET solution on `main`.

## UI

The current UI/product-design references are:

- [`APS_UI_UX_Product_Blueprint.md`](APS_UI_UX_Product_Blueprint.md)
- [`APS_UI_Implementation_Plan.md`](APS_UI_Implementation_Plan.md)

They are current design documents, but production UI implementation remains dependent on authoritative backend visibility and end-to-end acceptance.

The only direct-kernel Blazor planning surface is the explicitly gated demo sandbox at `/demo/planning`; it is not the production planner workspace.

## Verification

**Do not use GitHub Actions or CI for APS project verification.** Build/test/runtime verification is performed later in the intended developer environment.
