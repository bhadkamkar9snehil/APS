# Current APS Documentation

**Authority:** This index identifies the documents that currently define APS product/domain/backend behavior.

Canonical/current documents intentionally remain at the `docs/` root so existing GitHub issue links and external references remain stable. Non-authoritative material is physically separated under `docs/reference/` and `docs/archive/`.

## Read first

1. [`../APS_Backend_Acceptance_Audit_2026-08-18.md`](../APS_Backend_Acceptance_Audit_2026-08-18.md) — current backend acceptance audit and implementation-state truth.
2. [`../APS_End_to_End_Manufacturing_Planning_Flow.md`](../APS_End_to_End_Manufacturing_Planning_Flow.md) — canonical causal flow from demand through material, manufacture, execution and replan.
3. [`../APS_Backend_Work_Program.md`](../APS_Backend_Work_Program.md) — dependency-ordered one-primary-issue-at-a-time backend implementation program.
4. [`../APS_Demand_to_Production_Order_and_Due_Date_Model.md`](../APS_Demand_to_Production_Order_and_Due_Date_Model.md) — Sales Order / Production Order / service-date semantics.
5. [`../APS_Backend_Visibility_Contract.md`](../APS_Backend_Visibility_Contract.md) — backend facts, controls and read/command visibility required before production UI.
6. [`../APS_Backend_Audit_Remediation_Map.md`](../APS_Backend_Audit_Remediation_Map.md) — audit finding to issue/dependency map.
7. [`../APS_Steel_Domain_Architecture_Roadmap.md`](../APS_Steel_Domain_Architecture_Roadmap.md) — steel-domain architecture and solver direction.
8. [`../dotnet-planning-core.md`](../dotnet-planning-core.md) — current implementation note for the .NET planning core; subordinate to the canonical architecture/audit documents above.

## Demand and service-date working documents

These are current supporting specifications for Issue #45 and related campaign/service work:

- [`../APS_Demand_Orchestration_Acceptance_Scenarios.md`](../APS_Demand_Orchestration_Acceptance_Scenarios.md)
- [`../APS_Demand_Orchestration_Gap_and_Implementation_Checklist.md`](../APS_Demand_Orchestration_Gap_and_Implementation_Checklist.md)
- [`../APS_Demand_Service_Date_Implementation_Map.md`](../APS_Demand_Service_Date_Implementation_Map.md)
- [`../APS_Due_Date_and_Campaign_Clubbing_Audit.md`](../APS_Due_Date_and_Campaign_Clubbing_Audit.md)
- [`../APS_SO_PO_Campaign_Service_Date_Examples.md`](../APS_SO_PO_Campaign_Service_Date_Examples.md)
- [`../APS_SO_PO_Campaign_Summary.md`](../APS_SO_PO_Campaign_Summary.md)

## Repository governance

- [`../APS_Repository_Cleanup_and_Documentation_Archive_Plan.md`](../APS_Repository_Cleanup_and_Documentation_Archive_Plan.md)
- [`../APS_Repository_Current_Reference_Archive_Classification_Rules.md`](../APS_Repository_Current_Reference_Archive_Classification_Rules.md)
- [`../APS_Repo_Cleanup_Acceptance_Scenarios.md`](../APS_Repo_Cleanup_Acceptance_Scenarios.md)
- [`../APS_Repository_Cleanup_Manifest_2026-08-18.md`](../APS_Repository_Cleanup_Manifest_2026-08-18.md)

## Future UI design — current but implementation-deferred

These are the current product/UI design references, but UI implementation waits for backend visibility/end-to-end readiness:

- [`../APS_UI_UX_Product_Blueprint.md`](../APS_UI_UX_Product_Blueprint.md)
- [`../APS_UI_Implementation_Plan.md`](../APS_UI_Implementation_Plan.md)

## Authority rule

If a document under `docs/reference/` or `docs/archive/` conflicts with any document listed above, the current document wins.

The legacy Python/workbook implementation remains in the repository for parity, migration and behavior reference. It is not the canonical production architecture.

**Verification rule:** do not use GitHub Actions/CI as APS project verification. Build/test/runtime verification is performed later in the intended developer environment.