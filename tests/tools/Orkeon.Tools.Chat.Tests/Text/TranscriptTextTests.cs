using Orkeon.Tools.Chat.Text;

namespace Orkeon.Tools.Chat.Tests.Text;

/// <summary>WebVTT as an agent reads it.</summary>
public sealed class TranscriptTextTests
{
    [Fact]
    public void Should_write_one_line_per_speech() =>
        Assert.Equal(
            ["[00:00:16] Ada Lovelace: Hello there & welcome.", "[00:00:20] Bob: Hi."],
            TranscriptText.FromVtt(
                "WEBVTT\r\n\r\n00:00:16.246 --> 00:00:17.726\r\n<v Ada Lovelace>Hello <i>there</i></v>\r\n\r\n" +
                "id-2\r\n00:00:18.000 --> 00:00:19.000\r\n<v Ada Lovelace>&amp; welcome.</v>\r\n\r\n00:00:20.000 --> 00:00:21.000\r\n<v Bob>Hi.</v>\r\n").Lines);

    [Theory]
    [InlineData("-00:00:01.000 --> 00:00:02.000\n<v A>x</v>", "[-00:00:01] A: x")]
    [InlineData("01:02.000 --> 01:03.000\nx", "[00:01:02] x")]
    [InlineData("00:00:01.000 --> 00:00:02.000\n<v A: B>x</v>", "[00:00:01] A  B: x")]
    [InlineData("\n\nid-1\n00:00:01.000 --> 00:00:02.000\n<v A>x</v>", "[00:00:01] A: x")]
    [InlineData("00:00:01.000 --> 00:00:02.000\n<v.loud A>2 < 3 <b", "[00:00:01] A: 2 < 3 <b")]
    [InlineData("00:00:01.000 --> 00:00:02.000\n<v A>one\ntwo</v>", "[00:00:01] A: one two")]
    public void Should_read_a_cue(string cue, string line) => Assert.Equal([line], TranscriptText.FromVtt("WEBVTT\n\n" + cue).Lines);

    [Fact]
    public void Should_drop_what_is_not_a_cue() =>
        Assert.Empty(TranscriptText.FromVtt("WEBVTT\nKind: captions\n\nNOTE ignore me\n\nSTYLE\n::cue {}\n\n00:00:01.000 --> 00:00:02.000\n<v A></v>").Lines);

    [Fact]
    public void Should_tell_two_voices_of_a_cue_apart() =>
        Assert.Equal(
            ["[00:00:01] A: x", "[00:00:01] B: y"],
            TranscriptText.FromVtt("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<v A>x</v> <v B>y</v>").Lines);

    [Fact]
    public void Should_cut_a_long_speech()
    {
        var lines = TranscriptText.FromVtt($"WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<v A>{new string('x', 1200)}</v>").Lines;

        Assert.Equal([500, 500, 200], lines.Select(line => line["[00:00:01] A: ".Length..].Count(c => c == 'x')));
        Assert.All(lines, line => Assert.StartsWith("[00:00:01] A: ", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Should_keep_the_lines_of_a_plain_text() => Assert.Equal(["a", "b"], TranscriptText.FromPlain("a\r\n\r\n b\u202e ").Lines);

    [Fact]
    public void Should_tell_what_is_said_without_the_names_between() =>
        Assert.Equal("a b", TranscriptText.FromVtt("WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n<v A>a</v>\n\n00:00:02.000 --> 00:00:03.000\n<v B>b</v>").Words);

    [Fact]
    public void Should_keep_a_text_of_unclosed_tags()
    {
        var hostile = string.Concat(Enumerable.Repeat("<v ", 500_000));

        Assert.Equal(hostile.Trim().Length, TranscriptText.FromVtt($"WEBVTT\n\n00:00:01.000 --> 00:00:02.000\n{hostile}").Words.Length);
    }
}
