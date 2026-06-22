# MCP Gemini Enterprise Server and ASP.NET Client

This repository contains a standalone Python Model Context Protocol (MCP) server
and an ASP.NET Core 8 web client.

The Python server exposes enterprise tools over MCP JSON-RPC on stdio. The .NET
web app starts that Python process, sends MCP requests over redirected
stdin/stdout, persists chat/config data in PostgreSQL, and optionally uses
Gemini embeddings plus PostgreSQL `pgvector` for context-based RAG memory.

## Current capabilities

- Standalone Python MCP server runnable with `python main.py`.
- MCP stdio transport only: no dependency on Cursor, Claude Desktop, VS Code, or
  any AI IDE.
- Gemini-powered processing via `google-generativeai`.
- Enterprise tool modules:
  - Excel workbook analysis.
  - PDF text extraction and summarization.
  - Primavera P6 EPPM REST connector.
  - Autodesk ACC mock connector.
  - SAP mock connector.
  - EAM mock connector.
  - Unifier mock connector.
- ASP.NET Core 8 Razor/API web client.
- PostgreSQL-backed configuration defaults and overrides.
- Chat sessions and chat history persistence.
- Config-change audit messages stored as conversation memory.
- Context-based RAG memory with Gemini embeddings and PostgreSQL `pgvector`.
- RAG latency optimizations:
  - bounded retrieval timeout,
  - embedding cache,
  - embedding failure cooldown,
  - background indexing queue.

## Repository layout

```text
.
|-- mcp_gemini_server/
|   |-- main.py
|   |-- config.py
|   |-- requirements.txt
|   |-- test_client.py
|   |-- core/
|   |   |-- base_tool.py
|   |   `-- gemini_client.py
|   `-- tools/
|       |-- excel_tool.py
|       |-- pdf_tool.py
|       |-- p6_tool.py
|       |-- sap_tool.py
|       |-- eam_tool.py
|       |-- acc_tool.py
|       `-- unifier_tool.py
`-- MCPWebApp/
    |-- Program.cs
    |-- appsettings.json
    |-- Controllers/Api/
    |   |-- ChatController.cs
    |   `-- ConfigController.cs
    |-- Data/
    |   `-- AppDbContext.cs
    |-- Models/
    |-- Pages/Chat/
    `-- Services/
```

## High-level architecture

```text
Browser
  |
  | HTTP / Razor / fetch()
  v
ASP.NET Core 8 Web App
  |
  | System.Diagnostics.Process
  | JSON-RPC 2.0 over stdin/stdout
  v
Python MCP Server
  |
  | Tool execution
  v
Enterprise systems / files
  |
  | raw data
  v
Gemini
  |
  | response
  v
ASP.NET UI + PostgreSQL memory
```

### Python MCP server

The Python server is located in `mcp_gemini_server/`.

It:

1. Loads `GEMINI_API_KEY` from environment.
2. Dynamically scans `tools/`.
3. Instantiates every class inheriting from `BaseTool`.
4. Registers MCP `tools/list` and `tools/call`.
5. Runs stdio JSON-RPC transport.

The entry point is:

```bash
python mcp_gemini_server/main.py
```

Important: stdout is reserved for MCP protocol messages. Diagnostics are logged
to stderr.

### ASP.NET Core client

The web app is located in `MCPWebApp/`.

It:

1. Starts the Python MCP server process.
2. Redirects stdin, stdout, and stderr.
3. Sends JSON-RPC requests to stdin.
4. Reads stdout asynchronously.
5. Matches JSON-RPC responses by `id`.
6. Exposes REST APIs and a Razor chat UI.
7. Persists chat/config/RAG data in PostgreSQL.

Main UI:

```text
/Chat
```

Main APIs:

```text
GET  /api/chat/tools
POST /api/chat/send
GET  /api/chat/history
GET  /api/chat/history/{sessionId}
POST /api/chat/memory/search
GET  /api/config
POST /api/config/override
```

## Prerequisites

### Required

- Python 3.10 or newer.
- .NET 8 SDK.
- Google Gemini API key.
- PostgreSQL for the ASP.NET app persistence layer.

### Optional but recommended

- PostgreSQL `pgvector` extension for semantic RAG memory.

On Ubuntu/PostgreSQL 16:

```bash
sudo apt-get install postgresql-16-pgvector
sudo -u postgres psql -d mcp_gemini -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

Creating the extension usually requires a PostgreSQL superuser. The app user can
use the vector table after the extension is installed.

## Python server setup

From repository root:

```bash
python3 -m pip install --user -r mcp_gemini_server/requirements.txt
```

Required environment:

```bash
export GEMINI_API_KEY="your-gemini-api-key"
export GEMINI_MODEL="gemini-2.5-flash"
```

Run the MCP server directly:

```bash
python3 mcp_gemini_server/main.py
```

The server will wait for MCP JSON-RPC messages on stdin.

### Test the Python MCP server without an IDE

```bash
GEMINI_API_KEY=dummy python3 mcp_gemini_server/test_client.py --python python3
```

This validates:

- MCP initialize handshake,
- `tools/list`,
- dynamic tool discovery.

It does not call Gemini unless you pass `--tool`.

Example tool call:

```bash
GEMINI_API_KEY="real-key" python3 mcp_gemini_server/test_client.py \
  --python python3 \
  --tool p6_tool \
  --arguments '{"project_code":"PRJ-001","query":"Summarize schedule risk","use_mock":true}'
```

## ASP.NET web app setup

From repository root:

```bash
dotnet restore MCPWebApp/MCPWebApp.csproj
dotnet build MCPWebApp/MCPWebApp.csproj
```

Set environment variables so the ASP.NET process and its Python child process
can access them:

```bash
export GEMINI_API_KEY="your-gemini-api-key"
export GEMINI_MODEL="gemini-2.5-flash"
```

Run:

```bash
dotnet run --project MCPWebApp/MCPWebApp.csproj
```

Open:

```text
https://localhost:<port>/Chat
```

For local non-HTTPS testing you can bind HTTP:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5080 \
GEMINI_API_KEY="your-gemini-api-key" \
dotnet run --project MCPWebApp/MCPWebApp.csproj
```

## PostgreSQL configuration

`MCPWebApp/appsettings.json` contains:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=mcp_gemini;Username=mcp_user;Password=change-me"
}
```

Example local database setup:

```bash
sudo service postgresql start
sudo -u postgres psql -c "CREATE ROLE mcp_user LOGIN PASSWORD 'change-me';"
sudo -u postgres createdb -O mcp_user mcp_gemini
sudo -u postgres psql -d mcp_gemini -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

The app uses `Database.EnsureCreated()` at startup when this setting is true:

```json
"Database": {
  "InitializeOnStartup": true
}
```

For enterprise deployment, replace startup schema creation with EF migrations or
a DBA-managed database release process.

## Database model

The app currently creates and uses:

### `config_settings`

Stores defaults and user overrides.

Conceptual fields:

- category,
- key,
- default value,
- override value,
- secret flag,
- secret reference,
- updated by,
- timestamps.

Secret values are not stored directly. For example, Gemini key is stored as a
reference to `GEMINI_API_KEY`, not as the raw key.

### `chat_sessions`

Stores conversation sessions.

### `chat_messages`

Stores:

- user requests,
- assistant/tool responses,
- config-change events,
- parameters JSON,
- success/error state,
- duration.

### `memory_embeddings`

Created manually by `RagMemoryService` because it uses PostgreSQL `vector`.

Stores:

- source type,
- source id,
- chat session id,
- tool name,
- text content,
- metadata JSON,
- content hash,
- embedding vector,
- timestamps.

## Configuration system

Configuration is layered:

1. `appsettings.json` provides default values.
2. `ConfigStoreService` seeds those defaults into PostgreSQL.
3. Users can save overrides through:

```http
POST /api/config/override
```

4. `MCPClientService` reads effective DB config and passes non-secret values to
   the Python process as environment variables.
5. Secret values remain in environment variables or a secret manager.

Example config override request:

```json
{
  "category": "P6",
  "key": "BaseUrl",
  "overrideValue": "https://primavera.iges.in:18206/p6ws/restapi",
  "reason": "Set production P6 REST endpoint"
}
```

After config changes, the ASP.NET app restarts the Python MCP process so new
environment values are applied.

## Gemini configuration

Required:

```bash
export GEMINI_API_KEY="your-key"
```

Recommended default model:

```bash
export GEMINI_MODEL="gemini-2.5-flash"
```

Higher reasoning model:

```bash
export GEMINI_MODEL="gemini-2.5-pro"
```

Low-cost/high-throughput model:

```bash
export GEMINI_MODEL="gemini-2.5-flash-lite"
```

The Python code currently uses `google-generativeai` because it was requested in
the original specification. That package may emit a deprecation warning. A future
developer can migrate `core/gemini_client.py` and
`MCPWebApp/Services/GeminiEmbeddingService.cs` to Google's newer SDK/API when
desired.

## RAG memory

RAG is configured in `appsettings.json`:

```json
"RAG": {
  "Enabled": true,
  "EmbeddingModel": "text-embedding-004",
  "EmbeddingDimensions": 768,
  "TopK": 5,
  "MinSimilarity": 0.72,
  "MaxContextChars": 6000,
  "SearchTimeoutMs": 1500,
  "EmbeddingCacheMinutes": 30,
  "EmbeddingFailureCooldownSeconds": 60,
  "IndexQueueCapacity": 1000,
  "GeminiApiBaseUrl": "https://generativelanguage.googleapis.com"
}
```

Request flow with RAG:

```text
User query
  -> bounded embedding/search, max SearchTimeoutMs
  -> retrieve top-K memories from pgvector
  -> inject memory into MCP tool query
  -> call Python MCP server
  -> return response
  -> queue new memory embedding in background
```

The response path does not wait for post-response embedding/indexing.

If `GEMINI_API_KEY` is missing or invalid, RAG fails soft and the app continues
without semantic memory.

Manual memory search:

```http
POST /api/chat/memory/search
Content-Type: application/json

{
  "query": "what P6 endpoint did we configure?",
  "topK": 3
}
```

## P6 EPPM connector

The P6 connector is in:

```text
mcp_gemini_server/tools/p6_tool.py
```

It was verified against:

```text
https://primavera.iges.in:18206/p6ws/restapi/openapi.json
```

Swagger summary:

- OpenAPI version: `3.0.1`
- Title: `REST API for Oracle Primavera P6`
- Path count: `439`

Major endpoints implemented by the connector:

```text
/project
/activity
/wbs
/relationship
/resourceAssignment
```

The Swagger shows `Fields` is required and `Filter` is optional for these GET
endpoints. The connector therefore uses `Fields` plus `Filter` templates, not a
simple `projectCode` query parameter.

Default P6 settings:

```json
"P6": {
  "BaseUrl": "https://primavera.iges.in:18206/p6ws/restapi",
  "AuthMode": "basic",
  "ProjectEndpoint": "project",
  "ActivityEndpoint": "activity",
  "WbsEndpoint": "wbs",
  "RelationshipEndpoint": "relationship",
  "ResourceAssignmentEndpoint": "resourceAssignment",
  "FilterParam": "Filter",
  "FieldsParam": "Fields",
  "ProjectFilterTemplate": "Id:eq:{project_code}",
  "ActivityFilterTemplate": "ProjectId:eq:{project_code}",
  "WbsFilterTemplate": "ProjectId:eq:{project_code}",
  "RelationshipFilterTemplate": "PredecessorProjectId:eq:{project_code}:or:SuccessorProjectId:eq:{project_code}",
  "ResourceAssignmentFilterTemplate": "ProjectId:eq:{project_code}",
  "TimeoutSeconds": 30,
  "VerifySsl": true
}
```

### P6 authentication

Basic auth:

```bash
export P6_AUTH_MODE="basic"
export P6_USERNAME="your-p6-user"
export P6_PASSWORD="your-p6-password"
```

Bearer token:

```bash
export P6_AUTH_MODE="bearer"
export P6_ACCESS_TOKEN="token"
```

P6 `AuthToken` header:

```bash
export P6_AUTH_MODE="auth_token"
export P6_AUTH_TOKEN="token"
export P6_TOKEN_HEADER="AuthToken"
```

No auth, for a gateway that handles auth externally:

```bash
export P6_AUTH_MODE="none"
```

### Example P6 tool request

```json
{
  "toolName": "p6_tool",
  "parameters": {
    "project_code": "YOUR_PROJECT_ID",
    "query": "Analyze schedule risks, critical activities, float, and resource bottlenecks",
    "include_activities": true,
    "include_wbs": true,
    "include_relationships": true,
    "include_resource_assignments": true,
    "activity_limit": 100,
    "related_record_limit": 100
  }
}
```

### What still depends on credentials

The connector is aligned to the Swagger paths and query shapes. A full live data
test still requires valid P6 credentials or token. Without credentials, the
server correctly returns `401` for protected endpoints.

## Tool reference

### Excel tool

Name:

```text
excel_tool
```

Parameters:

```json
{
  "file_path": "/path/to/workbook.xlsx",
  "query": "Summarize this workbook",
  "worksheet": "Sheet1",
  "max_rows": 200
}
```

### PDF tool

Name:

```text
pdf_tool
```

Parameters:

```json
{
  "file_path": "/path/to/document.pdf",
  "query": "Summarize the key risks",
  "max_chars": 5000
}
```

### ACC tool

Name:

```text
acc_tool
```

Currently mocked. Parameters:

```json
{
  "project_id": "ACC-PROJECT-ID",
  "query": "Summarize open issues"
}
```

### SAP tool

Name:

```text
sap_tool
```

Currently mocked. Parameters:

```json
{
  "system_id": "S4HANA-PRD",
  "entity": "purchase_orders",
  "query": "Summarize delayed purchase orders"
}
```

### EAM tool

Name:

```text
eam_tool
```

Currently mocked. Parameters:

```json
{
  "asset_id": "ASSET-001",
  "query": "Summarize maintenance risk"
}
```

### Unifier tool

Name:

```text
unifier_tool
```

Currently mocked. Parameters:

```json
{
  "shell_number": "SHELL-001",
  "query": "Summarize cost risk"
}
```

## Adding a new Python MCP tool

1. Create a new file under:

```text
mcp_gemini_server/tools/
```

2. Implement `BaseTool`:

```python
from typing import Any

from core.base_tool import BaseTool
from core.gemini_client import GeminiClient


class MyTool(BaseTool):
    def __init__(self) -> None:
        self._gemini = GeminiClient()

    def get_name(self) -> str:
        return "my_tool"

    def get_description(self) -> str:
        return "Describe what this tool does."

    def get_input_schema(self) -> dict[str, Any]:
        return {
            "type": "object",
            "properties": {
                "query": {"type": "string"}
            },
            "required": ["query"],
            "additionalProperties": False,
        }

    async def execute(self, params: dict[str, Any]) -> str:
        raw_data = "data fetched from enterprise system"
        return await self._gemini.process_data(raw_data, params["query"])
```

3. Restart the Python server or ASP.NET web app.

The server scans `tools/` dynamically. No registration change is required in
`main.py`.

## JSON-RPC / MCP behavior

The ASP.NET service sends messages like:

```json
{
  "jsonrpc": "2.0",
  "id": "generated-guid",
  "method": "tools/call",
  "params": {
    "name": "p6_tool",
    "arguments": {
      "project_code": "PRJ-001",
      "query": "Summarize risk"
    }
  }
}
```

The Python server returns MCP tool content in the JSON-RPC result.

`MCPClientService` maintains:

- one Python child process,
- async stdout reader loop,
- async stderr reader loop,
- pending request dictionary keyed by JSON-RPC `id`,
- restart support after config changes.

## Development validation checklist

Run Python syntax validation:

```bash
python3 -m compileall mcp_gemini_server
```

Run MCP stdio handshake/tool listing:

```bash
GEMINI_API_KEY=dummy python3 mcp_gemini_server/test_client.py --python python3
```

Build ASP.NET:

```bash
dotnet build MCPWebApp/MCPWebApp.csproj
```

Run web app:

```bash
ASPNETCORE_URLS=http://127.0.0.1:5080 \
GEMINI_API_KEY=dummy \
dotnet run --no-build --project MCPWebApp/MCPWebApp.csproj
```

Check tools endpoint:

```bash
curl -sS http://127.0.0.1:5080/api/chat/tools
```

Check memory search endpoint:

```bash
curl -sS -X POST http://127.0.0.1:5080/api/chat/memory/search \
  -H "Content-Type: application/json" \
  -d '{"query":"p6 endpoint","topK":3}'
```

## Production notes

- Do not store raw passwords, API keys, or access tokens in source control.
- Prefer environment variables, Azure Key Vault, AWS Secrets Manager, or an
  equivalent secret manager.
- Set `GEMINI_API_KEY` in the ASP.NET hosting environment so the Python child
  process inherits it.
- Install and enable PostgreSQL `pgvector` before enabling RAG in production.
- Use HTTPS and authentication/authorization on the ASP.NET web app before
  exposing config APIs.
- Restrict file paths for Excel/PDF tools if users are untrusted.
- Consider adding role-based access control for config changes.
- Replace `EnsureCreated()` with EF migrations for controlled deployments.
- Add retry/circuit-breaker policies for real enterprise REST connectors.
- Move mocked ACC/SAP/EAM/Unifier tools to real API connectors when credentials
  and endpoint specs are available.

## Troubleshooting

### `GEMINI_API_KEY is not set`

Set:

```bash
export GEMINI_API_KEY="your-key"
```

Then restart the web app or Python server.

### Python executable not found

Default config uses:

```json
"PythonPath": "python"
```

`MCPClientService` falls back to `python3` if `python` is unavailable. You can
also override this in config:

```json
{
  "category": "MCP",
  "key": "PythonPath",
  "overrideValue": "python3"
}
```

### PostgreSQL extension vector is unavailable

Install pgvector and create the extension:

```bash
sudo apt-get install postgresql-16-pgvector
sudo -u postgres psql -d mcp_gemini -c "CREATE EXTENSION IF NOT EXISTS vector;"
```

### RAG memory returns empty results

Check:

- `GEMINI_API_KEY` is valid.
- `RAG.Enabled` is true.
- `memory_embeddings` table exists.
- Background indexing worker has had time to process jobs.
- Similarity threshold is not too high.

Lower threshold for testing:

```json
"MinSimilarity": 0.5
```

### P6 returns 401

The endpoint shape is likely correct, but credentials are missing or invalid.
Configure one of:

```bash
P6_AUTH_MODE=basic
P6_USERNAME=...
P6_PASSWORD=...
```

or:

```bash
P6_AUTH_MODE=auth_token
P6_AUTH_TOKEN=...
```

### P6 returns 400

Usually means the `Fields` or `Filter` string does not match your P6 version.
Compare with:

```text
https://primavera.iges.in:18206/p6ws/restapi/openapi.json
```

Then override the relevant config:

- `P6:ProjectFields`
- `P6:ActivityFields`
- `P6:ProjectFilterTemplate`
- `P6:ActivityFilterTemplate`

## Current implementation status

Implemented:

- Python MCP stdio server.
- ASP.NET Core client.
- PostgreSQL config and chat memory.
- pgvector RAG memory.
- P6 REST connector aligned to provided Swagger.

Partially implemented / placeholders:

- ACC connector is mocked.
- SAP connector is mocked.
- EAM connector is mocked.
- Unifier connector is mocked.

Recommended next work:

1. Add authentication to the ASP.NET web app.
2. Convert startup DB creation to EF migrations.
3. Implement real ACC OAuth + APS APIs.
4. Implement real Unifier REST calls.
5. Implement SAP OData/RFC connector.
6. Add integration tests with sandbox credentials.
7. Add Docker Compose for PostgreSQL + pgvector + web app.
