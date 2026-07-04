using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Strongly typed LLM response metadata.
/// </summary>
public sealed record LlmMetadata : ValueObjectRecord
{
    /// <summary>Error message, if any.</summary>
    public string? Error { get; init; }
    /// <summary>Number of tokens used.</summary>
    public int? TokensUsed { get; init; }
    /// <summary>Token limit for the model.</summary>
    public int? TokensLimit { get; init; }
    /// <summary>Response time from the LLM.</summary>
    public TimeSpan? ResponseTime { get; init; }
    /// <summary>Model identifier.</summary>
    public string? Model { get; init; }
    /// <summary>Temperature used for generation.</summary>
    public double? Temperature { get; init; }
    /// <summary>Custom fields.</summary>
    public Dictionary<string, string>? CustomFields { get; init; }

    /// <summary>
    /// Initializes a new instance of <see cref="LlmMetadata"/> with validation.
    /// </summary>
    public LlmMetadata(
        string? Error = null,
        int? TokensUsed = null,
        int? TokensLimit = null,
        TimeSpan? ResponseTime = null,
        string? Model = null,
        double? Temperature = null,
        Dictionary<string, string>? CustomFields = null)
    {
        if (TokensUsed.HasValue && TokensUsed.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(TokensUsed), "TokensUsed cannot be negative.");

        if (TokensLimit.HasValue && TokensLimit.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(TokensLimit), "TokensLimit cannot be negative.");

        if (Temperature.HasValue && (Temperature.Value < 0.0 || Temperature.Value > 2.0))
            throw new ArgumentOutOfRangeException(nameof(Temperature), "Temperature must be between 0.0 and 2.0.");

        if (ResponseTime.HasValue && ResponseTime.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ResponseTime), "ResponseTime cannot be negative.");

        this.Error = Error;
        this.TokensUsed = TokensUsed;
        this.TokensLimit = TokensLimit;
        this.ResponseTime = ResponseTime;
        this.Model = Model;
        this.Temperature = Temperature;
        this.CustomFields = CustomFields;
    }

    /// <summary>
    /// Deconstruct for backward compatibility with positional record syntax.
    /// </summary>
    public void Deconstruct(
        out string? error,
        out int? tokensUsed,
        out int? tokensLimit,
        out TimeSpan? responseTime,
        out string? model,
        out double? temperature,
        out Dictionary<string, string>? customFields)
    {
        error = Error;
        tokensUsed = TokensUsed;
        tokensLimit = TokensLimit;
        responseTime = ResponseTime;
        model = Model;
        temperature = Temperature;
        customFields = CustomFields;
    }

    /// <summary>
    /// Creates a new <see cref="LlmMetadata"/> instance.
    /// </summary>
    public static LlmMetadata Create(
        string? error = null,
        int? tokensUsed = null,
        int? tokensLimit = null,
        TimeSpan? responseTime = null,
        string? model = null,
        double? temperature = null,
        Dictionary<string, string>? customFields = null)
        => new(error, tokensUsed, tokensLimit, responseTime, model, temperature, customFields);
}
