using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;

namespace Orkeon.Infrastructure.Flows;

/// <summary>
/// In-memory implementation of IFlowDefinition.
/// </summary>
public sealed class InMemoryFlowDefinition : IFlowDefinition
{
    /// <inheritdoc />
    public FlowId Id { get; set; } = FlowId.Create();
    /// <inheritdoc />
    public string Name { get; set; } = string.Empty;
    /// <inheritdoc />
    public string Description { get; set; } = string.Empty;
    /// <inheritdoc />
    public FlowType Type { get; set; } = FlowType.Sequential;
    /// <inheritdoc />
    public IReadOnlyList<FlowStep> Steps { get; set; } = Array.Empty<FlowStep>();
    /// <inheritdoc />
    public FlowConfiguration Configuration { get; set; } = new();

    /// <inheritdoc />
    public bool Validate(out IReadOnlyList<string> errors)
    {
        var errorList = new List<string>();
        errors = errorList;

        if (string.IsNullOrWhiteSpace(Name))
            errorList.Add("Flow name is required.");

        if (Steps.Count == 0)
            errorList.Add("Flow must have at least one step.");

        ValidateStepDefinitions(errorList);
        var stepIds = ValidateUniqueStepIds(errorList);
        ValidateCircularDependencies(stepIds, errorList);
        ValidateDependencyReferences(errorList);

        return errorList.Count == 0;
    }

    private void ValidateStepDefinitions(List<string> errors)
    {
        foreach (var step in Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"Step with ID '{step.Id}' must have a name.");

            if (string.IsNullOrWhiteSpace(step.Type))
                errors.Add($"Step '{step.Name}' must have a type.");
        }
    }

    private HashSet<string> ValidateUniqueStepIds(List<string> errors)
    {
        var stepIds = new HashSet<string>();
        foreach (var duplicateId in Steps.Where(step => !stepIds.Add(step.Id)).Select(step => step.Id))
        {
            errors.Add($"Duplicate step ID: '{duplicateId}'.");
        }
        return stepIds;
    }

    private void ValidateCircularDependencies(HashSet<string> stepIds, List<string> errors)
    {
        if (stepIds.Count == Steps.Count && HasCircularDependencies(out var cycle))
            errors.Add($"Circular dependency detected: {cycle}");
    }

    private void ValidateDependencyReferences(List<string> errors)
    {
        var allIds = Steps.Select(s => s.Id).ToHashSet();
        foreach (var step in Steps)
        {
            foreach (var dep in step.Dependencies)
            {
                if (!allIds.Contains(dep))
                    errors.Add($"Step '{step.Name}' depends on unknown step '{dep}'.");
            }
        }
    }

    private bool HasCircularDependencies(out string cycle)
    {
        cycle = string.Empty;
        var stepMap = Steps.ToDictionary(s => s.Id);
        var visited = new HashSet<FlowStepId>();
        var inStack = new HashSet<FlowStepId>();

        foreach (var step in Steps)
        {
            if (DetectCycle(step.Id, stepMap, visited, inStack, [], out cycle))
                return true;
        }

        return false;
    }

    private static bool DetectCycle(
        FlowStepId stepId,
        Dictionary<FlowStepId, FlowStep> stepMap,
        HashSet<FlowStepId> visited,
        HashSet<FlowStepId> inStack,
        List<FlowStepId> path,
        out string cycle)
    {
        cycle = string.Empty;

        if (inStack.Contains(stepId))
        {
            path.Add(stepId);
            cycle = string.Join(" -> ", path);
            return true;
        }

        if (visited.Contains(stepId))
            return false;

        visited.Add(stepId);
        inStack.Add(stepId);
        path.Add(stepId);

        if (stepMap.TryGetValue(stepId, out var step))
        {
            foreach (var dep in step.Dependencies)
            {
                if (DetectCycle(dep, stepMap, visited, inStack, [.. path], out cycle))
                    return true;
            }
        }

        inStack.Remove(stepId);
        return false;
    }
}
