namespace Orkeon.Infrastructure.HumanInput;

/// <summary>What a run does when a task asks a human and nobody has answered yet (FWK-23).</summary>
public enum HumanInputPause
{
    /// <summary>The run waits for the answer, up to <see cref="HumanInputPauseOptions.HoldTimeout"/>, then suspends.</summary>
    Hold,

    /// <summary>The run stops at once: the question is persisted, the run ends as <c>waiting_for_input</c> (exit 75).</summary>
    Suspend,
}

/// <summary>The <c>Orkeon:HumanInput</c> section: how a run pauses on a question (FWK-23). Active only with <c>Orkeon:Checkpointing:Enabled</c>.</summary>
public sealed class HumanInputPauseOptions
{
    /// <summary>The section's configuration path.</summary>
    public const string SectionName = "Orkeon:HumanInput";

    /// <summary>
    /// Gets or sets the policy. Unset, the host decides: <see cref="HumanInputPause.Hold"/> in the daemon and under
    /// <c>--events</c>, <see cref="HumanInputPause.Suspend"/> in a plain <c>orkeon run</c>.
    /// </summary>
    public HumanInputPause? Pause { get; set; }

    /// <summary>
    /// Gets or sets how long a held run waits before it suspends (the question stays persisted, nothing is refused). Unset,
    /// the host decides: 30 minutes in the daemon, no limit under <c>--events</c>.
    /// </summary>
    public TimeSpan? HoldTimeout { get; set; }
}

/// <summary>The keys the durable provider adds to a <see cref="Orkeon.Domain.HumanInput.HumanInputContext"/> it hands the provider it wraps.</summary>
public static class HumanInputPauseMetadata
{
    /// <summary>The question's durable id (<c>{TaskKey}#{n}.{q}</c>): the provider that asks a person correlates the answer with it.</summary>
    public const string RequestId = "orkeon.requestId";
}
