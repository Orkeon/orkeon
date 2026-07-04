using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Agent.Commands.CreateAgent;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Services;

internal partial class AgentManagementService
{
    private readonly IConsoleAdapter _console;
    private readonly ConsoleInputService _input;
    private readonly ILogger<AgentManagementService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<AgentDto> _agents = [];

    public AgentManagementService(
        IConsoleAdapter console,
        ConsoleInputService input,
        ILogger<AgentManagementService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _console = console;
        _input = input;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task CreateAgentAsync()
    {
        _console.WriteLine("\n--- Create New Agent ---");

        var name = _input.GetRequiredString("Enter agent name: ");
        var role = _input.GetRequiredString("Enter agent role: ");
        var goal = _input.GetRequiredString("Enter agent goal: ");
        var backstory = _input.GetOptionalString("Enter agent backstory (optional): ");

        var availableTools = new List<string>
        {
            "WebSearch",
            "FileRead",
            "FileWrite",
            "Calculator",
            "CodeInterpreter"
        };

        var selectedTools = _input.GetMultipleChoices(
            "Select tools for the agent:",
            availableTools);

        var command = new CreateAgentCommand(
            Role: role,
            Goal: goal,
            Backstory: backstory ?? "No backstory provided",
            Tools: selectedTools);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateAgentCommand, AgentDto>>();
        var agentDto = await handler.HandleAsync(command);

        _agents.Add(agentDto);

        _console.WriteLine($"\nAgent '{name}' created successfully!");
        LogCreatedAgent(name, role);

        _input.WaitForKey();
    }

    public async Task CreateAgentAsync(string name, string role, string goal, string backstory)
    {
        var command = new CreateAgentCommand(
            Role: role,
            Goal: goal,
            Backstory: backstory,
            Tools: null);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateAgentCommand, AgentDto>>();
        var agentDto = await handler.HandleAsync(command);

        _agents.Add(agentDto);
        LogCreatedAgent(name, role);
    }

    public async Task ListAgentsAsync()
    {
        _console.WriteLine("\n--- List of Agents ---");

        if (_agents.Count == 0)
        {
            _console.WriteLine("No agents created yet.");
        }
        else
        {
            _console.WriteLine($"\nTotal agents: {_agents.Count}\n");
            _console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,-25} {2,-40}", "Name", "Role", "Goal"));
            _console.WriteLine(new string('-', 85));

            foreach (var agent in _agents)
            {
                _console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-20} {1,-25} {2,-40}",
                    TruncateString(agent.Id, 20),
                    TruncateString(agent.Role, 25),
                    TruncateString(agent.Goal, 40)));
            }
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public async Task DeleteAgentAsync()
    {
        _console.WriteLine("\n--- Delete Agent ---");

        if (_agents.Count == 0)
        {
            _console.WriteLine("No agents to delete.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        _console.WriteLine("Select agent to delete:");
        for (int i = 0; i < _agents.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {_agents[i].Id}");
        }

        var choice = _input.GetMenuChoice(1, _agents.Count);
        var agentToDelete = _agents[choice - 1];

        if (_input.GetYesNo($"Are you sure you want to delete agent '{agentToDelete.Id}'?"))
        {
            _agents.RemoveAt(choice - 1);
            _console.WriteLine($"Agent '{agentToDelete.Id}' deleted successfully!");
            LogDeletedAgent(agentToDelete.Id);
        }
        else
        {
            _console.WriteLine("Deletion cancelled.");
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public IReadOnlyList<AgentDto> Agents => _agents;

    private static string TruncateString(string str, int maxLength)
    {
        if (str.Length <= maxLength)
            return str;
        return string.Concat(str.AsSpan(0, maxLength - 3), "...");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Created agent: {AgentName} with role: {AgentRole}")]
    private partial void LogCreatedAgent(string agentName, string agentRole);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Deleted agent: {AgentId}")]
    private partial void LogDeletedAgent(string agentId);
}
