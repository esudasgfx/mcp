using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPWebApp.Models;
using Microsoft.Extensions.Options;

namespace MCPWebApp.Services;

public sealed class MCPClientService : IMCPClientService, IHostedService, IDisposable
{
    private readonly ILogger<MCPClientService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly MCPOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private Process? _process;
    private CancellationTokenSource? _readerCancellation;
    private bool _initialized;
    private bool _disposed;

    public MCPClientService(
        ILogger<MCPClientService> logger,
        IWebHostEnvironment environment,
        IOptions<MCPOptions> options)
    {
        _logger = logger;
        _environment = environment;
        _options = options.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureProcessStartedAsync(cancellationToken);
        await EnsureInitializedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopProcess();
        return Task.CompletedTask;
    }

    public async Task<string> SendRequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        return await SendRawRequestAsync(method, parameters, cancellationToken);
    }

    public async Task<List<object>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var responseJson = await SendRequestAsync("tools/list", null, cancellationToken);
        using var document = JsonDocument.Parse(responseJson);

        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(FormatJsonRpcError(error));
        }

        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("tools", out var toolsElement))
        {
            return [];
        }

        return toolsElement
            .EnumerateArray()
            .Select(tool => JsonSerializer.Deserialize<object>(tool.GetRawText(), _jsonOptions)!)
            .ToList();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && IsProcessRunning())
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && IsProcessRunning())
            {
                return;
            }

            await EnsureProcessStartedAsync(cancellationToken);

            var initializeParams = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "MCPWebApp",
                    version = "1.0.0"
                }
            };

            _logger.LogInformation("Sending MCP initialize request.");
            var initializeResponse = await SendRawRequestAsync(
                "initialize",
                initializeParams,
                cancellationToken);
            using var document = JsonDocument.Parse(initializeResponse);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(FormatJsonRpcError(error));
            }

            await SendNotificationAsync(
                "notifications/initialized",
                new { },
                cancellationToken);
            _initialized = true;
            _logger.LogInformation("MCP initialize handshake completed.");
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<string> SendRawRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await EnsureProcessStartedAsync(cancellationToken);

        var id = Guid.NewGuid().ToString("N");
        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = parameters
        };
        var payload = JsonSerializer.Serialize(request, _jsonOptions);
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Duplicate JSON-RPC request id generated: {id}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        using var registration = timeout.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(id, out var pending))
            {
                pending.TrySetException(new TimeoutException(
                    $"MCP request '{method}' timed out after {_options.RequestTimeoutSeconds} seconds."));
            }
        });

        try
        {
            await WriteLineAsync(payload, timeout.Token);
            _logger.LogDebug("MCP request {RequestId}: {Payload}", id, payload);
            return await completion.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await EnsureProcessStartedAsync(cancellationToken);

        var notification = new JsonRpcRequest
        {
            Method = method,
            Params = parameters
        };
        var payload = JsonSerializer.Serialize(notification, _jsonOptions);
        await WriteLineAsync(payload, cancellationToken);
        _logger.LogDebug("MCP notification: {Payload}", payload);
    }

    private async Task WriteLineAsync(string payload, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process?.StandardInput is null || process.HasExited)
        {
            throw new InvalidOperationException("The Python MCP server process is not running.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // MCP stdio transport expects one JSON-RPC message per line. ASP.NET
            // writes to the Python process StandardInput while the reader loop below
            // continuously drains StandardOutput to match responses by JSON-RPC id.
            await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task EnsureProcessStartedAsync(CancellationToken cancellationToken)
    {
        if (IsProcessRunning())
        {
            return;
        }

        await _processLock.WaitAsync(cancellationToken);
        try
        {
            if (IsProcessRunning())
            {
                return;
            }

            StopProcess();
            var scriptPath = ResolveScriptPath();
            if (!File.Exists(scriptPath))
            {
                _logger.LogError("Python MCP server script does not exist: {ScriptPath}", scriptPath);
                throw new FileNotFoundException("Python MCP server script was not found.", scriptPath);
            }

            _readerCancellation = new CancellationTokenSource();
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.PythonPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!
            };
            startInfo.ArgumentList.Add(scriptPath);

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.Exited += (_, _) =>
            {
                _logger.LogCritical(
                    "Python MCP server exited unexpectedly with code {ExitCode}.",
                    SafeExitCode(process));
                _initialized = false;
                FailPendingRequests("Python MCP server exited before responding.");
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start the Python MCP server process.");
            }

            _process = process;
            _initialized = false;
            _ = Task.Run(() => ReadStdOutLoopAsync(process, _readerCancellation.Token));
            _ = Task.Run(() => ReadStdErrLoopAsync(process, _readerCancellation.Token));
            _logger.LogInformation(
                "Started Python MCP server process {ProcessId}: {PythonPath} {ScriptPath}",
                process.Id,
                _options.PythonPath,
                scriptPath);
        }
        finally
        {
            _processLock.Release();
        }
    }

    private async Task ReadStdOutLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                _logger.LogDebug("MCP response: {Line}", line);
                DispatchResponse(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reading Python MCP server stdout.");
            FailPendingRequests("Failed while reading the Python MCP server response stream.");
        }
    }

    private async Task ReadStdErrLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                _logger.LogWarning("Python MCP stderr: {Line}", line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reading Python MCP server stderr.");
        }
    }

    private void DispatchResponse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var idElement))
            {
                _logger.LogDebug("Ignoring MCP notification without id: {Line}", line);
                return;
            }

            var id = idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetRawText(),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Ignoring MCP response with blank id: {Line}", line);
                return;
            }

            if (_pendingRequests.TryRemove(id, out var completion))
            {
                completion.TrySetResult(line);
            }
            else
            {
                _logger.LogWarning("No pending MCP request found for response id {Id}.", id);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON received from MCP server stdout: {Line}", line);
        }
    }

    private string ResolveScriptPath()
    {
        if (Path.IsPathRooted(_options.ScriptPath))
        {
            return Path.GetFullPath(_options.ScriptPath);
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.ScriptPath));
    }

    private bool IsProcessRunning()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatJsonRpcError(JsonElement error)
    {
        if (error.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? error.GetRawText();
        }

        return error.GetRawText();
    }

    private void FailPendingRequests(string message)
    {
        foreach (var pair in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(pair.Key, out var pending))
            {
                pending.TrySetException(new InvalidOperationException(message));
            }
        }
    }

    private void StopProcess()
    {
        _readerCancellation?.Cancel();
        _readerCancellation?.Dispose();
        _readerCancellation = null;
        _initialized = false;

        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping Python MCP server process.");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            FailPendingRequests("Python MCP server was stopped.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopProcess();
        _processLock.Dispose();
        _initializeLock.Dispose();
        _writeLock.Dispose();
        _disposed = true;
    }
}
