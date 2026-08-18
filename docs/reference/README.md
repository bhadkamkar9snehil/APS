# APS Reference Documentation

Everything under `docs/reference/` is **non-authoritative reference material**.

Use it for historical product thinking, legacy workbook/Python behavior, configuration ideas, parameter tuning and migration/parity work. It may contain terminology, architecture, APIs or planning behavior that has since changed.

Current product/domain/backend authority is indexed at [`../current/README.md`](../current/README.md).

## Reference categories

- top-level reference documents — useful earlier functional/architecture thinking;
- `legacy-api/` — workbook/Flask-era API references retained for migration/parity;
- `legacy-configuration/` — earlier configuration/master-data design material;
- `PARAMETER_TUNING_GUIDE.md` — legacy/runtime tuning reference.

## Legacy implementation source

The Python/workbook prototype remains in the repository outside this documentation folder, including `engine/`, workbook tooling, the workbook and legacy application/UI files. Those files are retained as **migration/reference source**, not as canonical production architecture.

If reference material conflicts with current documentation, current documentation wins.