namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over the <c>RateLimiting</c> section (shape of
/// <c>examples/appsettings/appsettings.json</c>).
/// </summary>
public sealed class RateLimitingSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = "RateLimiting";

    private readonly AppSettingsDocument _document;

    internal RateLimitingSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the section carries at least one key.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>Maximum in-flight LLM requests (<c>RateLimiting:MaxConcurrentRequests</c>).</summary>
    public int? MaxConcurrentRequests
    {
        get => _document.GetInt32($"{SectionPath}:MaxConcurrentRequests");
        set => _document.SetInt32($"{SectionPath}:MaxConcurrentRequests", value);
    }

    /// <summary>Process-wide request budget per minute.</summary>
    public int? GlobalRequestsPerMinute
    {
        get => _document.GetInt32($"{SectionPath}:GlobalRequestsPerMinute");
        set => _document.SetInt32($"{SectionPath}:GlobalRequestsPerMinute", value);
    }

    /// <summary>Per-provider request budget per minute.</summary>
    public int? ProviderRequestsPerMinute
    {
        get => _document.GetInt32($"{SectionPath}:ProviderRequestsPerMinute");
        set => _document.SetInt32($"{SectionPath}:ProviderRequestsPerMinute", value);
    }

    /// <summary>Per-agent request budget per minute.</summary>
    public int? AgentRequestsPerMinute
    {
        get => _document.GetInt32($"{SectionPath}:AgentRequestsPerMinute");
        set => _document.SetInt32($"{SectionPath}:AgentRequestsPerMinute", value);
    }

    /// <summary>Maximum queued requests before rejection.</summary>
    public int? QueueLimit
    {
        get => _document.GetInt32($"{SectionPath}:QueueLimit");
        set => _document.SetInt32($"{SectionPath}:QueueLimit", value);
    }

    /// <summary>Removes the whole section.</summary>
    public void Remove() => _document.Remove(SectionPath);
}
