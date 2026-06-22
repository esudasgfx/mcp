"""Primavera P6 EPPM REST connector tool."""

from __future__ import annotations

import asyncio
import json
import os
from dataclasses import dataclass
from typing import Any
from urllib.parse import urljoin

import requests
from requests.auth import HTTPBasicAuth

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


@dataclass(frozen=True)
class P6ConnectionSettings:
    """Environment-driven P6 REST connection settings."""

    base_url: str | None
    auth_mode: str
    username: str | None
    password: str | None
    bearer_token: str | None
    project_endpoint: str
    activity_endpoint: str
    project_code_param: str
    activity_project_code_param: str
    filter_param: str
    project_filter_template: str | None
    activity_filter_template: str | None
    fields_param: str
    project_fields: str | None
    activity_fields: str | None
    timeout_seconds: float
    verify_ssl: bool


class P6Tool(BaseTool):
    """Fetch P6 project/activity data and ask Gemini to interpret it."""

    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "p6_tool"

    def get_description(self) -> str:
        return "Fetches Primavera P6 EPPM REST project/activity data and analyzes it."

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
                "include_activities": {
                    "type": "boolean",
                    "description": "Whether to fetch project activity records.",
                    "default": True,
                },
                "activity_limit": {
                    "type": "integer",
                    "description": "Maximum activity records to include in the Gemini context.",
                    "default": 100,
                    "minimum": 1,
                    "maximum": 1000,
                },
                "use_mock": {
                    "type": "boolean",
                    "description": "Force mock data instead of calling P6 REST.",
                    "default": False,
                },
            },
            "required": ["project_code", "query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        project_code = str(params["project_code"])
        query = str(params["query"])
        include_activities = bool(params.get("include_activities", True))
        activity_limit = int(params.get("activity_limit", 100))
        use_mock = bool(params.get("use_mock", False))

        settings = self._load_settings()
        if use_mock or not self._is_real_p6_configured(settings):
            raw_data = self._build_mock_data(project_code, reason="P6_BASE_URL is not configured.")
            return await self._gemini.process_data(json.dumps(raw_data, indent=2), query)

        try:
            p6_data = await asyncio.to_thread(
                self._fetch_p6_data,
                settings,
                project_code,
                include_activities,
                activity_limit,
            )
        except requests.HTTPError as exc:
            response_text = exc.response.text[:1000] if exc.response is not None else ""
            return (
                "P6 REST request failed. Check P6_BASE_URL, endpoint paths, "
                f"authentication, and project code. Details: {exc}. "
                f"Response body: {response_text}"
            )
        except requests.RequestException as exc:
            return (
                "Could not connect to P6 EPPM REST. Check network access, SSL, "
                f"and P6 connection settings. Details: {exc}"
            )
        except Exception as exc:
            return f"P6 tool failed before Gemini analysis: {exc}"

        return await self._gemini.process_data(json.dumps(p6_data, indent=2), query)

    def _fetch_p6_data(
        self,
        settings: P6ConnectionSettings,
        project_code: str,
        include_activities: bool,
        activity_limit: int,
    ) -> dict[str, Any]:
        session = requests.Session()
        session.verify = settings.verify_ssl
        self._configure_auth(session, settings)

        project_response = self._get_json(
            session,
            self._build_url(settings.base_url or "", settings.project_endpoint),
            self._build_query_params(
                project_code,
                settings.project_code_param,
                settings.filter_param,
                settings.project_filter_template,
                settings.fields_param,
                settings.project_fields,
            ),
            settings.timeout_seconds,
        )

        result: dict[str, Any] = {
            "source": "Primavera P6 EPPM REST",
            "project_code": project_code,
            "project_endpoint": settings.project_endpoint,
            "project": project_response,
        }

        if include_activities:
            activity_response = self._get_json(
                session,
                self._build_url(settings.base_url or "", settings.activity_endpoint),
                self._build_query_params(
                    project_code,
                    settings.activity_project_code_param,
                    settings.filter_param,
                    settings.activity_filter_template,
                    settings.fields_param,
                    settings.activity_fields,
                ),
                settings.timeout_seconds,
            )
            result["activity_endpoint"] = settings.activity_endpoint
            result["activities"] = self._limit_records(activity_response, activity_limit)
            result["activity_limit"] = activity_limit

        return result

    def _get_json(
        self,
        session: requests.Session,
        url: str,
        params: dict[str, str],
        timeout_seconds: float,
    ) -> Any:
        response = session.get(url, params=params, timeout=timeout_seconds)
        response.raise_for_status()
        if not response.text.strip():
            return {}

        content_type = response.headers.get("content-type", "")
        if "json" in content_type.lower():
            return response.json()

        try:
            return response.json()
        except ValueError:
            return {"raw_response": response.text}

    def _configure_auth(
        self,
        session: requests.Session,
        settings: P6ConnectionSettings,
    ) -> None:
        if settings.auth_mode == "basic":
            if not settings.username or not settings.password:
                raise ValueError("P6_AUTH_MODE=basic requires P6_USERNAME and P6_PASSWORD.")
            session.auth = HTTPBasicAuth(settings.username, settings.password)
            return

        if settings.auth_mode == "bearer":
            if not settings.bearer_token:
                raise ValueError("P6_AUTH_MODE=bearer requires P6_ACCESS_TOKEN or P6_TOKEN.")
            session.headers.update({"Authorization": f"Bearer {settings.bearer_token}"})
            return

        if settings.auth_mode != "none":
            raise ValueError("P6_AUTH_MODE must be one of: basic, bearer, none.")

    def _build_query_params(
        self,
        project_code: str,
        code_param: str,
        filter_param: str,
        filter_template: str | None,
        fields_param: str,
        fields: str | None,
    ) -> dict[str, str]:
        params: dict[str, str] = {}
        if filter_template:
            params[filter_param] = filter_template.format(project_code=project_code)
        else:
            params[code_param] = project_code

        if fields:
            params[fields_param] = fields

        return params

    def _load_settings(self) -> P6ConnectionSettings:
        return P6ConnectionSettings(
            base_url=os.getenv("P6_BASE_URL"),
            auth_mode=os.getenv("P6_AUTH_MODE", "basic").strip().lower(),
            username=os.getenv("P6_USERNAME"),
            password=os.getenv("P6_PASSWORD"),
            bearer_token=os.getenv("P6_ACCESS_TOKEN") or os.getenv("P6_TOKEN"),
            project_endpoint=os.getenv("P6_PROJECT_ENDPOINT", "project"),
            activity_endpoint=os.getenv("P6_ACTIVITY_ENDPOINT", "activity"),
            project_code_param=os.getenv("P6_PROJECT_CODE_PARAM", "projectCode"),
            activity_project_code_param=os.getenv(
                "P6_ACTIVITY_PROJECT_CODE_PARAM",
                "projectCode",
            ),
            filter_param=os.getenv("P6_FILTER_PARAM", "Filter"),
            project_filter_template=os.getenv("P6_PROJECT_FILTER_TEMPLATE"),
            activity_filter_template=os.getenv("P6_ACTIVITY_FILTER_TEMPLATE"),
            fields_param=os.getenv("P6_FIELDS_PARAM", "Fields"),
            project_fields=os.getenv("P6_PROJECT_FIELDS"),
            activity_fields=os.getenv("P6_ACTIVITY_FIELDS"),
            timeout_seconds=float(os.getenv("P6_TIMEOUT_SECONDS", "30")),
            verify_ssl=os.getenv("P6_VERIFY_SSL", "true").strip().lower()
            not in {"0", "false", "no"},
        )

    def _build_url(self, base_url: str, endpoint: str) -> str:
        return urljoin(f"{base_url.rstrip('/')}/", endpoint.lstrip("/"))

    def _is_real_p6_configured(self, settings: P6ConnectionSettings) -> bool:
        return bool(
            settings.base_url
            and not settings.base_url.startswith("https://your-p6-host")
        )

    def _limit_records(self, payload: Any, limit: int) -> Any:
        bounded_limit = max(1, min(limit, 1000))
        if isinstance(payload, list):
            return payload[:bounded_limit]

        if isinstance(payload, dict):
            for key in ("items", "data", "activities", "results"):
                value = payload.get(key)
                if isinstance(value, list):
                    cloned = dict(payload)
                    cloned[key] = value[:bounded_limit]
                    cloned["returned_count"] = len(cloned[key])
                    return cloned

        return payload

    def _build_mock_data(self, project_code: str, reason: str) -> dict[str, Any]:
        return {
            "source": "Primavera P6 EPPM",
            "mode": "mock",
            "reason": reason,
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
