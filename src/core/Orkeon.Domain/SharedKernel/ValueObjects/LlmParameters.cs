using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Strongly typed LLM generation parameters.
/// </summary>
public sealed record LlmParameters : ValueObjectRecord
{
    /// <summary>Gets the sampling temperature (0.0–2.0). Higher values produce more random output.</summary>
    public double Temperature { get; init; }
    /// <summary>Gets the maximum number of tokens to generate.</summary>
    public int MaxTokens { get; init; }
    /// <summary>Gets the nucleus sampling probability mass (0.0–1.0).</summary>
    public double TopP { get; init; }
    /// <summary>Gets the frequency penalty to reduce repetition of token sequences.</summary>
    public double FrequencyPenalty { get; init; }
    /// <summary>Gets the presence penalty to encourage new topics.</summary>
    public double PresencePenalty { get; init; }
    /// <summary>Gets the stop sequences that terminate generation.</summary>
    public ImmutableArray<string> StopSequences { get; init; }
    /// <summary>Gets the optional random seed for deterministic generation.</summary>
    public int? Seed { get; init; }

    /// <summary>Initializes a new <see cref="LlmParameters"/> with the specified generation settings.</summary>
    /// <param name="Temperature">The sampling temperature (default <see cref="LlmDefaults.DefaultTemperature"/>).</param>
    /// <param name="MaxTokens">The maximum tokens to generate (default <see cref="LlmDefaults.DefaultMaxTokens"/>).</param>
    /// <param name="TopP">The nucleus sampling probability (default 1.0).</param>
    /// <param name="FrequencyPenalty">The frequency penalty (default 0.0).</param>
    /// <param name="PresencePenalty">The presence penalty (default 0.0).</param>
    /// <param name="StopSequences">The stop sequences (default empty).</param>
    /// <param name="Seed">The optional random seed (default null).</param>
    private LlmParameters(
        double Temperature = LlmDefaults.DefaultTemperature,
        int MaxTokens = LlmDefaults.DefaultMaxTokens,
        double TopP = 1.0,
        double FrequencyPenalty = 0.0,
        double PresencePenalty = 0.0,
        ImmutableArray<string> StopSequences = default,
        int? Seed = null)
    {
        this.Temperature = EnsureInRange(Temperature, 0.0, 2.0, nameof(Temperature));
        this.MaxTokens = MaxTokens > 0 ? MaxTokens
            : throw new ArgumentOutOfRangeException(nameof(MaxTokens), "MaxTokens must be greater than 0.");
        this.TopP = EnsureInRange(TopP, 0.0, 1.0, nameof(TopP));
        this.FrequencyPenalty = FrequencyPenalty;
        this.PresencePenalty = PresencePenalty;
        this.StopSequences = StopSequences;
        this.Seed = Seed;
        Validate();
    }

    /// <summary>Creates a new <see cref="LlmParameters"/> with the specified generation settings.</summary>
    public static LlmParameters Create(
        double temperature = LlmDefaults.DefaultTemperature,
        int maxTokens = LlmDefaults.DefaultMaxTokens,
        double topP = 1.0,
        double frequencyPenalty = 0.0,
        double presencePenalty = 0.0,
        ImmutableArray<string> stopSequences = default,
        int? seed = null)
        => new(temperature, maxTokens, topP, frequencyPenalty, presencePenalty, stopSequences, seed);

    /// <summary>Deconstructs the parameters into their component values.</summary>
    /// <param name="temperature">The sampling temperature.</param>
    /// <param name="maxTokens">The maximum tokens to generate.</param>
    /// <param name="topP">The nucleus sampling probability.</param>
    /// <param name="frequencyPenalty">The frequency penalty.</param>
    /// <param name="presencePenalty">The presence penalty.</param>
    /// <param name="stopSequences">The stop sequences.</param>
    /// <param name="seed">The random seed.</param>
    public void Deconstruct(
        out double temperature,
        out int maxTokens,
        out double topP,
        out double frequencyPenalty,
        out double presencePenalty,
        out ImmutableArray<string> stopSequences,
        out int? seed)
    {
        temperature = Temperature;
        maxTokens = MaxTokens;
        topP = TopP;
        frequencyPenalty = FrequencyPenalty;
        presencePenalty = PresencePenalty;
        stopSequences = StopSequences;
        seed = Seed;
    }
}
