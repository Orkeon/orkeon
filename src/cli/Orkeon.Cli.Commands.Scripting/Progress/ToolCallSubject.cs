using System.Text.Json;

namespace Orkeon.Cli.Commands.Scripting.Progress;

/// <summary>
/// What a tool call shows of its arguments: one argument named per tool, never the others. A tool that is not
/// listed shows its name alone, since nothing says its arguments are fit for the screen.
/// </summary>
public static class ToolCallSubject
{
    private const int MaxLength = 400;

    /// <summary>The text to show for a call of <paramref name="tool"/>, on one line; empty when it shows none.</summary>
    public static string Of(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var names = tool switch
        {
            "shell_command" => ["command"],
            "file_read" or "file_write" or "edit_file" or "directory_read" => ["path"],
            "directory_search" or "codebase_search" => ["query", "pattern"],
            _ => Array.Empty<string>(),
        };
        foreach (var name in names)
        {
            if (arguments.TryGetValue(name, out var value) && Text(value) is { Length: > 0 } text)
                return OneLine(text);
        }
        return string.Empty;
    }

    private static string? Text(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => null,
    };

    private static string OneLine(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxLength ? line : line[..MaxLength];
    }
}
