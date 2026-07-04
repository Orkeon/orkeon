// =====================================================================
// TUI key-event diagnostic.
//
// Standalone exe that boots Terminal.Gui exactly like our real TUI host,
// subscribes to EVERY keystroke source, and writes the full trace to
// /tmp/tui-keytest.log so we can see what (if anything) arrives when the
// user presses Ctrl+C in their actual terminal.
//
// Usage:
//   dotnet run --project examples/runners/tui-keytest
// then press Ctrl+C several times, plus a few control letters for sanity
// (Ctrl+L, Ctrl+G, Ctrl+Q to exit). Share /tmp/tui-keytest.log.
// =====================================================================

using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

const string LogPath = "/tmp/tui-keytest.log";
File.WriteAllText(LogPath, "");
void Log(string msg) =>
    File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {msg}\n");

Log("=== Boot ===");
Log($"OS: {Environment.OSVersion}");
Log($"TERM: {Environment.GetEnvironmentVariable("TERM")}");
Log($"LANG: {Environment.GetEnvironmentVariable("LANG")}");
Log($"IsInputRedirected: {Console.IsInputRedirected}");
Log($"IsOutputRedirected: {Console.IsOutputRedirected}");
Log($"TreatControlCAsInput initial: {Console.TreatControlCAsInput}");

// A SIGINT-style signal handler at the OS level. If Ctrl+C is being converted
// to a signal instead of a key event, THIS will fire instead of Application.KeyDown.
Console.CancelKeyPress += (_, e) =>
{
    Log("!!! Console.CancelKeyPress fired (Ctrl+C reached the .NET runtime as SIGINT, NOT as a key)");
    e.Cancel = true; // don't actually quit
};
Log("Subscribed to Console.CancelKeyPress");

Application.Init("dotnet");
Log($"After Init: Driver={Application.Driver?.GetType().FullName ?? "<null>"}");

// Try to switch Ctrl+C to keystroke mode.
try
{
    Console.TreatControlCAsInput = true;
    Log($"TreatControlCAsInput now: {Console.TreatControlCAsInput}");
}
catch (Exception ex)
{
    Log($"Could not set TreatControlCAsInput: {ex.GetType().Name}: {ex.Message}");
}

// 'top' is the caller-created toplevel: Application.Run does NOT auto-dispose it,
// so own it with 'using' — disposing the Window also disposes its child views
// (the instruction Label and the StatusBar added below).
using var top = new Window
{
    Title = "Press keys (especially Ctrl+C). Ctrl+Q to exit.",
};

// Status bar with a Ctrl+Q exit shortcut so the user has a clean way to stop.
var quit = new Shortcut(Key.Q.WithCtrl, "Quit", () => { Log("Quit shortcut fired"); Application.RequestStop(top); }, "Exit");
quit.BindKeyToApplication = true;

// Construct the children inline and transfer ownership to 'top' in a single Add(...)
// call: the parent Window disposes its child views, so no child outlives 'top'.
top.Add(
    new Label
    {
        X = 1, Y = 1,
        Width = Dim.Fill(1),
        Height = 6,
        Text = "Press these keys then Ctrl+Q:\n  - Ctrl+C  (the one we want to diagnose)\n  - Ctrl+L, Ctrl+G  (sanity — known to work)\n  - 'a' 'b' 'c'  (plain letters)\n\nAll events are logged to " + LogPath,
    },
    new StatusBar(new[] { quit })
    {
        X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1,
    });

int ctrlCDetected = 0;

// Subscribe to EVERY plausible keystroke source, log what arrives where.
Application.KeyDown += (_, key) =>
{
    var ctrl = key.IsCtrl ? "Ctrl+" : "";
    var shift = key.IsShift ? "Shift+" : "";
    var alt = key.IsAlt ? "Alt+" : "";
    Log($"Application.KeyDown: KeyCode={key.KeyCode} ({(int)key.KeyCode:X}) {ctrl}{shift}{alt} AsRune='{key.AsRune}' Handled={key.Handled}");

    // Replicate the handler that's now in TerminalGuiHost — verifies the condition matches.
    var stripped = key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);
    Log($"  stripped = {stripped} ({(int)stripped:X}); KeyCode.C = {KeyCode.C} ({(int)KeyCode.C:X}); equal={stripped == KeyCode.C}");
    if (key.IsCtrl && stripped == KeyCode.C)
    {
        ctrlCDetected++;
        Log($"  >>> Ctrl+C MATCHED in handler (count={ctrlCDetected})");
    }
};

top.KeyDown += (_, key) =>
{
    Log($"Window(top).KeyDown: KeyCode={key.KeyCode} ({(int)key.KeyCode:X}) Handled={key.Handled}");
};

Log("=== Entering Application.Run ===");
try
{
    Application.Run(top, errorHandler: null);
    Log("=== Application.Run returned normally ===");
}
catch (Exception ex)
{
    Log($"!!! Application.Run threw: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    Application.Shutdown();
    Log("=== Shutdown done ===");
}

Console.WriteLine($"\nDiag log written to {LogPath}");
Console.WriteLine($"Please share its contents.");
