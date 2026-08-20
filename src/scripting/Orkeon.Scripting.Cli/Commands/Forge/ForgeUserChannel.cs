using System.Text.Json;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Where the user's side of the conversation comes from: the terminal in interactive mode,
/// the JSONL stdin in <c>--events</c> mode, a script in the tests. Returning null means the
/// channel closed — the engine treats it as an interruption, never as an answer.
/// </summary>
internal interface IForgeUserChannel
{
    /// <summary>Reads the user's next message.</summary>
    Task<string?> ReadUserMessageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the user's arbitration among <paramref name="options"/> (a
    /// <c>decision.needed</c> was emitted first). Anything outside the options is asked
    /// again; null means the channel closed.
    /// </summary>
    Task<string?> ReadDecisionAsync(IReadOnlyList<string> options, CancellationToken cancellationToken);
}

/// <summary>
/// The <c>--events</c> mode's inbound half (SPEC-ORKEON-FORGE §6): one JSON document per
/// stdin line, <c>user.message { text }</c> carrying the conversation. Unknown kinds and
/// malformed lines are skipped — the outbound stream owns error reporting, and a client
/// bug must not wedge the engine.
/// </summary>
internal sealed class JsonLinesUserChannel : IForgeUserChannel
{
    private readonly TextReader _input;

    /// <summary>Creates the channel over <paramref name="input"/> (stdin in the CLI).</summary>
    public JsonLinesUserChannel(TextReader input) =>
        _input = input ?? throw new ArgumentNullException(nameof(input));

    /// <inheritdoc />
    public async Task<string?> ReadUserMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("kind", out var kind)
                    && kind.ValueKind == JsonValueKind.String
                    && string.Equals(kind.GetString(), "user.message", StringComparison.Ordinal)
                    && root.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
            catch (JsonException)
            {
                // Malformed input line: skip. The protocol is one-way authoritative — the
                // engine's outbound stream is the contract, the inbound is best-effort.
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadDecisionAsync(
        IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("kind", out var kind)
                    && kind.ValueKind == JsonValueKind.String
                    && string.Equals(kind.GetString(), "decision.made", StringComparison.Ordinal)
                    && root.TryGetProperty("value", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { } decision
                    && options.Contains(decision, StringComparer.Ordinal))
                {
                    return decision;
                }
            }
            catch (JsonException)
            {
                // Same tolerance as messages: skip and keep reading.
            }
        }
    }
}

/// <summary>
/// The interactive terminal's inbound half: a prompt, a line. Used when <c>orkeon forge</c>
/// runs without <c>--events</c>.
/// </summary>
internal sealed class TerminalUserChannel : IForgeUserChannel
{
    private readonly TextReader _input;
    private readonly TextWriter _output;

    /// <summary>Creates the channel over the terminal's streams.</summary>
    public TerminalUserChannel(TextReader input, TextWriter output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <inheritdoc />
    public async Task<string?> ReadUserMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _output.WriteAsync("> ").ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);

            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;

            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadDecisionAsync(
        IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        var prompt = $"[{string.Join('/', options)}]> ";
        while (true)
        {
            await _output.WriteAsync(prompt).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);

            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return null;

            var trimmed = line.Trim();
            if (options.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                return options.First(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }
}
