using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Task.Commands.CreateTask;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Task.DTOs;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.ConsoleApp.Services;

internal partial class TaskManagementService
{
    private readonly IConsoleAdapter _console;
    private readonly ConsoleInputService _input;
    private readonly ILogger<TaskManagementService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<TaskDto> _tasks = [];

    public TaskManagementService(
        IConsoleAdapter console,
        ConsoleInputService input,
        ILogger<TaskManagementService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _console = console;
        _input = input;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task CreateTaskAsync()
    {
        _console.WriteLine("\n--- Create New Task ---");

        var description = _input.GetRequiredString("Enter task description: ");
        var context = _input.GetOptionalString("Enter task context (optional): ");
        var expectedOutput = _input.GetRequiredString("Enter expected output: ");

        var command = new CreateTaskCommand(
            Description: description,
            ExpectedOutput: expectedOutput,
            AgentId: null);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateTaskCommand, TaskDto>>();
        var taskDto = await handler.HandleAsync(command);

        if (!string.IsNullOrEmpty(context))
        {
            taskDto = taskDto with
            {
                Context = ImmutableDictionary<string, object>.Empty.Add("context", context)
            };
        }

        _tasks.Add(taskDto);

        _console.WriteLine($"\nTask created successfully!");
        _console.WriteLine($"Description: {description}");
        _console.WriteLine($"Expected Output: {expectedOutput}");

        LogCreatedTask(description);

        _input.WaitForKey();
    }

    public async Task CreateTaskAsync(string description, string context, string expectedOutput)
    {
        var command = new CreateTaskCommand(
            Description: description,
            ExpectedOutput: expectedOutput,
            AgentId: null);

        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateTaskCommand, TaskDto>>();
        var taskDto = await handler.HandleAsync(command);

        if (!string.IsNullOrEmpty(context))
        {
            taskDto = taskDto with
            {
                Context = ImmutableDictionary<string, object>.Empty.Add("context", context)
            };
        }

        _tasks.Add(taskDto);
        LogCreatedTask(description);
    }

    public async Task ListTasksAsync()
    {
        _console.WriteLine("\n--- List of Tasks ---");

        if (_tasks.Count == 0)
        {
            _console.WriteLine("No tasks created yet.");
        }
        else
        {
            _console.WriteLine($"\nTotal tasks: {_tasks.Count}\n");

            for (int i = 0; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                _console.WriteLine($"{i + 1}. Task:");
                _console.WriteLine($"   Description: {task.Description}");
                _console.WriteLine($"   Expected Output: {task.ExpectedOutput}");

                if (task.Context.Count > 0)
                {
                    _console.WriteLine($"   Context: {string.Join(", ", task.Context.Keys)}");
                }

                if (!string.IsNullOrEmpty(task.AssignedAgent))
                {
                    _console.WriteLine($"   Assigned to: {task.AssignedAgent}");
                }
                else
                {
                    _console.WriteLine($"   Assigned to: [Not assigned]");
                }

                _console.WriteLine("");
            }
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public async Task AssignTaskAsync()
    {
        _console.WriteLine("\n--- Assign Task to Agent ---");

        if (_tasks.Count == 0)
        {
            _console.WriteLine("No tasks available. Please create tasks first.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        _console.WriteLine("Select task to assign:");
        for (int i = 0; i < _tasks.Count; i++)
        {
            var task = _tasks[i];
            var assignedStatus = !string.IsNullOrEmpty(task.AssignedAgent) ? $" [Assigned to {task.AssignedAgent}]" : " [Unassigned]";
            _console.WriteLine($"{i + 1}. {TruncateString(task.Description, 50)}{assignedStatus}");
        }

        var taskChoice = _input.GetMenuChoice(1, _tasks.Count);
        var selectedTask = _tasks[taskChoice - 1];

        _console.WriteLine("\nNote: Agent assignment requires integration with AgentManagementService.");
        _console.WriteLine("In a full implementation, you would select from available agents here.");
        _console.WriteLine($"Task '{selectedTask.Description}' selected for assignment.");

        LogTaskAssignmentInitiated(selectedTask.Description);

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public async Task DeleteTaskAsync()
    {
        _console.WriteLine("\n--- Delete Task ---");

        if (_tasks.Count == 0)
        {
            _console.WriteLine("No tasks to delete.");
            _input.WaitForKey();
            await Task.CompletedTask;
            return;
        }

        _console.WriteLine("Select task to delete:");
        for (int i = 0; i < _tasks.Count; i++)
        {
            _console.WriteLine($"{i + 1}. {TruncateString(_tasks[i].Description, 60)}");
        }

        var choice = _input.GetMenuChoice(1, _tasks.Count);
        var taskToDelete = _tasks[choice - 1];

        if (_input.GetYesNo($"Are you sure you want to delete this task?\n  '{taskToDelete.Description}'"))
        {
            _tasks.RemoveAt(choice - 1);
            _console.WriteLine("Task deleted successfully!");
            LogDeletedTask(taskToDelete.Description);
        }
        else
        {
            _console.WriteLine("Deletion cancelled.");
        }

        _input.WaitForKey();
        await Task.CompletedTask;
    }

    public IReadOnlyList<TaskDto> Tasks => _tasks;

    private static string TruncateString(string str, int maxLength)
    {
        if (str.Length <= maxLength)
            return str;
        return string.Concat(str.AsSpan(0, maxLength - 3), "...");
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Created task: {Description}")]
    private partial void LogCreatedTask(string description);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Task assignment initiated for: {TaskDescription}")]
    private partial void LogTaskAssignmentInitiated(string taskDescription);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Deleted task: {TaskDescription}")]
    private partial void LogDeletedTask(string taskDescription);
}
