"""
Campaign Engine
---------------
Builds APS-style release campaigns from prioritized sales orders.

Concept used here:
  - Sales orders are prioritized first.
  - Finished-good stock is consumed before creating production demand.
  - Compatible sales orders are grouped into an SMS campaign family.
  - One campaign contains a list of production orders derived from those SOs.
  - Material availability decides whether the campaign is releasable or held.
"""
import math

import pandas as pd

from engine.bom_explosion import (
    _effective_yield,
    _input_bom_rows,
    inventory_map,
    simulate_material_commit,
)
from engine.config import get_config

# Core batch and yield parameters from Algorithm_Config
def _get_heat_size_mt():
    """SMS standard heat size (MT)."""
    return get_config().get_float('HEAT_SIZE_MT', 50.0)

def _get_ccm_yield():
    """CCM casting yield factor."""
    return get_config().get_percentage('YIELD_CCM_PCT', 95) / 100

def _get_rm_yield_by_section():
    """RM rolling yield factors per section size (mm)."""
    config = get_config()
    return {
        5.5: config.get_percentage('YIELD_RM_5_5MM_PCT', 88) / 100,
        6.5: config.get_percentage('YIELD_RM_6_5MM_PCT', 89) / 100,
        8.0: config.get_percentage('YIELD_RM_8_0MM_PCT', 90) / 100,
        10.0: config.get_percentage('YIELD_RM_10_0MM_PCT', 91) / 100,
        12.0: config.get_percentage('YIELD_RM_12_0MM_PCT', 92) / 100,
    }

def _get_default_rm_yield():
    """Default RM rolling yield (when section not specified)."""
    return get_config().get_percentage('YIELD_RM_DEFAULT_PCT', 89) / 100

def _get_vd_required_grades():
    """Grades that require VD (vacuum degassing)."""
    vd_list = get_config().get_list('VD_REQUIRED_GRADES', ['1080', 'CHQ1006', 'CrMo4140'])
    return set(vd_list)

def _get_low_carbon_billet_grades():
    """Grades that use BIL-130 (low carbon) vs BIL-150."""
    lc_list = get_config().get_list('LOW_CARBON_BILLET_GRADES', ['1008', '1018', '1035'])
    return set(lc_list)

# Primary batch resource prefixes (these don't change)
PRIMARY_BATCH_PREFIXES = {
    "EAF": ("EAF-OUT-",),
    "LRF": ("LRF-OUT-",),
    "VD": ("VD-OUT-",),
    "CCM": ("BIL-",),
    "RM": ("RM-OUT-",),
}

# Grade scheduling order (these don't change)
GRADE_ORDER = {
    "SAE 1008": 1,
    "SAE 1018": 2,
    "SAE 1035": 3,
    "SAE 1045": 4,
    "SAE 1065": 5,
    "SAE 1080": 6,
    "CHQ 1006": 7,
    "Cr-Mo 4140": 8,
}

# Priority order (standard APS)
PRIORITY_ORDER = {"URGENT": 1, "HIGH": 2, "NORMAL": 3, "LOW": 4}


def priority_rank(priority: str) -> int:
    return PRIORITY_ORDER.get(str(priority or "").strip().upper(), 9)


def needs_vd_for_grade(grade: str) -> bool:
    """Check if grade requires VD (vacuum degassing) based on config."""
    vd_grades = _get_vd_required_grades()
    return str(grade or "").strip() in vd_grades


def billet_family_for_grade(grade: str) -> str:
    """Determine billet family (BIL-130 or BIL-150) based on grade and config."""
    lc_grades = _get_low_carbon_billet_grades()
    return "BIL-130" if str(grade or "").strip() in lc_grades else "BIL-150"


def rm_minutes_for_qty(qty_coil_mt: float, section_mm: float, include_setup: bool = True) -> int:
    """Rolling duration for a production order."""
    section = pd.to_numeric(pd.Series([section_mm]), errors="coerce").fillna(6.5).iloc[0]
    sec_rate = {5.5: 0.6, 6.5: 0.5, 8.0: 0.4, 10.0: 0.35, 12.0: 0.30}
    setup_by_sec = {5.5: 45, 6.5: 40, 8.0: 35, 10.0: 30, 12.0: 25}
    rate = sec_rate.get(section, 0.5)
    setup = setup_by_sec.get(section, 40) if include_setup else 0
    return max(1, int(round(float(qty_coil_mt or 0.0) * rate + setup)))


def _matches_primary_batch_stage(sku_id: str, primary_group: str) -> bool:
    prefixes = PRIMARY_BATCH_PREFIXES.get(str(primary_group or "").strip().upper(), ())
    sku_text = str(sku_id or "").strip().upper()
    return any(sku_text.startswith(prefix) for prefix in prefixes)


def _config_flag(config: dict | None, key: str, default: bool = False) -> bool:
    value = (config or {}).get(key, default)
    if value is None or (isinstance(value, float) and pd.isna(value)):
        return default
    if isinstance(value, bool):
        return value
    text = str(value).strip().upper()
    if text in {"Y", "YES", "TRUE", "1", "ON"}:
        return True
    if text in {"N", "NO", "FALSE", "0", "OFF"}:
        return False
    return default


def _config_choice(config: dict | None, key: str, default: str, aliases: dict[str, str]) -> str:
    raw_value = str((config or {}).get(key, default) or default).strip().upper()
    normalized = aliases.get(raw_value)
    if normalized is None:
        allowed = ", ".join(sorted(dict.fromkeys(aliases.values())))
        raise ValueError(f"Config > {key} must be one of: {allowed}.")
    return normalized


def _bom_structure_error_mode(config: dict | None = None) -> str:
    return _config_choice(
        config,
        "BOM_Structure_Error_Mode",
        "RAISE",
        {
            "RAISE": "raise",
            "HARD_FAIL": "raise",
            "FAIL": "raise",
            "RECORD": "record",
            "HOLD": "record",
        },
    )


def _manual_campaign_grouping_mode(config: dict | None = None) -> str:
    return _config_choice(
        config,
        "Manual_Campaign_Grouping_Mode",
        "PRESERVE_EXACT",
        {
            "PRESERVE_EXACT": "PRESERVE_EXACT",
            "PRESERVE": "PRESERVE_EXACT",
            "EXACT": "PRESERVE_EXACT",
            "NO_SPLIT": "PRESERVE_EXACT",
            "SPLIT_TO_MAX": "SPLIT_TO_MAX",
            "SPLIT": "SPLIT_TO_MAX",
            "RESPECT_MAX": "SPLIT_TO_MAX",
        },
    )


def _legacy_primary_batch_qty(line: dict, yield_loss_pct: float = 0.0) -> float:
    section = pd.to_numeric(pd.Series([line["section_mm"]]), errors="coerce").fillna(6.5).iloc[0]
    yield_factor = max(0.01, 1 - (float(yield_loss_pct or 0.0) / 100.0))
    rm_yield_map = _get_rm_yield_by_section()
    default_rm_yield = _get_default_rm_yield()
    rm_yield = max(rm_yield_map.get(section, default_rm_yield) * yield_factor, 0.01)
    ccm_yield = max(_get_ccm_yield() * yield_factor, 0.01)
    billet_mt = float(line["qty_mt"]) / rm_yield
    return billet_mt / ccm_yield


def _bom_input_lookup(bom: pd.DataFrame | None) -> dict[str, list[dict]]:
    if bom is None or getattr(bom, "empty", True):
        return {}

    bom_rows = _input_bom_rows(bom).copy()
    if bom_rows.empty:
        return {}

    bom_rows["Qty_Per"] = pd.to_numeric(bom_rows.get("Qty_Per"), errors="coerce").fillna(0.0)
    if "Level" in bom_rows.columns:
        bom_rows["Level"] = pd.to_numeric(bom_rows.get("Level"), errors="coerce").fillna(999.0)
        bom_rows = bom_rows.sort_values(["Parent_SKU", "Level", "Child_SKU"])
    else:
        bom_rows = bom_rows.sort_values(["Parent_SKU", "Child_SKU"])

    lookup: dict[str, list[dict]] = {}
    for _, row in bom_rows.iterrows():
        parent = str(row.get("Parent_SKU", "")).strip()
        if not parent:
            continue
        lookup.setdefault(parent, []).append(
            {
                "Child_SKU": str(row.get("Child_SKU", "")).strip(),
                "Qty_Per": float(row.get("Qty_Per", 0.0) or 0.0),
                "Yield_Factor": float(_effective_yield(row)),
            }
        )
    return lookup


def _required_qty_at_primary_batch(
    sku_id: str,
    required_qty: float,
    bom_lookup: dict[str, list[dict]],
    primary_group: str,
    max_depth: int = 12,
    *,
    return_reason: bool = False,
) -> float | None | tuple[float | None, str | None]:
    target = str(primary_group or "").strip().upper()
    if not target:
        return (None, "PRIMARY_BATCH_GROUP_MISSING") if return_reason else None

    def walk(current_sku: str, qty: float, depth: int, path: tuple[str, ...]) -> tuple[float | None, str | None]:
        sku_text = str(current_sku or "").strip()
        if not sku_text:
            return None, "PRIMARY_BATCH_PATH_MISSING_SKU"
        if _matches_primary_batch_stage(sku_text, target):
            return float(qty or 0.0), None
        if depth >= max_depth:
            return None, f"PRIMARY_BATCH_MAX_DEPTH_EXCEEDED:{' -> '.join(path + (sku_text,))}"
        if sku_text in path:
            return None, f"PRIMARY_BATCH_BOM_CYCLE:{' -> '.join(path + (sku_text,))}"

        children = bom_lookup.get(sku_text, [])
        if not children:
            return None, f"PRIMARY_BATCH_PATH_NOT_FOUND:{sku_text}"

        total = 0.0
        found = False
        reasons = set()
        for child in children:
            child_sku = child["Child_SKU"]
            qty_per = float(child.get("Qty_Per", 0.0) or 0.0)
            yield_factor = max(float(child.get("Yield_Factor", 1.0) or 1.0), 0.01)
            child_required = float(qty or 0.0) * qty_per / yield_factor
            descendant, reason = walk(child_sku, child_required, depth + 1, path + (sku_text,))
            if descendant is not None:
                total += descendant
                found = True
            elif reason:
                reasons.add(reason)
        if found:
            return total, None
        reason = sorted(reasons)[0] if reasons else f"PRIMARY_BATCH_PATH_NOT_FOUND:{sku_text}"
        return None, reason

    result, reason = walk(str(sku_id or "").strip(), float(required_qty or 0.0), 0, ())
    if return_reason:
        return result, reason
    return result


def _primary_batch_trace_issue(sku_id: str, reason: str | None, *, fallback_used: bool, blocked: bool) -> dict:
    reason_text = str(reason or "").strip() or "PRIMARY_BATCH_PATH_NOT_FOUND"
    return {
        "type": "PRIMARY_BATCH_TRACE",
        "sku_id": str(sku_id or "").strip(),
        "reason": reason_text,
        "path": reason_text.split(":", 1)[1] if ":" in reason_text else "",
        "legacy_fallback_used": bool(fallback_used),
        "blocked": bool(blocked),
    }


def _heats_estimate_from_lines(
    order_lines: list,
    *,
    bom: pd.DataFrame | None = None,
    config: dict | None = None,
    yield_loss_pct: float = 0.0,
    batch_size_mt: float | None = None,
) -> dict:
    if batch_size_mt is None:
        batch_size_mt = _get_heat_size_mt()
    total_primary_batch_mt = 0.0
    primary_group = str((config or {}).get("Primary_Batch_Resource_Group", "EAF") or "EAF").strip().upper()
    bom_lookup = _bom_input_lookup(bom)
    scenario_yield = max(0.01, 1 - (float(yield_loss_pct or 0.0) / 100.0))
    allow_legacy_fallback = _config_flag(config, "Allow_Legacy_Primary_Batch_Fallback", default=False)
    if bom is None or getattr(bom, "empty", True):
        allow_legacy_fallback = True
    warnings = []
    errors = []
    used_legacy_estimate = False
    blocked_legacy_estimate = False

    for line in order_lines:
        required_primary_mt, warning = _required_qty_at_primary_batch(
            line.get("sku_id"),
            line.get("qty_mt"),
            bom_lookup,
            primary_group,
            return_reason=True,
        )
        if required_primary_mt is None:
            issue = _primary_batch_trace_issue(
                line.get("sku_id"),
                warning,
                fallback_used=allow_legacy_fallback,
                blocked=True,
            )
            if allow_legacy_fallback:
                required_primary_mt = _legacy_primary_batch_qty(line, yield_loss_pct=0.0)
                used_legacy_estimate = True
            warnings.append(issue)
            blocked_legacy_estimate = True
            errors.append(issue)
        total_primary_batch_mt += float(required_primary_mt or 0.0) / scenario_yield

    heats = max(1, math.ceil(total_primary_batch_mt / max(float(batch_size_mt), 1.0)))
    return {
        "heats": heats,
        "total_primary_batch_mt": round(total_primary_batch_mt, 3),
        "warnings": warnings,
        "errors": errors,
        "heats_trace_valid": not errors,
        "used_legacy_estimate": used_legacy_estimate,
        "blocked_legacy_estimate": blocked_legacy_estimate,
    }


def _heats_needed_from_lines(
    order_lines: list,
    *,
    bom: pd.DataFrame | None = None,
    config: dict | None = None,
    yield_loss_pct: float = 0.0,
    batch_size_mt: float | None = None,
    return_details: bool = False,
) -> int | dict:
    if batch_size_mt is None:
        batch_size_mt = _get_heat_size_mt()
    result = _heats_estimate_from_lines(
        order_lines,
        bom=bom,
        config=config,
        yield_loss_pct=yield_loss_pct,
        batch_size_mt=batch_size_mt,
    )
    if return_details:
        return result
    return result["heats"]


def _normalize_sales_orders(
    sales_orders: pd.DataFrame,
    skus: pd.DataFrame | None = None,
    config: dict | None = None,
) -> pd.DataFrame:
    so = sales_orders.copy()
    missing_cols = [col for col in ["SO_ID", "SKU_ID", "Grade"] if col not in so.columns]
    if missing_cols:
        raise ValueError(f"Sales orders are missing required columns: {', '.join(missing_cols)}")
    default_section = float((config or {}).get("Default_Section_Fallback", 6.5) or 6.5)
    if "Status" not in so.columns:
        so["Status"] = "Open"
    so["Status"] = so["Status"].fillna("Open").astype(str).str.strip()
    so = so[so["Status"].str.upper() == "OPEN"].copy()
    if so.empty:
        return so

    if "Order_Qty_MT" not in so.columns:
        so["Order_Qty_MT"] = 0.0
    if "Section_mm" not in so.columns:
        so["Section_mm"] = default_section
    if "Delivery_Date" not in so.columns:
        so["Delivery_Date"] = pd.NaT
    if "Order_Date" not in so.columns:
        so["Order_Date"] = pd.NaT
    if "Priority" not in so.columns:
        so["Priority"] = "NORMAL"
    if "Campaign_Group" not in so.columns:
        so["Campaign_Group"] = so["Grade"]

    so["Order_Qty_MT"] = pd.to_numeric(so["Order_Qty_MT"], errors="coerce").fillna(0.0)
    so["Section_mm"] = pd.to_numeric(so["Section_mm"], errors="coerce")
    so["Delivery_Date"] = pd.to_datetime(so["Delivery_Date"])
    so["Order_Date"] = pd.to_datetime(so["Order_Date"])
    so["Priority"] = so["Priority"].fillna("NORMAL").astype(str).str.upper().str.strip()
    so["Priority_Rank"] = so["Priority"].map(priority_rank).fillna(9).astype(int)
    so["Campaign_Group"] = so["Campaign_Group"].fillna(so["Grade"]).astype(str).str.strip()
    so.loc[so["Campaign_Group"] == "", "Campaign_Group"] = so["Grade"]

    sku_lookup = None
    if skus is not None and not getattr(skus, "empty", True) and "SKU_ID" in skus.columns:
        sku_lookup = skus.drop_duplicates(subset=["SKU_ID"]).set_index("SKU_ID")

    if sku_lookup is not None and "Attribute_1" in sku_lookup.columns:
        attr_values = pd.to_numeric(so["SKU_ID"].map(sku_lookup["Attribute_1"]), errors="coerce")
        so["Section_mm"] = so["Section_mm"].fillna(attr_values)
    so["Section_mm"] = so["Section_mm"].fillna(default_section)

    if sku_lookup is not None and "Route_Variant" in sku_lookup.columns:
        so["Route_Variant"] = (
            so["SKU_ID"].map(sku_lookup["Route_Variant"]).fillna("").astype(str).str.upper().str.strip()
        )
        fallback_mask = so["Route_Variant"] == ""
        so.loc[fallback_mask, "Route_Variant"] = so.loc[fallback_mask, "Grade"].map(
            lambda grade: "Y" if needs_vd_for_grade(grade) else "N"
        )
    else:
        so["Route_Variant"] = so["Grade"].map(lambda grade: "Y" if needs_vd_for_grade(grade) else "N")
    so["Needs_VD"] = so["Route_Variant"].eq("Y")

    if sku_lookup is not None and "Product_Family" in sku_lookup.columns:
        so["Product_Family"] = so["SKU_ID"].map(sku_lookup["Product_Family"]).fillna("").astype(str).str.strip()
        family_mask = so["Product_Family"] == ""
        so.loc[family_mask, "Product_Family"] = so.loc[family_mask, "Grade"].map(billet_family_for_grade)
    else:
        so["Product_Family"] = so["Grade"].map(billet_family_for_grade)
    so["Billet_Family"] = so["Product_Family"]
    so["Route_Family"] = (
        so["Campaign_Group"].astype(str)
        + "|"
        + so["Grade"].astype(str)
        + "|"
        + so["Product_Family"].astype(str)
        + "|RV:"
        + so["Route_Variant"].astype(str)
    )
    so = so.sort_values(
        ["Priority_Rank", "Delivery_Date", "Order_Date", "Grade", "Section_mm", "SO_ID"]
    ).reset_index(drop=True)
    return so


def _consume_finished_goods_stock(sales_orders: pd.DataFrame, inv_map: dict) -> tuple[pd.DataFrame, dict]:
    so = sales_orders.copy()
    so["FG_Stock_Covered_MT"] = 0.0
    so["Make_Qty_MT"] = so["Order_Qty_MT"]

    for idx, row in so.iterrows():
        sku_id = str(row["SKU_ID"]).strip()
        required = float(row["Order_Qty_MT"] or 0.0)
        available = float(inv_map.get(sku_id, 0.0) or 0.0)
        covered = min(required, available)
        so.at[idx, "FG_Stock_Covered_MT"] = round(covered, 3)
        so.at[idx, "Make_Qty_MT"] = round(required - covered, 3)
        if covered > 0:
            inv_map[sku_id] = round(available - covered, 6)
    return so, inv_map


def _campaign_sort_key(campaign: dict):
    return (
        int(campaign.get("priority_rank", 9)),
        pd.to_datetime(campaign.get("due_date")),
        int(campaign.get("grade_order", 9)),
        str(campaign.get("campaign_group", "")),
        str(campaign.get("campaign_id", "")),
    )


def _ordered_production_orders(campaign: dict) -> list:
    return sorted(
        campaign.get("production_orders", []),
        key=lambda line: (
            int(line.get("priority_rank", 9)),
            pd.to_datetime(line.get("due_date")),
            pd.to_numeric(pd.Series([line.get("section_mm")]), errors="coerce").fillna(999).iloc[0],
            str(line.get("production_order_id", "")),
        ),
    )


def _finalize_campaign(
    campaign_lines: list,
    campaign_num: int,
    min_campaign_mt: float,
    bom: pd.DataFrame | None = None,
    config: dict | None = None,
    yield_loss_pct: float = 0.0,
    batch_size_mt: float | None = None,
) -> dict:
    if batch_size_mt is None:
        batch_size_mt = _get_heat_size_mt()
    line_df = pd.DataFrame(campaign_lines)
    grade = str(line_df["grade"].iloc[0])
    sections = sorted(
        pd.to_numeric(line_df["section_mm"], errors="coerce").dropna().astype(float).unique().tolist()
    )
    campaign_id = f"CMP-{campaign_num:03d}"

    production_orders = []
    for line_idx, line in enumerate(campaign_lines, start=1):
        po = dict(line)
        po["campaign_id"] = campaign_id
        po["production_order_id"] = f"{campaign_id}-PO{line_idx:02d}"
        production_orders.append(po)

    total_coil_mt = round(float(line_df["qty_mt"].sum()), 1)
    due_date = pd.to_datetime(line_df["due_date"]).min()
    priority_rank_value = int(line_df["priority_rank"].min())
    primary_priority = (
        line_df.sort_values(["priority_rank", "due_date", "so_id"]).iloc[0]["priority"]
        if not line_df.empty
        else "NORMAL"
    )
    unique_so_ids = list(dict.fromkeys(line_df["so_id"].astype(str).tolist()))
    heats_info = _heats_needed_from_lines(
        production_orders,
        bom=bom,
        config=config,
        yield_loss_pct=yield_loss_pct,
        batch_size_mt=batch_size_mt,
        return_details=True,
    )

    return {
        "campaign_id": campaign_id,
        "campaign_group": str(line_df["campaign_group"].iloc[0]),
        "grade": grade,
        "section_mm": sections[0] if len(sections) == 1 else "MIX",
        "sections_covered": ", ".join(f"{section:g}" for section in sections) if sections else "",
        "needs_vd": bool(line_df["needs_vd"].iloc[0]),
        "billet_family": str(line_df["billet_family"].iloc[0]),
        "total_coil_mt": total_coil_mt,
        "heats": int(heats_info["heats"]),
        "so_ids": unique_so_ids,
        "order_count": len(unique_so_ids),
        "due_date": due_date,
        "priority": primary_priority,
        "priority_rank": priority_rank_value,
        "grade_order": GRADE_ORDER.get(grade, 9),
        "production_orders": production_orders,
        "release_status": "UNREVIEWED",
        "material_status": "UNREVIEWED",
        "material_shortages": {},
        "material_issue": "",
        "material_consumed": {},
        "material_gross_requirements": {},
        "material_structure_errors": [],
        "inventory_before": {},
        "inventory_after": {},
        "yield_loss_pct": float(yield_loss_pct or 0.0),
        "below_min_campaign": total_coil_mt < float(min_campaign_mt or 0.0),
        "heats_calc_method": (
            "LEGACY_DIAGNOSTIC_ONLY"
            if heats_info["used_legacy_estimate"]
            else "LEGACY_BLOCKED"
            if heats_info.get("blocked_legacy_estimate")
            else "BOM_TRACE"
        ),
        "heats_trace_valid": bool(heats_info.get("heats_trace_valid", True)),
        "heats_calc_warnings": heats_info["warnings"],
        "heats_calc_errors": heats_info.get("errors", []),
        "manual_campaign_id": str(line_df["manual_campaign_id"].iloc[0]).strip() if "manual_campaign_id" in line_df.columns else "",
        "manual_campaign_split": False,
        "manual_campaign_grouping_mode": _manual_campaign_grouping_mode(config),
        "manual_campaign_over_max": False,
        # Generic SKU attributes for data-driven optional-operation support
        "sku_attributes": {
            k: v for k, v in campaign_lines[0].items()
            if k not in {
                "so_id", "sku_id", "grade", "section_mm", "qty_mt",
                "due_date", "priority", "priority_rank", "campaign_group",
                "needs_vd", "billet_family", "campaign_id", "production_order_id",
                "manual_campaign_id",
            }
        } if campaign_lines else {},
    }


def _renumber_campaigns(campaigns: list) -> list:
    renumbered = []
    for campaign_num, campaign in enumerate(campaigns, start=1):
        new_campaign = dict(campaign)
        new_campaign_id = f"CMP-{campaign_num:03d}"
        new_campaign["campaign_id"] = new_campaign_id
        new_campaign["release_seq"] = campaign_num
        new_orders = []
        for line_idx, order in enumerate(_ordered_production_orders(new_campaign), start=1):
            po = dict(order)
            po["campaign_id"] = new_campaign_id
            po["production_order_id"] = f"{new_campaign_id}-PO{line_idx:02d}"
            new_orders.append(po)
        new_campaign["production_orders"] = new_orders
        renumbered.append(new_campaign)
    return renumbered


def build_campaigns(
    sales_orders: pd.DataFrame,
    min_campaign_mt: float = 100.0,
    max_campaign_mt: float = 500.0,
    inventory: pd.DataFrame | dict | None = None,
    bom: pd.DataFrame | None = None,
    config: dict | None = None,
    skus: pd.DataFrame | None = None,
    yield_loss_pct: float = 0.0,
) -> list:
    """
    Build releasable APS campaigns from prioritized sales orders.

    Returns campaign dicts with:
      - production_orders: one entry per SO slice that still needs to be made
      - release_status / material_status
      - material_shortages when a campaign is held
    """
    open_so = _normalize_sales_orders(sales_orders, skus=skus, config=config)
    if open_so.empty:
        return []

    inv_map = inventory_map(inventory)
    open_so, release_inventory = _consume_finished_goods_stock(open_so, inv_map)
    make_so = open_so[open_so["Make_Qty_MT"] > 1e-6].copy()
    if make_so.empty:
        return []

    # Use Algorithm_Config for batch size, allow legacy config dict override
    default_batch_size = _get_heat_size_mt()
    batch_size_mt = float((config or {}).get("Default_Batch_Size_MT") or default_batch_size)
    max_campaign_mt = max(float(max_campaign_mt or 0.0), 1.0)
    min_campaign_mt = max(float(min_campaign_mt or 0.0), 0.0)

    campaigns = []
    campaign_num = 1

    # ── Manual campaign assignments ──────────────────────────────────────────
    # SOs with a filled Campaign_ID column are directly assigned to that campaign,
    # bypassing auto-grouping. This lets planners manually bundle SOs together.
    manual_campaign_so = pd.DataFrame()
    auto_group_so = make_so.copy()
    if "Campaign_ID" in make_so.columns:
        has_cid = (
            make_so["Campaign_ID"].notna()
            & make_so["Campaign_ID"].astype(str).str.strip().ne("")
        )
        manual_campaign_so = make_so[has_cid].copy()
        auto_group_so = make_so[~has_cid].copy()

    if not manual_campaign_so.empty:
        manual_grouping_mode = _manual_campaign_grouping_mode(config)
        for cid_val, cid_group in manual_campaign_so.groupby("Campaign_ID", sort=False):
            cid_group = cid_group.sort_values(
                ["Priority_Rank", "Delivery_Date", "Order_Date", "Section_mm", "SO_ID"]
            ).reset_index(drop=True)
            if manual_grouping_mode == "PRESERVE_EXACT":
                manual_lines: list[dict] = []
                for _, order in cid_group.iterrows():
                    qty_mt = round(float(order["Make_Qty_MT"] or 0.0), 3)
                    if qty_mt <= 1e-6:
                        continue
                    manual_lines.append(
                        {
                            "so_id": str(order["SO_ID"]),
                            "sku_id": str(order["SKU_ID"]),
                            "grade": str(order["Grade"]),
                            "section_mm": float(order["Section_mm"]),
                            "qty_mt": qty_mt,
                            "due_date": pd.to_datetime(order["Delivery_Date"]),
                            "priority": str(order["Priority"]),
                            "priority_rank": int(order["Priority_Rank"]),
                            "campaign_group": str(cid_val),
                            "needs_vd": bool(order["Needs_VD"]),
                            "billet_family": str(order.get("Product_Family", order["Billet_Family"])),
                            "manual_campaign_id": str(cid_val).strip(),
                        }
                    )
                if manual_lines:
                    campaigns.append(
                        _finalize_campaign(
                            manual_lines,
                            campaign_num,
                            min_campaign_mt,
                            bom=bom,
                            config=config,
                            yield_loss_pct=yield_loss_pct,
                            batch_size_mt=batch_size_mt,
                        )
                    )
                    campaigns[-1]["manual_campaign_id"] = str(cid_val).strip()
                    campaigns[-1]["manual_campaign_grouping_mode"] = manual_grouping_mode
                    campaigns[-1]["manual_campaign_over_max"] = (
                        float(campaigns[-1].get("total_coil_mt", 0.0) or 0.0) > max_campaign_mt + 1e-6
                    )
                    campaign_num += 1
                continue

            current_lines: list = []
            current_total: float = 0.0
            manual_campaign_indexes: list[int] = []

            def _flush_manual(c_lines=None):
                nonlocal campaign_num, current_lines, current_total, manual_campaign_indexes
                lines = c_lines if c_lines is not None else current_lines
                if not lines:
                    return
                campaigns.append(
                    _finalize_campaign(
                        lines,
                        campaign_num,
                        min_campaign_mt,
                        bom=bom,
                        config=config,
                        yield_loss_pct=yield_loss_pct,
                        batch_size_mt=batch_size_mt,
                    )
                )
                campaigns[-1]["manual_campaign_id"] = str(cid_val).strip()
                campaigns[-1]["manual_campaign_grouping_mode"] = manual_grouping_mode
                manual_campaign_indexes.append(len(campaigns) - 1)
                campaign_num += 1
                current_lines = []
                current_total = 0.0

            for _, order in cid_group.iterrows():
                remaining_qty = float(order["Make_Qty_MT"] or 0.0)
                while remaining_qty > 1e-6:
                    available_slot = max_campaign_mt - current_total
                    if available_slot <= 1e-6:
                        _flush_manual()
                        available_slot = max_campaign_mt
                    alloc_qty = min(remaining_qty, available_slot)
                    current_lines.append({
                        "so_id": str(order["SO_ID"]),
                        "sku_id": str(order["SKU_ID"]),
                        "grade": str(order["Grade"]),
                        "section_mm": float(order["Section_mm"]),
                        "qty_mt": round(alloc_qty, 3),
                        "due_date": pd.to_datetime(order["Delivery_Date"]),
                        "priority": str(order["Priority"]),
                        "priority_rank": int(order["Priority_Rank"]),
                        "campaign_group": str(cid_val),
                        "needs_vd": bool(order["Needs_VD"]),
                        "billet_family": str(order.get("Product_Family", order["Billet_Family"])),
                        "manual_campaign_id": str(cid_val).strip(),
                    })
                    current_total += alloc_qty
                    remaining_qty = round(remaining_qty - alloc_qty, 6)
                    if current_total >= max_campaign_mt - 1e-6:
                        _flush_manual()
            _flush_manual()
            if len(manual_campaign_indexes) > 1:
                for idx in manual_campaign_indexes:
                    campaigns[idx]["manual_campaign_split"] = True

    make_so = auto_group_so

    group_by_str = str(
        (config or {}).get(
            "Campaign_Group_By",
            "Route_Family,Campaign_Group,Grade,Product_Family,Route_Variant",
        )
        or ""
    )
    group_keys = [key.strip() for key in group_by_str.split(",") if key.strip() in make_so.columns]
    if not group_keys:
        group_keys = [
            key
            for key in ["Route_Family", "Campaign_Group", "Grade", "Billet_Family", "Needs_VD"]
            if key in make_so.columns
        ]
    grouped = make_so.groupby(group_keys, sort=False)

    for _, group in grouped:
        group = group.sort_values(
            ["Priority_Rank", "Delivery_Date", "Order_Date", "Section_mm", "SO_ID"]
        ).reset_index(drop=True)

        current_lines = []
        current_total = 0.0

        def flush_campaign():
            nonlocal campaign_num, current_lines, current_total
            if not current_lines:
                return
            campaigns.append(
                _finalize_campaign(
                    current_lines,
                    campaign_num,
                    min_campaign_mt,
                    bom=bom,
                    config=config,
                    yield_loss_pct=yield_loss_pct,
                    batch_size_mt=batch_size_mt,
                )
            )
            campaign_num += 1
            current_lines = []
            current_total = 0.0

        for _, order in group.iterrows():
            remaining_qty = float(order["Make_Qty_MT"] or 0.0)
            while remaining_qty > 1e-6:
                available_slot = max_campaign_mt - current_total
                if available_slot <= 1e-6:
                    flush_campaign()
                    available_slot = max_campaign_mt

                alloc_qty = min(remaining_qty, available_slot)
                current_lines.append(
                    {
                        "so_id": str(order["SO_ID"]),
                        "sku_id": str(order["SKU_ID"]),
                        "grade": str(order["Grade"]),
                        "section_mm": float(order["Section_mm"]),
                        "qty_mt": round(alloc_qty, 3),
                        "due_date": pd.to_datetime(order["Delivery_Date"]),
                        "priority": str(order["Priority"]),
                        "priority_rank": int(order["Priority_Rank"]),
                        "campaign_group": str(order["Campaign_Group"]),
                        "needs_vd": bool(order["Needs_VD"]),
                        "billet_family": str(order.get("Product_Family", order["Billet_Family"])),
                    }
                )
                current_total += alloc_qty
                remaining_qty = round(remaining_qty - alloc_qty, 6)

                if current_total >= max_campaign_mt - 1e-6:
                    flush_campaign()

        flush_campaign()

    campaigns = sorted(campaigns, key=_campaign_sort_key)

    # Check for missing BOM - never auto-release, always hold with status
    if bom is None or getattr(bom, "empty", True):
        for camp in campaigns:
            # According to rule 4.1: missing BOM blocks release
            camp["release_status"] = "MATERIAL HOLD"
            camp["material_status"] = "MASTER_DATA_MISSING"
            camp["material_shortages"] = {}
            camp["material_consumed"] = {}
            camp["material_gross_requirements"] = {}
            camp["material_structure_errors"] = [{"type": "MISSING_BOM", "message": "BOM master data is not configured"}]
            camp["inventory_before"] = {}
            camp["inventory_after"] = {}
            camp["material_issue"] = "BOM missing for material simulation"
        return _renumber_campaigns(campaigns)

    committed_inventory = dict(release_inventory)
    for camp in campaigns:
        inventory_before = dict(committed_inventory)
        heats_calc_errors = camp.get("heats_calc_errors", []) or []
        if not camp.get("heats_trace_valid", True):
            camp["release_status"] = "MATERIAL HOLD"
            camp["material_status"] = "BOM ERROR"
            camp["material_shortages"] = {}
            camp["material_consumed"] = {}
            camp["material_gross_requirements"] = {}
            camp["material_structure_errors"] = heats_calc_errors
            camp["inventory_before"] = inventory_before
            camp["inventory_after"] = dict(committed_inventory)
            issue_parts = [
                f"{err.get('type')}: {err.get('reason') or err.get('path')}"
                for err in heats_calc_errors
            ]
            camp["material_issue"] = ", ".join(issue_parts) or "PRIMARY_BATCH_TRACE_ERROR"
            continue

        demand_rows = pd.DataFrame(
            {
                "SKU_ID": [line["sku_id"] for line in camp["production_orders"]],
                "Required_Qty": [line["qty_mt"] for line in camp["production_orders"]],
            }
        )
        material_check = simulate_material_commit(
            demand_rows,
            bom,
            committed_inventory,
            on_structure_error=_bom_structure_error_mode(config),
            byproduct_inventory_mode=str(
                (config or {}).get("Byproduct_Inventory_Mode", "DEFERRED") or "DEFERRED"
            ).strip().lower(),
        )
        shortages = material_check["shortages"]
        structure_errors = material_check.get("structure_errors", []) or []
        if shortages or structure_errors:
            camp["release_status"] = "MATERIAL HOLD"
            camp["material_status"] = "BOM ERROR" if structure_errors else "SHORTAGE"
            camp["material_shortages"] = shortages
            camp["material_consumed"] = material_check["consumed"]
            camp["material_gross_requirements"] = material_check["gross_requirements"]
            camp["material_structure_errors"] = structure_errors
            camp["inventory_before"] = inventory_before
            camp["inventory_after"] = dict(committed_inventory)
            issue_parts = [f"{sku}: {qty:g}" for sku, qty in shortages.items()]
            issue_parts.extend(
                f"{err.get('type')}: {err.get('path')}" for err in structure_errors
            )
            camp["material_issue"] = ", ".join(issue_parts)
            continue

        committed_inventory = material_check["inventory_after"]
        camp["release_status"] = "RELEASED"
        camp["material_status"] = "READY"
        camp["material_shortages"] = {}
        camp["material_issue"] = ""
        camp["material_consumed"] = material_check["consumed"]
        camp["material_gross_requirements"] = material_check["gross_requirements"]
        camp["material_structure_errors"] = []
        camp["inventory_before"] = inventory_before
        camp["inventory_after"] = dict(committed_inventory)

    return _renumber_campaigns(campaigns)


def print_campaign_summary(campaigns: list):
    print(
        f"\n{'Campaign':<12} {'Grade':<14} {'MT':>8} {'Heats':>6} "
        f"{'Priority':<8} {'Release':<14} SOs"
    )
    print("-" * 96)
    for c in campaigns:
        print(
            f"{c['campaign_id']:<12} {c['grade']:<14} {c['total_coil_mt']:>8.1f} "
            f"{c['heats']:>6} {str(c.get('priority', '')):<8} "
            f"{str(c.get('release_status', '')):<14} {', '.join(c['so_ids'][:4])}"
            f"{'...' if len(c['so_ids']) > 4 else ''}"
        )
    print(f"\nTotal campaigns: {len(campaigns)}")
    print(f"Released campaigns: {sum(c.get('release_status') == 'RELEASED' for c in campaigns)}")
    print(f"Held campaigns: {sum(c.get('release_status') == 'MATERIAL HOLD' for c in campaigns)}")
    print(f"Total heats: {sum(c['heats'] for c in campaigns)}")
    print(f"Total coil MT: {sum(c['total_coil_mt'] for c in campaigns):.1f}")
