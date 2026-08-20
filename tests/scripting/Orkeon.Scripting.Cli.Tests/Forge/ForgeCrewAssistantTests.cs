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

/// <summary>Scripted <see cref="ILlmProvider"/>: a queue of canned responses, calls recorded.</summary>
internal sealed class ScriptedLlmProvider : ILlmProvider
{
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
            TestContext.Current.CancellationToken);

        Assert.Equal("Bonjour ! Quel problème résolvons-nous ?", reply.Message);
        Assert.Null(reply.BriefJson);
        Assert.True(reply.TokensConsumed > 0);

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
}
