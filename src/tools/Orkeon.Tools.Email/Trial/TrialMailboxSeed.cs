using System.Collections.Immutable;
using System.Text.Json;

namespace Orkeon.Tools.Email.Trial;

/// <summary>The messages a trial mailbox starts with.</summary>
public sealed record TrialMailboxSeed
{
    private const string SampleResource = "Orkeon.Tools.Email.Trial.Sample.sample-mailbox.json";

    /// <summary>The messages.</summary>
    public ImmutableList<TrialMailMessage> Messages { get; init; } = [];

    /// <summary>The embedded sample.</summary>
    public static TrialMailboxSeed Sample()
    {
        using var stream = typeof(TrialMailboxSeed).Assembly.GetManifestResourceStream(SampleResource)
            ?? throw new InvalidOperationException($"The embedded resource '{SampleResource}' is missing.");
        return JsonSerializer.Deserialize<TrialMailboxSeed>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"The embedded resource '{SampleResource}' is empty.");
    }

    /// <summary>A seed holding <paramref name="messages"/>.</summary>
    public TrialMailboxSeed With(IEnumerable<TrialMailMessage> messages) =>
        this with { Messages = Messages.AddRange(messages) };
}
