using System.Security.Cryptography;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Common.Mapping;

/// <summary>
/// Mapper for converting between Tool domain entities and DTOs.
/// </summary>
public static class ToolMapper
{
    /// <summary>
    /// Generates a deterministic GUID from a tool name using MD5 hash,
    /// so the same tool always maps to the same ID.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "MD5 is used only to derive a stable, deterministic GUID from a tool name (a non-security identifier mapping, UUIDv3-style); it protects no secret and guards no trust boundary, and the digest is never used as a security primitive.")]
    private static Guid DeterministicGuid(string toolName)
    {
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(toolName));
        return new Guid(hash);
    }

    private static readonly string[] s_filePermissions = ["file_read", "file_write"];
    private static readonly string[] s_databaseKeywords = ["sql", "database"];
    private static readonly string[] s_dataKeywords = ["csv", "json", "xml"];
    private static readonly string[] s_documentKeywords = ["pdf", "image", "document"];
    private static readonly string[] s_webKeywords = ["web", "http", "scrape"];
    private static readonly string[] s_communicationKeywords = ["email", "slack", "message"];
    private static readonly string[] s_developmentKeywords = ["github", "git"];
    private static readonly string[] s_searchKeywords = ["search"];
    private static readonly string[] s_searchExcludes = ["sql"];

    /// <summary>
    /// Converts a Tool domain entity to ToolDto.
    /// </summary>
    public static ToolDto ToDto(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ToolDto
        {
            Id = DeterministicGuid(tool.Name).ToString(), // Deterministic ID based on tool name
            Name = tool.Name,
            Description = tool.Description,
            Category = InferCategory(tool.Name),
            Version = "1.0.0", // Default version
            InputSchema = CreateInputSchema(tool),
            OutputSchema = CreateOutputSchema(),
            Configuration = CreateConfiguration(),
            Enabled = true, // Default enabled
            Deprecated = false,
            Metrics = CreateDefaultToolMetrics(),
            Capabilities = InferCapabilities(tool),
            RequiredPermissions = InferRequiredPermissions(tool),
            RateLimit = CreateDefaultRateLimit(),
            Security = CreateDefaultSecurity(tool),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Metadata = ToolDtoMetadata.CreateBuilder()
                .AddToolType(tool.GetType().Name)
                .AddToolName(tool.Name)
                .Build()
                .ToDictionary()
        };
    }

    /// <summary>
    /// Converts a list of Tool domain entities to ToolDto list.
    /// </summary>
    public static IReadOnlyList<ToolDto> ToDto(IEnumerable<IBaseTool> tools)
    {
        if (tools == null)
            return [];

        return tools.Select(ToDto).ToList();
    }

    /// <summary>
    /// Creates a summary ToolDto for list views.
    /// </summary>
    public static ToolDto ToSummaryDto(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new ToolDto
        {
            Id = DeterministicGuid(tool.Name).ToString(), // Deterministic ID based on tool name
            Name = tool.Name,
            Description = tool.Description,
            Category = InferCategory(tool.Name),
            Version = "1.0.0",
            Enabled = true,
            Deprecated = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            // Summary doesn't include detailed properties
            InputSchema = null,
            OutputSchema = null,
            Configuration = null!,
            Metrics = null,
            Capabilities = null!,
            RequiredPermissions = null!,
            RateLimit = null,
            Security = null,
            Metadata = null!
        };
    }

    /// <summary>
    /// Converts ToolUsage domain entity to ToolUsageDto.
    /// </summary>
    public static ToolUsageDto ToToolUsageDto(Domain.Tools.ToolUsage toolUsage)
    {
        ArgumentNullException.ThrowIfNull(toolUsage);

        return new ToolUsageDto
        {
            ToolId = toolUsage.ToolId,
            ToolName = toolUsage.ToolName,
            Input = ParseToolInput(toolUsage.Metadata),
            Output = toolUsage.Success ? "Success" : "Failed",
            Success = toolUsage.Success,
            ExecutionTime = toolUsage.Duration,
            Error = toolUsage.Success ? null : toolUsage.Error ?? "Tool execution failed",
            CalledAt = toolUsage.UsedAt
        };
    }

    /// <summary>
    /// Category rules ordered by specificity. Each rule has keywords that must match
    /// and optional exclusion keywords.
    /// </summary>
    private static readonly (string category, string[] keywords, string[]? excludes)[] CategoryRules =
    [
        ("database", s_databaseKeywords, null),
        ("data", s_dataKeywords, null),
        ("document", s_documentKeywords, null),
        ("web", s_webKeywords, null),
        ("communication", s_communicationKeywords, null),
        ("development", s_developmentKeywords, null),
        ("search", s_searchKeywords, s_searchExcludes),
    ];

    /// <summary>
    /// Infers tool category from tool name.
    /// </summary>
    private static string InferCategory(string toolName)
    {
#pragma warning disable CA1308 // normalized lowercase key matched against lowercase literal keywords
        var name = toolName.ToLowerInvariant();
#pragma warning restore CA1308

        foreach (var (category, keywords, excludes) in CategoryRules)
        {
            if (!keywords.Any(k => name.Contains(k, StringComparison.Ordinal)))
                continue;
            if (excludes != null && excludes.Any(e => name.Contains(e, StringComparison.Ordinal)))
                continue;
            return category;
        }

        if (MatchesFileCategory(name))
            return "file";

        return "utility";
    }

    private static bool MatchesFileCategory(string name)
    {
        if (name.Contains("file", StringComparison.Ordinal) || name.Contains("write", StringComparison.Ordinal))
            return true;
        return name.Contains("read", StringComparison.Ordinal)
            && !name.Contains("csv", StringComparison.Ordinal)
            && !name.Contains("pdf", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates input schema for a tool.
    /// </summary>
    private static ToolSchemaDto CreateInputSchema(IBaseTool tool)
    {
        return new ToolSchemaDto
        {
            Type = "object",
            Title = $"{tool.Name} Input",
            Description = $"Input parameters for {tool.Name}",
            Properties = CreateDefaultProperties(tool.Name),
            Required = ["input"],
            ValidationRules = ToolValidationRules.Empty.ToDictionary()
        };
    }

    /// <summary>
    /// Creates output schema for a tool.
    /// </summary>
    private static ToolSchemaDto CreateOutputSchema()
    {
        return new ToolSchemaDto
        {
            Type = "object",
            Title = "Tool Output",
            Description = "Output from tool execution",
            Properties = new Dictionary<string, ToolPropertyDto>
            {
                ["result"] = new ToolPropertyDto
                {
                    Type = "string",
                    Description = "The result of the tool execution",
                    Required = true
                },
                ["success"] = new ToolPropertyDto
                {
                    Type = "boolean",
                    Description = "Whether the tool execution was successful",
                    Required = true
                },
                ["metadata"] = new ToolPropertyDto
                {
                    Type = "object",
                    Description = "Additional metadata about the execution",
                    Required = false
                }
            },
            Required = ["result", "success"]
        };
    }

    /// <summary>
    /// Creates default properties based on tool name.
    /// </summary>
    private static Dictionary<string, ToolPropertyDto> CreateDefaultProperties(string toolName)
    {
#pragma warning disable CA1308 // normalized lowercase key matched against lowercase literal keywords
        var name = toolName.ToLowerInvariant();
#pragma warning restore CA1308
        var properties = new Dictionary<string, ToolPropertyDto>
        {
            // Common input property
            ["input"] = new ToolPropertyDto
            {
                Type = "string",
                Description = "Input data for the tool",
                Required = true
            }
        };

        // Tool-specific properties
        if (name.Contains("file", StringComparison.Ordinal))
        {
            properties["file_path"] = new ToolPropertyDto
            {
                Type = "string",
                Description = "Path to the file",
                Required = true
            };
        }

        if (name.Contains("web", StringComparison.Ordinal) || name.Contains("http", StringComparison.Ordinal))
        {
            properties["url"] = new ToolPropertyDto
            {
                Type = "string",
                Description = "URL to access",
                Required = true,
                Pattern = @"^https?://.+"
            };
        }

        if (name.Contains("search", StringComparison.Ordinal))
        {
            properties["query"] = new ToolPropertyDto
            {
                Type = "string",
                Description = "Search query",
                Required = true,
                MinLength = 1,
                MaxLength = 1000
            };
        }

        return properties;
    }

    /// <summary>
    /// Creates tool configuration.
    /// </summary>
    private static Dictionary<string, object> CreateConfiguration()
    {
        return ToolConfigurationData.CreateDefault().ToDictionary();
    }

    /// <summary>
    /// Creates default tool metrics.
    /// </summary>
    private static ToolMetricsDto CreateDefaultToolMetrics()
    {
        return new ToolMetricsDto
        {
            TotalCalls = 0,
            SuccessfulCalls = 0,
            FailedCalls = 0,
            AverageExecutionTime = 0,
            SuccessRate = 0,
            LastSuccessfulCall = null,
            LastFailedCall = null,
            ErrorDistribution = []
        };
    }

    /// <summary>
    /// Infers tool capabilities.
    /// </summary>
    private static List<string> InferCapabilities(IBaseTool tool)
    {
        var capabilities = new List<string> { "execute" };
#pragma warning disable CA1308 // normalized lowercase key matched against lowercase literal keywords
        var name = tool.Name.ToLowerInvariant();
#pragma warning restore CA1308

        if (name.Contains("read", StringComparison.Ordinal))
            capabilities.Add("read");
        if (name.Contains("write", StringComparison.Ordinal))
            capabilities.Add("write");
        if (name.Contains("search", StringComparison.Ordinal))
            capabilities.Add("search");
        if (name.Contains("web", StringComparison.Ordinal))
            capabilities.Add("web_access");
        if (name.Contains("file", StringComparison.Ordinal))
            capabilities.Add("file_system");
        if (name.Contains("api", StringComparison.Ordinal))
            capabilities.Add("api_access");

        return capabilities;
    }

    /// <summary>
    /// Infers required permissions.
    /// </summary>
    private static List<string> InferRequiredPermissions(IBaseTool tool)
    {
        var permissions = new List<string>();
#pragma warning disable CA1308 // normalized lowercase key matched against lowercase literal keywords
        var name = tool.Name.ToLowerInvariant();
#pragma warning restore CA1308

        if (name.Contains("file", StringComparison.Ordinal))
            permissions.AddRange(s_filePermissions);
        if (name.Contains("web", StringComparison.Ordinal) || name.Contains("http", StringComparison.Ordinal))
            permissions.Add("network_access");
        if (name.Contains("system", StringComparison.Ordinal))
            permissions.Add("system_access");
        if (name.Contains("database", StringComparison.Ordinal) || name.Contains("sql", StringComparison.Ordinal))
            permissions.Add("database_access");

        return permissions;
    }

    /// <summary>
    /// Creates default rate limit configuration.
    /// </summary>
    private static RateLimitDto CreateDefaultRateLimit()
    {
        return new RateLimitDto
        {
            CallsPerMinute = 60,
            CallsPerHour = 1000,
            CallsPerDay = 10000,
            MaxConcurrentCalls = 5,
            ResetStrategy = "sliding_window"
        };
    }

    /// <summary>
    /// Creates default security configuration.
    /// </summary>
    private static ToolSecurityDto CreateDefaultSecurity(IBaseTool tool)
    {
#pragma warning disable CA1308 // normalized lowercase key matched against lowercase literal keywords
        var name = tool.Name.ToLowerInvariant();
#pragma warning restore CA1308
        var riskLevel = "low";

        if (name.Contains("file", StringComparison.Ordinal) || name.Contains("system", StringComparison.Ordinal))
            riskLevel = "medium";
        if (name.Contains("exec", StringComparison.Ordinal) || name.Contains("command", StringComparison.Ordinal))
            riskLevel = "high";

        return new ToolSecurityDto
        {
            RequiresAuthentication = false,
            RequiresAuthorization = riskLevel != "low",
            AllowExternalAccess = name.Contains("web", StringComparison.Ordinal),
            AllowedDomains = [],
            InputValidationRules = ["sanitize_input", "validate_parameters"],
            OutputSanitizationRules = ["remove_sensitive_data", "limit_output_size"],
            RiskLevel = riskLevel
        };
    }

    /// <summary>
    /// Parses tool input from metadata.
    /// </summary>
    private static Dictionary<string, object> ParseToolInput(Domain.Memory.ValueObjects.ToolUsageMetadata? metadata)
    {
        if (metadata == null)
            return ToolInputData.Empty.ToDictionary();

        // Convert metadata to dictionary
        return metadata.ToDictionary();
    }
}
