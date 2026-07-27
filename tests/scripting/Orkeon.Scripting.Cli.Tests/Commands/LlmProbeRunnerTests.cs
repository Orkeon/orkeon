using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// The campaign harness behind <c>orkeon llm probe</c> (LLM-08/C1), exercised against a
/// scripted provider so the harness itself is covered without spending a credit.
/// </summary>
/// <remarks>
/// The campaigns this harness enables must run against real APIs — that is their entire
/// point. What can and must be verified offline is that the harness judges correctly: that a
/// failed mode is reported as failed, that an exception is a finding rather than a crash, and
/// that a provider lacking a capability is not marked down for it.
/// </remarks>
public sealed class LlmProbeRunnerTests
{
    private static LlmConfig Config() => LlmConfig.Create("probe-model", "unused-in-tests");

    [Fact]
    public async Task ShouldPassM1_WhenTheProviderReturnsContent()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "hello there" });

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M1], TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task ShouldFailM1_WhenTheProviderReturnsAnErrorResponse()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Error = "401 Unauthorized" });

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M1], TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.False(result.Passed);
        Assert.Contains("401", result.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The framework's contract is a typed error response, never a throw — so an exception is
    /// itself a finding, and must be recorded rather than aborting the campaign.
    /// </summary>
    [Fact]
    public async Task ShouldRecordAThrownException_AsAFailedModeRatherThanAbortingTheRun()
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Throw = new InvalidOperationException("boom") });

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M1, LlmProbeMode.M12], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Passed);
        Assert.Contains("InvalidOperationException", results[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>A provider that declares no capability must not be marked down for lacking it.</summary>
    [Fact]
    public Task ShouldSkipTheThinkingMode_WhenTheProviderDeclaresItAbsent() =>
        AssertModeIsSkippedAsync(LlmProbeMode.M7);

    /// <inheritdoc cref="ShouldSkipTheThinkingMode_WhenTheProviderDeclaresItAbsent"/>
    [Fact]
    public Task ShouldSkipTheResponseFormatMode_WhenTheProviderDeclaresItAbsent() =>
        AssertModeIsSkippedAsync(LlmProbeMode.M8);

    private static async Task AssertModeIsSkippedAsync(LlmProbeMode mode)
    {
        var runner = new LlmProbeRunner(new ScriptedProvider { Content = "irrelevant" });

        var results = await runner.RunAsync(Config(), [mode], TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.True(result.Passed);
        Assert.Contains("not applicable", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailM8_WhenTheConstrainedAnswerIsNotJson()
    {
        var provider = new ScriptedProvider
        {
            Content = "Sure! Here is your answer.",
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject },
        };
        var runner = new LlmProbeRunner(provider);

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M8], TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(results).Passed);
    }

    [Fact]
    public async Task ShouldPassM8_WhenTheConstrainedAnswerParses()
    {
        var provider = new ScriptedProvider
        {
            Content = """{"ok":true}""",
            Capabilities = new LlmProviderCapabilities { ResponseFormat = ResponseFormatSupport.JsonObject },
        };
        var runner = new LlmProbeRunner(provider);

        var results = await runner.RunAsync(
            Config(), [LlmProbeMode.M8], TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(results).Passed);
    }

    /// <summary>
    /// The report is meant to be pasted into the matrix journal, which requires the evidence
    /// level to be stated rather than implied.
    /// </summary>
    [Fact]
    public void ShouldRenderAJournalReadyReport()
    {
        var report = LlmProbeRunner.ToMarkdown(
            "openai", "gpt-5.6-sol", "0.9.2-beta",
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero),
            [
                new LlmProbeResult(LlmProbeMode.M1, true, "12 char(s), tokens=5", 120),
                new LlmProbeResult(LlmProbeMode.M8, false, "not JSON: oops", 90),
            ]);

        Assert.Contains("gpt-5.6-sol", report, StringComparison.Ordinal);
        Assert.Contains("2026-07-27", report, StringComparison.Ordinal);
        Assert.Contains("sortie archivée", report, StringComparison.Ordinal);
        Assert.Contains("| M1 | ✅ |", report, StringComparison.Ordinal);
        Assert.Contains("| M8 | ❌ |", report, StringComparison.Ordinal);
    }

    /// <summary>A provider whose behaviour the test dictates, so the harness's judgement is what is measured.</summary>
    private sealed class ScriptedProvider : ILlmProvider, IStreamingLlmProvider
    {
        public string Content { get; init; } = "";
        public string? Error { get; init; }
        public Exception? Throw { get; init; }

        public string Name => "scripted";

        public bool SupportsStreaming => true;

        public LlmProviderCapabilities Capabilities { get; init; } = LlmProviderCapabilities.Unknown;

        public Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            if (Throw is not null)
                throw Throw;

            var metadata = LlmResponseMetadata.CreateBuilder().AddProvider(Name);
            if (Error is not null)
                metadata.AddError(Error);

            return Task.FromResult(new LlmResponse
            {
                Content = Error is null ? Content : "",
                TokensUsed = 5,
                Metadata = metadata.Build().ToDictionary(),
            });
        }

        public Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => GenerateAsync("", config, cancellationToken);

        public async IAsyncEnumerable<string> GenerateStreamingAsync(
            string prompt, LlmConfig? config = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            foreach (var chunk in Content.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                yield return chunk;
        }

        public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
            LlmMessage[] messages, LlmConfig? config = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return LlmStreamEvent.Content(Content);
            yield return LlmStreamEvent.Complete(new LlmResponse { Content = Content });
        }
    }
}
