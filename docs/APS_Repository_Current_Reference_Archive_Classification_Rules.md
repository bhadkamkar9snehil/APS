# APS Documentation Classification Rules

Status: **cleanup policy**

Every substantive repository document must be classified as one of:

## Canonical
Current authoritative product/domain/backend/UI architecture.

May be cited by current GitHub issues and implementation work.

## Current implementation note
Describes current code behavior but is not the governing product architecture.

Must state the branch/implementation context it describes.

## Reference
Contains useful historical/domain ideas but is not authoritative where it conflicts with Canonical documents.

Must include a banner linking to superseding Canonical documents.

## Archive
Superseded design/implementation material retained for history only.

Must not be linked from the primary docs index except under Archive.

## Generated/temporary
Build output, temporary analysis, one-off scratch files. Do not retain in source control unless intentionally used as fixtures.

## Authority rule

When documents conflict:

1. current canonical backend acceptance/end-to-end docs;
2. current steel-domain roadmap;
3. current implementation notes;
4. reference docs;
5. archive docs.

The repository cleanup pass must produce a manifest containing:

```text
Old path | Classification | New path | Superseded by | Reason
```

No document is archived merely because it is old; it is archived because its architectural meaning is superseded or no longer applicable.
