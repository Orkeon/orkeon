using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Security;

namespace Orkeon.Infrastructure.Tests.Security;

/// <summary>
/// F2 (exp07): mode → per-tool verdicts of <see cref="ModePermissionGate"/>, including the
/// non-interactive Ask→Deny downgrade and the fail-closed classification of unknown tools.
/// </summary>
public class ModePermissionGateTests
{
    private static readonly Dictionary<string, object?> NoArgs = new();

    [Theory]
    // default: ask on everything → denied when non-interactive
    [InlineData("default", "file_read", false, PermissionAction.Deny)]
    [InlineData("default", "file_write", false, PermissionAction.Deny)]
    // acceptEdits: reads AND file edits auto-accepted (SPEC §3.3); shell asks (→ deny non-interactive)
    [InlineData("acceptEdits", "file_read", false, PermissionAction.Allow)]
    [InlineData("acceptEdits", "codebase_search", false, PermissionAction.Allow)]
    [InlineData("acceptEdits", "symbol_source", false, PermissionAction.Allow)]
    [InlineData("acceptEdits", "file_write", false, PermissionAction.Allow)]
    [InlineData("acceptEdits", "shell_command", false, PermissionAction.Deny)]
    // bypassPermissions: everything allowed
    [InlineData("bypassPermissions", "file_write", false, PermissionAction.Allow)]
    [InlineData("bypassPermissions", "shell_command", false, PermissionAction.Allow)]
    // plan: reads allowed, writes denied outright (not ask)
    [InlineData("plan", "file_read", false, PermissionAction.Allow)]
    [InlineData("plan", "codebase_map", false, PermissionAction.Allow)]
    [InlineData("plan", "file_write", false, PermissionAction.Deny)]
    [InlineData("plan", "shell_command", false, PermissionAction.Deny)]
    // unknown tool → classified as write (fail-closed)
    [InlineData("acceptEdits", "mystery_tool", false, PermissionAction.Deny)]
    [InlineData("bypassPermissions", "mystery_tool", false, PermissionAction.Allow)]
    // unknown mode → treated as default
    [InlineData("yolo", "file_read", false, PermissionAction.Deny)]
    public async Task CheckAsync_maps_mode_and_tool_class_to_expected_action(
        string mode, string tool, bool interactive, PermissionAction expected)
    {
        var gate = new ModePermissionGate(interactive);

        var verdict = await gate.CheckAsync(tool, NoArgs, mode, TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict.Action);
    }

    [Fact]
    public async Task Ask_survives_when_interactive()
    {
        var gate = new ModePermissionGate(isInteractive: true);

        var verdict = await gate.CheckAsync("shell_command", NoArgs, "acceptEdits", TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Ask, verdict.Action);
        Assert.Contains("approval", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_interactive_deny_message_explains_the_downgrade()
    {
        var gate = new ModePermissionGate(isInteractive: false);

        var verdict = await gate.CheckAsync("file_write", NoArgs, "default", TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("no interactive approval channel", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Plan_deny_message_names_the_mode()
    {
        var gate = new ModePermissionGate();

        var verdict = await gate.CheckAsync("shell_command", NoArgs, "plan", TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("plan mode", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }
}
