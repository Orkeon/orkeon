using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// Scripted <see cref="ILlmProvider"/>: a queue of canned responses, calls recorded.
/// <para>
/// It streams, like every provider the forge actually talks to. That is not decoration:
/// registering a delta sink is what puts the assistant on the SSE path, so a double that
/// only answers in one buffered piece exercises a branch production never takes.
/// </para>
/// </summary>
internal sealed class ScriptedLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>Characters per streamed fragment — an SSE chunk is a few characters, not a line.</summary>
    private const int ChunkSize = 5;

    private readonly Queue<LlmResponse> _responses = new();

    /// <summary>Every chat call's messages, in call order.</summary>
    public List<LlmMessage[]> Chats { get; } = [];

    /// <summary>Queues a plain text answer (no tool call).</summary>
    public ScriptedLlmProvider Answers(string content) =>
        Enqueue(new LlmResponse { Content = content, PromptTokens = 100, CompletionTokens = 20, TokensUsed = 120 });

    /// <summary>Queues a tool call in the OpenAI wire shape <c>JsLlmFacade.TryParseToolCall</c> reads.</summary>
    public ScriptedLlmProvider CallsTool(string name, object arguments)
    {
        var raw = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "",
                        tool_calls = new[]
                        {
                            new { function = new { name, arguments = JsonSerializer.Serialize(arguments) } },
                        },
                    },
                },
            },
        });

        return Enqueue(new LlmResponse
        {
            Content = "",
            RawResponseBody = raw,
            PromptTokens = 100,
            CompletionTokens = 30,
            TokensUsed = 130,
        });
    }

    private ScriptedLlmProvider Enqueue(LlmResponse response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <inheritdoc />
    public string Name => "scripted";

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Next());

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        Chats.Add(messages);
        return Task.FromResult(Next());
    }

    private LlmResponse Next() =>
        _responses.Count > 0 ? _responses.Dequeue() : new LlmResponse { Content = "(out of script)" };

    /// <inheritdoc />
    public bool SupportsStreaming => true;

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt, LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return Next().Content;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages, LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        Chats.Add(messages);
        var response = Next();

        for (var at = 0; at < response.Content.Length; at += ChunkSize)
        {
            yield return LlmStreamEvent.Content(
                response.Content.Substring(at, Math.Min(ChunkSize, response.Content.Length - at)));
        }

        yield return LlmStreamEvent.Complete(response);
    }
}

/// <summary>
/// The production assistant end to end, offline: the **real embedded pack** runs in Jint
/// over a scripted provider — the whole pipe (extraction, inputs, act loop, submission
/// tools, transcript, token tally) without a model and without a network.
/// </summary>
public sealed class ForgeCrewAssistantTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-assistant-" + Guid.NewGuid().ToString("N"));

    private readonly ForgeSession _session;
    private readonly ForgeSubmissionBox _box = new();
    private readonly ForgeUsageTally _tally = new();

    public ForgeCrewAssistantTests() => _session = ForgeSession.Create(_workspace, "essai");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ForgeCrewAssistant Build(ScriptedLlmProvider provider)
    {
        var packPath = ForgePack.Ensure(_session.Directory);

        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IFileSystemService>(new DiskBackedFileSystemService(_session.Directory, "/forge"))
            .AddSingleton(_box)
            .AddSingleton(_tally)
            .AddSingleton<ILlmUsageSink>(_tally)
            // Registered exactly as ForgeCommand does: production puts the tally on both
            // ports, and a fixture that only wires one cannot see a break in the other.
            .AddSingleton<ILlmDeltaSink>(_tally)
            .AddSingleton<IBaseTool>(new BriefSubmitTool(_box))
            .AddSingleton<IBaseTool>(new BlueprintSubmitTool(_box))
            .AddSingleton<ILlmProvider>(provider)
            .BuildServiceProvider();

        return new ForgeCrewAssistant(services, _session, packPath);
    }

    [Fact]
    public async Task A_conversation_turn_comes_back_as_a_message_and_lands_in_the_transcript()
    {
        var provider = new ScriptedLlmProvider().Answers("Bonjour ! Quel problème résolvons-nous ?");
        var assistant = Build(provider);

        var reply = await assistant.NextAsync(
            new ForgeAssistantRequest { Phase = ForgeAssistantPhase.Brief, UserMessage = "je veux une veille fournisseur" },
            spent: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("Bonjour ! Quel problème résolvons-nous ?", reply.Message);
        Assert.Null(reply.BriefJson);
        Assert.True(reply.Usage.TotalTokens > 0);

        var transcript = _session.LoadTranscript();
        Assert.Equal(["user", "assistant"], transcript.Select(t => t.Role));
        Assert.Equal("je veux une veille fournisseur", transcript[0].Text);

        // The system header carried the interview rules; the user body carried the message.
        var messages = Assert.Single(provider.Chats);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("brief_submit", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("je veux une veille fournisseur", messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_brief_submission_comes_back_as_the_validated_document()
    {
        var provider = new ScriptedLlmProvider()
            .CallsTool("brief_submit", new
            {
                goal = "Résumer les offres du fournisseur",
                acceptance = new[] { new { id = "A1", statement = "Cite ses sources", kind = "must" } },
            })
            .Answers("C'est envoyé.");
        var assistant = Build(provider);

        var reply = await assistant.NextAsync(
            new ForgeAssistantRequest { Phase = ForgeAssistantPhase.Brief, UserMessage = "vas-y" },
            spent: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply.BriefJson);
        Assert.True(ForgeBrief.TryParse(reply.BriefJson!, out var brief, out var errors), string.Join("; ", errors));
        Assert.Equal("Résumer les offres du fournisseur", brief!.Goal);

        // A submission is not conversation: only the user turn joined the transcript.
        Assert.Equal(["user"], _session.LoadTranscript().Select(t => t.Role));
    }

    [Fact]
    public async Task The_blueprint_phase_hands_the_brief_and_the_errors_to_the_model()
    {
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));

        var provider = new ScriptedLlmProvider().Answers("(thinking aloud, no submission)");
        var assistant = Build(provider);

        var reply = await assistant.NextAsync(
            new ForgeAssistantRequest
            {
                Phase = ForgeAssistantPhase.Blueprint,
                Brief = brief,
                Errors = ["FORGE-TOOL-UNKNOWN: agent 'x' names tool 'ghost_tool', which is not in the catalogue."],
            },
            spent: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(reply.Message);

        var messages = Assert.Single(provider.Chats);
        Assert.Contains("blueprint_submit", messages[0].Content, StringComparison.Ordinal);
        var body = messages[^1].Content;
        Assert.Contains("Résumer chaque matin", body, StringComparison.Ordinal);   // the brief travelled
        Assert.Contains("ghost_tool", body, StringComparison.Ordinal);              // the errors travelled verbatim
    }

    [Fact]
    public void The_pack_extraction_prefers_an_override_directory()
    {
        var overrideDir = Path.Combine(_workspace, "custom-pack");
        Directory.CreateDirectory(overrideDir);
        File.WriteAllText(Path.Combine(overrideDir, ForgePack.AssistantFileName), "// custom");

        var packPath = ForgePack.Ensure(_session.Directory, overrideDir);

        Assert.Equal("// custom", File.ReadAllText(packPath));

        // Without the override, the embedded pack comes back.
        var restored = ForgePack.Ensure(_session.Directory);
        Assert.Contains("orkeon-script", File.ReadAllText(restored), StringComparison.Ordinal);
    }

    /// <summary>
    /// The owner's report, third round — «pas de tokens montant / descendant pendant la
    /// phase», with the interview visibly running.
    /// <para>
    /// Every seam between the model call and the wire had its own test, and all of them were
    /// green: the tally splits, the stage reports, the engine emits, Studio binds. None of
    /// them ran the REAL assistant against a REAL stage, which is the only place a live meter
    /// can actually go missing. This is that test — the production assistant, the embedded
    /// pack, a real <see cref="BriefStage"/> and a real writer — and <c>cost.updated</c> must
    /// be on the wire BEFORE <c>brief.ready</c>, because a meter that only reports once the
    /// waiting is over is the defect, not the fix.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_meter_reaches_the_wire_during_the_interview_and_not_only_at_its_end()
    {
        var provider = new ScriptedLlmProvider()
            .Answers("Quel problème résolvons-nous ?")
            .CallsTool("brief_submit", new
            {
                goal = "Résumer les offres du fournisseur",
                acceptance = new[] { new { id = "A1", statement = "Cite ses sources", kind = "must" } },
            })
            .Answers("C'est envoyé.");

        using var output = new StringWriter();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock());
        var stage = new BriefStage(
            Build(provider), new ScriptedUserChannel("vas-y"), initialNeed: "une veille fournisseur");

        var outcome = await stage.RunAsync(_session, events, TestContext.Current.CancellationToken);
        Assert.Equal(ForgeTrigger.BriefSubmitted, outcome.Trigger);

        var kinds = Lines(output).Select(line => line.GetProperty("kind").GetString()!).ToList();
        var first = kinds.IndexOf("cost.updated");
        Assert.True(first >= 0, "the interview never metered anything: " + string.Join(", ", kinds));
        Assert.True(
            first < kinds.IndexOf("brief.ready"),
            "the meter only reported once the brief was in: " + string.Join(", ", kinds));

        // And the two directions travel apart, which is what the screen shows. Not on the
        // FIRST line: that one is a reply still being written, whose only knowable half is
        // the descending estimate — the ascending count exists once the call comes back.
        var costs = Lines(output)
            .Where(line => line.GetProperty("kind").GetString() == "cost.updated")
            .ToList();
        Assert.Contains(
            costs,
            line => line.GetProperty("promptTokens").GetInt64() > 0
                && line.GetProperty("completionTokens").GetInt64() > 0);
        Assert.All(costs, line => Assert.True(line.GetProperty("tokens").GetInt64()
            == line.GetProperty("promptTokens").GetInt64() + line.GetProperty("completionTokens").GetInt64(),
            DumpCosts(output)));
    }

    /// <summary>
    /// The finest granularity of the meter: a fragment of a reply that is still being
    /// written already moves the figure. The in-flight part is an approximation, so the line
    /// carries <c>estimatedTokens</c> — that is the «≈» the screen shows until the
    /// provider's own count lands and replaces it.
    /// </summary>
    [Fact]
    public async Task A_reply_still_being_written_already_moves_the_meter()
    {
        var provider = new ScriptedLlmProvider()
            .Answers("Je regarde votre dossier avant de vous répondre, un instant.")
            .CallsTool("brief_submit", new
            {
                goal = "Résumer les offres du fournisseur",
                acceptance = new[] { new { id = "A1", statement = "Cite ses sources", kind = "must" } },
            })
            .Answers("C'est envoyé.");

        using var output = new StringWriter();
        var stage = new BriefStage(
            Build(provider), new ScriptedUserChannel("vas-y"), initialNeed: "une veille fournisseur");

        await stage.RunAsync(
            _session, new ForgeEventWriter(output, new FakeOrkeonClock()), TestContext.Current.CancellationToken);

        var costs = Lines(output)
            .Where(line => line.GetProperty("kind").GetString() == "cost.updated")
            .ToList();

        Assert.NotEmpty(costs);
        Assert.Contains(costs, line => line.GetProperty("estimatedTokens").GetInt64() > 0);
    }

    private static string DumpCosts(StringWriter output) => string.Join(
        "\n",
        Lines(output).Where(l => l.GetProperty("kind").GetString() == "cost.updated").Select(l => l.GetRawText()));

    private static IReadOnlyList<JsonElement> Lines(StringWriter output) =>
    [
        .. output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement),
    ];
}
