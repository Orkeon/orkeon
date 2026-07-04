using Microsoft.Extensions.Options;
namespace Orkeon.Infrastructure.Security.Sinks;

using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Domain.Constants.Serialization;

/// <summary>
/// Audit sink that writes events as JSONL to daily rolling files via the virtual file system.
/// </summary>
public sealed class JsonFileAuditSink : IQueryableAuditSink, IDisposable
{
    private readonly IFileSystemService _fs;
    private readonly string _auditDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Initializes a new instance of <see cref="JsonFileAuditSink"/> using the virtual file system.</summary>
    /// <param name="fs">Virtual file system service.</param>
    /// <param name="auditVirtualDir">Virtual directory where audit JSONL files are written (e.g. <c>/logs/audit</c>).</param>
    public JsonFileAuditSink(IFileSystemService fs, string auditVirtualDir)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditVirtualDir);
        _fs = fs;
        _auditDirectory = auditVirtualDir;
    }

    /// <summary>
    /// Initializes a new instance using <see cref="AuditOptions"/> and the virtual file system.
    /// </summary>
    /// <param name="fs">Virtual file system service.</param>
    /// <param name="options">The audit options containing the virtual directory path.</param>
    public JsonFileAuditSink(IFileSystemService fs, IOptions<AuditOptions> options)
        : this(fs, (options ?? throw new ArgumentNullException(nameof(options))).Value.AuditDirectory)
    {
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        return WriteCoreAsync();

        async Task WriteCoreAsync()
        {
            await _fs.CreateDirectoryAsync(_auditDirectory, ct).ConfigureAwait(false);

            var fileName = GetFileName(auditEvent.Timestamp);
            var filePath = $"{_auditDirectory}/{fileName}";
            var json = JsonSerializer.Serialize(auditEvent, SerializerOptions);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _fs.AppendAllTextAsync(filePath, json + Environment.NewLine, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return QueryCoreAsync();

        async Task<IReadOnlyList<AuditEvent>> QueryCoreAsync()
        {
            if (!await _fs.ExistsAsync(_auditDirectory, ct).ConfigureAwait(false))
                return Array.Empty<AuditEvent>();

            var results = new List<AuditEvent>();
            var opts = new VirtualEnumerationOptions(Recursive: false, SearchPattern: "audit-*.jsonl");
            var files = new List<string>();

            await foreach (var entry in _fs.EnumerateFilesAsync(_auditDirectory, opts, ct).ConfigureAwait(false))
            {
                if (entry.Kind != VirtualEntryKind.File) continue;
                files.Add(entry.VirtualPath);
            }

            foreach (var filePath in files.OrderByDescending(f => f))
            {
                ct.ThrowIfCancellationRequested();

                var reachedLimit = await ProcessAuditFileAsync(filePath, query, results, ct).ConfigureAwait(false);
                if (reachedLimit)
                    return results;
            }

            return results;
        }
    }

    private async Task<bool> ProcessAuditFileAsync(
        string virtualPath, AuditQuery query, List<AuditEvent> results, CancellationToken ct)
    {
        var raw = await _fs.TryReadAllTextAsync(virtualPath, ct).ConfigureAwait(false);
        if (raw is null) return false;

        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<AuditEvent>(line, SerializerOptions);
            if (evt is null || !MatchesQuery(evt, query))
                continue;

            results.Add(evt);
            if (results.Count >= query.Limit)
                return true;
        }

        return false;
    }

    private static bool MatchesQuery(AuditEvent evt, AuditQuery query)
    {
        if (query.Category.HasValue && evt.Category != query.Category.Value)
            return false;

        if (query.CorrelationId is not null && evt.CorrelationId != query.CorrelationId)
            return false;

        if (query.AgentRole is not null && evt.AgentRole != query.AgentRole)
            return false;

        if (query.From.HasValue && evt.Timestamp < query.From.Value)
            return false;

        if (query.To.HasValue && evt.Timestamp > query.To.Value)
            return false;

        if (query.MinSeverity.HasValue && evt.Severity < query.MinSeverity.Value)
            return false;

        return true;
    }

    private static string GetFileName(DateTime timestamp)
    {
        return $"audit-{timestamp:yyyy-MM-dd}.jsonl";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _writeLock.Dispose();
    }
}
