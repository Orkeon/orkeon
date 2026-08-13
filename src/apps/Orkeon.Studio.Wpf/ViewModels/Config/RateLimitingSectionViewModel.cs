using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>The <c>RateLimiting</c> form (spec §4.1): five optional integer budgets.</summary>
public sealed class RateLimitingSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the form to the <c>RateLimiting</c> section of the document.</summary>
    public RateLimitingSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
    }

    private RateLimitingSection Section => Document.RateLimiting;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>How many LLM calls may be in flight at once.</summary>
    public int? MaxConcurrentRequests
    {
        get => Section.MaxConcurrentRequests;
        set => SetValue(Section.MaxConcurrentRequests, value, v => Section.MaxConcurrentRequests = v);
    }

    /// <summary>Ceiling across every provider and agent.</summary>
    public int? GlobalRequestsPerMinute
    {
        get => Section.GlobalRequestsPerMinute;
        set => SetValue(Section.GlobalRequestsPerMinute, value, v => Section.GlobalRequestsPerMinute = v);
    }

    /// <summary>Ceiling per provider.</summary>
    public int? ProviderRequestsPerMinute
    {
        get => Section.ProviderRequestsPerMinute;
        set => SetValue(Section.ProviderRequestsPerMinute, value, v => Section.ProviderRequestsPerMinute = v);
    }

    /// <summary>Ceiling per agent.</summary>
    public int? AgentRequestsPerMinute
    {
        get => Section.AgentRequestsPerMinute;
        set => SetValue(Section.AgentRequestsPerMinute, value, v => Section.AgentRequestsPerMinute = v);
    }

    /// <summary>How many calls may wait once the limiter is saturated.</summary>
    public int? QueueLimit
    {
        get => Section.QueueLimit;
        set => SetValue(Section.QueueLimit, value, v => Section.QueueLimit = v);
    }

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }
}
