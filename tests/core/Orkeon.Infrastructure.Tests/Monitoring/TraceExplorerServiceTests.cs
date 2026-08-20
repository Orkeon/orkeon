using System.Diagnostics;
using Orkeon.Application.Interfaces.Monitoring;
using Orkeon.Infrastructure.Monitoring;

namespace Orkeon.Infrastructure.Tests.Monitoring;

public sealed class TraceExplorerServiceTests : IClassFixture<TraceExplorerServiceTestsFixture>, IDisposable
{
    private readonly TraceExplorerServiceTestsFixture _fixture;
    private readonly TraceExplorerService _service;

    // A dedicated ActivitySource for tests — uses "Orkeon" prefix so the listener picks it up.
    private static readonly ActivitySource TestSource = new("Orkeon.Test.Monitoring");

    public TraceExplorerServiceTests(TraceExplorerServiceTestsFixture fixture)
    {
        _fixture = fixture;
        _service = _fixture.CreateService();
    }

    [Fact]
    public async Task GetRecentTracesAsync_ReturnsEmpty_WhenNoTraces()
    {
        // Listens to a prefix nothing emits, so "empty" means empty rather than "no other test
        // happened to emit an Orkeon activity while this one ran".
        using var isolated = _fixture.CreateService(sourcePrefix: "Orkeon.Test.NothingEmitsThis");

        // Act
        var traces = await isolated.GetRecentTracesAsync(ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(traces);
        Assert.Empty(traces);
    }

    [Fact]
    public async Task GetTraceByIdAsync_ReturnsNull_ForUnknownId()
    {
        // Act
        var detail = await _service.GetTraceByIdAsync("non-existent-trace-id", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(detail);
    }

    [Fact]
    public async Task SearchTracesAsync_FiltersByOperationName()
    {
        // Arrange — create two activities with different operation names
        using (var activity1 = TestSource.StartActivity("SearchTest.Op1"))
        {
            // Activity stops on dispose
        }
        using (var activity2 = TestSource.StartActivity("SearchTest.Op2"))
        {
            // Activity stops on dispose
        }

        // Small delay to ensure activities are processed
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act
        var criteria = new TraceSearchCriteria { OperationName = "SearchTest.Op1" };
        var results = await _service.SearchTracesAsync(criteria, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(results);
        Assert.All(results, t => Assert.Equal("SearchTest.Op1", t.OperationName));
    }

    [Fact]
    public async Task CircularBuffer_RespectsMaxSize()
    {
        // Arrange — create a service with a very small buffer
        using var smallService = _fixture.CreateService(maxTraceHistory: 5);

        // Create more activities than the buffer can hold
        for (int i = 0; i < 10; i++)
        {
            using var activity = TestSource.StartActivity($"BufferTest.Op{i}");
            // Activity stops on dispose
        }

        // Small delay to ensure activities are processed
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Act — get all traces
        var traces = await smallService.GetRecentTracesAsync(limit: 100, TestContext.Current.CancellationToken);

        // Assert — we should have at most 5 spans total (buffer is 5 activities)
        // The total span count across all traces should not exceed the buffer max
        var totalSpans = traces.Sum(t => t.SpanCount);
        Assert.True(totalSpans <= 5,
            $"Expected at most 5 spans in buffer, but found {totalSpans}");
    }

    [Fact]
    public void Service_ImplementsIDisposable()
    {
        // Arrange
        using var service = _fixture.CreateService();

        // Act & Assert — should not throw
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }
}
