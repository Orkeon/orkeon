namespace Orkeon.Cli.Abstractions.Console.LineEditing;

/// <summary>
/// Minimal console surface the <see cref="LineEditor"/> renders against. Abstracted so the editing
/// logic (insert/delete/move/history/Tab) can be unit-tested without a real terminal.
/// </summary>
public interface ILineEditorConsole
{
    /// <summary>Usable column count of the terminal (≥ 1). Used for wrap-aware cursor placement.</summary>
    int Width { get; }

    /// <summary>Current cursor position (column, row).</summary>
    (int Left, int Top) GetCursor();

    /// <summary>Move the cursor to (left, top). Implementations clamp/ignore out-of-range targets.</summary>
    void SetCursor(int left, int top);

    /// <summary>Write text at the cursor without a trailing newline.</summary>
    void Write(string text);

    /// <summary>Write text followed by a newline.</summary>
    void WriteLine(string text);

    /// <summary>Block until a key is pressed and return it (intercepted — not echoed).</summary>
    ConsoleKeyInfo ReadKey();
}
