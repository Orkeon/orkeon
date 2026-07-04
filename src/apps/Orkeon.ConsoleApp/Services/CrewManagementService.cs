using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Commands.CreateCrew;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Crew.DTOs;
using Orkeon.Application.Task.DTOs;
using Orkeon.Cli.Abstractions.Console;
using DomainProcessType = Orkeon.Domain.SharedKernel.ValueObjects.ProcessType;

namespace Orkeon.ConsoleApp.Services;

internal partial class CrewManagementService
{
    private readonly IConsoleAdapter _console;
    private readonly ConsoleInputService _input;
    private readonly ILogger<CrewManagementService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentManagementService _agentService;
    private readonly TaskManagementService _taskService;
    private readonly List<CrewDto> _crews = [];
    private readonly Dictionary<string, List<AgentDto>> _crewAgents = [];
    private readonly Dictionary<string, List<TaskDto>> _crewTasks = [];

    public CrewManagementService(
        IConsoleAdapter console,
        ConsoleInputService input,
        ILogger<CrewManagementService> logger,
        IServiceScopeFactory scopeFactory,
        AgentManagementService agentService,
        TaskManagementService taskService)
    {
        _console = console;
        _input = input;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _agentService = agentService;
        _taskService = taskService;
    }

    public async Task CreateCrewAsync()
    {
        _console.WriteLine("\n--- Create New Crew ---");

        var name = _input.GetRequiredString("Enter crew name: ");
        var description = _input.GetOptionalString("Enter crew description (optional): ");

        var processOptions = new List<string> { "sequential", "hierarchical" };
        _console.WriteLine("\nSelect process type:");
        for (int i = 0; i < processOptions.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {processOptions[i]}");
        }
        var processChoice = _input.GetMenuChoice(1, processOptions.Count);
        var process = processChoice == 1 ? DomainProcessType.Sequential : DomainProcessType.Hierarchical;

        var command = new CreateCrewCommand(
            Name: name,
            Goal: description ?? "No description provided",
            ProcessType: process,
            AgentIds: null);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCrewCommand, CrewDto>>();
        var crewDto = await handler.HandleAsync(command);

        _crews.Add(crewDto);
        _crewAgents[crewDto.Id] = [];
        _crewTasks[crewDto.Id] = [];

        _console.WriteLine($"\nCrew '{name}' created successfully with {process} process!");
        LogCreatedCrew(name);

        if (_input.GetYesNo("Would you like to add agents to this crew now?"))
        {
            await AddAgentsToCrewInteractiveAsync(crewDto);
        }

        _input.WaitForKey();
    }

    public async Task CreateCrewAsync(string name, string description)
    {
        var command = new CreateCrewCommand(
            Name: name,
            Goal: description,
            ProcessType: DomainProcessType.Sequential,
            AgentIds: null);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateCrewCommand, CrewDto>>();
        var crewDto = await handler.HandleAsync(command);

        _crews.Add(crewDto);
        _crewAgents[crewDto.Id] = [];
        _crewTasks[crewDto.Id] = [];
        LogCreatedCrew(name);
    }

    public async Task ListCrewsAsync()
    {
        _console.WriteLine("\n--- List of Crews ---");

        if (_crews.Count == 0)
        {
            _console.WriteLine("No crews created yet.");
        }
        else
        {
            _console.WriteLine($"\nTotal crews: {_crews.Count}\n");
            _console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-25} {1,-15} {2,-10} {3,-10}", "Name", "Process", "Agents", "Tasks"));
            _console.WriteLine(new string('-', 60));

            foreach (var crew in _crews)
            {
                var agentCount = _crewAgents.TryGetValue(crew.Id, out var agents) ? agents.Count : 0;
                var taskCount = _crewTasks.TryGetValue(crew.Id, out var tasks) ? tasks.Count : 0;
                _console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-25} {1,-15} {2,-10} {3,-10}",
                    TruncateString(crew.Name, 25),
                    crew.ProcessType.ToString(),
                    agentCount,
                    taskCount));
            }
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public async Task AddAgentToCrewAsync()
    {
        _console.WriteLine("\n--- Add Agent to Crew ---");

        if (_crews.Count == 0)
        {
            _console.WriteLine("No crews available. Please create a crew first.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        _console.WriteLine("Select crew:");
        for (int i = 0; i < _crews.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {_crews[i].Name}");
        }

        var crewChoice = _input.GetMenuChoice(1, _crews.Count);
        var crew = _crews[crewChoice - 1];

        await AddAgentsToCrewInteractiveAsync(crew);
        _input.WaitForKey();
    }

    private async Task AddAgentsToCrewInteractiveAsync(CrewDto crew)
    {
        var agents = _agentService.Agents;

        if (agents.Count == 0)
        {
            _console.WriteLine("No agents available. Please create agents first.");
            await Task.CompletedTask;
            return;
        }

        var crewAgentList = _crewAgents.TryGetValue(crew.Id, out var existing) ? existing : [];
        DisplayAvailableAgents(agents, crewAgentList);

        _console.WriteLine("\nEnter agent numbers to add (comma-separated) or press Enter to skip:");
        var input = (await _console.ReadLineAsync().ConfigureAwait(false))?.Trim();

        if (!string.IsNullOrEmpty(input))
        {
            AddSelectedAgentsToCrew(input, agents, crew, crewAgentList);
        }
    }

    private void DisplayAvailableAgents(IReadOnlyList<AgentDto> agents, List<AgentDto> crewAgents)
    {
        _console.WriteLine("\nAvailable agents:");
        for (int i = 0; i < agents.Count; i++)
        {
            var isInCrew = crewAgents.Any(a => a.Id == agents[i].Id);
            var status = isInCrew ? " [Already in crew]" : "";
            _console.WriteLine($"{i + 1}. {agents[i].Id} - {agents[i].Role}{status}");
        }
    }

    private void AddSelectedAgentsToCrew(string input, IReadOnlyList<AgentDto> agents, CrewDto crew, List<AgentDto> crewAgentList)
    {
        var numbers = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var number in numbers)
        {
            if (!int.TryParse(number, out int index) || index < 1 || index > agents.Count)
                continue;

            var agent = agents[index - 1];

            if (crewAgentList.Any(a => a.Id == agent.Id))
            {
                _console.WriteLine($"Agent '{agent.Id}' is already in the crew");
                continue;
            }

            crewAgentList.Add(agent);
            _console.WriteLine($"Added agent '{agent.Id}' to crew '{crew.Name}'");
            LogAddedAgentToCrew(agent.Id, crew.Name);
        }
    }

    public async Task DeleteCrewAsync()
    {
        _console.WriteLine("\n--- Delete Crew ---");

        if (_crews.Count == 0)
        {
            _console.WriteLine("No crews to delete.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        _console.WriteLine("Select crew to delete:");
        for (int i = 0; i < _crews.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {_crews[i].Name}");
        }

        var choice = _input.GetMenuChoice(1, _crews.Count);
        var crewToDelete = _crews[choice - 1];

        if (_input.GetYesNo($"Are you sure you want to delete crew '{crewToDelete.Name}'?"))
        {
            _crews.RemoveAt(choice - 1);
            _crewAgents.Remove(crewToDelete.Id);
            _crewTasks.Remove(crewToDelete.Id);
            _console.WriteLine($"Crew '{crewToDelete.Name}' deleted successfully!");
            LogDeletedCrew(crewToDelete.Name);
        }
        else
        {
            _console.WriteLine("Deletion cancelled.");
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public async Task ExecuteCrewAsync()
    {
        _console.WriteLine("\n--- Execute Crew ---");

        if (_crews.Count == 0)
        {
            _console.WriteLine("No crews available. Please create a crew first.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        var selectedCrew = SelectCrewForExecution();
        var crewAgentList = _crewAgents.TryGetValue(selectedCrew.Id, out var agents) ? agents : [];
        var crewTaskList = _crewTasks.TryGetValue(selectedCrew.Id, out var tasks) ? tasks : [];

        if (crewAgentList.Count == 0)
        {
            _console.WriteLine($"Crew '{selectedCrew.Name}' has no agents. Please add agents first.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        if (crewTaskList.Count == 0)
        {
            PromptUserToSelectTasks(crewTaskList);

            if (crewTaskList.Count == 0)
            {
                _console.WriteLine("No tasks selected. Execution cancelled.");
                _input.WaitForKey();
                await Task.CompletedTask;
                return;
            }
        }

        await SimulateCrewExecutionAsync(selectedCrew, crewAgentList, crewTaskList);

        LogExecutedCrew(selectedCrew.Name, crewTaskList.Count);

        _input.WaitForKey();
    }

    private CrewDto SelectCrewForExecution()
    {
        _console.WriteLine("Select crew to execute:");
        for (int i = 0; i < _crews.Count; i++)
        {
            var crew = _crews[i];
            var agentCount = _crewAgents.TryGetValue(crew.Id, out var a) ? a.Count : 0;
            var taskCount = _crewTasks.TryGetValue(crew.Id, out var t) ? t.Count : 0;
            _console.WriteLine($"{i + 1}. {crew.Name} ({agentCount} agents, {taskCount} tasks)");
        }

        var choice = _input.GetMenuChoice(1, _crews.Count);
        return _crews[choice - 1];
    }

    private void PromptUserToSelectTasks(List<TaskDto> crewTaskList)
    {
        var tasks = _taskService.Tasks;
        if (tasks.Count == 0)
            return;

        _console.WriteLine("\nAvailable tasks:");
        for (int i = 0; i < tasks.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {tasks[i].Description}");
        }

        _console.WriteLine("\nSelect tasks to execute (comma-separated):");
        var input = _console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
            return;

        var numbers = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var number in numbers)
        {
            if (int.TryParse(number, out int index) && index > 0 && index <= tasks.Count)
            {
                crewTaskList.Add(tasks[index - 1]);
            }
        }
    }

    private async Task SimulateCrewExecutionAsync(CrewDto selectedCrew, List<AgentDto> crewAgents, List<TaskDto> crewTasks)
    {
        _console.WriteLine($"\n[SIMULATION] Executing crew '{selectedCrew.Name}'...");
        _console.WriteLine($"Process type: {selectedCrew.ProcessType}");
        _console.WriteLine($"Agents involved: {string.Join(", ", crewAgents.Select(a => a.Id))}");
        _console.WriteLine($"Tasks to execute: {crewTasks.Count}");

        foreach (var task in crewTasks)
        {
            _console.WriteLine($"\n[SIMULATION] Executing task: {task.Description}");
            await Task.Delay(100);

            if (selectedCrew.ProcessType == "Sequential")
            {
                await SimulateSequentialExecutionAsync(crewAgents);
            }
            else
            {
                await SimulateHierarchicalExecutionAsync(crewAgents);
            }

            _console.WriteLine($"  [SIMULATION] Task completed!");
        }

        PrintSimulationSummary();
    }

    private async Task SimulateSequentialExecutionAsync(List<AgentDto> crewAgents)
    {
        foreach (var agent in crewAgents)
        {
            _console.WriteLine($"  [SIMULATION] {agent.Id} is working on the task...");
            await Task.Delay(50);
        }
    }

    private async Task SimulateHierarchicalExecutionAsync(List<AgentDto> crewAgents)
    {
        var manager = crewAgents.FirstOrDefault();
        if (manager == null)
            return;

        _console.WriteLine($"  [SIMULATION] Manager {manager.Id} is delegating work...");
        await Task.Delay(50);

        foreach (var agent in crewAgents.Skip(1))
        {
            _console.WriteLine($"  [SIMULATION] {agent.Id} received delegation...");
            await Task.Delay(50);
        }
    }

    private void PrintSimulationSummary()
    {
        _console.WriteLine($"\n[SIMULATION] Crew execution completed!");
        _console.WriteLine("\nNote: This is a simulation. In a real implementation, the crew would:");
        _console.WriteLine("- Use LLM providers to generate responses");
        _console.WriteLine("- Execute tools and interact with external systems");
        _console.WriteLine("- Store results in memory providers");
    }

    public IReadOnlyList<CrewDto> Crews => _crews;

    private static string TruncateString(string str, int maxLength)
    {
        if (str.Length <= maxLength)
            return str;
        return string.Concat(str.AsSpan(0, maxLength - 3), "...");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Created crew: {CrewName}")]
    private partial void LogCreatedCrew(string crewName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Added agent {AgentId} to crew {CrewName}")]
    private partial void LogAddedAgentToCrew(string agentId, string crewName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Deleted crew: {CrewName}")]
    private partial void LogDeletedCrew(string crewName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Executed crew: {CrewName} with {TaskCount} tasks")]
    private partial void LogExecutedCrew(string crewName, int taskCount);
}
