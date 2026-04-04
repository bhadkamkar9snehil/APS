"""
Finite Scheduler — OR-Tools CP-SAT
----------------------------------
Schedules heats through SMS (EAF -> LRF -> VD? -> CCM) and derived production
orders on RM.

Time unit: MINUTES from planning horizon start.
"""
from __future__ import annotations

import math
from datetime import datetime, timedelta

import pandas as pd
try:
    from ortools.sat.python import cp_model
except ModuleNotFoundError:  # pragma: no cover - environment-dependent import
    cp_model = None

from engine.campaign import HEAT_SIZE_MT, billet_family_for_grade, rm_minutes_for_qty, needs_vd_for_grade

EAF_TIME = 90
LRF_TIME = 40
VD_TIME = 45
CCM_130 = 50
CCM_150 = 60

OPERATION_ORDER = {"EAF": 1, "LRF": 2, "VD": 3, "CCM": 4, "RM": 5}
OPERATION_ALIASES = {
    "EAF": "EAF",
    "MELTING": "EAF",
    "LRF": "LRF",
    "REFINING": "LRF",
    "VD": "VD",
    "DEGASSING": "VD",
    "CCM": "CCM",
    "CASTING": "CCM",
    "RM": "RM",
    "ROLLING": "RM",
}
DEFAULT_MACHINE_GROUPS = {
    "EAF": ["EAF-01", "EAF-02"],
    "LRF": ["LRF-01", "LRF-02", "LRF-03"],
    "VD": ["VD-01"],
    "CCM": ["CCM-01", "CCM-02"],
    "RM": ["RM-01", "RM-02"],
}
QUEUE_VIOLATION_WEIGHT = 500


def _floor_hour(ts: datetime) -> datetime:
    return ts.replace(minute=0, second=0, microsecond=0)


def _cp_sat_available() -> bool:
    return cp_model is not None and hasattr(cp_model, "CpModel") and hasattr(cp_model, "CpSolver")


def _coerce_datetime(value) -> datetime | None:
    ts = pd.to_datetime(value, errors="coerce")
    if pd.isna(ts):
        return None
    return ts.to_pydatetime()


def _planning_start(planning_start, frozen_jobs: dict | None = None) -> datetime:
    explicit_start = _coerce_datetime(planning_start)
    if explicit_start is not None:
        return _floor_hour(explicit_start)

    frozen_starts = []
    for frozen in frozen_jobs.values():
        frozen_start = _coerce_datetime((frozen or {}).get("Planned_Start"))
        if frozen_start is not None:
            frozen_starts.append(_floor_hour(frozen_start))
    if frozen_starts:
        return min(frozen_starts)
    raise ValueError("planning_start is required when no frozen job anchor is available.")


def _config_flag(config: dict | None, key: str, default: str = "N") -> bool:
    value = str((config or {}).get(key, default) or default).strip().upper()
    return value in {"Y", "YES", "TRUE", "1", "ON"}


def _allow_scheduler_default_masters(config: dict | None = None) -> bool:
    return _config_flag(config, "Allow_Scheduler_Default_Masters", "N")


def _master_data_error(message: str, *, allow_defaults: bool = False) -> str:
    if allow_defaults:
        return message
    return (
        f"{message} Populate Routing/Resource_Master or set "
        "Config > Allow_Scheduler_Default_Masters = Y to use demo fallbacks."
    )


def _build_op_lookup(resources: pd.DataFrame | None) -> dict[str, str]:
    aliases = dict(OPERATION_ALIASES)
    if resources is None or getattr(resources, "empty", True) or "Operation_Group" not in resources.columns:
        return aliases

    for _, row in resources.iterrows():
        resource_id = str(row.get("Resource_ID", "")).strip().upper()
        op_group = str(row.get("Operation_Group", "")).strip().upper()
        if resource_id and op_group:
            aliases[resource_id] = op_group
            aliases[op_group] = op_group
    return aliases


def _resource_family(resource_id: str, op_lookup: dict[str, str] | None = None) -> str:
    resource_text = str(resource_id or "").strip().upper()
    if op_lookup:
        resolved = op_lookup.get(resource_text)
        if resolved:
            return resolved
    for family in DEFAULT_MACHINE_GROUPS:
        if resource_text.startswith(family):
            return family
    return resource_text


def _machine_groups(
    resources: pd.DataFrame | None,
    op_lookup: dict[str, str] | None = None,
    *,
    allow_defaults: bool = False,
) -> dict[str, list[str]]:
    groups = {family: [] for family in DEFAULT_MACHINE_GROUPS}
    if resources is None or getattr(resources, "empty", True):
        if allow_defaults:
            return {family: list(machine_ids) for family, machine_ids in DEFAULT_MACHINE_GROUPS.items()}
        return groups

    res = resources.copy()
    if "Status" in res.columns:
        status = res["Status"].fillna("Active").astype(str).str.strip().str.upper()
        res = res[~status.isin({"INACTIVE", "DOWN", "DISABLED"})]

    for resource_id in res.get("Resource_ID", pd.Series(dtype=object)).astype(str):
        family = _resource_family(resource_id, op_lookup=op_lookup)
        if family in groups:
            groups[family].append(resource_id.strip())

    for family, defaults in DEFAULT_MACHINE_GROUPS.items():
        if not groups[family]:
            groups[family] = list(defaults) if allow_defaults else []
        else:
            groups[family] = sorted(dict.fromkeys(groups[family]))
    return groups


def _normalize_operation(op: str, op_lookup: dict[str, str] | None = None) -> str:
    key = str(op or "").strip().upper()
    if op_lookup:
        return op_lookup.get(key, OPERATION_ALIASES.get(key, key))
    return OPERATION_ALIASES.get(key, key)


def _build_operation_order(routing: pd.DataFrame | None, op_lookup: dict[str, str] | None = None) -> dict[str, int]:
    if routing is None or getattr(routing, "empty", True) or "Sequence" not in routing.columns:
        return dict(OPERATION_ORDER)

    seq = routing.dropna(subset=["Sequence", "Operation"]).copy()
    if seq.empty:
        return dict(OPERATION_ORDER)
    seq["_Operation"] = seq["Operation"].map(lambda op: _normalize_operation(op, op_lookup))
    seq["Sequence"] = pd.to_numeric(seq["Sequence"], errors="coerce")
    seq = seq.dropna(subset=["Sequence", "_Operation"])
    if seq.empty:
        return dict(OPERATION_ORDER)
    return (
        seq.groupby("_Operation", as_index=False)["Sequence"]
        .min()
        .set_index("_Operation")["Sequence"]
        .astype(int)
        .to_dict()
    )


def _normalize_queue_times(
    queue_times: dict | None,
    op_lookup: dict[str, str] | None = None,
) -> dict[tuple[str, str], dict]:
    result = {}
    for (from_op, to_op), rule in (queue_times or {}).items():
        left = _normalize_operation(from_op, op_lookup)
        right = _normalize_operation(to_op, op_lookup)
        raw_min = pd.to_numeric((rule or {}).get("min", 0), errors="coerce")
        raw_max = pd.to_numeric((rule or {}).get("max", 9999), errors="coerce")
        result[(left, right)] = {
            "min": max(int(raw_min), 0) if pd.notna(raw_min) else 0,
            "max": max(int(raw_max), 0) if pd.notna(raw_max) else 9999,
            "enforcement": str((rule or {}).get("enforcement", "Hard") or "Hard").strip().upper(),
        }
    return result


def _operation_duration(profile: dict | None, *, include_setup: bool = False) -> int:
    profile = profile or {}
    cycle = float(profile.get("cycle", 0) or 0)
    setup = float(profile.get("setup", 0) or 0) if include_setup else 0.0
    return max(1, int(round(cycle + setup)))


def _ccm_time(grade: str) -> int:
    return CCM_130 if billet_family_for_grade(grade) == "BIL-130" else CCM_150


def _priority_weight(priority_rank_value: int) -> int:
    if priority_rank_value <= 1:
        return 4
    if priority_rank_value == 2:
        return 3
    if priority_rank_value == 3:
        return 2
    return 1


def _section_display(section_value, sections_covered: str = "") -> str:
    section_text = str(section_value or "").strip()
    if section_text.upper() == "MIX":
        return f"Mix: {sections_covered}" if sections_covered else "Mixed"
    if section_text in {"", "nan", "None"}:
        return ""
    try:
        return f"{float(section_value):g}"
    except Exception:
        return section_text


def _so_pool_display(so_ids: list) -> str:
    clean_ids = [str(so_id).strip() for so_id in so_ids if str(so_id or "").strip()]
    if not clean_ids:
        return ""
    if len(clean_ids) <= 3:
        return f"Pool: {', '.join(clean_ids)}"
    return f"Pool: {', '.join(clean_ids[:3])} +{len(clean_ids) - 3}"


def _production_orders_for_campaign(campaign: dict) -> list:
    orders = campaign.get("production_orders", [])
    if orders:
        return sorted(
            orders,
            key=lambda line: (
                int(line.get("priority_rank", 9)),
                pd.to_datetime(line.get("due_date")),
                pd.to_numeric(pd.Series([line.get("section_mm")]), errors="coerce").fillna(999).iloc[0],
                str(line.get("production_order_id", "")),
            ),
        )

    return [
        {
            "production_order_id": f"{campaign['campaign_id']}-PO01",
            "so_id": ", ".join(campaign.get("so_ids", [])),
            "sku_id": "",
            "section_mm": campaign.get("section_mm", 6.5),
            "qty_mt": campaign.get("total_coil_mt", 0),
            "due_date": pd.to_datetime(campaign["due_date"]),
            "priority_rank": int(campaign.get("priority_rank", 9)),
            "priority": campaign.get("priority", "NORMAL"),
        }
    ]


def _routing_rows_for_op(
    routing: pd.DataFrame | None,
    operation: str,
    *,
    grade: str | None = None,
    sku_id: str | None = None,
    billet_family: str | None = None,
    op_lookup: dict[str, str] | None = None,
) -> pd.DataFrame:
    if routing is None or getattr(routing, "empty", True):
        return pd.DataFrame()

    rt = routing.copy()
    if "Operation" not in rt.columns:
        return pd.DataFrame()
    rt["_Operation"] = rt["Operation"].map(lambda op: _normalize_operation(op, op_lookup))
    rt = rt[rt["_Operation"] == operation].copy()
    if rt.empty:
        return rt

    if sku_id and "SKU_ID" in rt.columns:
        exact = rt[rt["SKU_ID"].astype(str).str.strip() == str(sku_id).strip()]
        if not exact.empty:
            return exact

    if billet_family and "SKU_ID" in rt.columns:
        prefix = str(billet_family).strip().upper()
        family_rows = rt[rt["SKU_ID"].astype(str).str.upper().str.startswith(prefix)]
        if not family_rows.empty:
            rt = family_rows

    if grade and "Grade" in rt.columns:
        grade_rows = rt[rt["Grade"].astype(str).str.strip() == str(grade).strip()]
        if not grade_rows.empty:
            rt = grade_rows

    if "Op_Seq" in rt.columns:
        rt = rt.sort_values("Op_Seq")
    return rt


def build_operation_times(
    routing: pd.DataFrame | None,
    grade: str,
    *,
    sku_id: str | None = None,
    billet_family: str | None = None,
    resources: pd.DataFrame | None = None,
    op_lookup: dict[str, str] | None = None,
    allow_defaults: bool = False,
) -> dict[str, dict[str, float]]:
    defaults = (
        {
            "EAF": {"cycle": EAF_TIME, "setup": 30.0},
            "LRF": {"cycle": LRF_TIME, "setup": 10.0},
            "VD": {"cycle": VD_TIME, "setup": 15.0},
            "CCM": {"cycle": float(_ccm_time(grade)), "setup": 20.0},
            "RM": {"cycle": 0.0, "setup": 40.0},
        }
        if allow_defaults
        else {
            "EAF": {"cycle": 0.0, "setup": 0.0},
            "LRF": {"cycle": 0.0, "setup": 0.0},
            "VD": {"cycle": 0.0, "setup": 0.0},
            "CCM": {"cycle": 0.0, "setup": 0.0},
            "RM": {"cycle": 0.0, "setup": 0.0},
        }
    )

    if routing is None or getattr(routing, "empty", True):
        routing_rows_available = False
    else:
        routing_rows_available = True

    for op in defaults:
        rows = (
            _routing_rows_for_op(
                routing,
                op,
                grade=grade,
                sku_id=sku_id,
                billet_family=billet_family,
                op_lookup=op_lookup,
            )
            if routing_rows_available
            else pd.DataFrame()
        )
        if not rows.empty:
            cycle_series = pd.to_numeric(
                rows["Cycle_Time_Min_Heat"] if "Cycle_Time_Min_Heat" in rows.columns else pd.Series(dtype=float),
                errors="coerce",
            ).dropna()
            setup_series = pd.to_numeric(
                rows["Setup_Time_Min"] if "Setup_Time_Min" in rows.columns else pd.Series(dtype=float),
                errors="coerce",
            ).dropna()
            if not cycle_series.empty:
                defaults[op]["cycle"] = float(cycle_series.iloc[0])
            if not setup_series.empty:
                defaults[op]["setup"] = float(setup_series.iloc[0])

    if resources is not None and not getattr(resources, "empty", True) and "Operation_Group" in resources.columns:
        dedup = resources.drop_duplicates(subset=["Operation_Group"]).copy()
        for _, res in dedup.iterrows():
            op_group = _normalize_operation(res.get("Operation_Group", ""), op_lookup)
            if not op_group or op_group not in defaults:
                continue
            cycle = pd.to_numeric(res.get("Default_Cycle_Min"), errors="coerce")
            setup = pd.to_numeric(res.get("Default_Setup_Min"), errors="coerce")
            if allow_defaults:
                if defaults[op_group]["cycle"] > 0 and (
                    routing_rows_available
                    and not _routing_rows_for_op(
                        routing,
                        op_group,
                        grade=grade,
                        sku_id=sku_id,
                        billet_family=billet_family,
                        op_lookup=op_lookup,
                    ).empty
                ):
                    continue
                if pd.notna(cycle) and (op_group == "RM" or float(cycle) > 0):
                    defaults[op_group]["cycle"] = float(cycle)
                if pd.notna(setup):
                    defaults[op_group]["setup"] = float(setup)
            else:
                if pd.notna(cycle) and defaults[op_group]["cycle"] <= 0 and (op_group == "RM" or float(cycle) > 0):
                    defaults[op_group]["cycle"] = float(cycle)
                if pd.notna(setup) and defaults[op_group]["setup"] <= 0:
                    defaults[op_group]["setup"] = float(setup)
    return defaults


def _campaign_route_rows(
    campaign: dict,
    routing: pd.DataFrame | None,
    op_lookup: dict[str, str] | None = None,
) -> pd.DataFrame:
    if routing is None or getattr(routing, "empty", True):
        return pd.DataFrame()

    grade = str(campaign.get("grade", "")).strip()
    billet_family = str(campaign.get("billet_family", "")).strip().upper()
    production_orders = _production_orders_for_campaign(campaign)
    order_skus = [str(order.get("sku_id", "")).strip() for order in production_orders if str(order.get("sku_id", "")).strip()]
    route_frames = []

    rt = routing.copy()
    rt["_Operation"] = rt["Operation"].map(lambda op: _normalize_operation(op, op_lookup))

    sms_rows = rt[rt["_Operation"].isin(["EAF", "LRF", "VD", "CCM"])].copy()
    if grade and "Grade" in sms_rows.columns:
        sms_grade = sms_rows[sms_rows["Grade"].astype(str).str.strip() == grade]
        if not sms_grade.empty:
            sms_rows = sms_grade
    if billet_family and "SKU_ID" in sms_rows.columns:
        sms_family = sms_rows[sms_rows["SKU_ID"].astype(str).str.upper().str.startswith(billet_family)]
        if not sms_family.empty:
            sms_rows = sms_family
    if not sms_rows.empty:
        route_frames.append(sms_rows)

    rm_rows = rt[rt["_Operation"].eq("RM")].copy()
    if order_skus and "SKU_ID" in rm_rows.columns:
        exact_rm = rm_rows[rm_rows["SKU_ID"].astype(str).isin(order_skus)]
        if not exact_rm.empty:
            rm_rows = exact_rm
    elif grade and "Grade" in rm_rows.columns:
        rm_grade = rm_rows[rm_rows["Grade"].astype(str).str.strip() == grade]
        if not rm_grade.empty:
            rm_rows = rm_grade
    if not rm_rows.empty:
        route_frames.append(rm_rows)

    if not route_frames:
        return pd.DataFrame()

    route_rows = pd.concat(route_frames, ignore_index=True, sort=False)
    if "Sequence" in route_rows.columns:
        route_rows["Sequence"] = pd.to_numeric(route_rows["Sequence"], errors="coerce")
        sort_cols = ["Sequence"]
        if "Op_Seq" in route_rows.columns:
            route_rows["Op_Seq"] = pd.to_numeric(route_rows["Op_Seq"], errors="coerce")
            sort_cols.append("Op_Seq")
        if "SKU_ID" in route_rows.columns:
            sort_cols.append("SKU_ID")
        route_rows = route_rows.sort_values(sort_cols, kind="stable")
    elif "Op_Seq" in route_rows.columns:
        route_rows["Op_Seq"] = pd.to_numeric(route_rows["Op_Seq"], errors="coerce")
        sort_cols = ["Op_Seq"]
        if "SKU_ID" in route_rows.columns:
            sort_cols.append("SKU_ID")
        route_rows = route_rows.sort_values(sort_cols, kind="stable")
    return route_rows


def _route_condition_met(route_row: pd.Series, campaign: dict) -> bool:
    is_optional = str(route_row.get("Is_Optional", "N")).strip().upper() == "Y"
    if not is_optional:
        return True

    condition = str(route_row.get("Optional_Condition", "")).strip()
    if not condition:
        return False

    condition_upper = condition.upper()
    if condition_upper in {"NEEDS_VD", "ROUTE_VARIANT"}:
        return bool(campaign.get("needs_vd"))

    # Check top-level campaign dict first
    if condition in campaign:
        value = campaign.get(condition)
        return str(value or "").strip().upper() == "Y"

    # Then check generic sku_attributes for data-driven conditions
    sku_attrs = campaign.get("sku_attributes", {})
    if condition in sku_attrs:
        value = sku_attrs.get(condition)
        return str(value or "").strip().upper() == "Y"

    return False


def _campaign_sms_operations(
    campaign: dict,
    routing: pd.DataFrame | None,
    op_lookup: dict[str, str] | None = None,
    *,
    allow_defaults: bool = False,
) -> list[str]:
    route_rows = _campaign_route_rows(campaign, routing, op_lookup)
    ops = []
    for _, route_row in route_rows.iterrows():
        op = _normalize_operation(route_row.get("Operation", ""), op_lookup)
        if op == "RM":
            continue
        if not _route_condition_met(route_row, campaign):
            continue
        if op and op not in ops:
            ops.append(op)
    if ops:
        return ops
    if allow_defaults:
        return ["EAF", "LRF", "VD", "CCM"] if campaign.get("needs_vd") else ["EAF", "LRF", "CCM"]
    return []


def _campaign_transfer_times(
    campaign: dict,
    routing: pd.DataFrame | None,
    op_lookup: dict[str, str] | None = None,
) -> dict[tuple[str, str], int]:
    route_rows = _campaign_route_rows(campaign, routing, op_lookup)
    if route_rows.empty:
        return {}

    active_rows = []
    for _, route_row in route_rows.iterrows():
        op = _normalize_operation(route_row.get("Operation", ""), op_lookup)
        if not op:
            continue
        if not _route_condition_met(route_row, campaign):
            continue
        active_rows.append((op, route_row))

    result: dict[tuple[str, str], int] = {}
    for idx in range(1, len(active_rows)):
        prev_op = active_rows[idx - 1][0]
        current_op = active_rows[idx][0]
        transfer = pd.to_numeric(active_rows[idx][1].get("Transfer_Time_Min"), errors="coerce")
        if pd.notna(transfer) and float(transfer) > 0:
            result[(prev_op, current_op)] = int(float(transfer))
    return result


def _queue_status(gap_minutes: float | int | None, rule: dict | None) -> str:
    if not rule:
        return ""
    try:
        gap = float(gap_minutes)
    except Exception:
        return ""
    min_q = float(rule.get("min", 0) or 0)
    max_q = float(rule.get("max", 9999) or 9999)
    if gap < min_q - 1e-9:
        return "CRITICAL"
    if max_q >= 9999:
        return "OK"
    if gap > max_q + 1e-9:
        return "CRITICAL"
    if gap >= max_q * 0.75:
        return "WARN"
    return "OK"


def _queue_wait_minutes(
    start_value,
    previous_end_value,
    transfer_gap: float | int = 0,
) -> float | None:
    if start_value is None or previous_end_value is None:
        return None
    try:
        if isinstance(start_value, (datetime, pd.Timestamp)) or isinstance(previous_end_value, (datetime, pd.Timestamp)):
            elapsed = (pd.to_datetime(start_value) - pd.to_datetime(previous_end_value)).total_seconds() / 60.0
        else:
            elapsed = float(start_value) - float(previous_end_value)
        return elapsed - float(transfer_gap or 0)
    except Exception:
        return None


def _heats_calc_warning_text(warnings) -> str:
    if not warnings:
        return ""
    if isinstance(warnings, str):
        return warnings.strip()
    parts = []
    for warning in warnings:
        if not isinstance(warning, dict):
            text = str(warning).strip()
            if text:
                parts.append(text)
            continue
        issue_type = str(warning.get("type", "") or "").strip()
        reason = str(warning.get("reason", "") or "").strip()
        path = str(warning.get("path", "") or "").strip()
        detail = reason or path or str(warning.get("sku_id", "") or "").strip()
        parts.append(f"{issue_type}: {detail}" if issue_type else detail)
    return " | ".join(part for part in parts if part)


def _campaign_serialization_mode(config: dict | None = None) -> str:
    raw_mode = str(
        (config or {}).get("Campaign_Serialization_Mode", "STRICT_END_TO_END") or "STRICT_END_TO_END"
    ).strip().upper()
    mode_aliases = {
        "STRICT": "STRICT_END_TO_END",
        "END_TO_END": "STRICT_END_TO_END",
        "STRICT_END_TO_END": "STRICT_END_TO_END",
        "SMS": "OVERLAP_AFTER_SMS",
        "SMS_ONLY": "OVERLAP_AFTER_SMS",
        "PRIMARY_BATCH": "OVERLAP_AFTER_SMS",
        "OVERLAP_AFTER_SMS": "OVERLAP_AFTER_SMS",
    }
    normalized = mode_aliases.get(raw_mode)
    if normalized is None:
        raise ValueError(
            "Config > Campaign_Serialization_Mode must be STRICT_END_TO_END or OVERLAP_AFTER_SMS."
        )
    return normalized


def _master_data_mode(config: dict | None = None) -> str:
    return "DEFAULT_MASTERS_ALLOWED" if _allow_scheduler_default_masters(config) else "STRICT_MASTERS"


def _preferred_resource_for_operation(
    routing: pd.DataFrame | None,
    operation: str,
    *,
    grade: str | None = None,
    sku_id: str | None = None,
    op_lookup: dict[str, str] | None = None,
) -> str | None:
    """Extract preferred resource from routing for given operation and SKU/grade."""
    if routing is None or getattr(routing, "empty", True):
        return None

    rows = _routing_rows_for_op(routing, operation, grade=grade, sku_id=sku_id, op_lookup=op_lookup)
    if rows.empty:
        return None

    # Get preferred resource from first matching row (should be same across all)
    preferred = rows.iloc[0].get("Preferred_Resource")
    if preferred and str(preferred).strip().upper() not in {"", "NONE", "NA"}:
        return str(preferred).strip()

    return None


def _changeover_minutes(changeover_matrix: pd.DataFrame | None, from_grade: str, to_grade: str) -> int:
    if changeover_matrix is None or getattr(changeover_matrix, "empty", True):
        return 0

    from_key = str(from_grade or "").strip()
    to_key = str(to_grade or "").strip()
    if not from_key or not to_key or from_key == to_key:
        return 0

    try:
        value = changeover_matrix.loc[from_key, to_key]
    except Exception:
        try:
            value = changeover_matrix.set_index(changeover_matrix.columns[0]).loc[from_key, to_key]
        except Exception:
            return 0
    try:
        return max(int(float(value)), 0)
    except Exception:
        return 0


def _rm_duration(
    order: dict,
    grade: str,
    routing: pd.DataFrame | None = None,
    *,
    resources: pd.DataFrame | None = None,
    op_lookup: dict[str, str] | None = None,
    changeover_minutes: int = 0,
    include_setup: bool = True,
    add_changeover_to_duration: bool = False,
    allow_defaults: bool = False,
) -> int:
    section = pd.to_numeric(pd.Series([order.get("section_mm")]), errors="coerce").fillna(6.5).iloc[0]
    qty_mt = float(order.get("qty_mt", 0) or 0.0)
    sku_id = str(order.get("sku_id", "")).strip() or None
    profile = build_operation_times(
        routing,
        grade,
        sku_id=sku_id,
        billet_family=billet_family_for_grade(grade),
        resources=resources,
        op_lookup=op_lookup,
        allow_defaults=allow_defaults,
    ).get("RM", {})
    cycle = float(profile.get("cycle", 0) or 0)
    routing_setup = float(profile.get("setup", 0) or 0)
    changeover = max(int(round(changeover_minutes or 0)), 0) if add_changeover_to_duration else 0
    if cycle > 0:
        setup = routing_setup if include_setup else 0
        duration = (cycle * (qty_mt / HEAT_SIZE_MT)) + setup + changeover
        return max(1, int(round(duration)))
    if not allow_defaults:
        raise ValueError(
            _master_data_error(
                f"Missing RM cycle time for SKU {sku_id or '<blank>'} / grade {grade}.",
                allow_defaults=allow_defaults,
            )
        )
    return max(
        1,
        int(
            round(
                rm_minutes_for_qty(qty_mt, section, include_setup=include_setup)
                + changeover
            )
        ),
    )


def _validate_campaign_master_data(
    campaign: dict,
    sms_ops: list[str],
    sms_times: dict[str, dict[str, float]],
    machine_groups: dict[str, list[str]],
    *,
    allow_defaults: bool = False,
) -> None:
    cid = str(campaign.get("campaign_id", "")).strip() or "<unknown>"
    if not sms_ops:
        raise ValueError(
            _master_data_error(
                f"Missing SMS routing for campaign {cid}.",
                allow_defaults=allow_defaults,
            )
        )
    for op in sms_ops:
        if not machine_groups.get(op):
            raise ValueError(
                _master_data_error(
                    f"No active resources available for operation {op} in campaign {cid}.",
                    allow_defaults=allow_defaults,
                )
            )
        cycle = float((sms_times.get(op, {}) or {}).get("cycle", 0) or 0)
        if cycle <= 0:
            raise ValueError(
                _master_data_error(
                    f"Missing cycle time for operation {op} in campaign {cid}.",
                    allow_defaults=allow_defaults,
                )
            )
    if not machine_groups.get("RM"):
        raise ValueError(
            _master_data_error(
                f"No active resources available for operation RM in campaign {cid}.",
                allow_defaults=allow_defaults,
            )
        )


def _resolve_selected_machine(solver: cp_model.CpSolver, task: dict) -> str:
    if task.get("fixed_machine"):
        return task["fixed_machine"]
    for machine, literal in task.get("choices", {}).items():
        try:
            if solver.Value(literal):
                return machine
        except Exception:
            continue
    return task.get("candidates", [""])[0]


def _task_start_end_from_frozen(t0: datetime, frozen: dict, fallback_duration: int) -> tuple[int, int, int]:
    frozen_start = _coerce_datetime((frozen or {}).get("Planned_Start"))
    frozen_end = _coerce_datetime((frozen or {}).get("Planned_End"))
    if frozen_start is None or frozen_end is None:
        raise ValueError(f"Frozen job has invalid timestamps: {frozen}")
    if frozen_end <= frozen_start:
        raise ValueError(f"Frozen job end must be after start: {frozen}")
    if frozen_end <= t0:
        raise ValueError(f"Frozen job ends before planning_start: {frozen}")

    start_min = max(0, int((frozen_start - t0).total_seconds() / 60))
    end_min = max(1, int(math.ceil((frozen_end - t0).total_seconds() / 60.0)))
    duration = max(end_min - start_min, max(int(fallback_duration or 1), 1))
    return start_min, end_min, duration


def _next_available_start(start_min: int, duration: int, downtime_window: tuple[int, int] | None) -> int:
    if not downtime_window:
        return start_min
    down_start, down_end = downtime_window
    candidate = start_min
    while candidate < down_end and candidate + duration > down_start:
        candidate = down_end
    return candidate


def _validate_resource_feasibility(
    campaigns: list,
    machine_groups: dict[str, list[str]],
    routing: pd.DataFrame | None = None,
    op_lookup: dict[str, str] | None = None,
    allow_defaults: bool = False,
) -> list[str]:
    """Check resource feasibility before scheduling.

    Returns: List of warning messages (empty if all OK)
    """
    warnings = []

    # Check: Each operation has at least one available resource
    for operation, machines in machine_groups.items():
        if not machines:
            warnings.append(
                f"WARNING: No resources available for operation {operation}. "
                f"This will cause scheduling to fail."
            )
        if len(machines) == 1:
            warnings.append(
                f"NOTE: Single resource for {operation} ({machines[0]}). "
                f"Any downtime will block entire operation."
            )

    # Check: For each campaign, required operations are staffed
    for camp in campaigns:
        grade = camp.get("grade", "")
        heats = int(camp.get("heats", 1))
        needs_vd = needs_vd_for_grade(grade)

        required_ops = ["EAF", "LRF", "CCM"]
        if needs_vd:
            required_ops.append("VD")

        for op in required_ops:
            if not machine_groups.get(op):
                warnings.append(
                    f"ERROR: Campaign {camp.get('campaign_id')} requires {op}, "
                    f"but no {op} resources are available."
                )

    # Check: Preferred resources exist and are available
    if routing is not None and not getattr(routing, "empty", True):
        for _, route_row in routing.iterrows():
            preferred = str(route_row.get("Preferred_Resource", "")).strip()
            if preferred and preferred.upper() not in {"", "NONE", "NA"}:
                op = _normalize_operation(route_row.get("Operation"), op_lookup)
                available = machine_groups.get(op, [])
                if preferred not in available:
                    warnings.append(
                        f"WARNING: Preferred resource {preferred} for {op} is not available. "
                        f"Will use alternate: {available}"
                    )

    return warnings


def schedule(
    campaigns: list,
    resources: pd.DataFrame,
    planning_start=None,
    planning_horizon_days: int = 14,
    machine_down_resource: str | None = None,
    machine_down_hours: float = 0.0,
    machine_down_start_hour: float = 0.0,
    frozen_jobs: dict | None = None,
    routing: pd.DataFrame | None = None,
    queue_times: dict | None = None,
    changeover_matrix: pd.DataFrame | None = None,
    config: dict | None = None,
    solver_time_limit_sec: float = 30.0,
) -> dict:
    """Run the CP-SAT scheduler."""
    if not _cp_sat_available():
        return _greedy_fallback(
            campaigns,
            resources=resources,
            planning_start=planning_start,
            planning_horizon_days=planning_horizon_days,
            machine_down_resource=machine_down_resource,
            machine_down_hours=machine_down_hours,
            machine_down_start_hour=machine_down_start_hour,
            frozen_jobs=frozen_jobs,
            routing=routing,
            queue_times=queue_times,
            changeover_matrix=changeover_matrix,
            config=config,
            solver_detail="ORTOOLS_UNAVAILABLE",
        )

    model = cp_model.CpModel()

    planning_horizon_days = max(int(planning_horizon_days or 14), 1)
    frozen_jobs = frozen_jobs or {}
    t0 = _planning_start(planning_start, frozen_jobs)
    horizon = planning_horizon_days * 24 * 60
    max_time = horizon + (7 * 24 * 60)
    op_lookup = _build_op_lookup(resources)
    op_order = _build_operation_order(routing, op_lookup)
    allow_default_masters = _allow_scheduler_default_masters(config)
    machine_groups = _machine_groups(resources, op_lookup=op_lookup, allow_defaults=allow_default_masters)

    # Fix 8.1: Validate resource feasibility before building model
    feasibility_warnings = _validate_resource_feasibility(
        campaigns, machine_groups, routing=routing, op_lookup=op_lookup, allow_defaults=allow_default_masters
    )
    for warning in feasibility_warnings:
        print(f"[Scheduler] {warning}")

    normalized_queue_times = _normalize_queue_times(queue_times, op_lookup=op_lookup)
    default_queue_enforcement = str((config or {}).get("Queue_Enforcement", "Hard") or "Hard").strip().upper()
    serialization_mode = _campaign_serialization_mode(config)

    sms_tasks: dict[tuple[str, int, str], dict] = {}
    rm_tasks: dict[str, list[dict]] = {}
    sms_queue_status: dict[tuple[str, int, str], str] = {}
    rm_queue_status: dict[str, str] = {}
    machine_intervals = {
        machine: []
        for machines in machine_groups.values()
        for machine in machines
    }
    objective_terms: list[tuple[cp_model.IntVar, int]] = []
    rm_lateness_terms: list[tuple[cp_model.IntVar, int]] = []

    if machine_down_resource and machine_down_hours and machine_down_resource in machine_intervals:
        down_minutes = max(int(float(machine_down_hours) * 60), 0)
        down_start_offset = max(int(float(machine_down_start_hour or 0.0) * 60), 0)
        down_start_offset = min(down_start_offset, max_time)
        if down_minutes > 0:
            down_start = model.NewIntVar(
                down_start_offset,
                down_start_offset,
                f"start_down_{machine_down_resource}",
            )
            down_end = model.NewIntVar(
                down_start_offset + down_minutes,
                down_start_offset + down_minutes,
                f"end_down_{machine_down_resource}",
            )
            machine_intervals[machine_down_resource].append(
                model.NewIntervalVar(
                    down_start,
                    down_minutes,
                    down_end,
                    f"interval_down_{machine_down_resource}",
                )
            )

    def make_interval(name: str, duration: int, candidate_machines: list[str], job_key: str) -> dict:
        duration = max(int(round(duration or 1)), 1)
        frozen = frozen_jobs.get(job_key)
        candidates = list(candidate_machines or [])
        if not candidates:
            raise RuntimeError(f"No candidate machines available for job {job_key}")

        if frozen:
            machine = str(frozen.get("Resource_ID", "")).strip()
            if not machine:
                raise ValueError(f"Frozen job {job_key} is missing Resource_ID.")
            if machine not in candidates:
                raise ValueError(
                    f"Frozen job {job_key} uses incompatible resource {machine}; expected one of {candidates}."
                )
            start_min, end_min, duration = _task_start_end_from_frozen(t0, frozen, duration)
            if start_min > max_time or end_min > max_time:
                raise ValueError(
                    f"Frozen job {job_key} lies outside the planning horizon window ({start_min}, {end_min})."
                )
            start = model.NewIntVar(start_min, start_min, f"start_{name}")
            end = model.NewIntVar(end_min, end_min, f"end_{name}")
            interval = model.NewIntervalVar(start, duration, end, f"interval_{name}_{machine}")
            machine_intervals[machine].append(interval)
            fixed_choice = model.NewBoolVar(f"choose_{name}_{machine}")
            model.Add(fixed_choice == 1)
            return {
                "start": start,
                "end": end,
                "interval": interval,
                "choices": {machine: fixed_choice},
                "candidates": [machine],
                "fixed_machine": machine,
                "frozen_status": (frozen or {}).get("Status"),
                "duration": duration,
            }

        start = model.NewIntVar(0, max_time, f"start_{name}")
        end = model.NewIntVar(0, max_time, f"end_{name}")
        choices = {}
        for machine in candidates:
            literal = model.NewBoolVar(f"choose_{name}_{machine}")
            interval = model.NewOptionalIntervalVar(
                start,
                duration,
                end,
                literal,
                f"interval_{name}_{machine}",
            )
            choices[machine] = literal
            machine_intervals[machine].append(interval)
        model.AddExactlyOne(list(choices.values()))
        return {
            "start": start,
            "end": end,
            "choices": choices,
            "candidates": candidates,
            "fixed_machine": None,
            "frozen_status": None,
            "duration": duration,
        }

    previous_campaign_release_end = None
    for camp in campaigns:
        cid = camp["campaign_id"]
        grade = camp["grade"]
        heats = int(camp["heats"])
        billet_family = camp.get("billet_family") or billet_family_for_grade(grade)
        sms_ops = _campaign_sms_operations(
            camp,
            routing,
            op_lookup=op_lookup,
            allow_defaults=allow_default_masters,
        )
        transfer_times = _campaign_transfer_times(camp, routing, op_lookup=op_lookup)
        sms_times = build_operation_times(
            routing,
            grade,
            billet_family=billet_family,
            resources=resources,
            op_lookup=op_lookup,
            allow_defaults=allow_default_masters,
        )
        _validate_campaign_master_data(
            camp,
            sms_ops,
            sms_times,
            machine_groups,
            allow_defaults=allow_default_masters,
        )
        ccm_end_vars = []
        sms_end_vars = []
        prev_stage_tasks = {}
        first_eaf_task = None

        for heat_idx in range(heats):
            prefix = f"{cid}_H{heat_idx + 1}"
            previous_task = None
            previous_op = None

            for op in sms_ops:
                job_key = f"{cid}-H{heat_idx + 1}-{op}"
                op_duration = _operation_duration(
                    sms_times.get(op, {}),
                    include_setup=heat_idx == 0,
                )
                op_task = make_interval(
                    f"{prefix}_{op}",
                    op_duration,
                    machine_groups.get(op, []),
                    job_key,
                )
                sms_tasks[(cid, heat_idx, op)] = op_task
                sms_queue_status[(cid, heat_idx, op)] = ""
                sms_end_vars.append(op_task["end"])

                # Fix 4.1: Add soft preference cost for non-preferred resource selection
                if op_task.get("fixed_machine") is None:  # Only if not frozen/fixed
                    preferred = _preferred_resource_for_operation(
                        routing, op, grade=grade, op_lookup=op_lookup
                    )
                    if preferred and preferred in op_task.get("choices", {}):
                        for machine in op_task.get("candidates", []):
                            if machine != preferred:
                                cost = model.NewIntVar(0, 1, f"cost_{prefix}_{op}_{machine}")
                                model.Add(cost == 1).OnlyEnforceIf(op_task["choices"][machine])
                                objective_terms.append((cost, 10))  # Soft penalty: prefer preferred resource

                if op == "EAF" and heat_idx == 0:
                    first_eaf_task = op_task
                if prev_stage_tasks.get(op):
                    model.Add(prev_stage_tasks[op]["end"] <= op_task["start"])

                if previous_task is not None and previous_op is not None:
                    transfer_gap = int(transfer_times.get((previous_op, op), 0) or 0)
                    queue_rule = normalized_queue_times.get((previous_op, op))
                    min_queue = int((queue_rule or {}).get("min", 0) or 0)
                    model.Add(previous_task["end"] + transfer_gap + min_queue <= op_task["start"])

                    max_queue = int((queue_rule or {}).get("max", 9999) or 9999)
                    enforcement = str((queue_rule or {}).get("enforcement", default_queue_enforcement) or default_queue_enforcement).strip().upper()
                    if max_queue < 9999:
                        if enforcement == "HARD":
                            model.Add(op_task["start"] <= previous_task["end"] + transfer_gap + max_queue)
                        else:
                            q_viol = model.NewIntVar(0, max_time, f"qviol_{cid}_{heat_idx + 1}_{previous_op}_{op}")
                            model.Add(q_viol >= op_task["start"] - (previous_task["end"] + transfer_gap + max_queue))
                            objective_terms.append((q_viol, QUEUE_VIOLATION_WEIGHT))  # Proportional: q_viol = violation_magnitude

                previous_task = op_task
                previous_op = op
                prev_stage_tasks[op] = op_task

            ccm_task = sms_tasks.get((cid, heat_idx, "CCM"))
            if ccm_task:
                ccm_end_vars.append(ccm_task["end"])

        if previous_campaign_release_end is not None and first_eaf_task is not None:
            model.Add(previous_campaign_release_end <= first_eaf_task["start"])

        rm_orders = _production_orders_for_campaign(camp)
        rm_tasks[cid] = []
        previous_rm_end = None
        previous_section = None
        estimated_rm_duration = 0
        rm_end_vars = []

        for rm_idx, rm_order in enumerate(rm_orders, start=1):
            section = pd.to_numeric(
                pd.Series([rm_order.get("section_mm")]),
                errors="coerce",
            ).fillna(6.5).iloc[0]
            include_setup = previous_section is None or section != previous_section
            rm_duration = _rm_duration(
                rm_order,
                grade,
                routing,
                resources=resources,
                op_lookup=op_lookup,
                include_setup=include_setup,
                allow_defaults=allow_default_masters,
            )
            estimated_rm_duration += rm_duration
            rm_job_id = f"{rm_order.get('production_order_id', f'{cid}-PO{rm_idx:02d}')}-RM"
            rm_task = make_interval(
                f"{cid}_RM_{rm_idx}",
                rm_duration,
                machine_groups["RM"],
                rm_job_id,
            )

            # Fix 4.1: Add soft preference cost for RM resource selection
            if rm_task.get("fixed_machine") is None:  # Only if not frozen/fixed
                preferred = _preferred_resource_for_operation(
                    routing, "RM", grade=grade, sku_id=rm_order.get("sku_id"), op_lookup=op_lookup
                )
                if preferred and preferred in rm_task.get("choices", {}):
                    for machine in rm_task.get("candidates", []):
                        if machine != preferred:
                            cost = model.NewIntVar(0, 1, f"cost_{cid}_RM_{rm_idx}_{machine}")
                            model.Add(cost == 1).OnlyEnforceIf(rm_task["choices"][machine])
                            objective_terms.append((cost, 10))  # Soft penalty: prefer preferred resource

            if previous_rm_end is None:
                last_ccm_end = model.NewIntVar(0, max_time, f"last_ccm_end_{cid}")
                model.AddMaxEquality(last_ccm_end, ccm_end_vars)
                transfer_gap = int(transfer_times.get(("CCM", "RM"), 0) or 0)
                queue_rule = normalized_queue_times.get(("CCM", "RM"))
                min_queue = int((queue_rule or {}).get("min", 0) or 0)
                model.Add(last_ccm_end + transfer_gap + min_queue <= rm_task["start"])
                max_queue = int((queue_rule or {}).get("max", 9999) or 9999)
                enforcement = str((queue_rule or {}).get("enforcement", default_queue_enforcement) or default_queue_enforcement).strip().upper()
                if max_queue < 9999:
                    if enforcement == "HARD":
                        model.Add(rm_task["start"] <= last_ccm_end + transfer_gap + max_queue)
                    else:
                        q_viol = model.NewIntVar(0, max_time, f"qviol_{cid}_CCM_RM")
                        model.Add(q_viol >= rm_task["start"] - (last_ccm_end + transfer_gap + max_queue))
                        objective_terms.append((q_viol, 100))  # Proportional per-minute penalty (was QUEUE_VIOLATION_WEIGHT=500)
            else:
                model.Add(previous_rm_end <= rm_task["start"])

            rm_due = pd.to_datetime(rm_order.get("due_date", camp["due_date"]))
            due_min = max(0, int((rm_due - t0).total_seconds() / 60))
            lateness = model.NewIntVar(0, max_time, f"late_{rm_job_id}")
            model.AddMaxEquality(lateness, [rm_task["end"] - due_min, model.NewConstant(0)])
            weight = _priority_weight(int(rm_order.get("priority_rank", camp.get("priority_rank", 9))))
            objective_terms.append((lateness, weight))
            rm_lateness_terms.append((lateness, weight))

            rm_tasks[cid].append(
                {
                    "job_id": rm_job_id,
                    "start": rm_task["start"],
                    "end": rm_task["end"],
                    "choices": rm_task["choices"],
                    "candidates": rm_task["candidates"],
                    "fixed_machine": rm_task["fixed_machine"],
                    "frozen_status": rm_task["frozen_status"],
                    "order": dict(rm_order),
                    "duration": rm_duration,
                    "grade": grade,
                }
            )
            previous_rm_end = rm_task["end"]
            previous_section = section
            rm_end_vars.append(rm_task["end"])

        if ccm_end_vars:
            last_ccm_end = model.NewIntVar(0, max_time, f"campaign_ccm_end_{cid}")
            model.AddMaxEquality(last_ccm_end, ccm_end_vars)
            due_min = max(0, int((pd.to_datetime(camp["due_date"]) - t0).total_seconds() / 60))
            sms_due_min = max(due_min - estimated_rm_duration, 0)
            sms_lateness = model.NewIntVar(0, max_time, f"sms_late_{cid}")
            model.AddMaxEquality(sms_lateness, [last_ccm_end - sms_due_min, model.NewConstant(0)])
            objective_terms.append(
                (
                    sms_lateness,
                    max(1, math.ceil(_priority_weight(int(camp.get("priority_rank", 9))))),  # Fix 1.1: Removed 0.5x discount
                )
            )
            completion_candidates = list(rm_end_vars) if rm_end_vars else [last_ccm_end]
            campaign_completion_end = model.NewIntVar(0, max_time, f"campaign_end_{cid}")
            model.AddMaxEquality(campaign_completion_end, completion_candidates)
            sms_completion_end = model.NewIntVar(0, max_time, f"campaign_sms_end_{cid}")
            model.AddMaxEquality(sms_completion_end, sms_end_vars or ccm_end_vars)
            previous_campaign_release_end = (
                campaign_completion_end
                if serialization_mode == "STRICT_END_TO_END"
                else sms_completion_end
            )

    for machine, intervals in machine_intervals.items():
        if intervals:
            model.AddNoOverlap(intervals)

    rm_all_tasks = [task for tasks in rm_tasks.values() for task in tasks]
    for left_idx, left in enumerate(rm_all_tasks):
        for right_idx in range(left_idx + 1, len(rm_all_tasks)):
            right = rm_all_tasks[right_idx]
            shared_machines = set(left.get("choices", {})).intersection(right.get("choices", {}))
            if not shared_machines:
                continue
            change_lr = _changeover_minutes(changeover_matrix, left.get("grade"), right.get("grade"))
            change_rl = _changeover_minutes(changeover_matrix, right.get("grade"), left.get("grade"))
            for machine in shared_machines:
                left_lit = left["choices"][machine]
                right_lit = right["choices"][machine]
                left_before = model.NewBoolVar(f"rm_{left_idx}_before_{right_idx}_{machine}")
                right_before = model.NewBoolVar(f"rm_{right_idx}_before_{left_idx}_{machine}")
                model.Add(left_before <= left_lit)
                model.Add(left_before <= right_lit)
                model.Add(right_before <= left_lit)
                model.Add(right_before <= right_lit)
                model.Add(left_before + right_before >= left_lit + right_lit - 1)
                model.Add(left_before + right_before <= 1)
                model.Add(right["start"] >= left["end"] + change_lr).OnlyEnforceIf(left_before)
                model.Add(left["start"] >= right["end"] + change_rl).OnlyEnforceIf(right_before)

    if objective_terms:
        model.Minimize(sum(var * weight for var, weight in objective_terms))

    solver = cp_model.CpSolver()
    solver.parameters.max_time_in_seconds = max(float(solver_time_limit_sec or 30), 1.0)
    solver.parameters.num_search_workers = 4
    status = solver.Solve(model)

    status_str = {
        cp_model.OPTIMAL: "OPTIMAL",
        cp_model.FEASIBLE: "FEASIBLE",
        cp_model.INFEASIBLE: "INFEASIBLE",
        cp_model.UNKNOWN: "UNKNOWN",
    }.get(status, "UNKNOWN")

    if status not in (cp_model.OPTIMAL, cp_model.FEASIBLE):
        print(f"[Scheduler] No solution: {status_str}. Falling back to greedy.")
        return _greedy_fallback(
            campaigns,
            resources=resources,
            planning_start=t0,
            planning_horizon_days=planning_horizon_days,
            machine_down_resource=machine_down_resource,
            machine_down_hours=machine_down_hours,
            machine_down_start_hour=machine_down_start_hour,
            frozen_jobs=frozen_jobs,
            routing=routing,
            queue_times=queue_times,
            changeover_matrix=changeover_matrix,
            config=config,
            solver_detail=f"CP_SAT_{status_str}",
        )

    schedule_rows = []
    campaign_rows = []

    for camp in campaigns:
        cid = camp["campaign_id"]
        grade = camp["grade"]
        heats = int(camp["heats"])
        sms_ops = _campaign_sms_operations(
            camp,
            routing,
            op_lookup=op_lookup,
            allow_defaults=allow_default_masters,
        )
        transfer_times = _campaign_transfer_times(camp, routing, op_lookup=op_lookup)

        eaf_starts = []
        ccm_starts = []
        ccm_ends = []
        ccm_end_by_heat: dict[int, datetime] = {}
        for heat_idx in range(heats):
            previous_end_dt = None
            previous_op = None
            for op in sms_ops:
                task = sms_tasks.get((cid, heat_idx, op))
                if not task:
                    continue
                machine = _resolve_selected_machine(solver, task)
                start_dt = t0 + timedelta(minutes=solver.Value(task["start"]))
                end_dt = t0 + timedelta(minutes=solver.Value(task["end"]))
                if op == "EAF":
                    eaf_starts.append(start_dt)
                if op == "CCM":
                    ccm_starts.append(start_dt)
                    ccm_ends.append(end_dt)
                    ccm_end_by_heat[heat_idx] = end_dt
                frozen_status = str(task.get("frozen_status") or "").strip()
                status_text = frozen_status or ("LATE" if end_dt > pd.to_datetime(camp["due_date"]) else "Scheduled")
                queue_rule = normalized_queue_times.get((previous_op, op)) if previous_op else None
                transfer_gap = int(transfer_times.get((previous_op, op), 0) or 0) if previous_op else 0
                queue_gap = None
                if previous_end_dt is not None:
                    queue_gap = _queue_wait_minutes(start_dt, previous_end_dt, transfer_gap)
                queue_status = _queue_status(queue_gap, queue_rule)
                sms_queue_status[(cid, heat_idx, op)] = queue_status
                schedule_rows.append(
                    {
                        "Job_ID": f"{cid}-H{heat_idx + 1}-{op}",
                        "Campaign": cid,
                        "SO_ID": _so_pool_display(camp.get("so_ids", [])),
                        "Grade": grade,
                        "Section_mm": _section_display(camp.get("section_mm", ""), camp.get("sections_covered", "")),
                        "SKU_ID": f"{billet_family_for_grade(grade)}-{grade.replace(' ', '').replace('-', '')}",
                        "Operation": op,
                        "Resource_ID": machine,
                        "Planned_Start": start_dt.strftime("%Y-%m-%d %H:%M"),
                        "Planned_End": end_dt.strftime("%Y-%m-%d %H:%M"),
                        "Duration_Hrs": round((solver.Value(task["end"]) - solver.Value(task["start"])) / 60, 2),
                        "Heat_No": heat_idx + 1,
                        "Qty_MT": HEAT_SIZE_MT,
                        "Queue_Violation": queue_status,
                        "Status": status_text,
                        "_sort_start": start_dt,
                        "_sort_op": op_order.get(op, 99),
                    }
                )
                previous_end_dt = end_dt
                previous_op = op

        rm_start_times = []
        rm_end_times = []
        rm_machines = []
        rm_late = False
        last_ccm_dt = max(ccm_ends) if ccm_ends else None
        for rm_task in rm_tasks.get(cid, []):
            machine = _resolve_selected_machine(solver, rm_task)
            start_dt = t0 + timedelta(minutes=solver.Value(rm_task["start"]))
            end_dt = t0 + timedelta(minutes=solver.Value(rm_task["end"]))
            rm_start_times.append(start_dt)
            rm_end_times.append(end_dt)
            rm_machines.append(machine)

            order = rm_task["order"]
            order_due = pd.to_datetime(order.get("due_date", camp["due_date"]))
            frozen_status = str(rm_task.get("frozen_status") or "").strip()
            status_text = frozen_status or ("LATE" if end_dt > order_due else "Scheduled")
            rm_late = rm_late or status_text == "LATE"
            queue_status = ""
            if not rm_queue_status.get(rm_task["job_id"]) and last_ccm_dt is not None and len(rm_start_times) == 1:
                queue_rule = normalized_queue_times.get(("CCM", "RM"))
                transfer_gap = int(transfer_times.get(("CCM", "RM"), 0) or 0)
                queue_gap = _queue_wait_minutes(start_dt, last_ccm_dt, transfer_gap)
                queue_status = _queue_status(queue_gap, queue_rule)
                rm_queue_status[rm_task["job_id"]] = queue_status
            else:
                queue_status = rm_queue_status.get(rm_task["job_id"], "")
            schedule_rows.append(
                {
                    "Job_ID": rm_task["job_id"],
                    "Campaign": cid,
                    "SO_ID": str(order.get("so_id", "")),
                    "Grade": grade,
                    "Section_mm": _section_display(order.get("section_mm", "")),
                    "SKU_ID": order.get("sku_id", ""),
                    "Operation": "RM",
                    "Resource_ID": machine,
                    "Planned_Start": start_dt.strftime("%Y-%m-%d %H:%M"),
                    "Planned_End": end_dt.strftime("%Y-%m-%d %H:%M"),
                    "Duration_Hrs": round((solver.Value(rm_task["end"]) - solver.Value(rm_task["start"])) / 60, 2),
                    "Heat_No": "",
                    "Qty_MT": round(float(order.get("qty_mt", 0.0)), 3),
                    "Queue_Violation": queue_status,
                    "Status": status_text,
                    "_sort_start": start_dt,
                    "_sort_op": op_order.get("RM", OPERATION_ORDER["RM"]),
                }
            )

        first_eaf = min(eaf_starts) if eaf_starts else None
        first_ccm = min(ccm_starts) if ccm_starts else None
        last_ccm = max(ccm_ends) if ccm_ends else None
        first_rm = min(rm_start_times) if rm_start_times else None
        last_rm = max(rm_end_times) if rm_end_times else None
        campaign_rows.append(
            {
                "Campaign_ID": cid,
                "Campaign_Group": camp.get("campaign_group", ""),
                "Grade": camp["grade"],
                "Section_mm": camp.get("section_mm", ""),
                "Sections_Covered": camp.get("sections_covered", ""),
                "Total_MT": camp["total_coil_mt"],
                "Heats": camp["heats"],
                "Heats_Calc_Method": camp.get("heats_calc_method", ""),
                "Heats_Calc_Warnings": _heats_calc_warning_text(camp.get("heats_calc_warnings")),
                "Order_Count": camp.get("order_count", len(camp.get("so_ids", []))),
                "Priority": camp.get("priority", ""),
                "Release_Status": camp.get("release_status", "RELEASED"),
                "Material_Issue": camp.get("material_issue", ""),
                "EAF_Start": first_eaf.strftime("%Y-%m-%d %H:%M") if first_eaf else "",
                "CCM_Start": first_ccm.strftime("%Y-%m-%d %H:%M") if first_ccm else "",
                "RM_Start": first_rm.strftime("%Y-%m-%d %H:%M") if first_rm else "",
                "RM_End": last_rm.strftime("%Y-%m-%d %H:%M") if last_rm else "",
                "Due_Date": pd.to_datetime(camp["due_date"]).strftime("%Y-%m-%d"),
                "Status": "LATE" if rm_late else "On Time",
                "SOs_Covered": ", ".join(camp.get("so_ids", [])),
                "_sort_eaf_start": first_eaf or last_ccm or first_rm or pd.to_datetime(camp["due_date"]),
            }
        )

    schedule_df = pd.DataFrame(schedule_rows)
    if not schedule_df.empty:
        schedule_df = schedule_df.sort_values(
            ["_sort_start", "Resource_ID", "Campaign", "_sort_op", "Job_ID"]
        ).reset_index(drop=True)
        schedule_df = schedule_df.drop(columns=["_sort_start", "_sort_op"])

    campaign_df = pd.DataFrame(campaign_rows)
    if not campaign_df.empty:
        campaign_df = campaign_df.sort_values(["Campaign_ID", "_sort_eaf_start"]).reset_index(drop=True)
        campaign_df = campaign_df.drop(columns=["_sort_eaf_start"])

    weighted_lateness_minutes = sum(solver.Value(var) * weight for var, weight in rm_lateness_terms)
    return {
        "heat_schedule": schedule_df,
        "campaign_schedule": campaign_df,
        "solver_status": status_str,
        "solver_detail": "CP_SAT_SOLVED",
        "campaign_serialization_mode": serialization_mode,
        "master_data_mode": _master_data_mode(config),
        "allow_default_masters": allow_default_masters,
        "planning_start": t0,
        "planning_horizon_days": planning_horizon_days,
        "weighted_lateness_hours": round(weighted_lateness_minutes / 60.0, 2),
    }


def _greedy_fallback(
    campaigns: list,
    resources: pd.DataFrame | None = None,
    planning_start=None,
    planning_horizon_days: int = 14,
    machine_down_resource: str | None = None,
    machine_down_hours: float = 0.0,
    machine_down_start_hour: float = 0.0,
    frozen_jobs: dict | None = None,
    routing: pd.DataFrame | None = None,
    queue_times: dict | None = None,
    changeover_matrix: pd.DataFrame | None = None,
    config: dict | None = None,
    solver_detail: str = "",
) -> dict:
    """Greedy fallback that uses the same campaign/production-order structure."""
    frozen_jobs = frozen_jobs or {}
    t0 = _planning_start(planning_start, frozen_jobs)
    op_lookup = _build_op_lookup(resources)
    op_order = _build_operation_order(routing, op_lookup)
    allow_default_masters = _allow_scheduler_default_masters(config)
    machine_groups = _machine_groups(resources, op_lookup=op_lookup, allow_defaults=allow_default_masters)
    normalized_queue_times = _normalize_queue_times(queue_times, op_lookup=op_lookup)
    default_queue_enforcement = str((config or {}).get("Queue_Enforcement", "Hard") or "Hard").strip().upper()
    serialization_mode = _campaign_serialization_mode(config)
    machine_clocks = {
        machine: 0
        for machines in machine_groups.values()
        for machine in machines
    }
    machine_last_grade = {machine: "" for machine in machine_clocks}
    schedule_rows = []
    campaign_rows = []

    downtime_windows = {}
    if machine_down_resource and machine_down_hours:
        down_start = max(int(float(machine_down_start_hour or 0.0) * 60), 0)
        down_end = down_start + max(int(float(machine_down_hours) * 60), 0)
        if down_end > down_start:
            downtime_windows[machine_down_resource] = (down_start, down_end)

    for job_id, frozen in frozen_jobs.items():
        resource_id = str((frozen or {}).get("Resource_ID", "")).strip()
        if not resource_id:
            raise ValueError(f"Frozen job {job_id} is missing Resource_ID.")
        if resource_id not in machine_clocks:
            raise ValueError(f"Frozen job {job_id} uses unknown resource {resource_id}.")
        _, end_min, _ = _task_start_end_from_frozen(t0, frozen, 1)
        machine_clocks[resource_id] = max(machine_clocks.get(resource_id, 0), end_min)

    weighted_lateness_minutes = 0
    previous_campaign_release_end = 0
    for camp in campaigns:
        cid = camp["campaign_id"]
        grade = camp["grade"]
        heats = int(camp["heats"])
        ops = _campaign_sms_operations(
            camp,
            routing,
            op_lookup=op_lookup,
            allow_defaults=allow_default_masters,
        )
        transfer_times = _campaign_transfer_times(camp, routing, op_lookup=op_lookup)
        sms_times = build_operation_times(
            routing,
            grade,
            billet_family=camp.get("billet_family") or billet_family_for_grade(grade),
            resources=resources,
            op_lookup=op_lookup,
            allow_defaults=allow_default_masters,
        )
        _validate_campaign_master_data(
            camp,
            ops,
            sms_times,
            machine_groups,
            allow_defaults=allow_default_masters,
        )

        first_eaf_start = None
        first_ccm_start = None
        last_ccm_end = 0
        last_sms_end = 0
        previous_stage_end = {op: 0 for op in ops}

        for heat_idx in range(heats):
            op_end = 0
            previous_op = None
            for op in ops:
                job_id = f"{cid}-H{heat_idx + 1}-{op}"
                frozen = frozen_jobs.get(job_id)
                candidates = machine_groups[op]
                duration = _operation_duration(
                    sms_times.get(op, {}),
                    include_setup=heat_idx == 0,
                )
                if frozen:
                    resource_id = str(frozen.get("Resource_ID", "")).strip()
                    if not resource_id:
                        raise ValueError(f"Frozen job {job_id} is missing Resource_ID.")
                    if resource_id not in candidates:
                        raise ValueError(
                            f"Frozen job {job_id} uses incompatible resource {resource_id}; expected one of {candidates}."
                        )
                    start_min, end_min, _ = _task_start_end_from_frozen(t0, frozen, duration)
                    start_dt = t0 + timedelta(minutes=start_min)
                    end_dt = t0 + timedelta(minutes=end_min)
                    machine_clocks[resource_id] = max(machine_clocks.get(resource_id, 0), end_min)
                    status_text = str(frozen.get("Status") or "RUNNING")
                else:
                    best_choice = None
                    for resource_id in candidates:
                        queue_rule = normalized_queue_times.get((previous_op, op)) if previous_op else None
                        min_queue = int((queue_rule or {}).get("min", 0) or 0)
                        transfer_gap = int(transfer_times.get((previous_op, op), 0) or 0) if previous_op else 0
                        candidate_start = max(
                            machine_clocks.get(resource_id, 0),
                            op_end + transfer_gap + min_queue,
                            previous_stage_end.get(op, 0),
                        )
                        if heat_idx == 0 and op == "EAF":
                            candidate_start = max(candidate_start, previous_campaign_release_end)
                        candidate_start = _next_available_start(
                            candidate_start,
                            duration,
                            downtime_windows.get(resource_id),
                        )
                        max_queue = int((queue_rule or {}).get("max", 9999) or 9999)
                        enforcement = str((queue_rule or {}).get("enforcement", default_queue_enforcement) or default_queue_enforcement).strip().upper()
                        if previous_op and max_queue < 9999 and enforcement == "HARD" and candidate_start > op_end + transfer_gap + max_queue:
                            continue
                        candidate_end = candidate_start + duration
                        choice = (candidate_end, candidate_start, resource_id)
                        if best_choice is None or choice < best_choice:
                            best_choice = choice
                    if best_choice is None:
                        for resource_id in candidates:
                            candidate_start = max(machine_clocks.get(resource_id, 0), op_end, previous_stage_end.get(op, 0))
                            if heat_idx == 0 and op == "EAF":
                                candidate_start = max(candidate_start, previous_campaign_release_end)
                            candidate_start = _next_available_start(
                                candidate_start,
                                duration,
                                downtime_windows.get(resource_id),
                            )
                            candidate_end = candidate_start + duration
                            choice = (candidate_end, candidate_start, resource_id)
                            if best_choice is None or choice < best_choice:
                                best_choice = choice
                    _, start_min, resource_id = best_choice
                    end_min = start_min + duration
                    machine_clocks[resource_id] = end_min
                    start_dt = t0 + timedelta(minutes=start_min)
                    end_dt = t0 + timedelta(minutes=end_min)
                    status_text = "LATE" if end_dt > pd.to_datetime(camp["due_date"]) else "Scheduled"
                queue_rule = normalized_queue_times.get((previous_op, op)) if previous_op else None
                transfer_gap = int(transfer_times.get((previous_op, op), 0) or 0) if previous_op else 0
                queue_gap = _queue_wait_minutes(start_min, op_end, transfer_gap) if previous_op else None
                queue_status = _queue_status(queue_gap, queue_rule)

                if op == "EAF" and first_eaf_start is None:
                    first_eaf_start = start_dt
                if op == "CCM" and first_ccm_start is None:
                    first_ccm_start = start_dt
                if op == "CCM":
                    last_ccm_end = max(last_ccm_end, end_min)
                last_sms_end = max(last_sms_end, end_min)

                schedule_rows.append(
                    {
                        "Job_ID": job_id,
                        "Campaign": cid,
                        "SO_ID": _so_pool_display(camp.get("so_ids", [])),
                        "Grade": grade,
                        "Section_mm": _section_display(camp.get("section_mm", ""), camp.get("sections_covered", "")),
                        "SKU_ID": f"{billet_family_for_grade(grade)}-{grade.replace(' ', '').replace('-', '')}",
                        "Operation": op,
                        "Resource_ID": resource_id,
                        "Planned_Start": start_dt.strftime("%Y-%m-%d %H:%M"),
                        "Planned_End": end_dt.strftime("%Y-%m-%d %H:%M"),
                        "Duration_Hrs": round((end_min - start_min) / 60, 2),
                        "Heat_No": heat_idx + 1,
                        "Qty_MT": HEAT_SIZE_MT,
                        "Queue_Violation": queue_status,
                        "Status": status_text,
                        "_sort_start": start_dt,
                        "_sort_op": op_order.get(op, 99),
                    }
                )
                op_end = end_min
                previous_stage_end[op] = end_min
                previous_op = op

        rm_orders = _production_orders_for_campaign(camp)
        previous_rm_end = last_ccm_end
        previous_section = None
        rm_start_times = []
        rm_end_times = []
        rm_end_offsets = []
        rm_machines = []
        rm_late = False

        for rm_idx, order in enumerate(rm_orders, start=1):
            section = pd.to_numeric(pd.Series([order.get("section_mm")]), errors="coerce").fillna(6.5).iloc[0]
            include_setup = previous_section is None or section != previous_section
            job_id = f"{order.get('production_order_id', f'{cid}-PO{rm_idx:02d}')}-RM"
            frozen = frozen_jobs.get(job_id)

            if frozen:
                resource_id = str(frozen.get("Resource_ID", "")).strip()
                if not resource_id:
                    raise ValueError(f"Frozen job {job_id} is missing Resource_ID.")
                if resource_id not in machine_groups["RM"]:
                    raise ValueError(
                        f"Frozen job {job_id} uses incompatible resource {resource_id}; expected one of {machine_groups['RM']}."
                    )
                start_min, end_min, _ = _task_start_end_from_frozen(t0, frozen, 1)
                start_dt = t0 + timedelta(minutes=start_min)
                end_dt = t0 + timedelta(minutes=end_min)
                machine_clocks[resource_id] = max(machine_clocks.get(resource_id, 0), end_min)
                machine_last_grade[resource_id] = grade
                status_text = str(frozen.get("Status") or "RUNNING")
            else:
                best_choice = None
                for resource_id in machine_groups["RM"]:
                    duration = _rm_duration(
                        order,
                        grade,
                        routing,
                        resources=resources,
                        op_lookup=op_lookup,
                        include_setup=include_setup,
                        allow_defaults=allow_default_masters,
                    )
                    queue_rule = normalized_queue_times.get(("CCM", "RM")) if previous_section is None else None
                    min_queue = int((queue_rule or {}).get("min", 0) or 0)
                    transfer_gap = int(transfer_times.get(("CCM", "RM"), 0) or 0) if previous_section is None else 0
                    candidate_start = max(machine_clocks.get(resource_id, 0), previous_rm_end + transfer_gap + min_queue)
                    candidate_start = _next_available_start(
                        candidate_start,
                        duration,
                        downtime_windows.get(resource_id),
                    )
                    max_queue = int((queue_rule or {}).get("max", 9999) or 9999)
                    enforcement = str((queue_rule or {}).get("enforcement", default_queue_enforcement) or default_queue_enforcement).strip().upper()
                    if previous_section is None and max_queue < 9999 and enforcement == "HARD" and candidate_start > previous_rm_end + transfer_gap + max_queue:
                        continue
                    candidate_end = candidate_start + duration
                    choice = (candidate_end, candidate_start, resource_id, duration)
                    if best_choice is None or choice < best_choice:
                        best_choice = choice
                if best_choice is None:
                    for resource_id in machine_groups["RM"]:
                        duration = _rm_duration(
                            order,
                            grade,
                            routing,
                            resources=resources,
                            op_lookup=op_lookup,
                            include_setup=include_setup,
                            allow_defaults=allow_default_masters,
                        )
                        candidate_start = max(machine_clocks.get(resource_id, 0), previous_rm_end)
                        candidate_start = _next_available_start(
                            candidate_start,
                            duration,
                            downtime_windows.get(resource_id),
                        )
                        candidate_end = candidate_start + duration
                        choice = (candidate_end, candidate_start, resource_id, duration)
                        if best_choice is None or choice < best_choice:
                            best_choice = choice
                _, start_min, resource_id, duration = best_choice
                end_min = start_min + duration
                machine_clocks[resource_id] = end_min
                machine_last_grade[resource_id] = grade
                start_dt = t0 + timedelta(minutes=start_min)
                end_dt = t0 + timedelta(minutes=end_min)
                order_due = pd.to_datetime(order.get("due_date", camp["due_date"]))
                lateness_minutes = max(int((end_dt - order_due).total_seconds() / 60), 0)
                weight = _priority_weight(int(order.get("priority_rank", camp.get("priority_rank", 9))))
                weighted_lateness_minutes += lateness_minutes * weight
                status_text = "LATE" if end_dt > order_due else "Scheduled"
            queue_status = ""
            if previous_section is None:
                queue_rule = normalized_queue_times.get(("CCM", "RM"))
                transfer_gap = int(transfer_times.get(("CCM", "RM"), 0) or 0)
                queue_gap = _queue_wait_minutes(start_min, last_ccm_end, transfer_gap)
                queue_status = _queue_status(queue_gap, queue_rule)

            rm_start_times.append(start_dt)
            rm_end_times.append(end_dt)
            rm_end_offsets.append(end_min)
            rm_machines.append(resource_id)
            rm_late = rm_late or status_text == "LATE"
            schedule_rows.append(
                {
                    "Job_ID": job_id,
                    "Campaign": cid,
                    "SO_ID": str(order.get("so_id", "")),
                    "Grade": grade,
                    "Section_mm": _section_display(order.get("section_mm", "")),
                    "SKU_ID": order.get("sku_id", ""),
                    "Operation": "RM",
                    "Resource_ID": resource_id,
                    "Planned_Start": start_dt.strftime("%Y-%m-%d %H:%M"),
                    "Planned_End": end_dt.strftime("%Y-%m-%d %H:%M"),
                    "Duration_Hrs": round((end_min - start_min) / 60, 2),
                    "Heat_No": "",
                    "Qty_MT": round(float(order.get("qty_mt", 0.0)), 3),
                    "Queue_Violation": queue_status,
                    "Status": status_text,
                    "_sort_start": start_dt,
                    "_sort_op": op_order.get("RM", OPERATION_ORDER["RM"]),
                }
            )
            previous_rm_end = end_min
            previous_section = section

        previous_campaign_release_end = (
            max(rm_end_offsets) if (serialization_mode == "STRICT_END_TO_END" and rm_end_offsets) else last_sms_end
        )

        campaign_rows.append(
            {
                "Campaign_ID": cid,
                "Campaign_Group": camp.get("campaign_group", ""),
                "Grade": camp["grade"],
                "Section_mm": camp.get("section_mm", ""),
                "Sections_Covered": camp.get("sections_covered", ""),
                "Total_MT": camp["total_coil_mt"],
                "Heats": camp["heats"],
                "Heats_Calc_Method": camp.get("heats_calc_method", ""),
                "Heats_Calc_Warnings": _heats_calc_warning_text(camp.get("heats_calc_warnings")),
                "Order_Count": camp.get("order_count", len(camp.get("so_ids", []))),
                "Priority": camp.get("priority", ""),
                "Release_Status": camp.get("release_status", "RELEASED"),
                "Material_Issue": camp.get("material_issue", ""),
                "EAF_Start": first_eaf_start.strftime("%Y-%m-%d %H:%M") if first_eaf_start else "",
                "CCM_Start": first_ccm_start.strftime("%Y-%m-%d %H:%M") if first_ccm_start else "",
                "RM_Start": min(rm_start_times).strftime("%Y-%m-%d %H:%M") if rm_start_times else "",
                "RM_End": max(rm_end_times).strftime("%Y-%m-%d %H:%M") if rm_end_times else "",
                "Due_Date": pd.to_datetime(camp["due_date"]).strftime("%Y-%m-%d"),
                "Status": "LATE" if rm_late else "On Time",
                "SOs_Covered": ", ".join(camp.get("so_ids", [])),
                "_sort_eaf_start": first_eaf_start or min(rm_start_times) if rm_start_times else pd.to_datetime(camp["due_date"]),
            }
        )

    schedule_df = pd.DataFrame(schedule_rows)
    if not schedule_df.empty:
        schedule_df = schedule_df.sort_values(
            ["_sort_start", "Resource_ID", "Campaign", "_sort_op", "Job_ID"]
        ).reset_index(drop=True)
        schedule_df = schedule_df.drop(columns=["_sort_start", "_sort_op"])

    campaign_df = pd.DataFrame(campaign_rows)
    if not campaign_df.empty:
        campaign_df = campaign_df.sort_values(["Campaign_ID", "_sort_eaf_start"]).reset_index(drop=True)
        campaign_df = campaign_df.drop(columns=["_sort_eaf_start"])

    return {
        "heat_schedule": schedule_df,
        "campaign_schedule": campaign_df,
        "solver_status": "GREEDY",
        "solver_detail": solver_detail or "GREEDY_HEURISTIC",
        "campaign_serialization_mode": serialization_mode,
        "master_data_mode": _master_data_mode(config),
        "allow_default_masters": allow_default_masters,
        "planning_start": t0,
        "planning_horizon_days": max(int(planning_horizon_days or 14), 1),
        "weighted_lateness_hours": round(weighted_lateness_minutes / 60.0, 2),
    }
