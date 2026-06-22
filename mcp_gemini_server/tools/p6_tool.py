"""Primavera P6 EPPM mock connector tool."""

from __future__ import annotations

import json
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class P6Tool(BaseTool):
    """Mock P6 project/activity retrieval and Gemini interpretation."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "p6_tool"

    def get_description(self) -> str:
        return "Fetches Primavera P6 project schedule data (mocked) and analyzes it."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "project_code": {
                    "type": "string",
                    "description": "Primavera P6 project code.",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
            },
            "required": ["project_code", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        project_code = str(params["project_code"])
        query = str(params["query"])

        # Replace this mock with real P6 EPPM REST calls using requests.
        mock_data = {
            "source": "Primavera P6 EPPM",
            "project_code": project_code,
            "project_name": "Airport Enabling Works",
            "data_date": "2026-06-01",
            "activities": [
                {
                    "activity_id": "A1000",
                    "name": "Mobilize site team",
                    "status": "complete",
                    "percent_complete": 100,
                    "total_float_days": 0,
                },
                {
                    "activity_id": "A2210",
                    "name": "Install temporary utilities",
                    "status": "in_progress",
                    "percent_complete": 62,
                    "total_float_days": -4,
                },
                {
                    "activity_id": "A3350",
                    "name": "Complete foundation pour",
                    "status": "not_started",
                    "percent_complete": 0,
                    "total_float_days": 8,
                },
            ],
            "metrics": {"critical_activities": 12, "late_activities": 7},
        }

        return await self._gemini.process_data(json.dumps(mock_data, indent=2), query)
