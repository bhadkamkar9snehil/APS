"""
Layer A — BOM Explosion + Inventory Netting
Multi-level BOM: explode demand → net against inventory → gross requirement
"""
from collections import defaultdict

import pandas as pd


INPUT_FLOW_TYPES = {"", "INPUT", "CONSUME", "CONSUMED", "REQUIRED"}
BYPRODUCT_FLOW_TYPES = {"BYPRODUCT", "OUTPUT", "CO_PRODUCT", "COPRODUCT", "WASTE"}
OFFICIAL_BOM_EXPLOSION_API = "explode_bom_details"
PRODUCTION_BYPRODUCT_INVENTORY_MODE = "deferred"


def _normalize_sku_id(value) -> str:
    if value is None or pd.isna(value):
        return ""
    return str(value).strip()


def _flow_type_series(bom: pd.DataFrame) -> pd.Series:
    if bom is None or getattr(bom, "empty", True):
        return pd.Series(dtype=object)
    if "Flow_Type" not in bom.columns:
        return pd.Series(["INPUT"] * len(bom), index=bom.index, dtype=object)
    return bom["Flow_Type"].fillna("INPUT").astype(str).str.strip().str.upper()


def _bom_rows_for_flow_types(bom: pd.DataFrame, flow_types: set[str]) -> pd.DataFrame:
    if bom is None or getattr(bom, "empty", True):
        return pd.DataFrame()
    flow = _flow_type_series(bom)
    rows = bom[flow.isin(flow_types)].copy()
    for col in ["Parent_SKU", "Child_SKU", "Qty_Per"]:
        if col not in rows.columns:
            raise ValueError(f"BOM is missing required column: {col}")
    if "Yield_Pct" not in rows.columns:
        rows["Yield_Pct"] = pd.NA
    if "Scrap_%" not in rows.columns:
        rows["Scrap_%"] = pd.NA
    if "Level" not in rows.columns:
        rows["Level"] = pd.NA
    rows["Flow_Type"] = flow.loc[rows.index].astype(str).str.strip().str.upper()
    rows["Parent_SKU"] = rows["Parent_SKU"].map(_normalize_sku_id)
    rows["Child_SKU"] = rows["Child_SKU"].map(_normalize_sku_id)
    rows["Qty_Per"] = pd.to_numeric(rows["Qty_Per"], errors="coerce").fillna(0.0)
    rows["Level"] = pd.to_numeric(rows["Level"], errors="coerce")
    rows = rows[(rows["Parent_SKU"] != "") & (rows["Child_SKU"] != "")].copy()
    return rows


def _input_bom_rows(bom: pd.DataFrame) -> pd.DataFrame:
    return _bom_rows_for_flow_types(bom, INPUT_FLOW_TYPES)


def _byproduct_bom_rows(bom: pd.DataFrame) -> pd.DataFrame:
    return _bom_rows_for_flow_types(bom, BYPRODUCT_FLOW_TYPES)


def _effective_yield(bom_row) -> float:
    """Return decimal yield fraction [0.01, 1.0], preferring Yield_Pct over Scrap_%."""
    yp = pd.to_numeric(bom_row.get("Yield_Pct"), errors="coerce")
    if pd.notna(yp):
        return max(0.01, min(1.0, float(yp) / 100.0))
    scrap = pd.to_numeric(bom_row.get("Scrap_%"), errors="coerce")
    if pd.notna(scrap):
        return max(0.01, min(1.0, 1.0 - (float(scrap) / 100.0)))
    return 1.0


def _format_cycle_path(path: tuple[str, ...], next_sku: str) -> str:
    cycle_start = 0
    if next_sku in path:
        cycle_start = path.index(next_sku)
    cycle = list(path[cycle_start:]) + [next_sku]
    return " -> ".join(cycle)


def _coerce_float(value, default: float = 0.0) -> float:
    numeric = pd.to_numeric(value, errors="coerce")
    if pd.isna(numeric):
        return float(default)
    return float(numeric)


def _normalize_structure_error_mode(on_structure_error: str = "raise") -> str:
    mode = str(on_structure_error or "raise").strip().lower()
    if mode not in {"raise", "record"}:
        raise ValueError("on_structure_error must be 'raise' or 'record'.")
    return mode


def _normalize_byproduct_inventory_mode(
    byproduct_inventory_mode: str = PRODUCTION_BYPRODUCT_INVENTORY_MODE,
) -> str:
    mode = str(byproduct_inventory_mode or PRODUCTION_BYPRODUCT_INVENTORY_MODE).strip().lower()
    aliases = {
        "immediate": "immediate",
        "available_immediately": "immediate",
        "deferred": "deferred",
        "hold": "deferred",
        "delayed": "deferred",
    }
    normalized = aliases.get(mode)
    if normalized is None:
        raise ValueError("byproduct_inventory_mode must be 'IMMEDIATE' or 'DEFERRED'.")
    return normalized


def _handle_structure_error(
    *,
    structure_errors: list[dict],
    seen_structure_errors: set[tuple],
    mode: str,
    error_type: str,
    sku: str,
    qty: float,
    path: tuple[str, ...],
    max_levels: int,
) -> None:
    key = (error_type, sku, tuple(path))
    if key in seen_structure_errors:
        return
    seen_structure_errors.add(key)
    error_payload = {
        "type": error_type,
        "sku_id": sku,
        "required_qty": round(float(qty or 0.0), 3),
        "path": " -> ".join(path),
    }
    if mode == "raise":
        if error_type == "BOM_CYCLE":
            raise ValueError(f"BOM cycle detected: {error_payload['path']}")
        raise ValueError(f"BOM explosion exceeded max_levels={max_levels} while expanding {sku}.")
    structure_errors.append(error_payload)


def explode_bom_details(
    demand: pd.DataFrame,
    bom: pd.DataFrame,
    max_levels: int = 10,
    *,
    on_structure_error: str = "raise",
) -> dict:
    """
    Explode finished good demand through multi-level BOM.

    Uses the same `on_structure_error` contract as `simulate_material_commit`.

    Returns a dict with:
    - exploded: DataFrame with [SKU_ID, BOM_Level, Required_Qty, Parent_SKU, Flow_Type]
    - structure_errors: list of BOM_CYCLE / MAX_LEVEL_EXCEEDED errors
    - feasible: bool
    """
    normalized_error_mode = _normalize_structure_error_mode(on_structure_error)
    empty_result = pd.DataFrame(
        columns=["SKU_ID", "Required_Qty", "Produced_Qty", "BOM_Level", "Parent_SKU", "Flow_Type"]
    )
    if demand is None or demand.empty:
        return {
            "exploded": empty_result,
            "structure_errors": [],
            "feasible": True,
        }
    if not {"SKU_ID", "Required_Qty"}.issubset(demand.columns):
        raise ValueError("Demand must include SKU_ID and Required_Qty columns.")

    bom_input = _input_bom_rows(bom)
    bom_byproducts = _byproduct_bom_rows(bom)
    if bom_input.empty and bom_byproducts.empty:
        return {
            "exploded": empty_result,
            "structure_errors": [],
            "feasible": True,
        }

    children_map: dict[str, list[dict]] = {}
    for _, row in bom_input.iterrows():
        children_map.setdefault(_normalize_sku_id(row["Parent_SKU"]), []).append(
            {
                "Child_SKU": _normalize_sku_id(row["Child_SKU"]),
                "Qty_Per": float(row["Qty_Per"] or 0.0),
                "Yield_Factor": float(_effective_yield(row)),
            }
        )
    byproduct_map: dict[str, list[dict]] = {}
    for _, row in bom_byproducts.iterrows():
        byproduct_map.setdefault(_normalize_sku_id(row["Parent_SKU"]), []).append(
            {
                "Child_SKU": _normalize_sku_id(row["Child_SKU"]),
                "Qty_Per": abs(float(row["Qty_Per"] or 0.0)),
            }
        )

    all_requirements = []
    structure_errors: list[dict] = []
    seen_structure_errors: set[tuple] = set()
    queue = [
        {
            "SKU_ID": _normalize_sku_id(row["SKU_ID"]),
            "Demand_Qty": _coerce_float(row["Required_Qty"]),
            "Level": 0,
            "Path": (_normalize_sku_id(row["SKU_ID"]),),
        }
        for _, row in demand.iterrows()
        if _normalize_sku_id(row.get("SKU_ID"))
    ]

    while queue:
        current = queue.pop(0)
        sku = current["SKU_ID"]
        qty = float(current["Demand_Qty"] or 0.0)
        level = int(current["Level"] or 0)
        path = tuple(current["Path"])
        if qty <= 1e-9:
            continue
        if level >= max_levels:
            _handle_structure_error(
                structure_errors=structure_errors,
                seen_structure_errors=seen_structure_errors,
                mode=normalized_error_mode,
                error_type="MAX_LEVEL_EXCEEDED",
                sku=sku,
                qty=qty,
                path=path,
                max_levels=max_levels,
            )
            continue

        for byproduct in byproduct_map.get(sku, []):
            byproduct_sku = _normalize_sku_id(byproduct.get("Child_SKU"))
            produced_qty = round(qty * abs(float(byproduct.get("Qty_Per", 0.0) or 0.0)), 3)
            if not byproduct_sku or produced_qty <= 1e-9:
                continue
            all_requirements.append(
                {
                    "SKU_ID": byproduct_sku,
                    "Required_Qty": 0.0,
                    "Produced_Qty": produced_qty,
                    "BOM_Level": level + 1,
                    "Parent_SKU": sku,
                    "Flow_Type": "BYPRODUCT",
                }
            )

        for child in children_map.get(sku, []):
            child_sku = _normalize_sku_id(child.get("Child_SKU", ""))
            if not child_sku:
                continue
            if child_sku in path:
                _handle_structure_error(
                    structure_errors=structure_errors,
                    seen_structure_errors=seen_structure_errors,
                    mode=normalized_error_mode,
                    error_type="BOM_CYCLE",
                    sku=child_sku,
                    qty=qty,
                    path=path + (child_sku,),
                    max_levels=max_levels,
                )
                continue
            yield_factor = max(float(child.get("Yield_Factor", 1.0) or 1.0), 0.01)
            child_qty = round(qty * float(child.get("Qty_Per", 0.0) or 0.0) / yield_factor, 3)
            if child_qty <= 1e-9:
                continue
            next_level = level + 1
            all_requirements.append(
                {
                    "SKU_ID": child_sku,
                    "Required_Qty": child_qty,
                    "Produced_Qty": 0.0,
                    "BOM_Level": next_level,
                    "Parent_SKU": sku,
                    "Flow_Type": "INPUT",
                }
            )
            queue.append(
                {
                    "SKU_ID": child_sku,
                    "Demand_Qty": child_qty,
                    "Level": next_level,
                    "Path": path + (child_sku,),
                }
            )

    if not all_requirements:
        return {
            "exploded": empty_result,
            "structure_errors": structure_errors,
            "feasible": not structure_errors,
        }

    result = pd.DataFrame(all_requirements)
    result = result.groupby(
        ["SKU_ID", "BOM_Level", "Parent_SKU", "Flow_Type"],
        as_index=False,
    )[["Required_Qty", "Produced_Qty"]].sum()
    result["_flow_sort"] = result["Flow_Type"].map(lambda flow: 0 if str(flow).upper() in BYPRODUCT_FLOW_TYPES else 1)
    result = result.sort_values(["BOM_Level", "_flow_sort", "Parent_SKU", "SKU_ID"]).reset_index(drop=True)
    return {
        "exploded": result.drop(columns=["_flow_sort"]),
        "structure_errors": structure_errors,
        "feasible": not structure_errors,
    }


def explode_bom(
    demand: pd.DataFrame,
    bom: pd.DataFrame,
    max_levels: int = 10,
    *,
    on_structure_error: str = "raise",
) -> pd.DataFrame:
    """
    Explode finished good demand through multi-level BOM.

    Reporting wrapper around the official detail API: `explode_bom_details`.

    The returned DataFrame carries BOM structure diagnostics in `attrs`:
    - structure_errors
    - feasible
    - on_structure_error
    - official_api
    """
    details = explode_bom_details(
        demand,
        bom,
        max_levels=max_levels,
        on_structure_error=on_structure_error,
    )
    exploded = details["exploded"]
    exploded.attrs["structure_errors"] = details["structure_errors"]
    exploded.attrs["feasible"] = details["feasible"]
    exploded.attrs["on_structure_error"] = _normalize_structure_error_mode(on_structure_error)
    exploded.attrs["official_api"] = OFFICIAL_BOM_EXPLOSION_API
    return exploded


def net_requirements(
    gross: pd.DataFrame,
    inventory: pd.DataFrame,
    *,
    byproduct_inventory_mode: str = PRODUCTION_BYPRODUCT_INVENTORY_MODE,
) -> pd.DataFrame:
    """
    Net gross requirements against available inventory.
    Returns: DataFrame with [SKU_ID, BOM_Level, Gross_Req, Available, Net_Req]
    """
    if gross is None or gross.empty:
        base_cols = ["SKU_ID", "BOM_Level", "Gross_Req", "Produced_Qty", "Available", "Net_Req"]
        if gross is not None and "Parent_SKU" in gross.columns:
            base_cols.insert(2, "Parent_SKU")
        if gross is not None and "Flow_Type" in gross.columns:
            base_cols.insert(len(base_cols) - 3, "Flow_Type")
        return pd.DataFrame(columns=base_cols)
    if "Required_Qty" not in gross.columns or "SKU_ID" not in gross.columns:
        raise ValueError("Gross requirements must include SKU_ID and Required_Qty columns.")

    normalized_byproduct_mode = _normalize_byproduct_inventory_mode(byproduct_inventory_mode)
    remaining_inventory = inventory_map(inventory)
    netted = gross.copy()
    if "Flow_Type" not in netted.columns:
        netted["Flow_Type"] = "INPUT"
    netted["Flow_Type"] = netted["Flow_Type"].fillna("INPUT").astype(str).str.strip().str.upper()
    if "Produced_Qty" not in netted.columns:
        netted["Produced_Qty"] = 0.0
    netted["Produced_Qty"] = pd.to_numeric(netted["Produced_Qty"], errors="coerce").fillna(0.0)
    sort_cols = [col for col in ["BOM_Level", "Parent_SKU", "SKU_ID"] if col in netted.columns]
    netted["_flow_sort"] = netted["Flow_Type"].map(lambda flow: 0 if flow in BYPRODUCT_FLOW_TYPES else 1)
    netted = netted.sort_values([col for col in ["BOM_Level", "_flow_sort", "Parent_SKU", "SKU_ID"] if col in netted.columns], kind="stable").reset_index(drop=True)

    available_before = []
    net_reqs = []
    for _, row in netted.iterrows():
        sku = _normalize_sku_id(row.get("SKU_ID", ""))
        gross_qty = _coerce_float(row.get("Required_Qty"))
        produced_qty = _coerce_float(row.get("Produced_Qty"))
        available_qty = float(remaining_inventory.get(sku, 0.0) or 0.0)
        available_before.append(round(available_qty, 3))
        if str(row.get("Flow_Type", "INPUT")).upper() in BYPRODUCT_FLOW_TYPES:
            if normalized_byproduct_mode == "immediate":
                remaining_inventory[sku] = round(available_qty + produced_qty, 6)
            net_reqs.append(0.0)
        else:
            covered_qty = min(available_qty, gross_qty)
            remaining_inventory[sku] = round(available_qty - covered_qty, 6)
            net_reqs.append(round(max(gross_qty - covered_qty, 0.0), 3))

    netted["Available"] = available_before
    netted["Net_Req"] = net_reqs
    netted = netted.rename(columns={"Required_Qty": "Gross_Req"}).drop(columns=["_flow_sort"])
    netted.attrs["byproduct_inventory_mode"] = normalized_byproduct_mode
    return netted


def consolidate_demand(sales_orders: pd.DataFrame) -> pd.DataFrame:
    """Consolidate open SOs by SKU — respecting earliest delivery date."""
    if sales_orders is None or getattr(sales_orders, "empty", True):
        return pd.DataFrame(columns=["SKU_ID", "Total_Qty", "Earliest_Delivery", "Order_Count"])
    so = sales_orders.copy()
    status = so["Status"].fillna("Open") if "Status" in so.columns else pd.Series("Open", index=so.index)
    status = status.astype(str).str.strip().str.upper()
    open_so = so[status == "OPEN"].copy()
    if open_so.empty:
        return pd.DataFrame(columns=["SKU_ID", "Total_Qty", "Earliest_Delivery", "Order_Count"])
    open_so["SKU_ID"] = open_so["SKU_ID"].map(_normalize_sku_id)
    open_so = open_so[open_so["SKU_ID"] != ""].copy()
    if open_so.empty:
        return pd.DataFrame(columns=["SKU_ID", "Total_Qty", "Earliest_Delivery", "Order_Count"])
    if "Order_Qty_MT" not in open_so.columns and "Order_Qty" not in open_so.columns:
        raise ValueError("Sales orders must include Order_Qty_MT or Order_Qty.")
    qty_col = "Order_Qty_MT" if "Order_Qty_MT" in open_so.columns else "Order_Qty"
    consolidated = open_so.groupby("SKU_ID").agg(
        Total_Qty=(qty_col, "sum"),
        Earliest_Delivery=("Delivery_Date", "min"),
        Order_Count=("SO_ID", "count")
    ).reset_index()
    return consolidated


def inventory_map(inventory: pd.DataFrame | dict | None) -> dict:
    """Return a mutable SKU -> available quantity map."""
    if inventory is None:
        return {}
    if isinstance(inventory, dict):
        result = defaultdict(float)
        for sku_id, qty in inventory.items():
            sku = _normalize_sku_id(sku_id)
            if not sku:
                continue
            result[sku] += float(qty or 0.0)
        return {sku: round(qty, 6) for sku, qty in result.items()}
    if inventory.empty:
        return {}

    qty_col = "Available_Qty" if "Available_Qty" in inventory.columns else "Available"
    inv = inventory[["SKU_ID", qty_col]].copy()
    inv["SKU_ID"] = inv["SKU_ID"].map(_normalize_sku_id)
    inv = inv[inv["SKU_ID"] != ""].copy()
    inv[qty_col] = pd.to_numeric(inv[qty_col], errors="coerce").fillna(0.0)
    return inv.groupby("SKU_ID", as_index=False)[qty_col].sum().set_index("SKU_ID")[qty_col].to_dict()


def simulate_material_commit(
    demand: pd.DataFrame,
    bom: pd.DataFrame,
    inventory: pd.DataFrame | dict | None,
    max_levels: int = 10,
    *,
    on_structure_error: str = "raise",
    byproduct_inventory_mode: str = PRODUCTION_BYPRODUCT_INVENTORY_MODE,
) -> dict:
    """
    Dry-run or commit-style material pegging for one demand set.

    Inventory is consumed top-down: existing stock of the requested SKU is used
    before exploding the remaining requirement into its BOM children.
    Byproducts are always tracked in `byproducts_produced`; whether they become
    immediately usable inventory depends on `byproduct_inventory_mode`.
    The returned `inventory_after` map can be committed by the caller only when
    there are no shortages.
    """
    inv_map = inventory_map(inventory)
    normalized_byproduct_mode = _normalize_byproduct_inventory_mode(byproduct_inventory_mode)
    if demand is None or demand.empty:
        return {
            "inventory_after": inv_map,
            "consumed": {},
            "shortages": {},
            "gross_requirements": {},
            "byproducts_produced": {},
            "structure_errors": [],
            "feasible": True,
            "byproduct_inventory_mode": normalized_byproduct_mode,
        }

    bom_input = _input_bom_rows(bom)
    bom_byproducts = _byproduct_bom_rows(bom)
    bom_rows = (
        bom_input[["Parent_SKU", "Child_SKU", "Qty_Per", "Scrap_%", "Yield_Pct"]].copy()
        if not bom_input.empty
        else pd.DataFrame(columns=["Parent_SKU", "Child_SKU", "Qty_Per", "Scrap_%", "Yield_Pct"])
    )

    children_map = {}
    for _, row in bom_rows.iterrows():
        parent = str(row["Parent_SKU"]).strip()
        children_map.setdefault(parent, []).append(
            (
                str(row["Child_SKU"]).strip(),
                float(row["Qty_Per"]),
                float(_effective_yield(row)),
            )
        )

    byproduct_map = {}
    for _, row in bom_byproducts.iterrows():
        parent = _normalize_sku_id(row["Parent_SKU"])
        byproduct_map.setdefault(parent, []).append(
            (
                _normalize_sku_id(row["Child_SKU"]),
                abs(float(row["Qty_Per"])),
            )
        )

    gross_requirements = defaultdict(float)
    byproducts_produced = defaultdict(float)
    consumed = defaultdict(float)
    shortages = defaultdict(float)
    structure_errors: list[dict] = []
    seen_structure_errors: set[tuple] = set()

    normalized_error_mode = _normalize_structure_error_mode(on_structure_error)

    def record_structure_error(error_type: str, sku: str, qty: float, path: tuple[str, ...]):
        _handle_structure_error(
            structure_errors=structure_errors,
            seen_structure_errors=seen_structure_errors,
            mode=normalized_error_mode,
            error_type=error_type,
            sku=sku,
            qty=qty,
            path=path,
            max_levels=max_levels,
        )

    def allocate(sku_id: str, required_qty: float, level: int = 0, path: tuple[str, ...] = ()):
        qty = float(required_qty or 0.0)
        if qty <= 1e-9:
            return

        sku = _normalize_sku_id(sku_id)
        current_path = path + (sku,)
        gross_requirements[sku] += qty

        available = float(inv_map.get(sku, 0.0) or 0.0)
        used = min(available, qty)
        if used > 1e-9:
            consumed[sku] += used
            inv_map[sku] = round(available - used, 6)

        remaining = qty - used
        if remaining <= 1e-9:
            return

        for child_sku, qty_per in byproduct_map.get(sku, []):
            if not child_sku:
                continue
            produced_qty = remaining * abs(float(qty_per or 0.0))
            if produced_qty <= 1e-9:
                continue
            byproducts_produced[child_sku] += produced_qty
            if normalized_byproduct_mode == "immediate":
                inv_map[child_sku] = round(float(inv_map.get(child_sku, 0.0) or 0.0) + produced_qty, 6)

        children = children_map.get(sku, [])
        if not children:
            shortages[sku] += remaining
            return
        if level >= max_levels:
            record_structure_error("MAX_LEVEL_EXCEEDED", sku, remaining, current_path)
            return

        for child_sku, qty_per, yield_factor in children:
            if child_sku in current_path:
                record_structure_error("BOM_CYCLE", child_sku, remaining, current_path + (child_sku,))
                continue
            child_required = remaining * qty_per / max(float(yield_factor or 1.0), 0.01)
            allocate(child_sku, child_required, level + 1, current_path)

    demand_df = demand.copy()
    demand_df["Required_Qty"] = pd.to_numeric(demand_df["Required_Qty"], errors="coerce").fillna(0.0)
    for _, row in demand_df.iterrows():
        sku = _normalize_sku_id(row["SKU_ID"])
        if not sku:
            continue
        allocate(sku, float(row["Required_Qty"]), 0, ())

    feasible = not structure_errors and not shortages
    return {
        "inventory_after": inv_map,
        "consumed": {sku: round(qty, 3) for sku, qty in consumed.items() if qty > 1e-9},
        "shortages": {sku: round(qty, 3) for sku, qty in shortages.items() if qty > 1e-9},
        "gross_requirements": {
            sku: round(qty, 3) for sku, qty in gross_requirements.items() if qty > 1e-9
        },
        "byproducts_produced": {
            sku: round(qty, 3) for sku, qty in byproducts_produced.items() if qty > 1e-9
        },
        "structure_errors": structure_errors,
        "feasible": feasible,
        "byproduct_inventory_mode": normalized_byproduct_mode,
    }
