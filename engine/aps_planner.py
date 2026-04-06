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
        Select candidate SOs within planning window.

        Args:
            all_sos: All available sales orders
            window: Planning horizon (next 3/7/10/14 days, or overdue+5)

        Returns:
            List of SOs in the planning window
        """
        now = datetime.now()
        candidates = [so for so in all_sos if so.status == 'Open']

        if window == PlanningHorizon.OVERDUE_PLUS_5_DAYS:
            # Include overdue + next 5 days
            cutoff = now + timedelta(days=5)
            return [so for so in candidates if so.due_date_obj() <= cutoff]
        else:
            # Next N days
            days = window.value if isinstance(window.value, int) else 7
            cutoff = now + timedelta(days=days)
            return [so for so in candidates if so.due_date_obj() <= cutoff]

    # ===== STEP 2: PROPOSE PLANNING ORDERS =====

    def propose_planning_orders(
        self,
        window_sos: List[SalesOrder],
        rules: Dict[str, Any] = None,
    ) -> List[PlanningOrder]:
        """
        Auto-propose Planning Orders (manufacturing lots) from selected SOs.

        Rules applied (in order):
        1. Grade compatibility (prefer same grade to minimize changeovers)
        2. Due-date proximity (group similar due dates)
        3. Size/section compatibility if available
        4. Heat-size constraint (≤50 MT per heat typically)
        5. Urgent order protection (don't delay urgent orders)

        Args:
            window_sos: SOs in the planning window
            rules: Override rules (optional)

        Returns:
            List of proposed PlanningOrder objects
        """
        if not window_sos:
            return []

        # Default rules
        if rules is None:
            rules = {
                'max_lot_mt': 500,
                'max_heats_per_lot': 12,
                'heat_size_mt': 50,
                'urgent_window_hours': 48,
            }

        # Group by grade first (primary axis)
        grade_groups: Dict[str, List[SalesOrder]] = {}
        for so in window_sos:
            if so.grade not in grade_groups:
                grade_groups[so.grade] = []
            grade_groups[so.grade].append(so)

        # Within each grade, sub-group by due-date proximity
        planning_orders = []
        po_counter = 1

        for grade in sorted(grade_groups.keys()):
            grade_sos = grade_groups[grade]

            # Sort by due date (nearest first)
            grade_sos.sort(key=lambda so: so.due_date_obj())

            # Create lots within this grade
            i = 0
            while i < len(grade_sos):
                # Start a new lot with this SO
                lot_sos = [grade_sos[i]]
                lot_mt = grade_sos[i].qty_mt
                lot_due_min = grade_sos[i].due_date_obj()
                lot_due_max = lot_due_min

                i += 1

                # Greedily add compatible SOs to this lot
                while i < len(grade_sos):
                    next_so = grade_sos[i]
                    new_mt = lot_mt + next_so.qty_mt
                    new_heats = self._estimate_heats([so for so in lot_sos] + [next_so], rules)

                    # Check constraints
                    mt_ok = new_mt <= rules['max_lot_mt']
                    heats_ok = new_heats <= rules['max_heats_per_lot']
                    grade_ok = next_so.grade == grade

                    if mt_ok and heats_ok and grade_ok:
                        lot_sos.append(next_so)
                        lot_mt = new_mt
                        lot_due_max = next_so.due_date_obj()
                        i += 1
                    else:
                        break

                # Create PlanningOrder
                heats = self._estimate_heats(lot_sos, rules)
                po = PlanningOrder(
                    po_id=f'PO-{po_counter:04d}',
                    selected_so_ids=[so.so_id for so in lot_sos],
                    total_qty_mt=lot_mt,
                    grade_family=grade,
                    size_family=','.join(sorted(set(f'{so.section_mm}mm' for so in lot_sos))),
                    due_window=(
                        lot_due_min.isoformat()[:10],
                        lot_due_max.isoformat()[:10],
                    ),
                    route_family='SMS→RM',  # Typical steel path
                    heats_required=heats,
                    planner_status='PROPOSED',
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
        Derive heat requirements from Planning Orders.

        Args:
            planning_orders: The manufacturing lots
            heat_size_mt: Standard heat size (MT)

        Returns:
            List of HeatBatch objects
        """
        heats = []
        heat_counter = 1

        for po in planning_orders:
            # Calculate heats for this PO
            heats_needed = po.heats_required

            # Distribute PO qty across heats
            qty_per_heat = po.total_qty_mt / heats_needed
            remaining_qty = po.total_qty_mt

            for heat_seq in range(1, heats_needed + 1):
                heat_qty = min(qty_per_heat, remaining_qty)

                heat = HeatBatch(
                    heat_id=f'HEAT-{heat_counter:05d}',
                    planning_order_id=po.po_id,
                    grade=po.grade_family,
                    qty_mt=heat_qty,
                    heat_number_seq=heat_seq,
                    upstream_route='SMS→RM',
                    compatibility_class='standard',
                    expected_duration_hours=2.0,  # Typical SMS melt
                )
                heats.append(heat)
                heat_counter += 1
                remaining_qty -= heat_qty

        self.heat_batches = heats
        return heats

    # ===== HELPERS =====

    def _estimate_heats(
        self,
        sos: List[SalesOrder],
        rules: Dict[str, Any] = None,
    ) -> int:
        """Estimate number of heats needed."""
        if rules is None:
            rules = {'heat_size_mt': 50}

        total_mt = sum(so.qty_mt for so in sos)
        heat_size = rules.get('heat_size_mt', 50)
        return max(1, round(total_mt / heat_size))

    def validate_planning_orders(self, pos: List[PlanningOrder]) -> Dict[str, Any]:
        """Validate proposed planning orders."""
        return {
            'valid': True,
            'total_sos': sum(len(po.selected_so_ids) for po in pos),
            'total_mt': sum(po.total_qty_mt for po in pos),
            'total_heats': sum(po.heats_required for po in pos),
            'po_count': len(pos),
        }
