using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Session;

namespace Orkeon.Infrastructure.LLMs.Context;

/// <summary>
/// The context window a loop compacts against (#432), in this order: the profile's own
/// <c>ContextWindowTokens</c>, else the documented window of the model
/// (<see cref="LlmModelContextWindows"/>), else <c>Orkeon:Cli:Session:ContextWindowTokens</c>
/// (200,000 by default), with one warning per profile that names the figure assumed.
/// </summary>
internal sealed partial class ContextWindowResolver : IContextWindowResolver
{
    private readonly int _settingsWindow;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, byte> _warned = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="settingsWindow">
    /// <c>Orkeon:Cli:Session:ContextWindowTokens</c>, or null for <see cref="CliSessionOptions.DefaultContextWindowTokens"/>.
    /// </param>
    /// <param name="logger">Where the warning of an assumed window goes.</param>
    public ContextWindowResolver(int? settingsWindow, ILogger logger)
    {
        _settingsWindow = settingsWindow is > 0 ? settingsWindow.Value : CliSessionOptions.DefaultContextWindowTokens;
        _logger = logger;
    }

    /// <inheritdoc />
    public ContextWindow Resolve(string profile, string? model, int? profileWindowTokens)
    {
        if (profileWindowTokens is > 0)
            return new ContextWindow(profileWindowTokens.Value, ContextWindowSources.Profile);

        if (LlmModelContextWindows.ContextWindowTokens(model) is { } documented)
            return new ContextWindow(documented, ContextWindowSources.Table);

        if (_warned.TryAdd(profile, 0))
            LogAssumedWindow(_logger, profile, string.IsNullOrWhiteSpace(model) ? "(the profile's default)" : model, _settingsWindow);
        return new ContextWindow(_settingsWindow, ContextWindowSources.Settings);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "LLM profile '{Profile}' sets no ContextWindowTokens and the window of model {Model} is not documented: the agent loops compact against {Window} tokens (Orkeon:Cli:Session:ContextWindowTokens). Set Llm:ContextWindowTokens or Llm:Profiles:{Profile}:ContextWindowTokens to the model's window.")]
    private static partial void LogAssumedWindow(ILogger logger, string profile, string model, int window);
}
