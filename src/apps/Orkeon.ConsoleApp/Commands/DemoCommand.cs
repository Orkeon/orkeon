using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Commands;

/// <summary>Quick demo: creates two sample agents, a crew, and a task.</summary>
internal sealed class DemoCommand : IMainMenuCommand
{
    public string Name => "demo";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Run a quick demo (creates sample agents/crews/tasks)";

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteCoreAsync();

        async Task<CommandResult> ExecuteCoreAsync()
        {
            context.Console.WriteLine("\n=== QUICK DEMO ===");
            context.Console.WriteLine("This will create a sample crew and execute a task.");
            context.Console.WriteLine("Press any key to continue or ESC to cancel...");

            var keyInfo = await context.Console.ReadKeyAsync(true, cancellationToken).ConfigureAwait(false);
            if (keyInfo.Key == ConsoleKey.Escape)
            {
                return CommandResult.Continue("Demo cancelled.");
            }

            var agentService = context.Scope.GetRequiredService<AgentManagementService>();
            var crewService = context.Scope.GetRequiredService<CrewManagementService>();
            var taskService = context.Scope.GetRequiredService<TaskManagementService>();

            context.Console.WriteLine("\nCreating demo agents...");
            await agentService.CreateAgentAsync(
                "Researcher", "Research Specialist",
                "Find and analyze information",
                "Expert in research and data analysis").ConfigureAwait(false);
            await agentService.CreateAgentAsync(
                "Writer", "Content Writer",
                "Create well-written content",
                "Skilled in creating engaging content").ConfigureAwait(false);

            context.Console.WriteLine("Creating demo crew...");
            await crewService.CreateCrewAsync(
                "Content Creation Team",
                "A team for creating high-quality content").ConfigureAwait(false);

            context.Console.WriteLine("Creating demo task...");
            await taskService.CreateTaskAsync(
                "Write about AI trends",
                "Research and write a brief article about AI trends in 2025",
                "A well-written article about AI trends").ConfigureAwait(false);

            context.Console.WriteLine("\nDemo setup complete!");
            context.Console.WriteLine("You can now execute the crew via 'crew run'.");
            return CommandResult.Continue();
        }
    }
}
