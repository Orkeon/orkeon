using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>RateLimiting</c> form (spec §4.1): five optional integer budgets. Each field carries
/// the engine's default as a watermark (STUDIO-22): an empty box is not "nothing", it is the
/// value the engine will apply, and the screen says which.
/// </summary>
public sealed class RateLimitingSectionViewModel : DocumentSectionViewModel
{
    private readonly IStudioStrings _strings;

    /// <summary>Binds the form to the <c>RateLimiting</c> section of the document.</summary>
    public RateLimitingSectionViewModel(Func<AppSettingsDocument> document, Action onChanged, IStudioStrings? strings = null)
        : base(document, onChanged)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        _strings.CultureChanged += (_, _) => OnPropertyChanged(nameof(MaxConcurrentRequestsDefault));
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

    /// <summary>The watermark: the engine's 0 reads as no concurrency bound, so the word rather than the number.</summary>
    public string MaxConcurrentRequestsDefault => _strings[StudioStringKeys.SettingsUnlimited];

    /// <summary>Ceiling across every provider and agent.</summary>
    public int? GlobalRequestsPerMinute
    {
        get => Section.GlobalRequestsPerMinute;
        set => SetValue(Section.GlobalRequestsPerMinute, value, v => Section.GlobalRequestsPerMinute = v);
    }

    /// <summary>The watermark of <see cref="GlobalRequestsPerMinute"/>.</summary>
    public static string GlobalRequestsPerMinuteDefault => Invariant(RateLimitingSection.DefaultGlobalRequestsPerMinute);

    /// <summary>Ceiling per provider.</summary>
    public int? ProviderRequestsPerMinute
    {
        get => Section.ProviderRequestsPerMinute;
        set => SetValue(Section.ProviderRequestsPerMinute, value, v => Section.ProviderRequestsPerMinute = v);
    }

    /// <summary>The watermark of <see cref="ProviderRequestsPerMinute"/>.</summary>
    public static string ProviderRequestsPerMinuteDefault => Invariant(RateLimitingSection.DefaultProviderRequestsPerMinute);

    /// <summary>Ceiling per agent.</summary>
    public int? AgentRequestsPerMinute
    {
        get => Section.AgentRequestsPerMinute;
        set => SetValue(Section.AgentRequestsPerMinute, value, v => Section.AgentRequestsPerMinute = v);
    }

    /// <summary>The watermark of <see cref="AgentRequestsPerMinute"/>.</summary>
    public static string AgentRequestsPerMinuteDefault => Invariant(RateLimitingSection.DefaultAgentRequestsPerMinute);

    /// <summary>How many calls may wait once the limiter is saturated.</summary>
    public int? QueueLimit
    {
        get => Section.QueueLimit;
        set => SetValue(Section.QueueLimit, value, v => Section.QueueLimit = v);
    }

    /// <summary>The watermark of <see cref="QueueLimit"/>.</summary>
    public static string QueueLimitDefault => Invariant(RateLimitingSection.DefaultQueueLimit);

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        Refresh();
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
