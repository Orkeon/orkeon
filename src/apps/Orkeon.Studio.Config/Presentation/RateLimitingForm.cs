using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>The <c>RateLimiting</c> section as text fields — five request budgets.</summary>
internal sealed class RateLimitingForm : ISettingsForm
{
    /// <inheritdoc />
    public string Title => "Rate limiting";

    /// <summary>Maximum in-flight LLM requests.</summary>
    public string MaxConcurrentRequests { get; set; } = "";

    /// <summary>Process-wide request budget per minute.</summary>
    public string GlobalRequestsPerMinute { get; set; } = "";

    /// <summary>Per-provider request budget per minute.</summary>
    public string ProviderRequestsPerMinute { get; set; } = "";

    /// <summary>Per-agent request budget per minute.</summary>
    public string AgentRequestsPerMinute { get; set; } = "";

    /// <summary>Maximum queued requests before rejection.</summary>
    public string QueueLimit { get; set; } = "";

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.RateLimiting;
        MaxConcurrentRequests = FieldText.FromInt32(section.MaxConcurrentRequests);
        GlobalRequestsPerMinute = FieldText.FromInt32(section.GlobalRequestsPerMinute);
        ProviderRequestsPerMinute = FieldText.FromInt32(section.ProviderRequestsPerMinute);
        AgentRequestsPerMinute = FieldText.FromInt32(section.AgentRequestsPerMinute);
        QueueLimit = FieldText.FromInt32(section.QueueLimit);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        var errors = new List<string>();

        var concurrent = Read(MaxConcurrentRequests, "RateLimiting:MaxConcurrentRequests", errors);
        var global = Read(GlobalRequestsPerMinute, "RateLimiting:GlobalRequestsPerMinute", errors);
        var provider = Read(ProviderRequestsPerMinute, "RateLimiting:ProviderRequestsPerMinute", errors);
        var agent = Read(AgentRequestsPerMinute, "RateLimiting:AgentRequestsPerMinute", errors);
        var queue = Read(QueueLimit, "RateLimiting:QueueLimit", errors);

        if (errors.Count > 0)
            return errors;

        var section = document.RateLimiting;
        section.MaxConcurrentRequests = concurrent;
        section.GlobalRequestsPerMinute = global;
        section.ProviderRequestsPerMinute = provider;
        section.AgentRequestsPerMinute = agent;
        section.QueueLimit = queue;

        return [];
    }

    private static int? Read(string text, string fieldName, List<string> errors)
    {
        if (FieldText.TryReadInt32(text, fieldName, out var value, out var error))
            return value;

        errors.Add(error!);
        return null;
    }
}
