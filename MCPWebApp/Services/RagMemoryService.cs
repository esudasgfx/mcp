using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MCPWebApp.Data;
using MCPWebApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace MCPWebApp.Services;

public sealed class RagMemoryService : IRagMemoryService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;
    private readonly ILogger<RagMemoryService> _logger;

    public RagMemoryService(
        AppDbContext dbContext,
        IEmbeddingService embeddingService,
        IOptions<RagOptions> options,
        ILogger<RagMemoryService> logger)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("RAG semantic memory is disabled.");
            return;
        }

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS vector;",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PostgreSQL pgvector extension is unavailable. Semantic RAG memory is disabled until pgvector is installed.");
            return;
        }

        var dimensions = Math.Clamp(_options.EmbeddingDimensions, 1, 4096);
        var createMemoryTableSql =
            $"""
            CREATE TABLE IF NOT EXISTS memory_embeddings (
                id uuid PRIMARY KEY,
                source_type varchar(64) NOT NULL,
                source_id uuid NOT NULL,
                chat_session_id uuid NULL,
                tool_name varchar(128) NULL,
                content text NOT NULL,
                metadata_json jsonb NULL,
                content_hash varchar(64) NOT NULL,
                embedding vector({dimensions}) NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            """;
        await _dbContext.Database.ExecuteSqlRawAsync(createMemoryTableSql, cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS memory_embeddings_source_hash_idx
                ON memory_embeddings (source_type, source_id, content_hash);
            CREATE INDEX IF NOT EXISTS memory_embeddings_session_idx
                ON memory_embeddings (chat_session_id, created_at DESC);
            """,
            cancellationToken);

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS memory_embeddings_embedding_hnsw_idx
                    ON memory_embeddings USING hnsw (embedding vector_cosine_ops);
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to create pgvector HNSW index. Falling back to exact vector search without ANN index.");
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_embeddingService.IsConfigured)
        {
            return false;
        }

        try
        {
            var connection = _dbContext.Database.GetDbConnection();
            await EnsureOpenAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('public.memory_embeddings') IS NOT NULL;";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is bool available && available;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RAG semantic memory is not currently available.");
            return false;
        }
    }

    public async Task UpsertMemoryAsync(
        string sourceType,
        Guid sourceId,
        string content,
        Guid? chatSessionId = null,
        string? toolName = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            !await IsAvailableAsync(cancellationToken))
        {
            return;
        }

        float[] embedding;
        try
        {
            embedding = await _embeddingService.EmbedAsync(content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to generate embedding for {SourceType}:{SourceId}.", sourceType, sourceId);
            return;
        }

        if (embedding.Length != _options.EmbeddingDimensions)
        {
            _logger.LogWarning(
                "Embedding dimension mismatch for {SourceType}:{SourceId}. Expected {Expected}, got {Actual}.",
                sourceType,
                sourceId,
                _options.EmbeddingDimensions,
                embedding.Length);
            return;
        }

        var vectorLiteral = ToVectorLiteral(embedding);
        var contentHash = ComputeSha256(content);
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO memory_embeddings
                (id, source_type, source_id, chat_session_id, tool_name, content, metadata_json, content_hash, embedding, created_at, updated_at)
            VALUES
                (@id, @sourceType, @sourceId, @chatSessionId, @toolName, @content, @metadataJson, @contentHash, CAST(@embedding AS vector), now(), now())
            ON CONFLICT (source_type, source_id, content_hash)
            DO UPDATE SET
                tool_name = EXCLUDED.tool_name,
                content = EXCLUDED.content,
                metadata_json = EXCLUDED.metadata_json,
                embedding = EXCLUDED.embedding,
                updated_at = now();
            """;
        command.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        command.Parameters.Add(new NpgsqlParameter("sourceType", sourceType));
        command.Parameters.Add(new NpgsqlParameter("sourceId", sourceId));
        command.Parameters.Add(new NpgsqlParameter("chatSessionId", (object?)chatSessionId ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("toolName", (object?)toolName ?? DBNull.Value));
        command.Parameters.Add(new NpgsqlParameter("content", content));
        command.Parameters.Add(new NpgsqlParameter("metadataJson", NpgsqlDbType.Jsonb)
        {
            Value = (object?)metadataJson ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("contentHash", contentHash));
        command.Parameters.Add(new NpgsqlParameter("embedding", vectorLiteral));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<MemorySearchResultDto>> SearchAsync(
        string query,
        int? topK = null,
        double? minSimilarity = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            !await IsAvailableAsync(cancellationToken))
        {
            return [];
        }

        float[] embedding;
        try
        {
            embedding = await _embeddingService.EmbedAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to generate query embedding for RAG search.");
            return [];
        }

        if (embedding.Length != _options.EmbeddingDimensions)
        {
            _logger.LogWarning(
                "Query embedding dimension mismatch. Expected {Expected}, got {Actual}.",
                _options.EmbeddingDimensions,
                embedding.Length);
            return [];
        }

        var vectorLiteral = ToVectorLiteral(embedding);
        var boundedTopK = Math.Clamp(topK ?? _options.TopK, 1, 25);
        var threshold = minSimilarity ?? _options.MinSimilarity;
        var results = new List<MemorySearchResultDto>();
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                id,
                source_type,
                source_id,
                chat_session_id,
                tool_name,
                content,
                metadata_json::text,
                1 - (embedding <=> CAST(@embedding AS vector)) AS similarity,
                created_at
            FROM memory_embeddings
            WHERE 1 - (embedding <=> CAST(@embedding AS vector)) >= @minSimilarity
            ORDER BY embedding <=> CAST(@embedding AS vector)
            LIMIT @topK;
            """;
        command.Parameters.Add(new NpgsqlParameter("embedding", vectorLiteral));
        command.Parameters.Add(new NpgsqlParameter("minSimilarity", threshold));
        command.Parameters.Add(new NpgsqlParameter("topK", boundedTopK));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MemorySearchResultDto
            {
                Id = reader.GetGuid(0),
                SourceType = reader.GetString(1),
                SourceId = reader.GetGuid(2),
                ChatSessionId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                ToolName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Content = reader.GetString(5),
                MetadataJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                Similarity = reader.GetDouble(7),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(8)
            });
        }

        return results;
    }

    public async Task IndexConfigSettingAsync(
        ConfigSettingDto setting,
        CancellationToken cancellationToken = default)
    {
        var content = new StringBuilder()
            .Append("Configuration setting: ")
            .Append(setting.Category)
            .Append(':')
            .Append(setting.Key)
            .AppendLine()
            .Append("Effective value/reference: ")
            .Append(setting.EffectiveValue)
            .AppendLine()
            .Append("Description: ")
            .Append(setting.Description)
            .ToString();

        await UpsertMemoryAsync(
            "config_setting",
            setting.Id,
            content,
            metadataJson: System.Text.Json.JsonSerializer.Serialize(setting),
            cancellationToken: cancellationToken);
    }

    private static async Task EnsureOpenAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static string ToVectorLiteral(IEnumerable<float> embedding)
    {
        return "[" + string.Join(
            ",",
            embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
