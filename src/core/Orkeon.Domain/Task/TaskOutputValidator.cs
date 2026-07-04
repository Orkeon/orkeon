using Orkeon.Domain.Task.ValueObjects;

using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Task;

/// <summary>
/// Validates task output against defined schemas (JSON schema, type constraints).
/// Extracted from Task to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service -- no dependency injection.
/// </summary>
internal static class TaskOutputValidator
{
    /// <summary>
    /// Validates the task output against defined schemas.
    /// </summary>
    public static ValidationResult ValidateOutput(TaskOutput output, JsonSchema? outputJson, Type? outputPydantic)
    {
        var errors = new List<SharedKernel.ValidationError>();

        // Validation JSON Schema
        if (outputJson != null && !outputJson.Validate(output.Output))
        {
            errors.Add(new SharedKernel.ValidationError("Output", "Output does not match JSON schema"));
        }

        // Validation Pydantic (type)
        if (outputPydantic != null && output.StructuredOutput != null)
        {
            try
            {
                var outputType = output.StructuredOutput.GetType();
                if (!outputPydantic.IsAssignableFrom(outputType))
                {
                    errors.Add(new SharedKernel.ValidationError("StructuredOutput", $"Output type {outputType.Name} is not compatible with expected type {outputPydantic.Name}"));
                }
            }
            catch (InvalidCastException ex)
            {
                errors.Add(new SharedKernel.ValidationError("StructuredOutput", $"Type validation error: {ex.Message}"));
            }
            catch (System.Reflection.TargetException ex)
            {
                errors.Add(new SharedKernel.ValidationError("StructuredOutput", $"Type validation error: {ex.Message}"));
            }
        }

        return new ValidationResult(errors);
    }
}
