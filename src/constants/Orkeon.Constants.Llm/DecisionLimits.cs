namespace Orkeon.Constants.Llm;

/// <summary>Bounds of a decision task.</summary>
public static class DecisionLimits
{
    /// <summary>Most options a choice question holds.</summary>
    public const int MaxChoiceOptions = 255;

    /// <summary>Most levels a score question holds.</summary>
    public const int MaxScoreLevels = 10;

    /// <summary>Confidence floor used when a decision gives none.</summary>
    public const double DefaultConfidenceFloor = 0.5;

    /// <summary>Tokens a decision request holds before the model refuses it.</summary>
    public const int StandardContextTokens = 64000;

    /// <summary>Tokens a single question holds before the model refuses it.</summary>
    public const int QuestionContextTokens = 32000;

    /// <summary>Tokens a decision request holds in long context.</summary>
    public const int LongContextTokens = 262000;
}
