"""
Capable-to-promise engine.

Builds a ghost demand request, checks material availability net of the committed
plan, freezes the committed schedule, and slots the ghost campaign around it.
"""
from __future__ import annotations

from datetime import datetime

import pandas as pd

from engine.bom_explosion import inventory_map
from engine.campaign import build_campaigns, _heats_needed_from_lines
from engine.scheduler import schedule

COMMITTED_STATUSES = {"RELEASED", "RUNNING LOCK"}
DEGRADED_INVENTORY_LINEAGE_STATUSES = {"RECOMPUTED_FROM_CONSUMPTION", "CONSERVATIVE_BLEND"}


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
        horizon_days = max(int((config or {}).get("Planning_Horizon_Days", 14) or 14), 1)
        ts = requested_ts - pd.Timedelta(days=horizon_days)
    anchor = ts.to_pydatetime() if hasattr(ts, "to_pydatetime") else ts
    return anchor.replace(minute=0, second=0, microsecond=0)


def _config_flag(config: dict | None, key: str, default: str = "N") -> bool:
    value = str((config or {}).get(key, default) or default).strip().upper()
    return value in {"Y", "YES", "TRUE", "1", "ON"}


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


def _net_inventory_after_committed(campaigns: list, inventory) -> dict:
    return _net_inventory_after_committed_details(campaigns, inventory)["inventory"]


def _frozen_jobs_from_campaigns(campaigns: list) -> dict:
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


def _join_candidate(ghost_campaigns: list, campaigns: list) -> str | None:
    if not ghost_campaigns:
        return None
    ghost = ghost_campaigns[0]
    for camp in campaigns:
        if str(camp.get("release_status", "")).upper() not in COMMITTED_STATUSES:
            continue
        if (
            str(camp.get("campaign_group", "")) == str(ghost.get("campaign_group", ""))
            and str(camp.get("grade", "")) == str(ghost.get("grade", ""))
            and str(camp.get("billet_family", "")) == str(ghost.get("billet_family", ""))
            and bool(camp.get("needs_vd")) == bool(ghost.get("needs_vd"))
        ):
            return str(camp.get("campaign_id") or "").strip() or None
    return None


def _campaign_matches_join_target(campaign: dict, target_campaign: dict) -> bool:
    return (
        str(campaign.get("campaign_group", "")) == str(target_campaign.get("campaign_group", ""))
        and str(campaign.get("grade", "")) == str(target_campaign.get("grade", ""))
        and str(campaign.get("billet_family", "")) == str(target_campaign.get("billet_family", ""))
        and bool(campaign.get("needs_vd")) == bool(target_campaign.get("needs_vd"))
    )


def _merge_into_campaign(target_campaign: dict, ghost_campaign: dict, bom=None, config=None) -> dict:
    """Merge ghost production orders into a matched committed campaign."""
    merged = dict(target_campaign)
    ghost_orders = ghost_campaign.get("production_orders", [])
    existing_orders = list(merged.get("production_orders", []))

    # Append ghost POs with renumbered IDs
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
        float(merged.get("total_coil_mt", 0) or 0) + float(ghost_campaign.get("total_coil_mt", 0) or 0), 1
    )
    merged["order_count"] = len(set(
        str(o.get("so_id", "")) for o in existing_orders if str(o.get("so_id", "")).strip()
    ))

    # Recalculate heats
    merged["heats"] = _heats_needed_from_lines(
        existing_orders,
        bom=bom,
        config=config,
    )

    # Update SO list
    existing_so_ids = list(merged.get("so_ids", []))
    for order in ghost_orders:
        so_id = str(order.get("so_id", "")).strip()
        if so_id and so_id not in existing_so_ids:
            existing_so_ids.append(so_id)
    merged["so_ids"] = existing_so_ids

    return merged


def _campaign_action_summary(merged_campaign_ids: list[str], new_campaign_ids: list[str], *, stock_only: bool = False) -> str:
    if stock_only:
        return "STOCK_ONLY"
    if merged_campaign_ids and new_campaign_ids:
        return "PARTIAL_MERGE_AND_NEW"
    if merged_campaign_ids:
        return "MERGED_ONLY"
    if new_campaign_ids:
        return "NEW_CAMPAIGN_ONLY"
    return ""


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
    min_campaign_mt: float | None = None,
    max_campaign_mt: float | None = None,
    frozen_jobs: dict | None = None,
    queue_times: dict | None = None,
    changeover_matrix: pd.DataFrame | None = None,
) -> dict:
    requested_ts = pd.to_datetime(requested_date)
    planning_anchor = _normalize_planning_start(planning_start, requested_ts, config=config)

    # Finding 1: Filter to committed campaigns only for capacity reservation
    committed_campaigns = [
        dict(camp) for camp in (campaigns or [])
        if str(camp.get("release_status", "")).upper() in COMMITTED_STATUSES
    ]

    inventory_lineage = _net_inventory_after_committed_details(committed_campaigns, inventory)
    net_inventory = inventory_lineage["inventory"]
    result_base = {
        "sku_id": sku_id,
        "qty_mt": float(qty_mt or 0.0),
        "requested_date": requested_ts,
        "inventory_lineage_status": inventory_lineage["inventory_lineage_status"],
        "inventory_lineage_note": inventory_lineage["inventory_lineage_note"],
    }
    if (
        inventory_lineage["inventory_lineage_status"] in DEGRADED_INVENTORY_LINEAGE_STATUSES
        and _config_flag(config, "Require_Authoritative_CTP_Inventory", "Y")
    ):
        return {
            **result_base,
            "earliest_completion": None,
            "earliest_delivery": None,
            "plant_completion_feasible": None,
            "delivery_feasible": None,
            "feasible": None,
            "lateness_days": None,
            "completion_gap_days": None,
            "material_gaps": [],
            "joins_campaign": None,
            "new_campaign_needed": True,
            "campaign_action": "INVENTORY_LINEAGE_BLOCKED",
            "merged_campaign_ids": [],
            "new_campaign_ids": [],
            "partially_merged": False,
            "promise_basis": "INVENTORY_LINEAGE_BLOCKED",
            "delivery_modeled": False,
            "terminal_resource": None,
            "bottleneck_resource": None,
            "solver_status": f"BLOCKED: {inventory_lineage['inventory_lineage_status']}",
        }
    ghost_so = _ghost_sales_order(
        sku_id,
        qty_mt,
        requested_ts,
        skus,
        order_date=planning_anchor,
    )

    # Finding 3: Use caller-provided campaign sizing if available, fall back to config
    _min_mt = float(min_campaign_mt if min_campaign_mt is not None else (config or {}).get("Min_Campaign_MT", 100.0) or 100.0)
    _max_mt = float(max_campaign_mt if max_campaign_mt is not None else (config or {}).get("Max_Campaign_MT", 500.0) or 500.0)

    ghost_campaigns = build_campaigns(
        ghost_so,
        min_campaign_mt=_min_mt,
        max_campaign_mt=_max_mt,
        inventory=net_inventory,
        bom=bom,
        config=config,
        skus=skus,
    )

    if not ghost_campaigns:
        earliest = pd.to_datetime(planning_anchor)
        return {
            **result_base,
            "earliest_completion": earliest,
            "earliest_delivery": None,
            "plant_completion_feasible": earliest <= requested_ts,
            "delivery_feasible": None,
            "feasible": None,
            "lateness_days": None,
            "completion_gap_days": round((earliest - requested_ts).total_seconds() / 86400.0, 2),
            "material_gaps": [],
            "joins_campaign": None,
            "new_campaign_needed": False,
            "campaign_action": _campaign_action_summary([], [], stock_only=True),
            "merged_campaign_ids": [],
            "new_campaign_ids": [],
            "partially_merged": False,
            "promise_basis": "STOCK_AT_PLANNING_START",
            "delivery_modeled": False,
            "terminal_resource": None,
            "bottleneck_resource": None,
            "solver_status": "STOCK",
        }

    join_candidate = _join_candidate(ghost_campaigns, committed_campaigns)

    material_gaps = []
    for camp in ghost_campaigns:
        for material_sku, shortage_qty in (camp.get("material_shortages") or {}).items():
            material_gaps.append(
                {
                    "sku_id": str(material_sku).strip(),
                    "shortage_qty": round(float(shortage_qty or 0.0), 3),
                    "impacts_mt": round(float(camp.get("total_coil_mt", 0.0) or 0.0), 3),
                }
            )
    if material_gaps:
        return {
            **result_base,
            "earliest_completion": None,
            "earliest_delivery": None,
            "plant_completion_feasible": None,
            "delivery_feasible": None,
            "feasible": False,
            "lateness_days": None,
            "completion_gap_days": None,
            "material_gaps": material_gaps,
            "joins_campaign": join_candidate,
            "new_campaign_needed": join_candidate is None,
            "campaign_action": "MATERIAL_BLOCK",
            "merged_campaign_ids": [],
            "new_campaign_ids": [],
            "partially_merged": False,
            "promise_basis": "MATERIAL_AVAILABILITY",
            "delivery_modeled": False,
            "terminal_resource": None,
            "bottleneck_resource": None,
            "solver_status": "MATERIAL HOLD",
        }

    frozen_jobs = frozen_jobs or _frozen_jobs_from_campaigns(committed_campaigns)
    ghost_request_so_ids = {
        str(order.get("so_id", "")).strip()
        for camp in ghost_campaigns
        for order in camp.get("production_orders", [])
        if str(order.get("so_id", "")).strip()
    }
    request_job_ids: set[str] = set()
    target_campaign_ids: set[str] = set()
    merged_into_existing = False
    merged_campaign_ids: list[str] = []
    new_campaign_ids: list[str] = []

    # Finding 2: If a join candidate exists, merge ghost into it instead of scheduling separately
    if join_candidate and ghost_campaigns:
        target_campaign = next(
            (
                camp for camp in committed_campaigns
                if str(camp.get("campaign_id", "")).strip() == join_candidate
            ),
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
            if str(camp.get("campaign_id", "")).strip() == join_candidate and not merged:
                merged_camp = dict(camp)
                for ghost_camp in mergeable_ghosts:
                    merged_camp = _merge_into_campaign(merged_camp, ghost_camp, bom=bom, config=config)
                merged_campaigns.append(merged_camp)
                request_job_ids = {
                    f"{order['production_order_id']}-RM"
                    for order in merged_camp.get("production_orders", [])
                    if str(order.get("so_id", "")).strip() in ghost_request_so_ids
                }
                merged = True
            else:
                merged_campaigns.append(camp)
        merged_into_existing = merged
        if merged_into_existing and join_candidate:
            merged_campaign_ids = [join_candidate]
        renamed_remainder = _rename_ghost_campaigns(remainder_ghosts, committed_campaigns) if remainder_ghosts else []
        new_campaign_ids = [str(camp.get("campaign_id", "")).strip() for camp in renamed_remainder if str(camp.get("campaign_id", "")).strip()]
        request_job_ids.update(
            f"{order['production_order_id']}-RM"
            for camp in renamed_remainder
            for order in camp.get("production_orders", [])
        )
        target_campaign_ids = {join_candidate} if merged_into_existing else set()
        target_campaign_ids.update(camp["campaign_id"] for camp in renamed_remainder)
        combined_campaigns = merged_campaigns + renamed_remainder
    else:
        ghost_campaigns = _rename_ghost_campaigns(ghost_campaigns, committed_campaigns)
        new_campaign_ids = [str(camp.get("campaign_id", "")).strip() for camp in ghost_campaigns if str(camp.get("campaign_id", "")).strip()]
        request_job_ids = {
            f"{order['production_order_id']}-RM"
            for camp in ghost_campaigns
            for order in camp.get("production_orders", [])
        }
        target_campaign_ids = {camp["campaign_id"] for camp in ghost_campaigns}
        combined_campaigns = committed_campaigns + ghost_campaigns

    schedule_result = schedule(
        combined_campaigns,
        resources,
        planning_start=planning_anchor,
        planning_horizon_days=int((config or {}).get("Planning_Horizon_Days", 14) or 14),
        frozen_jobs=frozen_jobs,
        routing=routing,
        queue_times=queue_times,
        changeover_matrix=changeover_matrix,
        config=config,
        solver_time_limit_sec=float((config or {}).get("Default_Solver_Limit_Sec", 30.0) or 30.0),
    )

    heat_df = schedule_result.get("heat_schedule", pd.DataFrame()).copy()

    if not heat_df.empty:
        heat_df["Job_ID"] = heat_df.get("Job_ID", "").fillna("").astype(str)
        heat_df["Campaign"] = heat_df.get("Campaign", "").fillna("").astype(str)
        heat_df["Planned_End"] = pd.to_datetime(heat_df.get("Planned_End"), errors="coerce")
        if request_job_ids:
            ghost_rows = heat_df[heat_df["Job_ID"].isin(request_job_ids)].copy()
        else:
            ghost_rows = heat_df[heat_df["Campaign"].isin(target_campaign_ids)].copy()
    else:
        ghost_rows = pd.DataFrame()

    earliest_completion = None
    terminal_resource = None
    if not ghost_rows.empty:
        last_row = ghost_rows.sort_values(["Planned_End", "Planned_Start", "Resource_ID"]).iloc[-1]
        earliest_completion = pd.to_datetime(last_row["Planned_End"])
        terminal_resource = str(last_row.get("Resource_ID", "") or "").strip() or None

    completion_gap_days = None
    lateness_days = None
    if earliest_completion is not None:
        completion_gap_days = round((earliest_completion - requested_ts).total_seconds() / 86400.0, 2)

    return {
        **result_base,
        "earliest_completion": earliest_completion,
        "earliest_delivery": None,
        "plant_completion_feasible": (
            earliest_completion <= requested_ts if earliest_completion is not None else None
        ),
        "delivery_feasible": None,
        "feasible": None,
        "lateness_days": lateness_days,
        "completion_gap_days": completion_gap_days,
        "material_gaps": material_gaps,
        "joins_campaign": join_candidate,
        "new_campaign_needed": bool(new_campaign_ids),
        "campaign_action": _campaign_action_summary(merged_campaign_ids, new_campaign_ids),
        "merged_campaign_ids": merged_campaign_ids,
        "new_campaign_ids": new_campaign_ids,
        "partially_merged": bool(merged_campaign_ids and new_campaign_ids),
        "promise_basis": "PLANT_COMPLETION_MERGED" if merged_into_existing else "PLANT_COMPLETION",
        "delivery_modeled": False,
        "terminal_resource": terminal_resource,
        "bottleneck_resource": None,
        "solver_status": schedule_result.get("solver_status", "UNKNOWN"),
    }
