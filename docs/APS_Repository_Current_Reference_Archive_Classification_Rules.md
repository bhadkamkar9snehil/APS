# APS Documentation Classification Rules

**Status:** current documentation-governance policy  
**Re-baselined:** 23-Aug-2026

Every substantive repository document should be identifiable as one of the following.

## Current-state authority

Describes what is actually integrated now.

Requirements:

- name the canonical branch/SHA or clearly say it follows `main`;
- distinguish implemented behavior from target behavior;
- update when a major tranche materially changes status/architecture;
- never override the current production call path.

Primary current-state document: [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

## Canonical specification / product contract

Defines intended domain/product/architecture behavior, including behavior that may still be incomplete.

Must not silently present a target as already implemented. Current-state claims defer to current `main` and the current-state document.

Examples include the end-to-end manufacturing flow, backend visibility contract, Gantt requirements and UI product blueprint.

## Current implementation note

Explains one implemented subsystem/path but is subordinate to current-state and canonical product contracts.

Requirements:

- state implementation baseline/date;
- identify known gaps;
- avoid branch-specific status claims after the branch is merged;
- link to the current-state authority.

## Dated audit / reconnaissance / completion evidence

A point-in-time factual record of what was found or verified on a particular date/branch/SHA.

Examples:

- backend acceptance audit dated 18-Aug-2026;
- pre-overhaul Gantt reconnaissance;
- branch-specific completion/verification reports.

These are **historical evidence**, even if they remain at the docs root/current folder for link stability. They must not be used as live status authority after later work is merged.

## Reference

Useful non-authoritative design/domain/history material.

May contain old terminology or behavior. If it can plausibly be mistaken for current truth, the index or document should identify the superseding authority.

## Archive

Superseded implementation/design material retained for history only.

Archive documents must not appear in primary current-reading lists except when explaining provenance/history.

A filename containing `FINAL`, `COMPLETE`, `STATUS` or `SUMMARY` inside the archive does not confer current authority.

## Generated / temporary

Build output, one-off scratch material or temporary analysis. Do not retain unless intentionally used as a fixture/artifact.

---

## Authority order when documents conflict

1. **current `main` code / persisted contracts**;
2. **exact-SHA Windows verification evidence** for claims about build/test/runtime state;
3. [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md);
4. [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) for ordered remaining backend work;
5. canonical target/specification documents;
6. current subsystem implementation notes;
7. dated audits/reconnaissance/completion reports;
8. reference;
9. archive.

The old ordering that placed the 18-Aug backend audit above all later implementation notes is obsolete because substantial backend/UI/Gantt/release work has since been integrated.

---

## Branch-status rule

A document written on `codex/gantt-workbench-overhaul`, `integrate/current-workstream`, a Claude branch or a Ponytail cleanup branch becomes **historical branch evidence** once that branch is fully contained by `main`.

Do not keep describing a merged branch as the implementation baseline. Update the current-state document instead.

---

## Verification wording rule

The blanket wording **“Do not use GitHub Actions or CI for APS project verification”** is obsolete.

Current rule:

> GitHub Actions or hosted CI are not substitutes for APS's authoritative Windows verification contract. Use the shared self-hosted Azure DevOps `EOS` agent / Windows Build Lab with repository-owned `build/verify.ps1`, and inspect evidence for the exact SHA being claimed green.

Dated historical documents may retain the old statement as evidence of the process in force when they were written, but current indexes/specifications must not repeat it as present policy.

---

## Status-drift rule

When a major issue closes or architecture is consolidated, update in the same tranche:

1. current-state document;
2. relevant work program/status document;
3. primary docs index if reading order changes;
4. subsystem implementation note if its status materially changes;
5. testing/verification documentation if the gate/evidence changes.

Do not rewrite dated historical audits to pretend they were written later. Reclassify them and add a newer current-state authority.

---

## Cleanup manifest

Repository cleanup moves may still use a manifest:

```text
Old path | Classification | New path | Superseded by | Reason
```

A file should be moved to archive because its meaning is superseded, not merely because its date is old. Where moving would break many stable issue/external links, it is acceptable to retain the file path and classify it explicitly as dated/historical evidence.
