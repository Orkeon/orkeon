using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Runs;
using Orkeon.Constants.Protocol;
using Orkeon.Hosting.Runs;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.Runs;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// FWK-08: the runner host binds <c>Orkeon:RunHistory</c>, mounts <c>./.orkeon/runs</c> at <c>/runs</c> itself and
/// hands back a history that records and reads: no test here gives it the mount.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostRunHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-run-history-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RunnerHostRunHistoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string RunsFolder => Path.Combine(_root, ".orkeon", "runs");

    private string Settings(Dictionary<string, string> values)
    {
        var path = Path.Combine(_root, "appsettings.json");
        values["RaggableTree:Enabled"] = "false";
        File.WriteAllText(path, SettingsJson.Of(values));
        return path;
    }

    private RunnerMountPlan InRoot(bool byDefault = false) => new() { WorkingDirectory = _root, RunHistoryByDefault = byDefault };

    private static async Task RecordARunAsync(IServiceProvider services, string runId)
    {
        var sink = services.GetRequiredService<ConfiguredRunHistory>().Sink;
        var run = new RunEventPublisher([sink], TimeProvider.System, runId: runId);
        await run.WriteAsync(new RunEvent { Kind = RunEventKinds.RunStarted }, Ct);
        await run.WriteAsync(new RunEvent { Kind = RunEventKinds.RunFinished, Payload = new() { ["success"] = true } }, Ct);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldMountTheRunsFolderItself_AndRecordARunThroughTheVfs_WhenTheHistoryIsEnabled()
    {
        var settings = Settings(new() { ["Orkeon:RunHistory:Enabled"] = "true" });

        using var host = RunnerHost.Build(settings, InRoot());
        await RecordARunAsync(host.Services, "run-1");

        Assert.True(File.Exists(Path.Combine(RunsFolder, "run-1.jsonl")));
        Assert.True(File.Exists(Path.Combine(RunsFolder, "index.jsonl")));
        var reader = host.Services.GetRequiredService<IRunHistoryReader>();
        Assert.Equal(["run-1"], (await reader.ListAsync(new RunQuery(), Ct)).Select(r => r.RunId));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldKeepNoHistoryAndCreateNoFolder_WhenTheSectionIsAbsent()
    {
        using var host = RunnerHost.Build(Settings(new()), InRoot());

        Assert.Null(host.Services.GetService<IRunHistoryReader>());
        Assert.False(Directory.Exists(RunsFolder));
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldRecordByDefault_WhenTheHostAsksForIt_UnlessTheSettingsSayFalse()
    {
        using (var host = RunnerHost.Build(Settings(new()), InRoot(byDefault: true)))
            Assert.NotNull(host.Services.GetService<IRunHistoryReader>());

        using var off = RunnerHost.Build(Settings(new() { ["Orkeon:RunHistory:Enabled"] = "false" }), InRoot(byDefault: true));
        Assert.Null(off.Services.GetService<IRunHistoryReader>());
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldBackTheHistoryWithSqlite_WhenTheStoreSaysSo()
    {
        var settings = Settings(new() { ["Orkeon:RunHistory:Enabled"] = "true", ["Orkeon:RunHistory:Store"] = "sqlite" });

        using var host = RunnerHost.Build(settings, InRoot());
        await RecordARunAsync(host.Services, "run-sql");

        Assert.IsType<SqliteRunEventSink>(host.Services.GetRequiredService<ConfiguredRunHistory>().Owned);
        Assert.True(File.Exists(Path.Combine(RunsFolder, "runs.db")));
        Assert.Equal(RunStatus.Succeeded, (await host.Services.GetRequiredService<IRunHistoryReader>().GetAsync("run-sql", Ct))?.Status);

        // The history outlives the host, so closing the database is the test's job.
        host.Services.GetRequiredService<ConfiguredRunHistory>().Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldMaskSecretsOnTheWayToTheHistoryTheHostBuilt()
    {
        var settings = Settings(new() { ["Orkeon:RunHistory:Enabled"] = "true" });
        var secret = "s" + "k-" + new string('d', 32);

        using var host = RunnerHost.Build(settings, InRoot());
        var run = new RunEventPublisher([host.Services.GetRequiredService<ConfiguredRunHistory>().Sink], TimeProvider.System, runId: "run-secret");
        run.Emit(RunEventKinds.RunStarted);
        run.Emit(RunEventKinds.Error, new { message = "refused with api" + "_key=" + secret });
        run.Emit(RunEventKinds.RunFinished, new { success = false });

        var text = await File.ReadAllTextAsync(Path.Combine(RunsFolder, "run-secret.jsonl"), Ct);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public async Task ShouldNotPurgeWhenAHostIsOnlyBuilt_TheReadingSideNeverWritesOrDeletes()
    {
        var settings = Settings(new() { ["Orkeon:RunHistory:Enabled"] = "true", ["Orkeon:RunHistory:RetentionDays"] = "1" });
        Directory.CreateDirectory(RunsFolder);
        var fs = new Orkeon.Tests.Shared.FileSystem.DiskBackedFileSystemService(RunsFolder, "/runs");
        var ancient = new Orkeon.Tests.Shared.Timing.ManualTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var old = new RunEventPublisher(
            [new JsonLinesFileRunEventSink(fs, new RunHistoryOptions { Enabled = true, RetentionDays = 1 }, ancient, new RunProcessIdentity(1, "elsewhere", null))],
            ancient,
            runId: "old-run");
        old.Emit(RunEventKinds.RunStarted);
        old.Emit(RunEventKinds.RunFinished, new { success = true });

        using var host = RunnerHost.Build(settings, InRoot());

        Assert.True(File.Exists(Path.Combine(RunsFolder, "old-run.jsonl")));
        Assert.Single(await host.Services.GetRequiredService<IRunHistoryReader>().ListAsync(new RunQuery(), Ct));
    }

    [Theory]
    [InlineData("Orkeon:RunHistory:RetentionDays", "-1")]
    [InlineData("Orkeon:RunHistory:MaxRuns", "-5")]
    [InlineData("Orkeon:RunHistory:Path", "runs")]
    [Trait("BR", "FWK-08")]
    public void ShouldRefuseTheStart_OnASettingTheHistoryCannotHonour(string key, string value)
    {
        var settings = Settings(new() { ["Orkeon:RunHistory:Enabled"] = "true", [key] = value });

        var refusal = Assert.Throws<RunnerSettingsException>(() => RunnerHost.Build(settings, InRoot()));

        Assert.Contains("Orkeon:RunHistory", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-08")]
    public void ShouldLeaveTheMountToTheOperator_WhenRunsIsAlreadyMounted()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RunHistory:Enabled"] = "true" })
            .Build()
            .GetSection("RunHistory");

        Assert.Null(RunnerRunHistory.Plan(section, byDefault: false, _root, ["/runs"]));
        Assert.Equal(RunsFolder, RunnerRunHistory.Plan(section, byDefault: false, _root, []));
    }
}
