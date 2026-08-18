# APS Repository Cleanup and Documentation Archive Plan

Status: **planned cleanup / no code deletion yet**

## Purpose

The repository now contains:
- production .NET architecture/docs;
- current backend audit/remediation docs;
- UI planning docs;
- older workbook/Python prototype docs;
- historical architecture proposals;
- legacy implementation notes;
- active source code and tests.

This history is useful, but the current root/docs structure makes it too easy to read a stale document as current truth.

The cleanup objective is **clarity without destructive history loss**.

## Canonical documentation hierarchy

### `docs/current/`
Authoritative current product/backend documents only.

Recommended contents:
- APS steel-domain architecture roadmap
- backend acceptance audit
- backend visibility contract
- backend audit remediation map
- end-to-end manufacturing planning flow
- demand -> production order -> due-date model
- current .NET planning-core architecture
- current UI/UX blueprint and implementation plan when UI work resumes

### `docs/reference/`
Useful but non-authoritative references:
- original functional concept guide
- industry comparison/gap analysis
- generic architecture/scenario notes that still contain reusable concepts
- previous visibility/design studies

Every file in `reference/` should carry a banner stating that current architecture docs supersede it where they conflict.

### `docs/archive/`
Superseded/stale design and implementation notes retained only for historical context:
- old fix plans
- obsolete architecture variants
- abandoned UI directions
- docs describing APIs/models no longer present
- temporary implementation/session notes

Archive files should not be linked from the canonical docs index except through an Archive section.

## Legacy code/prototype policy

Do not delete the Python/workbook prototype merely because .NET is canonical.

Recommended organization once dependencies are understood:

```text
legacy/
  python-workbook-prototype/
```

or retain current paths with a very explicit `LEGACY_REFERENCE.md` if moving paths would break tooling/history.

Before moving any code:
- inventory imports/file references;
- identify any still-used workbook tooling;
- identify migration/reference tests;
- verify no production .NET runtime depends on it.

## Root repository cleanup

Audit root-level files and classify:
- production runtime source;
- developer tooling;
- active data/sample fixtures;
- legacy prototype;
- generated outputs;
- stale one-off scripts;
- docs that belong under `docs/`.

Do not delete a file solely because it looks old.

## README strategy

Root README should answer only:
1. What APS is.
2. What the canonical production direction is.
3. How repository areas are organized.
4. Which documents are authoritative.
5. How legacy/reference material is labeled.
6. Current backend completion status / governing epics.

`docs/README.md` should become the documentation map and contain explicit Current / Reference / Archive sections.

## Staleness metadata

Every substantial architecture document should have a header such as:

```text
Status: Canonical | Current implementation note | Reference | Archived
Supersedes: ...
Superseded by: ...
Last architecture review: YYYY-MM-DD
```

Avoid relying only on Git commit date to infer authority.

## Cleanup pass steps

1. Inventory all repository files/directories.
2. Inventory all Markdown/docs files.
3. Classify each document Current / Reference / Archive / Delete-generated-only.
4. Detect duplicate/conflicting architecture statements.
5. Update canonical documents first.
6. Move stale docs into archive preserving git history.
7. Add superseded banners to retained references.
8. Clean root clutter only after dependency inspection.
9. Update links after moves.
10. Verify no code/docs links point to old paths.
11. Produce a cleanup manifest showing old path -> new path -> classification/reason.

## Explicit non-goals

- no arbitrary deletion of prototype work;
- no formatting-only mass rewrite;
- no archive based solely on age;
- no moving source files during a documentation cleanup without dependency analysis;
- no GitHub Actions/CI use for the cleanup process.

## Acceptance criteria

- a new developer can identify authoritative architecture within one minute;
- stale docs cannot reasonably be mistaken for current requirements;
- all archived material remains discoverable;
- canonical docs contain no broken links to moved files;
- legacy Python/workbook status is unambiguous;
- a repository cleanup manifest records every move/archive decision.
