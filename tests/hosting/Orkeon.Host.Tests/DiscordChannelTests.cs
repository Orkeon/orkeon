using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Host.Gateway;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// GATE-04: what the Discord adapter can be held to without a token, a server or a network —
/// the translation, the two platform limits it has to respect, and the rule that keeps a bot
/// from answering strangers.
/// <para>
/// The rest is the specification's own acceptance criterion, and it is honest that it cannot be
/// automated here: launching a crew from a real thread, watching it progress and stopping it by
/// button needs a Discord account, which is an owner action.
/// </para>
/// </summary>
public class DiscordChannelTests
{
    [Fact]
    public void The_token_is_named_in_configuration_and_read_from_the_environment()
    {
        // The value never touches a configuration file, a commit or an image layer. A bot token
        // can read every message a server sends; it does not get an exception to that rule.
        var options = new DiscordChannelOptions { TokenEnvironmentVariable = "ORKEON_TEST_DISCORD_TOKEN" };

        Assert.Null(options.ReadToken());

        try
        {
            Environment.SetEnvironmentVariable("ORKEON_TEST_DISCORD_TOKEN", "not-a-real-token");
            Assert.Equal("not-a-real-token", options.ReadToken());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORKEON_TEST_DISCORD_TOKEN", null);
        }
    }

    [Fact]
    public void An_empty_token_variable_reads_as_absent()
    {
        // An empty variable is a variable someone meant to set. Treating it as a token would
        // fail at login with an unhelpful message instead of naming what is missing.
        var options = new DiscordChannelOptions { TokenEnvironmentVariable = "ORKEON_TEST_EMPTY_TOKEN" };

        try
        {
            Environment.SetEnvironmentVariable("ORKEON_TEST_EMPTY_TOKEN", "");
            Assert.Null(options.ReadToken());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ORKEON_TEST_EMPTY_TOKEN", null);
        }
    }

    [Fact]
    public void The_channel_is_off_and_closed_by_default()
    {
        var options = new DiscordChannelOptions();

        Assert.False(options.Enabled);
        Assert.Empty(options.AllowedUserIds);
        Assert.True(new AllowListChatAuthorizer(options.AllowedUserIds).IsEmpty);
    }

    [Fact]
    public void An_answer_longer_than_discord_accepts_is_cut_rather_than_lost()
    {
        // Discord refuses anything over 2000 characters. A crew's answer regularly exceeds it,
        // and losing the tail beats losing the whole thing.
        var long_answer = new string('x', 5000);

        var sent = DiscordResponder.Truncate(long_answer);

        Assert.True(sent.Length <= 2000);
        Assert.EndsWith("(truncated)", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_answer_is_sent_as_written()
    {
        Assert.Equal("here it is", DiscordResponder.Truncate("here it is"));
    }

    [Fact]
    public void An_empty_answer_says_so_instead_of_failing_to_send()
    {
        // Discord rejects an empty message body, so a run that produced nothing would end in a
        // send failure and total silence — the worst possible reading of "it finished".
        Assert.Equal("(no output)", DiscordResponder.Truncate(""));
    }

    [Fact]
    public void The_stop_button_carries_the_id_the_handler_reads_back()
    {
        // A mismatch here is a button that does nothing and reports nothing, which is the one
        // failure mode a user cannot distinguish from a hung run.
        var component = DiscordChannel.StopButton();

        Assert.Contains(
            component.Components.OfType<Discord.ActionRowComponent>().SelectMany(row => row.Components),
            item => item is Discord.ButtonComponent button && button.CustomId == DiscordChannel.StopButtonId);
    }

    [Fact]
    public void The_registered_commands_are_the_ones_the_handler_reads_back()
    {
        // Same argument as the button: a name registered under one spelling and read back
        // under another is a command that does nothing. The builder enforces Discord's own
        // limits (lowercase name, description length) at Build() time, so building here is
        // also the test that both commands are registrable at all.
        var commands = DiscordChannel.SlashCommands();

        Assert.Equal(2, commands.Length);
        Assert.Contains(commands, c => c.Name.Value == DiscordChannel.StatusCommandName);
        Assert.Contains(commands, c => c.Name.Value == DiscordChannel.StopCommandName);
        Assert.All(commands, c => Assert.False(string.IsNullOrWhiteSpace(c.Description.Value)));
    }

    [Fact]
    public void An_interaction_maps_to_an_invocation_on_the_thread_it_was_used_in()
    {
        // ChannelId is the thread's id — the same conversation identity the message and
        // button paths use, so /status finds the same run a message started.
        var invocation = DiscordChannel.ToInvocation("status", channelId: 123456789UL, userId: 42UL);

        Assert.NotNull(invocation);
        Assert.Equal("status", invocation!.CommandName);
        Assert.Equal("123456789", invocation.ConversationId);
        Assert.Equal("42", invocation.SenderId);
    }

    [Fact]
    public void An_interaction_with_no_channel_has_no_conversation_and_is_refused()
    {
        // A null channel id has nothing to act on; guessing a conversation would stop
        // somebody else's run.
        Assert.Null(DiscordChannel.ToInvocation("stop", channelId: null, userId: 42UL));
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        // An emoji astride the cut would leave a lone surrogate — invalid UTF-16 that Discord
        // rejects outright, turning "too long" into "not sent at all".
        var ellipsis = "\n…(truncated)";
        var cut = 2000 - ellipsis.Length;
        var text = new string('a', cut - 1) + "🚀" + new string('b', 100);

        var truncated = DiscordResponder.Truncate(text);

        Assert.True(truncated.Length <= 2000);
        Assert.DoesNotContain('\uD83D'.ToString(), truncated[^ellipsis.Length..], StringComparison.Ordinal);
        foreach (var (ch, i) in truncated.Select((c, i) => (c, i)))
        {
            if (char.IsHighSurrogate(ch))
                Assert.True(i + 1 < truncated.Length && char.IsLowSurrogate(truncated[i + 1]), "lone high surrogate");
        }
    }

    [Fact]
    public void Whitespace_only_output_reads_as_no_output()
    {
        // Discord refuses a blank body: the send would fail and the user's only reading of
        // "it finished" would be total silence — the worst possible one.
        Assert.Equal("(no output)", DiscordResponder.Truncate("   \n\t  "));
    }

    // ---- The channel without a connection: built, refused, disposed ---------------------

    private static DiscordChannelOptions UnsetToken() => new()
    {
        Enabled = true,
        AllowedUserIds = ["trusted"],
        TokenEnvironmentVariable = $"ORKEON_TEST_DISCORD_TOKEN_{Guid.NewGuid():N}",
    };

    [Fact]
    public async Task A_channel_is_built_without_connecting_and_throttles_its_replies()
    {
        await using var channel = new DiscordChannel(Options.Create(UnsetToken()), NullLogger<DiscordChannel>.Instance);

        Assert.Equal("discord", channel.Name);
        Assert.IsType<ThrottledResponder>(channel.Responder);
    }

    [Fact]
    public void A_channel_needs_its_options_and_a_logger()
    {
        Assert.Throws<ArgumentNullException>(
            () => new DiscordChannel(null!, NullLogger<DiscordChannel>.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new DiscordChannel(Options.Create(UnsetToken()), null!));
    }

    [Fact]
    public async Task An_unset_token_refuses_the_run_and_names_the_variable()
    {
        // Thrown, not returned: a silent return left the daemon up, deaf, and reported healthy.
        var options = UnsetToken();
        using var provider = new RecordingLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder.AddProvider(provider));
        await using var channel = new DiscordChannel(Options.Create(options), loggers.CreateLogger<DiscordChannel>());

        var ex = await Assert.ThrowsAsync<HostConfigurationException>(
            () => channel.RunAsync((_, _) => Task.CompletedTask, TestContext.Current.CancellationToken));

        Assert.Contains(options.TokenEnvironmentVariable, ex.Message, StringComparison.Ordinal);
        Assert.True(provider.Logger.HasEntry(e =>
            e.Level == LogLevel.Error && e.Message.Contains(options.TokenEnvironmentVariable, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_run_needs_a_message_handler()
    {
        await using var channel = new DiscordChannel(Options.Create(UnsetToken()), NullLogger<DiscordChannel>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => channel.RunAsync(null!, TestContext.Current.CancellationToken));
    }

    // ---- The responder: a conversation id that is no Discord channel ---------------------

    private static InboundMessage Message(string conversation) => new()
    {
        Channel = "discord",
        ConversationId = conversation,
        SenderId = "trusted",
        Text = "hello",
        ReceivedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task A_reply_to_a_conversation_that_is_not_a_channel_id_is_logged_not_thrown()
    {
        // The acknowledgement is awaited by the runner: a throw here would fail a run over a greeting.
        await using var client = new DiscordSocketClient();
        var logger = new RecordingLogger();
        var responder = new DiscordResponder(client, logger);
        var ct = TestContext.Current.CancellationToken;

        await responder.AcknowledgeAsync(Message("not-a-channel"), "on it", ct);
        await responder.ProgressAsync(Message("not-a-channel"), "working", ct);
        await responder.CompleteAsync(Message("not-a-channel"), "done", ct);

        Assert.Equal(3, logger.Entries.Count(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("not-a-channel", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_responder_needs_a_client_a_logger_and_a_message()
    {
        await using var client = new DiscordSocketClient();
        var responder = new DiscordResponder(client, new RecordingLogger());

        Assert.Throws<ArgumentNullException>(() => new DiscordResponder(null!, new RecordingLogger()));
        Assert.Throws<ArgumentNullException>(() => new DiscordResponder(client, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => responder.CompleteAsync(null!, "done", TestContext.Current.CancellationToken));
    }
}
