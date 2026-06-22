# Build and package the ASP.NET Core web app plus the local Python MCP server.
# The Python server is intentionally included in the same container because
# MCP stdio transport requires the web app to spawn a local process.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MCPWebApp/MCPWebApp.csproj MCPWebApp/
RUN dotnet restore "MCPWebApp/MCPWebApp.csproj"

COPY . .
RUN dotnet publish "MCPWebApp/MCPWebApp.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        python3 \
        python3-venv \
    && rm -rf /var/lib/apt/lists/*

COPY mcp_gemini_server/requirements.txt /tmp/mcp-requirements.txt
RUN python3 -m venv /opt/mcp-python \
    && /opt/mcp-python/bin/python -m pip install --no-cache-dir --upgrade pip \
    && /opt/mcp-python/bin/python -m pip install --no-cache-dir -r /tmp/mcp-requirements.txt \
    && rm /tmp/mcp-requirements.txt

COPY --from=build /app/publish ./MCPWebApp/
COPY mcp_gemini_server ./mcp_gemini_server

WORKDIR /app/MCPWebApp

ENV ASPNETCORE_URLS=http://+:8080 \
    MCP__PythonPath=/opt/mcp-python/bin/python \
    MCP__ScriptPath=../mcp_gemini_server/main.py

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/api/chat/tools >/dev/null || exit 1

ENTRYPOINT ["dotnet", "MCPWebApp.dll"]
