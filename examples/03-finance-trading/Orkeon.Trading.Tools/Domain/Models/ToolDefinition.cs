using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Trading.Tools.Domain.Models;

/// <summary>
/// YAML-based tool definition model. Loaded from Domain/ToolDefinitions/{category}/{tool_id}.yaml.
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();
    public Dictionary<string, ReturnFieldDefinition>? Returns { get; set; }

    public ToolSchema ToToolSchema() => new(
        Name: Name,
        Description: Description,
        Parameters: Parameters.ToDictionary(
            p => p.Key,
            p => new ParameterSchema(
                Type: p.Value.Type,
                Description: p.Value.Description,
                Required: p.Value.Required,
                Default: p.Value.Default,
                Enum: p.Value.Enum)),
        Returns: Returns?.ToDictionary(
            r => r.Key,
            r => (object?)new Dictionary<string, object> { ["type"] = r.Value.Type }));
}

public class ParameterDefinition
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = "";
    public bool Required { get; set; }
    public object? Default { get; set; }
    public List<object>? Enum { get; set; }
}

public class ReturnFieldDefinition
{
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
}
