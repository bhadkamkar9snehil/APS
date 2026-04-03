"""
Layer C — SimPy What-If Simulation
Stress-tests the baseline schedule against operational disruptions:
- Machine breakdowns
- Material delays
- Operator unavailability
- Variable process times

Run:  python simulation/simpy_engine.py
"""
# import simpy  # uncomment when simpy installed
import pandas as pd
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent))
from data.loader import load_all


class APSSimulation:
    """
    Discrete-event simulation of the steel plant APS schedule.
    Each resource is a simpy.Resource with fixed capacity=1 (single machine).
    """

    def __init__(self, schedule: pd.DataFrame, scenarios: pd.Series):
        self.schedule  = schedule
        self.scenarios = scenarios
        self.kpis = {
            "completed": 0,
            "late":      0,
            "total_delay_hrs": 0.0,
        }

    def run(self, sim_days: int = 14):
        # import simpy
        # env = simpy.Environment()
        # resources = {rid: simpy.Resource(env, capacity=1) for rid in self.schedule["Resource_ID"].unique()}
        # for _, job in self.schedule.iterrows():
        #     env.process(self._job_process(env, resources, job))
        # env.run(until=sim_days * 24)  # hours
        print("[SimPy] Simulation stub — install simpy and implement _job_process")
        return self.kpis

    def _job_process(self, env, resources, job):
        """Coroutine: request resource → process → release → log."""
        # res = resources[job["Resource_ID"]]
        # with res.request() as req:
        #     yield req
        #     yield env.timeout(job["Duration_Hrs"])
        #     # apply machine breakdown events, variable times, etc.
        pass

    def apply_machine_breakdown(self, env, resource, breakdown_hrs: float):
        """Inject a breakdown event at a random time."""
        # yield env.timeout(random_time)
        # resource.capacity = 0
        # yield env.timeout(breakdown_hrs)
        # resource.capacity = 1
        pass


def run_scenario(schedule: pd.DataFrame, scenarios_df: pd.DataFrame) -> dict:
    sc = scenarios_df["Value"]
    sim = APSSimulation(schedule, sc)
    return sim.run()


if __name__ == "__main__":
    data = load_all()
    # Load schedule from scheduler output (stub: empty df for now)
    schedule = pd.DataFrame(columns=["Job_ID","Resource_ID","Duration_Hrs","Planned_Start","Planned_End"])
    kpis = run_scenario(schedule, data["scenarios"])
    print("Simulation KPIs:", kpis)
