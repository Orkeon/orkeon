namespace Orkeon.Infrastructure.LLMs.Decision.Stages;

/// <summary>When a guard stage judges: the request, the answer, or both.</summary>
public enum DecisionStagePhase
{
    /// <summary>Before the call, on the conversation.</summary>
    Request,

    /// <summary>After the call, on the answer.</summary>
    Response,

    /// <summary>Both before and after.</summary>
    Both,
}
