"""
Compatibility entrypoint for building the canonical APS workbook.

Run once:
    python build_template.py

This delegates to build_template_v3.py, which generates APS_BF_SMS_RM.xlsx.
"""
from pathlib import Path
import runpy


if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).with_name("build_template_v3.py")), run_name="__main__")
