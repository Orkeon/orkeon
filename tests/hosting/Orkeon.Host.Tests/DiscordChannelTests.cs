using Orkeon.Host.Gateway;

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
}
