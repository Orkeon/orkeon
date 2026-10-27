using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Abstractions.Tests.Fixtures;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Tests.Doubles;

namespace Orkeon.Cli.Tests.Commands;

public sealed class HelpCommandTests
{
    private static readonly string[] FooAliases = ["f", "fo"];

    private static (HelpCommand cmd, TestConsoleAdapter console, IServiceProvider sp) Build(DefaultCommandRegistry? defaults = null)
    {
        defaults ??= new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        var services = new ServiceCollection();
        services.AddSingleton(defaults);
        var sp = services.BuildServiceProvider();
        var console = new TestConsoleAdapter();
        return (new HelpCommand(), console, sp);
    }

    private static CommandContext MakeCtx(TestConsoleAdapter console, IServiceProvider sp)
        => new() { RawInput = "help", Args = Array.Empty<string>(), Console = console, Scope = sp };

    [Fact]
    public async Task Help_lists_default_commands_in_default_block()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry();
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[Default]", output);
        Assert.Contains("help, ?, h", output);
        Assert.Contains("exit, quit, q", output);
        Assert.Contains("clear, cls", output);
    }

    [Fact]
    public async Task Help_groups_specific_commands_by_first_token()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(
            new RecordingCommand("agent list", description: "List agents"),
            new RecordingCommand("agent create", description: "Create agent"),
            new RecordingCommand("crew list", description: "List crews"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[agent]", output);
        Assert.Contains("[crew]", output);
        Assert.Contains("agent list", output);
        Assert.Contains("agent create", output);
        Assert.Contains("crew list", output);
    }

    [Fact]
    public async Task Help_lists_mono_token_commands_in_no_group_block()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(
            new RecordingCommand("demo", description: "Run quick demo"),
            new RecordingCommand("qa", description: "Enter Q&A mode"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[(no group)]", output);
        Assert.Contains("demo", output);
        Assert.Contains("qa", output);
    }

    [Fact]
    public async Task Help_appends_fallback_in_default_action_block_when_present()
    {
        var (cmd, console, sp) = Build();
        var fallback = new RecordingCommand("ask", description: "Ask the crew a question");
        var specific = TestCommandRegistry.WithFallback(fallback);
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[Default action]", output);
        Assert.Contains("(any other input)", output);
        Assert.Contains("Ask the crew a question", output);
    }

    [Fact]
    public async Task Help_omits_default_action_when_no_fallback()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(new RecordingCommand("foo"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.DoesNotContain("[Default action]", output);
    }

    [Fact]
    public async Task Help_aliases_displayed_after_name_comma_separated()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(
            new RecordingCommand("foo", aliases: FooAliases, description: "Foo it"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("foo, f, fo", output);
    }

    [Fact]
    public async Task Help_empty_specific_registry_shows_only_default()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry();
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[Default]", output);
        Assert.DoesNotContain("[agent]", output);
        Assert.DoesNotContain("[Default action]", output);
    }

    [Fact]
    public async Task Help_no_runner_context_shows_warning()
    {
        var (cmd, console, sp) = Build();
        // Do not push any registry — RunnerContext.Current returns null.

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[Default]", output);
        Assert.Contains("(no specific runner active)", output);
    }

    [Fact]
    public async Task Help_prints_one_block_per_declared_group_in_registration_order_and_no_group_last()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(
            new StubGroupedCommand("zeta", "session", "Zeta"),
            new StubGroupedCommand("loose", null, "Loose"),
            new StubGroupedCommand("alpha", "dev", "Alpha"),
            new StubGroupedCommand("beta", "session", "Beta"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        var session = output.IndexOf("[session]", StringComparison.Ordinal);
        var dev = output.IndexOf("[dev]", StringComparison.Ordinal);
        var noGroup = output.IndexOf("[(no group)]", StringComparison.Ordinal);
        Assert.True(session >= 0 && session < dev && dev < noGroup, output);
        Assert.Equal(1, output.Split("[session]").Length - 1);
        Assert.True(output.IndexOf("zeta", StringComparison.Ordinal) < output.IndexOf("beta", StringComparison.Ordinal));
        Assert.True(output.IndexOf("beta", StringComparison.Ordinal) < output.IndexOf("alpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Help_prefers_the_declared_group_over_the_first_token_of_the_name()
    {
        var (cmd, console, sp) = Build();
        var specific = new TestCommandRegistry(
            new StubGroupedCommand("rag ingest", "knowledge", "Ingest"),
            new RecordingCommand("rag query", description: "Query"));
        using var _ = RunnerContext.Push(specific);

        await cmd.ExecuteAsync(MakeCtx(console, sp), CancellationToken.None);
        var output = console.GetOutput();

        Assert.Contains("[knowledge]", output);
        Assert.Contains("[rag]", output);
    }
}
