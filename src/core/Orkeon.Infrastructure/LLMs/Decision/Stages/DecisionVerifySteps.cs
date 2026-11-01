namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>Which responses a <see cref="VerifyDecisionStage"/> judges.</summary>
public enum DecisionVerifySteps
{
    /// <summary>The final answers only: a response that calls tools is a step and passes unjudged.</summary>
    Final = 0,

    /// <summary>Every response, the tool-call steps of an agent loop included.</summary>
    All = 1,
}
