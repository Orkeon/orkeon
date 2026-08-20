using System.Text;
using System.Text.Json;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The interactive terminal's rendering of the event stream (SPEC-ORKEON-FORGE §5, §8 of
/// the UX study): a <see cref="TextWriter"/> the engine's <see cref="ForgeEventWriter"/>
/// writes its JSONL into, re-projected as human lines. One emission path for both modes —
/// a capability absent from the stream cannot exist on the terminal either, which is the
/// same guarantee the Studio client gets.
/// </summary>
internal sealed class ForgeTerminalRenderer : TextWriter
{
    private readonly TextWriter _console;

    /// <summary>Renders onto <paramref name="console"/> (stdout).</summary>
    public ForgeTerminalRenderer(TextWriter console) =>
        _console = console ?? throw new ArgumentNullException(nameof(console));

    /// <inheritdoc />
    public override Encoding Encoding => _console.Encoding;

    /// <inheritdoc />
    public override void Write(char value)
    {
        // The event writer only ever calls WriteLine(string); anything else is a bug in
        // the emitter, surfaced verbatim rather than silently swallowed.
        _console.Write(value);
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        try
        {
            using var document = JsonDocument.Parse(value);
            Render(document.RootElement);
        }
        catch (JsonException)
        {
            _console.WriteLine(value);
        }
    }

    /// <inheritdoc />
    public override void Flush() => _console.Flush();

    private void Render(JsonElement e)
    {
        switch (e.GetProperty("kind").GetString())
        {
            case "session.started":
                _console.WriteLine($"● {Text(e, "slug")}  ({Text(e, "dir")})");
                break;

            case "stage.entered":
                _console.WriteLine($"— {Text(e, "stage")} · cycle {e.GetProperty("iteration").GetInt32()} —");
                break;

            case "assistant.message":
                _console.WriteLine();
                _console.WriteLine(Text(e, "text"));
                break;

            case "brief.ready":
                _console.WriteLine("✔ Brief captured — acceptance criteria locked in.");
                break;

            case "blueprint.ready":
                _console.WriteLine("✔ Team plan proposed.");
                break;

            case "file.written":
                _console.WriteLine($"  wrote {Text(e, "path")}");
                break;

            case "validation.result":
                if (e.GetProperty("ok").GetBoolean())
                {
                    _console.WriteLine("✔ VALIDATION OK");
                }
                else
                {
                    _console.WriteLine("✖ VALIDATION FAILED");
                    foreach (var error in e.GetProperty("errors").EnumerateArray())
                        _console.WriteLine($"    - {error.GetString()}");
                }

                break;

            case "repair.started":
                _console.WriteLine($"↻ repair attempt {e.GetProperty("attempt").GetInt32()}");
                break;

            case "promoted":
                _console.WriteLine($"✔ PROMOTED → {Text(e, "path")}");
                _console.WriteLine($"  launch: {Text(e, "launcher")}");
                if (e.TryGetProperty("install", out var install) && install.ValueKind == JsonValueKind.String)
                    _console.WriteLine($"  schedule install (displayed, never executed): {install.GetString()}");
                break;

            case "error":
                _console.WriteLine($"✖ [{Text(e, "code")}] {Text(e, "message")}");
                break;

            case "session.finished":
                _console.WriteLine($"■ {Text(e, "status")} (exit {e.GetProperty("exitCode").GetInt32()})");
                break;

            default:
                // Unknown kinds stay visible in raw form: the terminal must never hide
                // what the stream said.
                _console.WriteLine(e.GetRawText());
                break;
        }

        _console.Flush();
    }

    private static string Text(JsonElement e, string property) =>
        e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
