using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Decision;

/// <summary>What the LLM judge does when the decision model cannot answer.</summary>
public enum GuardianJudgeFailMode
{
    /// <summary>The input or output is refused: an unjudged text is not let through.</summary>
    Closed,

    /// <summary>The text is let through with a warning, and the unavailability is audited as <c>guardian.judge.unavailable</c>.</summary>
    Open,
}

/// <summary>The phases a model can judge.</summary>
public enum GuardianJudgePhase
{
    /// <summary>The composed prompt of an agent turn, before the first provider call.</summary>
    Input,

    /// <summary>The validated output of a task, before it is kept.</summary>
    Output,
}

/// <summary>
/// The <c>Orkeon:Guardian:Llm</c> section: the guardian that judges an agent's input and a task's
/// output with the decision model (the port <c>IDecisionModel</c>, never a profile of its own),
/// beside the built-in guards. Off unless <see cref="Enabled"/>: a judgement is a model call.
/// </summary>
public sealed class GuardianLlmOptions
{
    /// <summary>The configuration section, under <c>Orkeon:Guardian</c>.</summary>
    public const string SectionName = "Orkeon:Guardian:Llm";

    /// <summary>Gets or sets whether the judge runs; off by default (it costs a model call per phase).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets the phases judged, <c>Input</c> and/or <c>Output</c>; empty judges both. Any other value
    /// (the tool and delegation phases are checked by rules, not by a model) is refused at start.
    /// </summary>
    public System.Collections.ObjectModel.Collection<GuardianJudgePhase> Phases { get; init; } = [];

    /// <summary>Gets the risks detected, each with what it is; empty takes the default risks.</summary>
    public Dictionary<string, string> Risks { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the probability at or over which a risk blocks the text.</summary>
    public double BlockThreshold { get; set; } = 0.8;

    /// <summary>Gets or sets the probability at or over which a risk is a warning.</summary>
    public double WarnThreshold { get; set; } = 0.5;

    /// <summary>Gets or sets what happens when the decision model cannot judge: <c>Closed</c> (default) refuses, <c>Open</c> lets the text in; both audit <c>guardian.judge.unavailable</c>.</summary>
    public GuardianJudgeFailMode FailMode { get; set; } = GuardianJudgeFailMode.Closed;

    /// <summary>Gets or sets the characters of a text the judge reads in one call. Under <c>Closed</c> a longer text is judged whole, chunk by chunk (any chunk that blocks blocks), up to 32 chunks, and a text beyond that is refused; under <c>Open</c> it is judged on its first and last half of that budget, what lies between is not judged, and the cut is audited.</summary>
    public int MaxContentChars { get; set; } = 8000;

    /// <summary>Whether the judge runs in <paramref name="phase"/>.</summary>
    /// <param name="phase">The guard phase.</param>
    /// <returns>True for <c>Input</c> or <c>Output</c> when it is listed, or when the list is empty.</returns>
    public bool Judges(GuardPhase phase) => phase switch
    {
        GuardPhase.Input => Phases.Count == 0 || Phases.Contains(GuardianJudgePhase.Input),
        GuardPhase.Output => Phases.Count == 0 || Phases.Contains(GuardianJudgePhase.Output),
        _ => false,
    };
}
