using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Agent.Commands.CreateAgent;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Crew.Commands.CreateCrew;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Task.Commands.CreateTask;
using Orkeon.Application.Task.DTOs;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>
/// Wires the agent, task and crew management services over a scripted console, fake create
/// handlers and a capturing logger, as the main menu resolves them.
/// </summary>
internal sealed class ManagementServicesHarness : IDisposable
{
    public FakeConsoleAdapter Console { get; } = new() { ScriptedLines = new(), ScriptedKeys = new() };
    public FakeCreateAgentCommandHandler AgentHandler { get; } = new();
    public FakeCreateCrewCommandHandler CrewHandler { get; } = new();
    public FakeCreateTaskCommandHandler TaskHandler { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();
    public ServiceProvider Provider { get; }

    public ManagementServicesHarness()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(Logs).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IConsoleAdapter>(Console);
        services.AddSingleton(new ConsoleInputService(Console));
        services.AddSingleton<ICommandHandler<CreateAgentCommand, AgentDto>>(AgentHandler);
        services.AddSingleton<ICommandHandler<CreateCrewCommand, CrewDto>>(CrewHandler);
        services.AddSingleton<ICommandHandler<CreateTaskCommand, TaskDto>>(TaskHandler);
        services.AddSingleton<AgentManagementService>();
        services.AddSingleton<TaskManagementService>();
        services.AddSingleton<CrewManagementService>();
        Provider = services.BuildServiceProvider();
    }

    public AgentManagementService Agents => Provider.GetRequiredService<AgentManagementService>();
    public TaskManagementService Tasks => Provider.GetRequiredService<TaskManagementService>();
    public CrewManagementService Crews => Provider.GetRequiredService<CrewManagementService>();

    /// <summary>Queues lines answered to the next line reads.</summary>
    public void Lines(params string?[] lines)
    {
        foreach (var line in lines)
            Console.ScriptedLines!.Enqueue(line);
    }

    /// <summary>Queues keys answered to the next key reads (yes/no prompts, "press any key").</summary>
    public void Keys(params ConsoleKey[] keys)
    {
        foreach (var key in keys)
            Console.ScriptedKeys!.Enqueue(key);
    }

    public CommandContext Context(string rawInput) => new()
    {
        RawInput = rawInput,
        Args = Array.Empty<string>(),
        Console = Console,
        Scope = Provider,
    };

    public bool Logged(string fragment)
        => Logs.Logger.Entries.Any(e => e.Message.Contains(fragment, StringComparison.Ordinal));

    public void Dispose() => Provider.Dispose();
}
