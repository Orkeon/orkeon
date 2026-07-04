using Orkeon.Cli.Abstractions.Commands;

namespace Orkeon.Cli.Abstractions.Tests.Fixtures;

/// <summary>Command that records each invocation and returns a configurable result.</summary>
public sealed class RecordingCommand : IInteractiveCommand
{
    private readonly Func<CommandContext, CommandResult>? _resultFactory;
    private readonly Action<CommandContext>? _action;

    public RecordingCommand(
        string name,
        IReadOnlyList<string>? aliases = null,
        string description = "",
        Func<CommandContext, CommandResult>? result = null,
        Action<CommandContext>? action = null)
    {
        Name = name;
        Aliases = aliases ?? Array.Empty<string>();
        Description = description;
        _resultFactory = result;
        _action = action;
    }

    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public string Description { get; }
    public List<CommandContext> Invocations { get; } = new();

    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        Invocations.Add(context);
        _action?.Invoke(context);
        return Task.FromResult(_resultFactory?.Invoke(context) ?? CommandResult.Continue());
    }
}
