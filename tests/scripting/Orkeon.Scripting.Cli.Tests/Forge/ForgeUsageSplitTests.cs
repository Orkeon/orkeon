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

    /// <summary>
    /// The owner asked for the meter «au chunk si c'est possible». It is: the tally is also
    /// the delta sink, so a response being written moves the figure as it arrives — marked
    /// «≈», because until the provider's own count lands the descending side is the
    /// runtime's estimate of the text it has seen.
    /// </summary>
    [Fact]
    public void A_response_being_streamed_moves_the_meter_chunk_by_chunk()
    {
        var tally = new ForgeUsageTally();
        var readings = new List<ForgeUsageSnapshot>();
        tally.Changed += readings.Add;

        ILlmDeltaSink sink = tally;
        sink.OnDelta(new string('a', 350));
        sink.OnDelta(new string('b', 350));

        Assert.Equal(2, readings.Count);
        Assert.True(readings[0].CompletionTokens > 0);
        Assert.True(readings[1].CompletionTokens > readings[0].CompletionTokens);
        // Every in-flight token is an estimate, and the snapshot says so.
        Assert.True(tally.Snapshot.HasEstimate);
        Assert.Equal(tally.Snapshot.CompletionTokens, tally.Snapshot.EstimatedTokens);
    }

    /// <summary>
    /// And the provider's own figure replaces the estimate rather than stacking on top of
    /// it — a meter that counted a streamed response twice would be worse than a frozen one.
    /// </summary>
    [Fact]
    public void The_measurement_replaces_the_chunk_estimate_it_stood_in_for()
    {
        var tally = new ForgeUsageTally();
        ILlmDeltaSink sink = tally;
        sink.OnDelta(new string('a', 700));
        var streamed = tally.Snapshot;
        Assert.True(streamed.CompletionTokens > 0);

        tally.Record(new CostUsageEvent { PromptTokens = 900, CompletionTokens = 210 });

        var settled = tally.Snapshot;
        Assert.Equal(900, settled.PromptTokens);
        Assert.Equal(210, settled.CompletionTokens);
        // The provider counted, so nothing is approximate any more and the «≈» goes away.
        Assert.Equal(0, settled.EstimatedTokens);
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
