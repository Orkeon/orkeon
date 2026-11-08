using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// FWK-15: a task's <c>retry: {attempts, backoffMs, backoff}</c> loads, exports and loads again, and a
/// bad value is refused at load by name. The graph's <c>maxRetryCycles</c>, a retry of a task that
/// <c>retry:</c> replaces, is an unknown key now.
/// </summary>
public sealed class YamlRetryRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlCrewDefinitionLoader Loader() =>
        new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static string Crew(string retryBlock, string process = "sequential", string root = "") => $$"""
name: c
goal: g
process: {{process}}
{{root}}
agents:
  a: { role: R, goal: G }
tasks:
  t:
    description: D
    expectedOutput: O
    agent: a
{{retryBlock}}
""";

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldLoadTheRetryBlock()
    {
        var config = await Loader().LoadFromStringAsync(
            Crew("    retry:\n      attempts: 3\n      backoffMs: 250\n      backoff: exponential"), Ct);

        var retry = Assert.IsType<TaskRetryPolicy>(config.Tasks.Single().Retry);
        Assert.Equal(3, retry.Attempts);
        Assert.Equal(250, retry.BackoffMs);
        Assert.Equal(TaskRetryBackoff.Exponential, retry.Backoff);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldDefaultTheBackoffToFixedAndNoWait()
    {
        var config = await Loader().LoadFromStringAsync(Crew("    retry: { attempts: 2 }"), Ct);

        var retry = Assert.IsType<TaskRetryPolicy>(config.Tasks.Single().Retry);
        Assert.Equal(0, retry.BackoffMs);
        Assert.Equal(TaskRetryBackoff.Fixed, retry.Backoff);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldLeaveRetryNull_WhenTheTaskWritesNone()
    {
        var config = await Loader().LoadFromStringAsync(Crew(""), Ct);

        Assert.Null(config.Tasks.Single().Retry);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseZeroAttempts_NamingTheTask()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Loader().LoadFromStringAsync(Crew("    retry: { attempts: 0 }"), Ct));

        Assert.Contains("'t'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("attempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseARetryBlockWithoutAttempts()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Loader().LoadFromStringAsync(Crew("    retry: { backoffMs: 100 }"), Ct));

        Assert.Contains("attempts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseANegativeBackoff()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Loader().LoadFromStringAsync(Crew("    retry: { attempts: 2, backoffMs: -5 }"), Ct));

        Assert.Contains("backoffMs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseAnUnknownBackoff_ListingTheKnownOnes()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Loader().LoadFromStringAsync(Crew("    retry: { attempts: 2, backoff: linear }"), Ct));

        Assert.Contains("linear", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fixed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("exponential", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseMaxRetryCyclesAsAnUnknownKey()
    {
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => Loader().LoadFromStringAsync(Crew("", "graph", "graph:\n  maxRetryCycles: 3"), Ct));

        Assert.Contains("maxRetryCycles", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldRefuseARetryOnADecisionTask()
    {
        var yaml = """
name: c
goal: g
process: sequential
agents:
  a: { role: R, goal: G }
tasks:
  triage:
    retry: { attempts: 2 }
    decision:
      questions:
        category:
          choice:
            bug: a defect
            other: anything else
      fallback:
        category: bug
""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadFromStringAsync(yaml, Ct));

        Assert.Contains("'triage'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("retry", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldExportTheRetryBlockAndLoadItBack()
    {
        var loader = Loader();
        var config = await loader.LoadFromStringAsync(
            Crew("    retry: { attempts: 4, backoffMs: 50, backoff: exponential }"), Ct);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewExporter>.Instance);

        var yaml = exporter.ExportToString(config);
        var again = await loader.LoadFromStringAsync(yaml, Ct);

        Assert.Contains("retry:", yaml, StringComparison.Ordinal);
        Assert.Equal(config.Tasks.Single().Retry, again.Tasks.Single().Retry);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public async System.Threading.Tasks.Task ShouldNotExportARetryBlock_WhenTheTaskHasNone()
    {
        var config = await Loader().LoadFromStringAsync(Crew(""), Ct);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewExporter>.Instance);

        Assert.DoesNotContain("retry", exporter.ExportToString(config), StringComparison.Ordinal);
    }
}
