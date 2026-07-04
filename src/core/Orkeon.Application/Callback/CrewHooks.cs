using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Callback;

/// <summary>
/// Hook that executes before crew kickoff.
/// </summary>
public sealed record BeforeKickoffHook
{
    /// <summary>Gets the name of this hook.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the function to execute when this hook is triggered.</summary>
    public Func<DomainCrew, System.Threading.Tasks.Task> Execute { get; init; } = _ => System.Threading.Tasks.Task.CompletedTask;
    /// <summary>Gets the priority of this hook (lower values execute first).</summary>
    public int Priority { get; init; }
    /// <summary>Gets a value indicating whether execution should continue if this hook throws an error.</summary>
    public bool ContinueOnError { get; init; } = true;
}

/// <summary>
/// Hook that executes after crew kickoff.
/// </summary>
public sealed record AfterKickoffHook
{
    /// <summary>Gets the name of this hook.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the function to execute when this hook is triggered.</summary>
    public Func<DomainCrew, object?, System.Threading.Tasks.Task> Execute { get; init; } = (_, __) => System.Threading.Tasks.Task.CompletedTask;
    /// <summary>Gets the priority of this hook (lower values execute first).</summary>
    public int Priority { get; init; }
    /// <summary>Gets a value indicating whether execution should continue if this hook throws an error.</summary>
    public bool ContinueOnError { get; init; } = true;
}
