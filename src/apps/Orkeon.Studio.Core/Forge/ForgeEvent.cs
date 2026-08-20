using System.Text.Json;

namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Wire spellings of the forge event protocol (SPEC-ORKEON-FORGE §6). Re-declared here on
/// purpose: Studio.Core never references the CLI (dependency diet), so the protocol shape
/// is the contract — pinned against the CLI's golden lines by
/// <c>ForgeEventParserTests</c>, exactly like the doctor JSON contract.
/// </summary>
public static class ForgeEventKinds
{
    /// <summary>Opening event of an engine run.</summary>
    public const string SessionStarted = "session.started";

    /// <summary>The cycle entered a stage.</summary>
    public const string StageEntered = "stage.entered";

    /// <summary>One turn of the assistant — the conversation itself.</summary>
    public const string AssistantMessage = "assistant.message";

    /// <summary>A closed question from the engine.</summary>
    public const string QuestionAsked = "question.asked";

    /// <summary>The structured brief is available.</summary>
    public const string BriefReady = "brief.ready";

    /// <summary>A team plan was proposed.</summary>
    public const string BlueprintReady = "blueprint.ready";

    /// <summary>One crew file was written.</summary>
    public const string FileWritten = "file.written";

    /// <summary>Validation verdict of the rendered crew.</summary>
    public const string ValidationResult = "validation.result";

    /// <summary>A repair loop started.</summary>
    public const string RepairStarted = "repair.started";

    /// <summary>The sandboxed run started.</summary>
    public const string RunStarted = "run.started";

    /// <summary>One task of the run completed.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>The sandboxed run finished.</summary>
    public const string RunFinished = "run.finished";

    /// <summary>The diagnosis produced its verdict.</summary>
    public const string VerdictReady = "verdict.ready";

    /// <summary>The engine waits for a human arbitration.</summary>
    public const string DecisionNeeded = "decision.needed";

    /// <summary>The session was promoted to an ordinary folder.</summary>
    public const string Promoted = "promoted";

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string SessionFinished = "session.finished";

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = "error";

    /// <summary>Inbound: the user's next conversation turn (stdin).</summary>
    public const string UserMessage = "user.message";

    /// <summary>Inbound: the user's arbitration (stdin).</summary>
    public const string DecisionMade = "decision.made";
}

/// <summary>
/// One line of the forge event stream: the envelope fields plus the whole document, kept
/// as JSON — the projection (<see cref="ForgeSessionModel"/>) reads the payload in one
/// place, this type only guarantees the envelope.
/// </summary>
public sealed record ForgeEvent
{
    /// <summary>Protocol version of the envelope.</summary>
    public required int Version { get; init; }

    /// <summary>Strictly increasing sequence number.</summary>
    public required long Seq { get; init; }

    /// <summary>Event kind, one of <see cref="ForgeEventKinds"/> — or unknown, kept as-is.</summary>
    public required string Kind { get; init; }

    /// <summary>The whole event document (envelope + flat payload), detached from its parser.</summary>
    public required JsonElement Root { get; init; }

    /// <summary>String property of the payload, null when absent or not a string.</summary>
    public string? GetString(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Boolean property of the payload, null when absent.</summary>
    public bool? GetBool(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>Integer property of the payload, null when absent or not a number.</summary>
    public long? GetInt64(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    /// <summary>Floating-point property of the payload, null when absent or not a number.</summary>
    public double? GetDouble(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}

/// <summary>
/// Tolerant reader of one stream line (the <c>DoctorReportParser</c> discipline): a line
/// that is not an event — malformed JSON, missing envelope — comes back <c>false</c> and
/// the caller shows it raw instead of losing it. The protocol is versioned; the version is
/// surfaced, never enforced here — a client decides what to do with a future one.
/// </summary>
public static class ForgeEventParser
{
    /// <summary>The protocol version this client was written against.</summary>
    public const int KnownProtocolVersion = 1;

    /// <summary>Parses one stdout line into an event; false when the line is not one.</summary>
    public static bool TryParse(string? line, out ForgeEvent? forgeEvent)
    {
        forgeEvent = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("seq", out var seq) || seq.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            forgeEvent = new ForgeEvent
            {
                Version = version.GetInt32(),
                Seq = seq.GetInt64(),
                Kind = kind.GetString()!,
                Root = root.Clone(),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
