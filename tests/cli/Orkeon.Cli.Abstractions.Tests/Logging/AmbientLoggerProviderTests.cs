using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Cli.Abstractions.Logging;

namespace Orkeon.Cli.Abstractions.Tests.Logging;

/// <summary>
/// Tests for the process-wide ambient logger registry. The static state is reset
/// before each test and the collection prevents parallel interference.
/// </summary>
[Collection("AmbientLogger")]
public sealed class AmbientLoggerProviderTests : IDisposable
{
    public AmbientLoggerProviderTests() => AmbientLoggerProvider.Reset();

    public void Dispose() => AmbientLoggerProvider.Reset();

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public int CreateLoggerCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public string? LastCategory { get; private set; }

        public ILogger CreateLogger(string categoryName)
        {
            CreateLoggerCalls++;
            LastCategory = categoryName;
            return NullLogger.Instance;
        }

        public void Dispose() => DisposeCalls++;
    }

    [Fact]
    public void Current_is_null_when_nothing_set()
    {
        Assert.Null(AmbientLoggerProvider.Current);
    }

    [Fact]
    public void Current_after_set_returns_non_null_lease()
    {
        using var provider = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(provider);

        Assert.NotNull(AmbientLoggerProvider.Current);
    }

    [Fact]
    public void Current_returns_a_new_lease_instance_each_call()
    {
        using var provider = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(provider);

        var first = AmbientLoggerProvider.Current;
        var second = AmbientLoggerProvider.Current;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Set_null_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => AmbientLoggerProvider.Set(null!));
    }

    [Fact]
    public void Reset_clears_current()
    {
        using var provider = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(provider);
        AmbientLoggerProvider.Reset();

        Assert.Null(AmbientLoggerProvider.Current);
    }

    [Fact]
    public void Lease_CreateLogger_delegates_to_inner_provider()
    {
        using var inner = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(inner);

        var lease = AmbientLoggerProvider.Current;
        var logger = lease!.CreateLogger("MyCategory");

        Assert.Same(NullLogger.Instance, logger);
        Assert.Equal(1, inner.CreateLoggerCalls);
        Assert.Equal("MyCategory", inner.LastCategory);
    }

    [Fact]
    public void Lease_Dispose_does_not_dispose_inner_provider()
    {
        using var inner = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(inner);

        var lease = AmbientLoggerProvider.Current;
        lease!.Dispose();

        Assert.Equal(0, inner.DisposeCalls);
    }

    [Fact]
    public void Set_replaces_previous_provider()
    {
        using var first = new RecordingLoggerProvider();
        using var second = new RecordingLoggerProvider();
        AmbientLoggerProvider.Set(first);
        AmbientLoggerProvider.Set(second);

        AmbientLoggerProvider.Current!.CreateLogger("cat");

        Assert.Equal(0, first.CreateLoggerCalls);
        Assert.Equal(1, second.CreateLoggerCalls);
    }
}
