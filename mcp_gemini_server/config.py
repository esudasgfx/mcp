"""Configuration for the standalone Gemini-backed MCP server."""

from __future__ import annotations

import os
from dataclasses import dataclass


@dataclass(frozen=True)
class Settings:
    """Runtime settings loaded from environment variables."""

    gemini_api_key: str
    gemini_model: str = "gemini-1.5-flash"
    gemini_temperature: float = 0.2
    max_pdf_chars: int = 5000


def load_settings() -> Settings:
    """Load settings and fail fast when required configuration is missing."""

    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        raise RuntimeError(
            "GEMINI_API_KEY is not set. Export your Google Gemini API key before "
            "starting the MCP server."
        )

    return Settings(
        gemini_api_key=api_key,
        gemini_model=os.getenv("GEMINI_MODEL", "gemini-1.5-flash"),
        gemini_temperature=float(os.getenv("GEMINI_TEMPERATURE", "0.2")),
        max_pdf_chars=int(os.getenv("MAX_PDF_CHARS", "5000")),
    )
