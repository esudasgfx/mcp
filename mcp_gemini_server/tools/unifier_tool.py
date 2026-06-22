"""Oracle Primavera Unifier mock connector tool."""

from __future__ import annotations

import json
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class UnifierTool(BaseTool):
    """Mock Unifier shell/business process data retrieval and analysis."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "unifier_tool"

    def get_description(self) -> str:
        return "Fetches Unifier shell and business process data (mocked) and analyzes it."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "shell_number": {
                    "type": "string",
                    "description": "Unifier shell/project number.",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
            },
            "required": ["shell_number", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        shell_number = str(params["shell_number"])
        query = str(params["query"])

        mock_data = {
            "source": "Oracle Primavera Unifier",
            "shell_number": shell_number,
            "business_processes": [
                {
                    "record_no": "CO-00045",
                    "type": "Change Order",
                    "status": "pending_approval",
                    "amount_usd": 184000,
                },
                {
                    "record_no": "RISK-0018",
                    "type": "Risk",
                    "status": "active",
                    "exposure_usd": 420000,
                },
            ],
            "cost_sheet": {
                "approved_budget_usd": 12500000,
                "forecast_at_completion_usd": 13180000,
                "committed_cost_usd": 7300000,
            },
        }

        return await self._gemini.process_data(json.dumps(mock_data, indent=2), query)
