"""Small helper for starting, stopping, and probing the APS API locally."""
from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[2]
RUNTIME_DIR = ROOT / ".runtime"
PID_FILE = RUNTIME_DIR / "api.pid"
LOG_FILE = RUNTIME_DIR / "api.log"
DEFAULT_WORKBOOK = ROOT / "APS_BF_SMS_RM.xlsx"
DEFAULT_PORT = 5000


def _runtime_dir() -> None:
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)


def _read_pid() -> int | None:
    try:
        return int(PID_FILE.read_text().strip())
    except Exception:
        return None


def _write_pid(pid: int) -> None:
    _runtime_dir()
    PID_FILE.write_text(f"{pid}\n")


def _clear_pid() -> None:
    try:
        PID_FILE.unlink()
    except FileNotFoundError:
        pass


def _process_alive(pid: int | None) -> bool:
    if not pid:
        return False
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def _base_url(port: int) -> str:
    return f"http://127.0.0.1:{port}"


def _health(port: int, timeout: float = 2.0) -> tuple[bool, str]:
    try:
        response = requests.get(f"{_base_url(port)}/api/health", timeout=timeout)
        return response.ok, response.text
    except requests.RequestException as exc:
        return False, str(exc)


def start_api(port: int, workbook: Path, wait_seconds: float) -> int:
    _runtime_dir()
    existing_pid = _read_pid()
    if _process_alive(existing_pid):
        print(f"API already running with PID {existing_pid}")
        return 0
    if existing_pid:
        _clear_pid()

    env = os.environ.copy()
    env["PORT"] = str(port)
    env["WORKBOOK_PATH"] = str(workbook)

    with LOG_FILE.open("ab") as log_file:
        process = subprocess.Popen(
            [sys.executable, "xaps_application_api.py"],
            cwd=ROOT,
            env=env,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            start_new_session=True,
        )

    _write_pid(process.pid)
    deadline = time.time() + max(wait_seconds, 0.0)
    while time.time() < deadline:
        healthy, _ = _health(port, timeout=1.0)
        if healthy:
            print(f"API started on {_base_url(port)} with PID {process.pid}")
            return 0
        if process.poll() is not None:
            print(f"API exited early with code {process.returncode}")
            return process.returncode or 1
        time.sleep(0.5)

    print(f"API process started with PID {process.pid}, but /api/health did not respond within {wait_seconds:.1f}s")
    return 1


def stop_api(timeout_seconds: float) -> int:
    pid = _read_pid()
    if not _process_alive(pid):
        _clear_pid()
        print("API is not running")
        return 0

    os.kill(pid, signal.SIGTERM)
    deadline = time.time() + max(timeout_seconds, 0.0)
    while time.time() < deadline:
        if not _process_alive(pid):
            _clear_pid()
            print(f"Stopped API PID {pid}")
            return 0
        time.sleep(0.25)

    os.kill(pid, signal.SIGKILL)
    _clear_pid()
    print(f"Force-killed API PID {pid}")
    return 0


def status_api(port: int) -> int:
    pid = _read_pid()
    alive = _process_alive(pid)
    healthy, detail = _health(port, timeout=1.0)
    payload = {
        "pid": pid,
        "process_alive": alive,
        "health_ok": healthy,
        "base_url": _base_url(port),
        "log_file": str(LOG_FILE),
    }
    if healthy:
        try:
            payload["health_response"] = json.loads(detail)
        except json.JSONDecodeError:
            payload["health_response"] = detail
    else:
        payload["health_error"] = detail
    print(json.dumps(payload, indent=2))
    return 0 if alive else 1


def health_api(port: int) -> int:
    healthy, detail = _health(port)
    if healthy:
        try:
            print(json.dumps(json.loads(detail), indent=2))
        except json.JSONDecodeError:
            print(detail)
        return 0
    print(detail)
    return 1


def tail_log(lines: int) -> int:
    if not LOG_FILE.exists():
        print(f"No log file found at {LOG_FILE}")
        return 1
    content = LOG_FILE.read_text(errors="replace").splitlines()
    for line in content[-max(lines, 1):]:
        print(line)
    return 0


def request_api(port: int, path: str, method: str, body: str | None) -> int:
    url = f"{_base_url(port)}{path if path.startswith('/') else '/' + path}"
    kwargs = {"timeout": 10.0}
    if body:
        kwargs["json"] = json.loads(body)
    response = requests.request(method.upper(), url, **kwargs)
    print(f"HTTP {response.status_code}")
    try:
        print(json.dumps(response.json(), indent=2))
    except ValueError:
        print(response.text)
    return 0 if response.ok else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--workbook", type=Path, default=DEFAULT_WORKBOOK)

    subparsers = parser.add_subparsers(dest="command", required=True)

    start = subparsers.add_parser("start", help="Start the API in the background")
    start.add_argument("--wait", type=float, default=20.0)

    stop = subparsers.add_parser("stop", help="Stop the background API")
    stop.add_argument("--timeout", type=float, default=5.0)

    subparsers.add_parser("status", help="Show process and health status")
    subparsers.add_parser("health", help="Call /api/health")

    tail = subparsers.add_parser("tail", help="Show the last lines from the API log")
    tail.add_argument("-n", "--lines", type=int, default=40)

    request = subparsers.add_parser("request", help="Make an arbitrary API request")
    request.add_argument("path")
    request.add_argument("--method", default="GET")
    request.add_argument("--body")

    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "start":
        return start_api(args.port, args.workbook, args.wait)
    if args.command == "stop":
        return stop_api(args.timeout)
    if args.command == "status":
        return status_api(args.port)
    if args.command == "health":
        return health_api(args.port)
    if args.command == "tail":
        return tail_log(args.lines)
    if args.command == "request":
        return request_api(args.port, args.path, args.method, args.body)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
