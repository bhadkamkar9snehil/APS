# APS Archived Documentation

Everything under `docs/archive/` is **superseded historical material retained for traceability**.

Archived documents must not be used to determine current feature completion, current issue priority, current test count, current verification policy, or current component/file structure. They may still be valuable for rationale, historical acceptance intent, migration/parity work and provenance.

Current authority is indexed at [`../current/README.md`](../current/README.md).

## Archive categories

- `backend-audits/` — dated backend audits and remediation maps preserved exactly as originally written;
- `demand-orchestration/` — #45-era demand/service implementation maps, acceptance scenarios and completion records;
- `domain-roadmaps/` — superseded architecture-roadmap snapshots;
- `gantt/` — pre-overhaul Gantt reconnaissance and other historical Gantt evidence;
- `superpowers/` — date-stamped implementation plans/design snapshots whose current-state assumptions were superseded;
- `repository-cleanup/` — earlier cleanup plan/manifest/acceptance records;
- `legacy-analysis/` — analyses of retired Python/workbook behavior;
- `legacy-api/` — superseded API consolidation/comparison material;
- `legacy-design/` — superseded design/scheduling analysis documents;
- `legacy-implementation/` — old phase completion/progress/refactoring reports;
- `legacy-optimization/` — old optimization/fix completion summaries;
- `ui/` — superseded UI/visibility/API mapping documents;
- `fix-plans/` — historical fix plans;
- `misc/` — orphaned or low-authority historical notes.

## Historical-text rule

Some archived documents contain statements such as:

- “current implementation”;
- “canonical audit”;
- “next issue”;
- “do not use CI”;
- old branch names or test counts.

Those statements are intentionally preserved because the archive records what was believed/required **at that point in time**. Their presence does not make them current.

A stable-path pointer outside `archive/` may link to one of these originals so old GitHub issue links continue to work. The pointer—not the historical body—explains current classification.

## Retired implementation source

The retired Python/workbook/Flask application and earlier UI implementations are no longer active source/runtime dependencies in the current tree. Their final retained source snapshot is available through Git history at tag `v0.2.5`.

**Archive means non-authoritative current-state evidence.**
