using Orkeon.Domain.Tools;

namespace Orkeon.Application.Crew.Execution;

/// <summary>What one tool call of a turn produced: the message for the conversation and the usage to record, appended by the caller in call order.</summary>
internal sealed record ToolCallOutcome<TMessage>(TMessage Message, ToolUsage? Usage);
