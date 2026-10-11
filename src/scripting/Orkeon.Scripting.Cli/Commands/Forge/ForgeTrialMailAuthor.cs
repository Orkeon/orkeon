using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>A trial mailbox seed and the note saying how it was drawn.</summary>
internal sealed record ForgeTrialMailbox(TrialMailboxSeed Seed, string Note);

/// <summary>Draws the mails of a need into the trial mailbox, once per session.</summary>
internal sealed class ForgeTrialMailAuthor(ILlmProvider? provider)
{
    /// <summary>The session artifact holding the drawn mails.</summary>
    public const string FileName = "trial-mailbox.json";

    private const int MaxMessages = 5;

    private const string SystemPrompt =
        "Write 0 to 5 fictitious e-mails a mailbox would plausibly hold for this need. "
        + "Use fictitious example.com or example.org addresses only, never real data. "
        + "Respond with exactly one JSON object: "
        + "{\"messages\":[{\"folder\":\"Inbox\",\"from\":\"...\",\"to\":[\"...\"],\"subject\":\"...\",\"body\":\"...\"}]}";

    internal static readonly IReadOnlySet<string> MailTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "email_accounts", "email_folders", "email_search", "email_read", "email_save_attachment",
        "email_create_folder", "email_rename_folder", "email_move", "email_mark", "email_delete",
        "email_draft", "email_send",
    };

    /// <summary>The seed of the session's trial mailbox.</summary>
    public async Task<ForgeTrialMailbox> SeedAsync(
        ForgeSession session, ForgeBrief? brief, IReadOnlyCollection<string> crewTools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(crewTools);

        var stored = session.TryLoadArtifact<StoredMailbox>(FileName);
        if (stored is not null)
            return new ForgeTrialMailbox(TrialMailboxSeed.Sample().With(stored.Messages), stored.Note);

        if (!crewTools.Any(MailTools.Contains))
            return new ForgeTrialMailbox(TrialMailboxSeed.Sample(), "");

        ImmutableList<TrialMailMessage> messages = [];
        var note = "";
        try
        {
            messages = await DrawAsync(brief, cancellationToken).ConfigureAwait(false);
            if (messages.Count == 0)
                note = Failure("no usable mail was returned");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            note = Failure(ex.Message);
        }

        session.SaveArtifact(FileName, new StoredMailbox { Messages = messages, Note = note });
        return new ForgeTrialMailbox(TrialMailboxSeed.Sample().With(messages), note);
    }

    private static string Failure(string reason) =>
        $"The mails drawn from the need could not be written ({reason}); the try runs on the sample mailbox alone.";

    private async Task<ImmutableList<TrialMailMessage>> DrawAsync(ForgeBrief? brief, CancellationToken cancellationToken)
    {
        if (provider is null)
            throw new InvalidOperationException("no model is available");

        var config = provider.BaseConfig ?? LlmConfig.OnProfile();
        if (provider.Capabilities.ResponseFormat != ResponseFormatSupport.None)
            config = config with { ResponseFormat = LlmResponseFormat.JsonObject() };

        LlmMessage[] prompt =
        [
            LlmMessage.System(SystemPrompt),
            new LlmMessage { Role = "user", Content = BuildBody(brief) },
        ];
        var response = await provider.ChatAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        return Parse(response.Content);
    }

    private static string BuildBody(ForgeBrief? brief)
    {
        var body = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(brief?.Goal))
            body.Append("Goal: ").AppendLine(brief.Goal);
        if (!string.IsNullOrWhiteSpace(brief?.ExpectedOutput?.Description))
            body.Append("Expected output: ").AppendLine(brief.ExpectedOutput.Description);
        if (!string.IsNullOrWhiteSpace(brief?.Sample?.InitialContext))
            body.Append("Sample input: ").AppendLine(brief.Sample.InitialContext);
        return body.ToString();
    }

    private static ImmutableList<TrialMailMessage> Parse(string? content)
    {
        using var document = JsonDocument.Parse(content ?? "");
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("messages", out var array)
            || array.ValueKind != JsonValueKind.Array)
            return [];

        var now = DateTimeOffset.UtcNow;
        var result = ImmutableList.CreateBuilder<TrialMailMessage>();
        foreach (var item in array.EnumerateArray())
        {
            if (result.Count == MaxMessages)
                break;
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var from = Text(item, "from");
            var subject = Text(item, "subject");
            if (from.Length == 0 || subject.Length == 0)
                continue;

            var folder = Text(item, "folder");
            result.Add(new TrialMailMessage
            {
                Folder = folder.Length == 0 ? "Inbox" : folder,
                From = from,
                To = item.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Array
                    ? [.. to.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!)]
                    : [],
                Subject = subject,
                Body = Text(item, "body"),
                Date = now,
            });
        }

        return result.ToImmutable();
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? ""
            : "";

    private sealed record StoredMailbox
    {
        public ImmutableList<TrialMailMessage> Messages { get; init; } = [];

        public string Note { get; init; } = "";
    }
}
