namespace MCPWebApp.Services;

public interface IMCPClientService
{
    Task<string> SendRequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<List<object>> ListToolsAsync(CancellationToken cancellationToken = default);
}
