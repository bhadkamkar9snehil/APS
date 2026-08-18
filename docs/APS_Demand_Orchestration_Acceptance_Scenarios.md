# APS MTO Demand Orchestration Acceptance Scenarios

Status: **acceptance scenarios**

1. Full FG coverage: SO remains demand, no manufacturing PO generated.
2. Partial FG coverage: MTO PO equals uncovered finished quantity only.
3. No FG but internally manufacturable finished product: MTO PO equals open SO quantity even when billet/raw materials are short.
4. Two SO items same grade/section: two POs remain distinct but may share campaign/heats/WOs through allocations.
5. Customer segregation: campaign clubbing is prevented where required.
6. Different due dates: each PO retains its own required-by date after clubbing.
7. Due-date change from SAP before firming: derived PO date updates idempotently.
8. Quantity decrease before firming: derived PO quantity reconciles without duplicate POs.
9. Firm/released PO: inventory/SAP changes do not silently rewrite committed manufacturing; reconciliation/replan policy applies.
10. SO cancellation before release: uncommitted derived PO is cancelled/reconciled.
11. Repeated sync: no duplicate MTO PO.
12. MTS remains independent from SO lineage.
