"""PDF text extraction and Gemini summarization tool."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from PyPDF2 import PdfReader

from config import load_settings
from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class PdfTool(BaseTool):
    """Extract text from PDFs and ask Gemini to analyze it."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()
        self._settings = load_settings()

    def get_name(self) -> str:
        return "pdf_tool"

    def get_description(self) -> str:
        return "Extracts text from a PDF and uses Gemini to summarize or answer questions."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "file_path": {
                    "type": "string",
                    "description": "Path to the PDF file on the MCP server machine.",
                },
                "query": {
                    "type": "string",
                    "description": "Question or instruction for Gemini.",
                },
                "max_chars": {
                    "type": "integer",
                    "description": "Maximum extracted characters to send to Gemini.",
                    "default": self._settings.max_pdf_chars,
                    "minimum": 500,
                    "maximum": 50000,
                },
            },
            "required": ["file_path", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        file_path = Path(str(params["file_path"])).expanduser()
        query = str(params["query"])
        max_chars = int(params.get("max_chars", self._settings.max_pdf_chars))

        if not file_path.exists():
            return f"PDF file not found: {file_path}"

        reader = PdfReader(str(file_path))
        page_text = []
        for index, page in enumerate(reader.pages, start=1):
            extracted = page.extract_text() or ""
            if extracted:
                page_text.append(f"--- Page {index} ---\n{extracted}")

        raw_text = "\n\n".join(page_text)
        if not raw_text.strip():
            return "No extractable text was found in the PDF."

        raw_data = raw_text[:max_chars]
        return await self._gemini.process_data(raw_data, query)
