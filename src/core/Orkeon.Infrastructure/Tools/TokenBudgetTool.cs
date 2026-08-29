using Orkeon.Constants.Configuration;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="TokenBudgetTool"/> (no parameters).</summary>
public sealed class TokenBudgetRequest
{
}

/// <summary>Typed response for <see cref="TokenBudgetTool"/>.</summary>
public sealed class TokenBudgetResponse
{
    /// <summary>Model context window size in tokens.</summary>
    [JsonPropertyName("context_window_tokens")]
    public int ContextWindowTokens { get; set; }

    /// <summary>Estimated tokens used by the current buffer.</summary>
    [JsonPropertyName("used_tokens")]
    public int UsedTokens { get; set; }

    /// <summary>Remaining budget (window − used).</summary>
    [JsonPropertyName("available_tokens")]
    public int AvailableTokens { get; set; }

    /// <summary>Active model name.</summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";
}

/// <summary>
/// Reports the model's context window, used tokens, and available budget for the current
/// session (exp 07 SPEC §7.3; backs SessionCompact + <c>/compact</c>).
/// </summary>
/// <remarks>
/// The domain <c>ILlmProvider</c> exposes no context-window API (SPEC risk R5), so the window
/// and model name are read from configuration: <c>Orkeon:Cli:Session:ContextWindowTokens</c>
/// (default 200000) and <c>Llm:Model</c>. Used tokens come from the buffer's heuristic.
/// </remarks>
public sealed class TokenBudgetTool : ToolBase<TokenBudgetRequest, TokenBudgetResponse>
{
    /// <inheritdoc />
    public override string Name => "token_budget";
    /// <inheritdoc />
    public override string Description =>
        "Return the current model's context window size, used tokens, and available budget.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private const int DefaultContextWindow = 200_000;

    private readonly ISessionBufferService _buffer;
    private readonly IConfiguration? _configuration;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public TokenBudgetTool(ISessionBufferService buffer, IConfiguration? configuration = null)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override Task<TokenBudgetResponse> ExecuteTypedAsync(
        TokenBudgetRequest request, CancellationToken cancellationToken)
    {
        var contextWindow = ReadInt(ConfigurationKeys.CliSessionContextWindowTokens, DefaultContextWindow);
        var model = _configuration?["Llm:Model"] ?? "unknown";
        var used = _buffer.EstimateTokenCount();

        return Task.FromResult(new TokenBudgetResponse
        {
            ContextWindowTokens = contextWindow,
            UsedTokens = used,
            AvailableTokens = Math.Max(0, contextWindow - used),
            Model = model,
        });
    }

    private int ReadInt(string key, int fallback)
    {
        var raw = _configuration?[key];
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
