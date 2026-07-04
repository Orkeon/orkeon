using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew;
using Orkeon.Infrastructure.Crew;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>
/// Regression coverage for Experiment 07 friction #5: per-task DeepSeek prompt-cache
/// counters and the derived crew-wide hit ratio must reach AUTO_SUMMARY.md so operators
/// can pilot the cache instead of running blind.
/// </summary>
public sealed class AutoSummaryCachePromptTokensTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiskBackedFileSystemService _fs;
    private const string VirtualOutput = "/output";

    public AutoSummaryCachePromptTokensTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cache-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);
        _fs = new DiskBackedFileSystemService(outputDir, virtualRoot: VirtualOutput);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
        GC.SuppressFinalize(this);
    }

    private AutoSummaryWriter CreateWriter()
        => new(_fs, VirtualOutput, NullLogger<AutoSummaryWriter>.Instance);

    private string SummaryPath() => Path.Combine(_tempDir, "output", "AUTO_SUMMARY.md");

    [Fact]
    public async Task AutoSummary_ShouldRenderCacheBreakdownColumn_WhenProviderReportsCacheStats()
    {
        var writer = CreateWriter();
        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "deepseek-crew",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Status = CrewHookStatus.Completed,
            Tasks = ImmutableList.Create(
                new TaskExecutionSnapshot
                {
                    TaskId = "t1",
                    AgentRole = "planner",
                    Success = true,
                    Duration = TimeSpan.FromSeconds(8),
                    CompletedAt = DateTimeOffset.UtcNow,
                    TokensUsed = 12_340,
                    CacheHitTokens = 9_800,
                    CacheMissTokens = 2_540,
                }),
        };

        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        var md = await File.ReadAllTextAsync(SummaryPath(), TestContext.Current.CancellationToken);
        Assert.Contains("12340 · 9800/2540", md);
        Assert.Contains("Prompt cache", md);
        Assert.Contains("hit 9800", md);
        Assert.Contains("miss 2540", md);
        // Hit ratio = 9800 / 12340 ≈ 79.4%.
        Assert.Contains("79.4", md);
    }

    [Fact]
    public async Task AutoSummary_ShouldOmitCacheSection_WhenNoTaskHasCacheStats()
    {
        var writer = CreateWriter();
        var snapshot = new CrewExecutionSnapshot
        {
            CrewId = "openai-crew",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndedAt = DateTimeOffset.UtcNow,
            Status = CrewHookStatus.Completed,
            Tasks = ImmutableList.Create(
                new TaskExecutionSnapshot
                {
                    TaskId = "t1",
                    AgentRole = "analyst",
                    Success = true,
                    Duration = TimeSpan.FromSeconds(3),
                    CompletedAt = DateTimeOffset.UtcNow,
                    TokensUsed = 500,
                }),
        };

        await writer.OnCrewCompletedAsync(snapshot, CancellationToken.None);

        var md = await File.ReadAllTextAsync(SummaryPath(), TestContext.Current.CancellationToken);
        // Tokens cell falls back to the plain count, no hit/miss suffix.
        Assert.DoesNotContain("Prompt cache", md);
        Assert.Matches(@"\| 500 \|", md);
    }
}
