using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Decision;

/// <summary>
/// A decision stage refused a model call: a decision, expected, that the user should be told of,
/// not a provider failure. A blocked call answers an <see cref="LlmResponse"/> whose
/// <see cref="LlmResponse.ErrorType"/> is <see cref="ErrorType"/>, with the profile, the stage,
/// the risk and the reason in its metadata; <see cref="FromResponse"/> reads them back, and a
/// caller that cannot return such a response (a text stream) throws this exception instead.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3925", Justification = "False positive: binary serialization of exceptions is obsolete in .NET 10 (SYSLIB0051), so no ISerializable constructor or GetObjectData is added.")]
public sealed class DecisionBlockedException : Exception
{
    /// <summary>The <see cref="LlmResponse.ErrorType"/> of a call a decision stage blocked.</summary>
    public const string ErrorType = "DecisionBlocked";

    /// <summary>The metadata key naming the decision profile that blocked a call.</summary>
    public const string ProfileMetadataKey = "decision_profile";

    /// <summary>The metadata key naming the stage that blocked a call.</summary>
    public const string StageMetadataKey = "decision_stage";

    /// <summary>The metadata key naming the risk a guard blocked a call on; absent for another stage.</summary>
    public const string RiskMetadataKey = "decision_risk";

    /// <summary>The metadata key carrying why the stage blocked the call.</summary>
    public const string ReasonMetadataKey = "decision_reason";

    /// <summary>Initializes a new instance of <see cref="DecisionBlockedException"/>.</summary>
    public DecisionBlockedException()
        : this("A decision stage blocked the call.")
    {
    }

    /// <summary>Initializes a new instance of <see cref="DecisionBlockedException"/>.</summary>
    /// <param name="message">The sentence a caller is shown.</param>
    public DecisionBlockedException(string message)
        : base(message)
    {
        Profile = string.Empty;
        Stage = string.Empty;
    }

    /// <summary>Initializes a new instance of <see cref="DecisionBlockedException"/>.</summary>
    /// <param name="message">The sentence a caller is shown.</param>
    /// <param name="innerException">The cause, if any.</param>
    public DecisionBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Profile = string.Empty;
        Stage = string.Empty;
    }

    /// <summary>Initializes a new instance of <see cref="DecisionBlockedException"/>.</summary>
    /// <param name="message">The sentence a caller is shown.</param>
    /// <param name="profile">The decision profile.</param>
    /// <param name="stage">The stage that blocked the call.</param>
    /// <param name="risk">The risk a guard matched; null for another stage.</param>
    /// <param name="reason">Why the stage blocked the call.</param>
    public DecisionBlockedException(string message, string profile, string stage, string? risk, string? reason)
        : base(message)
    {
        Profile = profile;
        Stage = stage;
        Risk = risk;
        Reason = reason;
    }

    /// <summary>Gets the decision profile that blocked the call.</summary>
    public string Profile { get; }

    /// <summary>Gets the stage that blocked the call.</summary>
    public string Stage { get; }

    /// <summary>Gets the risk a guard matched; null for another stage.</summary>
    public string? Risk { get; }

    /// <summary>Gets why the stage blocked the call.</summary>
    public string? Reason { get; }

    /// <summary>
    /// The block <paramref name="response"/> carries, or null when it is not a blocked call (an
    /// answer, or a provider failure).
    /// </summary>
    /// <param name="response">The provider's answer.</param>
    /// <returns>The exception describing the block, or null.</returns>
    public static DecisionBlockedException? FromResponse(LlmResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!string.Equals(response.ErrorType, ErrorType, StringComparison.Ordinal))
            return null;
        return new DecisionBlockedException(
            response.Error ?? "A decision stage blocked the call.",
            Read(response, ProfileMetadataKey) ?? string.Empty,
            Read(response, StageMetadataKey) ?? string.Empty,
            Read(response, RiskMetadataKey),
            Read(response, ReasonMetadataKey));
    }

    private static string? Read(LlmResponse response, string key) =>
        response.Metadata.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text ? text : null;
}
