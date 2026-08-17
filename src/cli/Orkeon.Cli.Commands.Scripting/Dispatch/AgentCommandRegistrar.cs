using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Scripting.Builders;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// Wires an agent's <c>onCommand</c> declarations onto <see cref="IAgentChannel"/> and records
/// its name in the <see cref="AgentCommandDirectory"/> — the seam of design §8 item 9 that lets
/// a <c>.cmd.ts</c> command (a different engine) dispatch to an agent by name.
/// </summary>
/// <remarks>
/// The handler is user JS and must run on the agent's engine under its engine lock —
/// <see cref="IAgentChannel.RequestAsync"/> awaits it directly, so the lock is what keeps Jint
/// single-threaded even when the call originates from a pool thread (an async <c>post</c>).
/// </remarks>
public static partial class AgentCommandRegistrar
{
    /// <summary>Registers <paramref name="agent"/>'s handlers and returns a handle that unregisters everything on dispose.</summary>
    public static IDisposable Register(
        JsAgent agent,
        Engine engine,
        SemaphoreSlim engineLock,
        IAgentChannel channel,
        AgentCommandDirectory directory,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(engineLock);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(directory);
        if (agent.CommandHandlers.Count == 0)
            throw new InvalidOperationException($"Agent '{agent.name}' declares no onCommand handler to register.");

        var log = logger ?? NullLogger.Instance;
        var agentId = AgentId.Parse(agent.id);
        var handlers = agent.CommandHandlers;

        var channelReg = channel.RegisterHandler(agentId, async (request, ct) =>
        {
            var handler = Select(handlers, request.Intent);
            if (handler is null)
                return AgentChannelResponse.Fail(request.CorrelationId, agentId,
                    $"agent '{agent.name}' has no onCommand handler for intent '{request.Intent}'");

            await engineLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var envelope = new AgentCommandEnvelope(
                    request.Intent, request.Payload, request.FromAgentId.ToString(), request.CorrelationId.ToString());
                var result = engine.Invoke(handler.Handler, new[] { JsValue.FromObject(engine, envelope) });
                if (result.IsPromise())
                    result = await Jint.JsValueExtensions.UnwrapIfPromiseAsync(result, ct).ConfigureAwait(false);
                return MapResponse(request.CorrelationId, agentId, result);
            }
            catch (Jint.Runtime.JavaScriptException ex)
            {
                LogHandlerThrew(log, ex, agent.name, request.Intent);
                return AgentChannelResponse.Fail(request.CorrelationId, agentId, ex.Message);
            }
            finally
            {
                engineLock.Release();
            }
        });

        var directoryReg = directory.Register(agent.name, agentId);
        return new CompositeDisposable(channelReg, directoryReg);
    }

    private static AgentCommandHandler? Select(IReadOnlyList<AgentCommandHandler> handlers, string intent)
    {
        // Exact-intent match wins; otherwise the first catch-all (null intent) handler.
        AgentCommandHandler? catchAll = null;
        foreach (var h in handlers)
        {
            if (h.Intent is null) { catchAll ??= h; continue; }
            if (string.Equals(h.Intent, intent, StringComparison.OrdinalIgnoreCase)) return h;
        }
        return catchAll;
    }

    private static AgentChannelResponse MapResponse(Guid correlationId, AgentId agentId, JsValue result)
    {
        if (result.IsUndefined() || result.IsNull())
            return AgentChannelResponse.Ok(correlationId, agentId, string.Empty);

        if (result.IsString())
            return AgentChannelResponse.Ok(correlationId, agentId, result.AsString());

        if (result.IsObject())
        {
            var obj = result.AsObject();
            var successVal = obj.Get("success");
            var payloadVal = obj.Get("payload");
            var errorVal = obj.Get("error");
            var success = successVal.IsUndefined() || (successVal.IsBoolean() && successVal.AsBoolean());
            var payload = payloadVal.IsString() ? payloadVal.AsString() : string.Empty;
            if (!success)
            {
                var error = errorVal.IsString() ? errorVal.AsString() : "agent handler returned success=false";
                return AgentChannelResponse.Fail(correlationId, agentId, error);
            }
            return AgentChannelResponse.Ok(correlationId, agentId, payload);
        }

        return AgentChannelResponse.Ok(correlationId, agentId, result.ToString() ?? string.Empty);
    }

    private sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failed Dispose of one subscription must not prevent disposing the others.")]
        public void Dispose()
        {
            foreach (var d in disposables)
            {
                try { d.Dispose(); } catch { /* best effort */ }
            }
        }
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Agent '{Agent}' onCommand handler threw for intent '{Intent}'")]
    static partial void LogHandlerThrew(ILogger logger, Exception ex, string agent, string intent);
}
