"""Standalone MCP server entry point.

Run with:

    python main.py

The server communicates only through MCP JSON-RPC over stdio. This means an
external client, including an ASP.NET Core application, can start this process
with redirected StandardInput and StandardOutput, write JSON-RPC requests to
stdin, and read JSON-RPC responses from stdout. Do not write diagnostic output
to stdout; use stderr logging instead.
"""

from __future__ import annotations

import asyncio
import importlib
import inspect
import logging
import pkgutil
import sys
from pathlib import Path
from typing import Any

from mcp.server import Server
from mcp.server.stdio import stdio_server
from mcp.types import TextContent, Tool

from config import load_settings
from core.base_tool import BaseTool


SERVER_NAME = "mcp-gemini-enterprise-server"
ROOT_DIR = Path(__file__).resolve().parent

logging.basicConfig(
    level=logging.INFO,
    stream=sys.stderr,
    format="%(asctime)s %(levelname)s %(name)s - %(message)s",
)
logger = logging.getLogger(SERVER_NAME)


def discover_tools() -> dict[str, BaseTool]:
    """Import every module in tools/ and instantiate BaseTool subclasses."""

    tools_dir = ROOT_DIR / "tools"
    discovered: dict[str, BaseTool] = {}

    for module_info in pkgutil.iter_modules([str(tools_dir)]):
        if module_info.name.startswith("_") or module_info.name == "init":
            continue

        module_name = f"tools.{module_info.name}"
        try:
            module = importlib.import_module(module_name)
        except Exception:
            logger.exception("Failed to import tool module %s", module_name)
            continue

        for _, candidate in inspect.getmembers(module, inspect.isclass):
            if candidate is BaseTool or not issubclass(candidate, BaseTool):
                continue
            if inspect.isabstract(candidate):
                continue

            try:
                instance = candidate()
                name = instance.get_name()
                if name in discovered:
                    logger.warning("Skipping duplicate tool name: %s", name)
                    continue
                discovered[name] = instance
                logger.info("Loaded MCP tool: %s", name)
            except Exception:
                logger.exception("Failed to instantiate tool class %s", candidate.__name__)

    if not discovered:
        raise RuntimeError("No MCP tools were discovered in the tools/ directory.")

    return discovered


async def main() -> None:
    """Initialize the server and run the stdio transport."""

    load_settings()
    server = Server(SERVER_NAME)
    tools = discover_tools()

    @server.list_tools()
    async def handle_list_tools() -> list[Tool]:
        return [
            Tool(
                name=tool.get_name(),
                description=tool.get_description(),
                inputSchema=tool.get_input_schema(),
            )
            for tool in tools.values()
        ]

    @server.call_tool()
    async def handle_call_tool(
        name: str,
        arguments: dict[str, Any] | None,
    ) -> list[TextContent]:
        tool = tools.get(name)
        if tool is None:
            return [TextContent(type="text", text=f"Unknown tool: {name}")]

        try:
            result = await tool.execute(arguments or {})
        except Exception as exc:
            logger.exception("Tool execution failed for %s", name)
            result = f"Tool '{name}' failed: {exc}"

        return [TextContent(type="text", text=result)]

    logger.info("Starting MCP stdio server with %d tools", len(tools))
    async with stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options(),
        )


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception:
        logger.exception("MCP server failed during startup")
        raise
