"""
Rolling Campaign Selection and Management

Implements rolling/sliding window campaign selection model where:
- Only 1 campaign in production at any time
- Next campaign selected proactively 24h before current completion
- Enables CP-SAT solver feasibility through simple 1-campaign scheduling
"""

from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import List, Optional, Dict, Any
import pandas as pd


@dataclass
class ProductionOrder:
    """Production order generated from one or more sales orders."""
    po_id: str
    so_ids: List[str]
    grade: str
    qty_mt: float
    heats: int
    duration_hours: float
    sequence: int
    resource_assignments: Dict[str, str] = field(default_factory=dict)

    def to_dict(self) -> dict:
        return {
            'po_id': self.po_id,
            'so_ids': self.so_ids,
            'grade': self.grade,
            'qty_mt': self.qty_mt,
            'heats': self.heats,
            'duration_hours': self.duration_hours,
            'sequence': self.sequence,
            'resource_assignments': self.resource_assignments
        }


@dataclass
class RollingCampaign:
    """A campaign in the rolling selection model."""
    id: str
    sales_orders: List[dict]
    grade: str
    total_mt: float
    estimated_heats: int
    estimated_sms_duration: float
    estimated_rm_duration: float
    estimated_total_duration: float
    production_orders: List[ProductionOrder] = field(default_factory=list)
    status: str = 'PLANNED'
    created_at: datetime = field(default_factory=datetime.now)

    def to_dict(self) -> dict:
        return {
            'id': self.id,
            'sales_orders': self.sales_orders,
            'grade': self.grade,
            'total_mt': self.total_mt,
            'estimated_heats': self.estimated_heats,
            'estimated_sms_duration': self.estimated_sms_duration,
            'estimated_rm_duration': self.estimated_rm_duration,
            'estimated_total_duration': self.estimated_total_duration,
            'production_orders': [po.to_dict() for po in self.production_orders],
            'status': self.status,
            'created_at': self.created_at.isoformat()
        }


class CampaignSelector:
    """Selects rolling campaigns from open sales orders."""

    def __init__(self, config: Dict[str, Any]):
        self.config = config

    def recommend_next_campaign(
        self,
        sales_orders: List[dict],
        strategy: str = 'URGENT_FIRST',
        max_campaign_mt: float = 500.0,
        max_campaign_heats: int = 12,
    ) -> Optional[RollingCampaign]:
        """
        Recommend the next campaign to be selected.

        Args:
            sales_orders: List of open SOs (not yet covered)
            strategy: Selection strategy (URGENT_FIRST, DEMAND_WINDOW, HYBRID_SCORE)
            max_campaign_mt: Maximum MT per campaign
            max_campaign_heats: Maximum heats per campaign

        Returns:
            RollingCampaign object or None if no SOs available
        """
        if not sales_orders:
            return None

        # Filter uncovered SOs (those not yet assigned to a released campaign)
        uncovered = [so for so in sales_orders if so.get('Status') != 'Covered']

        if not uncovered:
            return None

        if strategy == 'URGENT_FIRST':
            selected_sos = self._select_urgent_first(
                uncovered, max_campaign_mt, max_campaign_heats
            )
        elif strategy == 'DEMAND_WINDOW':
            selected_sos = self._select_demand_window(uncovered, max_campaign_mt)
        elif strategy == 'HYBRID_SCORE':
            selected_sos = self._select_hybrid_score(uncovered, max_campaign_mt)
        else:
            selected_sos = self._select_urgent_first(uncovered, max_campaign_mt, max_campaign_heats)

        if not selected_sos:
            return None

        # Build campaign
        campaign = self._build_campaign(selected_sos)
        return campaign

    def _select_urgent_first(self, candidates: List[dict], max_mt: float, max_heats: int) -> List[dict]:
        """
        URGENT_FIRST strategy: Prioritize urgent SOs, consolidate by grade.
        """
        # Filter for URGENT, sort by delivery date
        urgent = [so for so in candidates if so.get('Priority') == 'URGENT']
        urgent.sort(key=lambda x: x.get('Delivery_Date') or '2099-12-31')

        if not urgent:
            return []

        selected = [urgent[0]]
        campaign_grade = urgent[0].get('Grade')
        campaign_mt = urgent[0].get('Order_Qty_MT', 0)
        campaign_heats = self._estimate_heats([urgent[0]])

        # Greedily add more SOs of same grade
        for so in urgent[1:]:
            if so.get('Grade') == campaign_grade:
                new_mt = campaign_mt + so.get('Order_Qty_MT', 0)
                new_heats = self._estimate_heats(selected + [so])
                if new_mt <= max_mt and new_heats <= max_heats:
                    selected.append(so)
                    campaign_mt = new_mt
                    campaign_heats = new_heats

        return selected

    def _select_demand_window(self, candidates: List[dict], max_mt: float) -> List[dict]:
        """
        DEMAND_WINDOW strategy: Next 48 hours, largest grade.
        """
        # Filter SOs due within next 48 hours
        now = datetime.now()
        cutoff = now + timedelta(hours=48)

        window_sos = [
            so for so in candidates
            if self._parse_date(so.get('Delivery_Date')) <= cutoff
        ]

        if not window_sos:
            return []

        # Group by grade, pick grade with max urgent MT
        grade_urgent_mt = {}
        for so in window_sos:
            if so.get('Priority') == 'URGENT':
                grade = so.get('Grade')
                grade_urgent_mt[grade] = grade_urgent_mt.get(grade, 0) + so.get('Order_Qty_MT', 0)

        if not grade_urgent_mt:
            return []

        best_grade = max(grade_urgent_mt, key=grade_urgent_mt.get)

        # Select all urgent SOs of that grade
        selected = [
            so for so in window_sos
            if so.get('Grade') == best_grade and so.get('Priority') == 'URGENT'
        ]

        # Truncate to max_mt if needed
        total = sum(so.get('Order_Qty_MT', 0) for so in selected)
        if total > max_mt:
            selected.sort(key=lambda x: x.get('Delivery_Date') or '2099-12-31')
            cumsum = 0
            trimmed = []
            for so in selected:
                qty = so.get('Order_Qty_MT', 0)
                if cumsum + qty <= max_mt:
                    trimmed.append(so)
                    cumsum += qty
            selected = trimmed

        return selected

    def _select_hybrid_score(self, candidates: List[dict], max_mt: float) -> List[dict]:
        """
        HYBRID_SCORE strategy: Weighted scoring on urgency, batch size, consolidation.
        """
        now = datetime.now()
        window_cutoff = now + timedelta(hours=48)

        # Calculate scores
        scores = []
        for so in candidates:
            due = self._parse_date(so.get('Delivery_Date'))
            hours_to_due = (due - now).total_seconds() / 3600 if due else 9999

            # Urgency (0-1): max if due within 24h, 0 if beyond 96h
            urgency = max(0, min(1, 1 - hours_to_due / 48))

            # Batch size (0-1): 0 at 0 MT, 1 at 500+ MT
            batch_size = min(1, so.get('Order_Qty_MT', 0) / 500)

            # Priority boost
            priority_boost = 1.5 if so.get('Priority') == 'URGENT' else 1.2 if so.get('Priority') == 'HIGH' else 1.0

            # Combined score
            score = (0.5 * urgency + 0.3 * batch_size) * priority_boost

            in_window = due <= window_cutoff
            scores.append((so, score, in_window))

        # Filter to window, sort by score
        window_scores = [(so, s) for so, s, w in scores if w]
        if not window_scores:
            window_scores = scores

        window_scores.sort(key=lambda x: x[1], reverse=True)

        # Greedy selection
        selected = []
        selected_mt = 0
        campaign_grade = None

        for so, _ in window_scores:
            if not selected:
                # First SO sets the grade
                campaign_grade = so.get('Grade')
                selected.append(so)
                selected_mt += so.get('Order_Qty_MT', 0)
            elif so.get('Grade') == campaign_grade:
                # Same grade
                new_mt = selected_mt + so.get('Order_Qty_MT', 0)
                if new_mt <= max_mt:
                    selected.append(so)
                    selected_mt = new_mt

        return selected

    def _build_campaign(self, selected_sos: List[dict]) -> RollingCampaign:
        """Build a RollingCampaign from selected SOs."""
        total_mt = sum(so.get('Order_Qty_MT', 0) for so in selected_sos)
        grade = selected_sos[0].get('Grade') if selected_sos else 'Unknown'
        heats = self._estimate_heats(selected_sos)
        sms_duration = self._estimate_sms_duration(selected_sos)
        rm_duration = self._estimate_rm_duration(selected_sos)
        total_duration = sms_duration + rm_duration

        campaign_id = f"CMP-{datetime.now().strftime('%Y%m%d%H%M%S')}"

        campaign = RollingCampaign(
            id=campaign_id,
            sales_orders=selected_sos,
            grade=grade,
            total_mt=total_mt,
            estimated_heats=heats,
            estimated_sms_duration=sms_duration,
            estimated_rm_duration=rm_duration,
            estimated_total_duration=total_duration
        )

        return campaign

    def _estimate_heats(self, sos: List[dict]) -> int:
        """Estimate number of SMS heats needed."""
        total_mt = sum(so.get('Order_Qty_MT', 0) for so in sos)
        # Rough estimate: 50 MT per heat on average
        return max(1, round(total_mt / 50))

    def _estimate_sms_duration(self, sos: List[dict]) -> float:
        """Estimate SMS duration in hours."""
        heats = self._estimate_heats(sos)
        # ~2 hours per heat + 1h setup
        return heats * 2 + 1

    def _estimate_rm_duration(self, sos: List[dict]) -> float:
        """Estimate RM (rolling mill) duration in hours."""
        total_mt = sum(so.get('Order_Qty_MT', 0) for so in sos)
        # ~30 MT/hour rolling speed + 1h setup
        return round(total_mt / 30) + 1

    def _parse_date(self, date_str: Any) -> datetime:
        """Parse a date string to datetime."""
        if isinstance(date_str, datetime):
            return date_str
        if isinstance(date_str, str):
            for fmt in ['%Y-%m-%d', '%Y-%m-%d %H:%M:%S', '%m/%d/%Y']:
                try:
                    return datetime.strptime(date_str, fmt)
                except ValueError:
                    pass
        return datetime(2099, 12, 31)


class POGenerator:
    """Generates production orders from a rolling campaign."""

    def __init__(self, config: Dict[str, Any]):
        self.config = config

    def generate_pos(
        self,
        campaign: RollingCampaign,
        strategy: str = '1to1'
    ) -> List[ProductionOrder]:
        """
        Generate production orders from a campaign.

        Args:
            campaign: The rolling campaign
            strategy: PO generation strategy
                     '1to1': One PO per SO
                     'consolidated': One PO per grade
                     'heat-optimized': Minimize heats, consolidate same-grade SOs

        Returns:
            List of ProductionOrder objects
        """
        if strategy == '1to1':
            return self._generate_1to1(campaign)
        elif strategy == 'consolidated':
            return self._generate_consolidated(campaign)
        elif strategy == 'heat-optimized':
            return self._generate_heat_optimized(campaign)
        else:
            return self._generate_1to1(campaign)

    def _generate_1to1(self, campaign: RollingCampaign) -> List[ProductionOrder]:
        """One PO per SO."""
        pos = []
        for i, so in enumerate(campaign.sales_orders):
            po = ProductionOrder(
                po_id=f"{campaign.id}-PO-{i+1:03d}",
                so_ids=[so.get('SO_ID')],
                grade=so.get('Grade'),
                qty_mt=so.get('Order_Qty_MT', 0),
                heats=max(1, round(so.get('Order_Qty_MT', 0) / 50)),
                duration_hours=max(1, round(so.get('Order_Qty_MT', 0) / 50)) * 2 + 3,  # SMS + RM
                sequence=i
            )
            pos.append(po)
        return pos

    def _generate_consolidated(self, campaign: RollingCampaign) -> List[ProductionOrder]:
        """One PO per grade."""
        pos_by_grade = {}
        for so in campaign.sales_orders:
            grade = so.get('Grade')
            if grade not in pos_by_grade:
                pos_by_grade[grade] = {
                    'so_ids': [],
                    'qty_mt': 0,
                    'heats': 0
                }
            pos_by_grade[grade]['so_ids'].append(so.get('SO_ID'))
            pos_by_grade[grade]['qty_mt'] += so.get('Order_Qty_MT', 0)

        # Estimate heats and convert to PO
        pos = []
        for i, (grade, data) in enumerate(pos_by_grade.items()):
            heats = max(1, round(data['qty_mt'] / 50))
            po = ProductionOrder(
                po_id=f"{campaign.id}-PO-{i+1:03d}",
                so_ids=data['so_ids'],
                grade=grade,
                qty_mt=data['qty_mt'],
                heats=heats,
                duration_hours=heats * 2 + 3,
                sequence=i
            )
            pos.append(po)

        return pos

    def _generate_heat_optimized(self, campaign: RollingCampaign) -> List[ProductionOrder]:
        """Minimize heats, consolidate SOs of same grade."""
        # Group SOs by grade
        sos_by_grade = {}
        for so in campaign.sales_orders:
            grade = so.get('Grade')
            if grade not in sos_by_grade:
                sos_by_grade[grade] = []
            sos_by_grade[grade].append(so)

        # For each grade, create heats
        pos = []
        po_counter = 0

        for grade in sorted(sos_by_grade.keys()):
            grade_sos = sos_by_grade[grade]
            grade_mt = sum(so.get('Order_Qty_MT', 0) for so in grade_sos)
            heats_needed = max(1, round(grade_mt / 50))

            # Distribute SOs across heats
            sos_per_heat = len(grade_sos) / heats_needed if heats_needed > 0 else 1
            so_index = 0

            for heat in range(heats_needed):
                po_counter += 1
                heat_sos = []
                heat_mt = 0
                heat_target = grade_mt / heats_needed

                while so_index < len(grade_sos) and heat_mt < heat_target * 0.95:
                    so = grade_sos[so_index]
                    heat_sos.append(so.get('SO_ID'))
                    heat_mt += so.get('Order_Qty_MT', 0)
                    so_index += 1

                # If no SOs assigned to this heat (shouldn't happen), add the next one
                if not heat_sos and so_index < len(grade_sos):
                    heat_sos.append(grade_sos[so_index].get('SO_ID'))
                    heat_mt = grade_sos[so_index].get('Order_Qty_MT', 0)
                    so_index += 1

                if heat_sos:
                    po = ProductionOrder(
                        po_id=f"{campaign.id}-PO-{po_counter:03d}",
                        so_ids=heat_sos,
                        grade=grade,
                        qty_mt=heat_mt,
                        heats=1,  # One heat per PO
                        duration_hours=1 * 2 + 1.5,  # SMS + RM for one heat
                        sequence=po_counter - 1
                    )
                    pos.append(po)

        return pos
