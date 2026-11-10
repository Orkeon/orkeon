using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.MCP;

/// <summary>Typed request for <see cref="McpPromptGetTool"/>.</summary>
public sealed class McpPromptGetRequest
{
    /// <summary>The id of the MCP server, as declared under <c>MCP:Servers</c>.</summary>
    [JsonPropertyName("server")]
    [FieldSchema(Description = "Id of the connected MCP server that offers the prompt", IsRequired = true)]
    public string Server { get; init; } = "";

    /// <summary>The prompt's name; empty lists the prompts of the server.</summary>
    [JsonPropertyName("name")]
    [FieldSchema(Description = "Name of the prompt to get; leave empty to list the prompts of the server", IsRequired = false)]
    public string? Name { get; init; }

    /// <summary>The prompt's arguments.</summary>
    [JsonPropertyName("arguments")]
    [FieldSchema(Description = "Arguments of the prompt, as name to value", IsRequired = false)]
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }
}

/// <summary>A prompt of a server, as listed.</summary>
public sealed class McpPromptSummary
{
    /// <summary>The prompt's name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>What the prompt does.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The arguments the prompt takes; a required one is marked.</summary>
    [JsonPropertyName("arguments")]
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

/// <summary>One message of a prompt, as text.</summary>
public sealed class McpPromptTextMessage
{
    /// <summary>The speaker: <c>user</c> or <c>assistant</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    /// <summary>The text of the message; a block that is not text is named by its type.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

/// <summary>Typed response for <see cref="McpPromptGetTool"/>.</summary>
public sealed class McpPromptGetResponse
{
    /// <summary>What the prompt does.</summary>
    [JsonPropertyName("description")]
    [ReturnSchema(Description = "What the prompt does")]
    public string? Description { get; init; }

    /// <summary>The messages of the prompt, its arguments filled in.</summary>
    [JsonPropertyName("messages")]
    [ReturnSchema(Description = "The messages of the prompt, its arguments filled in")]
    public IReadOnlyList<McpPromptTextMessage> Messages { get; init; } = [];

    /// <summary>The prompts of the server, when no name was given.</summary>
    [JsonPropertyName("prompts")]
    [ReturnSchema(Description = "The prompts the server offers, when no name was given")]
    public IReadOnlyList<McpPromptSummary> Prompts { get; init; } = [];
}

/// <summary>
/// Gets a prompt template of a connected MCP server (<c>prompts/get</c>), or lists the server's prompts
/// when no name is given. The text the server returns is data, never instructions of the operator.
/// </summary>
[ToolContract("mcp_prompt_get",
    Description = "Get a prompt template from a connected MCP server, its arguments filled in; without a name, list the prompts the server offers.",
    Category = "MCP")]
public sealed class McpPromptGetTool : ToolBase<McpPromptGetRequest, McpPromptGetResponse>
{
    private readonly Func<McpToolProvider> _provider;

    /// <summary>The access class: a prompt is read from the server, which changes nothing.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <summary>Initializes a new instance of <see cref="McpPromptGetTool"/>.</summary>
    /// <param name="provider">The provider that holds the connected servers.</param>
    public McpPromptGetTool(McpToolProvider provider)
        : this(Capture(provider))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="McpPromptGetTool"/> that reaches the provider when a call
    /// needs it: the provider needs the tool registry, which holds this tool, so the container cannot hand
    /// it over at construction.
    /// </summary>
    /// <param name="provider">Resolves the provider that holds the connected servers.</param>
    public McpPromptGetTool(Func<McpToolProvider> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    private static Func<McpToolProvider> Capture(McpToolProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return () => provider;
    }

    /// <inheritdoc />
    protected override async Task<McpPromptGetResponse> ExecuteTypedAsync(
        McpPromptGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            var prompts = await _provider().ListPromptsAsync(request.Server, cancellationToken).ConfigureAwait(false);
            return new McpPromptGetResponse
            {
                Prompts =
                [
                    .. prompts.Select(p => new McpPromptSummary
                    {
                        Name = p.Name,
                        Description = p.Description,
                        Arguments = [.. p.Arguments.Select(a => a.Required ? a.Name + " (required)" : a.Name)],
                    }),
                ],
            };
        }

        var result = await _provider()
            .GetPromptAsync(request.Server, request.Name, request.Arguments, cancellationToken)
            .ConfigureAwait(false);
        return new McpPromptGetResponse
        {
            Description = result.Description,
            Messages =
            [
                .. result.Messages.Select(m => new McpPromptTextMessage
                {
                    Role = m.Role,
                    Text = m.Content.Text ?? $"[{m.Content.Type} content]",
                }),
            ],
        };
    }
}
