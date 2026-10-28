namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>What a task declares about decisions, for the crew-level checks.</summary>
public sealed record DecisionTaskDeclaration(string Name, TaskDecision? Decision, TaskCondition? Condition, bool HasAgent);
