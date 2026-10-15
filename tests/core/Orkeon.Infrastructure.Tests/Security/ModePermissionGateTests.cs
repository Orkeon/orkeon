using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Security;

/// <summary>
/// Mode → per-tool verdicts of <see cref="ModePermissionGate"/>, including the
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

    [Theory]
    [InlineData("file_write", ToolAccess.Unspecified)]
    [InlineData("shell_command", ToolAccess.Execute)]
    [InlineData("my_tool", ToolAccess.Edit)]
    public async Task DontAsk_denies_what_it_does_not_allow_even_with_an_approval_channel(string tool, ToolAccess declared)
    {
        // dontAsk never asks: with an interactive channel, normalising it to default would turn
        // these calls into a prompt, the opposite of the mode.
        var gate = new ModePermissionGate(isInteractive: true);

        var verdict = await gate.CheckAsync(
            tool, NoArgs, "dontAsk", declared, TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("dontAsk", verdict.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DontAsk_allows_a_read(bool interactive)
    {
        var gate = new ModePermissionGate(interactive);

        var verdict = await gate.CheckAsync(
            "file_read", NoArgs, "dontAsk", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
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

    [Theory]
    [InlineData(ToolApprovalDecision.AllowOnce)]
    [InlineData(ToolApprovalDecision.AllowForSession)]
    public async Task An_ask_the_operator_allows_through_the_channel_is_allowed(ToolApprovalDecision decision)
    {
        var channel = new StubToolApprovalChannel { Decision = decision };
        var gate = new ModePermissionGate(channel);
        var arguments = new Dictionary<string, object?> { ["command"] = "npm test" };

        var verdict = await gate.CheckAsync(
            "shell_command", arguments, "acceptEdits", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
        var request = Assert.Single(channel.Requests);
        Assert.Equal("shell_command", request.ToolName);
        Assert.Equal("acceptEdits", request.Mode);
        Assert.Equal("npm test", request.Arguments["command"]);
        Assert.Contains("requires approval", request.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ask_the_operator_denies_is_denied_with_the_reason_and_the_refusal()
    {
        var channel = new StubToolApprovalChannel { Decision = ToolApprovalDecision.Deny };
        var gate = new ModePermissionGate(channel);

        var verdict = await gate.CheckAsync(
            "file_write", NoArgs, "default", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Equal(
            "tool 'file_write' requires approval (mode 'default'). The operator denied the call.",
            verdict.Message);
    }

    [Fact]
    public async Task An_unknown_mode_is_put_to_the_operator_as_the_default_mode()
    {
        var channel = new StubToolApprovalChannel();
        var gate = new ModePermissionGate(channel);

        await gate.CheckAsync("file_write", NoArgs, "yolo", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("default", Assert.Single(channel.Requests).Mode);
    }

    [Theory]
    [InlineData("default", "file_read", PermissionAction.Allow)]
    [InlineData("acceptEdits", "file_write", PermissionAction.Allow)]
    [InlineData("bypassPermissions", "shell_command", PermissionAction.Allow)]
    [InlineData("plan", "file_write", PermissionAction.Deny)]
    [InlineData("dontAsk", "shell_command", PermissionAction.Deny)]
    public async Task The_channel_is_asked_only_when_the_mode_asks(string mode, string tool, PermissionAction expected)
    {
        var channel = new StubToolApprovalChannel();
        var gate = new ModePermissionGate(channel);

        var verdict = await gate.CheckAsync(tool, NoArgs, mode, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, verdict.Action);
        Assert.Empty(channel.Requests);
    }

    // ── session rules (IToolPermissionRules) ─────────────────────────────────

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> Command(string command) => new() { ["command"] = command };

    [Fact]
    public async Task An_allow_rule_runs_the_call_without_asking()
    {
        var channel = new StubToolApprovalChannel { Decision = ToolApprovalDecision.Deny };
        var gate = new ModePermissionGate(channel) { Rules = new StubToolPermissionRules().Allow("file_write") };

        var verdict = await gate.CheckAsync("file_write", NoArgs, "default", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task An_allow_rule_on_an_argument_covers_only_the_calls_it_matches()
    {
        var channel = new StubToolApprovalChannel { Decision = ToolApprovalDecision.Deny };
        var gate = new ModePermissionGate(channel)
        {
            Rules = new StubToolPermissionRules().Allow("shell_command", "command", "npm test*"),
        };

        var matching = await gate.CheckAsync("shell_command", Command("npm test --watch=false"), "default", cancellationToken: Ct);
        var other = await gate.CheckAsync("shell_command", Command("npm run build"), "default", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Allow, matching.Action);
        Assert.Equal(PermissionAction.Deny, other.Action);
        Assert.Equal("npm run build", Assert.Single(channel.Requests).Arguments["command"]);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("acceptEdits")]
    [InlineData("bypassPermissions")]
    [InlineData("dontAsk")]
    [InlineData("plan")]
    public async Task A_deny_rule_refuses_the_call_in_every_mode_without_asking(string mode)
    {
        var channel = new StubToolApprovalChannel();
        var gate = new ModePermissionGate(channel)
        {
            Rules = new StubToolPermissionRules().Deny("shell_command", "command", "rm *"),
        };

        var verdict = await gate.CheckAsync("shell_command", Command("rm -r build"), mode, cancellationToken: Ct);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Equal("tool 'shell_command' is denied by the session rule 'deny shell_command command=rm *'.", verdict.Message);
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task A_deny_rule_wins_over_an_allow_rule()
    {
        var gate = new ModePermissionGate(new StubToolApprovalChannel())
        {
            Rules = new StubToolPermissionRules().Allow("shell_command").Deny("shell_command", "command", "git push*"),
        };

        var verdict = await gate.CheckAsync("shell_command", Command("git push origin main"), "default", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
    }

    [Fact]
    public async Task An_allow_rule_does_not_widen_plan_mode()
    {
        var gate = new ModePermissionGate(new StubToolApprovalChannel())
        {
            Rules = new StubToolPermissionRules().Allow("file_write"),
        };

        var verdict = await gate.CheckAsync("file_write", NoArgs, "plan", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Deny, verdict.Action);
        Assert.Contains("plan mode", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DontAsk_runs_what_a_rule_allows_and_denies_the_rest_without_asking()
    {
        var channel = new StubToolApprovalChannel();
        var gate = new ModePermissionGate(channel) { Rules = new StubToolPermissionRules().Allow("file_write") };

        var allowed = await gate.CheckAsync("file_write", NoArgs, "dontAsk", cancellationToken: Ct);
        var denied = await gate.CheckAsync("shell_command", Command("npm test"), "dontAsk", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Allow, allowed.Action);
        Assert.Equal(PermissionAction.Deny, denied.Action);
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task An_allow_rule_runs_the_call_in_a_headless_session()
    {
        var gate = new ModePermissionGate(isInteractive: false) { Rules = new StubToolPermissionRules().Allow("shell_command") };

        var verdict = await gate.CheckAsync("shell_command", Command("npm test"), "default", cancellationToken: Ct);

        Assert.Equal(PermissionAction.Allow, verdict.Action);
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
