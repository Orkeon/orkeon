using System.Collections.Concurrent;
using Orkeon.Trading.Tools.Domain.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orkeon.Trading.Tools.Infrastructure.Base;

/// <summary>
/// Loads and caches YAML tool definitions from Domain/ToolDefinitions/{category}/.
/// Each tool has a {tool_id}.yaml file describing its name, description, parameters, and returns.
/// </summary>
public static class ToolDefinitionLoader
{
    private static readonly ConcurrentDictionary<string, ToolDefinition> _cache = new();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ToolDefinition Load(string toolId)
    {
        return _cache.GetOrAdd(toolId, id =>
        {
            var yamlFile = FindYamlFile(id)
                ?? throw new FileNotFoundException(
                    $"Tool definition '{id}.yaml' not found in Domain/ToolDefinitions/. " +
                    $"Searched in: {GetSearchPath()}");

            var yaml = File.ReadAllText(yamlFile);
            return _deserializer.Deserialize<ToolDefinition>(yaml);
        });
    }

    private static string? FindYamlFile(string toolId)
    {
        var searchPath = GetSearchPath();
        if (!Directory.Exists(searchPath))
            return null;

        return Directory.GetFiles(searchPath, $"{toolId}.yaml", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string GetSearchPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Domain", "ToolDefinitions");
    }
}
