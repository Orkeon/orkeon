namespace Orkeon.Application.Decision;

/// <summary>What a decision stage decided about a model call.</summary>
public enum DecisionVerdictKind
{
    /// <summary>The call goes on unchanged.</summary>
    Continue,

    /// <summary>The call is refused: the caller receives a structured error, not an answer.</summary>
    Block,

    /// <summary>Before the call: the conversation is sent to another profile instead.</summary>
    Route,

    /// <summary>After the call: the conversation is sent again, once, to a stronger profile.</summary>
    Escalate,
}
