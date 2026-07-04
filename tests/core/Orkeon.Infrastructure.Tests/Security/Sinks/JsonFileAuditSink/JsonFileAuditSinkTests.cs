using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Tests.Shared.FileSystem;
namespace Orkeon.Infrastructure.Tests.Security.Sinks;

using JsonFileAuditSinkSut = Orkeon.Infrastructure.Security.Sinks.JsonFileAuditSink;

public sealed class JsonFileAuditSinkTests : IDisposable
{
    private readonly string _physicalDir;
    private readonly DiskBackedFileSystemService _fs;
    private readonly JsonFileAuditSinkSut _sink;

    private const string VirtualAuditDir = "/logs/audit";

    public JsonFileAuditSinkTests()
    {
        _physicalDir = Path.Combine(Path.GetTempPath(), "audit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_physicalDir);
        _fs = new DiskBackedFileSystemService(_physicalDir, VirtualAuditDir);
        _sink = new JsonFileAuditSinkSut(_fs, VirtualAuditDir);
    }

    private static AuditEvent CreateEvent(
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

    [Fact]
    public async Task ShouldCreateJsonlFile_WhenWritingEvent()
    {
        // Act
        await _sink.WriteAsync(CreateEvent(), TestContext.Current.CancellationToken);

        // Assert
        var files = Directory.GetFiles(_physicalDir, "audit-*.jsonl");
        Assert.Single(files);

        var lines = await File.ReadAllLinesAsync(files[0], TestContext.Current.CancellationToken);
        Assert.Single(lines);
        Assert.Contains("\"category\":", lines[0]);
    }

    [Fact]
    public async Task ShouldAppendToSameDayFile_WhenWritingMultipleEvents()
    {
        // Act
        var now = DateTime.UtcNow;
        await _sink.WriteAsync(CreateEvent(timestamp: now), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(timestamp: now), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(timestamp: now), TestContext.Current.CancellationToken);

        // Assert
        var files = Directory.GetFiles(_physicalDir, "audit-*.jsonl");
        Assert.Single(files);

        var lines = await File.ReadAllLinesAsync(files[0], TestContext.Current.CancellationToken);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task ShouldFilterByCategory_WhenQuerying()
    {
        // Arrange
        await _sink.WriteAsync(CreateEvent(category: AuditCategory.LlmCall), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(category: AuditCategory.SecurityEvent), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(category: AuditCategory.LlmCall), TestContext.Current.CancellationToken);

        // Act
        var results = await _sink.QueryAsync(new AuditQuery(Category: AuditCategory.LlmCall), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(AuditCategory.LlmCall, r.Category));
    }

    [Fact]
    public async Task ShouldFilterByDateRange_WhenQuerying()
    {
        // Arrange
        var past = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var recent = DateTime.UtcNow;

        await _sink.WriteAsync(CreateEvent(timestamp: past), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(timestamp: recent), TestContext.Current.CancellationToken);

        // Act
        var results = await _sink.QueryAsync(new AuditQuery(
            From: recent.AddMinutes(-1),
            To: recent.AddMinutes(1)), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public async Task ShouldFilterByCorrelationId_WhenQuerying()
    {
        // Arrange
        await _sink.WriteAsync(CreateEvent(correlationId: "corr-A"), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(correlationId: "corr-B"), TestContext.Current.CancellationToken);
        await _sink.WriteAsync(CreateEvent(correlationId: "corr-A"), TestContext.Current.CancellationToken);

        // Act
        var results = await _sink.QueryAsync(new AuditQuery(CorrelationId: "corr-A"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("corr-A", r.CorrelationId));
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenDirectoryIsEmpty()
    {
        // Use a new empty physical dir mapped to the same virtual root
        var emptyPhysical = Path.Combine(Path.GetTempPath(), "audit-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyPhysical);
        var emptyFs = new DiskBackedFileSystemService(emptyPhysical, VirtualAuditDir);
        using var emptySink = new JsonFileAuditSinkSut(emptyFs, VirtualAuditDir);

        // Act
        var results = await emptySink.QueryAsync(new AuditQuery(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);

        // Cleanup
        if (Directory.Exists(emptyPhysical))
            Directory.Delete(emptyPhysical, true);
    }

    [Fact]
    public async Task ShouldRespectLimit_WhenQuerying()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
            await _sink.WriteAsync(CreateEvent(), TestContext.Current.CancellationToken);

        // Act
        var results = await _sink.QueryAsync(new AuditQuery(Limit: 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
    }

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
