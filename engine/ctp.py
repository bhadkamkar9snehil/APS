"""
Smart capable-to-promise engine.

Builds a ghost demand request, checks inventory/material availability net of the
committed plan, evaluates merge/new-campaign scenarios, schedules around frozen
jobs, and returns a planner-grade CTP result with alternatives.
"""
from __future__ import annotations

from datetime import datetime
from typing import Any

import pandas as pd

from engine.bom_explosion import inventory_map
from engine.campaign import build_campaigns, _heats_needed_from_lines
from engine.config import get_config, resolve_config_bool, resolve_config_float, resolve_config_int, resolve_config_value
from engine.scheduler import schedule

COMMITTED_STATUSES = {"RELEASED", "RUNNING LOCK"}
DEGRADED_INVENTORY_LINEAGE_STATUSES = {"RECOMPUTED_FROM_CONSUMPTION", "CONSERVATIVE_BLEND"}
HEURISTIC_SOLVER_STATUSES = {"GREEDY", "UNKNOWN"}

DEFAULT_DECISION_PRECEDENCE = {
    "PROMISE_CONFIRMED_STOCK_ONLY": 1,
    "PROMISE_CONFIRMED_MERGED": 2,
    "PROMISE_CONFIRMED_NEW_CAMPAIGN": 3,
    "PROMISE_HEURISTIC_ONLY": 4,
    "PROMISE_LATER_DATE": 5,
    "PROMISE_SPLIT_REQUIRED": 6,
    "PROMISE_CONDITIONAL_EXPEDITE": 7,
    "CANNOT_PROMISE_POLICY_ONLY": 8,
    "CANNOT_PROMISE_CAPACITY": 9,
    "CANNOT_PROMISE_MATERIAL": 10,
    "CANNOT_PROMISE_INVENTORY_TRUST": 11,
    "CANNOT_PROMISE_MASTER_DATA": 12,
    "CANNOT_PROMISE_MIXED_BLOCKERS": 13,
}


def _config_sequence(value: object, default: list[str]) -> list[str]:
    if value is None:
        return list(default)
    if isinstance(value, str):
        items = [item.strip() for item in value.split(",") if item.strip()]
        return items or list(default)
    if isinstance(value, (list, tuple, set)):
        items = [str(item).strip() for item in value if str(item).strip()]
        return items or list(default)
    return list(default)


def _decision_precedence_lookup(config: dict | None = None) -> dict[str, int]:
    configured = _config_sequence(
        resolve_config_value(
            config,
            "CTP_DECISION_PRECEDENCE_SEQUENCE",
            list(DEFAULT_DECISION_PRECEDENCE),
        ),
        list(DEFAULT_DECISION_PRECEDENCE),
    )
    lookup = {decision: idx + 1 for idx, decision in enumerate(configured)}
    for decision_class, rank in DEFAULT_DECISION_PRECEDENCE.items():
        lookup.setdefault(decision_class, rank)
    return lookup


def _campaign_number(campaign_id: str) -> int:
    text = str(campaign_id or "").strip()
    digits = "".join(ch for ch in text if ch.isdigit())
    try:
        return int(digits)
    except Exception:
        return 0


def _committed_campaign_sort_key(campaign: dict) -> tuple:
    release_seq = pd.to_numeric(pd.Series([campaign.get("release_seq")]), errors="coerce").iloc[0]
    due_date = pd.to_datetime(campaign.get("due_date"), errors="coerce")
    due_sort = due_date if pd.notna(due_date) else pd.Timestamp.max
    if pd.notna(release_seq):
        return (0, int(release_seq), due_sort, str(campaign.get("campaign_id", "")).strip())
    return (1, _campaign_number(campaign.get("campaign_id")), due_sort, str(campaign.get("campaign_id", "")).strip())


def _normalize_planning_start(planning_start, requested_ts, config: dict | None = None) -> datetime:
    ts = pd.to_datetime(planning_start, errors="coerce")
    if pd.isna(ts):
        horizon_days = max(resolve_config_int(config, "PLANNING_HORIZON_DAYS", 14), 1)
        ts = requested_ts - pd.Timedelta(days=horizon_days)
    anchor = ts.to_pydatetime() if hasattr(ts, "to_pydatetime") else ts
    return anchor.replace(minute=0, second=0, microsecond=0)


def _config_flag(config: dict | None, key: str, default: str = "N") -> bool:
    default_bool = str(default).strip().upper() in {"Y", "YES", "TRUE", "1", "ON"}
    return resolve_config_bool(config, key, default_bool)


def _config_float(config: dict | None, key: str, default: float) -> float:
    return resolve_config_float(config, key, default)


def _get_ctp_score_stock_only() -> float:
    """Get CTP score for stock-only promise from Algorithm_Config."""
    return get_config().get_float('CTP_SCORE_STOCK_ONLY', 60.0)


def _get_ctp_score_merge_campaign() -> float:
    """Get CTP score for merging with existing campaign from Algorithm_Config."""
    return get_config().get_float('CTP_SCORE_MERGE_CAMPAIGN', 10.0)


def _get_ctp_score_new_campaign() -> float:
    """Get CTP score for creating new campaign from Algorithm_Config."""
    return get_config().get_float('CTP_SCORE_NEW_CAMPAIGN', 4.0)


def _get_ctp_mergeable_score_threshold() -> float:
    """Get minimum score threshold to consider merge viable from Algorithm_Config."""
    return get_config().get_float('CTP_MERGEABLE_SCORE_THRESHOLD', 55.0)


def _get_ctp_inventory_zero_tolerance() -> float:
    """Get inventory zero tolerance threshold from Algorithm_Config."""
    return get_config().get_float('CTP_INVENTORY_ZERO_TOLERANCE', 1e-9)


def _get_ctp_merge_penalty() -> float:
    """Get penalty for non-selection of merge option from Algorithm_Config."""
    return get_config().get_float('CTP_MERGE_PENALTY', 1.0)


def _normalize_inventory_snapshot(snapshot) -> dict | None:
    if not isinstance(snapshot, dict):
        return None
    normalized = {}
    for sku_id, qty in snapshot.items():
        sku = str(sku_id or "").strip()
        if not sku:
            continue
        normalized[sku] = float(qty or 0.0)
    return normalized


def _snapshot_chain_inventory_after(committed: list) -> dict | None:
    previous_after = None
    for camp in committed:
        before = _normalize_inventory_snapshot(camp.get("inventory_before"))
        after = _normalize_inventory_snapshot(camp.get("inventory_after"))
        if before is None or after is None:
            return None

        if previous_after is not None and before != previous_after:
            return None

        for sku_id, qty in (camp.get("material_consumed") or {}).items():
            sku = str(sku_id or "").strip()
            expected_after = round(float(before.get(sku, 0.0) or 0.0) - float(qty or 0.0), 6)
            actual_after = round(float(after.get(sku, 0.0) or 0.0), 6)
            if abs(actual_after - expected_after) > 1e-6:
                return None

        previous_after = after
    return previous_after


def _net_inventory_after_committed_details(campaigns: list, inventory) -> dict:
    committed = sorted(
        [camp for camp in campaigns if str(camp.get("release_status", "")).upper() in COMMITTED_STATUSES],
        key=_committed_campaign_sort_key,
    )
    recomputed = inventory_map(inventory)
    for camp in committed:
        for sku_id, qty in (camp.get("material_consumed") or {}).items():
            sku = str(sku_id).strip()
            recomputed[sku] = round(float(recomputed.get(sku, 0.0) or 0.0) - float(qty or 0.0), 6)

    if not committed:
        return {
            "inventory": recomputed,
            "inventory_lineage_status": "NO_COMMITTED_CAMPAIGNS",
            "inventory_lineage_note": "No committed campaigns were present; CTP used current inventory directly.",
        }

    authoritative_after = _snapshot_chain_inventory_after(committed)
    if authoritative_after is not None:
        return {
            "inventory": authoritative_after,
            "inventory_lineage_status": "AUTHORITATIVE_SNAPSHOT_CHAIN",
            "inventory_lineage_note": "Committed inventory snapshots formed a verified before/after chain.",
        }

    last_snapshot = _normalize_inventory_snapshot(committed[-1].get("inventory_after"))
    if not last_snapshot:
        return {
            "inventory": recomputed,
            "inventory_lineage_status": "RECOMPUTED_FROM_CONSUMPTION",
            "inventory_lineage_note": (
                "Committed inventory snapshots were unavailable or inconsistent; "
                "CTP recomputed inventory from base stock and committed consumption."
            ),
        }

    conservative = dict(recomputed)
    for sku_id, snapshot_qty in last_snapshot.items():
        if sku_id in conservative:
            conservative[sku_id] = round(min(float(conservative[sku_id] or 0.0), snapshot_qty), 6)
        else:
            conservative[sku_id] = round(snapshot_qty, 6)
    return {
        "inventory": conservative,
        "inventory_lineage_status": "CONSERVATIVE_BLEND",
        "inventory_lineage_note": (
            "Committed inventory snapshots were inconsistent; "
            "CTP used the conservative minimum of recomputed inventory and the latest snapshot."
        ),
    }


def _frozen_jobs_from_campaigns(campaigns: list) -> dict:
    """Extract frozen jobs from campaigns with scheduled_jobs field (legacy).
    
    NOTE: This is deprecated in favor of _frozen_jobs_from_schedule_dataframe
    which builds from actual schedule output DataFrames. Kept for compatibility.
    """
    frozen = {}
    for camp in campaigns:
        for job in camp.get("scheduled_jobs", []) or []:
            job_id = str(job.get("Job_ID", "")).strip()
            if not job_id:
                continue
            frozen[job_id] = {
                "Resource_ID": str(job.get("Resource_ID", "")).strip(),
                "Planned_Start": job.get("Planned_Start"),
                "Planned_End": job.get("Planned_End"),
                "Status": str(job.get("Status", "LOCKED") or "LOCKED").strip(),
            }
    return frozen


def _frozen_jobs_from_schedule_dataframe(schedule_df: pd.DataFrame | None) -> dict:
    """Build frozen jobs from schedule output DataFrame.
    
    This is the proper way to extract frozen jobs after scheduling, as it uses
    the actual schedule output rather than campaign.scheduled_jobs field which may
    not be populated.
    
    Args:
        schedule_df: Output DataFrame from scheduler with columns like Job_ID, Resource_ID, etc.
        
    Returns:
        Dict mapping job_id -> {Resource_ID, Planned_Start, Planned_End, Status}
    """
    frozen = {}
    
    if schedule_df is None or getattr(schedule_df, "empty", True):
        return frozen
    
    for _, row in schedule_df.iterrows():
        job_id = str(row.get("Job_ID") or row.get("job_id") or "").strip()
        if not job_id:
            continue
        
        # Extract timing information from schedule
        planned_start = row.get("Planned_Start") or row.get("planned_start")
        planned_end = row.get("Planned_End") or row.get("planned_end")
        
        # Try to convert to datetime if they're strings
        try:
            if isinstance(planned_start, str):
                planned_start = pd.to_datetime(planned_start)
            if isinstance(planned_end, str):
                planned_end = pd.to_datetime(planned_end)
        except Exception:
            pass
        
        frozen[job_id] = {
            "Resource_ID": str(row.get("Resource_ID") or row.get("resource_id") or "").strip(),
            "Planned_Start": planned_start,
            "Planned_End": planned_end,
            "Status": "LOCKED",  # Jobs from schedule are always locked/committed
        }
    
    return frozen


def _ghost_sales_order(
    sku_id: str,
    qty_mt: float,
    requested_date,
    skus: pd.DataFrame | None,
    *,
    order_date=None,
) -> pd.DataFrame:
    sku_id = str(sku_id or "").strip()
    requested_ts = pd.to_datetime(requested_date)
    order_ts = pd.to_datetime(order_date, errors="coerce")
    if pd.isna(order_ts):
        order_ts = requested_ts

    meta = {}
    if skus is not None and not getattr(skus, "empty", True) and "SKU_ID" in skus.columns:
        lookup = skus.drop_duplicates(subset=["SKU_ID"]).set_index("SKU_ID")
        if sku_id in lookup.index:
            meta = lookup.loc[sku_id].to_dict()

    grade = meta.get("Grade", "")
    section_mm = meta.get("Section_mm", meta.get("Attribute_1", ""))
    campaign_group = str(meta.get("Campaign_Group") or grade or sku_id).strip()

    return pd.DataFrame(
        [
            {
                "SO_ID": "CTP-REQUEST",
                "SKU_ID": sku_id,
                "Grade": grade,
                "Section_mm": section_mm,
                "Order_Qty_MT": float(qty_mt or 0.0),
                "Order_Qty": float(qty_mt or 0.0),
                "Delivery_Date": requested_ts,
                "Order_Date": order_ts,
                "Priority": "URGENT",
                "Status": "Open",
                "Campaign_Group": campaign_group,
            }
        ]
    )


def _rename_ghost_campaigns(ghost_campaigns: list, existing_campaigns: list) -> list:
    next_num = max((_campaign_number(camp.get("campaign_id")) for camp in existing_campaigns), default=0) + 1
    renamed = []
    for offset, camp in enumerate(ghost_campaigns, start=0):
        new_id = f"CTP-{next_num + offset:03d}"
        new_camp = dict(camp)
        new_camp["campaign_id"] = new_id
        new_orders = []
        for idx, order in enumerate(camp.get("production_orders", []), start=1):
            new_order = dict(order)
            new_order["campaign_id"] = new_id
            new_order["production_order_id"] = f"{new_id}-PO{idx:02d}"
            new_orders.append(new_order)
        new_camp["production_orders"] = new_orders
        renamed.append(new_camp)
    return renamed


def _campaign_matches_join_target(campaign: dict, target_campaign: dict) -> bool:
    return (
        str(campaign.get("campaign_group", "")) == str(target_campaign.get("campaign_group", ""))
        and str(campaign.get("grade", "")) == str(target_campaign.get("grade", ""))
        and str(campaign.get("billet_family", "")) == str(target_campaign.get("billet_family", ""))
        and bool(campaign.get("needs_vd")) == bool(target_campaign.get("needs_vd"))
    )


def _merge_into_campaign(target_campaign: dict, ghost_campaign: dict, bom=None, config=None) -> dict:
    merged = dict(target_campaign)
    ghost_orders = ghost_campaign.get("production_orders", [])
    existing_orders = list(merged.get("production_orders", []))

    cid = merged["campaign_id"]
    next_po = len(existing_orders) + 1
    for order in ghost_orders:
        new_order = dict(order)
        new_order["campaign_id"] = cid
        new_order["production_order_id"] = f"{cid}-PO{next_po:02d}"
        existing_orders.append(new_order)
        next_po += 1

    merged["production_orders"] = existing_orders
    merged["total_coil_mt"] = round(
        float(merged.get("total_coil_mt", 0) or 0) + float(ghost_campaign.get("total_coil_mt", 0) or 0), 3
    )
    merged["order_count"] = len(set(str(o.get("so_id", "")) for o in existing_orders if str(o.get("so_id", "")).strip()))
    merged["heats"] = _heats_needed_from_lines(existing_orders, bom=bom, config=config)

    existing_so_ids = list(merged.get("so_ids", []))
    for order in ghost_orders:
        so_id = str(order.get("so_id", "")).strip()
        if so_id and so_id not in existing_so_ids:
            existing_so_ids.append(so_id)
    merged["so_ids"] = existing_so_ids
    return merged


def _coerce_float(value: Any, default: float = 0.0) -> float:
    numeric = pd.to_numeric(pd.Series([value]), errors="coerce").iloc[0]
    return float(default if pd.isna(numeric) else numeric)


def _coerce_timestamp(value) -> pd.Timestamp | None:
    ts = pd.to_datetime(value, errors="coerce")
    return None if pd.isna(ts) else ts


def _safe_str(value: Any) -> str:
    if value is None:
        return ""
    try:
        return str(value).strip()
    except Exception:
        return ""


def _qty_precision(config: dict | None = None) -> float:
    precision = _config_float(config, "CTP_Qty_Precision_MT", 1.0)
    return max(round(precision, 3), 0.1)


def _normalize_shortages(campaigns: list) -> list[dict]:
    gaps = {}
    impacted_qty = {}
    for camp in campaigns or []:
        impact_mt = round(float(camp.get("total_coil_mt", 0.0) or 0.0), 3)
        for material_sku, shortage_qty in (camp.get("material_shortages") or {}).items():
            sku = _safe_str(material_sku)
            gaps[sku] = round(float(gaps.get(sku, 0.0) or 0.0) + float(shortage_qty or 0.0), 3)
            impacted_qty[sku] = round(float(impacted_qty.get(sku, 0.0) or 0.0) + impact_mt, 3)
    return [
        {
            "sku_id": sku,
            "shortage_qty": round(qty, 3),
            "impacts_mt": round(float(impacted_qty.get(sku, 0.0) or 0.0), 3),
        }
        for sku, qty in sorted(gaps.items(), key=lambda item: (-item[1], item[0]))
    ]


def _normalize_structure_errors(campaigns: list) -> list[dict]:
    errors = []
    for camp in campaigns or []:
        cid = _safe_str(camp.get("campaign_id"))
        for err in camp.get("material_structure_errors", []) or []:
            payload = dict(err or {})
            payload.setdefault("campaign_id", cid)
            errors.append(payload)
        for err in camp.get("heats_calc_errors", []) or []:
            payload = dict(err or {})
            payload.setdefault("campaign_id", cid)
            if payload not in errors:
                errors.append(payload)
    return errors


def _inventory_trust_blocked(inventory_lineage: dict, config: dict | None = None) -> bool:
    return (
        inventory_lineage.get("inventory_lineage_status") in DEGRADED_INVENTORY_LINEAGE_STATUSES
        and _config_flag(config, "Require_Authoritative_CTP_Inventory", "Y")
    )


def _has_master_data_failure(schedule_result: dict) -> bool:
    detail = _safe_str(schedule_result.get("solver_detail")).upper()
    if not detail:
        return False
    return any(token in detail for token in ["MASTER", "ROUTING", "RESOURCE", "VALIDATE", "UNKNOWN RESOURCE"])


def _score_join_candidate(target_campaign: dict, ghost_campaigns: list, requested_ts: pd.Timestamp) -> dict:
    if not target_campaign or not ghost_campaigns:
        return {
            "candidate_id": None,
            "score": -1.0,
            "mergeable": False,
            "reasons": ["NO_GHOST_CAMPAIGN"],
        }

    reasons = []
    score = 0.0
    target_id = _safe_str(target_campaign.get("campaign_id"))
    ghost = ghost_campaigns[0]

    if _campaign_matches_join_target(ghost, target_campaign):
        score += _get_ctp_score_stock_only()
    else:
        reasons.append("ATTRIBUTE_MISMATCH")

    ghost_sections = sorted({round(_coerce_float(o.get("section_mm"), 0.0), 3) for c in ghost_campaigns for o in c.get("production_orders", [])})
    target_sections = sorted({round(_coerce_float(o.get("section_mm"), 0.0), 3) for o in target_campaign.get("production_orders", [])})
    if ghost_sections and target_sections and set(ghost_sections).issubset(set(target_sections)):
        score += _get_ctp_score_merge_campaign()
    elif ghost_sections and target_sections and not set(ghost_sections).intersection(set(target_sections)):
        reasons.append("SECTION_INCOMPATIBLE")
    else:
        score += _get_ctp_score_new_campaign()

    target_due = _coerce_timestamp(target_campaign.get("due_date"))
    if target_due is not None:
        due_gap_days = abs((target_due - requested_ts).total_seconds()) / 86400.0
        score += max(0.0, 15.0 - min(due_gap_days, 15.0))
        if due_gap_days > 14:
            reasons.append("DUE_WINDOW_WEAK")

    target_release = _safe_str(target_campaign.get("release_status")).upper()
    if target_release in COMMITTED_STATUSES:
        score += 8.0
    else:
        reasons.append("TARGET_NOT_COMMITTED")

    if target_campaign.get("material_status") == "READY":
        score += _get_ctp_score_new_campaign()

    available_headroom = max(
        _config_float(None, "dummy", 0.0),
        _coerce_float(target_campaign.get("max_campaign_headroom_mt"), 0.0),
    )
    if available_headroom > 0:
        score += min(8.0, available_headroom / 10.0)

    return {
        "candidate_id": target_id or None,
        "score": round(score, 2),
        "mergeable": score >= _get_ctp_mergeable_score_threshold() and "ATTRIBUTE_MISMATCH" not in reasons and "SECTION_INCOMPATIBLE" not in reasons,
        "reasons": reasons,
    }


def _best_join_candidate(ghost_campaigns: list, campaigns: list, requested_ts: pd.Timestamp) -> dict:
    best = {
        "candidate_id": None,
        "score": -1.0,
        "mergeable": False,
        "reasons": ["NO_MATCHING_COMMITTED_CAMPAIGN"],
        "all_candidates": [],
    }
    for camp in campaigns or []:
        if _safe_str(camp.get("release_status")).upper() not in COMMITTED_STATUSES:
            continue
        scored = _score_join_candidate(camp, ghost_campaigns, requested_ts)
        best["all_candidates"].append(scored)
        if scored["score"] > best["score"]:
            best = dict(scored, all_candidates=best["all_candidates"])
    if not best["all_candidates"]:
        return best
    top = max(best["all_candidates"], key=lambda item: item.get("score", -1.0))
    return {**top, "all_candidates": best["all_candidates"]}


def _prepare_combined_campaigns(
    ghost_campaigns: list,
    committed_campaigns: list,
    *,
    best_join: dict,
    bom,
    config,
) -> dict:
    ghost_request_so_ids = {
        _safe_str(order.get("so_id"))
        for camp in ghost_campaigns
        for order in camp.get("production_orders", [])
        if _safe_str(order.get("so_id"))
    }

    request_job_ids: set[str] = set()
    target_campaign_ids: set[str] = set()
    merged_campaign_ids: list[str] = []
    new_campaign_ids: list[str] = []
    merged_into_existing = False

    join_candidate = best_join.get("candidate_id") if best_join.get("mergeable") else None
    if join_candidate and ghost_campaigns:
        target_campaign = next(
            (camp for camp in committed_campaigns if _safe_str(camp.get("campaign_id")) == join_candidate),
            None,
        )
        mergeable_idx = {
            idx
            for idx, camp in enumerate(ghost_campaigns)
            if target_campaign is not None and _campaign_matches_join_target(camp, target_campaign)
        }
        mergeable_ghosts = [camp for idx, camp in enumerate(ghost_campaigns) if idx in mergeable_idx]
        remainder_ghosts = [camp for idx, camp in enumerate(ghost_campaigns) if idx not in mergeable_idx]

        merged_campaigns = []
        merged = False
        for camp in committed_campaigns:
            if _safe_str(camp.get("campaign_id")) == join_candidate and not merged:
                merged_camp = dict(camp)
                for ghost_camp in mergeable_ghosts:
                    merged_camp = _merge_into_campaign(merged_camp, ghost_camp, bom=bom, config=config)
                merged_campaigns.append(merged_camp)
                request_job_ids = {
                    f"{order['production_order_id']}-RM"
                    for order in merged_camp.get("production_orders", [])
                    if _safe_str(order.get("so_id")) in ghost_request_so_ids
                }
                merged = True
            else:
                merged_campaigns.append(camp)

        merged_into_existing = merged
        if merged_into_existing and join_candidate:
            merged_campaign_ids = [join_candidate]
        renamed_remainder = _rename_ghost_campaigns(remainder_ghosts, committed_campaigns) if remainder_ghosts else []
        new_campaign_ids = [_safe_str(camp.get("campaign_id")) for camp in renamed_remainder if _safe_str(camp.get("campaign_id"))]
        request_job_ids.update(
            f"{order['production_order_id']}-RM"
            for camp in renamed_remainder
            for order in camp.get("production_orders", [])
        )
        target_campaign_ids = ({join_candidate} if merged_into_existing else set()) | {
            camp["campaign_id"] for camp in renamed_remainder
        }
        combined_campaigns = merged_campaigns + renamed_remainder
    else:
        renamed = _rename_ghost_campaigns(ghost_campaigns, committed_campaigns)
        new_campaign_ids = [_safe_str(camp.get("campaign_id")) for camp in renamed if _safe_str(camp.get("campaign_id"))]
        request_job_ids = {
            f"{order['production_order_id']}-RM"
            for camp in renamed
            for order in camp.get("production_orders", [])
        }
        target_campaign_ids = {camp["campaign_id"] for camp in renamed}
        combined_campaigns = committed_campaigns + renamed

    return {
        "combined_campaigns": combined_campaigns,
        "request_job_ids": request_job_ids,
        "target_campaign_ids": target_campaign_ids,
        "merged_into_existing": merged_into_existing,
        "merged_campaign_ids": merged_campaign_ids,
        "new_campaign_ids": new_campaign_ids,
        "partially_merged": bool(merged_campaign_ids and new_campaign_ids),
    }


def _extract_ghost_rows(heat_df: pd.DataFrame, request_job_ids: set[str], target_campaign_ids: set[str]) -> pd.DataFrame:
    if heat_df is None or getattr(heat_df, "empty", True):
        return pd.DataFrame()
    rows = heat_df.copy()
    rows["Job_ID"] = rows.get("Job_ID", "").fillna("").astype(str)
    rows["Campaign"] = rows.get("Campaign", "").fillna("").astype(str)
    rows["Planned_Start"] = pd.to_datetime(rows.get("Planned_Start"), errors="coerce")
    rows["Planned_End"] = pd.to_datetime(rows.get("Planned_End"), errors="coerce")
    rows["Duration_Hrs"] = pd.to_numeric(rows.get("Duration_Hrs"), errors="coerce").fillna(0.0)
    if request_job_ids:
        selected = rows[rows["Job_ID"].isin(request_job_ids)].copy()
        if not selected.empty:
            return selected
    if target_campaign_ids:
        return rows[rows["Campaign"].isin(target_campaign_ids)].copy()
    return pd.DataFrame()


def _derive_bottleneck(ghost_rows: pd.DataFrame) -> tuple[str | None, dict]:
    if ghost_rows is None or getattr(ghost_rows, "empty", True):
        return None, {}
    rows = ghost_rows.copy()
    rows["Resource_ID"] = rows.get("Resource_ID", "").fillna("").astype(str)
    agg = (
        rows.groupby("Resource_ID", dropna=False)
        .agg(
            latest_end=("Planned_End", "max"),
            total_hrs=("Duration_Hrs", "sum"),
            task_count=("Job_ID", "count"),
        )
        .reset_index()
    )
    if agg.empty:
        return None, {}
    agg = agg.sort_values(["latest_end", "total_hrs", "task_count", "Resource_ID"], ascending=[False, False, False, True])
    top = agg.iloc[0]
    return _safe_str(top["Resource_ID"]) or None, {
        "resource_id": _safe_str(top["Resource_ID"]),
        "latest_end": top["latest_end"],
        "total_hrs": round(float(top["total_hrs"] or 0.0), 2),
        "task_count": int(top["task_count"] or 0),
    }


def _schedule_confidence(schedule_result: dict, inventory_lineage_status: str, material_hold: bool) -> tuple[str, list[str]]:
    """Compute promise confidence with strict degradation rules.
    
    Rule 6.3: Confidence is strictly lowered when:
    - Inventory lineage is degraded (recomputed, conservative blend, etc.)
    - Schedule basis is greedy fallback (not CP-SAT)
    - Default masters were used
    - Material check was skipped or failed
    """
    flags = []
    solver_status = _safe_str(schedule_result.get("solver_status")).upper()
    solver_detail = _safe_str(schedule_result.get("solver_detail")).upper()
    
    # Flag degradation sources
    is_greedy = solver_status in HEURISTIC_SOLVER_STATUSES
    has_degraded_lineage = inventory_lineage_status in DEGRADED_INVENTORY_LINEAGE_STATUSES
    has_default_masters = bool(schedule_result.get("allow_default_masters"))
    has_master_data_risk = "MASTER" in solver_detail or "RESOURCE" in solver_detail or "ROUTING" in solver_detail
    
    if is_greedy:
        flags.append("HEURISTIC_SCHEDULE")
    if has_degraded_lineage:
        flags.append("DEGRADED_INVENTORY_LINEAGE")
    if has_default_masters:
        flags.append("DEFAULT_MASTER_DATA_ALLOWED")
    if material_hold:
        flags.append("MATERIAL_BLOCK")
    if has_master_data_risk:
        flags.append("MASTER_DATA_RISK")

    # STRICT RULES: Mandatory confidence downgrade conditions
    # Rule: HIGH confidence only if all of these are false:
    # - Master data risk flag
    # - Material hold
    
    if has_master_data_risk or material_hold:
        return "LOW", flags
    
    # MEDIUM when any of these degradations exist:
    # - Heuristic/greedy schedule (not CP-SAT optimization)
    # - Degraded inventory lineage
    # - Default masters allowed
    if is_greedy or has_degraded_lineage or has_default_masters:
        return "MEDIUM", flags
    
    # Otherwise HIGH confidence
    return "HIGH", flags


def _decision_class(
    *,
    stock_only: bool,
    on_time: bool | None,
    earliest_completion: pd.Timestamp | None,
    material_gaps: list[dict],
    structure_errors: list[dict],
    inventory_trust_blocked: bool,
    master_data_failure: bool,
    merged_into_existing: bool,
    solver_status: str,
    policy_only: bool = False,
) -> str:
    if inventory_trust_blocked:
        return "CANNOT_PROMISE_INVENTORY_TRUST"
    if master_data_failure or structure_errors:
        return "CANNOT_PROMISE_MASTER_DATA"
    if policy_only:
        return "CANNOT_PROMISE_POLICY_ONLY"
    if material_gaps:
        return "CANNOT_PROMISE_MATERIAL"
    if earliest_completion is None:
        return "CANNOT_PROMISE_CAPACITY"
    if on_time:
        if stock_only:
            return "PROMISE_CONFIRMED_STOCK_ONLY"
        if solver_status.upper() in HEURISTIC_SOLVER_STATUSES:
            return "PROMISE_HEURISTIC_ONLY"
        if merged_into_existing:
            return "PROMISE_CONFIRMED_MERGED"
        return "PROMISE_CONFIRMED_NEW_CAMPAIGN"
    return "PROMISE_LATER_DATE"


def _primary_blocker(
    *,
    decision_class: str,
    inventory_lineage: dict,
    material_gaps: list[dict],
    structure_errors: list[dict],
    bottleneck_resource: str | None,
    schedule_result: dict,
    best_join: dict,
) -> dict:
    solver_status = _safe_str(schedule_result.get("solver_status"))
    if decision_class == "CANNOT_PROMISE_INVENTORY_TRUST":
        return {
            "type": "INVENTORY_TRUST",
            "code": inventory_lineage.get("inventory_lineage_status", "UNKNOWN"),
            "text": inventory_lineage.get("inventory_lineage_note", "Inventory lineage is not authoritative."),
        }
    if decision_class == "CANNOT_PROMISE_MASTER_DATA":
        if structure_errors:
            err = structure_errors[0]
            return {
                "type": "MASTER_DATA",
                "code": _safe_str(err.get("type") or "BOM_STRUCTURE_ERROR"),
                "text": _safe_str(err.get("reason") or err.get("path") or "BOM or master data structure error blocked CTP."),
            }
        return {
            "type": "MASTER_DATA",
            "code": "SCHEDULER_MASTER_DATA",
            "text": _safe_str(schedule_result.get("solver_detail") or "Routing/resource master data blocked scheduling."),
        }
    if decision_class == "CANNOT_PROMISE_MATERIAL":
        gap = material_gaps[0] if material_gaps else {}
        return {
            "type": "MATERIAL",
            "code": _safe_str(gap.get("sku_id") or "SHORTAGE"),
            "text": f"Material {_safe_str(gap.get('sku_id'))} is short by {round(float(gap.get('shortage_qty', 0.0) or 0.0), 3)} MT after committed-plan netting.",
        }
    if decision_class in {"CANNOT_PROMISE_CAPACITY", "PROMISE_LATER_DATE", "PROMISE_SPLIT_REQUIRED", "PROMISE_CONDITIONAL_EXPEDITE"}:
        code = _safe_str(bottleneck_resource or solver_status or "CAPACITY")
        merge_note = ""
        if best_join.get("candidate_id") and not best_join.get("mergeable"):
            merge_note = f" Best merge candidate {best_join.get('candidate_id')} was rejected: {', '.join(best_join.get('reasons', []))}."
        return {
            "type": "CAPACITY",
            "code": code,
            "text": f"Capacity is the dominant blocker, with {code} acting as the bottleneck resource or schedule driver.{merge_note}",
        }
    return {
        "type": "NONE",
        "code": decision_class,
        "text": "Full requested quantity is feasible under the selected scenario.",
    }


def _secondary_blockers(
    *,
    inventory_lineage: dict,
    material_gaps: list[dict],
    structure_errors: list[dict],
    best_join: dict,
    schedule_result: dict,
) -> list[dict]:
    blockers = []
    status = inventory_lineage.get("inventory_lineage_status")
    if status in DEGRADED_INVENTORY_LINEAGE_STATUSES:
        blockers.append({
            "type": "INVENTORY_TRUST",
            "code": status,
            "text": inventory_lineage.get("inventory_lineage_note", ""),
        })
    for gap in material_gaps[:3]:
        blockers.append({
            "type": "MATERIAL",
            "code": _safe_str(gap.get("sku_id")),
            "text": f"Short by {round(float(gap.get('shortage_qty', 0.0) or 0.0), 3)} MT.",
        })
    for err in structure_errors[:3]:
        blockers.append({
            "type": "MASTER_DATA",
            "code": _safe_str(err.get("type") or "STRUCTURE_ERROR"),
            "text": _safe_str(err.get("reason") or err.get("path") or "Structure error."),
        })
    if best_join.get("candidate_id") and best_join.get("reasons"):
        blockers.append({
            "type": "CAMPAIGN",
            "code": _safe_str(best_join.get("candidate_id")),
            "text": f"Merge candidate score={best_join.get('score')} reasons={', '.join(best_join.get('reasons', []))}",
        })
    solver_status = _safe_str(schedule_result.get("solver_status")).upper()
    if solver_status in HEURISTIC_SOLVER_STATUSES:
        blockers.append({
            "type": "SCHEDULE_QUALITY",
            "code": solver_status,
            "text": _safe_str(schedule_result.get("solver_detail") or "Heuristic scheduler result."),
        })
    return blockers[:8]


def _request_narrative(
    *,
    decision_class: str,
    qty_mt: float,
    requested_ts: pd.Timestamp,
    earliest_completion: pd.Timestamp | None,
    primary_blocker: dict,
    promise_confidence: str,
    merged_into_existing: bool,
    material_gaps: list[dict],
) -> str:
    if decision_class.startswith("PROMISE_CONFIRMED"):
        mode = "existing committed campaign" if merged_into_existing else "new campaign or stock"
        completion_text = earliest_completion.strftime("%Y-%m-%d %H:%M") if earliest_completion is not None else requested_ts.strftime("%Y-%m-%d %H:%M")
        return (
            f"Full requested quantity {round(float(qty_mt or 0.0), 3)} MT is feasible by the requested date. "
            f"Promise is based on {mode}. Planned plant completion is {completion_text}. "
            f"Confidence is {promise_confidence}."
        )
    if decision_class == "PROMISE_LATER_DATE":
        completion_text = earliest_completion.strftime("%Y-%m-%d %H:%M") if earliest_completion is not None else "unknown"
        return (
            f"Full requested quantity is feasible, but not by the requested date. "
            f"Earliest plant completion is {completion_text}. Primary blocker: {primary_blocker.get('text', '')}"
        )
    if decision_class == "CANNOT_PROMISE_MATERIAL":
        top_gap = material_gaps[0] if material_gaps else {}
        return (
            f"Full requested quantity cannot be promised because material {_safe_str(top_gap.get('sku_id'))} "
            f"is short by {round(float(top_gap.get('shortage_qty', 0.0) or 0.0), 3)} MT after committed-plan netting."
        )
    return primary_blocker.get("text", "CTP could not confirm a feasible promise.")


def _build_alternative(
    *,
    alternative_type: str,
    description: str,
    scenario: dict | None,
    required_assumptions: list[str] | None = None,
    tradeoffs: list[str] | None = None,
) -> dict:
    scenario = scenario or {}
    return {
        "alternative_type": alternative_type,
        "description": description,
        "feasible": bool(scenario.get("exact_requested_qty_feasible")) if scenario else False,
        "promised_qty_mt": round(float(scenario.get("promised_qty_mt", 0.0) or 0.0), 3) if scenario else 0.0,
        "promised_date": scenario.get("promised_completion_date") if scenario else None,
        "required_assumptions": required_assumptions or [],
        "tradeoffs": tradeoffs or [],
        "confidence": scenario.get("promise_confidence") if scenario else None,
        "decision_class": scenario.get("decision_class") if scenario else None,
    }


def _scenario_result_base(
    *,
    sku_id: str,
    qty_mt: float,
    requested_ts: pd.Timestamp,
    planning_anchor: datetime,
    inventory_lineage: dict,
    scenario_name: str,
) -> dict:
    return {
        "request_id": f"CTP-{_safe_str(sku_id)}-{requested_ts.strftime('%Y%m%d%H%M')}-{int(round(float(qty_mt or 0.0) * 1000))}",
        "scenario_name": scenario_name,
        "sku_id": _safe_str(sku_id),
        "qty_mt": round(float(qty_mt or 0.0), 3),
        "requested_date": requested_ts,
        "planning_anchor": planning_anchor,
        "ctp_scope": "PLANT_COMPLETION",
        "delivery_modeled": False,
        "inventory_lineage_status": inventory_lineage.get("inventory_lineage_status"),
        "inventory_lineage_note": inventory_lineage.get("inventory_lineage_note"),
    }


def _evaluate_scenario(
    *,
    sku_id: str,
    qty_mt: float,
    requested_ts: pd.Timestamp,
    campaigns: list,
    resources: pd.DataFrame,
    bom: pd.DataFrame,
    inventory,
    routing: pd.DataFrame,
    skus: pd.DataFrame,
    planning_anchor: datetime,
    config: dict | None,
    campaign_config: pd.DataFrame | None,
    min_campaign_mt: float,
    max_campaign_mt: float,
    frozen_jobs: dict | None,
    queue_times: dict | None,
    changeover_matrix: pd.DataFrame | None,
    scenario_name: str,
) -> dict:
    committed_campaigns = [
        dict(camp) for camp in (campaigns or []) if _safe_str(camp.get("release_status")).upper() in COMMITTED_STATUSES
    ]
    inventory_lineage = _net_inventory_after_committed_details(committed_campaigns, inventory)
    net_inventory = inventory_lineage["inventory"]
    inventory_trust_blocked = _inventory_trust_blocked(inventory_lineage, config=config)

    result = _scenario_result_base(
        sku_id=sku_id,
        qty_mt=qty_mt,
        requested_ts=requested_ts,
        planning_anchor=planning_anchor,
        inventory_lineage=inventory_lineage,
        scenario_name=scenario_name,
    )

    if qty_mt <= _get_ctp_inventory_zero_tolerance():
        result.update(
            {
                "decision_class": "PROMISE_CONFIRMED_STOCK_ONLY",
                "promise_confidence": "HIGH",
                "exact_requested_qty_feasible": True,
                "exact_requested_date_feasible": True,
                "full_qty_feasible": True,
                "promised_qty_mt": 0.0,
                "promised_completion_date": pd.to_datetime(planning_anchor),
                "earliest_completion": pd.to_datetime(planning_anchor),
                "earliest_delivery": None,
                "plant_completion_feasible": True,
                "delivery_feasible": None,
                "feasible": True,
                "lateness_days": 0.0,
                "completion_gap_days": 0.0,
                "material_gaps": [],
                "material_structure_errors": [],
                "joins_campaign": None,
                "best_join_candidate_id": None,
                "join_candidate_ids": [],
                "mergeability_score": None,
                "mergeability_fail_reasons": [],
                "new_campaign_needed": False,
                "campaign_action": "STOCK_ONLY",
                "merged_campaign_ids": [],
                "new_campaign_ids": [],
                "partially_merged": False,
                "promise_basis": "STOCK_AT_PLANNING_START",
                "schedule_mode": "NONE",
                "solver_status": "STOCK",
                "solver_detail": "STOCK_ONLY",
                "terminal_resource": None,
                "bottleneck_resource": None,
                "critical_path_resource": None,
                "primary_blocker_type": "NONE",
                "primary_blocker_code": "NONE",
                "primary_blocker_text": "No production is required.",
                "secondary_blockers": [],
                "latent_blockers": [],
                "alternatives": [],
                "best_alternative": None,
                "decision_trace": ["Zero-quantity request confirmed immediately."],
                "assumption_flags": [],
                "data_quality_flags": [],
                "warning_flags": [],
                "narrative": "Zero quantity requested; nothing needs to be produced.",
                "scenario_evaluated": True,
            }
        )
        return result

    if inventory_trust_blocked:
        decision_class = "CANNOT_PROMISE_INVENTORY_TRUST"
        blocker = _primary_blocker(
            decision_class=decision_class,
            inventory_lineage=inventory_lineage,
            material_gaps=[],
            structure_errors=[],
            bottleneck_resource=None,
            schedule_result={},
            best_join={},
        )
        result.update(
            {
                "decision_class": decision_class,
                "promise_confidence": "LOW",
                "exact_requested_qty_feasible": False,
                "exact_requested_date_feasible": False,
                "full_qty_feasible": False,
                "promised_qty_mt": 0.0,
                "promised_completion_date": None,
                "earliest_completion": None,
                "earliest_delivery": None,
                "plant_completion_feasible": None,
                "delivery_feasible": None,
                "feasible": False,
                "lateness_days": None,
                "completion_gap_days": None,
                "material_gaps": [],
                "material_structure_errors": [],
                "joins_campaign": None,
                "best_join_candidate_id": None,
                "join_candidate_ids": [],
                "mergeability_score": None,
                "mergeability_fail_reasons": [],
                "new_campaign_needed": True,
                "campaign_action": "INVENTORY_LINEAGE_BLOCKED",
                "merged_campaign_ids": [],
                "new_campaign_ids": [],
                "partially_merged": False,
                "promise_basis": "INVENTORY_LINEAGE_BLOCKED",
                "schedule_mode": "NONE",
                "solver_status": f"BLOCKED: {inventory_lineage['inventory_lineage_status']}",
                "solver_detail": "AUTHORITATIVE_INVENTORY_REQUIRED",
                "terminal_resource": None,
                "bottleneck_resource": None,
                "critical_path_resource": None,
                "primary_blocker_type": blocker["type"],
                "primary_blocker_code": blocker["code"],
                "primary_blocker_text": blocker["text"],
                "secondary_blockers": _secondary_blockers(
                    inventory_lineage=inventory_lineage,
                    material_gaps=[],
                    structure_errors=[],
                    best_join={},
                    schedule_result={},
                ),
                "latent_blockers": [],
                "alternatives": [],
                "best_alternative": None,
                "decision_trace": [
                    "Committed inventory lineage was evaluated before ghost demand creation.",
                    "Config requires authoritative inventory for CTP.",
                    "Scenario blocked before campaign construction.",
                ],
                "assumption_flags": ["AUTHORITATIVE_INVENTORY_REQUIRED"],
                "data_quality_flags": [inventory_lineage["inventory_lineage_status"]],
                "warning_flags": ["INVENTORY_LINEAGE_DEGRADED"],
                "narrative": inventory_lineage.get("inventory_lineage_note", "Inventory lineage blocked CTP."),
                "scenario_evaluated": True,
            }
        )
        return result

    ghost_so = _ghost_sales_order(sku_id, qty_mt, requested_ts, skus, order_date=planning_anchor)
    ghost_campaigns = build_campaigns(
        ghost_so,
        min_campaign_mt=float(min_campaign_mt),
        max_campaign_mt=float(max_campaign_mt),
        inventory=net_inventory,
        bom=bom,
        routing=routing,
        config=config,
        skus=skus,
        campaign_config=campaign_config,
    )

    if not ghost_campaigns:
        earliest = pd.to_datetime(planning_anchor)
        decision_class = "PROMISE_CONFIRMED_STOCK_ONLY"
        result.update(
            {
                "decision_class": decision_class,
                "promise_confidence": "HIGH",
                "exact_requested_qty_feasible": True,
                "exact_requested_date_feasible": earliest <= requested_ts,
                "full_qty_feasible": True,
                "promised_qty_mt": round(float(qty_mt or 0.0), 3),
                "promised_completion_date": earliest,
                "earliest_completion": earliest,
                "earliest_delivery": None,
                "plant_completion_feasible": earliest <= requested_ts,
                "delivery_feasible": None,
                "feasible": True,
                "lateness_days": max(round((earliest - requested_ts).total_seconds() / 86400.0, 2), 0.0),
                "completion_gap_days": round((earliest - requested_ts).total_seconds() / 86400.0, 2),
                "material_gaps": [],
                "material_structure_errors": [],
                "joins_campaign": None,
                "best_join_candidate_id": None,
                "join_candidate_ids": [],
                "mergeability_score": None,
                "mergeability_fail_reasons": [],
                "new_campaign_needed": False,
                "campaign_action": "STOCK_ONLY",
                "merged_campaign_ids": [],
                "new_campaign_ids": [],
                "partially_merged": False,
                "promise_basis": "STOCK_AT_PLANNING_START",
                "schedule_mode": "NONE",
                "solver_status": "STOCK",
                "solver_detail": "STOCK_ONLY",
                "terminal_resource": None,
                "bottleneck_resource": None,
                "critical_path_resource": None,
                "primary_blocker_type": "NONE",
                "primary_blocker_code": "NONE",
                "primary_blocker_text": "No blocker. Net finished-goods stock covers the request.",
                "secondary_blockers": [],
                "latent_blockers": [],
                "alternatives": [],
                "best_alternative": None,
                "decision_trace": [
                    "Ghost demand was created.",
                    "Finished-goods stock fully covered the request after committed-plan netting.",
                ],
                "assumption_flags": [],
                "data_quality_flags": [],
                "warning_flags": [],
                "narrative": "Request is fully feasible from net finished-goods stock; no new production campaign is required.",
                "scenario_evaluated": True,
            }
        )
        return result

    material_gaps = _normalize_shortages(ghost_campaigns)
    structure_errors = _normalize_structure_errors(ghost_campaigns)
    best_join = _best_join_candidate(ghost_campaigns, committed_campaigns, requested_ts)

    if material_gaps or structure_errors:
        decision_class = _decision_class(
            stock_only=False,
            on_time=False,
            earliest_completion=None,
            material_gaps=material_gaps,
            structure_errors=structure_errors,
            inventory_trust_blocked=False,
            master_data_failure=False,
            merged_into_existing=False,
            solver_status="MATERIAL HOLD",
        )
        blocker = _primary_blocker(
            decision_class=decision_class,
            inventory_lineage=inventory_lineage,
            material_gaps=material_gaps,
            structure_errors=structure_errors,
            bottleneck_resource=None,
            schedule_result={"solver_status": "MATERIAL HOLD", "solver_detail": "MATERIAL_OR_STRUCTURE_BLOCK"},
            best_join=best_join,
        )
        result.update(
            {
                "decision_class": decision_class,
                "promise_confidence": "HIGH" if not structure_errors else "LOW",
                "exact_requested_qty_feasible": False,
                "exact_requested_date_feasible": False,
                "full_qty_feasible": False,
                "promised_qty_mt": 0.0,
                "promised_completion_date": None,
                "earliest_completion": None,
                "earliest_delivery": None,
                "plant_completion_feasible": None,
                "delivery_feasible": None,
                "feasible": False,
                "lateness_days": None,
                "completion_gap_days": None,
                "material_gaps": material_gaps,
                "material_structure_errors": structure_errors,
                "joins_campaign": best_join.get("candidate_id"),
                "best_join_candidate_id": best_join.get("candidate_id"),
                "join_candidate_ids": [c.get("candidate_id") for c in best_join.get("all_candidates", []) if c.get("candidate_id")],
                "mergeability_score": best_join.get("score"),
                "mergeability_fail_reasons": best_join.get("reasons", []),
                "new_campaign_needed": best_join.get("candidate_id") is None,
                "campaign_action": "MATERIAL_BLOCK",
                "merged_campaign_ids": [],
                "new_campaign_ids": [],
                "partially_merged": False,
                "promise_basis": "MATERIAL_AVAILABILITY",
                "schedule_mode": "NONE",
                "solver_status": "MATERIAL HOLD",
                "solver_detail": "MATERIAL_OR_STRUCTURE_BLOCK",
                "terminal_resource": None,
                "bottleneck_resource": None,
                "critical_path_resource": None,
                "primary_blocker_type": blocker["type"],
                "primary_blocker_code": blocker["code"],
                "primary_blocker_text": blocker["text"],
                "secondary_blockers": _secondary_blockers(
                    inventory_lineage=inventory_lineage,
                    material_gaps=material_gaps,
                    structure_errors=structure_errors,
                    best_join=best_join,
                    schedule_result={"solver_status": "MATERIAL HOLD", "solver_detail": "MATERIAL_OR_STRUCTURE_BLOCK"},
                ),
                "latent_blockers": [],
                "alternatives": [],
                "best_alternative": None,
                "decision_trace": [
                    "Ghost campaign(s) were constructed from the request.",
                    "Material simulation was executed against net inventory after committed campaigns.",
                    "Material or BOM structure issues blocked release before scheduling.",
                ],
                "assumption_flags": [],
                "data_quality_flags": [inventory_lineage.get("inventory_lineage_status")],
                "warning_flags": ["MATERIAL_BLOCK"] if material_gaps else ["STRUCTURE_BLOCK"],
                "narrative": _request_narrative(
                    decision_class=decision_class,
                    qty_mt=qty_mt,
                    requested_ts=requested_ts,
                    earliest_completion=None,
                    primary_blocker=blocker,
                    promise_confidence="HIGH" if not structure_errors else "LOW",
                    merged_into_existing=False,
                    material_gaps=material_gaps,
                ),
                "scenario_evaluated": True,
            }
        )
        return result

    prepared = _prepare_combined_campaigns(
        ghost_campaigns,
        committed_campaigns,
        best_join=best_join,
        bom=bom,
        config=config,
    )
    frozen_jobs = frozen_jobs or _frozen_jobs_from_campaigns(committed_campaigns)

    schedule_result = schedule(
        prepared["combined_campaigns"],
        resources,
        planning_start=planning_anchor,
        planning_horizon_days=max(resolve_config_int(config, "PLANNING_HORIZON_DAYS", 14), 1),
        frozen_jobs=frozen_jobs,
        routing=routing,
        queue_times=queue_times,
        changeover_matrix=changeover_matrix,
        config=config,
        solver_time_limit_sec=resolve_config_float(config, "SOLVER_TIME_LIMIT_SECONDS", 30.0),
    )

    ghost_rows = _extract_ghost_rows(
        schedule_result.get("heat_schedule", pd.DataFrame()),
        prepared["request_job_ids"],
        prepared["target_campaign_ids"],
    )

    earliest_completion = None
    terminal_resource = None
    if not ghost_rows.empty:
        last_row = ghost_rows.sort_values(["Planned_End", "Planned_Start", "Resource_ID"]).iloc[-1]
        earliest_completion = pd.to_datetime(last_row["Planned_End"], errors="coerce")
        terminal_resource = _safe_str(last_row.get("Resource_ID")) or None

    bottleneck_resource, bottleneck_detail = _derive_bottleneck(ghost_rows)
    completion_gap_days = None
    lateness_days = None
    on_time = None
    if earliest_completion is not None:
        completion_gap_days = round((earliest_completion - requested_ts).total_seconds() / 86400.0, 2)
        lateness_days = max(completion_gap_days, 0.0)
        on_time = earliest_completion <= requested_ts

    master_data_failure = _has_master_data_failure(schedule_result)
    decision_class = _decision_class(
        stock_only=False,
        on_time=on_time,
        earliest_completion=earliest_completion,
        material_gaps=[],
        structure_errors=[],
        inventory_trust_blocked=False,
        master_data_failure=master_data_failure,
        merged_into_existing=prepared["merged_into_existing"],
        solver_status=_safe_str(schedule_result.get("solver_status")),
    )
    promise_confidence, confidence_flags = _schedule_confidence(
        schedule_result=schedule_result,
        inventory_lineage_status=inventory_lineage.get("inventory_lineage_status"),
        material_hold=False,
    )
    blocker = _primary_blocker(
        decision_class=decision_class,
        inventory_lineage=inventory_lineage,
        material_gaps=[],
        structure_errors=[],
        bottleneck_resource=bottleneck_resource,
        schedule_result=schedule_result,
        best_join=best_join,
    )

    result.update(
        {
            "decision_class": decision_class,
            "promise_confidence": promise_confidence,
            "exact_requested_qty_feasible": bool(on_time),
            "exact_requested_date_feasible": bool(on_time),
            "full_qty_feasible": earliest_completion is not None,
            "promised_qty_mt": round(float(qty_mt or 0.0), 3) if earliest_completion is not None else 0.0,
            "promised_completion_date": earliest_completion,
            "earliest_completion": earliest_completion,
            "earliest_delivery": None,
            "plant_completion_feasible": on_time,
            "delivery_feasible": None,
            "feasible": bool(on_time) if on_time is not None else False,
            "lateness_days": lateness_days,
            "completion_gap_days": completion_gap_days,
            "material_gaps": [],
            "material_structure_errors": [],
            "joins_campaign": best_join.get("candidate_id"),
            "best_join_candidate_id": best_join.get("candidate_id"),
            "join_candidate_ids": [c.get("candidate_id") for c in best_join.get("all_candidates", []) if c.get("candidate_id")],
            "mergeability_score": best_join.get("score"),
            "mergeability_fail_reasons": best_join.get("reasons", []),
            "new_campaign_needed": bool(prepared["new_campaign_ids"]),
            "campaign_action": (
                "PARTIAL_MERGE_AND_NEW" if prepared["partially_merged"] else
                "MERGED_ONLY" if prepared["merged_campaign_ids"] else
                "NEW_CAMPAIGN_ONLY"
            ),
            "merged_campaign_ids": prepared["merged_campaign_ids"],
            "new_campaign_ids": prepared["new_campaign_ids"],
            "partially_merged": prepared["partially_merged"],
            "promise_basis": "PLANT_COMPLETION_MERGED" if prepared["merged_into_existing"] else "PLANT_COMPLETION",
            "schedule_mode": "FINITE",
            "solver_status": schedule_result.get("solver_status", "UNKNOWN"),
            "solver_detail": schedule_result.get("solver_detail", ""),
            "terminal_resource": terminal_resource,
            "bottleneck_resource": bottleneck_resource,
            "critical_path_resource": bottleneck_resource,
            "bottleneck_detail": bottleneck_detail,
            "primary_blocker_type": blocker["type"],
            "primary_blocker_code": blocker["code"],
            "primary_blocker_text": blocker["text"],
            "secondary_blockers": _secondary_blockers(
                inventory_lineage=inventory_lineage,
                material_gaps=[],
                structure_errors=[],
                best_join=best_join,
                schedule_result=schedule_result,
            ),
            "latent_blockers": [],
            "alternatives": [],
            "best_alternative": None,
            "decision_trace": [
                "Ghost campaign(s) were constructed from the request.",
                "Material availability was validated against net inventory after committed campaigns.",
                "Merge/new-campaign scenario was selected.",
                f"Finite schedule evaluated with solver status {schedule_result.get('solver_status', 'UNKNOWN')}.",
            ],
            "assumption_flags": confidence_flags,
            "data_quality_flags": [inventory_lineage.get("inventory_lineage_status")],
            "warning_flags": ["HEURISTIC_ONLY"] if _safe_str(schedule_result.get("solver_status")).upper() in HEURISTIC_SOLVER_STATUSES else [],
            "narrative": _request_narrative(
                decision_class=decision_class,
                qty_mt=qty_mt,
                requested_ts=requested_ts,
                earliest_completion=earliest_completion,
                primary_blocker=blocker,
                promise_confidence=promise_confidence,
                merged_into_existing=prepared["merged_into_existing"],
                material_gaps=[],
            ),
            "heat_schedule": schedule_result.get("heat_schedule"),
            "campaign_schedule": schedule_result.get("campaign_schedule"),
            "weighted_lateness_hours": schedule_result.get("weighted_lateness_hours"),
            "scenario_evaluated": True,
        }
    )
    return result


def _scenario_is_on_time(scenario: dict | None) -> bool:
    if not isinstance(scenario, dict):
        return False
    return bool(scenario.get("exact_requested_qty_feasible")) and bool(scenario.get("exact_requested_date_feasible"))


def _scenario_rank_key(scenario: dict | None, config: dict | None = None) -> tuple:
    scenario = scenario or {}
    decision_class = _safe_str(scenario.get("decision_class"))
    precedence = _decision_precedence_lookup(config).get(decision_class, 999)
    promised_qty = -float(scenario.get("promised_qty_mt", 0.0) or 0.0)
    promised_date = _coerce_timestamp(scenario.get("promised_completion_date")) or pd.Timestamp.max
    confidence_rank = {"HIGH": 0, "MEDIUM": 1, "LOW": 2}.get(_safe_str(scenario.get("promise_confidence")).upper(), 9)
    merge_penalty = 0 if scenario.get("merged_campaign_ids") else 1
    return (precedence, promised_date, promised_qty, confidence_rank, merge_penalty)


def _find_max_qty_by_date(
    *,
    sku_id: str,
    requested_qty: float,
    requested_ts: pd.Timestamp,
    campaigns: list,
    resources: pd.DataFrame,
    bom: pd.DataFrame,
    inventory,
    routing: pd.DataFrame,
    skus: pd.DataFrame,
    planning_anchor: datetime,
    config: dict | None,
    campaign_config: pd.DataFrame | None,
    min_campaign_mt: float,
    max_campaign_mt: float,
    frozen_jobs: dict | None,
    queue_times: dict | None,
    changeover_matrix: pd.DataFrame | None,
) -> dict | None:
    precision = _qty_precision(config)
    requested_qty = round(float(requested_qty or 0.0), 3)
    if requested_qty <= precision:
        return None

    low = 0.0
    high = requested_qty
    best = None
    tried = set()

    for _ in range(12):
        mid = round(((low + high) / 2.0) / precision) * precision
        mid = max(min(mid, requested_qty), 0.0)
        key = round(mid, 3)
        if key in tried:
            if high - low <= precision:
                break
            mid = round((high - precision) / precision) * precision
            key = round(mid, 3)
            if key in tried:
                break
        tried.add(key)
        if mid <= 0:
            low = max(low, precision)
            continue

        scenario = _evaluate_scenario(
            sku_id=sku_id,
            qty_mt=mid,
            requested_ts=requested_ts,
            campaigns=campaigns,
            resources=resources,
            bom=bom,
            inventory=inventory,
            routing=routing,
            skus=skus,
            planning_anchor=planning_anchor,
            config=config,
            campaign_config=campaign_config,
            min_campaign_mt=min_campaign_mt,
            max_campaign_mt=max_campaign_mt,
            frozen_jobs=frozen_jobs,
            queue_times=queue_times,
            changeover_matrix=changeover_matrix,
            scenario_name=f"PARTIAL_QTY_{mid:g}",
        )
        if _scenario_is_on_time(scenario):
            best = scenario
            low = mid + precision
        else:
            high = mid - precision
        if high < low:
            break
    return best


def _augment_with_alternatives(
    base: dict,
    *,
    sku_id: str,
    requested_qty: float,
    requested_ts: pd.Timestamp,
    campaigns: list,
    resources: pd.DataFrame,
    bom: pd.DataFrame,
    inventory,
    routing: pd.DataFrame,
    skus: pd.DataFrame,
    planning_anchor: datetime,
    config: dict | None,
    campaign_config: pd.DataFrame | None,
    min_campaign_mt: float,
    max_campaign_mt: float,
    frozen_jobs: dict | None,
    queue_times: dict | None,
    changeover_matrix: pd.DataFrame | None,
) -> dict:
    base = dict(base or {})
    alternatives = []

    full_later = None
    if base.get("decision_class") == "PROMISE_LATER_DATE":
        full_later = base
        alternatives.append(
            _build_alternative(
                alternative_type="FULL_QTY_LATER_DATE",
                description="Full requested quantity is feasible, but only after the requested date.",
                scenario=base,
                tradeoffs=["Misses requested date"],
            )
        )

    partial = _find_max_qty_by_date(
        sku_id=sku_id,
        requested_qty=requested_qty,
        requested_ts=requested_ts,
        campaigns=campaigns,
        resources=resources,
        bom=bom,
        inventory=inventory,
        routing=routing,
        skus=skus,
        planning_anchor=planning_anchor,
        config=config,
        campaign_config=campaign_config,
        min_campaign_mt=min_campaign_mt,
        max_campaign_mt=max_campaign_mt,
        frozen_jobs=frozen_jobs,
        queue_times=queue_times,
        changeover_matrix=changeover_matrix,
    )
    if partial and float(partial.get("promised_qty_mt", 0.0) or 0.0) > 0:
        remainder = max(round(float(requested_qty or 0.0) - float(partial.get("promised_qty_mt", 0.0) or 0.0), 3), 0.0)
        description = f"Split promise: {partial.get('promised_qty_mt')} MT by requested date"
        if full_later and full_later.get("promised_completion_date") is not None:
            description += f", balance {remainder} MT by {pd.to_datetime(full_later.get('promised_completion_date')).strftime('%Y-%m-%d %H:%M')}"
        alternatives.append(
            _build_alternative(
                alternative_type="SPLIT_PROMISE",
                description=description,
                scenario=partial,
                tradeoffs=["Requires split execution", f"Balance remaining {remainder} MT later" if remainder > 0 else ""],
            )
        )
        base["max_feasible_qty_by_requested_date_mt"] = round(float(partial.get("promised_qty_mt", 0.0) or 0.0), 3)
        base["partial_feasible_by_requested_date"] = True
        base["partial_qty_scenario"] = partial
    else:
        base["max_feasible_qty_by_requested_date_mt"] = 0.0
        base["partial_feasible_by_requested_date"] = False
        base["partial_qty_scenario"] = None

    if base.get("decision_class") in {"CANNOT_PROMISE_MATERIAL", "CANNOT_PROMISE_CAPACITY", "PROMISE_LATER_DATE"}:
        relaxed_min = min(min_campaign_mt, max(_qty_precision(config), 0.0))
        advisory = _evaluate_scenario(
            sku_id=sku_id,
            qty_mt=requested_qty,
            requested_ts=requested_ts,
            campaigns=campaigns,
            resources=resources,
            bom=bom,
            inventory=inventory,
            routing=routing,
            skus=skus,
            planning_anchor=planning_anchor,
            config={
                **(config or {}),
                "Require_Authoritative_CTP_Inventory": resolve_config_value(
                    config,
                    "Require_Authoritative_CTP_Inventory",
                    "Y",
                ),
            },
            campaign_config=campaign_config,
            min_campaign_mt=relaxed_min,
            max_campaign_mt=max(max_campaign_mt, requested_qty),
            frozen_jobs=frozen_jobs,
            queue_times=queue_times,
            changeover_matrix=changeover_matrix,
            scenario_name="POLICY_RELAXED_ADVISORY",
        )
        if advisory.get("decision_class", "").startswith("PROMISE_CONFIRMED") or advisory.get("decision_class") == "PROMISE_LATER_DATE":
            alternatives.append(
                _build_alternative(
                    alternative_type="POLICY_RELAXED_ADVISORY",
                    description="Advisory scenario with relaxed campaign-size policy.",
                    scenario=advisory,
                    required_assumptions=["Allow urgent small campaign or policy override"],
                    tradeoffs=["Uses policy exception"],
                )
            )
            base["policy_relaxed_scenario"] = advisory

    alternatives = [alt for alt in alternatives if alt.get("description")]
    best_alt = None
    if alternatives:
        alt_scenarios = [
            base if alt.get("alternative_type") == "FULL_QTY_LATER_DATE" and full_later is base else
            partial if alt.get("alternative_type") == "SPLIT_PROMISE" else
            base.get("policy_relaxed_scenario") if alt.get("alternative_type") == "POLICY_RELAXED_ADVISORY" else None
            for alt in alternatives
        ]
        valid_pairs = [(alt, scn) for alt, scn in zip(alternatives, alt_scenarios) if scn is not None]
        if valid_pairs:
            best_alt = min(valid_pairs, key=lambda pair: _scenario_rank_key(pair[1], config=config))[0]

    base["alternatives"] = alternatives
    base["best_alternative"] = best_alt

    if base.get("decision_class") in {"CANNOT_PROMISE_MATERIAL", "CANNOT_PROMISE_CAPACITY", "PROMISE_LATER_DATE"} and partial and float(partial.get("promised_qty_mt", 0.0) or 0.0) > 0:
        base["decision_class"] = "PROMISE_SPLIT_REQUIRED" if base.get("decision_class") != "PROMISE_LATER_DATE" else "PROMISE_SPLIT_REQUIRED"
        base["primary_blocker_type"] = base.get("primary_blocker_type") or "CAPACITY"
        base["primary_blocker_code"] = base.get("primary_blocker_code") or "PARTIAL_ONLY"
        base["primary_blocker_text"] = (
            f"Full requested quantity cannot be promised on the requested date, but {partial.get('promised_qty_mt')} MT is feasible by the requested date."
        )
        base["narrative"] = base["primary_blocker_text"]

    return base


def capable_to_promise(
    sku_id: str,
    qty_mt: float,
    requested_date,
    campaigns: list,
    resources: pd.DataFrame,
    bom: pd.DataFrame,
    inventory,
    routing: pd.DataFrame,
    skus: pd.DataFrame,
    planning_start,
    config: dict = None,
    *,
    campaign_config: pd.DataFrame | None = None,
    min_campaign_mt: float | None = None,
    max_campaign_mt: float | None = None,
    frozen_jobs: dict | None = None,
    queue_times: dict | None = None,
    changeover_matrix: pd.DataFrame | None = None,
) -> dict:
    requested_ts = pd.to_datetime(requested_date)
    planning_anchor = _normalize_planning_start(planning_start, requested_ts, config=config)
    qty_mt = round(float(qty_mt or 0.0), 3)

    effective_min_campaign_mt = float(
        min_campaign_mt if min_campaign_mt is not None else resolve_config_value(config, "CAMPAIGN_MIN_SIZE_MT", 100.0) or 100.0
    )
    effective_max_campaign_mt = float(
        max_campaign_mt if max_campaign_mt is not None else resolve_config_value(config, "CAMPAIGN_MAX_SIZE_MT", 500.0) or 500.0
    )

    base = _evaluate_scenario(
        sku_id=sku_id,
        qty_mt=qty_mt,
        requested_ts=requested_ts,
        campaigns=campaigns,
        resources=resources,
        bom=bom,
        inventory=inventory,
        routing=routing,
        skus=skus,
        planning_anchor=planning_anchor,
        config=config,
        campaign_config=campaign_config,
        min_campaign_mt=effective_min_campaign_mt,
        max_campaign_mt=effective_max_campaign_mt,
        frozen_jobs=frozen_jobs,
        queue_times=queue_times,
        changeover_matrix=changeover_matrix,
        scenario_name="BASE",
    )

    # Keep original top-level fields for backward compatibility.
    base["new_campaign_needed"] = bool(base.get("new_campaign_ids"))
    base["plant_completion_feasible"] = base.get("exact_requested_date_feasible")
    base["delivery_feasible"] = None
    base["earliest_delivery"] = None
    base["feasible"] = bool(base.get("exact_requested_date_feasible"))

    if base.get("decision_class") not in {"PROMISE_CONFIRMED_STOCK_ONLY", "PROMISE_CONFIRMED_MERGED", "PROMISE_CONFIRMED_NEW_CAMPAIGN", "PROMISE_HEURISTIC_ONLY"}:
        base = _augment_with_alternatives(
            base,
            sku_id=sku_id,
            requested_qty=qty_mt,
            requested_ts=requested_ts,
            campaigns=campaigns,
            resources=resources,
            bom=bom,
            inventory=inventory,
            routing=routing,
            skus=skus,
            planning_anchor=planning_anchor,
            config=config,
            campaign_config=campaign_config,
            min_campaign_mt=effective_min_campaign_mt,
            max_campaign_mt=effective_max_campaign_mt,
            frozen_jobs=frozen_jobs,
            queue_times=queue_times,
            changeover_matrix=changeover_matrix,
        )

    if base.get("best_alternative") and not base.get("alternatives"):
        base["alternatives"] = [base["best_alternative"]]

    return base
