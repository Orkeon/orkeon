namespace Orkeon.Cli.TerminalGui.Tests.EndToEnd;

// TUI-08 originally specified an end-to-end test fixture using Terminal.Gui's FakeDriver.
// That driver does not exist in Terminal.Gui v2.1.0; the new testing surface is
// Terminal.Gui.Testing.InputInjector (see TUI-12). Even with InputInjector, the v2.1.0
// ModuleInitializer crashes inside xUnit test processes (TypeLoadException on
// MemberNotNullWhenAttribute via Microsoft.TestPlatform.CoreUtilities).
//
// Decision: end-to-end coverage is deferred to TUI-12. The functional pipeline is
// covered today by:
//   - 39 in-process tests (TtyDetector, TerminalGuiOptions, LooksLikePrompt heuristic,
//     log Format helpers, NextLevel cycle, DI registration shape).
//   - The smoke test path documented in TUI-09 (manual run on experiment 05).
// Skip is intentional. End-to-end coverage is deferred to TUI-12
// because Terminal.Gui 2.1.0 ModuleInitializer crashes under xUnit (verified).
// These tests are reported as Skipped (not Passed) in CI via [Fact(Skip = ...)].
//
// xUnit1004 ("test methods should not be skipped") is silenced because the skip
// markers are deliberate CI-visible placeholders until TUI-12 lands.
#pragma warning disable xUnit1004
public class TerminalGuiEndToEndTests
{
    private const string SkipReason = "End-to-end tests pending Terminal.Gui 2.1.x fix — see TUI-12";

    [Fact(Skip = SkipReason)]
    public void Boot_run_quit_round_trip() { }

    [Fact(Skip = SkipReason)]
    public void Repl_writes_to_history_and_logs_appear_in_pane() { }

    [Fact(Skip = SkipReason)]
    public void CtrlQ_stops_application() { }
}
#pragma warning restore xUnit1004
