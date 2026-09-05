using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.CostTracking;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Where a turn's submissions land (SPEC-ORKEON-FORGE §7.1): the two submit tools write
/// here, the assistant wrapper reads after the turn. This is the **only** channel by which
/// the conversation produces anything — the assistant writes no file and fires no trigger.
/// </summary>
internal sealed class ForgeSubmissionBox
{
    private readonly Lock _gate = new();
    private string? _briefJson;
    private string? _blueprintJson;

    /// <summary>Stores a schema-valid brief submission (the latest wins within a turn).</summary>
    public void OfferBrief(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        lock (_gate)
            _briefJson = json;
    }

    /// <summary>Stores a schema-valid blueprint submission (the latest wins within a turn).</summary>
    public void OfferBlueprint(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        lock (_gate)
            _blueprintJson = json;
    }

    /// <summary>Empties the box; the assistant wrapper calls this before every turn.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _briefJson = null;
            _blueprintJson = null;
        }
    }

    /// <summary>Takes the turn's submissions, leaving the box empty.</summary>
    public (string? BriefJson, string? BlueprintJson) Take()
    {
        lock (_gate)
        {
            var taken = (_briefJson, _blueprintJson);
            _briefJson = null;
            _blueprintJson = null;
            return taken;
        }
    }
}

/// <summary>
/// Shared plumbing of the two submit tools: the model's arguments become the submitted JSON
/// document, validated against the schema **before** the box accepts it — a rejected
/// submission comes back to the model as the tool result, with the schema errors verbatim,
/// so the act loop self-corrects without a user round-trip.
/// </summary>
internal abstract class ForgeSubmitToolBase : IBaseTool
{
    private static readonly JsonSerializerOptions ArgumentOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private protected ForgeSubmitToolBase(ForgeSubmissionBox box) =>
        Box = box ?? throw new ArgumentNullException(nameof(box));

    /// <summary>The box the submissions land in.</summary>
    private protected ForgeSubmissionBox Box { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract ToolSchema Schema { get; }

    /// <summary>Validates and stores <paramref name="json"/>; the returned text goes back to the model.</summary>
    private protected abstract string Submit(string json);

    /// <inheritdoc />
    public Task<ToolCallResponse> CallAsync(
        Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The model's arguments ARE the document: no JSON-in-a-string double encoding,
        // which small models fumble.
        var json = JsonSerializer.Serialize(request.Parameters, ArgumentOptions);

        // Success stays true either way: the feedback below is the tool result the act
        // loop feeds back, and that text — not a transport-level failure — is what lets
        // the model fix its own submission.
        return Task.FromResult(new ToolCallResponse(Success: true, Result: Submit(json), Error: null));
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        var feedback = Submit(string.IsNullOrWhiteSpace(input) ? "{}" : input);
        return Task.FromResult(new ToolResult { Success = true, Output = feedback });
    }

    /// <inheritdoc />
    public bool ValidateInput(string input) => true;
}

/// <summary>The interview's terminal gesture: <c>brief_submit</c> (SPEC §7.1).</summary>
internal sealed class BriefSubmitTool : ForgeSubmitToolBase
{
    /// <summary>Builds the tool over the shared box.</summary>
    public BriefSubmitTool(ForgeSubmissionBox box)
        : base(box) { }

    /// <inheritdoc />
    public override string Name => "brief_submit";

    /// <inheritdoc />
    public override string Description =>
        "Submit the completed brief and end the interview. Call this exactly once, when the "
        + "user has confirmed the acceptance criteria. Arguments: goal (string, required); "
        + "context (string); inputs (array of {name, description, example}); expectedOutput "
        + "({format: markdown|json|text|file, description}); constraints (array of strings); "
        + "toolHints (array of strings); acceptance (array of {id: 'A1'…, statement, kind: "
        + "must|should}, required, at least one); sample ({variables: object, initialContext}); "
        + "language ('fr' or 'en').";

    /// <inheritdoc />
    public override ToolSchema Schema => new(
        Name,
        Description,
        new Dictionary<string, ParameterSchema>
        {
            ["goal"] = new("string", "What the crew must accomplish, in the user's words.", Required: true),
            ["acceptance"] = new("array", "The acceptance criteria: {id, statement, kind: must|should}.", Required: true),
            ["context"] = new("string", "Domain and business constraints.", Required: false),
            ["inputs"] = new("array", "Named inputs: {name, description, example}.", Required: false),
            ["expectedOutput"] = new("object", "{format: markdown|json|text|file, description}.", Required: false),
            ["constraints"] = new("array", "Tone, length, language, allowed sources.", Required: false),
            ["toolHints"] = new("array", "Voiced needs ('read PDFs', 'call an API').", Required: false),
            ["sample"] = new("object", "The test input: {variables, initialContext}.", Required: false),
            ["language"] = new("string", "'fr' or 'en'.", Required: false),
        });

    /// <inheritdoc />
    private protected override string Submit(string json)
    {
        if (!ForgeBrief.TryParse(json, out _, out var errors))
            return "REJECTED — fix these and call brief_submit again: " + string.Join(" ", errors);

        Box.OfferBrief(json);
        return "Brief submitted. The interview is complete; do not call brief_submit again.";
    }
}

/// <summary>The construction's terminal gesture: <c>blueprint_submit</c> (SPEC §7.1).</summary>
internal sealed class BlueprintSubmitTool : ForgeSubmitToolBase
{
    /// <summary>Builds the tool over the shared box.</summary>
    public BlueprintSubmitTool(ForgeSubmissionBox box)
        : base(box) { }

    /// <inheritdoc />
    public override string Name => "blueprint_submit";

    /// <inheritdoc />
    public override string Description =>
        "Submit the team plan. Arguments: crew ({name, goal, process: sequential|hierarchical|"
        + "parallel|consensual|graph|autonomous, verbose, memory}, required); agents (array of "
        + "{key, role, goal, backstory, tools: array of catalogue names, allowDelegation, "
        + "maxIterations}, required, at least one, unique keys); tasks (array of {key, "
        + "description, expectedOutput, agent: an agent key, dependencies: array of task keys, "
        + "deliverable: virtual /output/… path}, required, at least one, unique keys); manager "
        + "(an agent key, hierarchical only); rationale (string: why this shape, in plain words).";

    /// <inheritdoc />
    public override ToolSchema Schema => new(
        Name,
        Description,
        new Dictionary<string, ParameterSchema>
        {
            ["crew"] = new("object", "{name, goal, process, verbose, memory}.", Required: true),
            ["agents"] = new("array", "{key, role, goal, backstory, tools, allowDelegation, maxIterations}.", Required: true),
            ["tasks"] = new("array", "{key, description, expectedOutput, agent, dependencies, deliverable}.", Required: true),
            ["manager"] = new("string", "Manager agent key, hierarchical crews only.", Required: false),
            ["rationale"] = new("string", "Why this shape of team, in plain words.", Required: false),
        });

    /// <inheritdoc />
    private protected override string Submit(string json)
    {
        if (!ForgeBlueprint.TryParse(json, out _, out var errors))
            return "REJECTED — fix these and call blueprint_submit again: " + string.Join(" ", errors);

        Box.OfferBlueprint(json);
        return "Blueprint submitted. Do not call blueprint_submit again.";
    }
}

/// <summary>
/// What a stretch of the session cost, split by direction: what went UP to the model and
/// what came back DOWN. Studio shows the two separately (↑/↓) while the user waits, so
/// the grand total alone is not enough to carry.
/// </summary>
/// <param name="PromptTokens">Ascending — everything sent to the model.</param>
/// <param name="CompletionTokens">Descending — everything the model sent back.</param>
/// <param name="EstimatedTokens">
/// How much of the sum the runtime had to approximate because the provider reported no
/// usage. Zero means every figure is the provider's own.
/// </param>
internal readonly record struct ForgeUsageSnapshot(
    long PromptTokens, long CompletionTokens, long EstimatedTokens)
{
    /// <summary>Both directions together — the one number the budget meters.</summary>
    public long TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>Whether any part of this was approximated rather than reported.</summary>
    public bool HasEstimate => EstimatedTokens > 0;

    /// <summary>What was spent between <paramref name="before"/> and this reading.</summary>
    public ForgeUsageSnapshot Since(ForgeUsageSnapshot before) => new(
        PromptTokens - before.PromptTokens,
        CompletionTokens - before.CompletionTokens,
        EstimatedTokens - before.EstimatedTokens);

    /// <summary>This reading plus <paramref name="other"/> — a stage accumulating its turns.</summary>
    public ForgeUsageSnapshot Plus(ForgeUsageSnapshot other) => new(
        PromptTokens + other.PromptTokens,
        CompletionTokens + other.CompletionTokens,
        EstimatedTokens + other.EstimatedTokens);
}

/// <summary>
/// Thread-safe token tally over the host's <see cref="ILlmUsageSink"/> port: the assistant
/// wrapper reads the delta around each turn to charge the session budget.
/// <para>
/// The two directions are kept apart here. They used to be added together on arrival —
/// the split arrived on every event and was destroyed one line later — which is why the
/// protocol could only ever carry a grand total.
/// </para>
/// <para>
/// It is also the <see cref="ILlmDeltaSink"/>, which is what makes the meter move WHILE a
/// response is being written rather than once it is finished. Registering a delta sink is
/// what puts the assistant on the provider's streaming path; the OpenAI-compatible base
/// reassembles <c>tool_calls</c> from the stream fragments precisely so the scripted
/// <c>act</c> loop keeps working over it, so the interview's submissions are unaffected.
/// </para>
/// </summary>
internal sealed class ForgeUsageTally : ILlmUsageSink, ILlmDeltaSink
{
    private readonly Lock _gate = new();
    private ForgeUsageSnapshot _committed;
    private long _inFlightCharacters;

    /// <summary>
    /// Raised whenever the meter moves — on a recorded call AND on every streamed chunk.
    /// This is what lets a long turn report as it goes instead of only when it returns.
    /// </summary>
    public event Action<ForgeUsageSnapshot>? Changed;

    /// <summary>
    /// What has been spent since the tally was created, including the response currently
    /// being streamed. The in-flight part is an approximation of the descending side and is
    /// counted as estimated — so a client marks it «≈» until the provider's own figure
    /// replaces it.
    /// </summary>
    public ForgeUsageSnapshot Snapshot
    {
        get { lock (_gate) return Current(); }
    }

    /// <summary>Both directions together, since the tally was created.</summary>
    public long TotalTokens => Snapshot.TotalTokens;

    /// <summary>Committed plus in-flight. Caller holds the lock.</summary>
    private ForgeUsageSnapshot Current()
    {
        var inFlight = LlmUsageEstimator.FromCharacterCount(_inFlightCharacters);
        return inFlight == 0
            ? _committed
            : new ForgeUsageSnapshot(
                _committed.PromptTokens,
                _committed.CompletionTokens + inFlight,
                _committed.EstimatedTokens + inFlight);
    }

    /// <inheritdoc />
    public void Record(CostUsageEvent usage)
    {
        if (usage is null)
            return;

        var spent = (long)usage.PromptTokens + usage.CompletionTokens;
        ForgeUsageSnapshot updated;
        lock (_gate)
        {
            _committed = new ForgeUsageSnapshot(
                _committed.PromptTokens + usage.PromptTokens,
                _committed.CompletionTokens + usage.CompletionTokens,
                _committed.EstimatedTokens + (usage.Estimated ? spent : 0));
            // The measurement replaces the chunk estimate it was standing in for. The
            // figure may settle a little below what the stream was showing; that is the
            // estimate being corrected, and it is exactly what the «≈» announced.
            _inFlightCharacters = 0;
            updated = _committed;
        }

        // Outside the lock: a subscriber writes to the protocol stream, and holding the
        // tally's lock across that would put the LLM latency path behind stdout.
        Changed?.Invoke(updated);
    }

    /// <inheritdoc />
    public void OnDelta(string delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        ForgeUsageSnapshot updated;
        lock (_gate)
        {
            _inFlightCharacters += delta.Length;
            updated = Current();
        }

        Changed?.Invoke(updated);
    }

    /// <summary>
    /// End of a streamed turn. Nothing to do: the usage event that follows is what commits
    /// the real figure, and clearing here would make the meter dip and jump back.
    /// </summary>
    public void OnTurnCompleted()
    {
        // Intentionally empty: the turn's usage event commits the real figure and resets
        // the in-flight estimate. Clearing it here would make the meter dip and jump back.
    }
}
