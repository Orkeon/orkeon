using System.Diagnostics;
using System.Diagnostics.Metrics;
using Orkeon.Domain.Constants.Platform;

namespace Orkeon.Infrastructure.Telemetry;

/// <summary>
/// OpenTelemetry metrics for Orkeon operations.
/// Provides counters, histograms, and gauges for monitoring crew, agent, task, LLM, and tool metrics.
/// </summary>
public sealed class OrkeonMetrics : IDisposable
{
    /// <summary>
    /// Meter name used for OpenTelemetry metrics registration.
    /// </summary>
    public const string MeterName = "Orkeon";

    private readonly Meter _meter;

    /// <summary>
    /// The underlying Meter instance. Exposed for test isolation: a MeterListener that
    /// filters by Meter name alone would also capture measurements from other OrkeonMetrics
    /// instances created by parallel test classes (same <see cref="MeterName"/>). Filtering
    /// by this instance reference keeps each test observing only its own metrics.
    /// </summary>
    internal Meter Meter => _meter;

    // Counters
    private readonly Counter<long> _llmCallsCounter;
    private readonly Counter<long> _llmTokensCounter;
    private readonly Counter<long> _toolExecutionsCounter;
    private readonly Counter<long> _taskExecutionsCounter;
    private readonly Counter<long> _crewExecutionsCounter;
    private readonly Counter<long> _securityEventsCounter;

    // Histograms
    private readonly Histogram<double> _llmDurationHistogram;
    private readonly Histogram<double> _toolDurationHistogram;
    private readonly Histogram<double> _taskDurationHistogram;
    private readonly Histogram<double> _crewDurationHistogram;

    // UpDownCounters for active operations
    private readonly UpDownCounter<long> _activeCrews;
    private readonly UpDownCounter<long> _activeTasks;
    private readonly UpDownCounter<long> _activeLlmCalls;

    // Cost tracking
    private double _totalCostUsd;
    private readonly object _costLock = new();

    /// <summary>Initializes a new instance of <see cref="OrkeonMetrics"/> and registers all OpenTelemetry instruments.</summary>
    public OrkeonMetrics()
    {
        _meter = new Meter(MeterName, OrkeonDiagnostics.ServiceVersion);

        // Counters
        _llmCallsCounter = _meter.CreateCounter<long>(
            "orkeon.llm.calls",
            unit: "{call}",
            description: "Total number of LLM API calls");

        _llmTokensCounter = _meter.CreateCounter<long>(
            "orkeon.llm.tokens",
            unit: "{token}",
            description: "Total number of tokens consumed by LLM calls");

        _toolExecutionsCounter = _meter.CreateCounter<long>(
            "orkeon.tool.executions",
            unit: "{execution}",
            description: "Total number of tool executions");

        _taskExecutionsCounter = _meter.CreateCounter<long>(
            "orkeon.task.executions",
            unit: "{execution}",
            description: "Total number of task executions");

        _crewExecutionsCounter = _meter.CreateCounter<long>(
            "orkeon.crew.executions",
            unit: "{execution}",
            description: "Total number of crew executions");

        _securityEventsCounter = _meter.CreateCounter<long>(
            "orkeon.security.events",
            unit: "{event}",
            description: "Total number of security events");

        // Histograms
        _llmDurationHistogram = _meter.CreateHistogram<double>(
            "orkeon.llm.duration",
            unit: "ms",
            description: "Duration of LLM API calls in milliseconds");

        _toolDurationHistogram = _meter.CreateHistogram<double>(
            "orkeon.tool.duration",
            unit: "ms",
            description: "Duration of tool executions in milliseconds");

        _taskDurationHistogram = _meter.CreateHistogram<double>(
            "orkeon.task.duration",
            unit: "ms",
            description: "Duration of task executions in milliseconds");

        _crewDurationHistogram = _meter.CreateHistogram<double>(
            "orkeon.crew.duration",
            unit: "ms",
            description: "Duration of crew executions in milliseconds");

        // UpDownCounters
        _activeCrews = _meter.CreateUpDownCounter<long>(
            "orkeon.crew.active",
            unit: "{crew}",
            description: "Number of currently active crew executions");

        _activeTasks = _meter.CreateUpDownCounter<long>(
            "orkeon.task.active",
            unit: "{task}",
            description: "Number of currently active task executions");

        _activeLlmCalls = _meter.CreateUpDownCounter<long>(
            "orkeon.llm.active",
            unit: "{call}",
            description: "Number of currently active LLM calls");

        // Observable gauge for total cost
        _meter.CreateObservableGauge(
            "orkeon.cost.total_usd",
            observeValue: () => GetTotalCost(),
            unit: PlatformDefaults.DefaultCurrency,
            description: "Total estimated cost in USD");
    }

    /// <summary>
    /// Records an LLM call with its duration, token usage, and cost.
    /// </summary>
    public void RecordLlmCall(
        string provider,
        string model,
        double durationMs,
        int promptTokens = 0,
        int completionTokens = 0,
        double costUsd = 0.0,
        bool success = true)
    {
        var tags = new TagList
        {
            { OrkeonDiagnosticTags.LlmProvider, provider },
            { OrkeonDiagnosticTags.LlmModel, model },
            { OrkeonDiagnosticTags.LlmSuccess, success }
        };

        _llmCallsCounter.Add(1, tags);
        _llmDurationHistogram.Record(durationMs, tags);

        if (promptTokens + completionTokens > 0)
        {
            _llmTokensCounter.Add(promptTokens + completionTokens, tags);
        }

        if (costUsd > 0)
        {
            AddCost(costUsd);
        }
    }

    /// <summary>
    /// Records a tool execution with its duration and result.
    /// </summary>
    public void RecordToolExecution(
        string toolName,
        double durationMs,
        bool success = true,
        string? agentRole = null)
    {
        var tags = new TagList
        {
            { OrkeonDiagnosticTags.ToolName, toolName },
            { OrkeonDiagnosticTags.ToolSuccess, success }
        };

        if (agentRole is not null)
            tags.Add(OrkeonDiagnosticTags.AgentRole, agentRole);

        _toolExecutionsCounter.Add(1, tags);
        _toolDurationHistogram.Record(durationMs, tags);
    }

    /// <summary>
    /// Records a task execution with its duration and result.
    /// </summary>
    public void RecordTaskExecution(
        string taskId,
        double durationMs,
        bool success = true,
        string? agentRole = null)
    {
        var tags = new TagList
        {
            { OrkeonDiagnosticTags.TaskId, taskId },
            { OrkeonDiagnosticTags.TaskSuccess, success }
        };

        if (agentRole is not null)
            tags.Add(OrkeonDiagnosticTags.AgentRole, agentRole);

        _taskExecutionsCounter.Add(1, tags);
        _taskDurationHistogram.Record(durationMs, tags);
    }

    /// <summary>
    /// Records a crew execution with its duration and result.
    /// </summary>
    public void RecordCrewExecution(
        string crewId,
        double durationMs,
        bool success = true,
        string? processType = null)
    {
        var tags = new TagList
        {
            { OrkeonDiagnosticTags.CrewId, crewId }
        };

        if (processType is not null)
            tags.Add(OrkeonDiagnosticTags.CrewProcess, processType);

        _crewExecutionsCounter.Add(1, tags);
        _crewDurationHistogram.Record(durationMs, tags);
    }

    /// <summary>
    /// Records a security event.
    /// </summary>
    public void RecordSecurityEvent(string eventType)
    {
        _securityEventsCounter.Add(1, new TagList
        {
            { OrkeonDiagnosticTags.SecurityEventType, eventType }
        });
    }

    /// <summary>
    /// Marks a crew execution as started (increments active count).
    /// </summary>
    public void CrewStarted(string crewId)
    {
        _activeCrews.Add(1, new TagList
        {
            { OrkeonDiagnosticTags.CrewId, crewId }
        });
    }

    /// <summary>
    /// Marks a crew execution as completed (decrements active count).
    /// </summary>
    public void CrewCompleted(string crewId)
    {
        _activeCrews.Add(-1, new TagList
        {
            { OrkeonDiagnosticTags.CrewId, crewId }
        });
    }

    /// <summary>
    /// Marks a task execution as started (increments active count).
    /// </summary>
    public void TaskStarted(string taskId)
    {
        _activeTasks.Add(1, new TagList
        {
            { OrkeonDiagnosticTags.TaskId, taskId }
        });
    }

    /// <summary>
    /// Marks a task execution as completed (decrements active count).
    /// </summary>
    public void TaskCompleted(string taskId)
    {
        _activeTasks.Add(-1, new TagList
        {
            { OrkeonDiagnosticTags.TaskId, taskId }
        });
    }

    /// <summary>
    /// Marks an LLM call as started (increments active count).
    /// </summary>
    public void LlmCallStarted(string provider)
    {
        _activeLlmCalls.Add(1, new TagList
        {
            { OrkeonDiagnosticTags.LlmProvider, provider }
        });
    }

    /// <summary>
    /// Marks an LLM call as completed (decrements active count).
    /// </summary>
    public void LlmCallCompleted(string provider)
    {
        _activeLlmCalls.Add(-1, new TagList
        {
            { OrkeonDiagnosticTags.LlmProvider, provider }
        });
    }

    private void AddCost(double cost)
    {
        lock (_costLock)
        {
            _totalCostUsd += cost;
        }
    }

    private double GetTotalCost()
    {
        lock (_costLock)
        {
            return _totalCostUsd;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _meter.Dispose();
    }
}
