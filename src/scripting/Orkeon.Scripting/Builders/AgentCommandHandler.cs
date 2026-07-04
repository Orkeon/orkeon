using Jint.Native;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// One <c>onCommand</c> declaration on an agent (design §8 item 9): the JS closure that
/// produces the response terminating a dispatched command, optionally scoped to a single
/// <see cref="Intent"/>.
/// </summary>
/// <param name="Intent">Intent this handler answers, or <see langword="null"/> to answer any intent.</param>
/// <param name="Handler">The JS closure invoked with the command envelope; returns the response payload.</param>
public sealed record AgentCommandHandler(string? Intent, JsValue Handler);
