using System.Text;
using System.Text.Json;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The judge (SPEC-ORKEON-FORGE §7.4): grades a run's output against the brief's acceptance
/// criteria, in the very words the interview captured. One seam; null means "no judge
/// available" — the diagnosis then announces a degraded verdict, never a faked one.
/// </summary>
internal interface IForgeJudge
{
    /// <summary>Judges <paramref name="output"/> against the brief.</summary>
    Task<ForgeJudgement> JudgeAsync(ForgeBrief brief, string output, CancellationToken cancellationToken);
}

/// <summary>
/// What one judging attempt produced: a verdict — or null when no judge could run — and
/// what it cost either way (a failed parse was still paid for, so it is still charged to
/// the session budget).
/// </summary>
internal sealed record ForgeJudgement(ForgeVerdict? Verdict, ForgeUsageSnapshot Usage)
{
    /// <summary>No judge available, nothing spent.</summary>
    public static readonly ForgeJudgement Unavailable = new(null, default);
}

/// <summary>
/// The production judge: one prompt + schema unit over <see cref="ILlmProvider.ChatAsync"/>
/// — not a conversation, not an agent. Structured output follows the provider's declared
/// capability (<c>json_object</c> when supported, prompt-embedded schema otherwise), and
/// the response is parsed tolerantly with one retry carrying the parse error back.
/// <para>
/// Deliberate deviation from SPEC §10's first idea (recorded in FORGE-04):
/// <c>LlmJudgeEvaluatorBase</c> parses a bare score+reasoning — too poor for the
/// per-criterion findings and suggestions the verdict schema requires, so the judge owns
/// its prompt/parse and skips the evaluator plumbing.
/// </para>
/// </summary>
internal sealed class LlmForgeJudge : IForgeJudge
{
    private const int MaxAttempts = 2;

    private readonly ILlmProvider? _provider;

    /// <summary>Builds the judge; a null provider makes it unavailable, announced upstream.</summary>
    public LlmForgeJudge(ILlmProvider? provider) => _provider = provider;

    /// <inheritdoc />
    public async Task<ForgeJudgement> JudgeAsync(
        ForgeBrief brief, string output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(output);

        if (_provider is null)
            return ForgeJudgement.Unavailable;

        var config = _provider.BaseConfig ?? LlmConfig.Default();
        if (_provider.Capabilities.ResponseFormat != ResponseFormatSupport.None)
            config = config with { ResponseFormat = LlmResponseFormat.JsonObject() };

        var usage = default(ForgeUsageSnapshot);
        string? parseError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LlmMessage[] prompt =
            [
                LlmMessage.System(SystemPrompt),
                new LlmMessage { Role = "user", Content = BuildBody(brief, output, parseError) },
            ];
            var response = await _provider.ChatAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            usage = usage.Plus(Measure(response, prompt));

            if (ForgeVerdict.TryParse(response.Content, out var verdict, out var errors))
                return new ForgeJudgement(verdict, usage);

            parseError = string.Join(" ", errors);
        }

        // Two unparsable responses: the judge is effectively unavailable for this run —
        // but the attempts were paid for.
        return new ForgeJudgement(null, usage);
    }

    /// <summary>
    /// What one judging call cost, split by direction. The judge talks to the provider
    /// directly — no usage sink sits between them — so it does its own reading, and falls
    /// back to an estimate when the provider reports nothing rather than counting zero.
    /// </summary>
    private static ForgeUsageSnapshot Measure(LlmResponse response, IReadOnlyList<LlmMessage> prompt)
    {
        if (!Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Reported(response))
        {
            var estimatedPrompt = Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Prompt(prompt);
            var estimatedCompletion = Orkeon.Infrastructure.CostTracking.LlmUsageEstimator.Completion(response);
            return new ForgeUsageSnapshot(
                estimatedPrompt, estimatedCompletion, estimatedPrompt + estimatedCompletion);
        }

        var promptTokens = response.PromptTokens ?? 0;
        return new ForgeUsageSnapshot(
            promptTokens,
            response.CompletionTokens ?? Math.Max(0, response.TokensUsed - promptTokens),
            0);
    }

    /// <summary>Stable across runs — a prompt-cache prefix, like the assistant's header.</summary>
    private const string SystemPrompt =
        "You are the impartial judge of an agent team's output. You grade the output against "
        + "the acceptance criteria it was built for — those criteria and nothing else. You "
        + "never rewrite the output and you never invent criteria. You respond with exactly "
        + "one JSON object, no prose around it, of this shape: "
        + "{\"score\": 0.0-1.0, \"findings\": [{\"id\": \"F1\", \"severity\": \"blocking|major|minor\", "
        + "\"acceptance\": \"A1\", \"statement\": \"...\", \"evidence\": \"...\"}], "
        + "\"suggestions\": [{\"target\": \"agent:<key>|task:<key>|crew\", \"change\": \"...\", \"reason\": \"...\"}]}. "
        + "One finding per criterion that is missed or only partly met; a missed 'must' "
        + "criterion is severity blocking. Suggestions must be concrete changes to the team "
        + "— which agent, which task, what to alter — phrased so a designer can apply them.";

    private static string BuildBody(ForgeBrief brief, string output, string? parseError)
    {
        var body = new StringBuilder();

        body.AppendLine("## Acceptance criteria");
        foreach (var criterion in brief.Acceptance ?? [])
            body.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- {criterion.Id} ({criterion.Kind}): {criterion.Statement}");

        if (brief.ExpectedOutput is { } expected)
        {
            body.AppendLine();
            body.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"## Expected shape: {expected.Format} — {expected.Description}");
        }

        body.AppendLine();
        body.AppendLine("## The output to judge");
        body.AppendLine(output);

        if (parseError is not null)
        {
            body.AppendLine();
            body.AppendLine("## Your previous response was rejected");
            body.AppendLine(parseError + " Respond with the JSON object only.");
        }

        return body.ToString();
    }
}
