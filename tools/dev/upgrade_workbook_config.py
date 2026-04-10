"""Upgrade a workbook copy with the latest APS runtime config rows."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WORKBOOK = ROOT / "APS_BF_SMS_RM.xlsx"
sys.path.insert(0, str(ROOT))

from engine.config import upgrade_workbook_config


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workbook", nargs="?", type=Path, default=DEFAULT_WORKBOOK)
    parser.add_argument("--output", type=Path, help="Optional output workbook path")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    result = upgrade_workbook_config(args.workbook, output_path=args.output)
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
