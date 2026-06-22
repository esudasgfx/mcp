"""Minimal MCP stdio test client for local validation.

This script starts the Python MCP server, performs the MCP initialize
handshake, lists tools, and optionally calls one tool.

Example:
    GEMINI_API_KEY=... python test_client.py --tool acc_tool \
        --arguments '{"project_id":"ACC-123","query":"Summarize risks"}'
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import threading
from pathlib import Path
from typing import Any


def _drain_stderr(process: subprocess.Popen[str]) -> None:
    assert process.stderr is not None
    for line in process.stderr:
        sys.stderr.write(f"[server] {line}")


def _send(process: subprocess.Popen[str], payload: dict[str, Any]) -> None:
    assert process.stdin is not None
    process.stdin.write(json.dumps(payload, separators=(",", ":")) + "\n")
    process.stdin.flush()


def _read_response(process: subprocess.Popen[str], expected_id: str) -> dict[str, Any]:
    assert process.stdout is not None
    while True:
        line = process.stdout.readline()
        if not line:
            raise RuntimeError("Server exited before returning a response.")
        message = json.loads(line)
        if str(message.get("id")) == expected_id:
            return message


def main() -> int:
    parser = argparse.ArgumentParser(description="Test the MCP Gemini stdio server.")
    parser.add_argument("--python", default=sys.executable, help="Python executable.")
    parser.add_argument(
        "--server",
        default=str(Path(__file__).with_name("main.py")),
        help="Path to main.py.",
    )
    parser.add_argument("--tool", help="Optional tool name to call.")
    parser.add_argument(
        "--arguments",
        default="{}",
        help="JSON object containing tool arguments.",
    )
    args = parser.parse_args()

    process = subprocess.Popen(
        [args.python, args.server],
        cwd=str(Path(args.server).resolve().parent),
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
    )
    threading.Thread(target=_drain_stderr, args=(process,), daemon=True).start()

    try:
        _send(
            process,
            {
                "jsonrpc": "2.0",
                "id": "init-1",
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "stdio-test-client", "version": "1.0.0"},
                },
            },
        )
        print(json.dumps(_read_response(process, "init-1"), indent=2))

        _send(
            process,
            {
                "jsonrpc": "2.0",
                "method": "notifications/initialized",
                "params": {},
            },
        )

        _send(process, {"jsonrpc": "2.0", "id": "tools-1", "method": "tools/list"})
        print(json.dumps(_read_response(process, "tools-1"), indent=2))

        if args.tool:
            _send(
                process,
                {
                    "jsonrpc": "2.0",
                    "id": "call-1",
                    "method": "tools/call",
                    "params": {
                        "name": args.tool,
                        "arguments": json.loads(args.arguments),
                    },
                },
            )
            print(json.dumps(_read_response(process, "call-1"), indent=2))
    finally:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
