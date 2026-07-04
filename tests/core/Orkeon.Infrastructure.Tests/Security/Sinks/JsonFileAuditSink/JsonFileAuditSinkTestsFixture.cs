using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Security.Sinks;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Security.Sinks;

public sealed class JsonFileAuditSinkTestsFixture : IDisposable
{
    private readonly string _physicalDir;
    private readonly DiskBackedFileSystemService _fs;
    private readonly JsonFileAuditSink _sink;

    private const string VirtualAuditDir = "/logs/audit";

    public JsonFileAuditSinkTestsFixture()
    {
        _physicalDir = Path.Combine(Path.GetTempPath(), "audit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_physicalDir);
        _fs = new DiskBackedFileSystemService(_physicalDir, VirtualAuditDir);
        _sink = new JsonFileAuditSink(_fs, VirtualAuditDir);
    }

    // --- Execution ---

    public async Task WriteAsync(AuditEvent evt)
        => await _sink.WriteAsync(evt);

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery? query = null)
        => await _sink.QueryAsync(query ?? new AuditQuery());

    // --- Factory ---

    public static AuditEvent CreateEvent(
        AuditCategory category = AuditCategory.LlmCall,
        string correlationId = "test-corr",
        string? agentRole = null,
        DateTime? timestamp = null) => new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = timestamp ?? DateTime.UtcNow,
            Category = category,
            Action = "Test action",
            Outcome = AuditOutcome.Success,
            CorrelationId = correlationId,
            AgentRole = agentRole,
            Severity = AuditSeverity.Info
        };

    // --- Helper for empty-dir tests ---

    public static JsonFileAuditSink CreateEmptySink()
    {
        var emptyPhysical = Path.Combine(Path.GetTempPath(), "audit-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyPhysical);
        var emptyFs = new DiskBackedFileSystemService(emptyPhysical, VirtualAuditDir);
        return new JsonFileAuditSink(emptyFs, VirtualAuditDir);
    }

    // --- Inspection ---

    public JsonFileAuditSink GetSink() => _sink;
    public string GetTempDir() => _physicalDir;

    // --- Cleanup ---

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _sink.Dispose();
        if (Directory.Exists(_physicalDir))
        {
            try { Directory.Delete(_physicalDir, true); }
            catch { /* Best effort cleanup */ }
        }
    }
}
