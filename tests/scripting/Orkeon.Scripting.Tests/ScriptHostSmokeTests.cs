using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests;

public sealed class ScriptHostSmokeTests
{
    private static ScriptHost CreateHost(out string virtualRoot, ILlmProvider? llmProvider = null)
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        virtualRoot = "/scripts";
        var fs = new DiskBackedFileSystemService(fixturesDir, virtualRoot);
        return new ScriptHost(fs, new JsEngineFactory(llmProvider: llmProvider));
    }

    /// <summary>
    /// The host token is the root pump's only bound (SCR-25): a script parked on a host operation that
    /// never settles is abandoned when the token fires, and the caller gets the cancellation as the
    /// exact <see cref="OperationCanceledException"/> — not the 30-minute promise wait of before, nor
    /// a <see cref="TaskCanceledException"/>. The cancel is raised by the provider the body awaits,
    /// so no timer sits between it and the pump's own check.
    /// </summary>
    [Fact]
    public async Task RunAsync_propagates_cancellation_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var host = CreateHost(out var root, new CancellingParkedProvider(cts));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => host.RunAsync($"{root}/parked-crew.ork.ts", cts.Token).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
    }

    /// <summary>Cancels the source it was given the moment the script calls it, then never answers.</summary>
    private sealed class CancellingParkedProvider(CancellationTokenSource source) : ILlmProvider
    {
        public string Name => "parked";
        public LlmConfig? BaseConfig => LlmConfig.Default() with { Model = "parked" };

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken ct = default) => ParkAsync();

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken ct = default) => ParkAsync();

        private async Task<LlmResponse> ParkAsync()
        {
            // The token's state flips before this returns; only its callbacks run asynchronously.
            await source.CancelAsync();
            return await new TaskCompletionSource<LlmResponse>().Task;
        }
    }

    [Fact]
    public async Task RunAsync_returns_last_expression_value()
    {
        var host = CreateHost(out var root);

        var result = await host.RunAsync($"{root}/hello.ork.ts", CancellationToken.None);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RunAsync_falls_back_to_result_variable_when_last_expression_is_undefined()
    {
        var host = CreateHost(out var root);

        var result = await host.RunAsync($"{root}/result-variable.ork.ts", CancellationToken.None);

        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task RunAsync_throws_FileNotFoundException_for_missing_script()
    {
        var host = CreateHost(out var root);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => host.RunAsync($"{root}/does-not-exist.ork.ts", CancellationToken.None));
    }
}
