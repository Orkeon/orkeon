using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Streaming;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;

namespace Orkeon.Cli.Commands.Scripting.Tests.Streaming;

/// <summary>
/// exp07 F5 L3: the native console delta sink writes deltas inline and terminates the
/// line at end of turn; its DI registration is a strict config opt-in.
/// </summary>
public sealed class ConsoleLlmDeltaSinkTests
{
    [Fact]
    public void Deltas_are_written_inline_and_the_turn_terminator_closes_the_line()
    {
        var console = new ScriptedTestConsole();
        var sink = new ConsoleLlmDeltaSink(console);

        sink.OnDelta("Hel");
        sink.OnDelta("lo");
        sink.OnTurnCompleted();

        Assert.Equal("Hello" + Environment.NewLine, console.Output);
    }

    [Fact]
    public void Registration_is_a_config_opt_in()
    {
        var disabled = BuildProvider(enabled: null);
        Assert.Null(disabled.GetService<ILlmDeltaSink>());

        var off = BuildProvider(enabled: "false");
        Assert.Null(off.GetService<ILlmDeltaSink>());

        var on = BuildProvider(enabled: "true");
        Assert.IsType<ConsoleLlmDeltaSink>(on.GetService<ILlmDeltaSink>());
    }

    private static ServiceProvider BuildProvider(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
            values["Orkeon:Cli:ConsoleStreaming:Enabled"] = enabled;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConsoleAdapter>(new ScriptedTestConsole());
        services.AddLlmConsoleStreaming(configuration);
        return services.BuildServiceProvider();
    }
}
