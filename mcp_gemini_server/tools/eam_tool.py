"""Enterprise Asset Management mock connector tool."""

from __future__ import annotations

import json
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class EamTool(BaseTool):
    """Mock EAM asset data retrieval and Gemini interpretation."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "eam_tool"

    def get_description(self) -> str:
        return "Fetches enterprise asset management data (mocked) and analyzes it."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "asset_id": {
                    "type": "string",
                    "description": "Enterprise asset identifier.",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
            },
            "required": ["asset_id", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        asset_id = str(params["asset_id"])
        query = str(params["query"])

        mock_data = {
            "source": "Enterprise Asset Management",
            "asset_id": asset_id,
            "asset_name": "Chiller Plant 02",
            "health_score": 74,
            "maintenance_history": [
                {"work_order": "WO-8801", "type": "preventive", "status": "closed"},
                {"work_order": "WO-8912", "type": "corrective", "status": "open"},
            ],
            "sensor_summary": {
                "runtime_hours": 12420,
                "vibration_mm_s": 4.2,
                "temperature_c": 77.5,
            },
            "risk_flags": ["vibration above baseline", "open corrective work order"],
        }

        return await self._gemini.process_data(json.dumps(mock_data, indent=2), query)
