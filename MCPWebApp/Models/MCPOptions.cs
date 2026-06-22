namespace MCPWebApp.Models;

public sealed class MCPOptions
{
    public string PythonPath { get; set; } = "python";

    public string ScriptPath { get; set; } = "../mcp_gemini_server/main.py";

    public int RequestTimeoutSeconds { get; set; } = 60;
}
