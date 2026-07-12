using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
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
    // default: reads auto-accepted (headless sessions must be able to observe);
    // everything else asks → denied when non-interactive
    [InlineData("default", "file_read", false, PermissionAction.Allow)]
    [InlineData("default", "directory_read", false, PermissionAction.Allow)]
    [InlineData("default", "codebase_map", false, PermissionAction.Allow)]
    [InlineData("default", "file_write", false, PermissionAction.Deny)]
    [InlineData("default", "shell_command", false, PermissionAction.Deny)]
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
    // unknown mode → treated as default (reads allowed, writes ask → deny headless)
    [InlineData("yolo", "file_read", false, PermissionAction.Allow)]
    [InlineData("yolo", "file_write", false, PermissionAction.Deny)]
    public async Task CheckAsync_maps_mode_and_tool_class_to_expected_action(
        string mode, string tool, bool interactive, PermissionAction expected)
    {
        var gate = new ModePermissionGate(interactive);

        var verdict = await gate.CheckAsync(
            tool, NoArgs, mode, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict.Action);
    }

    [Theory]
    // A self-declared access class (IBaseTool.Access) wins over the name tables:
    // an unknown tool declaring Read becomes readable in plan mode...
    [InlineData("plan", "my_custom_probe", ToolAccess.Read, PermissionAction.Allow)]
    [InlineData("acceptEdits", "my_custom_probe", ToolAccess.Read, PermissionAction.Allow)]
    // ...a tool declaring Edit is auto-accepted by acceptEdits but denied in plan...
    [InlineData("acceptEdits", "my_custom_editor", ToolAccess.Edit, PermissionAction.Allow)]
    [InlineData("plan", "my_custom_editor", ToolAccess.Edit, PermissionAction.Deny)]
    // ...and an Execute declaration overrides even a name the read table knows.
    [InlineData("plan", "file_read", ToolAccess.Execute, PermissionAction.Deny)]
    [InlineData("acceptEdits", "file_read", ToolAccess.Execute, PermissionAction.Deny)]
    // default mode auto-accepts a declared Read; a declared Execute still asks (→ deny headless).
    [InlineData("default", "my_custom_probe", ToolAccess.Read, PermissionAction.Allow)]
    [InlineData("default", "my_custom_runner", ToolAccess.Execute, PermissionAction.Deny)]
    // Unspecified keeps the name-table fallback intact.
    [InlineData("plan", "file_read", ToolAccess.Unspecified, PermissionAction.Allow)]
    public async Task CheckAsync_honours_the_declared_access_class(
        string mode, string tool, ToolAccess declared, PermissionAction expected)
    {
        var gate = new ModePermissionGate(isInteractive: false);

        var verdict = await gate.CheckAsync(
            tool, NoArgs, mode, declared, TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict.Action);
    }

    [Fact]
    public async Task Ask_survives_when_interactive()
    {
        var gate = new ModePermissionGate(isInteractive: true);

        var verdict = await gate.CheckAsync(
            "shell_command", NoArgs, "acceptEdits", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Ask, verdict.Action);
        Assert.Contains("approval", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_interactive_deny_message_explains_the_downgrade()
    {
        var gate = new ModePermissionGate(isInteractive: false);

        var verdict = await gate.CheckAsync(
            "file_write", NoArgs, "default", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("no interactive approval channel", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Plan_deny_message_names_the_mode()
    {
        var gate = new ModePermissionGate();

        var verdict = await gate.CheckAsync(
            "shell_command", NoArgs, "plan", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("plan mode", verdict.Message, StringComparison.OrdinalIgnoreCase);
    }
}
