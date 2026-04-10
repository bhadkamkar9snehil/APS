"""
Layer C — SimPy What-If Simulation (stub)
Stress-tests the baseline schedule against operational disruptions.
"""
import pandas as pd


class APSSimulation:
    def __init__(self, schedule: pd.DataFrame, scenarios: pd.Series):
        self.schedule = schedule
        self.scenarios = scenarios
        self.kpis = {"completed": 0, "late": 0, "total_delay_hrs": 0.0}

    def run(self, sim_days: int = 14):
        print("[SimPy] Simulation stub — install simpy and implement _job_process")
        return self.kpis


def run_scenario(schedule: pd.DataFrame, scenarios_df: pd.DataFrame) -> dict:
    sc = scenarios_df["Value"]
    sim = APSSimulation(schedule, sc)
    return sim.run()
