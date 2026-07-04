namespace Orkeon.Cli.Abstractions.Console.LineEditing;

/// <summary>
/// Production <see cref="ILineEditorConsole"/> over <see cref="System.Console"/>. Reads keys in
/// raw/intercept mode and positions the cursor for inline editing. All console access is guarded so
/// a resized or absent terminal degrades gracefully instead of throwing into the REPL loop.
/// </summary>
public sealed class SystemLineEditorConsole : ILineEditorConsole
{
    private const int FallbackWidth = 80;

    public int Width => SafeBufferWidth();

    public (int Left, int Top) GetCursor()
    {
        try { return (System.Console.CursorLeft, System.Console.CursorTop); }
        catch (IOException) { return (0, 0); }
    }

    public void SetCursor(int left, int top)
    {
        try
        {
            var w = SafeBufferWidth();
            var h = Math.Max(1, System.Console.BufferHeight);
            System.Console.SetCursorPosition(Math.Clamp(left, 0, w - 1), Math.Clamp(top, 0, h - 1));
        }
        catch (ArgumentOutOfRangeException) { /* terminal resized / at edge — best effort */ }
        catch (IOException) { /* no console — ignore */ }
    }

    public void Write(string text) => System.Console.Write(text);

    public void WriteLine(string text) => System.Console.WriteLine(text);

    public ConsoleKeyInfo ReadKey() => System.Console.ReadKey(intercept: true);

    private static int SafeBufferWidth()
    {
        try { return Math.Max(1, System.Console.BufferWidth); }
        catch (IOException) { return FallbackWidth; }
    }
}
