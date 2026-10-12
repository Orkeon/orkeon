using System.Text.Json;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>orkeon forge rephrase</c> (STUDIO-57): the verb parses with the request after it and
/// refuses every cycle option; one call to the LLM rewrites the request and the answer comes
/// back cleaned of what a model wraps it in.
/// </summary>
public sealed class ForgeRephraseTests
{
    private sealed class ScriptedLlm(string answer) : ILlmProvider
    {
        public string Name => "scripted";

        public LlmMessage[]? Prompt { get; private set; }

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse { Content = answer });

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Prompt = messages;
            return Task.FromResult(new LlmResponse { Content = answer });
        }
    }

    [Fact]
    public void The_verb_takes_the_request_after_it_and_events_and_settings_only()
    {
        var options = ForgeCommandOptions.Parse(["rephrase", "une", "équipe", "qui", "envoie", "des", "mails", "--events", "jsonl", "--settings", "/s.json"]);

        Assert.Null(options.Error);
        Assert.True(options.Rephrase);
        Assert.Equal("une équipe qui envoie des mails", options.Need);
        Assert.True(options.Events);
        Assert.Equal("/s.json", options.SettingsPath);

        Assert.Equal(
            "rephrase needs the request to rewrite, typed after the verb.",
            ForgeCommandOptions.Parse(["rephrase", "--events", "jsonl"]).Error);
        Assert.StartsWith(
            "rephrase takes the request, --events and --settings only",
            ForgeCommandOptions.Parse(["rephrase", "un", "besoin", "--dry"]).Error,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "rephrase takes the request, --events and --settings only",
            ForgeCommandOptions.Parse(["rephrase", "un", "besoin", "--read", "/docs"]).Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_is_asked_once_with_the_request_and_its_answer_comes_back_cleaned()
    {
        var llm = new ScriptedLlm("```text\n« L'équipe reçoit deux dossiers de PDF et envoie les mails. »\n```");

        var rewritten = await ForgeRephrase.RewriteAsync(llm, "  deux dossiers de pdf, envoyer des mails  ", TestContext.Current.CancellationToken);

        Assert.Equal("L'équipe reçoit deux dossiers de PDF et envoie les mails.", rewritten);
        Assert.NotNull(llm.Prompt);
        Assert.Equal(["system", "user"], llm.Prompt.Select(m => m.Role));
        Assert.Equal(ForgeRephrase.SystemPrompt, llm.Prompt[0].Content);
        Assert.Equal("deux dossiers de pdf, envoyer des mails", llm.Prompt[1].Content);
    }

    [Fact]
    public async Task An_empty_answer_is_null_and_the_cleaning_leaves_an_ordinary_answer_alone()
    {
        Assert.Null(await ForgeRephrase.RewriteAsync(new ScriptedLlm("   "), "x", TestContext.Current.CancellationToken));
        Assert.Null(ForgeRephrase.Clean("```\n```"));
        Assert.Equal("Une phrase \"citée\" au milieu.", ForgeRephrase.Clean("Une phrase \"citée\" au milieu.\n"));
        Assert.Equal("Tout entre guillemets.", ForgeRephrase.Clean("\"Tout entre guillemets.\""));
    }

    [Fact]
    [Trait("BR", "BR-09")]
    public async Task ShouldWriteCallAndCostLines_WhenRephrasingWithEvents()
    {
        var llm = new CountingLlm("Un brief clair.");
        var output = new StringWriter();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock());

        var rewritten = await ForgeRephrase.RewriteAsync(llm, "un besoin flou", events, TestContext.Current.CancellationToken);

        Assert.Equal("Un brief clair.", rewritten);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
        var kinds = lines.Select(l => l.GetProperty("kind").GetString()).ToList();
        var started = kinds.IndexOf("llm.call.started");
        var finished = kinds.IndexOf("llm.call.finished");
        var cost = kinds.IndexOf("cost.updated");
        Assert.True(started >= 0 && finished > started && cost > finished, string.Join(",", kinds));
        Assert.Equal(
            lines[started].GetProperty("callId").GetString(),
            lines[finished].GetProperty("callId").GetString());
        Assert.Equal(12, lines[finished].GetProperty("promptTokens").GetInt32());
        Assert.Equal(5, lines[finished].GetProperty("completionTokens").GetInt32());
        Assert.Equal(17, lines[cost].GetProperty("tokens").GetInt64());
    }

    private sealed class CountingLlm(string answer) : ILlmProvider
    {
        public string Name => "counting";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            ChatAsync([], config, cancellationToken);

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LlmResponse { Content = answer, PromptTokens = 12, CompletionTokens = 5, TokensUsed = 17 });
    }

    [Fact]
    public void The_event_line_carries_the_text_and_the_original()
    {
        var output = new StringWriter();
        new ForgeEventWriter(output, new FakeOrkeonClock()).Emit(ForgeRephrase.EventKind, new { text = "clair", original = "flou" });

        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("need.rephrased", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("clair", document.RootElement.GetProperty("text").GetString());
        Assert.Equal("flou", document.RootElement.GetProperty("original").GetString());
    }
}
