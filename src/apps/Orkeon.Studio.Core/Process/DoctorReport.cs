using System.Text.Json;

namespace Orkeon.Studio.Core.Process;

/// <summary>Status of a single <c>orkeon doctor</c> check.</summary>
public enum DoctorStatus
{
    /// <summary>The CLI reported a status Studio does not know — shown, never dropped.</summary>
    Unknown,

    /// <summary><c>"ok"</c>.</summary>
    Ok,

    /// <summary><c>"warn"</c>.</summary>
    Warning,

    /// <summary><c>"fail"</c>.</summary>
    Failure,
}

/// <summary>One line of the diagnostic table.</summary>
/// <param name="Check">Check identifier, e.g. <c>llm-reachability</c>.</param>
/// <param name="Status">Parsed status.</param>
/// <param name="Detail">Human-readable detail from the CLI.</param>
/// <param name="RawStatus">The status string as emitted, kept for unrecognised values.</param>
public sealed record DoctorCheck(string Check, DoctorStatus Status, string Detail, string RawStatus);

/// <summary>Result of a <c>orkeon doctor --json</c> invocation.</summary>
public sealed record DoctorReport
{
    /// <summary>Checks parsed from the output; empty when the output could not be parsed.</summary>
    public IReadOnlyList<DoctorCheck> Checks { get; init; } = [];

    /// <summary>How the child process itself ended.</summary>
    public required ProcessRunResult Run { get; init; }

    /// <summary>Raw stdout, kept so the UI can show what the CLI actually printed.</summary>
    public string RawOutput { get; init; } = "";

    /// <summary>Why <see cref="Checks"/> is empty, when parsing failed.</summary>
    public string? ParseError { get; init; }

    /// <summary>True when at least one check failed.</summary>
    public bool HasFailures => Checks.Any(c => c.Status == DoctorStatus.Failure);

    /// <summary>True when at least one check warned.</summary>
    public bool HasWarnings => Checks.Any(c => c.Status == DoctorStatus.Warning);
}

/// <summary>
/// Reads the <c>--json</c> payload of <c>orkeon doctor</c> — a single array of
/// <c>{check, status, detail}</c> objects.
/// <para>
/// Parsing is deliberately tolerant: the array is located inside the output rather than
/// assumed to be the whole of it (a settings warning on stdout must not break the
/// diagnostic), unknown status strings become <see cref="DoctorStatus.Unknown"/> instead of
/// an error, and malformed entries are skipped. A version of the CLI that adds a field stays
/// readable; one that changes the shape entirely reports a parse error rather than an empty
/// green table.
/// </para>
/// </summary>
public static class DoctorReportParser
{
    /// <summary>Parses the checks out of <paramref name="output"/>.</summary>
    /// <returns>False when no JSON array of checks could be read; <paramref name="error"/> then says why.</returns>
    public static bool TryParse(string? output, out IReadOnlyList<DoctorCheck> checks, out string? error)
    {
        checks = [];

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "orkeon doctor produced no output.";
            return false;
        }

        var start = output.IndexOf('[', StringComparison.Ordinal);
        var end = output.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            error = "orkeon doctor did not print a JSON array — was it run without --json?";
            return false;
        }

        var json = output[start..(end + 1)];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "orkeon doctor printed JSON that is not an array of checks.";
                return false;
            }

            var parsed = new List<DoctorCheck>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryReadCheck(element, out var check))
                    parsed.Add(check);
            }

            checks = parsed;
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"orkeon doctor printed malformed JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadCheck(JsonElement element, out DoctorCheck check)
    {
        check = default!;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var name = ReadString(element, "check");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var rawStatus = ReadString(element, "status") ?? "";
        check = new DoctorCheck(name, ParseStatus(rawStatus), ReadString(element, "detail") ?? "", rawStatus);
        return true;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Maps the CLI's status vocabulary; anything else stays <see cref="DoctorStatus.Unknown"/>.</summary>
    public static DoctorStatus ParseStatus(string? status) => status switch
    {
        "ok" => DoctorStatus.Ok,
        "warn" => DoctorStatus.Warning,
        "fail" => DoctorStatus.Failure,
        _ => DoctorStatus.Unknown,
    };
}
