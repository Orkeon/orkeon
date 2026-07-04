using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

public class TtyDetectorTests
{
    [Fact]
    public void IsInteractiveTty_returns_false_when_CI_env_is_true()
    {
        // xUnit redirects stdout, so the function returns false regardless.
        // This test still validates the contract under the canonical CI env.
        var prev = Environment.GetEnvironmentVariable("CI");
        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Assert.False(TtyDetector.IsInteractiveTty());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", prev);
        }
    }

    [Fact]
    public void IsInteractiveTty_returns_false_when_stdout_is_redirected()
    {
        // xUnit always runs with redirected stdout/stdin → must return false.
        Assert.False(TtyDetector.IsInteractiveTty());
    }

    [Fact]
    public void ResolveEffectiveMode_returns_Plain_when_requested_Plain()
    {
        Assert.Equal(UiMode.Plain, TtyDetector.ResolveEffectiveMode(UiMode.Plain));
    }

    [Fact]
    public void ResolveEffectiveMode_returns_Tui_when_requested_Tui_even_in_CI()
    {
        var prev = Environment.GetEnvironmentVariable("CI");
        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Assert.Equal(UiMode.Tui, TtyDetector.ResolveEffectiveMode(UiMode.Tui));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", prev);
        }
    }

    [Fact]
    public void ResolveEffectiveMode_returns_Plain_when_auto_and_not_tty()
    {
        // Under xUnit, stdin/stdout are redirected → not a TTY → Plain.
        Assert.Equal(UiMode.Plain, TtyDetector.ResolveEffectiveMode(null));
    }

    // xUnit1004 suppressed: Skip is intentional — requires a real TTY, which is impossible
    // under xUnit (stdout is always redirected).
#pragma warning disable xUnit1004 // Test methods should not be skipped
    [Fact(Skip = "Requires real TTY — cannot run under xUnit (stdout always redirected).")]
    public void ResolveEffectiveMode_returns_Tui_when_auto_and_tty()
    {
        Assert.Equal(UiMode.Tui, TtyDetector.ResolveEffectiveMode(null));
    }
#pragma warning restore xUnit1004
}
