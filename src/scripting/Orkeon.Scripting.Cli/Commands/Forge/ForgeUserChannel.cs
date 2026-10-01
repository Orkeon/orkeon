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

    /// <summary>
    /// Reads the amended blueprint that must follow a <c>decision.made {edit}</c>, as raw
    /// JSON. The engine validates it in full — this only carries it. Null means the
    /// channel closed.
    /// </summary>
    Task<string?> ReadBlueprintAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the folder list the user confirms after a <c>folders.proposed</c> (STUDIO-46):
    /// <paramref name="proposed"/> as it is, or amended — renamed, re-roled, bound to a real
    /// directory. The engine validates what comes back; this only carries it. Null means the
    /// channel closed.
    /// </summary>
    Task<IReadOnlyList<ForgeFolder>?> ReadFoldersAsync(
        IReadOnlyList<ForgeFolder> proposed, CancellationToken cancellationToken);
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

    /// <inheritdoc />
    public async Task<string?> ReadBlueprintAsync(CancellationToken cancellationToken)
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
                    && string.Equals(kind.GetString(), "blueprint.edited", StringComparison.Ordinal)
                    && root.TryGetProperty("blueprint", out var blueprint)
                    && blueprint.ValueKind == JsonValueKind.Object)
                {
                    return blueprint.GetRawText();
                }
            }
            catch (JsonException)
            {
                // Same tolerance as messages: skip and keep reading.
            }
        }
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeFolder>?> ReadFoldersAsync(
        IReadOnlyList<ForgeFolder> proposed, CancellationToken cancellationToken)
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
                    && string.Equals(kind.GetString(), "folders.confirmed", StringComparison.Ordinal)
                    && root.TryGetProperty("folders", out var folders)
                    && folders.ValueKind == JsonValueKind.Array)
                {
                    return folders.Deserialize<List<ForgeFolder>>(FolderOptions) ?? [];
                }
            }
            catch (JsonException)
            {
                // Same tolerance as messages: skip and keep reading.
            }
        }
    }

    private static readonly JsonSerializerOptions FolderOptions = new() { PropertyNameCaseInsensitive = true };
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

    /// <inheritdoc />
    public async Task<string?> ReadBlueprintAsync(CancellationToken cancellationToken)
    {
        // One pasted line of JSON. The terminal is the expert's channel: no editor is
        // spawned, and a malformed paste comes back as the engine's validation error.
        await _output.WriteAsync("blueprint json> ").ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);

        return await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ForgeFolder>?> ReadFoldersAsync(
        IReadOnlyList<ForgeFolder> proposed, CancellationToken cancellationToken)
    {
        // The terminal takes the proposal as it is (STUDIO-46): the folders were just printed,
        // and a request that wants others names them. Studio is where a list is edited.
        await _output.WriteLineAsync("  folders accepted as proposed — name others in the request to change them.")
            .ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return proposed;
    }
}
