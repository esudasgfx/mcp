"""Autodesk Construction Cloud mock connector tool."""

from __future__ import annotations

import json
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class AccTool(BaseTool):
    """Mock ACC/APS project data retrieval and Gemini interpretation."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "acc_tool"

    def get_description(self) -> str:
        return "Fetches Autodesk Construction Cloud project data (mocked) and analyzes it."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "project_id": {
                    "type": "string",
                    "description": "Autodesk ACC/APS project identifier.",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
            },
            "required": ["project_id", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        project_id = str(params["project_id"])
        query = str(params["query"])

        # Replace this mock with real APS/ACC REST calls using requests and OAuth.
        mock_data = {
            "source": "Autodesk Construction Cloud",
            "project_id": project_id,
            "project_name": "North Terminal Expansion",
            "issues": [
                {"id": "ACC-101", "status": "open", "priority": "high", "trade": "MEP"},
                {"id": "ACC-102", "status": "in_review", "priority": "medium", "trade": "Civil"},
            ],
            "rfis": [
                {"id": "RFI-44", "status": "answered", "days_open": 3},
                {"id": "RFI-45", "status": "open", "days_open": 11},
            ],
            "documents": {"approved": 128, "in_review": 17, "rejected": 4},
        }

        return await self._gemini.process_data(json.dumps(mock_data, indent=2), query)
