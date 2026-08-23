# APS Reference Documentation

Everything under `docs/reference/` is **supporting, non-authoritative reference material** unless a current canonical document explicitly incorporates a specific reference by link/requirement ID.

Use this area for behavioral benchmarks, earlier product thinking, migration/parity information, configuration ideas and historical design detail. Reference documents may contain terminology, branch names, APIs or implementation observations that have since changed.

Current product/domain/backend authority is indexed at [`../current/README.md`](../current/README.md).

## Reference categories

- top-level reference documents — useful earlier functional/architecture/product thinking;
- [`APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md`](APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md) — external behavioral benchmark for APS's owned Gantt implementation;
- [`APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md`](APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS_2026-08-22_FULL.md) — complete detailed 22-Aug requirement definitions and pre-overhaul starting audit. The current canonical [`../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](../APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md) explicitly incorporates its detailed requirement families while superseding its old current-state/file-layout claims;
- `legacy-api/` — workbook/Flask-era API references retained for historical migration/parity;
- `legacy-configuration/` — earlier configuration/master-data design material;
- `PARAMETER_TUNING_GUIDE.md` — legacy/runtime tuning reference.

## Retired implementation source

The retired Python/workbook/Flask application and older UI implementations are **not present as active runtime/build source in the current product tree**. Their final retained source snapshot is available through Git history at tag `v0.2.5`.

Do not infer that old prototype directories/files still exist on `main` merely because a reference document describes them.

## Conflict rule

When reference material conflicts with current code or current/canonical documentation:

1. current `main` code wins for implementation fact;
2. [`../current/APS_CURRENT_STATE_2026-08-23.md`](../current/APS_CURRENT_STATE_2026-08-23.md) and current canonical docs define current status/architecture;
3. reference material remains useful only for the rationale/detail that has not been superseded.
