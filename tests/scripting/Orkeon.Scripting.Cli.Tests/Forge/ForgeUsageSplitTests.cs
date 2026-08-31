using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// What a session costs, kept apart by direction all the way to the wire.
/// <para>
/// The split existed on every usage event and was destroyed one line later: the tally added
/// the two halves on arrival, so <c>cost.updated</c> could only ever carry a grand total and
/// a client had nothing to show but one number that grows. These tests pin the whole chain —
/// tally, budget, event — and the estimate that stands in when a provider counts nothing.
/// </para>
/// </summary>
public sealed class ForgeUsageSplitTests
{
    private static ForgeBrief Brief()
    {
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        return brief!;
    }

    [Fact]
    public void The_tally_keeps_the_two_directions_apart()
    {
        var tally = new ForgeUsageTally();
        tally.Record(new CostUsageEvent { PromptTokens = 900, CompletionTokens = 120 });
        tally.Record(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 80 });

        var snapshot = tally.Snapshot;
        Assert.Equal(1000, snapshot.PromptTokens);
        Assert.Equal(200, snapshot.CompletionTokens);
        Assert.Equal(1200, snapshot.TotalTokens);
        Assert.False(snapshot.HasEstimate);
    }

    [Fact]
    public void An_estimated_event_is_counted_and_flagged_as_estimated()
    {
        var tally = new ForgeUsageTally();
        tally.Record(new CostUsageEvent { PromptTokens = 400, CompletionTokens = 100 });
        tally.Record(new CostUsageEvent { PromptTokens = 300, CompletionTokens = 50, Estimated = true });

        var snapshot = tally.Snapshot;
        Assert.Equal(850, snapshot.TotalTokens);
        // Only the approximated call is counted as such — a session that mixes a counting
        // provider with a silent one must not have its whole meter written off as a guess,
        // nor pass the guess off as a count.
        Assert.Equal(350, snapshot.EstimatedTokens);
        Assert.True(snapshot.HasEstimate);
    }

    [Fact]
    public void A_turn_is_charged_the_delta_and_nothing_of_what_came_before()
    {
        var tally = new ForgeUsageTally();
        tally.Record(new CostUsageEvent { PromptTokens = 500, CompletionTokens = 40 });
        var before = tally.Snapshot;

        tally.Record(new CostUsageEvent { PromptTokens = 300, CompletionTokens = 60, Estimated = true });

        var turn = tally.Snapshot.Since(before);
        Assert.Equal(300, turn.PromptTokens);
        Assert.Equal(60, turn.CompletionTokens);
        Assert.Equal(360, turn.EstimatedTokens);
    }

    [Fact]
    public void The_budget_meters_the_total_and_remembers_the_split()
    {
        var budget = new ForgeBudget { MaxTokens = 1000 };
        budget.RegisterTokens(new ForgeUsageSnapshot(600, 100, 0));
        budget.RegisterTokens(new ForgeUsageSnapshot(200, 50, 250));

        Assert.Equal(950, budget.ConsumedTokens);
        Assert.Equal(800, budget.ConsumedPromptTokens);
        Assert.Equal(150, budget.ConsumedCompletionTokens);
        Assert.Equal(250, budget.ConsumedEstimatedTokens);
        Assert.Null(budget.ExhaustedDimension());
    }

    /// <summary>
    /// The judge talks to the provider directly — no usage sink sits between them — so it
    /// reads the split itself, and estimates when the provider reports nothing rather than
    /// charging the session zero for two paid attempts.
    /// </summary>
    [Fact]
    public async Task The_judge_estimates_what_a_silent_provider_would_have_cost()
    {
        var silent = new SilentUsageProvider("""{"score":0.9,"passing":true,"findings":[],"suggestions":[]}""");

        var judgement = await new LlmForgeJudge(silent).JudgeAsync(
            Brief(),
            "le résumé",
            TestContext.Current.CancellationToken);

        Assert.NotNull(judgement.Verdict);
        Assert.True(judgement.Usage.PromptTokens > 0);
        Assert.True(judgement.Usage.CompletionTokens > 0);
        // Every token of it was approximated, and the record says so.
        Assert.Equal(judgement.Usage.TotalTokens, judgement.Usage.EstimatedTokens);
    }

    [Fact]
    public async Task A_counting_provider_is_taken_at_its_word_never_estimated_over()
    {
        var counting = new ScriptedLlmProvider()
            .Answers("""{"score":0.9,"passing":true,"findings":[],"suggestions":[]}""");

        var judgement = await new LlmForgeJudge(counting).JudgeAsync(
            Brief(),
            "le résumé",
            TestContext.Current.CancellationToken);

        Assert.Equal(100, judgement.Usage.PromptTokens);
        Assert.Equal(20, judgement.Usage.CompletionTokens);
        Assert.Equal(0, judgement.Usage.EstimatedTokens);
    }
}

/// <summary>
/// A provider in the shape several OpenAI-compatible endpoints really answer in: a complete
/// response with no usage block at all — not zero tokens, no tokens reported.
/// </summary>
internal sealed class SilentUsageProvider : ILlmProvider
{
    private readonly string _content;

    public SilentUsageProvider(string content) => _content = content;

    /// <inheritdoc />
    public string Name => "silent";

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(
        string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = _content });

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new LlmResponse { Content = _content });
}
