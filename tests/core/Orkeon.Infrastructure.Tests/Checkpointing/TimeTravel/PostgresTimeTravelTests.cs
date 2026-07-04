using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.Checkpointing;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Checkpointing.TimeTravel;

/// <summary>
/// Time-travel tests using the <see cref="PostgresStateStore"/> implementation.
/// Only runs when the ORKEON_POSTGRES_TEST_CONN environment variable is set
/// to a valid PostgreSQL connection string.
/// </summary>
public class PostgresTimeTravelTests : TimeTravelTestBase
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ORKEON_POSTGRES_TEST_CONN");

    private static bool IsPostgresAvailable => !string.IsNullOrEmpty(ConnectionString);

    private const string PostgresSkipReason = "ORKEON_POSTGRES_TEST_CONN not set";

    protected override IStateStore CreateStore()
    {
        if (!IsPostgresAvailable)
        {
            // Return a throwaway InMemoryStateStore so InitializeAsync does not throw.
            // The test methods themselves will early-return when Postgres is unavailable.
            return new InMemoryStateStore();
        }

        var options = Options.Create(new PostgresStateStoreOptions
        {
            ConnectionString = ConnectionString!,
            SchemaName = $"test_{Guid.NewGuid():N}",
            AutoMigrate = true
        });
        return new PostgresStateStore(options);
    }

    [Fact]
    public override async Task SaveVersioned_CreatesIncrementingVersions()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.SaveVersioned_CreatesIncrementingVersions();
    }

    [Fact]
    public override async Task GetHistory_ReturnsAllVersionsDescending()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.GetHistory_ReturnsAllVersionsDescending();
    }

    [Fact]
    public override async Task GetHistory_WithLimit_ReturnsRequestedCount()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.GetHistory_WithLimit_ReturnsRequestedCount();
    }

    [Fact]
    public override async Task GetAtTimestamp_ReturnsCorrectVersion()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.GetAtTimestamp_ReturnsCorrectVersion();
    }

    [Fact]
    public override async Task GetByStep_ReturnsVersionForStep()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.GetByStep_ReturnsVersionForStep();
    }

    [Fact]
    public override async Task Fork_CreatesNewSession_WithCopiedState()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Fork_CreatesNewSession_WithCopiedState();
    }

    [Fact]
    public override async Task Fork_SetsForkedFromMetadata()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Fork_SetsForkedFromMetadata();
    }

    [Fact]
    public override async Task Fork_OriginalSessionUnmodified()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Fork_OriginalSessionUnmodified();
    }

    [Fact]
    public override async Task Diff_DetectsTaskChanges()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Diff_DetectsTaskChanges();
    }

    [Fact]
    public override async Task Diff_DetectsPhaseChange()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Diff_DetectsPhaseChange();
    }

    [Fact]
    public override async Task Diff_DetectsNewTasks()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Diff_DetectsNewTasks();
    }

    [Fact]
    public override async Task ResumeFromVersion_RestoresCorrectState()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.ResumeFromVersion_RestoresCorrectState();
    }

    [Fact]
    public override async Task Retrocompat_SaveAsync_StillWorks()
    {
        Assert.SkipWhen(!IsPostgresAvailable, PostgresSkipReason);
        await base.Retrocompat_SaveAsync_StillWorks();
    }
}
