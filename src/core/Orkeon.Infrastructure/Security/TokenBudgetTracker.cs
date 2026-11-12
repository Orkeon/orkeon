using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Tracks token usage and enforces budget limits per agent and per crew.
/// Thread-safe using ConcurrentDictionary.
/// </summary>
public partial class TokenBudgetTracker : ITokenBudgetTracker
{
    private readonly ConcurrentDictionary<string, TokenBudgetUsage> _agentUsage = new();
    private readonly ConcurrentDictionary<string, TokenBudgetUsage> _crewUsage = new();
    private readonly TokenBudgetOptions _options;
    private readonly ILogger<TokenBudgetTracker> _logger;
    private readonly IModelPricingRegistry? _pricingRegistry;

    /// <summary>Initializes a new instance of <see cref="TokenBudgetTracker"/>.</summary>
    /// <param name="options">The token budget options.</param>
    /// <param name="logger">The logger.</param>
    public TokenBudgetTracker(IOptions<TokenBudgetOptions> options, ILogger<TokenBudgetTracker> logger)
        : this(options, logger, null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="TokenBudgetTracker"/> with a pricing registry.</summary>
    /// <param name="options">The token budget options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="pricingRegistry">The model pricing registry used as the single source of truth for costs.</param>
    public TokenBudgetTracker(
        IOptions<TokenBudgetOptions> options,
        ILogger<TokenBudgetTracker> logger,
        IModelPricingRegistry? pricingRegistry)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _pricingRegistry = pricingRegistry;
    }

    /// <inheritdoc />
    public BudgetCheckResult RecordUsage(string crewId, string agentRole, int promptTokens, int completionTokens, string model)
    {
        var totalTokens = promptTokens + completionTokens;
        var cost = EstimateCost(promptTokens, completionTokens, model, _pricingRegistry);

        var agentKey = $"{crewId}:{agentRole}";

        var agentUsage = _agentUsage.AddOrUpdate(
            agentKey,
            _ => new TokenBudgetUsage { TotalTokens = totalTokens, EstimatedCost = cost, CallCount = 1 },
            (_, existing) => new TokenBudgetUsage
            {
                TotalTokens = existing.TotalTokens + totalTokens,
                EstimatedCost = existing.EstimatedCost + cost,
                CallCount = existing.CallCount + 1,
            });

        var crewUsage = _crewUsage.AddOrUpdate(
            crewId,
            _ => new TokenBudgetUsage { TotalTokens = totalTokens, EstimatedCost = cost, CallCount = 1 },
            (_, existing) => new TokenBudgetUsage
            {
                TotalTokens = existing.TotalTokens + totalTokens,
                EstimatedCost = existing.EstimatedCost + cost,
                CallCount = existing.CallCount + 1,
            });

        // Check agent budget
        if (_options.MaxTokensPerAgent > 0 && agentUsage.TotalTokens > _options.MaxTokensPerAgent)
        {
            LogAgentTokenBudgetExceededAgent(agentRole, crewId, agentUsage.TotalTokens, _options.MaxTokensPerAgent);
            return BudgetCheckResult.BudgetExceeded(
                $"Agent '{agentRole}' exceeded token budget ({agentUsage.TotalTokens}/{_options.MaxTokensPerAgent})",
                agentUsage);
        }

        // Check crew token budget
        if (_options.MaxTokensPerCrew > 0 && crewUsage.TotalTokens > _options.MaxTokensPerCrew)
        {
            LogCrewTokenBudgetExceededCrew(crewId, crewUsage.TotalTokens, _options.MaxTokensPerCrew);
            return BudgetCheckResult.BudgetExceeded(
                $"Crew '{crewId}' exceeded token budget ({crewUsage.TotalTokens}/{_options.MaxTokensPerCrew})",
                crewUsage);
        }

        // Check crew cost budget
        if (_options.MaxCostPerCrew > 0 && crewUsage.EstimatedCost > _options.MaxCostPerCrew)
        {
            LogCrewCostBudgetExceededCrew(crewId, crewUsage.EstimatedCost, _options.MaxCostPerCrew);
            return BudgetCheckResult.BudgetExceeded(
                Inv.Format($"Crew '{crewId}' exceeded cost budget (${crewUsage.EstimatedCost:F4}/${_options.MaxCostPerCrew:F2})"),
                crewUsage);
        }

        return BudgetCheckResult.WithinBudget(agentUsage, crewUsage);
    }

    /// <inheritdoc />
    public TokenBudgetReport GetReport(string crewId)
    {
        var crewUsage = _crewUsage.GetValueOrDefault(crewId, TokenBudgetUsage.None);

        var prefix = $"{crewId}:";
        var agentBreakdown = _agentUsage
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key[prefix.Length..], kvp => kvp.Value);

        return new TokenBudgetReport(crewId, crewUsage, agentBreakdown);
    }

    /// <summary>
    /// Estimates cost using the provided <see cref="IModelPricingRegistry"/> as the source of truth,
    /// or falls back to built-in approximate rates when the registry is null or has no entry for the model.
    /// </summary>
    public static decimal EstimateCost(
        int promptTokens, int completionTokens, string model,
        IModelPricingRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (registry is not null)
        {
            var pricing = registry.TryGetPricing(model);
            if (pricing is not null)
            {
                // ModelPricingRegistry stores $/million tokens; convert to per-token cost
                return (promptTokens * pricing.PromptPricePerMillion / 1_000_000m)
                     + (completionTokens * pricing.CompletionPricePerMillion / 1_000_000m);
            }
        }

        // Fallback when no registry is injected: built-in approximate per-1K rates
        var (promptRate, completionRate) = GetFallbackRatesPerThousand(model);
        return (promptTokens * promptRate / 1000m) + (completionTokens * completionRate / 1000m);
    }

    /// <summary>
    /// Built-in approximate per-1K-token rates used when no <see cref="IModelPricingRegistry"/> is available.
    /// These values mirror the authoritative prices in <c>ModelPricingRegistry</c> converted to $/1K.
    /// </summary>
    private static (decimal promptRate, decimal completionRate) GetFallbackRatesPerThousand(string model)
    {
#pragma warning disable CA1308 // lowercase is the required normalized form matched by the switch, not a comparison normalization
        var normalized = model.ToLowerInvariant();
#pragma warning restore CA1308
        return normalized switch
        {
            _ when normalized.Contains("gpt-4o", StringComparison.Ordinal) => (0.0025m, 0.01m),
            _ when normalized.Contains("gpt-4", StringComparison.Ordinal) => (0.03m, 0.06m),
            _ when normalized.Contains("gpt-3.5", StringComparison.Ordinal) => (0.0005m, 0.0015m),
            _ when normalized.Contains("claude-3-opus", StringComparison.Ordinal) => (0.015m, 0.075m),
            _ when normalized.Contains("claude-3-sonnet", StringComparison.Ordinal) => (0.003m, 0.015m),
            _ => (0.001m, 0.002m) // Default conservative estimate
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent token budget exceeded: agent={Agent}, crew={Crew}, tokens={Tokens}/{Max}")]
    private partial void LogAgentTokenBudgetExceededAgent(object agent, object crew, object tokens, int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Crew token budget exceeded: crew={Crew}, tokens={Tokens}/{Max}")]
    private partial void LogCrewTokenBudgetExceededCrew(object crew, object tokens, int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Crew cost budget exceeded: crew={Crew}, cost={Cost:C}/{Max:C}")]
    private partial void LogCrewCostBudgetExceededCrew(object crew, object cost, object max);

}
