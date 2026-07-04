using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Application.Callback;

/// <summary>
/// Default <see cref="ICrewKickoffHookRunner"/> implementation. Collects the
/// <see cref="BeforeKickoffHook"/> / <see cref="AfterKickoffHook"/> instances registered
/// in DI and executes them sequentially in ascending priority order.
/// </summary>
/// <remarks>
/// Wiring into the crew execution pipeline is deliberately left to the host (the
/// orchestrator refactor is tracked separately — R4.1): call
/// <see cref="RunBeforeKickoffAsync"/> before and <see cref="RunAfterKickoffAsync"/>
/// after the crew kickoff.
/// </remarks>
public sealed partial class CrewKickoffHookRunner : ICrewKickoffHookRunner
{
    private readonly IReadOnlyList<BeforeKickoffHook> _beforeHooks;
    private readonly IReadOnlyList<AfterKickoffHook> _afterHooks;
    private readonly ILogger<CrewKickoffHookRunner> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CrewKickoffHookRunner"/>.
    /// </summary>
    /// <param name="beforeHooks">Hooks executed before kickoff (resolved from DI).</param>
    /// <param name="afterHooks">Hooks executed after kickoff (resolved from DI).</param>
    /// <param name="logger">Optional logger.</param>
    public CrewKickoffHookRunner(
        IEnumerable<BeforeKickoffHook> beforeHooks,
        IEnumerable<AfterKickoffHook> afterHooks,
        ILogger<CrewKickoffHookRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(beforeHooks);
        ArgumentNullException.ThrowIfNull(afterHooks);
        _beforeHooks = beforeHooks.OrderBy(h => h.Priority).ToList();
        _afterHooks = afterHooks.OrderBy(h => h.Priority).ToList();
        _logger = logger ?? NullLogger<CrewKickoffHookRunner>.Instance;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task RunBeforeKickoffAsync(
        DomainCrew crew, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return RunBeforeKickoffCoreAsync();

        async System.Threading.Tasks.Task RunBeforeKickoffCoreAsync()
        {
            foreach (var hook in _beforeHooks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await hook.Execute(crew).ConfigureAwait(false);
                }
                catch (Exception ex) when (hook.ContinueOnError)
                {
                    LogHookFailed(_logger, "before-kickoff", hook.Name, ex);
                }
            }
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task RunAfterKickoffAsync(
        DomainCrew crew, object? result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(crew);
        return RunAfterKickoffCoreAsync();

        async System.Threading.Tasks.Task RunAfterKickoffCoreAsync()
        {
            foreach (var hook in _afterHooks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await hook.Execute(crew, result).ConfigureAwait(false);
                }
                catch (Exception ex) when (hook.ContinueOnError)
                {
                    LogHookFailed(_logger, "after-kickoff", hook.Name, ex);
                }
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Kickoff hook '{HookName}' ({Phase}) threw; ContinueOnError is set, continuing.")]
    private static partial void LogHookFailed(
        ILogger logger, string phase, string hookName, Exception exception);
}
