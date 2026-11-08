using System.Text.Json;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.Converters;

namespace Orkeon.Infrastructure.Tests.LLMs.Converters;

public class ContentConverterAudioTests
{
    private static MultiModalContent Spoken(string mime = "audio/wav") =>
        MultiModalContent.Empty()
            .AddText("Transcribe this")
            .AddAudio(AudioContentPart.FromBytes([0x52, 0x49, 0x46, 0x46], mime));

    [Fact]
    public void An_audio_model_gets_an_input_audio_part()
    {
        var parts = ContentConverter.ToOpenAIContentParts(Spoken(), "gpt-4o-audio-preview");

        var json = JsonSerializer.Serialize(parts);
        using var doc = JsonDocument.Parse(json);
        var audio = doc.RootElement[1];
        Assert.Equal("input_audio", audio.GetProperty("type").GetString());
        Assert.Equal("wav", audio.GetProperty("input_audio").GetProperty("format").GetString());
        Assert.Equal(Convert.ToBase64String([0x52, 0x49, 0x46, 0x46]), audio.GetProperty("input_audio").GetProperty("data").GetString());
    }

    [Fact]
    public void An_mp3_part_is_sent_as_mp3()
    {
        var parts = ContentConverter.ToOpenAIContentParts(Spoken("audio/mpeg"), "gpt-4o-audio-preview");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(parts));
        Assert.Equal("mp3", doc.RootElement[1].GetProperty("input_audio").GetProperty("format").GetString());
    }

    [Fact]
    public void A_model_without_audio_refuses_the_part_and_names_itself()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(Spoken(), "gpt-4o"));

        Assert.Contains("'gpt-4o'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("audio", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_model_the_table_does_not_know_is_refused_and_the_override_is_named()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(Spoken(), "acme-voice-2"));

        Assert.Contains("'acme-voice-2'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Llm:ModelCapabilities", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_override_lets_an_unknown_model_take_audio()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-voice"] = new() { AudioInput = true },
        });

        var parts = ContentConverter.ToOpenAIContentParts(Spoken(), "acme-voice-2", registry);

        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void An_audio_format_the_api_does_not_know_is_refused()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(Spoken("audio/ogg"), "gpt-4o-audio-preview"));

        Assert.Contains("audio/ogg", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_audio_part_given_by_address_is_refused()
    {
        var content = MultiModalContent.Empty().AddAudio(AudioContentPart.FromUri(new Uri("https://example.test/a.wav"), "audio/wav"));

        Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToOpenAIContentParts(content, "gpt-4o-audio-preview"));
    }

    [Fact]
    public void The_chat_client_path_refuses_audio_for_a_model_without_it_and_names_the_model()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => ContentConverter.ToAIContents(Spoken(), "gpt-4o"));

        Assert.Contains("'gpt-4o'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chat_client_path_passes_audio_for_a_model_that_takes_it()
    {
        var contents = ContentConverter.ToAIContents(Spoken(), "gpt-4o-audio-preview");

        Assert.Equal(2, contents.Count);
    }

    [Fact]
    public void Text_alone_never_asks_the_table()
    {
        var content = MultiModalContent.Empty().AddText("hello");

        Assert.Single(ContentConverter.ToAIContents(content, "acme-voice-2"));
    }
}
