"""Abstract base class for pluggable MCP tools."""

from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any


class BaseTool(ABC):
    """Contract every enterprise MCP tool must implement."""

    @abstractmethod
    def get_name(self) -> str:
        """Return the stable MCP tool name."""

    @abstractmethod
    def get_description(self) -> str:
        """Return a human-readable tool description."""

    @abstractmethod
    def get_input_schema(self) -> dict[str, Any]:
        """Return a JSON Schema describing this tool's arguments."""

    @abstractmethod
    async def execute(self, params: dict[str, Any]) -> str:
        """Execute the tool and return a string response."""
