using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;

namespace Orkeon.Infrastructure.Consensus;

/// <summary>
/// The default <see cref="IBallotCollector"/> (GAP-04): the voting agent casts its own ballot,
/// through <see cref="IAgentExecutionService"/> — its own LLM configuration, the same token
/// counting as its answers. It is asked for a JSON object
/// (<see cref="LlmResponseFormat.JsonObject"/>):
/// <c>{"ranking": ["B", "A"], "abstain": false, "confidence": 0.8, "justification": "…"}</c>.
/// </summary>
/// <remarks>
/// A reply that is not that object, a ranking that names no offered label, and a failed
/// execution each count as an abstention and are logged; none fails the task. Labels the
/// voter was not offered — its own answer among them — are dropped from the ranking. The
/// ballot runs with <see cref="Orkeon.Application.Context.SimpleExecutionContext.StoreResultInMemory"/>
/// and <see cref="Orkeon.Application.Context.SimpleExecutionContext.RecallFromMemory"/> off,
/// whatever the request's context says: its JSON is not a task result and never reaches the crew's
/// memory (GAP-20), and a vote recalls none of it (GAP-30).
/// </remarks>
public sealed partial class AgentBallotCollector : IBallotCollector
{
    /// <summary>Room kept in the ballot prompt for everything but the candidates' text.</summary>
    private const int PromptOverheadChars = 4_096;

    private const string TruncationMarker = "\n[… answer truncated for the ballot]";

    private readonly IAgentExecutionService _executionService;
    private readonly ILogger<AgentBallotCollector> _logger;

    /// <summary>Initializes a new instance of <see cref="AgentBallotCollector"/>.</summary>
    /// <param name="executionService">Runs the ballot as a task of the voting agent.</param>
    /// <param name="logger">The logger.</param>
    public AgentBallotCollector(IAgentExecutionService executionService, ILogger<AgentBallotCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(logger);
        _executionService = executionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<Ballot> CollectAsync(BallotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CollectCoreAsync(request, cancellationToken);
    }

    private async System.Threading.Tasks.Task<Ballot> CollectCoreAsync(BallotRequest request, CancellationToken cancellationToken)
    {
        var ballotTask = new CrewTaskBuilder()
            .Description(BuildPrompt(request))
            .ExpectedOutput(
                "A JSON object: {\"ranking\": [labels, best first], \"abstain\": false, " +
                "\"confidence\": a number from 0 to 1, \"justification\": \"one sentence\"}")
            .WithResponseFormat(LlmResponseFormat.JsonObject())
            .Build();

        // A ballot is not a task result: whatever context it comes with, its JSON never goes to
        // the crew's memory, where a later task would read it as one (GAP-20). Nor does it answer
        // the task: the crew's earlier work has no place in its prompt (GAP-30).
        var context = request.Context with { StoreResultInMemory = false, RecallFromMemory = false };

        var execution = await _executionService.ExecuteTaskAsync(
            request.Voter, ballotTask, context, cancellationToken).ConfigureAwait(false);

        if (!execution.Success)
        {
            var reason = $"the ballot execution failed: {execution.Error ?? execution.LastError ?? "no error given"}";
            LogBallotAbstention(request.Voter.Role.Value, reason);
            return Ballot.Abstention(reason, execution);
        }

        var ballot = Parse(execution.Output, request.Candidates.Select(c => c.Label).ToHashSet(StringComparer.Ordinal));
        if (ballot.Abstained)
            LogBallotAbstention(request.Voter.Role.Value, ballot.Justification ?? "abstained");
        return ballot with { Execution = execution };
    }

    /// <summary>
    /// Reads a ballot reply. Anything but a JSON object with a ranking of offered labels —
    /// or an explicit <c>"abstain": true</c> — is an abstention.
    /// </summary>
    internal static Ballot Parse(string? reply, IReadOnlySet<string> offeredLabels)
    {
        var json = ExtractJsonObject(reply);
        if (json is null)
            return Ballot.Abstention("the ballot reply is not a JSON object");

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Ballot.Abstention("the ballot reply is not a JSON object");

            var justification = root.TryGetProperty("justification", out var j) && j.ValueKind == JsonValueKind.String
                ? j.GetString()
                : null;

            if (root.TryGetProperty("abstain", out var abstain) && abstain.ValueKind == JsonValueKind.True)
                return Ballot.Abstention(justification ?? "the voter abstained");

            var ranking = ReadRanking(root, offeredLabels);
            if (ranking.IsEmpty)
                return Ballot.Abstention("the ballot ranks none of the offered candidates");

            return new Ballot
            {
                Ranking = ranking,
                Confidence = ReadConfidence(root),
                Justification = justification,
            };
        }
        catch (JsonException ex)
        {
            return Ballot.Abstention($"the ballot reply is not valid JSON: {ex.Message}");
        }
    }

    private static ImmutableList<string> ReadRanking(JsonElement root, IReadOnlySet<string> offeredLabels)
    {
        if (!root.TryGetProperty("ranking", out var ranking) || ranking.ValueKind != JsonValueKind.Array)
            return [];

        var labels = ImmutableList.CreateBuilder<string>();
        foreach (var item in ranking.EnumerateArray())
        {
            var label = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (label is not null && offeredLabels.Contains(label) && !labels.Contains(label))
                labels.Add(label);
        }

        return labels.ToImmutable();
    }

    private static float ReadConfidence(JsonElement root)
    {
        if (!root.TryGetProperty("confidence", out var confidence) || confidence.ValueKind != JsonValueKind.Number
            || !confidence.TryGetDouble(out var value) || double.IsNaN(value))
            return 1f;
        return (float)Math.Clamp(value, 0d, 1d);
    }

    /// <summary>The first <c>{</c> to the last <c>}</c>: tolerates a fenced block or a sentence around the object.</summary>
    private static string? ExtractJsonObject(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return null;
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        return start >= 0 && end > start ? reply[start..(end + 1)] : null;
    }

    /// <summary>
    /// The ballot prompt: the task, then each anonymised candidate. The candidates share what
    /// the task description may hold, so a long answer is truncated rather than failing the ballot.
    /// </summary>
    internal static string BuildPrompt(BallotRequest request)
    {
        var budget = Math.Max(
            256,
            (TaskDefaults.TaskDescriptionMaxLength - PromptOverheadChars
             - request.Task.Description.Value.Length - request.Task.ExpectedOutput.Value.Length)
            / Math.Max(1, request.Candidates.Count));

        var prompt = new StringBuilder();
        prompt.AppendLine("You are casting a ballot. Other agents answered the task below independently;");
        prompt.AppendLine("their answers are anonymised under labels. Rank every answer from best to worst");
        prompt.AppendLine("on how well it accomplishes the task and meets the expected output.");
        prompt.AppendLine();
        prompt.AppendLine("## Task");
        prompt.AppendLine(request.Task.Description.Value);
        prompt.AppendLine();
        prompt.AppendLine("## Expected output");
        prompt.AppendLine(request.Task.ExpectedOutput.Value);
        prompt.AppendLine();
        prompt.AppendLine("## Candidate answers");
        foreach (var candidate in request.Candidates)
        {
            prompt.AppendLine();
            prompt.AppendLine(CultureInfo.InvariantCulture, $"### Candidate {candidate.Label}");
            prompt.AppendLine(candidate.Output.Length <= budget
                ? candidate.Output
                : string.Concat(candidate.Output.AsSpan(0, budget), TruncationMarker));
        }

        prompt.AppendLine();
        prompt.AppendLine("## Your ballot");
        prompt.AppendLine("Reply with a JSON object only, no other text:");
        prompt.AppendLine("{\"ranking\": [\"<label>\", ...], \"abstain\": false, \"confidence\": <0 to 1>, \"justification\": \"<one sentence>\"}");
        prompt.Append(CultureInfo.InvariantCulture,
            $"\"ranking\" lists the labels {string.Join(", ", request.Candidates.Select(c => c.Label))}, best first. ");
        prompt.Append("If no answer is acceptable, set \"abstain\" to true and leave \"ranking\" empty.");
        return prompt.ToString();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ballot of agent {Voter} counts as an abstention: {Reason}")]
    private partial void LogBallotAbstention(string voter, string reason);
}
