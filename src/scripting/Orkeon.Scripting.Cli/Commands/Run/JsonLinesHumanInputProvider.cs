using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Commands.Run;

/// <summary>
/// Reads one answer line from an inbound channel. A separate seam from the writer so the
/// tests can drive a dialogue without a process, exactly as the Atelier's user channel does.
/// </summary>
internal interface IAnswerChannel
{
    /// <summary>
    /// Reads answer lines until one carries <paramref name="correlationId"/>, or the channel
    /// closes. Returns null when no answer will come.
    /// </summary>
    Task<string?> ReadAnswerAsync(string correlationId, CancellationToken cancellationToken);
}

/// <summary>
/// Reads <c>input.given { correlationId, value }</c> from stdin, one JSON document per line.
/// Malformed lines are skipped: the outbound stream is the contract, the inbound one is
/// tolerant — the same rule the Atelier's channel follows.
/// </summary>
internal sealed class StdinAnswerChannel : IAnswerChannel
{
    private readonly TextReader _input;

    /// <summary>Reads from <paramref name="input"/> (stdin in the CLI).</summary>
    public StdinAnswerChannel(TextReader input) =>
        _input = input ?? throw new ArgumentNullException(nameof(input));

    /// <inheritdoc />
    public async Task<string?> ReadAnswerAsync(string correlationId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;   // the channel closed: no answer will come

            if (TryReadAnswer(line, correlationId, out var value))
                return value;
        }

        return null;
    }

    /// <summary>Parses one inbound line; false when it is not the answer we are waiting for.</summary>
    internal static bool TryReadAnswer(string line, string correlationId, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.GetString() != RunEventKinds.InputGiven)
            {
                return false;
            }

            // An answer without a correlation id answers whatever is pending: a human
            // typing into a terminal has no id to quote, and refusing them would be silly.
            if (root.TryGetProperty("correlationId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() != correlationId)
            {
                return false;
            }

            value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// The structured human-input provider (BUS-04): asks on the event stream and waits for the
/// answer on stdin, so a task declared <c>humanInput: true</c> finally reaches a human
/// outside a terminal.
/// <para>
/// It replaces <c>AutoApproveHumanInputProvider</c>, which answers <c>true</c> on the user's
/// behalf — a defensible fallback for an unattended run, and the wrong answer entirely once a
/// screen is watching. When no answer comes back (channel closed, run cancelled), this
/// provider **refuses** rather than approving: silence is not consent.
/// </para>
/// </summary>
internal sealed class JsonLinesHumanInputProvider : IHumanInputProvider
{
    private readonly OrkeonEventWriter _events;
    private readonly IAnswerChannel _answers;

    /// <summary>Builds the provider over the outbound stream and the inbound channel.</summary>
    public JsonLinesHumanInputProvider(OrkeonEventWriter events, IAnswerChannel answers)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _answers = answers ?? throw new ArgumentNullException(nameof(answers));
    }

    /// <inheritdoc />
    public async Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await AskAsync(context, "text", cancellationToken).ConfigureAwait(false)
            ?? context.DefaultValue
            ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answer = await AskAsync(context, "confirm", cancellationToken).ConfigureAwait(false);

        // No answer is a refusal, never an approval: the whole point of this provider is
        // that nobody answers on the user's behalf.
        return answer is not null && IsAffirmative(answer);
    }

    /// <inheritdoc />
    public async Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answer = await AskAsync(context, "choice", cancellationToken).ConfigureAwait(false);
        if (answer is not null && (context.Options.Count == 0 || context.Options.Contains(answer, StringComparer.Ordinal)))
            return answer;

        // An unusable answer falls back to the declared default, then to the first option —
        // the same degradation the auto-approve provider documents, but only after asking.
        return context.DefaultValue ?? (context.Options.Count > 0 ? context.Options[0] : string.Empty);
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private async Task<string?> AskAsync(HumanInputContext context, string kind, CancellationToken cancellationToken)
    {
        var correlationId = context.RequestId.ToString();

        _events.Emit(
            RunEventKinds.InputNeeded,
            new OrkeonEventScope
            {
                AgentId = string.IsNullOrEmpty(context.AgentRole) ? null : context.AgentRole,
                CorrelationId = correlationId,
            },
            new
            {
                // NOT `kind`: that name belongs to the envelope and a payload naming it
                // loses (BUS-01's reserved names). A client could then not tell a text
                // question from a confirmation.
                inputKind = kind,
                prompt = context.Prompt,
                choices = context.Options.Count > 0 ? context.Options : null,
                defaultValue = context.DefaultValue,
                taskDescription = string.IsNullOrEmpty(context.TaskDescription) ? null : context.TaskDescription,
            });

        return await _answers.ReadAnswerAsync(correlationId, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsAffirmative(string answer) =>
        answer.Trim() is "y" or "yes" or "true" or "1" or "o" or "oui";
}
