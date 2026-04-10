"""
APS Planner - Correct SO → PlanningOrder → HeatBatch → Schedule Model

Implements the layered planning architecture per APS Design Philosophy:
1. SalesOrder - demand source from customer
2. PlanningOrder - manufacturable lot (user can merge/split SOs)
3. HeatBatch - derived upstream production batch (SMS constraint)
4. ScheduledOperation - finite resource schedule (when/where it runs)

This replaces the broken campaign-first model with a correct SO-driven model.
"""

from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import List, Dict, Any, Optional, Tuple
from enum import Enum
import math

class PlanningHorizon(Enum):
    """Planning window options."""
    NEXT_3_DAYS = 3
    NEXT_7_DAYS = 7
    NEXT_10_DAYS = 10
    NEXT_14_DAYS = 14
    OVERDUE_PLUS_5_DAYS = "overdue+5"


@dataclass
class SalesOrder:
    """Demand source object from customer."""
    so_id: str
    customer_id: str
    grade: str
    section_mm: float
    qty_mt: float
    due_date: str  # ISO format
    priority: str  # URGENT, HIGH, NORMAL
    route_family: str  # SMS/RM, for example
    status: str  # Open, Covered, Completed
    order_date: Optional[str] = None
    order_type: str = "MTO"  # MTS | MTO — metadata only
    rolling_mode: str = "HOT"  # HOT | COLD

    def due_date_obj(self) -> datetime:
        """Parse due date to datetime."""
        try:
            return datetime.fromisoformat(self.due_date)
        except (ValueError, TypeError):
            return datetime(2099, 12, 31)

    def hours_until_due(self) -> float:
        """Hours remaining until due date."""
        return (self.due_date_obj() - datetime.now()).total_seconds() / 3600


@dataclass
class PlanningOrder:
    """Manufacturing lot - planner-friendly grouping of SOs."""
    po_id: str
    selected_so_ids: List[str]
    total_qty_mt: float
    grade_family: str
    size_family: str
    due_window: Tuple[str, str]  # (min_date, max_date)
    route_family: str
    heats_required: int
    planner_status: str  # PROPOSED, APPROVED, MERGED, SPLIT, FROZEN
    frozen_flag: bool = False
    created_at: datetime = field(default_factory=datetime.now)
    order_type: str = "MTO"  # MTS | MTO — metadata only
    rolling_mode: str = "HOT"  # HOT | COLD
    priority: str = "NORMAL"  # URGENT, HIGH, NORMAL - inherited from source SOs

    def to_dict(self) -> dict:
        return {
            'po_id': self.po_id,
            'selected_so_ids': self.selected_so_ids,
            'total_qty_mt': self.total_qty_mt,
            'grade_family': self.grade_family,
            'size_family': self.size_family,
            'due_window': self.due_window,
            'route_family': self.route_family,
            'heats_required': self.heats_required,
            'planner_status': self.planner_status,
            'frozen_flag': self.frozen_flag,
            'created_at': self.created_at.isoformat(),
            'order_type': self.order_type,
            'rolling_mode': self.rolling_mode,
            'priority': self.priority,
        }


@dataclass
class HeatBatch:
    """Derived upstream manufacturing batch from SMS."""
    heat_id: str
    planning_order_id: str
    grade: str
    qty_mt: float
    heat_number_seq: int  # 1st heat, 2nd heat, etc for this PO
    upstream_route: str  # SMS → RM path
    compatibility_class: str  # grade transitioning rules
    expected_duration_hours: float = 2.0  # SMS melt time

    def to_dict(self) -> dict:
        return {
            'heat_id': self.heat_id,
            'planning_order_id': self.planning_order_id,
            'grade': self.grade,
            'qty_mt': self.qty_mt,
            'heat_number_seq': self.heat_number_seq,
            'upstream_route': self.upstream_route,
            'compatibility_class': self.compatibility_class,
            'expected_duration_hours': self.expected_duration_hours,
        }


@dataclass
class ScheduledOperation:
    """Machine-level scheduled task."""
    operation_id: str
    parent_object_type: str  # heat, lot
    parent_object_id: str
    resource_id: str  # SMS-01, RM-01, etc
    operation_type: str  # SMS, RM, VD, CCM
    start_time: datetime
    end_time: datetime
    setup_before: float  # minutes
    lateness_cost: float  # penalty if late
    sequence_position: int

    def duration_hours(self) -> float:
        return (self.end_time - self.start_time).total_seconds() / 3600

    def to_dict(self) -> dict:
        return {
            'operation_id': self.operation_id,
            'parent_object_type': self.parent_object_type,
            'parent_object_id': self.parent_object_id,
            'resource_id': self.resource_id,
            'operation_type': self.operation_type,
            'start_time': self.start_time.isoformat(),
            'end_time': self.end_time.isoformat(),
            'setup_before': self.setup_before,
            'lateness_cost': self.lateness_cost,
            'sequence_position': self.sequence_position,
        }


class APSPlanner:
    """Main planning engine - SO-driven, lot formation, heat-aware, finite scheduling."""

    def __init__(self, config: Dict[str, Any]):
        self.config = config
        self.planning_orders: List[PlanningOrder] = []
        self.heat_batches: List[HeatBatch] = []

    # ===== STEP 1: SELECT PLANNING WINDOW =====

    def select_planning_window(
        self,
        all_sos: List[SalesOrder],
        window: PlanningHorizon = PlanningHorizon.NEXT_7_DAYS,
    ) -> List[SalesOrder]:
        """
        Select valid candidate SOs within the planning window.

        Rules:
        - only open / planned / confirmed work
        - valid SO_ID required
        - positive quantity required
        - valid due date required
        - sorted by due date, then priority, then qty desc
        """
        now = datetime.now()

        def _priority_rank(priority: str) -> int:
            p = str(priority or "").strip().upper()
            return {"URGENT": 0, "HIGH": 1, "NORMAL": 2, "LOW": 3}.get(p, 9)

        valid = []
        for so in all_sos:
            if not so:
                continue
            if not str(so.so_id or "").strip():
                continue
            if float(so.qty_mt or 0) <= 0:
                continue
            status = str(so.status or "").strip().upper()
            if status not in {"OPEN", "CONFIRMED", "PLANNED", ""}:
                continue
            due_dt = so.due_date_obj()
            if due_dt.year >= 2099:
                continue
            valid.append(so)

        if window == PlanningHorizon.OVERDUE_PLUS_5_DAYS:
            cutoff = now + timedelta(days=5)
            selected = [so for so in valid if so.due_date_obj() <= cutoff]
        else:
            days = int(window.value) if isinstance(window.value, int) else 7
            cutoff = now + timedelta(days=days)
            selected = [so for so in valid if so.due_date_obj() <= cutoff]

        selected.sort(
            key=lambda so: (
                so.due_date_obj(),
                _priority_rank(so.priority),
                -float(so.qty_mt or 0),
                str(so.so_id),
            )
        )
        return selected
    # ===== STEP 2: PROPOSE PLANNING ORDERS =====

    def propose_planning_orders(
        self,
        window_sos: List[SalesOrder],
        rules: Dict[str, Any] = None,
    ) -> List[PlanningOrder]:
        """
        Auto-propose manufacturing lots.

        Grouping logic:
        1. urgent jobs can stand alone or only combine with near-due compatible jobs
        2. same grade required
        3. same / near section preferred
        4. due-window spread capped
        5. max lot tonnage / max heats respected
        """
        if not window_sos:
            self.planning_orders = []
            return []

        rules = rules or {
            "heat_size_mt": float(self.config.get("HEAT_SIZE_MT", 50) or 50),
            "max_lot_mt": float(self.config.get("APS_MAX_LOT_MT", 300) or 300),
            "max_heats_per_lot": int(self.config.get("APS_MAX_HEATS_PER_LOT", 8) or 8),
            "urgent_window_hours": int(self.config.get("APS_URGENT_WINDOW_HOURS", 48) or 48),
            "max_due_spread_days": int(self.config.get("APS_MAX_DUE_SPREAD_DAYS", 3) or 3),
            "section_tolerance_mm": float(self.config.get("APS_SECTION_TOLERANCE_MM", 0.6) or 0.6),
        }

        def _priority_rank(priority: str) -> int:
            p = str(priority or "").strip().upper()
            return {"URGENT": 0, "HIGH": 1, "NORMAL": 2, "LOW": 3}.get(p, 9)

        def _is_urgent(so: SalesOrder) -> bool:
            return so.hours_until_due() <= rules["urgent_window_hours"] or str(so.priority or "").upper() == "URGENT"

        def _section_ok(a: SalesOrder, b: SalesOrder) -> bool:
            try:
                return abs(float(a.section_mm or 0) - float(b.section_mm or 0)) <= rules["section_tolerance_mm"]
            except Exception:
                return True

        def _due_spread_ok(lot: List[SalesOrder], candidate: SalesOrder) -> bool:
            dates = [x.due_date_obj() for x in lot] + [candidate.due_date_obj()]
            return (max(dates) - min(dates)).days <= rules["max_due_spread_days"]

        def _rolling_mode_ok(seed: SalesOrder, candidate: SalesOrder) -> bool:
            # Reject merging if rolling_mode differs
            return str(seed.rolling_mode or "HOT").strip().upper() == str(candidate.rolling_mode or "HOT").strip().upper()

        # Sort strongest planning signal first
        ordered = sorted(
            window_sos,
            key=lambda so: (
                _priority_rank(so.priority),
                so.due_date_obj(),
                str(so.grade or ""),
                float(so.section_mm or 0),
                -float(so.qty_mt or 0),
                str(so.so_id),
            ),
        )

        planning_orders: List[PlanningOrder] = []
        used: set[str] = set()
        po_counter = 1

        for seed in ordered:
            if seed.so_id in used:
                continue

            lot = [seed]
            used.add(seed.so_id)

            for candidate in ordered:
                if candidate.so_id in used:
                    continue

                # hard compatibility checks
                if str(candidate.grade or "").strip() != str(seed.grade or "").strip():
                    continue
                if not _section_ok(seed, candidate):
                    continue
                if not _due_spread_ok(lot, candidate):
                    continue
                if not _rolling_mode_ok(seed, candidate):
                    continue

                # urgent protection: do not bury urgent SOs in large pools
                if _is_urgent(seed) and len(lot) >= 2:
                    continue
                if _is_urgent(candidate) and not _is_urgent(seed):
                    continue

                trial_lot = lot + [candidate]
                trial_mt = sum(float(x.qty_mt or 0) for x in trial_lot)
                trial_heats = self._estimate_heats(trial_lot, rules)

                if trial_mt > rules["max_lot_mt"]:
                    continue
                if trial_heats > rules["max_heats_per_lot"]:
                    continue

                lot.append(candidate)
                used.add(candidate.so_id)

            total_mt = round(sum(float(x.qty_mt or 0) for x in lot), 3)
            heats = self._estimate_heats(lot, rules)
            due_dates = sorted(x.due_date_obj() for x in lot)
            section_values = sorted({float(x.section_mm or 0) for x in lot if x.section_mm is not None})

            # Determine priority as highest (most urgent) priority in the lot
            lot_priorities = [str(x.priority or "NORMAL").strip().upper() for x in lot]
            if "URGENT" in lot_priorities:
                lot_priority = "URGENT"
            elif "HIGH" in lot_priorities:
                lot_priority = "HIGH"
            else:
                lot_priority = "NORMAL"

            po = PlanningOrder(
                po_id=f"PO-{po_counter:04d}",
                selected_so_ids=[x.so_id for x in lot],
                total_qty_mt=total_mt,
                grade_family=str(seed.grade or "").strip(),
                size_family=",".join(f"{v:g}mm" for v in section_values) if section_values else "",
                due_window=(due_dates[0].date().isoformat(), due_dates[-1].date().isoformat()),
                route_family=str(seed.route_family or "SMS→RM"),
                heats_required=heats,
                planner_status="PROPOSED",
                order_type=str(seed.order_type or "MTO").strip().upper() or "MTO",
                rolling_mode=str(seed.rolling_mode or "HOT").strip().upper() or "HOT",
                priority=lot_priority,
            )
            planning_orders.append(po)
            po_counter += 1

        self.planning_orders = planning_orders
        return planning_orders

    # ===== STEP 3: DERIVE HEAT BATCHES =====

    def derive_heat_batches(
        self,
        planning_orders: List[PlanningOrder],
        heat_size_mt: float = 50.0,
    ) -> List[HeatBatch]:
        """
        Derive heats using ceiling-based fill logic.
        """
        heat_size_mt = float(heat_size_mt or self.config.get("HEAT_SIZE_MT", 50) or 50)
        heats: List[HeatBatch] = []
        heat_counter = 1

        for po in planning_orders:
            total_qty = float(po.total_qty_mt or 0)
            heats_needed = max(1, int(math.ceil(total_qty / heat_size_mt))) if heat_size_mt > 0 else 1
            remaining = total_qty

            for seq in range(1, heats_needed + 1):
                if seq < heats_needed:
                    heat_qty = min(heat_size_mt, remaining)
                else:
                    heat_qty = remaining

                heat = HeatBatch(
                    heat_id=f"HEAT-{heat_counter:05d}",
                    planning_order_id=po.po_id,
                    grade=po.grade_family,
                    qty_mt=round(float(heat_qty), 3),
                    heat_number_seq=seq,
                    upstream_route=po.route_family or "SMS→RM",
                    compatibility_class=str(po.grade_family or "").strip(),
                    expected_duration_hours=float(self.config.get("default_heat_duration", 2.0) or 2.0),
                )
                heats.append(heat)
                heat_counter += 1
                remaining = round(remaining - heat_qty, 6)

        self.heat_batches = heats
        return heats

    # ===== HELPERS =====

    def _estimate_heats(
        self,
        sos: List[SalesOrder],
        rules: Dict[str, Any] = None,
    ) -> int:
        """Estimate heats using ceiling, never round."""
        rules = rules or {"heat_size_mt": float(self.config.get("HEAT_SIZE_MT", 50) or 50)}
        total_mt = sum(float(so.qty_mt or 0) for so in sos)
        heat_size = float(rules.get("heat_size_mt", 50) or 50)
        if heat_size <= 0:
            heat_size = 50.0
        return max(1, int(math.ceil(total_mt / heat_size)))

    def validate_planning_orders(self, pos: List[PlanningOrder]) -> Dict[str, Any]:
        issues: List[str] = []
        total_mt = 0.0
        total_heats = 0
        total_sos = 0

        seen_po_ids = set()
        seen_so_ids = set()

        for po in pos:
            if po.po_id in seen_po_ids:
                issues.append(f"Duplicate PO_ID: {po.po_id}")
            seen_po_ids.add(po.po_id)

            po_mt = float(po.total_qty_mt or 0)
            if po_mt <= 0:
                issues.append(f"{po.po_id}: non-positive total_qty_mt")
            if not po.selected_so_ids:
                issues.append(f"{po.po_id}: no linked sales orders")
            if int(po.heats_required or 0) <= 0:
                issues.append(f"{po.po_id}: non-positive heats_required")
            if not str(po.grade_family or "").strip():
                issues.append(f"{po.po_id}: blank grade_family")

            for so_id in (po.selected_so_ids or []):
                so_id = str(so_id).strip()
                if not so_id:
                    issues.append(f"{po.po_id}: blank sales order id in selected_so_ids")
                    continue
                if so_id in seen_so_ids:
                    issues.append(f"{po.po_id}: sales order {so_id} appears in more than one planning order")
                seen_so_ids.add(so_id)

            total_mt += po_mt
            total_heats += int(po.heats_required or 0)
            total_sos += len(po.selected_so_ids or [])

        return {
            "valid": len(issues) == 0,
            "issues": issues,
            "total_sos": total_sos,
            "total_mt": round(total_mt, 3),
            "total_heats": total_heats,
            "po_count": len(pos),
        }
    # ===== STEP 4: FINITE CAPACITY SCHEDULING =====

    def simulate_finite_schedule(
        self,
        heat_batches: List[HeatBatch],
        planning_orders: List[PlanningOrder] = None,
        sms_resources: List[str] = None,
        rm_resources: List[str] = None,
        solver_time_limit_sec: float = 30.0,
    ) -> Dict[str, Any]:
        """
        Load estimator only.
        This is NOT the authoritative scheduler; the API should use engine.scheduler.schedule(...)
        for authoritative feasibility.
        """
        if not heat_batches:
            return {
                "authoritative": False,
                "solver_status": "NO_DATA",
                "feasible": False,
                "message": "No heat batches to schedule",
                "total_duration_hours": 0.0,
                "sms_hours": 0.0,
                "rm_hours": 0.0,
                "load_factor": "0%",
            }

        sms_count = max(1, len(sms_resources or ["SMS-01"]))
        rm_count = max(1, len(rm_resources or ["RM-01"]))

        default_heat_hrs = float(self.config.get("default_heat_duration", 2.0) or 2.0)
        rm_factor = float(self.config.get("rm_duration_factor", 1.2) or 1.2)
        horizon_hours = float(self.config.get("planning_horizon_hours", 168) or 168)

        total_sms_hours = sum(float(h.expected_duration_hours or default_heat_hrs) for h in heat_batches)
        total_rm_hours = sum(float(h.expected_duration_hours or default_heat_hrs) * rm_factor for h in heat_batches)

        sms_span = total_sms_hours / sms_count
        rm_span = total_rm_hours / rm_count
        total_duration = max(sms_span, rm_span)
        feasible = total_duration <= horizon_hours

        return {
            "authoritative": False,
            "solver_status": "ESTIMATE_ONLY",
            "feasible": feasible,
            "message": "Estimated load only. Use API simulate for authoritative finite schedule.",
            "total_duration_hours": round(total_duration, 2),
            "sms_hours": round(total_sms_hours, 2),
            "rm_hours": round(total_rm_hours, 2),
            "load_factor": f"{round((total_duration / horizon_hours) * 100, 1)}%",
        }
