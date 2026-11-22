using System.Text.Json;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>orkeon forge rephrase</c>: the verb parses with the request after it and
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
            .Select(l => JsonElement.Parse(l)).ToList();
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

    /// <summary>#453: with no LLM configured the verb refuses on the stream, and its error says the process ends.</summary>
    [Fact]
    [Trait("BR", "CH-466")]
    public async Task Without_an_llm_the_refusal_on_the_stream_is_terminal()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "orkeon-forge-rephrase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var settings = Path.Combine(workspace, "settings.json");
            await File.WriteAllTextAsync(settings, "{}", TestContext.Current.CancellationToken);
            using var console = new TestConsole();

            var exitCode = await ForgeCommand.DispatchAsync(console, ["rephrase", "un besoin", "--events", "jsonl", "--settings", settings], workspace);

            Assert.Equal(1, exitCode);
            var lines = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var error = JsonElement.Parse(Assert.Single(lines, l => l.Contains("\"kind\":\"error\"", StringComparison.Ordinal)));
            Assert.Contains("\"kind\":\"error\"", lines[^1], StringComparison.Ordinal);
            Assert.Equal(ForgeRephrase.LlmUnavailableCode, error.GetProperty("code").GetString());
            Assert.Equal(NoLlmMessage, error.GetProperty("message").GetString());
            Assert.False(error.GetProperty("recoverable").GetBoolean());
            Assert.True(error.GetProperty("terminal").GetBoolean());
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private const string NoLlmMessage = "no LLM is configured — run `orkeon init`, or pass --settings.";

    /// <summary>#466: through the real dispatch, settings wiring no LLM and no --events: one stderr line, exit 1.</summary>
    [Fact]
    [Trait("BR", "CH-466")]
    public async Task Without_an_llm_through_the_dispatch_the_terminal_refusal_is_one_error_line()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "orkeon-forge-rephrase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var settings = Path.Combine(workspace, "settings.json");
            await File.WriteAllTextAsync(settings, "{}", TestContext.Current.CancellationToken);
            using var console = new TestConsole();

            var exitCode = await ForgeCommand.DispatchAsync(console, ["rephrase", "un besoin", "--settings", settings], workspace);

            // The host's own warning on the missing Llm section comes first; the refusal is the last line.
            Assert.Equal(1, exitCode);
            var errors = console.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal("orkeon forge: " + NoLlmMessage, errors[^1]);
            Assert.Single(errors, l => l.StartsWith("orkeon forge:", StringComparison.Ordinal));
            Assert.Empty(console.Stdout);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>#466: an answer with nothing usable in it ends the verb on the stream with a terminal error.</summary>
    [Theory]
    [Trait("BR", "CH-466")]
    [InlineData("   ")]
    [InlineData("```\n```")]
    public async Task An_empty_answer_on_the_stream_is_a_single_terminal_error(string answer)
    {
        using var console = new TestConsole();

        var exitCode = await ForgeRephrase.AnswerAsync(
            console, Rephrase("un besoin", "--events", "jsonl"), new ForgeEventWriter(console.Output), new ScriptedLlm(answer));

        Assert.Equal(1, exitCode);
        var lines = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var error = JsonElement.Parse(Assert.Single(lines, l => l.Contains("\"kind\":\"error\"", StringComparison.Ordinal)));
        Assert.Contains("\"kind\":\"error\"", lines[^1], StringComparison.Ordinal);
        Assert.Equal(ForgeRephrase.EmptyCode, error.GetProperty("code").GetString());
        Assert.Equal("the model gave no rewritten request.", error.GetProperty("message").GetString());
        Assert.False(error.GetProperty("recoverable").GetBoolean());
        Assert.True(error.GetProperty("terminal").GetBoolean());
        Assert.DoesNotContain(lines, l => l.Contains(ForgeRephrase.EventKind, StringComparison.Ordinal));
        Assert.Empty(console.Stderr);
    }

    /// <summary>#466: in the terminal the empty answer is one line on the error output.</summary>
    [Fact]
    [Trait("BR", "CH-466")]
    public async Task An_empty_answer_in_the_terminal_is_one_error_line()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeRephrase.AnswerAsync(console, Rephrase("un besoin"), events: null, new ScriptedLlm("   "));

        Assert.Equal(1, exitCode);
        Assert.Equal("orkeon forge: the model gave no rewritten request.", console.Stderr.Trim());
        Assert.Empty(console.Stdout);
    }

    /// <summary>#466: with no LLM configured, in the terminal, the refusal is one line on the error output.</summary>
    [Fact]
    [Trait("BR", "CH-466")]
    public async Task Without_an_llm_in_the_terminal_the_refusal_is_one_error_line()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeRephrase.AnswerAsync(console, Rephrase("un besoin"), events: null, provider: null);

        Assert.Equal(1, exitCode);
        Assert.Equal("orkeon forge: no LLM is configured — run `orkeon init`, or pass --settings.", console.Stderr.Trim());
        Assert.Empty(console.Stdout);
    }

    /// <summary>#466: a usable answer is the rewritten request, on the stream and in the terminal; exit 0.</summary>
    [Fact]
    [Trait("BR", "CH-466")]
    public async Task A_usable_answer_is_written_and_the_verb_exits_zero()
    {
        using var terminal = new TestConsole();
        Assert.Equal(0, await ForgeRephrase.AnswerAsync(terminal, Rephrase("un besoin"), events: null, new ScriptedLlm("Le besoin, clair.")));
        Assert.Equal("Le besoin, clair.", terminal.Stdout.Trim());

        using var stream = new TestConsole();
        Assert.Equal(0, await ForgeRephrase.AnswerAsync(
            stream, Rephrase("un besoin", "--events", "jsonl"), new ForgeEventWriter(stream.Output), new ScriptedLlm("Le besoin, clair.")));
        var rephrased = JsonElement.Parse(Assert.Single(stream.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            l => l.Contains(ForgeRephrase.EventKind, StringComparison.Ordinal)));
        Assert.Equal("Le besoin, clair.", rephrased.GetProperty("text").GetString());
        Assert.Equal("un besoin", rephrased.GetProperty("original").GetString());
    }

    private static ForgeCommandOptions Rephrase(params string[] rest)
    {
        var options = ForgeCommandOptions.Parse(["rephrase", .. rest]);
        Assert.Null(options.Error);
        return options;
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
