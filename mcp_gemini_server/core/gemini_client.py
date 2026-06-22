"""Gemini API wrapper used by MCP tools."""

from __future__ import annotations

import asyncio
import threading
from typing import Optional

import google.generativeai as genai

from config import Settings, load_settings


class GeminiClient:
    """Singleton wrapper around the Google Gemini GenerativeModel client."""

    _instance: Optional["GeminiClient"] = None
    _lock = threading.Lock()

    def __new__(cls, settings: Settings | None = None) -> "GeminiClient":
        if cls._instance is None:
            with cls._lock:
                if cls._instance is None:
                    cls._instance = super().__new__(cls)
                    cls._instance._initialized = False
        return cls._instance

    def __init__(self, settings: Settings | None = None) -> None:
        if self._initialized:
            return

        self._settings = settings or load_settings()
        genai.configure(api_key=self._settings.gemini_api_key)
        self._model = genai.GenerativeModel(
            model_name=self._settings.gemini_model,
            generation_config={
                "temperature": self._settings.gemini_temperature,
            },
        )
        self._initialized = True

    async def process_data(self, raw_data: str, user_prompt: str) -> str:
        """Send enterprise data and a user prompt to Gemini for analysis."""

        prompt = (
            "You are an enterprise data analyst. Analyze the data below and "
            "answer the user's request clearly, concisely, and with practical "
            "business insight.\n\n"
            f"User request:\n{user_prompt}\n\n"
            f"Enterprise data:\n{raw_data}"
        )

        response = await asyncio.to_thread(self._model.generate_content, prompt)
        text = getattr(response, "text", None)
        if text:
            return text.strip()

        return "Gemini returned an empty response for the supplied data."
