"""SAP OData/RFC mock connector tool."""

from __future__ import annotations

import json
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class SapTool(BaseTool):
    """Mock SAP business data retrieval and Gemini interpretation."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "sap_tool"

    def get_description(self) -> str:
        return "Fetches SAP OData/RFC business data (mocked) and analyzes it."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "system_id": {
                    "type": "string",
                    "description": "SAP system identifier, such as S4HANA-PRD.",
                },
                "entity": {
                    "type": "string",
                    "description": "SAP entity or object type to inspect.",
                    "default": "purchase_orders",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
            },
            "required": ["system_id", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        system_id = str(params["system_id"])
        entity = str(params.get("entity", "purchase_orders"))
        query = str(params["query"])

        # Replace this mock with SAP OData/RFC integration and authentication.
        mock_data = {
            "source": "SAP",
            "system_id": system_id,
            "entity": entity,
            "records": [
                {
                    "purchase_order": "4500001001",
                    "vendor": "Contoso Industrial",
                    "value_usd": 125000,
                    "delivery_status": "delayed",
                    "days_late": 9,
                },
                {
                    "purchase_order": "4500001002",
                    "vendor": "Northwind Electrical",
                    "value_usd": 78000,
                    "delivery_status": "on_track",
                    "days_late": 0,
                },
            ],
        }

        return await self._gemini.process_data(json.dumps(mock_data, indent=2), query)
