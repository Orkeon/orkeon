using System.Globalization;
using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Interfaces;

namespace Orkeon.Infrastructure.Parsing;

/// <summary>
/// Reads the crew planner's reply (GAP-31): <c>{"plans": [{"task": 1, "plan": "…"}]}</c>, one
/// step-by-step plan per task, by the number the planning prompt gave the task. It tolerates what
/// models put around the object — a <c>```json</c> fence, a sentence (<see cref="LlmJsonText"/>) —,
/// property names in any case, a number written as a string and a plan written as a list of steps.
/// A number given twice keeps its first plan, a number the crew does not have and an entry without
/// a number or a plan are ignored, each with a warning. A reply without a <c>plans</c> array cannot
/// be read: the planner is asked once more, with the reason this reader gives.
/// </summary>
/// <remarks>
/// Kept out of the Domain, which stays free of <see cref="System.Text.Json"/>. The former hybrid
/// reader — task and agent ids to copy back to the character, an order, parallel groups,
/// dependencies, and a <c>KEY: value</c> text fallback that kept the first line of a multi-line
/// instruction — is gone with what it fed.
/// </remarks>
public sealed class ExecutionPlanParser : IExecutionPlanParser
{
    /// <inheritdoc />
    public ExecutionPlanReading Read(string reply, IReadOnlyList<TaskId> numberedTasks)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(numberedTasks);

        var json = LlmJsonText.ExtractObject(reply);
        if (json is null)
            return ExecutionPlanReading.Unreadable("the reply holds no JSON object");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ExecutionPlanReading.Unreadable($"the reply is not valid JSON ({ex.Message})");
        }

        using (document)
        {
            if (Property(document.RootElement, "plans") is not { ValueKind: JsonValueKind.Array } plans)
                return ExecutionPlanReading.Unreadable("the reply has no \"plans\" array");

            var planned = new List<PlannedTask>();
            var taken = new HashSet<int>();
            var warnings = new List<string>();
            foreach (var entry in plans.EnumerateArray())
                ReadEntry(entry, numberedTasks, planned, taken, warnings);

            return ExecutionPlanReading.Of(ExecutionPlan.Create(planned), warnings);
        }
    }

    private static void ReadEntry(
        JsonElement entry,
        IReadOnlyList<TaskId> numberedTasks,
        List<PlannedTask> planned,
        HashSet<int> taken,
        List<string> warnings)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("the planner wrote an entry that is not an object; it is ignored.");
            return;
        }

        if (Number(Property(entry, "task")) is not { } number)
        {
            warnings.Add("the planner wrote a plan without a task number; it is ignored.");
            return;
        }

        if (number < 1 || number > numberedTasks.Count)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"the planner wrote a plan for task {number}, which the crew does not have; it is ignored."));
            return;
        }

        if (Text(Property(entry, "plan")) is not { } text)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"the planner wrote an empty plan for task {number}; it is ignored."));
            return;
        }

        if (!taken.Add(number))
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"the planner wrote two plans for task {number}; the first is kept."));
            return;
        }

        planned.Add(new PlannedTask(numberedTasks[number - 1], text));
    }

    /// <summary>The property named <paramref name="name"/>, whatever its case: models capitalise keys.</summary>
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    /// <summary>A task number, written as a JSON number or as a string of digits.</summary>
    private static int? Number(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } number when number.TryGetInt32(out var parsed) => parsed,
        { ValueKind: JsonValueKind.String } text when int.TryParse(text.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    /// <summary>A plan: a string, or a list of steps joined one per line; null when blank.</summary>
    private static string? Text(JsonElement? value)
    {
        var text = value switch
        {
            { ValueKind: JsonValueKind.String } plan => plan.GetString(),
            { ValueKind: JsonValueKind.Array } steps => string.Join('\n', steps.EnumerateArray()
                .Where(step => step.ValueKind == JsonValueKind.String)
                .Select(step => step.GetString()?.Trim())
                .Where(step => !string.IsNullOrEmpty(step))),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
