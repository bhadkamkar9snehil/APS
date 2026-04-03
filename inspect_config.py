import pandas as pd
WORKBOOK = r'c:\Users\bhadk\Documents\APS\APS_BF_SMS_RM.xlsx'

print("=== Config ===")
df = pd.read_excel(WORKBOOK, sheet_name='Config', header=2)
df = df.dropna(subset=['Key'])
for _, r in df.iterrows():
    k = str(r.get('Key','')).strip()
    v = str(r.get('Value','')).strip()
    print(f"  {k:40s} = {v}")

print("\n=== CP-SAT check ===")
try:
    from ortools.sat.python import cp_model
    ok = hasattr(cp_model, 'CpModel') and hasattr(cp_model, 'CpSolver')
    print(f"  CP-SAT available: {ok}")
    # Try actually using it
    m = cp_model.CpModel()
    s = cp_model.CpSolver()
    x = m.NewIntVar(0,10,'x')
    m.Add(x >= 3)
    status = s.Solve(m)
    statuses = {0:'UNKNOWN',1:'MODEL_INVALID',2:'FEASIBLE',3:'INFEASIBLE',4:'OPTIMAL'}
    print(f"  Test solve status: {statuses.get(status, status)} ({status})")
    print("  CP-SAT is working correctly!")
except Exception as e:
    print(f"  CP-SAT error: {e}")

print("\n=== Scheduler GREEDY reason ===")
import requests
try:
    r = requests.get('http://localhost:5000/api/health', timeout=3)
    d = r.json()
    print(f"  solver_status = {d.get('solver_status')}")
    print(f"  last_run = {d.get('last_run')}")
except Exception as e:
    print(f"  API error: {e}")

print("\n=== All sheets ===")
import openpyxl
wb = openpyxl.load_workbook(WORKBOOK, read_only=True, data_only=True)
sheets = wb.sheetnames
wb.close()
for s in sheets:
    try:
        df2 = pd.read_excel(WORKBOOK, sheet_name=s, header=2, nrows=2)
        cols = [c for c in df2.columns if not str(c).startswith('Unnamed')][:10]
        print(f"  {s:28s} ncols={len(cols):3d}  {cols[:6]}")
    except Exception as e:
        print(f"  {s:28s} ERROR: {str(e)[:60]}")
